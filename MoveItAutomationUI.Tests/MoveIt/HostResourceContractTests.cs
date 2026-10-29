using System.Reflection;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class HostResourceContractTests
{
    [Fact]
    public void CertificateInventoryHandlesSingleRecordsAndExcludesExpiredCertificates()
    {
        var cert = new JsonObject { ["Store"] = "My", ["IssuedTo"] = "Certificate", ["SHA1Thumbprint"] = "AA BB", ["xIsExpired"] = false };
        var response = new JsonObject { ["Certs"] = new JsonObject { ["Cert"] = cert.DeepClone() } };
        var items = HostResourceContracts.Certificates(response);
        Assert.Single(items);
        HostResourceContracts.ValidateCertificate(new JsonObject { ["DefClientCertStore"] = "my", ["DefClientCertSubject"] = "Certificate", ["DefClientCertSHA1"] = "aabb" }, items);
        cert["xIsExpired"] = true;
        response["Certs"]!["Cert"] = new JsonArray(cert);
        Assert.Empty(HostResourceContracts.Certificates(response));
    }

    [Fact]
    public void LibraryAndCertificateReferencesRejectIncompletePairs()
    {
        Assert.Throws<InvalidOperationException>(() => HostResourceContracts.ValidateAtomicFields(new JsonObject { ["DefClientCertSHA1"] = "AA BB" }));
        Assert.Throws<InvalidOperationException>(() => HostResourceContracts.ValidateAtomicFields(new JsonObject { ["DefDocumentLibrary"] = "Documents" }));
        Assert.Throws<InvalidOperationException>(() => HostResourceContracts.ValidateAtomicFields(new JsonObject { ["DefDocumentLibrary"] = "Documents", ["DefDocumentLibraryGuid"] = "bad" }));
        HostResourceContracts.ValidateCertificate(new JsonObject { ["DefClientCertSHA1"] = "", ["DefClientCertStore"] = "", ["DefClientCertSubject"] = "" }, Array.Empty<HostTlsCertificateOption>());
    }

    [Fact]
    public async Task DocumentLibraryBrowseUsesDraftSettingsWithoutMutatingTheHostOrSendingSecrets()
    {
        var raw = new JsonObject { ["ID"] = "42", ["UsingAppAuthentication"] = 2, ["ProxyType"] = "None", ["Secure"] = 1,
            ["TenantName"] = "tenant", ["Site"] = "old-site", ["SiteUrlPrefix"] = "/sites/", ["ClientID"] = "client", ["TenantID"] = "tenant-id",
            ["DefClientCertStore"] = "My", ["DefClientCertSubject"] = "Certificate", ["DefClientCertSHA1"] = "AA BB", ["ClientSecret"] = "PRIVATE_SAVED_SECRET",
            ["DefConnTimeoutSecs"] = 60, ["DefDataTimeoutSecs"] = 60, ["DefDocumentLibraryGuid"] = "11111111-2222-3333-4444-555555555555" };
        JsonObject? submitted = null;
        var rest = Stub<IMoveItRestClient>((method, args) => method.Name switch
        {
            nameof(IMoveItRestClient.GetHostAsync) => Task.FromResult(new JsonObject { ["SharePoint"] = raw.DeepClone() }),
            nameof(IMoveItRestClient.BrowseSharePointHostV0Async) => Browse((JsonObject)args[0]!),
            _ => throw new NotSupportedException(method.Name)
        });
        Task<JsonObject> Browse(JsonObject request)
        {
            submitted = request;
            return Task.FromResult(new JsonObject { ["Buckets"] = new JsonObject { ["Bucket"] = new JsonObject
            { ["Name"] = "Documents", ["GUID"] = "11111111-2222-3333-4444-555555555555" } } });
        }
        var coordinator = new HostCrudCoordinator(rest, Stub<IMoveItComClient>((_, _) => throw new InvalidOperationException("No COM write allowed")),
            Stub<IHostDefinitionSaveService>((_, _) => throw new InvalidOperationException("No save allowed")));
        var result = await coordinator.BrowseDocumentLibrariesAsync(new("42", new JsonObject { ["Site"] = "new-site" }));
        Assert.Single(result);
        Assert.Equal("new-site", submitted!["site"]!.GetValue<string>());
        Assert.Equal("old-site", raw["Site"]!.GetValue<string>());
        Assert.False(submitted["showfiles"]!.GetValue<bool>());
        Assert.Null(submitted["taskid"]); Assert.Null(submitted["stepid"]);
        Assert.Equal(2, submitted["logininfo"]!["authenticationType"]!.GetValue<int>());
        Assert.DoesNotContain("PRIVATE_SAVED_SECRET", submitted.ToJsonString());
        Assert.False(submitted["logininfo"]!.AsObject().ContainsKey("clientsecret"));
        raw["UsingAppAuthentication"] = 1;
        raw["RealmID"] = "realm";
        var app = HostSharePointBrowseContract.Build("42", raw);
        Assert.Equal(1, app["logininfo"]!["authenticationType"]!.GetValue<int>());
        Assert.Null(app["logininfo"]!["clientsecret"]);
        Assert.DoesNotContain("PRIVATE_SAVED_SECRET", app.ToJsonString());
        raw["UsingAppAuthentication"] = 2; raw["ProxyType"] = "HTTPS";
        // A selected proxy still needs its connection fields; no browse call is guessed.
        Assert.Throws<InvalidOperationException>(() => HostSharePointBrowseContract.Build("42", raw));
    }

    [Fact]
    public void SftpAlgorithmOrderAndUnknownNamesArePreserved()
    {
        var host = new JsonObject();
        HostCapturedProfiles.Apply(host, new JsonObject { ["EncList"] = "custom-cipher,aes128-ctr,aes256-ctr", ["KexAlgs"] = "gss-test+abc==,curve25519-sha256" }, "SSHFTP");
        Assert.Equal("custom-cipher,aes128-ctr,aes256-ctr", host["EncList"]!.GetValue<string>());
        Assert.Equal("gss-test+abc==,curve25519-sha256", host["KexAlgs"]!.GetValue<string>());
        var catalog = HostSshAlgorithmTests.Catalog();
        Assert.Equal(6, catalog.Encryption.Count(choice => choice.DefaultEnabled));
        Assert.Equal(13, catalog.KeyExchange.Count(choice => choice.DefaultEnabled));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("on")]
    [InlineData("on-ccc")]
    [InlineData("implicit")]
    [InlineData("implicit-ccc")]
    public void FtpTlsModesUseCapturedNativeValues(string mode)
    {
        var host = new JsonObject();
        HostCapturedProfiles.Apply(host, new JsonObject { ["Secure"] = mode }, "FTP");
        Assert.Equal(mode, host["Secure"]!.GetValue<string>());
        Assert.Throws<InvalidOperationException>(() => HostCapturedProfiles.Apply(host, new JsonObject { ["Secure"] = "unknown-mode" }, "FTP"));
    }

    [Fact]
    public void FtpPatchesRespectNativePropertyCasingAndSuppressEverySecretVariant()
    {
        var host = new JsonObject { ["host"] = "old", ["defPassword"] = "PRIVATE_CIPHER", ["ProxyPassword"] = "PRIVATE_PROXY", ["proxypassword"] = "SECOND_CIPHER" };
        HostCapturedProfiles.Apply(host, new JsonObject { ["Host"] = "new" }, "FTP");
        HostCapturedProfiles.PreserveUnsubmittedSecrets(host, new JsonObject(), "FTP");
        Assert.Equal("new", host["host"]!.GetValue<string>());
        Assert.False(host.ContainsKey("Host"));
        Assert.Null(host["defPassword"]);
        Assert.DoesNotContain("PRIVATE", host.ToJsonString());
        Assert.DoesNotContain("SECOND_CIPHER", host.ToJsonString());
    }

    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, HostSharedCrudTests.CrudProxy>();
        ((HostSharedCrudTests.CrudProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}
