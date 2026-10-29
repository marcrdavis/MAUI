using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Features.Editors;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class ResourceGroupPermissionsTests
{
    private static JsonObject Group(string name) => new() { ["Name"] = name, ["Members"] = new JsonArray(new JsonObject { ["value"] = "123", ["t"] = "task" }) };
    private static JsonObject Acl(string group, string userGroup, int grant = 0)
        => new() { ["ResourceGroupName"] = group, ["UserGroupName"] = userGroup, ["ACLID"] = "0",
            ["Permissions"] = new JsonObject(ResourceGroupPermissionsService.PermissionKeys.Select(key => KeyValuePair.Create<string, JsonNode?>(key, JsonValue.Create(grant)))) };

    [Fact]
    public void CountsAssignmentsIndependentlyOfResourceMembershipAndKeepsEmptyGroups()
    {
        var yes = Acl("Finance", "Operators", 1); var no = Acl("finance", "Auditors"); no["ACLID"] = "1";
        no["Permissions"]!["TaskRun"] = 1; yes["Permissions"]!["HostAddEditDelete"] = 0;
        var snapshot = ResourceGroupPermissionsService.BuildSnapshot([Group("Finance"), Group("Empty")], [yes, no, Acl("Not visible", "Invisible", 1)], DateTimeOffset.UtcNow);
        Assert.Equal(new[] { "Empty", "Finance" }, snapshot.Groups.Select(group => group.Name));
        var empty = snapshot.Groups[0]; Assert.Empty(empty.UserGroups);
        Assert.All(empty.Permissions.Values, permission => { Assert.Equal("None", permission.State); Assert.Equal(0, permission.Total); });
        var finance = snapshot.Groups[1]; Assert.Equal(2, finance.UserGroups.Count);
        Assert.Equal("All", finance.Permissions["TaskRun"].State);
        Assert.Equal("None", finance.Permissions["HostAddEditDelete"].State);
        Assert.Equal("Some", finance.Permissions["TaskEditExisting"].State);
        Assert.Equal(1, finance.Permissions["TaskEditExisting"].Granted);
        Assert.Equal(new[] { "1", "0" }, finance.UserGroups.Select(grant => grant.AclId));
        Assert.Equal(new[] { "Auditors", "Operators" }, finance.UserGroups.Select(grant => grant.Name));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Name\":42}")]
    [InlineData("{\"Name\":\" \"}")]
    public void InvalidGroupsDoNotBecomeAnEmptySuccessfulView(string json)
        => Assert.Throws<InvalidOperationException>(() => ResourceGroupPermissionsService.BuildSnapshot([JsonNode.Parse(json)!.AsObject()], [], DateTimeOffset.UtcNow));

    [Fact]
    public void MissingOrInvalidGrantsAndDuplicateAssignmentsFailClosed()
    {
        var missing = Acl("A", "Users"); missing["Permissions"]!.AsObject().Remove("TaskRun");
        var malformed = Acl("A", "Users"); malformed["Permissions"]!["TaskRun"] = 2;
        var unnamed = Acl("A", " "); var missingPermissions = Acl("A", "Users"); missingPermissions.Remove("Permissions");
        foreach (var acl in new[] { missing, malformed, unnamed, missingPermissions })
            Assert.Throws<InvalidOperationException>(() => ResourceGroupPermissionsService.BuildSnapshot([Group("A")], [acl], DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => ResourceGroupPermissionsService.BuildSnapshot([Group("A"), Group("a")], [], DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => ResourceGroupPermissionsService.BuildSnapshot([Group("A")], [Acl("A", "Users"), Acl("a", "users")], DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("-1")] [InlineData("../0")] [InlineData("not-an-id")]
    public void MalformedVisibleAclIdsDoNotProduceAnEditableSnapshot(string? id)
    {
        var acl = Acl("A", "Users"); acl["ACLID"] = id;
        Assert.Throws<InvalidOperationException>(() => ResourceGroupPermissionsService.BuildSnapshot([Group("A")], [acl], DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ReusedAclIdentityAcrossVisibleGroupsIsRejectedButZeroRemainsValid()
    {
        Assert.Throws<InvalidOperationException>(() => ResourceGroupPermissionsService.BuildSnapshot([Group("A"), Group("B")],
            [Acl("A", "Users"), Acl("B", "Other")], DateTimeOffset.UtcNow));
        var snapshot = ResourceGroupPermissionsService.BuildSnapshot([Group("A")], [Acl("A", "Users")], DateTimeOffset.UtcNow);
        Assert.Equal("0", snapshot.Groups[0].UserGroups[0].AclId);
    }

    [Fact]
    public async Task CacheIsUserScopedAndFailedRefreshPreservesTheLastCompleteSnapshot()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var state = new MoveItSessionState { ServerKey = "server", Username = "first", Info = new MoveItInfo { IsAdmin = true } }; var calls = 0; var fail = false;
        var session = Stub<IMoveItSessionService>((method, _) => method.Name == nameof(IMoveItSessionService.Get) ? state : throw new NotSupportedException(method.Name));
        var rest = Stub<IMoveItRestClient>((method, _) =>
        {
            calls++;
            return method.Name switch
            {
                nameof(IMoveItRestClient.GetResourceGroupDefinitionsAsync) => Task.FromResult<IReadOnlyList<JsonObject>>([Group(state.Username)]),
                nameof(IMoveItRestClient.GetResourceGroupAclDefinitionsAsync) => fail ? Task.FromException<IReadOnlyList<JsonObject>>(new InvalidOperationException("ACL read failed")) : Task.FromResult<IReadOnlyList<JsonObject>>([]),
                _ => throw new InvalidOperationException("Read-only grid cannot check out or modify groups: " + method.Name)
            };
        });
        var service = new ResourceGroupPermissionsService(rest, session, cache);
        var first = await service.GetSnapshotAsync(); Assert.Same(first, await service.GetSnapshotAsync()); Assert.Equal(2, calls);
        fail = true; await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetSnapshotAsync(true));
        Assert.Same(first, await service.GetSnapshotAsync()); fail = false;
        state.Username = "second"; Assert.Equal("second", (await service.GetSnapshotAsync()).Groups[0].Name);
        state.Username = "first"; state.ServerKey = "other"; Assert.NotSame(first, await service.GetSnapshotAsync());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task RestReadsEveryPageUsingAuthenticatedGetsOnly(bool acls)
    {
        using var handler = new PageHandler(); var client = Client(handler);
        var rows = acls ? await client.GetResourceGroupAclDefinitionsAsync() : await client.GetResourceGroupDefinitionsAsync();
        Assert.Equal(new[] { "Page1", "Page2" }, rows.Select(row => row["Name"]!.ToString()));
        Assert.Equal(2, handler.Calls.Count);
        Assert.All(handler.Calls, call => Assert.StartsWith("GET /api/v1/resourcegroups" + (acls ? "/acls" : "") + "?page=", call));
        Assert.EndsWith("page=2&perPage=100", handler.Calls[1]);
    }

    [Theory]
    [InlineData("repeated")] [InlineData("incomplete")] [InlineData("changed")] [InlineData("invalid")]
    public async Task RestRejectsIncompleteOrInconsistentPages(string mode)
    {
        using var handler = new PageHandler { Mode = mode };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Client(handler).GetResourceGroupDefinitionsAsync());
        Assert.InRange(handler.Calls.Count, 1, 2);
    }

    [Fact]
    public void ExperimentalGridDefaultsOffAndSupportsConfigurationBinding()
    {
        Assert.False(new MauiEditorOptions().UseMauiGroupsAndPermissionsEditor.Value);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Editors:UseMauiGroupsAndPermissionsEditor:Value"] = "true" }).Build();
        Assert.True(config.GetSection("Editors").Get<MauiEditorOptions>()!.UseMauiGroupsAndPermissionsEditor.Value);
    }

    private sealed class PageHandler : HttpMessageHandler
    {
        public string Mode { get; init; } = "normal";
        public List<string> Calls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("Bearer test-token", request.Headers.Authorization!.ToString()); Assert.Null(request.Content);
            Calls.Add(request.Method + " " + request.RequestUri!.PathAndQuery);
            var page = Calls.Count;
            var json = new JsonObject
            {
                ["paging"] = new JsonObject { ["page"] = Mode == "repeated" ? 1 : page, ["totalPages"] = 2, ["totalItems"] = Mode == "changed" && page == 2 ? 3 : 2 },
                ["items"] = Mode == "invalid" ? new JsonArray(42) : Mode == "incomplete" && page == 2 ? new JsonArray() : new JsonArray(Group("Page" + page))
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") });
        }
    }
    private static MoveItRestClient Client(HttpMessageHandler handler)
    {
        var session = Stub<IMoveItSessionService>((method, _) => method.Name == nameof(IMoveItSessionService.Get)
            ? new MoveItSessionState { WebAdminUrl = "https://moveit.test", AccessToken = "test-token", AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) }
            : throw new NotSupportedException(method.Name));
        return new MoveItRestClient(new ClientFactory(handler), session, ConfigurationStartupState.Capture(new ConfigurationBuilder().Build()), null!, NullLogger<MoveItRestClient>.Instance);
    }
    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, disposeHandler: false); }
    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    { var proxy = DispatchProxy.Create<T, HostSharedCrudTests.CrudProxy>(); ((HostSharedCrudTests.CrudProxy)(object)proxy).Handler = handler; return proxy; }
}
