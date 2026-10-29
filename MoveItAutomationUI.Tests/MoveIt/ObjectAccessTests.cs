using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Tasks;

namespace MoveItAutomationUI.Tests.MoveIt;

// Effective grants are supplied by MOVEit for the signed-in principal. Visibility,
// MAUI administrator status, and membership inferred from the ACL grid are not grants.
public sealed class ObjectAccessTests
{
    [Fact]
    public async Task EffectiveCreationGrantsUnionAcrossGroupsAndRemainObjectTypeSpecific()
    {
        using var fixture = new Fixture();
        fixture.Groups = [Group("Hosts", "xAddHost"), Group("Tasks", "xAddTask"), Group("More Tasks", "xAddTask"), new() { ["Name"] = "Visible only" }];
        var access = await fixture.Access.GetCreationAsync();
        Assert.False(access.IsAdmin);
        Assert.Equal(new[] { "More Tasks", "Tasks" }, access.Groups["Task"]);
        Assert.Equal(new[] { "Hosts" }, access.Groups["Host"]);
        Assert.False(access.CanCreate("Script"));
        Assert.Equal("Tasks", await fixture.Access.RequireCreationAsync("Task", " tasks "));
        await Assert.ThrowsAsync<MoveItObjectAccessException>(() => fixture.Access.RequireCreationAsync("Task", null));
        await Assert.ThrowsAsync<MoveItObjectAccessException>(() => fixture.Access.RequireCreationAsync("Task", "Hosts"));
        await Assert.ThrowsAsync<MoveItObjectAccessException>(() => fixture.Access.RequireCreationAsync("PGP", "Tasks"));
        Assert.All(fixture.Calls, name => Assert.Equal(nameof(IMoveItRestClient.GetResourceGroupDefinitionsAsync), name));
    }

    [Fact]
    public async Task MoveItAdministratorDoesNotRequireAResourceGroupOrInventoryRead()
    {
        using var fixture = new Fixture(); fixture.State.Info = new MoveItInfo { IsAdmin = true };
        Assert.True((await fixture.Access.GetCreationAsync()).CanCreate("Task"));
        Assert.Equal("", await fixture.Access.RequireCreationAsync("Host", null));
        Assert.True((await fixture.Access.GetObjectAsync("Script", "100001")).CanEdit);
        Assert.Empty(fixture.Calls);
    }

    [Fact]
    public async Task EditExistingAllowsChangingEveryTaskElementWithoutGrantingCreateOrDelete()
    {
        using var fixture = new Fixture();
        fixture.Object = JsonNode.Parse("""{"ID":"921","TT":"","Name":"Existing","xAlter":true,"xAddEditDelete":false,"xRun":true,"CacheNames":"random","steps":[]} """)!.AsObject();
        var rights = await fixture.Access.GetObjectAsync("Task", "921");
        Assert.True(rights.CanEdit); Assert.True(rights.CanRun); Assert.False(rights.CanDelete);
        Assert.False((await fixture.Access.GetCreationAsync()).CanCreate("Task"));
        await Assert.ThrowsAsync<MoveItObjectAccessException>(() => fixture.Access.RequireAsync("Task", "921", "Delete"));
        var persistence = new TaskJsonPersistenceService(fixture.Rest, access: fixture.Access);
        await persistence.UpdateFreshAsync("921", MoveItTaskKind.Traditional, task =>
        {
            task["steps"] = JsonNode.Parse("""[{"Source":{"ID":"11","HostID":"17","FileMask":"*.*"}},{"Destination":{"ID":"12","HostID":"18"}}]""");
            task["Schedules"] = JsonNode.Parse("""{"Schedule":[{"OnlyUntilFirstSuccess":0}]}""");
            task["Parameters"] = JsonNode.Parse("""{"Parameter":[{"Name":"Test","value":"value"}]}""");
            task["NextActions"] = JsonNode.Parse("""{"NextAction":[{"Type":"Email","HostID":"19"}]}""");
            return task;
        });
        Assert.Equal(2, fixture.Saved!["steps"]!.AsArray().Count);
        Assert.NotNull(fixture.Saved["Schedules"]); Assert.NotNull(fixture.Saved["Parameters"]); Assert.NotNull(fixture.Saved["NextActions"]);
        Assert.Contains(nameof(IMoveItRestClient.ReleaseTaskCheckoutV0Async), fixture.Calls);
    }

    [Theory]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, true)]
    [InlineData(false, false, false, false)]
    public async Task NativePerObjectFlagsSeparateEditDeleteAndRun(bool alter, bool full, bool edit, bool delete)
    {
        using var fixture = new Fixture();
        fixture.Object = new() { ["ID"] = "17", ["xAlter"] = alter, ["xAddEditDelete"] = full, ["xRun"] = false };
        var rights = await fixture.Access.GetObjectAsync("Task", "17");
        Assert.Equal(edit, rights.CanEdit); Assert.Equal(delete, rights.CanDelete); Assert.False(rights.CanRun);
    }

    [Fact]
    public async Task GlobalParameterManagementRequiresNativeAdminEvenWithObjectWriteFlags()
    {
        using var fixture = new Fixture();
        fixture.Object = new() { ["Name"] = "Visible Parameter", ["xAddEditDelete"] = true };
        var rights = await fixture.Access.GetObjectAsync("GlobalParameter", "Visible Parameter");
        Assert.False(rights.CanEdit); Assert.False(rights.CanDelete);
        Assert.Empty(fixture.Calls); // Denied management never reads the parameter's sensitive details.
        fixture.State.Info = new MoveItInfo { IsAdmin = true };
        Assert.True((await fixture.NewAccess().GetObjectAsync("GlobalParameter", "Visible Parameter")).CanEdit);
    }

    [Fact]
    public async Task MissingFlagsRequireExplicitAssignmentToAnEligibleGroup()
    {
        using var fixture = new Fixture();
        fixture.Groups = [Group("Writable", "xAddHost"), new() { ["Name"] = "Visible" }];
        fixture.Object = new() { ["ID"] = "17", ["Group"] = new JsonArray("Visible") };
        Assert.False((await fixture.Access.GetObjectAsync("Host", "17")).CanEdit);
        fixture.Object["Group"] = new JsonArray("Writable");
        // A new request rechecks per-object grants rather than reusing a previous object's result.
        Assert.True((await fixture.NewAccess().GetObjectAsync("Host", "17")).CanEdit);
        fixture.Object["xAddEditDelete"] = false;
        Assert.False((await fixture.NewAccess().GetObjectAsync("Host", "17")).CanEdit);
    }

    [Fact]
    public async Task CreationCacheIsUserScopedAndFreshChecksObserveRevokedGrants()
    {
        using var fixture = new Fixture(); fixture.Groups = [Group("Writable", "xAddTask")];
        Assert.True((await fixture.Access.GetCreationAsync()).CanCreate("Task"));
        fixture.Groups = [];
        fixture.State.Username = "another-user";
        Assert.False((await fixture.NewAccess().GetCreationAsync()).CanCreate("Task"));
        fixture.State.Username = "user";
        Assert.True((await fixture.NewAccess().GetCreationAsync()).CanCreate("Task")); // UI cache only.
        Assert.False((await fixture.NewAccess().GetCreationAsync(true)).CanCreate("Task"));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task WrappedHostFlagsPreserveExplicitDenials(bool wrapperGrant)
    {
        using var fixture = new Fixture(); fixture.Groups = [Group("Writable", "xAddHost")];
        fixture.Object = new() { ["xAddEditDelete"] = wrapperGrant,
            ["FTP"] = new JsonObject { ["ID"] = "17", ["xAddEditDelete"] = false, ["Group"] = new JsonArray("Writable") } };
        var rights = await fixture.Access.GetObjectAsync("Host", "17");
        Assert.False(rights.CanEdit); Assert.False(rights.CanDelete);
        Assert.DoesNotContain(nameof(IMoveItRestClient.GetResourceGroupDefinitionsAsync), fixture.Calls);
    }

    [Theory]
    [InlineData("Task")] [InlineData("Host")] [InlineData("Script")] [InlineData("DateList")]
    public async Task NativeUndoRejectsReadOnlyRequestsBeforeAnySnapshot(string type)
    {
        using var fixture = new NativeUndoFixture();
        await Assert.ThrowsAsync<MoveItObjectAccessException>(() => fixture.Service.InitializeAsync("launch", type, "42", "user"));
        Assert.Equal(new[] { type + "|42|Edit" }, fixture.PermissionChecks);
        Assert.Equal(0, fixture.NativeCalls);
    }

    [Theory]
    [InlineData("Task")] [InlineData("Host")] [InlineData("Script")] [InlineData("DateList")]
    public async Task NativeUndoRechecksRevokedRightsWithoutConsumingTheRestore(string type)
    {
        using var fixture = new NativeUndoFixture { CanEdit = true };
        Assert.False((await fixture.Service.InitializeAsync("launch", type, "42", "user")).UndoAvailable);
        fixture.Xml = "changed";
        Assert.True((await fixture.Service.AdvanceAsync("launch", type, "42", "user")).UndoAvailable);
        var calls = fixture.NativeCalls;
        fixture.CanEdit = false;
        await Assert.ThrowsAsync<MoveItObjectAccessException>(() => fixture.Service.AdvanceAsync("launch", type, "42", "user"));
        await Assert.ThrowsAsync<MoveItObjectAccessException>(() => fixture.Service.RestoreAsync("launch", type, "42", "user"));
        if (type == "Task")
            await Assert.ThrowsAsync<MoveItObjectAccessException>(() => fixture.Service.RebindAsync("launch", type, "42", "43", "user"));
        Assert.Equal(calls, fixture.NativeCalls); Assert.Equal("changed", fixture.Xml);
        fixture.CanEdit = true;
        _ = await fixture.Service.RestoreAsync("launch", type, "42", "user");
        Assert.Equal("original", fixture.Xml);
        Assert.False((await fixture.Service.AdvanceAsync("launch", type, "42", "user")).UndoAvailable);
    }

    [Fact]
    public async Task NativeUndoRebindRequiresEditAccessToTheNewTask()
    {
        using var fixture = new NativeUndoFixture { CanEdit = true };
        _ = await fixture.Service.InitializeAsync("launch", "Task", "42", "user");
        var calls = fixture.NativeCalls; fixture.DeniedId = "43";
        await Assert.ThrowsAsync<MoveItObjectAccessException>(() => fixture.Service.RebindAsync("launch", "Task", "42", "43", "user"));
        Assert.Equal(calls, fixture.NativeCalls);
        Assert.Equal("Task|43|Edit", fixture.PermissionChecks[^1]);
    }

    [Fact]
    public async Task BulkKeyPermissionChecksShareOneInventoryReadWithinTheRequest()
    {
        using var fixture = new Fixture();
        fixture.Object = new() { ["ID"] = "17", ["xAddEditDelete"] = true };
        var rights = await fixture.Access.GetObjectsAsync("SSH", ["17", "18"]);
        Assert.True(rights["17"].CanEdit); Assert.True(rights["18"].CanDelete);
        Assert.Equal(new[] { nameof(IMoveItRestClient.GetSshKeysAsync) }, fixture.Calls);
        fixture.Object["ID"] = "19";
        Assert.True((await fixture.Access.GetObjectAsync("SSH", "19")).CanEdit);
        Assert.Equal(2, fixture.Calls.Count); // A later create/save lookup must not reuse the old list.
    }

    [Fact]
    public async Task ImportChecksEveryGroupBeforeWritingAndDoesNotStripAssignments()
    {
        using var fixture = new Fixture(); fixture.Groups = [Group("Permitted", "xAddTask")];
        var definition = new JsonObject { ["Group"] = new JsonArray("Permitted", "Denied") };
        var before = definition.ToJsonString();
        await Assert.ThrowsAsync<MoveItObjectAccessException>(() => fixture.Access.RequireDefinitionCreationAsync("Task", definition, default));
        Assert.Equal(before, definition.ToJsonString()); Assert.Null(fixture.Saved);
        await Assert.ThrowsAsync<MoveItObjectAccessException>(() => fixture.Access.RequireXmlCreationAsync("Task", System.Xml.Linq.XElement.Parse("<Task><Group>Denied</Group></Task>"), default));
    }

    [Theory]
    [InlineData("Bulk", true, false)] [InlineData("Bulk", false, true)]
    [InlineData("Save", true, false)] [InlineData("Save", false, true)]
    public async Task EndpointFilterAuthorizesScalarAndBulkIdsBeforeInvokingAction(string action, bool deny, bool expectedCalled)
    {
        var checkedIds = new List<string>();
        var access = Stub<IMoveItObjectAccessService>((method, args) =>
        {
            Assert.Equal(nameof(IMoveItObjectAccessService.RequireAsync), method.Name);
            checkedIds.Add((string)args[1]!);
            return deny && (string)args[1]! == "2" ? Task.FromException(new MoveItObjectAccessException("Denied")) : Task.CompletedTask;
        });
        var descriptor = new ControllerActionDescriptor { MethodInfo = typeof(Endpoints).GetMethod(action)! };
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), descriptor);
        var arguments = new Dictionary<string, object?> { ["request"] = action == "Bulk" ? new BulkRequest(["1", "2"]) : new SaveRequest("2") };
        var context = new ActionExecutingContext(actionContext, [], arguments, new Endpoints());
        var called = false;
        await new MoveItObjectPermissionFilter(access).OnActionExecutionAsync(context, () =>
        {
            called = true; return Task.FromResult(new ActionExecutedContext(actionContext, [], context.Controller));
        });
        Assert.Equal(expectedCalled, called);
        Assert.Equal(action == "Bulk" ? new[] { "1", "2" } : new[] { "2" }, checkedIds);
        if (deny) Assert.Equal(403, Assert.IsType<ObjectResult>(context.Result).StatusCode);
    }

    [Fact]
    public async Task VisibleReadOnlyTaskCanBeOpenedButCannotBeSaved()
    {
        using var fixture = new Fixture();
        fixture.Object = JsonNode.Parse("""{"ID":"921","Name":"Visible","xAlter":false,"xAddEditDelete":false,"xRun":false}""")!.AsObject();
        await fixture.Access.RequireAsync("Task", "921", "Read");
        await Assert.ThrowsAsync<MoveItObjectAccessException>(() => fixture.Access.RequireAsync("Task", "921", "Edit"));
        await Assert.ThrowsAsync<MoveItObjectAccessException>(() => fixture.Access.RequireAsync("Task", "921", "Delete"));
    }

    public sealed record BulkRequest(string[] TaskIds);
    public sealed record SaveRequest(string Id);
    public sealed class Endpoints
    {
        [MoveItObjectPermission("Task", "Edit", "TaskIds")] public void Bulk() { }
        [MoveItObjectPermission("Task", "Edit", "id")] public void Save() { }
    }
    private static JsonObject Group(string name, string grant) => new() { ["Name"] = name, [grant] = 1 };
    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, HostSharedCrudTests.CrudProxy>();
        ((HostSharedCrudTests.CrudProxy)(object)proxy).Handler = handler; return proxy;
    }
    private sealed class NativeUndoFixture : IDisposable
    {
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());
        public bool CanEdit { get; set; }
        public string? DeniedId { get; set; }
        public string Xml { get; set; } = "original";
        public int NativeCalls { get; private set; }
        public List<string> PermissionChecks { get; } = [];
        public NativeEditorUndoService Service { get; }
        public NativeUndoFixture()
        {
            var access = Stub<IMoveItObjectAccessService>((method, args) =>
            {
                Assert.Equal(nameof(IMoveItObjectAccessService.RequireAsync), method.Name);
                PermissionChecks.Add(args[0] + "|" + args[1] + "|" + args[2]);
                return CanEdit && !Equals(args[1], DeniedId) ? Task.CompletedTask : Task.FromException(new MoveItObjectAccessException("Read-only"));
            });
            object? Native(MethodInfo method, object?[] args)
            {
                if (method.Name == nameof(IAutomationScriptService.GetScriptSnapshotAsync))
                    return Task.FromResult(new ScriptCacheSnapshot([], DateTimeOffset.UtcNow));
                NativeCalls++;
                if (method.Name.StartsWith("Restore", StringComparison.Ordinal)) Xml = (string)args[1]!;
                return method.ReturnType switch
                {
                    var t when t == typeof(Task<TaskConfigurationSnapshot>) => Task.FromResult(new TaskConfigurationSnapshot("42", "Object", Xml)),
                    var t when t == typeof(Task<HostConfigurationSnapshot>) => Task.FromResult(new HostConfigurationSnapshot("42", "Object", "FTP", Xml)),
                    var t when t == typeof(Task<ScriptConfigurationSnapshot>) => Task.FromResult(new ScriptConfigurationSnapshot("42", "Object", "PS1", Xml)),
                    var t when t == typeof(Task<DateListConfigurationSnapshot>) => Task.FromResult(new DateListConfigurationSnapshot("42", "Object", Xml)),
                    _ => throw new InvalidOperationException("Unexpected native operation " + method.Name)
                };
            }
            var workflow = Stub<IConfigurationUndoWorkflow>((method, _) => method.Name == nameof(IConfigurationUndoWorkflow.GetEnvironmentKey)
                ? "environment" : throw new InvalidOperationException(method.Name));
            var metadata = Stub<IMetadataRepository>((method, _) => method.Name == nameof(IMetadataRepository.RecordObjectConfigurationModifiedAsync)
                ? Task.CompletedTask : throw new InvalidOperationException(method.Name));
            Service = new(_cache, workflow, Stub<IAutomationTaskService>(Native), Stub<IAutomationHostService>(Native),
                Stub<IAutomationScriptService>(Native), Stub<IAutomationDateListService>(Native), metadata,
                NullLogger<NativeEditorUndoService>.Instance, access);
        }
        public void Dispose() => _cache.Dispose();
    }
    private sealed class Fixture : IDisposable
    {
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());
        public MoveItSessionState State { get; } = new() { ServerKey = "test", Username = "user", Info = new MoveItInfo { IsAdmin = false } };
        public IReadOnlyList<JsonObject> Groups { get; set; } = [];
        public JsonObject Object { get; set; } = new() { ["ID"] = "17" };
        public JsonObject? Saved { get; private set; }
        public List<string> Calls { get; } = [];
        public IMoveItRestClient Rest { get; }
        private readonly IMoveItSessionService _session;
        public MoveItObjectAccessService Access { get; }
        public MoveItObjectAccessService NewAccess() => new(Rest, _session, _cache);
        public Fixture()
        {
            _session = Stub<IMoveItSessionService>((_, _) => State);
            Rest = Stub<IMoveItRestClient>((method, args) =>
            {
                Calls.Add(method.Name);
                return method.Name switch
                {
                    nameof(IMoveItRestClient.GetResourceGroupDefinitionsAsync) => Task.FromResult(Groups),
                    nameof(IMoveItRestClient.GetSshKeysAsync) => Task.FromResult<IReadOnlyList<JsonObject>>([Object.DeepClone().AsObject(), new() { ["ID"] = "18", ["xAddEditDelete"] = true }]),
                    nameof(IMoveItRestClient.GetTaskAsync) or nameof(IMoveItRestClient.GetHostAsync) => Task.FromResult(Object.DeepClone().AsObject()),
                    nameof(IMoveItRestClient.CheckoutTaskV0Async) or nameof(IMoveItRestClient.ReleaseTaskCheckoutV0Async) => Task.CompletedTask,
                    nameof(IMoveItRestClient.UpdateTaskAsync) => Save((JsonObject)args[1]!),
                    _ => throw new InvalidOperationException("Unexpected permission lookup/write: " + method.Name)
                };
            });
            Access = NewAccess();
        }
        private Task Save(JsonObject value) { Saved = value.DeepClone().AsObject(); return Task.CompletedTask; }
        public void Dispose() => _cache.Dispose();
    }
}
