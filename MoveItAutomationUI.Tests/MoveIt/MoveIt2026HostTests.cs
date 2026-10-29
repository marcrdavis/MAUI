using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Features.Editors.Hosts;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class MoveIt2026HostTests
{
    [Theory]
    [InlineData("2025.1", "2026.0", false)]
    [InlineData("2026.0", "2025.1", true)]
    [InlineData("2026.0.1.17", "", true)]
    [InlineData("18.0.0.17", "3.0", true)]
    [InlineData("18.0.1", "", true)]
    [InlineData("17.1.8", "2026.0", false)]
    [InlineData("19.0", "", true)]
    [InlineData("128.0", "", false)]
    [InlineData("2027.0", "", true)]
    [InlineData("2026", "", true)]
    [InlineData("MOVEit Automation 2026.0 (Build 17)", "", true)]
    [InlineData("", "2026.0", true)]
    [InlineData("unknown", "2026.0", false)]
    [InlineData("", "", false)]
    [InlineData("15.0.1", "2026.0", false)]
    public void MinimumVersionsUseTheEngineAndFailClosedWhenUnknown(string engine, string webAdmin, bool expected)
    {
        var info = new MoveItInfo { CentralVersion = engine, AppVersion = webAdmin };
        foreach (var feature in Enum.GetValues<MoveItFeature>()) Assert.Equal(expected, MoveItFeatureSupport.Supports(info, feature));
    }

    [Fact]
    public void OlderAndUnknownVersionsKeepAccessKeyControlsWithoutNewRoleChoices()
    {
        foreach (var version in new[] { "2025.1", "" })
        {
            var info = new MoveItInfo { CentralVersion = version };
            Assert.False(HostCapturedProfiles.TryGetSupported("OneDrive", info, out _));
            Assert.True(HostCapturedProfiles.TryGetSupported("S3", info, out var fields));
            Assert.DoesNotContain(fields, field => field.Key is "AuthMethod" or HostCloudAuthenticationSettings.S3ProfileKey);
            Assert.Equal("", fields.First(field => field.Key == "DefUsername").ConditionKey);
            Assert.Equal("", fields.First(field => field.Key == "DefPassword").ConditionKey);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void S3RolePayloadsRemoveAccessKeyAndAlternateProviderSettings(int mode)
    {
        var host = new JsonObject { ["AuthMethod"] = 0, ["DefUsername"] = "old-key", ["DefPassword"] = "PRIVATE_CIPHERTEXT",
            ["IsNonAmazonEndPoint"] = 1, ["S3AlternativeEndpoint"] = "storage.test", ["Retained"] = "native", ["ProxyPassword"] = "PRIVATE_PROXY" };
        var patch = new JsonObject { ["AuthMethod"] = mode };
        if (mode == 1) patch[HostCloudAuthenticationSettings.S3ProfileKey] = "profile-name";
        HostCapturedProfiles.Apply(host, patch, "S3");
        HostCapturedProfiles.PreserveUnsubmittedSecrets(host, patch, "S3");
        Assert.Equal(mode, host["AuthMethod"]!.GetValue<int>());
        Assert.False(host.ContainsKey("DefPassword")); Assert.False(host.ContainsKey("IsNonAmazonEndPoint"));
        Assert.False(host.ContainsKey("S3AlternativeEndpoint")); Assert.False(host.ContainsKey(HostCloudAuthenticationSettings.S3ProfileKey));
        Assert.Equal(mode == 1 ? "profile-name" : "", HostResourceContracts.Text(host, "DefUsername"));
        Assert.Equal("native", host["Retained"]!.ToString());
        Assert.True(host.ContainsKey("ProxyPassword")); Assert.Null(host["ProxyPassword"]);
        var browse = new JsonObject { ["logininfo"] = new JsonObject { ["username"] = "old-key", ["password"] = null } };
        HostCloudAuthenticationSettings.ApplyS3Browse(browse, host);
        Assert.Equal(mode, browse["authmethod"]!.GetValue<int>()); Assert.Equal("", browse["endpoint"]!.ToString());
        if (mode == 2) Assert.Equal("", browse["logininfo"]!["username"]!.ToString());
    }

    [Fact]
    public void ReturningToAccessKeysRequiresExplicitCredentialsAndProfileMayBeBlank()
    {
        var host = new JsonObject { ["AuthMethod"] = 2 };
        HostCapturedProfiles.Apply(host, new JsonObject { ["AuthMethod"] = 1, [HostCloudAuthenticationSettings.S3ProfileKey] = "" }, "S3");
        Assert.Equal("", host["DefUsername"]!.ToString());
        Assert.Throws<InvalidOperationException>(() => HostCapturedProfiles.Apply(host, new JsonObject { ["AuthMethod"] = 0 }, "S3"));
        HostCapturedProfiles.Apply(host, new JsonObject { ["AuthMethod"] = 0, ["DefUsername"] = "new-key", ["DefPassword"] = "  replacement  " }, "S3");
        Assert.Equal("  replacement  ", host["DefPassword"]!.ToString());
        Assert.Equal("new-key", host["DefUsername"]!.ToString());
    }

    private static JsonObject OneDrive(bool certificate) => new()
    {
        ["ID"] = "728053036", ["Name"] = "OneDrive", ["TenantID"] = "TenantID", ["DefUsername"] = "Username",
        ["OAuthClientID"] = "ClientID", ["UseOAuth"] = 1, ["Secure"] = 1, ["ProxyType"] = "None",
        ["DefConnTimeoutSecs"] = 60, ["DefDataTimeoutSecs"] = 60,
        [certificate ? "DefClientCertSHA1" : "OAuthClientSecret"] = certificate ? "AA BB" : "PRIVATE_SECRET",
        ["DefClientCertStore"] = certificate ? "My" : "", ["DefClientCertSubject"] = certificate ? "TLSClientCert" : ""
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OneDriveBrowseUsesTheCapturedAuthenticationShapeWithoutExposingSecrets(bool certificate)
    {
        var request = HostCloudAuthenticationSettings.OneDriveBrowse("728053036", OneDrive(certificate));
        Assert.False(request["showfiles"]!.GetValue<bool>()); Assert.Equal("", request["dirname"]!.ToString());
        Assert.Equal("ClientID", request["logininfo"]!["username"]!.ToString());
        Assert.Null(request["logininfo"]!["password"]); Assert.Equal("Username", request["username"]!.ToString());
        Assert.Equal("TenantID", request["tenantid"]!.ToString()); Assert.True(request.ContainsKey(""));
        Assert.Equal(certificate, request.ContainsKey("clientcertsha1"));
        Assert.DoesNotContain("PRIVATE_", request.ToJsonString());
    }

    [Fact]
    public void OneDriveModeChangesDoNotLeakHelperOrInactiveCredentialFields()
    {
        var host = OneDrive(true);
        var patch = new JsonObject { [HostCloudAuthenticationSettings.OneDriveModeKey] = "client-secret", ["OAuthClientSecret"] = "  new secret  " };
        HostCapturedProfiles.Apply(host, patch, "OneDrive");
        Assert.False(host.ContainsKey(HostCloudAuthenticationSettings.OneDriveModeKey));
        Assert.False(host.ContainsKey("DefClientCertSHA1")); Assert.Equal("  new secret  ", host["OAuthClientSecret"]!.ToString());
        HostCloudAuthenticationSettings.Verify(host, host, patch, "OneDrive");
        var certificate = new JsonObject { [HostCloudAuthenticationSettings.OneDriveModeKey] = "certificate",
            ["DefClientCertSHA1"] = "CC DD", ["DefClientCertStore"] = "My", ["DefClientCertSubject"] = "New Certificate" };
        HostCapturedProfiles.Apply(host, certificate, "OneDrive");
        HostCapturedProfiles.PreserveUnsubmittedSecrets(host, certificate, "OneDrive");
        Assert.False(host.ContainsKey("OAuthClientSecret")); Assert.Null(host["DefPassword"]); Assert.Equal("certificate", HostCloudAuthenticationSettings.OneDriveMode(host));
    }

    [Fact]
    public async Task OneDriveCreationUsesTheNativeShellAndVerifiesTheReturnedId()
    {
        JsonObject? sent = null;
        var rest = Stub<IMoveItRestClient>((method, args) => method.Name switch
        {
            nameof(IMoveItRestClient.CreateHostV0Async) => Post((JsonObject)args[0]!),
            nameof(IMoveItRestClient.GetHostAsync) => Task.FromResult(new JsonObject { ["OneDrive"] = new JsonObject { ["ID"] = "728053036", ["Name"] = "New OneDrive" } }),
            _ => throw new NotSupportedException(method.Name)
        });
        Task<JsonObject> Post(JsonObject request) { sent = request.DeepClone().AsObject(); return Task.FromResult(JsonNode.Parse("""{"id":"728053036","status":"success","errorcode":0}""")!.AsObject()); }
        var result = await HostV0Creation.CreateAsync(rest, "New OneDrive", "OneDrive", default);
        Assert.Equal("728053036", result.Id); Assert.True(sent!["OneDrive"] is JsonObject);
        var host = sent!["OneDrive"]!.AsObject(); Assert.Equal("", host["ID"]!.ToString());
        Assert.Equal(1, host["UseOAuth"]!.GetValue<int>()); Assert.True(host.ContainsKey("DefClientCertSHA1"));
        Assert.False(host.ContainsKey("OAuthClientSecret"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OneDriveTestAndSharedBrowseUseTheSameAuthContractAndReleaseCheckout(bool certificate)
    {
        var server = "https://onedrive-" + Guid.NewGuid().ToString("N") + ".test";
        using var scope = ObjectCheckout.ForServer(server);
        var host = OneDrive(certificate); var releases = 0; var requests = new List<JsonObject>();
        var rest = Stub<IMoveItRestClient>((method, args) => method.Name switch
        {
            nameof(IMoveItRestClient.GetHostAsync) or nameof(IMoveItRestClient.CheckoutHostV0Async) => Task.FromResult(new JsonObject { ["OneDrive"] = host.DeepClone() }),
            nameof(IMoveItRestClient.ReleaseHostCheckoutV0Async) => Release(),
            nameof(IMoveItRestClient.BrowseOneDriveHostV0Async) => Capture((JsonObject)args[0]!),
            _ => throw new NotSupportedException(method.Name)
        });
        Task Release() { releases++; return Task.CompletedTask; }
        Task<JsonObject> Browse() => Task.FromResult(JsonNode.Parse("""{"CurrentDir":"/","Dirs":{"Dir":[{"Name":"Documents","Path":"/Documents"}]}}""")!.AsObject());
        Task<JsonObject> Capture(JsonObject body) { requests.Add(body.DeepClone().AsObject()); return Browse(); }
        var sessions = Sessions(new MoveItSessionState { WebAdminUrl = server, Info = new MoveItInfo { CentralVersion = "2026.0" } });
        var tests = new HostConnectionTestService(rest, NullLogger<HostConnectionTestService>.Instance, sessions);
        var test = await tests.TestHostAsync("728053036");
        Assert.Equal(HostConnectionTestService.StatusPassed, test.Status);
        var browser = new AutomationHostBrowseService(rest, new TraditionalTaskHostProfileCatalog(), NullLogger<AutomationHostBrowseService>.Instance, sessions);
        var listing = await browser.BrowseAsync("728053036", "/");
        Assert.Contains(listing.Items, item => item.Name == "Documents");
        Assert.True(releases == 2 && requests.Count == 2);
        Assert.False(requests[0]["showfiles"]!.GetValue<bool>()); Assert.True(requests[1]["showfiles"]!.GetValue<bool>());
        Assert.All(requests, body => { Assert.Equal(certificate, body.ContainsKey("clientcertsha1")); Assert.DoesNotContain("PRIVATE_", body.ToJsonString()); });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OneDriveEditorUsesSharedFieldsAndNeverReturnsStoredSecrets(bool certificate)
    {
        var host = OneDrive(certificate);
        var common = new AutomationHost { Id = "728053036", Name = "OneDrive", TypeCode = "OneDrive", Type = "Microsoft OneDrive", CanEditDelete = true };
        var hosts = Stub<IAutomationHostService>((method, _) => method.Name switch
        {
            nameof(IAutomationHostService.GetHostAsync) => Task.FromResult(common),
            nameof(IAutomationHostService.GetHostDefinitionAsync) => Task.FromResult(new JsonObject { ["OneDrive"] = host.DeepClone() }),
            nameof(IAutomationHostService.GetHostConfigurationSnapshotAsync) => Task.FromResult(new HostConfigurationSnapshot(common.Id, common.Name, common.TypeCode, "<OneDrive ID=\"728053036\" />")),
            _ => throw new NotSupportedException(method.Name)
        });
        var tests = Stub<IHostConnectionTestService>((_, _) => true);
        var scripts = Stub<IAutomationScriptService>((method, _) => throw new NotSupportedException(method.Name));
        var sessions = Sessions(new MoveItSessionState { Info = new MoveItInfo { CentralVersion = "2026.0" } });
        var model = await new HostEditorService(hosts, tests, scripts, sessions: sessions).GetHostAsync(common.Id);
        Assert.True(model.CanEditConfiguration); Assert.DoesNotContain("PRIVATE_", JsonSerializer.Serialize(model));
        var fields = model.ConfigurationSections.SelectMany(section => section.Fields).ToArray();
        Assert.Equal(certificate ? "certificate" : "client-secret", fields.First(field => field.Key == HostCloudAuthenticationSettings.OneDriveModeKey).Value);
        Assert.Contains(fields, field => field.Key == "OAuthClientSecret" && field.Sensitive);
        Assert.Contains(fields, field => field.Key == "DefClientCertSHA1" && field.ValueType == "Certificate");
        Assert.DoesNotContain(fields, field => field.Key == "Secure");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task NativeS3TestUsesItsSavedAuthenticationMethodWithoutSendingInactiveSecrets(int mode)
    {
        var server = "https://s3-" + Guid.NewGuid().ToString("N") + ".test";
        using var scope = ObjectCheckout.ForServer(server);
        var host = new JsonObject { ["ID"] = "2026005", ["Name"] = "S3", ["AuthMethod"] = mode,
            ["DefPassword"] = "PRIVATE_CIPHERTEXT", ["DefBucket"] = "bucket", ["ProxyType"] = "None", ["Secure"] = 1 };
        if (mode != 2) host["DefUsername"] = mode == 1 ? "profile" : "key";
        JsonObject? sent = null; var releases = 0;
        var rest = Stub<IMoveItRestClient>((method, args) => method.Name switch
        {
            nameof(IMoveItRestClient.GetHostAsync) or nameof(IMoveItRestClient.CheckoutHostV0Async) => Task.FromResult(new JsonObject { ["S3"] = host.DeepClone() }),
            nameof(IMoveItRestClient.ReleaseHostCheckoutV0Async) => Release(),
            nameof(IMoveItRestClient.TestS3HostV0Async) => Capture((JsonObject)args[0]!),
            _ => throw new NotSupportedException(method.Name)
        });
        Task Release() { releases++; return Task.CompletedTask; }
        Task<JsonObject> Capture(JsonObject body) { sent = body.DeepClone().AsObject(); return Task.FromResult(JsonNode.Parse("""{"status":"success","errorcode":0}""")!.AsObject()); }
        var sessions = Sessions(new MoveItSessionState { WebAdminUrl = server, Info = new MoveItInfo { CentralVersion = "2026.0" } });
        var result = await new HostConnectionTestService(rest, NullLogger<HostConnectionTestService>.Instance, sessions).TestHostAsync("2026005");
        Assert.Equal(HostConnectionTestService.StatusPassed, result.Status); Assert.True(releases == 1);
        Assert.Equal(mode, sent!["AuthMethod"]!.GetValue<int>()); Assert.DoesNotContain("PRIVATE_", sent.ToJsonString());
        if (mode != 0) Assert.False(sent.ContainsKey("DefPassword"));
        if (mode == 2) Assert.False(sent.ContainsKey("DefUsername"));
    }

    [Theory]
    [InlineData("2025.1", false)]
    [InlineData("", false)]
    [InlineData("2026.0", true)]
    [InlineData("18.0.1", true)]
    [InlineData("17.1", false)]
    public async Task RestBoundaryGatesOneDriveAndIamBeforeAnyRequest(string version, bool allowed)
    {
        var state = new MoveItSessionState { WebAdminUrl = "https://moveit.test", AccessToken = "token",
            AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddHours(1), Info = new MoveItInfo { CentralVersion = version } };
        var sessions = Sessions(state);
        using var handler = new Handler();
        var rest = new MoveItRestClient(new Factory(handler), sessions, ConfigurationStartupState.Capture(new ConfigurationBuilder().Build()), null!, NullLogger<MoveItRestClient>.Instance);
        Func<Task>[] operations = [
            async () => { await rest.CreateHostV0Async(new JsonObject { ["OneDrive"] = OneDrive(false) }); },
            async () => { await rest.UpdateHostV0Async("728053036", new JsonObject { ["OneDrive"] = OneDrive(true) }); },
            async () => { await rest.BrowseOneDriveHostV0Async(HostCloudAuthenticationSettings.OneDriveBrowse("728053036", OneDrive(false))); },
            async () => { await rest.TestS3HostV0Async(new JsonObject { ["AuthMethod"] = 2 }); },
            async () => { await rest.BrowseS3HostV0Async(new JsonObject { ["authmethod"] = 1 }); }
        ];
        foreach (var operation in operations)
            if (allowed) await operation(); else await Assert.ThrowsAsync<InvalidOperationException>(operation);
        Assert.True(handler.Paths.Count == (allowed ? 5 : 0));
        if (allowed) Assert.Contains("/api/v0/hosts/onedrive/browse", handler.Paths);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"status\":\"success\",\"errorcode\":0,\"id\":\"728053036\"}") });
        }
    }
    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(handler, disposeHandler: false); }
    internal static IMoveItSessionService Sessions(MoveItSessionState state) => Stub<IMoveItSessionService>((method, _) => method.Name == nameof(IMoveItSessionService.Get) ? state : throw new NotSupportedException(method.Name));
    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, HostSharedCrudTests.CrudProxy>(); ((HostSharedCrudTests.CrudProxy)(object)proxy).Handler = handler; return proxy;
    }
}
