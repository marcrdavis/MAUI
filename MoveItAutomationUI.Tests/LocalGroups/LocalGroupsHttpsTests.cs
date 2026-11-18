using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MoveItAutomationUI.Features.LocalGroups;
using MoveItAutomationUI.Services.Configuration;

namespace MoveItAutomationUI.Tests.LocalGroups;

public sealed class LocalGroupsHttpsTests
{
    [Theory]
    [InlineData("")]
    [InlineData("/MAUI")]
    public async Task RealHttpsNegotiatesCertificatesAndKeepsAgentAuthenticationSeparateFromBrowserLogin(string pathBase)
    {
        await using var host = await Fixture.StartAsync(pathBase);
        using var browser = host.Client();
        var agentPath = pathBase + "/api/local-groups-agent";

        foreach (var suffix in new[] { "", "/" })
        {
            using var information = await browser.GetAsync(agentPath + suffix);
            Assert.Equal(HttpStatusCode.OK, information.StatusCode);
            Assert.Null(information.Headers.Location);
            Assert.True(information.Headers.CacheControl?.NoStore == true);
            using var json = JsonDocument.Parse(await information.Content.ReadAsStringAsync());
            Assert.Equal(1, json.RootElement.GetProperty("protocolVersion").GetInt32());
            Assert.Contains("POST poll", json.RootElement.GetProperty("message").GetString());
            Assert.Equal(2, json.RootElement.EnumerateObject().Count());
        }

        Assert.Equal("none", await browser.GetStringAsync(pathBase + "/test-certificate"));
        await AssertAgentRejected(browser, agentPath);
        using (var protectedPage = await browser.GetAsync(pathBase + "/browser-page"))
        {
            Assert.Equal(HttpStatusCode.Redirect, protectedPage.StatusCode);
            Assert.Contains("/Account/Login", protectedPage.Headers.Location!.ToString());
        }
        using (var signIn = await browser.GetAsync(pathBase + "/test-browser-sign-in"))
            Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);
        using (var protectedPage = await browser.GetAsync(pathBase + "/browser-page"))
            Assert.Equal(HttpStatusCode.OK, protectedPage.StatusCode);
        await AssertAgentRejected(browser, agentPath);

        // An actual TLS 1.2 connection must transmit the offered client certificate.
        // The test-only transport pins this fixture's certificate; production authentication
        // still rejects this enrolled but self-signed/untrusted certificate.
        using var service = host.Client(withCertificate: true);
        Assert.Equal(host.ClientCertificate.GetCertHashString(HashAlgorithmName.SHA256),
            await service.GetStringAsync(pathBase + "/test-certificate"));
        await AssertAgentRejected(service, agentPath);

        host.Configuration["MoveItLocalGroups:Enabled:Value"] = "false";
        using var disabledInformation = await browser.GetAsync(agentPath + "/");
        Assert.Equal(HttpStatusCode.OK, disabledInformation.StatusCode);
        await AssertAgentRejected(browser, agentPath);
    }

    private static async Task AssertAgentRejected(HttpClient client, string agentPath)
    {
        foreach (var operation in new[] { "poll", "result" })
        {
            using var response = await client.PostAsJsonAsync(agentPath + "/" + operation,
                new { protocolVersion = 1, computerName = "SERVER01" });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Null(response.Headers.Location);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required WebApplication App { get; init; }
        public required Uri Address { get; init; }
        public required string DirectoryPath { get; init; }
        public required X509Certificate2 ServerCertificate { get; init; }
        public required X509Certificate2 ClientCertificate { get; init; }
        public required IConfiguration Configuration { get; init; }

        public HttpClient Client(bool withCertificate = false)
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseProxy = false,
                SslProtocols = SslProtocols.Tls12,
                // Trust only this test's exact server certificate, retaining name validation.
                ServerCertificateCustomValidationCallback = (_, certificate, _, errors) => certificate is not null
                    && certificate.GetCertHashString(HashAlgorithmName.SHA256) == ServerCertificate.GetCertHashString(HashAlgorithmName.SHA256)
                    && (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) == SslPolicyErrors.None
            };
            if (withCertificate) handler.ClientCertificates.Add(ClientCertificate);
            return new HttpClient(handler) { BaseAddress = Address, Timeout = TimeSpan.FromSeconds(15) };
        }

        public static async Task<Fixture> StartAsync(string pathBase)
        {
            var directory = Path.Combine(Path.GetTempPath(), "maui-agent-https-" + Guid.NewGuid().ToString("N"));
            var serverCertificate = Certificate(server: true);
            var clientCertificate = Certificate(server: false);
            WebApplication? app = null;
            try
            {
                var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
                builder.Configuration.Sources.Clear();
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MoveItLocalGroups:Enabled:Value"] = "true",
                    ["MoveItLocalGroups:Agents:0:ServerKey"] = "A",
                    ["MoveItLocalGroups:Agents:0:CertificateSha256"] = clientCertificate.GetCertHashString(HashAlgorithmName.SHA256)
                });
                builder.Logging.ClearProviders();
                builder.WebHost.ConfigureKestrel(options =>
                {
                    LocalGroupsHosting.ConfigureHttps(options);
                    options.Listen(IPAddress.Loopback, 0, listener => listener.UseHttps(https =>
                    {
                        Assert.Equal(ClientCertificateMode.AllowCertificate, https.ClientCertificateMode);
                        https.ServerCertificate = serverCertificate;
                        // This override exists only in this fixture. It permits the TLS probe to
                        // offer its exact test certificate without altering OS trust stores.
                        https.ClientCertificateValidation = (certificate, _, _) =>
                            certificate.GetCertHashString(HashAlgorithmName.SHA256) == clientCertificate.GetCertHashString(HashAlgorithmName.SHA256);
                    }));
                });
                builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
                builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                    .AddCookie(options => options.LoginPath = "/Account/Login")
                    .AddScheme<AuthenticationSchemeOptions, AgentAuthenticationHandler>(LocalGroupsProtocol.AgentScheme, _ => { });
                builder.Services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
                builder.Services.AddControllers().AddApplicationPart(typeof(LocalGroupsAgentController).Assembly);
                builder.Services.AddSingleton(new LocalGroupsConfiguration(new Snapshots(builder.Configuration)));
                builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
                builder.Services.AddSingleton(new LocalGroupsLedger(directory));
                builder.Services.AddSingleton<LocalGroupsBroker>();
                app = builder.Build();
                if (pathBase.Length > 0) app.UsePathBase(pathBase);
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.MapControllerRoute("default", "{controller=Account}/{action=Login}/{id?}");
                app.MapGet("/test-certificate", async (HttpContext context) =>
                    (await context.Connection.GetClientCertificateAsync())?.GetCertHashString(HashAlgorithmName.SHA256) ?? "none").AllowAnonymous();
                app.MapGet("/test-browser-sign-in", async (HttpContext context) =>
                {
                    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                        new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Admin"), new Claim(ClaimTypes.Role, "Admin")], CookieAuthenticationDefaults.AuthenticationScheme)));
                    context.Response.StatusCode = StatusCodes.Status204NoContent;
                }).AllowAnonymous();
                app.MapGet("/browser-page", () => "Browser session accepted.").RequireAuthorization();
                await app.StartAsync();
                var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
                return new Fixture { App = app, Address = new Uri(Assert.Single(addresses)), DirectoryPath = directory,
                    ServerCertificate = serverCertificate, ClientCertificate = clientCertificate, Configuration = builder.Configuration };
            }
            catch
            {
                if (app is not null) await app.DisposeAsync();
                serverCertificate.Dispose();
                clientCertificate.Dispose();
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
                throw;
            }
        }

        private static X509Certificate2 Certificate(bool server)
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=MAUI HTTPS Test " + Guid.NewGuid().ToString("N"), key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new(server ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2") }, false));
            if (server)
            {
                var names = new SubjectAlternativeNameBuilder();
                names.AddDnsName("localhost"); names.AddIpAddress(IPAddress.Loopback);
                request.CertificateExtensions.Add(names.Build());
            }
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
            return new X509Certificate2(certificate.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.Exportable);
        }

        public async ValueTask DisposeAsync()
        {
            await App.DisposeAsync();
            ServerCertificate.Dispose();
            ClientCertificate.Dispose();
            if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, recursive: true);
        }
    }

    private sealed class Snapshots(IConfigurationRoot configuration) : IAppSettingsConfigurationSnapshotReader
    {
        public AppSettingsConfigurationSnapshot Read() => new(new JsonObject(), configuration, "https-test");
        public AppSettingsConfigurationSnapshot FromDocument(JsonObject document) => throw new NotSupportedException();
        public string GetRevision(JsonObject document) => "https-test";
    }
}
