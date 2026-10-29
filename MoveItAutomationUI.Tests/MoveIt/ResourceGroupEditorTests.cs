using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Tasks;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class ResourceGroupEditorTests
{
    [Fact]
    public async Task RecoveryForcesTheDatasetCheckoutAndPreservesTheCompletePayload()
    {
        var f = new Fixture(); f.Group["FutureProperty"] = "preserve"; var original = f.Dataset.ToJsonString();
        await f.Service.RecoverCheckoutAsync();
        Assert.True(f.ForcedCheckouts == 1); Assert.True(f.Checkouts == 1);
        Assert.Equal(original, Assert.Single(f.Writes).ToJsonString());
    }
    [Fact]
    public async Task CopyCreatesOnlyMissingGroupNamesAndPreservesExistingData()
    {
        var f=new Fixture();f.Group["Members"]=new JsonArray(Member("task","2"),Member("future","99"));f.Group["NativeExtra"]="retained";
        var before=f.Group.ToJsonString();
        var created=await f.Service.EnsureGroupsAsync([" finance ","Ops","ops","Development"]);
        Assert.Equal(new[]{"Development","Ops"},created);Assert.Equal(before,f.Group.ToJsonString());
        Assert.True(f.Checkouts == 1);Assert.True(f.Writes.Count == 1);Assert.Equal("retained",f.Writes[0]["SectionMarker"]!.ToString());
        Assert.True(f.ForcedCheckouts == 0);
        foreach(var group in f.Groups.OfType<JsonObject>().Skip(1)){Assert.Equal("",group["Notes"]!.ToString());Assert.Empty(group["Members"]!.AsArray());}
        Assert.Empty(await f.Service.EnsureGroupsAsync(["FINANCE","development","OPS"]));
        Assert.True(f.Checkouts == 1);Assert.True(f.Writes.Count == 1);
    }
    [Fact]
    public async Task ConcurrentGroupCreationIsRecheckedUnderCheckoutWithoutOverwritingIt()
    {
        var f=new Fixture();
        f.BeforeCheckout=()=>f.Groups.Add(new JsonObject{["Name"]="OPS",["Notes"]="Created elsewhere",["Members"]=new JsonArray(Member("host","3"))});
        Assert.Empty(await f.Service.EnsureGroupsAsync(["Ops"]));
        Assert.True(f.Groups.Count == 2);Assert.Equal("Created elsewhere",f.Groups[1]!["Notes"]!.ToString());
        Assert.Equal("3",f.Groups[1]!["Members"]![0]!["value"]!.ToString());Assert.True(f.Writes.Count == 1);
    }
    [Theory]
    [InlineData("timeout")] [InlineData("empty")]
    public async Task UnconfirmedGroupCreationIsNotRetriedOrRolledBack(string mode)
    {
        var f=new Fixture{AcknowledgementMode=mode};
        await Assert.ThrowsAsync<ResourceGroupSaveUnconfirmedException>(()=>f.Service.EnsureGroupsAsync(["Ops"]));
        Assert.True(f.Checkouts == 1);Assert.True(f.Writes.Count == 1);
        Assert.Contains(f.Groups,node=>node!["Name"]!.ToString()=="Ops");
    }
    [Fact]
    public async Task InvalidProspectiveGroupNamesFailBeforeCheckout()
    {
        var f=new Fixture();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.EnsureGroupsAsync(["bad\u0001name"]));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.EnsureGroupsAsync([new string('x',255)]));
        Assert.True(f.Checkouts == 0);Assert.Empty(f.Writes);
    }
    [Fact]
    public async Task ExplicitCopyPlacementDoesNotExpandDependenciesOrChangeUnrelatedMembers()
    {
        var fixture = new Fixture();
        fixture.Group["Members"] = new JsonArray(Member("task", "2"), Member("future", "99"));
        await fixture.Service.SetObjectGroupsAsync(new("task", "1"), ["finance"]);
        var members = fixture.Writes.Last()["Groups"]!["Group"]![0]!["Members"]!.AsArray();
        Assert.Contains(members, item => item!["t"]!.ToString() == "task" && item["value"]!.ToString() == "1");
        Assert.Contains(members, item => item!["t"]!.ToString() == "future");
        Assert.DoesNotContain(members, item => item!["t"]!.ToString() is "host" or "script" or "ssh" or "pgp");
        await fixture.Service.SetObjectGroupsAsync(new("task", "1"), []);
        members = fixture.Writes.Last()["Groups"]!["Group"]![0]!["Members"]!.AsArray();
        Assert.DoesNotContain(members, item => item!["value"]!.ToString() == "1");
        Assert.Contains(members, item => item!["value"]!.ToString() == "2");
    }
    [Fact]
    public async Task DependenciesIncludeTaskHostScriptsAndKeysAndRemainUntilLastRootIsRemoved()
    {
        var fixture = new Fixture();
        var members = await fixture.Service.ResolveMembersAsync([new("task", "1"), new("task", "2")]);
        Assert.Equal(7, members.Count);
        Assert.Equal(2, members.First(item => item.MemberType == "host").RequiredBy.Count);
        Assert.Contains(members, item => item.MemberType == "script" && item.Id == "4");
        Assert.Contains(members, item => item.MemberType == "script" && item.Id == "5");
        Assert.Contains(members, item => item.MemberType == "pgp" && item.Id == "8");
        Assert.Contains(members, item => item.MemberType == "ssh" && item.Id == "6");
        members = await fixture.Service.ResolveMembersAsync([new("task", "2")]);
        Assert.Contains(members, item => item.MemberType == "host" && item.RequiredBy.Count == 1);
        Assert.Empty(await fixture.Service.ResolveMembersAsync([]));
        members = await fixture.Service.ResolveMembersAsync([new("host", "3")]);
        Assert.Equal(new[] { "host", "script", "ssh" }, members.Select(item => item.MemberType));
        Assert.All(members.Where(item => item.MemberType != "host"), item => Assert.NotEmpty(item.RequiredBy));
        Assert.Empty(fixture.Writes);
    }

    [Fact]
    public async Task EditLoadRecognizesExpandedAndMinimalNativeMembershipWithoutCheckingOut()
    {
        var fixture = new Fixture();
        fixture.Group["Members"] = new JsonArray(Member("task", "1"), Member("host", "3"), Member("ssh", "6"));
        var result = await fixture.Service.PrepareEditorAsync("Finance");
        Assert.Equal(new[] { "task|1" }, result.Editor.Members.Select(item => item.MemberType + "|" + item.Id));
        Assert.Contains(result.Editor.Items, item => item.MemberType == "host" && item.RequiredBy.Count > 0);
        Assert.Equal("Task description", result.AvailableItems.First(item => item.MemberType == "task").Description);
        Assert.Equal(0, fixture.Checkouts);
        var version = result.Editor.EditVersion;
        var reordered = fixture.Group.DeepClone().AsObject();
        reordered["Members"] = new JsonArray(Member("ssh", "6"), Member("task", "1"), Member("host", "3"));
        reordered["xAddEditDelete"] = 1;
        Assert.Equal(version, ResourceGroupManagementService.Version(reordered));
    }

    [Fact]
    public async Task IndividuallyAddedHostBringsItsTlsCertificateByNormalizedThumbprint()
    {
        var fixture = new Fixture { IncludeTls = true };
        var members = await fixture.Service.ResolveMembersAsync([new("host", "3")]);
        Assert.Contains(members, item => item.MemberType == "ssl" && item.Id == "7" && item.RequiredBy.Count > 0);
    }

    [Fact]
    public async Task CreationUsesCompleteDatasetAndServerDerivedDependencies()
    {
        var fixture = new Fixture();
        var before = fixture.Group.ToJsonString();
        var saved = await fixture.Service.SaveEditorAsync(new() { Name = " New Group ", Description = " Description ", Members = [new("task", "1")] });
        Assert.Equal("New Group", saved.Name); Assert.Equal("Description", saved.Description);
        Assert.Equal(1, fixture.Checkouts); Assert.Single(fixture.Writes);
        var groups = fixture.Writes[0]["Groups"]!["Group"]!.AsArray();
        Assert.Equal(2, groups.Count); Assert.Equal(before, groups[0]!.ToJsonString());
        Assert.Equal(6, groups[1]!["Members"]!.AsArray().Count);
        Assert.Equal("retained", fixture.Writes[0]["SectionMarker"]!.ToString());
        Assert.Equal(saved.EditVersion, ResourceGroupManagementService.Version(groups[1]!.AsObject()));
    }

    [Fact]
    public async Task PreviouslyIndependentDependenciesUseTheSameRootsBeforeAndAfterReopening()
    {
        var fixture = new Fixture();
        var saved = await fixture.Service.SaveEditorAsync(new() { Name = "New Group", Members = [new("host", "3"), new("ssh", "6"), new("task", "1")] });
        Assert.Equal(new[] { "task|1" }, saved.Members.Select(member => member.MemberType + "|" + member.Id));
        var reopened = await fixture.Service.PrepareEditorAsync("New Group");
        Assert.Equal(saved.Members, reopened.Editor.Members);
    }

    [Fact]
    public async Task EditPreservesOtherGroupsUnknownMembersAndNativeFields()
    {
        var fixture = new Fixture();
        fixture.Group["Members"] = new JsonArray(Member("future", "99"));
        fixture.Group["NativeExtra"] = new JsonObject { ["Retain"] = true };
        var setup = await fixture.Service.PrepareEditorAsync("Finance");
        // Another group can change while this editor is open; it must be preserved from the latest checkout.
        fixture.Groups.Add(new JsonObject { ["Name"] = "Other", ["Notes"] = "Changed elsewhere", ["Members"] = new JsonArray() });
        var saved = await fixture.Service.SaveEditorAsync(new() { OriginalName = "Finance", EditVersion = setup.Editor.EditVersion,
            Name = "Renamed", Description = "Updated", Members = [new("host", "3")] });
        var groups = fixture.Writes[0]["Groups"]!["Group"]!.AsArray();
        Assert.Equal("Updated", groups[0]!["Notes"]!.ToString());
        Assert.True(groups[0]!["NativeExtra"]!["Retain"]!.GetValue<bool>());
        Assert.Contains(groups[0]!["Members"]!.AsArray(), node => node!["t"]!.ToString() == "future");
        Assert.Equal("Changed elsewhere", groups[1]!["Notes"]!.ToString());
        Assert.Equal("Renamed", saved.Name);
    }

    [Fact]
    public async Task ConcurrentEditAndDuplicateNameRestoreOnlyTheUntouchedCheckout()
    {
        var fixture = new Fixture();
        var setup = await fixture.Service.PrepareEditorAsync("Finance");
        fixture.Group["Notes"] = "Someone else's change";
        var before = fixture.Dataset.ToJsonString();
        await Assert.ThrowsAsync<ObjectEditConflictException>(() => fixture.Service.SaveEditorAsync(new()
            { OriginalName = "Finance", EditVersion = setup.Editor.EditVersion, Name = "Finance", Description = "stale" }));
        Assert.Equal(before, fixture.Writes[0].ToJsonString());
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SaveEditorAsync(new() { Name = "FINANCE" }));
        Assert.Equal(before, fixture.Writes[1].ToJsonString());
    }

    [Fact]
    public async Task InvalidOrMissingMembersNeverAcquireCheckout()
    {
        var fixture = new Fixture();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SaveEditorAsync(new() { Name = "Test", Members = [new("host", "missing")] }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ResolveMembersAsync([new("invalid", "3")]));
        Assert.Equal(0, fixture.Checkouts); Assert.Empty(fixture.Writes);
    }

    [Fact]
    public async Task MalformedCheckoutNeverBecomesAnEmptyDataset()
    {
        var fixture = new Fixture();
        fixture.Dataset["Groups"]!["Group"] = new JsonObject { ["Name"] = "Must not disappear" };
        var before = fixture.Dataset.ToJsonString();
        await Assert.ThrowsAsync<MoveItApiException>(() => fixture.Service.SaveEditorAsync(new() { Name = "New" }));
        Assert.Single(fixture.Writes);
        Assert.Equal(before, fixture.Writes[0].ToJsonString());
    }

    [Fact]
    public async Task AmbiguousPutIsNotRetriedOrFollowedByAnOldSnapshotWrite()
    {
        var fixture = new Fixture { FailPut = true };
        await Assert.ThrowsAsync<ResourceGroupSaveUnconfirmedException>(() => fixture.Service.SaveEditorAsync(new() { Name = "New Group" }));
        Assert.Equal(1, fixture.Checkouts); Assert.Single(fixture.Writes);
        Assert.Equal(2, fixture.Writes[0]["Groups"]!["Group"]!.AsArray().Count);
    }

    [Theory]
    [InlineData("empty")] [InlineData("status-only")] [InlineData("code-only")]
    [InlineData("invalid-code")] [InlineData("timeout")] [InlineData("malformed-json")]
    public async Task MissingAcknowledgementOrTimeoutAfterCommitCannotBeReportedAsSavedOrReplayed(string mode)
    {
        var fixture = new Fixture { AcknowledgementMode = mode };
        await Assert.ThrowsAsync<ResourceGroupSaveUnconfirmedException>(() => fixture.Service.SaveEditorAsync(new() { Name = "New Group" }));
        Assert.Single(fixture.Writes); Assert.Equal(1, fixture.Checkouts);
        // The fake server committed before losing/corrupting the response. Never write the original dataset back.
        Assert.Equal(2, fixture.Groups.Count);
    }

    [Fact]
    public async Task ExistingTaskBulkActionsShareTheSameCompositionAndDatasetCommit()
    {
        var fixture = new Fixture();
        var added = await fixture.Service.AddTasksToGroupAsync(["1"], "Finance");
        Assert.Equal(6, added.AddedCount);
        var created = await fixture.Service.CreateForTasksAsync(["2"], "Task Group", "Description");
        Assert.Equal(6, created.TotalMembers);
        Assert.Equal(2, fixture.Checkouts); Assert.Equal(2, fixture.Writes.Count);
        Assert.Equal(2, fixture.Writes[1]["Groups"]!["Group"]!.AsArray().Count);
    }

    [Fact]
    public async Task DeleteUsesCheckoutAndPreservesTheLatestUnrelatedDataset()
    {
        var fixture = new Fixture();
        fixture.Group["Members"] = new JsonArray(Member("future", "missing"));
        var setup = await fixture.Service.PrepareDeleteAsync("finance");
        Assert.Equal(0, fixture.Checkouts); Assert.Empty(fixture.Writes);
        // Include fields/member types unknown to MAUI and an unrelated group added after confirmation opens.
        var other = new JsonObject { ["Name"] = "Other", ["NativeExtra"] = true, ["Members"] = new JsonArray(Member("future", "99")) };
        fixture.Groups.Add(other);
        var preserved = other.ToJsonString();
        await fixture.Service.DeleteAsync(new() { Name = setup.Name, EditVersion = setup.EditVersion });
        Assert.Equal(1, fixture.Checkouts); Assert.Single(fixture.Writes); Assert.Single(fixture.Groups);
        Assert.Equal(preserved, fixture.Groups[0]!.ToJsonString());
        Assert.Equal("retained", fixture.Writes[0]["SectionMarker"]!.ToString());
    }

    [Theory]
    [InlineData("changed")] [InlineData("removed")] [InlineData("forged")]
    public async Task DeleteConflictReleasesOnlyTheUnchangedCheckout(string scenario)
    {
        var fixture = new Fixture();
        var setup = await fixture.Service.PrepareDeleteAsync("Finance");
        if (scenario == "changed") fixture.Group["Notes"] = "Changed by another user";
        if (scenario == "removed") fixture.Groups.Clear();
        var before = fixture.Dataset.ToJsonString();
        await Assert.ThrowsAsync<ObjectEditConflictException>(() => fixture.Service.DeleteAsync(new()
            { Name = setup.Name, EditVersion = scenario == "forged" ? "forged" : setup.EditVersion }));
        Assert.Equal(1, fixture.Checkouts); Assert.Single(fixture.Writes);
        Assert.Equal(before, fixture.Writes[0].ToJsonString());
    }

    [Fact]
    public async Task DeleteRequiresNameAndRevisionBeforeCheckoutAndCanRemoveTheLastGroup()
    {
        var fixture = new Fixture();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.DeleteAsync(new() { EditVersion = "version" }));
        await Assert.ThrowsAsync<ObjectEditConflictException>(() => fixture.Service.DeleteAsync(new() { Name = "Finance" }));
        Assert.Equal(0, fixture.Checkouts); Assert.Empty(fixture.Writes);
        var setup = await fixture.Service.PrepareDeleteAsync("Finance");
        await fixture.Service.DeleteAsync(new() { Name = setup.Name, EditVersion = setup.EditVersion });
        Assert.Empty(fixture.Groups); Assert.Single(fixture.Writes);
    }

    [Theory]
    [InlineData("timeout")] [InlineData("malformed-json")] [InlineData("empty")]
    public async Task UnconfirmedDeleteNeverReplaysOrRestoresTheDeletedGroup(string mode)
    {
        var fixture = new Fixture { AcknowledgementMode = mode };
        var setup = await fixture.Service.PrepareDeleteAsync("Finance");
        await Assert.ThrowsAsync<ResourceGroupSaveUnconfirmedException>(() => fixture.Service.DeleteAsync(new() { Name = setup.Name, EditVersion = setup.EditVersion }));
        Assert.Equal(1, fixture.Checkouts); Assert.Single(fixture.Writes); Assert.Empty(fixture.Groups);
    }

    private static JsonObject Member(string type, string id) => new() { ["t"] = type, ["value"] = id };
    private sealed class Fixture
    {
        public JsonObject Dataset { get; } = JsonNode.Parse("""{"SectionMarker":"retained","Groups":{"Group":[{"Name":"Finance","Notes":"Original","Members":[]}]}}""")!.AsObject();
        public JsonArray Groups => Dataset["Groups"]!["Group"]!.AsArray();
        public JsonObject Group => Groups[0]!.AsObject();
        public List<JsonObject> Writes { get; } = [];
        public int Checkouts { get; private set; }
        public int ForcedCheckouts { get; private set; }
        public Action? BeforeCheckout { get; set; }
        public bool FailPut { get; init; }
        public bool IncludeTls { get; init; }
        public string AcknowledgementMode { get; init; } = "normal";
        public ResourceGroupManagementService Service { get; }
        public Fixture()
        {
            var rest = Stub<IMoveItRestClient>((method, args) => method.Name switch
            {
                nameof(IMoveItRestClient.GetResourceGroupDefinitionsAsync) => Task.FromResult<IReadOnlyList<JsonObject>>(Groups.OfType<JsonObject>().ToArray()),
                nameof(IMoveItRestClient.CheckoutResourceGroupsV0Async) => Checkout(),
                nameof(IMoveItRestClient.ForceCheckoutObjectV0Async) => ForceCheckout(args!),
                nameof(IMoveItRestClient.UpdateResourceGroupsV0Async) => Put((JsonObject)args![0]!),
                nameof(IMoveItRestClient.GetHostAsync) => Task.FromResult(JsonNode.Parse(IncludeTls
                    ? """{"FTP":{"ID":"3","DefClientKeyID":"6","DirScriptID":"5","DefClientCertSHA1":"AA:BB"}}"""
                    : """{"SSHFTP":{"ID":"3","DefClientKeyID":"6","DirScriptID":"5"}}""")!.AsObject()),
                _ => throw new NotSupportedException(method.Name)
            });
            var crud = Stub<ITaskCrudCoordinator>((method, _) => method.Name == nameof(ITaskCrudCoordinator.GetDefinitionAsync)
                ? Task.FromResult(JsonNode.Parse("""{"steps":[{"Source":{"HostID":"3"}},{"Process":{"ScriptID":"4"}}],"Parameters":{"Parameter":[{"Name":"PGPRecipientKey","value":"0x1234"}]}}""")!.AsObject())
                : throw new NotSupportedException(method.Name));
            var tasks = Stub<IAutomationTaskService>((_, _) => Task.FromResult<IReadOnlyList<AutomationTask>>([
                new() { Id = "1", Name = "First", Description = "Task description" }, new() { Id = "2", Name = "Second" }]));
            var hosts = Stub<IAutomationHostService>((_, _) => Task.FromResult<IReadOnlyList<AutomationHost>>([new() { Id = "3", Name = "Host" }]));
            var scripts = Stub<IAutomationScriptService>((_, _) => Task.FromResult<IReadOnlyList<AutomationScript>>([
                new() { Id = "4", Name = "Process" }, new() { Id = "5", Name = "Directory parser" }]));
            var keys = Stub<IAutomationKeyCertificateService>((_, _) => Task.FromResult(new KeyCertificateCacheSnapshot([
                new() { Id = "6", Name = "SSH", Kind = KeyCertificateKind.Ssh }, new() { Id = "7", Name = "TLS", Kind = KeyCertificateKind.Tls, Sha1Thumbprint = "AABB" },
                new() { Id = "8", Name = "PGP", Kind = KeyCertificateKind.Pgp, Fingerprint = "0x1234" }], DateTimeOffset.UtcNow)));
            Service = new(rest, new ResourceGroupLookupService(rest), crud, hosts, scripts, keys, NullLogger<ResourceGroupManagementService>.Instance, tasks);
        }
        private Task<JsonObject> Checkout() { Checkouts++; BeforeCheckout?.Invoke(); return Task.FromResult(Dataset.DeepClone().AsObject()); }
        private Task<JsonObject> ForceCheckout(object?[] args)
        { Assert.Equal("Resource Groups", args[0]); Assert.Equal("-1", args[1]); ForcedCheckouts++; return Checkout(); }
        private Task<JsonObject> Put(JsonObject payload)
        {
            Writes.Add(payload.DeepClone().AsObject());
            if (FailPut) throw new MoveItApiException("Unavailable", HttpStatusCode.ServiceUnavailable);
            Dataset["Groups"] = payload["Groups"]!.DeepClone();
            return AcknowledgementMode switch
            {
                "empty" => Task.FromResult(new JsonObject()),
                "status-only" => Task.FromResult(new JsonObject { ["status"] = "success" }),
                "code-only" => Task.FromResult(new JsonObject { ["errorcode"] = 0 }),
                "invalid-code" => Task.FromResult(new JsonObject { ["status"] = "success", ["errorcode"] = "unknown" }),
                "timeout" => throw new MoveItApiException("Response timed out after commit", HttpStatusCode.RequestTimeout),
                "malformed-json" => throw new JsonException("Truncated response after commit"),
                _ => Task.FromResult(new JsonObject { ["status"] = "success", ["errorcode"] = 0 })
            };
        }
    }
    public class Proxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
    }
    private static T Stub<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    { var proxy = DispatchProxy.Create<T, Proxy>(); ((Proxy)(object)proxy).Handler = handler; return proxy; }
}
