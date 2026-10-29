using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Tests.MoveIt;

// Exercise the real HTTP parser and shared save lifecycle. These are synthetic
// response-shape regressions, not a claim of live MOVEit acceptance testing.
public sealed class HostSaveResponseShapeTests
{
    public static IEnumerable<object[]> Types => HostV0CreationTests.Types;

    [Theory] [MemberData(nameof(Types))]
    public async Task InformationSavesFlatIndividualDefinitionsUsingExactInventoryIdentity(string type)
    {
        using var handler = new Handler(type);
        var session = Stub<IMoveItSessionService>((method, _) => method.Name == nameof(IMoveItSessionService.Get)
            ? new MoveItSessionState { WebAdminUrl = "https://moveit.test", AccessToken = "test-token", AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) }
            : throw new NotSupportedException(method.Name));
        var client = new MoveItRestClient(new ClientFactory(handler), session,
            ConfigurationStartupState.Capture(new ConfigurationBuilder().Build()), null!, NullLogger<MoveItRestClient>.Instance);
        var com = Stub<IMoveItComClient>((method, _) => throw new Exception("Information REST save must not use COM: " + method.Name));
        var saves = new HostDefinitionSaveService(client, com, null!, NullLogger<HostDefinitionSaveService>.Instance);
        var coordinator = new HostCrudCoordinator(client, com, saves);
        var revision = ObjectEditVersion.Named("Original", "Old");
        using var edit = ObjectEditVersion.Require("Host", "107355230", ObjectEditVersion.Create("Host", "107355230", revision));
        await coordinator.UpdateBasicDetailsAsync("107355230", "Renamed", "Updated");
        Assert.Equal(new[]
        {
            "GET /api/v1/hosts/107355230", "GET /api/v1/hosts", "POST /api/v0/hosts/107355230/checkout",
            "GET /api/v1/hosts/107355230", "GET /api/v1/hosts", "PUT /api/v0/hosts/107355230",
            "GET /api/v1/hosts/107355230", "GET /api/v1/hosts", "DELETE /api/v0/hosts/107355230/checkout"
        }, handler.Calls);
        Assert.Equal("Renamed", handler.Saved["Name"]!.ToString()); Assert.Equal("Updated", handler.Saved["Desc"]!.ToString());
        Assert.Null(handler.Submitted![type]!["Type"]);
        Assert.Equal(ObjectEditVersion.Create("Host", "107355230", ObjectEditVersion.Named("Renamed", "Updated")), ObjectEditVersion.SavedVersion);
    }

    [Theory]
    [InlineData("Share", HttpStatusCode.BadRequest)]
    [InlineData("siLock", HttpStatusCode.Forbidden)]
    [InlineData("SSHFTP", HttpStatusCode.UnprocessableEntity)]
    public async Task RealHttpPutRejectionIsSafeOnlyAfterConfirmedCheckoutRelease(string type, HttpStatusCode status)
    {
        using var handler = new Handler(type) { WriteStatus = status };
        var session = Stub<IMoveItSessionService>((method, _) => method.Name == nameof(IMoveItSessionService.Get)
            ? new MoveItSessionState { WebAdminUrl = "https://moveit.test", AccessToken = "test-token", AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) }
            : throw new NotSupportedException(method.Name));
        var client = new MoveItRestClient(new ClientFactory(handler), session,
            ConfigurationStartupState.Capture(new ConfigurationBuilder().Build()), null!, NullLogger<MoveItRestClient>.Instance);
        var com = Stub<IMoveItComClient>((method, _) => throw new NotSupportedException(method.Name));
        var saves = new HostDefinitionSaveService(client, com, null!, NullLogger<HostDefinitionSaveService>.Instance);
        var error = await Assert.ThrowsAsync<MoveItApiException>(() => new HostCrudCoordinator(client, com, saves)
            .UpdateBasicDetailsAsync("107355230", "Renamed", "Updated"));
        Assert.Equal(status, error.StatusCode); Assert.DoesNotContain("REPLACEMENT_SECRET", error.ToString());
        Assert.Equal("DELETE /api/v0/hosts/107355230/checkout", handler.Calls.Last());
        Assert.Equal(1, handler.Calls.Count(call => call.StartsWith("PUT ", StringComparison.Ordinal)));
        Assert.Equal("Original", handler.Saved["Name"]!.ToString());
    }

    private sealed class Handler : HttpMessageHandler
    {
        private readonly string _type;
        public HttpStatusCode? WriteStatus { get; init; }
        public JsonObject Saved { get; private set; }
        public JsonObject? Submitted { get; private set; }
        public List<string> Calls { get; } = [];
        public Handler(string type)
        {
            _type = type;
            using var stream = typeof(HostV0CreationTests).Assembly.GetManifestResourceStream("MoveItAutomationUI.Tests.MoveIt.Fixtures.HostV0CreationCaptures.json")!;
            Saved = JsonNode.Parse(stream)![type]!.DeepClone().AsObject();
            Saved["ID"] = "107355230"; Saved["Name"] = "Original"; Saved["Desc"] = "Old";
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("Bearer test-token", request.Headers.Authorization!.ToString());
            var path = request.RequestUri!.AbsolutePath; Calls.Add(request.Method + " " + path);
            if (request.Method == HttpMethod.Get && path == "/api/v1/hosts/107355230") return Reply(Saved);
            if (request.Method == HttpMethod.Get && path == "/api/v1/hosts") return Reply(new JsonArray(Wrap()));
            if (request.Method == HttpMethod.Post && path.EndsWith("/checkout", StringComparison.Ordinal)) return Reply(Wrap());
            if (request.Method == HttpMethod.Put && path == "/api/v0/hosts/107355230")
            {
                Submitted = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
                if (WriteStatus is { } status) return new HttpResponseMessage(status)
                    { Content = new StringContent("{\"errors\":[{\"message\":\"REPLACEMENT_SECRET\"}]}", Encoding.UTF8, "application/json") };
                Saved = Submitted[_type]!.DeepClone().AsObject();
                return Reply(JsonNode.Parse("""{"id":"107355230","status":"success","errorcode":0,"refreshRequired":true}""")!);
            }
            if (request.Method == HttpMethod.Delete && path.EndsWith("/checkout", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            throw new InvalidOperationException("Unexpected HTTP call: " + request.Method + " " + path);
        }
        private JsonObject Wrap() => new() { [_type] = Saved.DeepClone() };
        private static HttpResponseMessage Reply(JsonNode value) => new(HttpStatusCode.OK)
        { Content = new StringContent(value.ToJsonString(), Encoding.UTF8, "application/json") };
    }
    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, disposeHandler: false); }
    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    { var value = DispatchProxy.Create<T, Proxy>(); ((Proxy)(object)value).Handler = handler; return value; }
    public class Proxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args ?? []);
    }
}
