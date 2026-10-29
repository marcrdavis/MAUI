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

public sealed class ResourceGroupUserGroupTests
{
    [Fact]
    public async Task SetupListsLiveLocalOrDomainGroupsAndExistingAssignments()
    {
        using var f = new Fixture(); f.Acls.Add(Acl("0", "Operators", 1));
        var setup = await f.Service.PrepareUserGroupsAsync(" finance ", "remote");
        Assert.Equal("Finance", setup.ResourceGroupName); Assert.Equal("remote", setup.UserGroupType);
        Assert.Equal(new[] { "DOMAIN\\Readers" }, setup.Groups.Select(item => item.Name));
        Assert.Equal(new[] { "Operators" }, setup.AssignedGroups); Assert.Empty(f.Posts); Assert.Empty(f.Deletes);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.PrepareUserGroupsAsync("Finance", "domain"));
    }
    [Fact]
    public async Task AddValidatesAllSelectionsAndPostsZeroPermissionShellWithoutChangingExistingGrants()
    {
        using var f = new Fixture(); var existing = Acl("7", "Operators", 1); f.Acls.Add(existing);
        var before = existing.ToJsonString();
        var result = await f.Service.AddUserGroupsAsync(Add(new ResourceUserGroupSelection("operators", "local"), new("Backup Operators", "local"), new("DOMAIN\\Readers", "remote")));
        Assert.Equal(new[] { "alreadyAssigned", "added", "added" }, result.Items.Select(item => item.Status));
        Assert.Equal(2, f.Posts.Count); Assert.Equal(before, existing.ToJsonString());
        Assert.All(f.Posts, payload =>
        {
            Assert.True(payload.ContainsKey("ACLID")); Assert.Null(payload["ACLID"]);
            Assert.Equal("Finance", payload["ResourceGroupName"]!.ToString());
            Assert.Equal(ResourceGroupPermissionsService.PermissionKeys.Count, payload["Permissions"]!.AsObject().Count);
            Assert.All(payload["Permissions"]!.AsObject(), pair => Assert.Equal("0", pair.Value!.ToString()));
        });
        Assert.Equal("0", f.Acls[1]["ACLID"]!.ToString()); // Native ID zero is a valid assignment, not a missing ID.
        var again = await f.Service.AddUserGroupsAsync(Add(new ResourceUserGroupSelection("Backup Operators", "local")));
        Assert.Equal("alreadyAssigned", again.Items[0].Status); Assert.Equal(2, f.Posts.Count);
    }
    [Fact]
    public async Task ForgedOrStaleUserGroupsCannotCreateAnyAssignments()
    {
        using var f = new Fixture();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.AddUserGroupsAsync(Add(new ResourceUserGroupSelection("Operators", "local"), new("Not in inventory", "remote"))));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.AddUserGroupsAsync(Add(new ResourceUserGroupSelection("Operators", "local"), new("operators", "local"))));
        Assert.Empty(f.Posts);
    }
    [Fact]
    public async Task LostCreateAcknowledgementReconcilesWithoutReposting()
    {
        using var f = new Fixture { LoseAcknowledgement = true };
        var result = await f.Service.AddUserGroupsAsync(Add(new ResourceUserGroupSelection("Operators", "local")));
        Assert.False(result.SaveUnconfirmed); Assert.Equal("alreadyAssigned", result.Items[0].Status); Assert.Single(f.Posts); Assert.Single(f.Acls);
    }
    [Fact]
    public async Task KnownRejectionCanReportPartialResultsWhileUnconfirmedOutcomeStopsTheBatch()
    {
        using var f = new Fixture { FailFirst = HttpStatusCode.UnprocessableEntity };
        var result = await f.Service.AddUserGroupsAsync(Add(new ResourceUserGroupSelection("Operators", "local"), new("Backup Operators", "local")));
        Assert.Equal(new[] { "failed", "added" }, result.Items.Select(item => item.Status)); Assert.False(result.SaveUnconfirmed);
        using var uncertain = new Fixture { FailFirst = HttpStatusCode.InternalServerError };
        result = await uncertain.Service.AddUserGroupsAsync(Add(new ResourceUserGroupSelection("Operators", "local"), new("Backup Operators", "local")));
        Assert.True(result.SaveUnconfirmed); Assert.Equal(new[] { "unconfirmed", "notAttempted" }, result.Items.Select(item => item.Status)); Assert.Single(uncertain.Posts);
    }
    [Fact]
    public async Task ClearValidationRejectionDoesNotBecomeAnUnconfirmedSaveWhenReadsFail()
    {
        using var f = new Fixture { FailFirst = HttpStatusCode.UnprocessableEntity, FailReadAfterPost = true };
        var result = await f.Service.AddUserGroupsAsync(Add(new ResourceUserGroupSelection("Operators", "local")));
        Assert.False(result.SaveUnconfirmed); Assert.Equal("failed", result.Items[0].Status); Assert.Single(f.Posts);
    }
    [Fact]
    public async Task AclMutationInvalidatesTheCachedSnapshot()
    {
        using var f = new Fixture(); var before = await f.Service.GetSnapshotAsync();
        await f.Service.AddUserGroupsAsync(Add(new ResourceUserGroupSelection("Operators", "local")));
        var after = await f.Service.GetSnapshotAsync(); Assert.NotSame(before, after); Assert.Single(after.Groups[0].UserGroups);
    }
    [Fact]
    public async Task RemoveUsesExactAclZeroAndRetainsOtherAssignmentsAndPermissions()
    {
        using var f = new Fixture(); f.Acls.Add(Acl("0", "Operators", 1)); var other = Acl("1", "Backup Operators", 1); f.Acls.Add(other);
        var before = other.ToJsonString(); var request = await Removal(f);
        var result = await f.Service.RemoveUserGroupAsync(request);
        Assert.Equal("removed", result.Items[0].Status); Assert.Equal(new[] { "0" }, f.Deletes); Assert.Equal(before, f.Acls[0].ToJsonString());
        result = await f.Service.RemoveUserGroupAsync(request); Assert.Equal("alreadyRemoved", result.Items[0].Status); Assert.Single(f.Deletes);
    }
    [Theory]
    [InlineData("permissions")] [InlineData("identity")] [InlineData("revision")]
    public async Task StaleOrReusedAclCannotBeRemoved(string change)
    {
        using var f = new Fixture(); f.Acls.Add(Acl("0", "Operators", 1)); var request = await Removal(f);
        if (change == "permissions") f.Acls[0]["Permissions"]!["TaskRun"] = 0;
        if (change == "identity") f.Acls[0]["UserGroupName"] = "Other";
        if (change == "revision") request.EditVersion = "expired";
        await Assert.ThrowsAsync<ObjectEditConflictException>(() => f.Service.RemoveUserGroupAsync(request)); Assert.Empty(f.Deletes);
    }
    [Fact]
    public async Task LostDeleteAcknowledgementReconcilesWithoutDeletingAgain()
    {
        using var f = new Fixture { LoseAcknowledgement = true }; f.Acls.Add(Acl("0", "Operators"));
        var result = await f.Service.RemoveUserGroupAsync(await Removal(f)); Assert.Equal("removed", result.Items[0].Status); Assert.Single(f.Deletes);
    }
    [Fact]
    public async Task OnlyLiveMoveItAdministratorsCanListOrChangeAclAssignments()
    {
        using var f = new Fixture(); f.State.Info = new MoveItInfo { IsAdmin = false };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.GetSnapshotAsync());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.PrepareUserGroupsAsync("Finance", "local"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.AddUserGroupsAsync(Add(new ResourceUserGroupSelection("Operators", "local"))));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.RemoveUserGroupAsync(new()));
        Assert.Equal(0, f.Reads); Assert.Empty(f.Posts); Assert.Empty(f.Deletes);
    }
    [Fact]
    public async Task AclWritesWaitForTheExistingResourceGroupMutationBoundary()
    {
        using var f = new Fixture(); Task<ResourceGroupUserGroupMutation> pending;
        await ResourceGroupManagementService.UpdateGate.WaitAsync();
        try
        {
            pending = f.Service.AddUserGroupsAsync(Add(new ResourceUserGroupSelection("Operators", "local")));
            Assert.False(pending.IsCompleted); Assert.Empty(f.Posts);
        }
        finally { ResourceGroupManagementService.UpdateGate.Release(); }
        var result = await pending; Assert.Equal("added", result.Items[0].Status); Assert.Single(f.Posts);
    }
    [Fact]
    public async Task TransportUsesPagedUserGroupsAndTheCapturedV1AclContracts()
    {
        using var handler = new TransportHandler();
        var sessions = Stub<IMoveItSessionService>((method, _) => method.Name == nameof(IMoveItSessionService.Get)
            ? new MoveItSessionState { WebAdminUrl = "https://moveit.test", AccessToken = "token", AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) }
            : throw new NotSupportedException(method.Name));
        var client = new MoveItRestClient(new ClientFactory(handler), sessions, ConfigurationStartupState.Capture(new ConfigurationBuilder().Build()), null!, NullLogger<MoveItRestClient>.Instance);
        Assert.Equal(2, (await client.GetUserGroupDefinitionsAsync("remote")).Count);
        await client.CreateResourceGroupAclAsync(new JsonObject { ["ACLID"] = null, ["ResourceGroupName"] = "Finance", ["UserGroupName"] = "DOMAIN\\Readers", ["Permissions"] = new JsonObject() });
        await client.DeleteResourceGroupAclAsync("0");
        await Assert.ThrowsAsync<ArgumentException>(() => client.DeleteResourceGroupAclAsync("../1"));
        Assert.Equal(new[] { "GET /api/v1/usergroups?type=remote&page=1&perPage=100", "GET /api/v1/usergroups?type=remote&page=2&perPage=100", "POST /api/v1/resourcegroups/acls", "DELETE /api/v1/resourcegroups/acls/0" }, handler.Calls);
        Assert.Contains("\"ACLID\":null", handler.Body);
    }
    private static ResourceGroupUserGroupsRequest Add(params ResourceUserGroupSelection[] selections) => new() { ResourceGroupName = "finance", UserGroups = selections.ToList() };
    private static async Task<ResourceGroupRemoveUserGroupRequest> Removal(Fixture f)
    {
        var grant = (await f.Service.GetSnapshotAsync(true)).Groups[0].UserGroups.First(item => item.Name == "Operators");
        return new() { ResourceGroupName = "Finance", UserGroupName = grant.Name, AclId = grant.AclId, EditVersion = grant.EditVersion };
    }
    private static JsonObject Acl(string id, string user, int permission = 0) => new() { ["ACLID"] = id, ["ResourceGroupName"] = "Finance", ["UserGroupName"] = user,
        ["Permissions"] = new JsonObject(ResourceGroupPermissionsService.PermissionKeys.Select(key => KeyValuePair.Create<string, JsonNode?>(key, JsonValue.Create(permission)))) };
    private sealed class Fixture : IDisposable
    {
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());
        public MoveItSessionState State { get; } = new() { Username = "Admin", ServerKey = "test", Info = new MoveItInfo { IsAdmin = true } };
        public List<JsonObject> Acls { get; } = []; public List<JsonObject> Posts { get; } = []; public List<string> Deletes { get; } = [];
        public int Reads { get; private set; } public bool FailReadAfterPost { get; init; } public bool LoseAcknowledgement { get; init; } public HttpStatusCode? FailFirst { get; init; }
        public ResourceGroupPermissionsService Service { get; }
        public Fixture()
        {
            var rest = Stub<IMoveItRestClient>((method, args) =>
            {
                if (method.Name == nameof(IMoveItRestClient.GetResourceGroupDefinitionsAsync)) { Reads++; return Task.FromResult<IReadOnlyList<JsonObject>>([new JsonObject { ["Name"] = "Finance" }]); }
                if (method.Name == nameof(IMoveItRestClient.GetUserGroupDefinitionsAsync)) { Reads++; return Task.FromResult<IReadOnlyList<JsonObject>>(args[0]?.ToString() == "local"
                    ? [new JsonObject { ["Name"] = "Operators", ["Description"] = "Run tasks" }, new JsonObject { ["Name"] = "Backup Operators", ["Description"] = "Backup" }]
                    : [new JsonObject { ["Name"] = "DOMAIN\\Readers", ["Description"] = "Read only" }]); }
                if (method.Name == nameof(IMoveItRestClient.GetResourceGroupAclDefinitionsAsync)) { Reads++; return FailReadAfterPost && Posts.Count > 0
                    ? Task.FromException<IReadOnlyList<JsonObject>>(new InvalidOperationException("Read unavailable"))
                    : Task.FromResult<IReadOnlyList<JsonObject>>(Acls.Select(row => row.DeepClone().AsObject()).ToArray()); }
                if (method.Name == nameof(IMoveItRestClient.CreateResourceGroupAclAsync))
                {
                    var payload = ((JsonObject)args[0]!).DeepClone().AsObject(); Posts.Add(payload);
                    if (Posts.Count == 1 && FailFirst.HasValue) return Task.FromException<JsonObject>(new MoveItApiException("Rejected", FailFirst));
                    var saved = payload.DeepClone().AsObject(); saved["ACLID"] = (Posts.Count - 1).ToString(); Acls.Add(saved);
                    return LoseAcknowledgement ? Task.FromException<JsonObject>(new HttpRequestException("Lost acknowledgement")) : Task.FromResult(saved);
                }
                if (method.Name == nameof(IMoveItRestClient.DeleteResourceGroupAclAsync))
                {
                    var id = args[0]!.ToString()!; Deletes.Add(id); Acls.RemoveAll(row => row["ACLID"]!.ToString() == id);
                    return LoseAcknowledgement ? Task.FromException(new HttpRequestException("Lost acknowledgement")) : Task.CompletedTask;
                }
                throw new InvalidOperationException("Unexpected transport (no v0 checkout/rewrite is allowed): " + method.Name);
            });
            var sessions = Stub<IMoveItSessionService>((method, _) => method.Name == nameof(IMoveItSessionService.Get) ? State : throw new NotSupportedException(method.Name));
            Service = new(rest, sessions, _cache);
        }
        public void Dispose() => _cache.Dispose();
    }
    private sealed class TransportHandler : HttpMessageHandler
    {
        public List<string> Calls { get; } = []; public string Body { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("Bearer token", request.Headers.Authorization!.ToString()); Calls.Add(request.Method + " " + request.RequestUri!.PathAndQuery);
            if (request.Method == HttpMethod.Delete) { Assert.Null(request.Content); return new(HttpStatusCode.NoContent); }
            if (request.Method == HttpMethod.Post) { Body = await request.Content!.ReadAsStringAsync(ct); Assert.Equal("application/json", request.Content.Headers.ContentType!.MediaType); return new(HttpStatusCode.OK) { Content = new StringContent("{\"ACLID\":\"0\"}", Encoding.UTF8, "application/json") }; }
            var page = Calls.Count;
            return new(HttpStatusCode.OK) { Content = new StringContent(new JsonObject { ["paging"] = new JsonObject { ["page"] = page, ["totalPages"] = 2, ["totalItems"] = 2 },
                ["items"] = new JsonArray(new JsonObject { ["Name"] = "Group " + page }) }.ToJsonString(), Encoding.UTF8, "application/json") };
        }
    }
    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(handler, false); }
    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    { var proxy = DispatchProxy.Create<T, HostSharedCrudTests.CrudProxy>(); ((HostSharedCrudTests.CrudProxy)(object)proxy).Handler = handler; return proxy; }
}
