using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Features.LocalGroups;
using MoveItAutomationUI.Services.Configuration;

namespace MoveItAutomationUI.Tests.LocalGroups;

public sealed class AgentAuthenticationTests
{
    [Theory]
    [InlineData(false,"https","agent.example.com")]
    [InlineData(true,"http","agent.example.com")]
    [InlineData(true,"https","browser.example.com")]
    [InlineData(true,"https","agent.example.com")]
    public async Task FeatureOffInsecureTransportAndMissingCertificateCannotAuthenticate(bool enabled,string scheme,string host)
    {
        var result = await Authenticate(enabled,scheme,host,null);
        Assert.False(result.Succeeded);
    }
    [Fact]
    public async Task BrowserAdministratorIdentityCannotReplaceTheAgentCertificate()
    {
        var result = await Authenticate(true,"https","agent.example.com",null,true);
        Assert.False(result.Succeeded);
    }
    [Fact]
    public async Task UnregisteredCertificateAndRegisteredUntrustedCertificateAreRejected()
    {
        using var key=RSA.Create(2048);
        var request=new CertificateRequest("CN=MAUI Test "+Guid.NewGuid().ToString("N"),key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection{new("1.3.6.1.5.5.7.3.2")},false));
        using var certificate=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1));
        Assert.False((await Authenticate(true,"https","agent.example.com",certificate)).Succeeded);
        Assert.False((await Authenticate(true,"https","agent.example.com",certificate,false,certificate.GetCertHashString(HashAlgorithmName.SHA256))).Succeeded);
    }
    [Fact]
    public void CertificatePurposeAndValidityAreRequired()
    {
        using var key=RSA.Create(2048);
        var request=new CertificateRequest("CN=MAUI Server Purpose Test",key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection{new("1.3.6.1.5.5.7.3.1")},false));
        using var certificate=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1));
        Assert.False(AgentAuthenticationHandler.ValidCertificate(certificate,DateTimeOffset.UtcNow));
        Assert.False(AgentAuthenticationHandler.ValidCertificate(certificate,DateTimeOffset.UtcNow.AddDays(2)));
    }
    private static async Task<AuthenticateResult> Authenticate(bool enabled,string scheme,string host,X509Certificate2? certificate,bool browserAdmin=false,string? fingerprint=null)
    {
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["MoveItLocalGroups:Enabled:Value"]=enabled.ToString(),
            ["MoveItLocalGroups:Agents:0:ServerKey"]="A",
            ["MoveItLocalGroups:Agents:0:CertificateSha256"]=fingerprint??new string('A',64)
        }).Build();
        using var services=new ServiceCollection().AddSingleton<TimeProvider>(TimeProvider.System).BuildServiceProvider();
        var context=new DefaultHttpContext{RequestServices=services};context.Request.Scheme=scheme;context.Request.Host=new HostString(host);context.Connection.ClientCertificate=certificate;
        if(browserAdmin)context.User=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name,"Admin"),new Claim(ClaimTypes.Role,"Admin")],"Cookies"));
        var handler=new AgentAuthenticationHandler(new StaticOptions(),NullLoggerFactory.Instance,UrlEncoder.Default,new LocalGroupsConfiguration(new Snapshots(configuration)));
        await handler.InitializeAsync(new AuthenticationScheme(LocalGroupsProtocol.AgentScheme,null,typeof(AgentAuthenticationHandler)),context);
        return await handler.AuthenticateAsync();
    }
    private sealed class StaticOptions:IOptionsMonitor<AuthenticationSchemeOptions>
    {
        public AuthenticationSchemeOptions CurrentValue{get;}=new();
        public AuthenticationSchemeOptions Get(string? name)=>CurrentValue;
        public IDisposable? OnChange(Action<AuthenticationSchemeOptions,string?> listener)=>null;
    }
    private sealed class Snapshots(IConfigurationRoot configuration):IAppSettingsConfigurationSnapshotReader
    {
        public AppSettingsConfigurationSnapshot Read()=>new(new JsonObject(),configuration,"test");
        public AppSettingsConfigurationSnapshot FromDocument(JsonObject document)=>throw new NotSupportedException();
        public string GetRevision(JsonObject document)=>"test";
    }
}
