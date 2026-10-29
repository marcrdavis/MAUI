using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class KeyCertificateExportTests
{
    private static readonly AutomationKeyCertificate Tls = new()
    { Kind = KeyCertificateKind.Tls, Name = "NewTLS", Store = "My", Issuer = "NewTLS", IssuedTo = "NewTLS", SerialNumber = "B7 49 D4 6A", Sha1Thumbprint = "676F B324 CD7C D227" };

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TlsPayloadMatchesSuppliedNativeContract(bool secret)
    {
        var body = KeyCertificateExport.Compose(Tls, new() { IncludePrivateKey = secret, Password = "password", ConfirmPassword = "password" });
        Assert.Equal("My", body["store"]!.ToString()); Assert.Equal("NewTLS", body["issuer"]!.ToString());
        Assert.Equal("B7 49 D4 6A", body["serialnum"]!.ToString()); Assert.Equal(Tls.Sha1Thumbprint, body["sha1"]!.ToString());
        Assert.Equal("NewTLS", body["subject"]!.ToString()); Assert.Equal(secret, body["exportprivatekey"]!.GetValue<bool>());
        Assert.Equal(secret ? "password" : "", body["password"]!.ToString()); Assert.True(body.Count == 8);
    }
    [Theory]
    [InlineData("", "")] [InlineData("one", "two")]
    public void TlsPrivateRequiresMatchingPassword(string password, string confirm)
        => Assert.Throws<InvalidOperationException>(() => KeyCertificateExport.Compose(Tls, new() { IncludePrivateKey = true, Password = password, ConfirmPassword = confirm }));

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PgpPayloadHasNoPasswordOrTlsFields(bool secret)
    {
        var item = new AutomationKeyCertificate { Kind = KeyCertificateKind.Pgp, Id = "0x9B4C7DA7", Name = "key", PublicPrivate = "Private" };
        var body = KeyCertificateExport.Compose(item, new() { IncludePrivateKey = secret, Password = "must-not-send", ConfirmPassword = "different" });
        Assert.True(body.Count == 3); Assert.Equal(item.Id, body["id"]!.ToString()); Assert.Equal("key", body["filename"]!.ToString());
        Assert.Equal(secret, body["exportsecret"]!.GetValue<bool>()); Assert.Null(body["password"]);
    }
    [Fact]
    public void PublicPgpCannotExportSecretAndSshHasNoExport()
    {
        Assert.Throws<InvalidOperationException>(() => KeyCertificateExport.Compose(new() { Kind = KeyCertificateKind.Pgp, PublicPrivate = "Public" }, new() { IncludePrivateKey = true }));
        Assert.Throws<InvalidOperationException>(() => KeyCertificateExport.Compose(new() { Kind = KeyCertificateKind.Ssh }, new()));
    }
    [Theory]
    [InlineData("Public", false)] [InlineData("pair", true)] [InlineData("Pair", true)] [InlineData("Private", true)]
    public void PgpPrivateAvailabilityUsesTheNativePublicPairValue(string publicPrivate, bool available)
    {
        var item = new AutomationKeyCertificate { Kind = KeyCertificateKind.Pgp, Id = "0x9B4C7DA7", Name = "key", PublicPrivate = publicPrivate };
        Assert.Equal(available, KeyCertificateExport.CanIncludePrivateKey(item));
        if (available)
        {
            var body = KeyCertificateExport.Compose(item, new() { IncludePrivateKey = true });
            Assert.True(body["exportsecret"]!.GetValue<bool>()); Assert.Null(body["password"]);
        }
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void TlsBase64AndBinaryProduceTheSameDownload(bool secret)
    {
        byte[] binary = [0x30, 0x82, 0x01, 0xff];
        var encoded = Encoding.ASCII.GetBytes(Convert.ToBase64String(binary));
        Assert.Equal(binary, KeyCertificateExport.Download(Tls, secret, encoded).Bytes);
        Assert.Equal(binary, KeyCertificateExport.Download(Tls, secret, binary).Bytes);
        Assert.EndsWith(secret ? ".pfx" : ".cer", KeyCertificateExport.Download(Tls, secret, encoded).FileName);
    }
    [Fact]
    public void PgpArmorIsPreservedAndInvalidSuccessfulDownloadsDoNotLeakMaterial()
    {
        var item = new AutomationKeyCertificate { Kind = KeyCertificateKind.Pgp, Name = "../Bad:Name" };
        var armor = Encoding.UTF8.GetBytes("-----BEGIN PGP PUBLIC KEY BLOCK-----\r\nexample\r\n-----END PGP PUBLIC KEY BLOCK-----");
        var result = KeyCertificateExport.Download(item, false, armor);
        Assert.Equal(armor, result.Bytes); Assert.EndsWith(".asc", result.FileName); Assert.DoesNotContain("/", result.FileName); Assert.DoesNotContain(":", result.FileName);
        var error = Assert.Throws<MoveItApiException>(() => KeyCertificateExport.Download(Tls, true, Encoding.UTF8.GetBytes("{\"secret\":\"PRIVATE_MATERIAL\"}")));
        Assert.DoesNotContain("PRIVATE_MATERIAL", error.ToString());
    }
    [Fact]
    public void EndpointUsesEditPermissionAndAntiforgery()
    {
        var method = typeof(ResourcesController).GetMethod(nameof(ResourcesController.ExportKeyCertificate))!;
        var rule = method.GetCustomAttribute<MoveItObjectPermissionAttribute>()!;
        Assert.Equal("Edit", rule.Operation); Assert.Equal("RowId", rule.Ids);
        Assert.NotNull(method.GetCustomAttribute<Microsoft.AspNetCore.Mvc.ValidateAntiForgeryTokenAttribute>());
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task RealHttpExportPreservesAttachmentAndExactV0Route(bool tls)
    {
        using var handler = new ExportHandler();
        var session = DispatchProxy.Create<IMoveItSessionService, SessionProxy>();
        var client = new MoveItRestClient(new Factory(handler), session, ConfigurationStartupState.Capture(new ConfigurationBuilder().Build()), null!, NullLogger<MoveItRestClient>.Instance);
        var body = new JsonObject { ["filename"] = "key", ["exportsecret"] = true };
        var result = tls ? await client.ExportTlsCertificateV0Async(body) : await client.ExportPgpKeyV0Async(body);
        Assert.Equal(handler.Bytes, result); Assert.Equal(tls ? "/api/v0/sslcerts/export" : "/api/v0/pgpkeys/export", handler.Path);
        Assert.Equal(body.ToJsonString(), handler.Body);
        handler.Fail = true;
        var error = await Assert.ThrowsAsync<MoveItApiException>(() => client.ExportPgpKeyV0Async(body));
        Assert.DoesNotContain("PRIVATE_MATERIAL", error.ToString());
    }
    private sealed class ExportHandler : HttpMessageHandler
    {
        public byte[] Bytes { get; } = [0x30, 0x82, 0xff];
        public string? Path { get; private set; }
        public string? Body { get; private set; }
        public bool Fail { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Post, request.Method); Assert.Equal("Bearer test-token", request.Headers.Authorization!.ToString());
            Assert.Contains(request.Headers.Accept, value => value.MediaType == "*/*");
            Path = request.RequestUri!.AbsolutePath; Body = await request.Content!.ReadAsStringAsync(ct);
            return new(Fail ? HttpStatusCode.Forbidden : HttpStatusCode.OK)
            { Content = Fail ? new StringContent("PRIVATE_MATERIAL") : new ByteArrayContent(Bytes) };
        }
    }
    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, false); }
    public class SessionProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => method?.Name == nameof(IMoveItSessionService.Get) ? new MoveItSessionState
            { WebAdminUrl = "https://moveit.test", AccessToken = "test-token", AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) } : throw new NotSupportedException();
    }
}
