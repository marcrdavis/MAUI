using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class ResourceGroupPermissionUpdateTests
{
    [Fact]
    public async Task ChangedAclsUseIndividualPutsIncludingZeroAndPreserveOtherNativeFields()
    {
        using var f = new Fixture(); var request = await f.Request("0", "1");
        request.Updates[0].Permissions["HostAddEditDelete"] = true;
        request.Updates[1].Permissions["SSLAddEditDelete"] = true;
        var cached = await f.Service.GetSnapshotAsync(); var untouched = f.Acls[2].ToJsonString();
        var result = await f.Service.SavePermissionsAsync(request);
        Assert.Equal(new[] { "saved", "saved" }, result.Items.Select(item => item.Status));
        Assert.Equal(new[] { "0", "1" }, f.Puts.Select(item => item["ACLID"]!.ToString()));
        Assert.Equal(untouched, f.Acls[2].ToJsonString());
        Assert.All(f.Puts, item => { Assert.Equal("Finance", item["ResourceGroupName"]!.ToString()); Assert.Equal("native", item["Extra"]!.ToString()); Assert.Equal("kept", item["Permissions"]!["FutureFlag"]!.ToString()); });
        Assert.Equal("1", f.Puts[0]["Permissions"]!["HostAddEditDelete"]!.ToString());
        Assert.Equal("0", f.Puts[0]["Permissions"]!["TaskRun"]!.ToString());
        Assert.NotSame(cached, await f.Service.GetSnapshotAsync());
    }
    [Fact]
    public async Task UnchangedRowsNeverProducePuts()
    {
        using var f = new Fixture(); var result = await f.Service.SavePermissionsAsync(await f.Request("0"));
        Assert.Equal("unchanged", result.Items[0].Status); Assert.Empty(f.Puts);
    }
    [Theory]
    [InlineData("revision")] [InlineData("permissions")] [InlineData("identity")] [InlineData("group")] [InlineData("missing")]
    public async Task EveryAclIsPrevalidatedBeforeTheFirstPut(string change)
    {
        using var f = new Fixture(); var request = await f.Request("0", "1");
        request.Updates[0].Permissions["HostAddEditDelete"] = true;
        if (change == "revision") request.Updates[1].EditVersion = "expired";
        if (change == "permissions") f.Acls[1]["Permissions"]!["TaskRun"] = 1;
        if (change == "identity") f.Acls[1]["UserGroupName"] = "Reused ID";
        if (change == "group") f.Acls[1]["ResourceGroupName"] = "Other";
        if (change == "missing") f.Acls.RemoveAt(1);
        await Assert.ThrowsAsync<ObjectEditConflictException>(() => f.Service.SavePermissionsAsync(request)); Assert.Empty(f.Puts);
    }
    [Fact]
    public async Task MalformedFlagsDuplicatesAndInvalidTaskDependenciesAreRejectedWithoutWrites()
    {
        using var f = new Fixture(); var request = await f.Request("0");
        request.Updates[0].Permissions["TaskAddEditDelete"] = true;
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.SavePermissionsAsync(request));
        request.Updates[0].Permissions["TaskEditExisting"] = true;
        request.Updates[0].Permissions.Remove("TaskRun");
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.SavePermissionsAsync(request));
        request = await f.Request("0"); request.Updates.Add(request.Updates[0]);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.SavePermissionsAsync(request));
        request = await f.Request("0"); request.Updates[0].AclId = "../0";
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.SavePermissionsAsync(request)); Assert.Empty(f.Puts);
    }
    [Fact]
    public async Task EditExistingCanBeGrantedWithoutRunOrAddEditDelete()
    {
        using var f = new Fixture(); var request = await f.Request("0"); request.Updates[0].Permissions["TaskEditExisting"] = true;
        await f.Service.SavePermissionsAsync(request);
        Assert.Equal("1", f.Puts[0]["Permissions"]!["TaskEditExisting"]!.ToString());
        Assert.Equal("0", f.Puts[0]["Permissions"]!["TaskRun"]!.ToString()); Assert.Equal("0", f.Puts[0]["Permissions"]!["TaskAddEditDelete"]!.ToString());
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task LostAcknowledgementsAreReconciledOnceAndNeverReplayed(bool persisted)
    {
        using var f = new Fixture { FailId = "0", Failure = HttpStatusCode.InternalServerError, PersistBeforeFailure = persisted };
        var request = await f.Request("0", "1"); foreach (var item in request.Updates) item.Permissions["HostAddEditDelete"] = true;
        var result = await f.Service.SavePermissionsAsync(request);
        Assert.Equal(persisted ? new[] { "saved", "saved" } : new[] { "unconfirmed", "notAttempted" }, result.Items.Select(item => item.Status));
        Assert.Equal(!persisted, result.SaveUnconfirmed); Assert.Equal(persisted ? 2 : 1, f.Puts.Count);
    }
    [Fact]
    public async Task ClearRejectionReportsOnlyFailedRowForRetry()
    {
        using var f = new Fixture { FailId = "0", Failure = HttpStatusCode.UnprocessableEntity };
        var request = await f.Request("0", "1"); foreach (var item in request.Updates) item.Permissions["PGPAddEditDelete"] = true;
        var result = await f.Service.SavePermissionsAsync(request);
        Assert.False(result.SaveUnconfirmed); Assert.False(result.EditConflict); Assert.Equal(new[] { "failed", "saved" }, result.Items.Select(item => item.Status)); Assert.Equal(2, f.Puts.Count);
    }
    [Fact]
    public async Task NativeChangeAfterAnEarlierPutStopsRemainingWritesAndReportsPartialSave()
    {
        using var f = new Fixture { ChangeNextAclAfterPut = true }; var request = await f.Request("0", "1", "2");
        foreach (var item in request.Updates) item.Permissions["HostAddEditDelete"] = true;
        var result = await f.Service.SavePermissionsAsync(request);
        Assert.True(result.EditConflict); Assert.Equal(new[] { "saved", "conflict", "notAttempted" }, result.Items.Select(item => item.Status)); Assert.Single(f.Puts);
    }
    [Fact]
    public async Task UnavailableInventoryAfterAnEarlierPutRetainsItsSuccessAndStopsTheBatch()
    {
        using var f = new Fixture { FailReadAfterPut = true }; var request = await f.Request("0", "1", "2");
        foreach (var item in request.Updates) item.Permissions["HostAddEditDelete"] = true;
        var result = await f.Service.SavePermissionsAsync(request);
        Assert.True(result.SaveUnconfirmed); Assert.Equal(new[] { "saved", "notAttempted", "notAttempted" }, result.Items.Select(item => item.Status)); Assert.Single(f.Puts);
    }
    [Fact]
    public async Task UpdateUsesTheSharedGroupWriteGateAndRequiresMoveItAdministrator()
    {
        using var f = new Fixture(); var request = await f.Request("0"); request.Updates[0].Permissions["TaskRun"] = true;
        await ResourceGroupManagementService.UpdateGate.WaitAsync(); Task<ResourceGroupPermissionsSaveResult> pending;
        try { pending = f.Service.SavePermissionsAsync(request); Assert.False(pending.IsCompleted); Assert.Empty(f.Puts); }
        finally { ResourceGroupManagementService.UpdateGate.Release(); }
        await pending;
        f.State.Info = new MoveItInfo { IsAdmin = false };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.SavePermissionsAsync(request)); Assert.Single(f.Puts);
    }
    [Fact]
    public async Task PutTransportAcceptsAnEmptyAcknowledgementAndRejectsMismatchedOrUnsafeIds()
    {
        using var handler = new PutHandler();
        var sessions = Stub<IMoveItSessionService>((method, _) => method.Name == nameof(IMoveItSessionService.Get)
            ? new MoveItSessionState { WebAdminUrl = "https://moveit.test", AccessToken = "token", AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) }
            : throw new NotSupportedException(method.Name));
        var client = new MoveItRestClient(new Factory(handler), sessions, ConfigurationStartupState.Capture(new ConfigurationBuilder().Build()), null!, NullLogger<MoveItRestClient>.Instance);
        await client.UpdateResourceGroupAclAsync("0", new JsonObject { ["ACLID"] = "0", ["Permissions"] = new JsonObject { ["TaskRun"] = 1 } });
        await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateResourceGroupAclAsync("0", new JsonObject { ["ACLID"] = "1" }));
        await Assert.ThrowsAsync<ArgumentException>(() => client.UpdateResourceGroupAclAsync("../0", new JsonObject { ["ACLID"] = "../0" }));
        Assert.Equal(1, handler.Calls); Assert.Contains("\"TaskRun\":1", handler.Body);
    }
    private sealed class Fixture : IDisposable
    {
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());
        public MoveItSessionState State { get; } = new() { Username = "Admin", ServerKey = "test", Info = new MoveItInfo { IsAdmin = true } };
        public List<JsonObject> Acls { get; } = Enumerable.Range(0, 3).Select(i => new JsonObject { ["ACLID"] = i.ToString(), ["ResourceGroupName"] = "Finance", ["UserGroupName"] = "Users " + i, ["Extra"] = "native",
            ["Permissions"] = new JsonObject(ResourceGroupPermissionsService.PermissionKeys.Select(key => KeyValuePair.Create<string, JsonNode?>(key, JsonValue.Create(0)))) { ["FutureFlag"] = "kept" } }).ToList();
        public List<JsonObject> Puts { get; } = []; public string? FailId { get; init; } public HttpStatusCode Failure { get; init; }
        public bool PersistBeforeFailure { get; init; } public bool ChangeNextAclAfterPut { get; init; } public bool FailReadAfterPut { get; init; }
        public ResourceGroupPermissionsService Service { get; }
        public Fixture()
        {
            var rest = Stub<IMoveItRestClient>((method, args) =>
            {
                if (method.Name == nameof(IMoveItRestClient.GetResourceGroupDefinitionsAsync)) return Task.FromResult<IReadOnlyList<JsonObject>>([new JsonObject { ["Name"] = "Finance" }]);
                if (method.Name == nameof(IMoveItRestClient.GetResourceGroupAclDefinitionsAsync)) return FailReadAfterPut && Puts.Count > 0
                    ? Task.FromException<IReadOnlyList<JsonObject>>(new HttpRequestException("Read unavailable"))
                    : Task.FromResult<IReadOnlyList<JsonObject>>(Acls.Select(acl => acl.DeepClone().AsObject()).ToArray());
                if (method.Name != nameof(IMoveItRestClient.UpdateResourceGroupAclAsync)) throw new InvalidOperationException("Unexpected transport: " + method.Name);
                var id = args[0]!.ToString()!; var payload = ((JsonObject)args[1]!).DeepClone().AsObject(); Puts.Add(payload);
                if (id != FailId || PersistBeforeFailure) Acls[Acls.FindIndex(acl => acl["ACLID"]!.ToString() == id)] = payload;
                if (ChangeNextAclAfterPut) Acls[1]["Permissions"]!["TaskRun"] = 1;
                return id == FailId ? Task.FromException(new MoveItApiException("Native rejection", Failure)) : Task.CompletedTask;
            });
            Service = new(rest, Stub<IMoveItSessionService>((method, _) => method.Name == nameof(IMoveItSessionService.Get) ? State : throw new NotSupportedException(method.Name)), _cache);
        }
        public async Task<ResourceGroupPermissionsSaveRequest> Request(params string[] ids)
        {
            var snapshot = await Service.GetSnapshotAsync(true);
            return new() { ResourceGroupName = "finance", Updates = ids.Select(id => { var child = snapshot.Groups[0].UserGroups.First(row => row.AclId == id); return new ResourceGroupPermissionUpdate {
                AclId = id, UserGroupName = child.Name, EditVersion = child.EditVersion, Permissions = new(child.Permissions, StringComparer.Ordinal) }; }).ToList() };
        }
        public void Dispose() => _cache.Dispose();
    }
    private sealed class PutHandler : HttpMessageHandler
    {
        public int Calls { get; private set; } public string Body { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; Assert.Equal(HttpMethod.Put, request.Method); Assert.Equal("/api/v1/resourcegroups/acls/0", request.RequestUri!.PathAndQuery);
            Assert.Equal("Bearer token", request.Headers.Authorization!.ToString()); Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            Body = await request.Content.ReadAsStringAsync(ct); return new(HttpStatusCode.NoContent);
        }
    }
    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(handler, false); }
    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    { var proxy = DispatchProxy.Create<T, HostSharedCrudTests.CrudProxy>(); ((HostSharedCrudTests.CrudProxy)(object)proxy).Handler = handler; return proxy; }
}
