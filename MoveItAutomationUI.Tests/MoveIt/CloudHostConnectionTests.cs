using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class CloudHostConnectionTests
{
    [Theory]
    [InlineData(1, false)] [InlineData(1, true)]
    [InlineData(2, false)] [InlineData(2, true)]
    public async Task SharePointTestUsesOnlyTheSelectedAuthenticationFields(int mode, bool defaultSite)
    {
        var fixture = new Fixture("SharePoint", mode);
        fixture.Host["Site"] = defaultSite ? "" : "CertifyArchive";
        var result = await fixture.TestAsync();
        Assert.Equal(HostConnectionTestService.StatusPassed, result.Status);
        var request = Assert.Single(fixture.Requests);
        Assert.Equal(mode, request["UsingAppAuthentication"]!.GetValue<int>());
        Assert.Equal(fixture.Host["Site"]!.ToString(), request["Site"]!.ToString());
        Assert.Equal("42", request["ID"]!.ToString());
        Assert.Equal("client", request["ClientID"]!.ToString());
        Assert.False(request.ContainsKey("DefPassword"));
        Assert.False(request.ContainsKey("DefUsername"));
        if (mode == 1)
        {
            Assert.Equal("realm", request["RealmID"]!.ToString());
            Assert.True(request.ContainsKey("ClientSecret")); Assert.Null(request["ClientSecret"]);
            Assert.False(request.ContainsKey("TenantID"));
            Assert.False(request.ContainsKey("DefClientCertSHA1"));
            Assert.False(request.ContainsKey("DefClientCertStore"));
            Assert.False(request.ContainsKey("DefClientCertSubject"));
        }
        else
        {
            Assert.Equal("tenant-id", request["TenantID"]!.ToString());
            Assert.Equal("AA BB", request["DefClientCertSHA1"]!.ToString());
            Assert.False(request.ContainsKey("RealmID")); Assert.False(request.ContainsKey("ClientSecret"));
        }
        fixture.AssertUnchangedAndReleased();
    }

    [Theory]
    [InlineData(0)] [InlineData(1)]
    public async Task GoogleTestUsesHmacOrOAuthWithoutLeakingStoredCredentials(int mode)
    {
        var fixture = new Fixture("GoogleStorage", mode);
        var result = await fixture.TestAsync();
        Assert.Equal(HostConnectionTestService.StatusPassed, result.Status);
        var request = Assert.Single(fixture.Requests);
        Assert.Equal(mode, request["UseOAuth"]!.GetValue<int>());
        Assert.Equal("42", request["ID"]!.ToString());
        Assert.Equal("bucket", request["DefBucket"]!.ToString());
        if (mode == 1)
        {
            Assert.Equal("project", request["ProjectID"]!.ToString());
            Assert.Equal("oauth-client", request["OAuthClientID"]!.ToString());
            Assert.True(request.ContainsKey("OAuthClientSecret")); Assert.Null(request["OAuthClientSecret"]);
            Assert.False(request.ContainsKey("DefUsername")); Assert.False(request.ContainsKey("DefPassword"));
        }
        else
        {
            Assert.Equal("hmac-key", request["DefUsername"]!.ToString()); Assert.Null(request["DefPassword"]);
            Assert.False(request.ContainsKey("OAuthClientSecret"));
            Assert.False(request.ContainsKey("ProjectID")); Assert.False(request.ContainsKey("OAuthClientID"));
        }
        fixture.AssertUnchangedAndReleased();
    }

    [Fact]
    public async Task SharePointAppTestIgnoresCertificateValuesLeftByAnAuthenticationSwitch()
    {
        var clean = new Fixture("SharePoint", 1);
        var switched = new Fixture("SharePoint", 1);
        switched.Host["TenantID"] = "old-tenant-id";
        switched.Host["DefClientCertStore"] = "My";
        switched.Host["DefClientCertSubject"] = "Previous certificate";
        switched.Host["DefClientCertSHA1"] = "OLD CERTIFICATE";
        Assert.Equal(HostConnectionTestService.StatusPassed, (await clean.TestAsync()).Status);
        Assert.Equal(HostConnectionTestService.StatusPassed, (await switched.TestAsync()).Status);
        Assert.Equal(Assert.Single(clean.Requests).ToJsonString(), Assert.Single(switched.Requests).ToJsonString());
        clean.AssertUnchangedAndReleased(); switched.AssertUnchangedAndReleased();
    }

    [Theory]
    [InlineData("SharePoint", 1, "RealmID", "Realm ID")]
    [InlineData("SharePoint", 2, "TenantID", "Tenant ID")]
    [InlineData("SharePoint", 2, "DefClientCertSHA1", "Client Certificate SHA1")]
    [InlineData("GoogleStorage", 1, "ProjectID", "Project ID")]
    [InlineData("GoogleStorage", 1, "OAuthClientID", "OAuth Client ID")]
    public async Task MissingActiveAuthenticationFieldsDoNotSendAnIncompleteTest(string type, int mode, string field, string label)
    {
        var fixture = new Fixture(type, mode);
        fixture.Host.Remove(field);
        var result = await fixture.TestAsync();
        Assert.Equal(HostConnectionTestService.StatusIncomplete, result.Status);
        Assert.Contains(label, result.Message);
        Assert.Empty(fixture.Requests);
        fixture.AssertUnchangedAndReleased();
    }

    [Theory]
    [InlineData("SharePoint", "RealmID")]
    [InlineData("GoogleStorage", "ProjectID")]
    [InlineData("GoogleStorage", "OAuthClientID")]
    public async Task MissingCheckoutFieldsAreEnrichedBeforeTesting(string type, string field)
    {
        var fixture = new Fixture(type, 1) { OmitCheckoutField = field };
        var result = await fixture.TestAsync();
        Assert.Equal(HostConnectionTestService.StatusPassed, result.Status);
        Assert.Equal(fixture.Host[field]!.ToString(), Assert.Single(fixture.Requests)[field]!.ToString());
        fixture.AssertUnchangedAndReleased();
    }

    [Theory]
    [InlineData("SharePoint", false)] [InlineData("SharePoint", true)]
    [InlineData("GoogleStorage", false)] [InlineData("GoogleStorage", true)]
    public async Task NativeFailuresKeepTheExistingResponseHandlingAndReleaseCheckout(string type, bool throwError)
    {
        var fixture = new Fixture(type, 1) { Fail = true, ThrowError = throwError };
        var result = await fixture.TestAsync();
        Assert.Equal(HostConnectionTestService.StatusFailed, result.Status);
        Assert.Contains("Authentication failed", result.Message);
        Assert.Single(fixture.Requests);
        fixture.AssertUnchangedAndReleased();
    }

    [Fact]
    public async Task DeprecatedSharePointUserAccessIsStillUnsupported()
    {
        var fixture = new Fixture("SharePoint", 0);
        var result = await fixture.TestAsync();
        Assert.Equal(HostConnectionTestService.StatusUnsupported, result.Status);
        Assert.Contains("App Access or Certificate Access", result.Message);
        Assert.Empty(fixture.Requests);
        fixture.AssertUnchangedAndReleased();
    }

    private sealed class Fixture
    {
        private readonly string _type;
        private readonly IMoveItRestClient _rest;
        private string _original = "";
        private int _releases;
        public JsonObject Host { get; }
        public List<JsonObject> Requests { get; } = [];
        public string? OmitCheckoutField { get; init; }
        public bool Fail { get; init; }
        public bool ThrowError { get; init; }

        public Fixture(string type, int mode)
        {
            _type = type;
            Host = new JsonObject
            {
                ["ID"] = "42", ["Name"] = "Cloud Host", ["ProxyType"] = "None", ["Secure"] = 1,
                ["DefConnTimeoutSecs"] = 60, ["DefDataTimeoutSecs"] = 60,
                ["DefPassword"] = "PRIVATE_STORED_PASSWORD", ["ClientSecret"] = "PRIVATE_STORED_APP_SECRET",
                ["OAuthClientSecret"] = "PRIVATE_STORED_PRIVATE_KEY", ["DefUsername"] = "hmac-key",
                ["TenantName"] = "tenant", ["SiteUrlPrefix"] = "/sites/", ["Site"] = "site",
                ["UsingAppAuthentication"] = mode, ["ClientID"] = "client", ["DefDocumentLibrary"] = "Documents",
                ["DefDocumentLibraryGuid"] = "010207f2-17a8-4c5f-87ad-ae2b07d6bcaa",
                ["UseOAuth"] = mode, ["ProjectID"] = "project", ["OAuthClientID"] = "oauth-client", ["DefBucket"] = "bucket"
            };
            if (mode == 1) Host["RealmID"] = "realm";
            else
            {
                Host["TenantID"] = "tenant-id"; Host["DefClientCertStore"] = "My";
                Host["DefClientCertSubject"] = "Certificate"; Host["DefClientCertSHA1"] = "AA BB";
            }
            _rest = DispatchProxy.Create<IMoveItRestClient, HostSharedCrudTests.CrudProxy>();
            ((HostSharedCrudTests.CrudProxy)(object)_rest).Handler = (method, args) => method.Name switch
            {
                nameof(IMoveItRestClient.CheckoutHostV0Async) => Read(true),
                nameof(IMoveItRestClient.GetHostAsync) => Read(false),
                nameof(IMoveItRestClient.ReleaseHostCheckoutV0Async) => Release(),
                nameof(IMoveItRestClient.TestSharePointHostV0Async) when _type == "SharePoint" => Send((JsonObject)args[0]!),
                nameof(IMoveItRestClient.TestGoogleStorageHostV0Async) when _type == "GoogleStorage" => Send((JsonObject)args[0]!),
                _ => throw new NotSupportedException(method.Name)
            };
        }

        public async Task<HostConnectionTestResult> TestAsync()
        {
            _original = Host.ToJsonString();
            using var scope = ObjectCheckout.ForServer("https://cloud-test-" + Guid.NewGuid().ToString("N") + ".test");
            return await new HostConnectionTestService(_rest, NullLogger<HostConnectionTestService>.Instance).TestHostAsync("42");
        }

        public void AssertUnchangedAndReleased()
        {
            Assert.Equal(_original, Host.ToJsonString()); Assert.True(_releases == 1);
            Assert.All(Requests, request => Assert.DoesNotContain("PRIVATE_STORED", request.ToJsonString()));
        }

        private Task<JsonObject> Read(bool checkout)
        {
            var definition = Host.DeepClone().AsObject();
            if (checkout && OmitCheckoutField is { } field) definition.Remove(field);
            return Task.FromResult(new JsonObject { [_type] = definition });
        }
        private Task Release() { _releases++; return Task.CompletedTask; }
        private Task<JsonObject> Send(JsonObject request)
        {
            Requests.Add(request.DeepClone().AsObject());
            if (ThrowError) return Task.FromException<JsonObject>(new MoveItApiException("Authentication failed"));
            return Task.FromResult(Fail
                ? new JsonObject { ["status"] = "failed", ["errorcode"] = 1, ["message"] = "Authentication failed" }
                : new JsonObject { ["status"] = "success", ["errorcode"] = 0 });
        }
    }
}
