using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Hosts;
using MoveItAutomationUI.Services.MoveIt.Tasks.AdvancedTasks;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class LocalTaskAndBrowseTests
{
    [Theory]
    [InlineData("Source", false)] [InlineData("Destination", false)]
    [InlineData("Source", true)] [InlineData("Destination", true)]
    public void LocalStepsComposeNewAndChangedHostsWithoutNetworkState(string kind, bool changeHost)
    {
        var task = JsonNode.Parse("""{"ID":"100","steps":[],"NativeTask":"keep"}""")!.AsObject();
        if (changeHost) task["steps"]!.AsArray().Add(new JsonObject { [kind] = new JsonObject
        { ["ID"] = "11", ["Type"] = "FTP", ["HostID"] = "42", ["Password"] = "PRIVATE_OLD_SECRET", ["Username"] = "old", ["FileMask"] = "*.txt" } });
        var patch = LocalPatch(kind);
        var request = new JsonObject { ["kind"] = kind, ["id"] = "11", ["index"] = changeHost ? 0 : -1, ["patch"] = patch };
        new TraditionalTaskDefinitionComposer(new TraditionalTaskHostProfileCatalog()).ApplyEditorSteps(task, new JsonArray(request));
        var saved = task["steps"]![0]![kind]!.AsObject();
        Assert.Equal("0", saved["HostID"]!.GetValue<string>());
        Assert.Equal(@"C:\files\[YYYY]", saved["Path"]!.GetValue<string>());
        Assert.Equal(1, saved["UseDefRetryCount"]!.GetValue<int>());
        Assert.False(saved.ContainsKey("Username")); Assert.False(saved.ContainsKey("Password"));
        Assert.False(saved.ContainsKey("UseDefWinCopyFileAPI"));
        Assert.Equal("keep", task["NativeTask"]!.GetValue<string>());
        if (kind == "Source") Assert.Equal("*.*", saved["FileMask"]!.GetValue<string>());
        else Assert.Equal(1, saved["UseDefPreserveFileTimestamp"]!.GetValue<int>());
    }

    [Fact]
    public void AdvancedLocalSourceAndLoopDestinationReuseTheSharedComposer()
    {
        var task = JsonNode.Parse("""{"ID":"100","TT":"COS","NextEID":11,"steps":[]}""")!.AsObject();
        var flow = JsonNode.Parse("""[{"key":"source","kind":"Source"},{"key":"loop","kind":"For","steps":[{"key":"destination","kind":"Destination"}]}]""")!.AsArray();
        var patches = new JsonArray(new JsonObject { ["clientKey"] = "source", ["kind"] = "Source", ["patch"] = LocalPatch("Source") },
            new JsonObject { ["clientKey"] = "destination", ["kind"] = "Destination", ["patch"] = LocalPatch("Destination") });
        AdvancedTaskDefinition.Apply(task, flow, patches, null, new TraditionalTaskDefinitionComposer(new TraditionalTaskHostProfileCatalog()));
        Assert.Equal("*.*", task["steps"]![0]!["Source"]!["FileMask"]!.GetValue<string>());
        Assert.Equal("0", task["steps"]![1]!["For"]!["steps"]![0]!["Destination"]!["HostID"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("HostID", "42")] [InlineData("RescanSecs", "-1")]
    [InlineData("Password", "unrelated")] [InlineData("UseDefWinCopyFileAPI", "1")]
    public void LocalStepInvalidIdentityOrNetworkFieldsCannotBeSaved(string key, string value)
    {
        var patch = LocalPatch("Source"); patch[key] = value;
        Assert.Throws<InvalidOperationException>(() => new TraditionalTaskDefinitionComposer(new TraditionalTaskHostProfileCatalog())
            .ApplyEditorSteps(new JsonObject(), new JsonArray(new JsonObject { ["kind"] = "Source", ["index"] = -1, ["patch"] = patch })));
    }

    public static IEnumerable<object[]> BrowseModes()
    {
        foreach (var type in new[] { "FTP", "SSHFTP", "S3", "AzureBlob", "GoogleStorage", "SharePoint" })
        {
            HostCapturedProfiles.TryGet(type, out var fields);
            foreach (var proxy in fields.First(field => field.Key == "ProxyType").Choices!)
            {
                yield return new object[] { type, proxy.Value, false };
                if (type is "GoogleStorage" or "SharePoint") yield return new object[] { type, proxy.Value, true };
            }
        }
    }

    [Theory]
    [MemberData(nameof(BrowseModes))]
    public async Task TaskAndOperationalBrowseShareSupportedProxyAndAuthenticationPayloads(string type, string proxy, bool alternateAuth)
    {
        var host = Host(type, proxy, alternateAuth);
        var original = host.ToJsonString();
        var requests = new List<JsonObject>(); var releases = 0;
        var rest = Rest(type, host, requests, () => releases++);
        var editor = new TraditionalTaskEditorFolderBrowseService(rest, NullLogger<TraditionalTaskEditorFolderBrowseService>.Instance);
        await editor.BrowseAsync("100", "42", "Source", "/", bucket: "bucket", container: "container", documentLibrary: "Documents", documentLibraryGuid: LibraryGuid);
        var operational = new AutomationHostBrowseService(rest, new TraditionalTaskHostProfileCatalog(), NullLogger<AutomationHostBrowseService>.Instance);
        await operational.BrowseAsync("42", "/", rootName: type == "SharePoint" ? "Documents" : "bucket", rootId: LibraryGuid);
        Assert.Equal(2, requests.Count); Assert.Equal(2, releases);
        foreach (var request in requests)
        {
            var login = request["logininfo"]!.AsObject();
            Assert.Equal(proxy, login["proxytype"]!.GetValue<string>());
            if (proxy != "None")
            {
                Assert.Equal("proxy.example.test", login["proxyhost"]!.GetValue<string>());
                Assert.Equal(8443, login["proxyport"]!.GetValue<int>());
                Assert.Null(login["proxypassword"]);
            }
            Assert.DoesNotContain("PRIVATE_STORED", request.ToJsonString());
            if (type == "GoogleStorage")
            {
                Assert.Equal(alternateAuth, request["useoauth"]!.GetValue<bool>());
                Assert.Null(request["oauthclientsecret"]);
                if (alternateAuth)
                {
                    Assert.Equal("oauth-client", request["oauthclientid"]!.GetValue<string>());
                    Assert.Equal("project", request["projectid"]!.GetValue<string>());
                    Assert.Null(login["username"]); Assert.Null(login["password"]);
                }
                else Assert.Equal("account", login["username"]!.GetValue<string>());
            }
            if (type == "SharePoint")
            {
                Assert.Equal("", request["site"]!.GetValue<string>());
                Assert.Equal(alternateAuth ? 1 : 2, login["authenticationType"]!.GetValue<int>());
                if (alternateAuth) { Assert.Equal("realm", login["realmid"]!.GetValue<string>()); Assert.Null(login["clientsecret"]); }
                else Assert.False(login.ContainsKey("clientsecret"));
            }
        }
        Assert.Equal(original, host.ToJsonString());
    }

    [Fact]
    public async Task FailedBrowseStillReleasesCheckoutWithoutRetrying()
    {
        var host = Host("GoogleStorage", "HTTPS", true); var requests = new List<JsonObject>(); var releases = 0;
        var rest = Rest("GoogleStorage", host, requests, () => releases++, fail: true);
        await Assert.ThrowsAsync<MoveItApiException>(() => new TraditionalTaskEditorFolderBrowseService(rest,
            NullLogger<TraditionalTaskEditorFolderBrowseService>.Instance).BrowseAsync("100", "42", "Source", "/", bucket: "bucket"));
        Assert.Single(requests); Assert.Equal(1, releases);
    }

    [Fact]
    public void HostPickerAllowsExplicitReplacementSecretsButNeverCopiesStoredOnes()
    {
        var host = Host("SharePoint", "HTTPS", true);
        var request = HostSharePointBrowseContract.Build("42", host, replacements: new JsonObject
        { ["ClientSecret"] = "  replacement  ", ["ProxyPassword"] = "new proxy" });
        Assert.Equal("  replacement  ", request["logininfo"]!["clientsecret"]!.GetValue<string>());
        Assert.Equal("new proxy", request["logininfo"]!["proxypassword"]!.GetValue<string>());
        Assert.DoesNotContain("PRIVATE_STORED", request.ToJsonString());
        host["UsingAppAuthentication"] = 0;
        Assert.Throws<InvalidOperationException>(() => HostSharePointBrowseContract.Build("42", host));
    }

    private const string LibraryGuid = "11111111-2222-3333-4444-555555555555";
    private static JsonObject LocalPatch(string kind)
    {
        var patch = new JsonObject { ["ID"] = "11", ["HostID"] = "0", ["Type"] = "FileSystem", ["Path"] = @"C:\files\[YYYY]" };
        if (kind == "Source") patch["FileMask"] = "*.*";
        else { patch["FileName"] = "[OrigName]"; patch["UseOrigName"] = 1; }
        return patch;
    }

    private static JsonObject Host(string type, string proxy, bool alternateAuth) => new()
    {
        ["ID"] = "42", ["Name"] = "Host", ["Host"] = "host.example.test", ["Port"] = 22,
        ["Secure"] = type == "FTP" ? "on" : "1", ["DefUsername"] = type == "GoogleStorage" && alternateAuth ? "" : "account", ["DefPassword"] = "PRIVATE_STORED_PASSWORD",
        ["DefConnTimeoutSecs"] = 60, ["DefDataTimeoutSecs"] = 60, ["ProxyType"] = proxy,
        ["ProxyHost"] = "proxy.example.test", ["ProxyPort"] = 8443, ["ProxyUsername"] = "proxy-user", ["ProxyPassword"] = "PRIVATE_STORED_PROXY",
        ["ProxyIgnoreCertErrors"] = 1, ["UseOAuth"] = alternateAuth ? 1 : 0, ["ProjectID"] = "project", ["OAuthClientID"] = "oauth-client",
        ["OAuthClientSecret"] = "PRIVATE_STORED_PRIVATE_KEY", ["UsingAppAuthentication"] = alternateAuth ? 1 : 2,
        ["TenantName"] = "tenant", ["ClientID"] = "client", ["RealmID"] = "realm", ["ClientSecret"] = "PRIVATE_STORED_CLIENT_SECRET",
        ["TenantID"] = "tenant-id", ["Site"] = "", ["SiteUrlPrefix"] = "/sites/", ["DefClientCertStore"] = "My",
        ["DefClientCertSubject"] = "Certificate", ["DefClientCertSHA1"] = "AA BB", ["DefDocumentLibraryGuid"] = LibraryGuid,
        ["DefBucket"] = "bucket", ["DefContainer"] = "container", ["IsNonAmazonEndPoint"] = 0
    };

    private static IMoveItRestClient Rest(string type, JsonObject host, List<JsonObject> requests, Action release, bool fail = false)
    {
        var rest = DispatchProxy.Create<IMoveItRestClient, HostSharedCrudTests.CrudProxy>();
        ((HostSharedCrudTests.CrudProxy)(object)rest).Handler = (method, args) => method.Name switch
        {
            nameof(IMoveItRestClient.CheckoutHostV0Async) or nameof(IMoveItRestClient.GetHostAsync) => Task.FromResult(new JsonObject { [type] = host.DeepClone() }),
            nameof(IMoveItRestClient.ReleaseHostCheckoutV0Async) => Released(),
            _ when method.Name.StartsWith("Browse", StringComparison.Ordinal) => Browse((JsonObject)args[0]!),
            _ => throw new NotSupportedException(method.Name)
        };
        Task Released() { release(); return Task.CompletedTask; }
        Task<JsonObject> Browse(JsonObject request)
        {
            requests.Add(request.DeepClone().AsObject());
            return fail ? Task.FromException<JsonObject>(new MoveItApiException("Browse rejected"))
                : Task.FromResult(JsonNode.Parse("""{"CurrentDir":"/","Files":{"File":[{"Name":"file.txt","Size":12}]}}""")!.AsObject());
        }
        return rest;
    }
}
