using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.MoveIt;

// Real HTTP/MVC, the production REST client and combined SSH/TLS/PGP inventory. In particular,
// a network failure after response headers previously escaped the handler-only observation.
public sealed class MoveItGridConnectionHttpTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var pathBase in new[] { "", "/maui" })
        foreach (var action in new[] { "InitialKeysAndCertificatesGrid", "RefreshKeysAndCertificatesGrid" })
        foreach (var mode in new[] { "dns", "refused", "body-network", "body-ended", "body-timeout", "http500", "tls", "badjson", "live" })
            yield return [pathBase, action, mode];
    }

    [Theory, MemberData(nameof(Cases))]
    public async Task CombinedKeyInventoryUsesTheSharedTransportContractOverHttp(string pathBase, string action, string mode)
    {
        await using var fixture = await Fixture.StartAsync(pathBase, mode);
        using var client = fixture.Client();
        using var response = await client.GetAsync($"{pathBase}/Resources/{action}?forceRefresh=true");
        var expected = mode switch
        {
            "body-timeout" => "timeout",
            "http500" => "responded",
            "tls" => "none",
            "badjson" or "live" => "live",
            _ => "unreachable"
        };
        var expectedStatus = mode switch
        {
            "http500" or "badjson" => HttpStatusCode.BadRequest,
            "tls" => HttpStatusCode.InternalServerError,
            "live" => HttpStatusCode.OK,
            _ => HttpStatusCode.ServiceUnavailable
        };
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal(expected, response.Headers.GetValues(MoveItGridConnectionMiddleware.HeaderName).First());
        Assert.True(fixture.ReadPaths.Count == 3, "The production inventory must request SSH, TLS and PGP before awaiting the combined result.");
        Assert.Contains("/api/v1/sshkeys", fixture.ReadPaths);
        Assert.Contains("/api/v1/sslcerts", fixture.ReadPaths);
        Assert.Contains("/api/v1/pgpkeys", fixture.ReadPaths);
        if (expected is "timeout" or "unreachable")
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains(expected == "timeout" ? "MOVEit did not respond" : "MAUI cannot connect to MOVEit", body);
            Assert.DoesNotContain("transport detail", body);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("/maui")]
    public async Task FailedKeyReadDoesNotReplaceAnExistingCachedInventory(string pathBase)
    {
        await using var fixture = await Fixture.StartAsync(pathBase, "live");
        using var client = fixture.Client();
        using var warm = await client.GetAsync(pathBase + "/Resources/RefreshKeysAndCertificatesGrid?forceRefresh=true");
        Assert.True(warm.IsSuccessStatusCode);
        fixture.Mode = "body-network";
        using var failure = await client.GetAsync(pathBase + "/Resources/RefreshKeysAndCertificatesGrid?forceRefresh=true");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failure.StatusCode);
        using var cached = await client.GetAsync(pathBase + "/Resources/InitialKeysAndCertificatesGrid?preferCached=true");
        Assert.True(cached.IsSuccessStatusCode);
        Assert.Equal("none", cached.Headers.GetValues(MoveItGridConnectionMiddleware.HeaderName).First());
        fixture.Mode = "live";
        using var recovered = await client.GetAsync(pathBase + "/Resources/RefreshKeysAndCertificatesGrid?forceRefresh=true");
        Assert.True(recovered.IsSuccessStatusCode);
        Assert.Equal("live", recovered.Headers.GetValues(MoveItGridConnectionMiddleware.HeaderName).First());
    }

    [Fact]
    public async Task TokenBodyFailureIsReportedAsConnectivityNotAnExpiredLogin()
    {
        await using var fixture = await Fixture.StartAsync("", "token-body");
        using var client = fixture.Client();
        using var response = await client.GetAsync("/Resources/RefreshKeysAndCertificatesGrid?forceRefresh=true");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("unreachable", response.Headers.GetValues(MoveItGridConnectionMiddleware.HeaderName).First());
        Assert.All(fixture.ReadPaths, path => Assert.Equal("/api/v1/token", path));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public WebApplication App { get; private set; } = null!;
        public string Mode { get; set; } = "live";
        public ConcurrentQueue<string> ReadPaths { get; } = new();
        private string address = string.Empty;
        public HttpClient Client() => new() { BaseAddress = new Uri(address) };

        public static async Task<Fixture> StartAsync(string pathBase, string mode)
        {
            var fixture = new Fixture { Mode = mode };
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
                { ApplicationName = typeof(ResourcesController).Assembly.GetName().Name, EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddMemoryCache();
            builder.Services.AddControllersWithViews().AddApplicationPart(typeof(ResourcesController).Assembly).AddControllersAsServices();
            builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, Authentication>("Test", _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddTransient<MoveItGridConnectionHandler>();
            builder.Services.AddHttpClient("MoveItAutomation").AddHttpMessageHandler<MoveItGridConnectionHandler>()
                .ConfigurePrimaryHttpMessageHandler(() => new InventoryHandler(fixture));
            builder.Services.AddSingleton(ConfigurationStartupState.Capture(builder.Configuration));
            var session = new MoveItSessionState { ServerKey = "test", Username = "Test", WebAdminUrl = "https://moveit.test",
                AccessToken = "test-token", RefreshToken = "test-refresh",
                AccessTokenExpiresUtc = mode == "token-body" ? DateTimeOffset.UtcNow.AddMinutes(-1) : DateTimeOffset.UtcNow.AddHours(1),
                Info = new MoveItInfo { IsAdmin = true } };
            var sessions = Stub<IMoveItSessionService>((method, _) => method.Name == nameof(IMoveItSessionService.Get)
                ? session : throw new NotSupportedException(method.Name));
            builder.Services.AddSingleton(sessions);
            builder.Services.AddScoped<IMoveItRestClient>(provider => new MoveItRestClient(
                provider.GetRequiredService<IHttpClientFactory>(), sessions, provider.GetRequiredService<ConfigurationStartupState>(),
                null!, provider.GetRequiredService<ILogger<MoveItRestClient>>()));
            builder.Services.AddScoped<IAutomationKeyCertificateService>(provider => new AutomationKeyCertificateService(
                provider.GetRequiredService<IMoveItRestClient>(), null!, sessions,
                provider.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>(),
                provider.GetRequiredService<IOptionsSnapshot<KeyCertificateBehaviorOptions>>()));
            var metadata = Stub<IMetadataRepository>((method, _) => method.Name switch
            {
                nameof(IMetadataRepository.EnsureObjectsAsync) => Task.CompletedTask,
                nameof(IMetadataRepository.GetObjectMetadataBatchAsync) => Task.FromResult<IReadOnlyDictionary<string, ObjectMetadataSnapshot>>(
                    new Dictionary<string, ObjectMetadataSnapshot>()),
                _ => throw new NotSupportedException(method.Name)
            });
            builder.Services.AddTransient(provider => new ResourcesController(
                scriptService: null!, dateListService: null!, globalParameterService: null!,
                keyCertificateService: provider.GetRequiredService<IAutomationKeyCertificateService>(),
                metadataRepository: metadata, metadataValueValidation: null!, scriptUndoService: null!, recycleBinService: null!,
                resourcePortabilityService: null!, dateListUndoService: null!, sessionService: sessions,
                uiOptions: provider.GetRequiredService<IOptionsSnapshot<UiOptions>>(),
                gridBehaviorOptions: provider.GetRequiredService<IOptionsSnapshot<GridBehaviorOptions>>(),
                metadataOptions: provider.GetRequiredService<IOptionsSnapshot<MetadataUiOptions>>(),
                scriptBehaviorOptions: provider.GetRequiredService<IOptionsSnapshot<ScriptBehaviorOptions>>(),
                dateListBehaviorOptions: provider.GetRequiredService<IOptionsSnapshot<DateListBehaviorOptions>>(),
                globalParameterBehaviorOptions: provider.GetRequiredService<IOptionsSnapshot<GlobalParameterBehaviorOptions>>(),
                keyCertificateBehaviorOptions: provider.GetRequiredService<IOptionsSnapshot<KeyCertificateBehaviorOptions>>(),
                logger: provider.GetRequiredService<ILogger<ResourcesController>>(), scriptMetadataSaves: null!));
            fixture.App = builder.Build();
            fixture.App.UseExceptionHandler(error => error.Run(async context =>
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new { message = "Application error" });
            }));
            if (pathBase.Length > 0) fixture.App.UsePathBase(pathBase);
            fixture.App.UseRouting(); fixture.App.UseAuthentication(); fixture.App.UseAuthorization();
            fixture.App.UseMiddleware<MoveItGridConnectionMiddleware>();
            fixture.App.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
            await fixture.App.StartAsync();
            fixture.address = fixture.App.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            return fixture;
        }
        public async ValueTask DisposeAsync() => await App.DisposeAsync();
    }

    private sealed class InventoryHandler(Fixture fixture) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            fixture.ReadPaths.Enqueue(request.RequestUri!.AbsolutePath);
            if (fixture.Mode is "dns" or "refused" or "tls") throw new HttpRequestException(fixture.Mode switch
            {
                "dns" => HttpRequestError.NameResolutionError,
                "refused" => HttpRequestError.ConnectionError,
                _ => HttpRequestError.SecureConnectionError
            }, "transport detail");
            var response = new HttpResponseMessage(fixture.Mode == "http500" ? HttpStatusCode.InternalServerError : HttpStatusCode.OK)
            {
                Content = fixture.Mode == "token-body" ? new FailedBody("body-network")
                    : fixture.Mode.StartsWith("body-", StringComparison.Ordinal) ? new FailedBody(fixture.Mode)
                    : new StringContent(fixture.Mode == "badjson" ? "{bad" : "[]", Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class FailedBody(string mode) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.FromException(mode switch
        {
            "body-timeout" => new TaskCanceledException("transport detail", new TimeoutException()),
            "body-ended" => new HttpIOException(HttpRequestError.ResponseEnded, "transport detail"),
            _ => new HttpRequestException(HttpRequestError.ConnectionError, "transport detail")
        });
    }

    private sealed class Authentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Test")], "Test")), "Test")));
    }
    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    { var proxy = DispatchProxy.Create<T, HostSharedCrudTests.CrudProxy>(); ((HostSharedCrudTests.CrudProxy)(object)proxy).Handler = handler; return proxy; }
}
