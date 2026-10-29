using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class ObjectCreationGroupPayloadTests
{
    [Theory]
    [InlineData("SSH")] [InlineData("TLS")] [InlineData("PGP")]
    [InlineData("ImportSSH")] [InlineData("ImportTLS")] [InlineData("ImportPGP")]
    public async Task KeyCreationAndImportPutResourceGroupInTheNativePayload(string operation)
    {
        using var handler = new CaptureHandler();
        var sessions = DispatchProxy.Create<IMoveItSessionService, HostSharedCrudTests.CrudProxy>();
        ((HostSharedCrudTests.CrudProxy)(object)sessions).Handler = (method, _) => method.Name == nameof(IMoveItSessionService.Get)
            ? new MoveItSessionState { WebAdminUrl = "https://moveit.test", AccessToken = "test-token", AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) }
            : throw new NotSupportedException(method.Name);
        var client = new MoveItRestClient(new ClientFactory(handler), sessions,
            ConfigurationStartupState.Capture(new ConfigurationBuilder().Build()), null!, NullLogger<MoveItRestClient>.Instance);
        const string group = "Permitted Resource Group";
        switch (operation)
        {
            case "SSH": await client.CreateSshKeyV0Async("New Key", "RSA", 2048, resourceGroup: group); break;
            case "TLS": await client.CreateTlsCertificateV0Async("CN=Test", "12/31/2027", 2048, resourceGroup: group); break;
            case "PGP": await client.CreatePgpKeyV0Async("Test", "RSA", 2048, "SHA256", 1, "years", "", resourceGroup: group); break;
            case "ImportSSH": await client.ImportSshKeyV0Async("New Key", "", "test-private-material", resourceGroup: group); break;
            case "ImportTLS": await client.ImportTlsCertificateV0Async("test.cer", [1, 2, 3], true, "", resourceGroup: group); break;
            case "ImportPGP": await client.ImportPgpKeyV0Async([1, 2, 3], "", resourceGroup: group); break;
        }
        Assert.Equal(group, JsonNode.Parse(handler.Group!)![0]!.ToString());
        Assert.Equal(1, handler.Calls); Assert.Equal("Bearer test-token", handler.Authorization);
    }
    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? Group { get; private set; }
        public string? Authorization { get; private set; }
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; Authorization = request.Headers.Authorization?.ToString();
            Assert.Equal(HttpMethod.Post, request.Method);
            if (request.Content is MultipartFormDataContent multipart)
            {
                foreach (var part in multipart)
                    if (part.Headers.ContentDisposition?.Name?.Trim('"') == "Group") Group = await part.ReadAsStringAsync(ct);
            }
            else Group = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!["Group"]!.ToJsonString();
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"status\":\"success\",\"errorcode\":0}") };
        }
    }
    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false);
    }
}
