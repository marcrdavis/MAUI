using System.Security.Claims;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class MoveItSessionStateConcurrencyTests
{
    [Fact]
    public void ClearingAnIsolatedDataAccessSessionDoesNotSignOutTheBrowser()
    {
        using var fixture = SessionFixture.Create();
        var browser = SessionFixture.NewState(); var id = fixture.Service.Create(browser); fixture.SetPrincipal(id);
        var dataAccess = new MoveItSessionState { Username = "data-access", Password = "temporary", AccessToken = "isolated-token", RefreshToken = "isolated-refresh" };
        using (fixture.Service.PushBackgroundSession(dataAccess))
        {
            Assert.Equal("data-access", fixture.Service.Get()!.Username);
            fixture.Service.Clear();
        }
        Assert.NotNull(fixture.Service.Get()); Assert.Equal(browser.AccessToken, fixture.Service.Get()!.AccessToken);
        Assert.Empty(dataAccess.Password); Assert.Empty(dataAccess.AccessToken); Assert.Empty(dataAccess.RefreshToken);
        Assert.True(fixture.Service.HasLiveBrowserSession(SessionFixture.BuildPrincipal(id)));
    }
    [Fact]
    public void HasLiveBrowserSession_RejectsMissingServerSideSession()
    {
        using var fixture = SessionFixture.Create();
        var principal = SessionFixture.BuildPrincipal(Guid.NewGuid().ToString("N"));

        Assert.False(fixture.Service.HasLiveBrowserSession(principal));
    }

    [Fact]
    public void HasLiveBrowserSession_RejectsExpiredServerSideSession()
    {
        using var fixture = SessionFixture.Create(idleMinutes: 15);
        var state = SessionFixture.NewState();
        state.LastUserActivityUtc = DateTimeOffset.UtcNow.AddMinutes(-20);
        var sessionId = fixture.Service.Create(state);
        var principal = SessionFixture.BuildPrincipal(sessionId);

        Assert.False(fixture.Service.HasLiveBrowserSession(principal));
    }

    [Fact]
    public void TokenRefresh_FollowedByActivity_PreservesRefreshedTokens()
    {
        using var fixture = SessionFixture.Create();
        var sessionId = fixture.Service.Create(SessionFixture.NewState());
        fixture.SetPrincipal(sessionId);

        fixture.Service.UpdateTokens("new-access", "new-refresh", DateTimeOffset.UtcNow.AddMinutes(15));
        Assert.True(fixture.Service.RecordUserActivity());

        var current = fixture.Service.Get();
        Assert.NotNull(current);
        Assert.Equal("new-access", current!.AccessToken);
        Assert.Equal("new-refresh", current.RefreshToken);
        Assert.True(current.LastUserActivityUtc > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public void Clear_RemovesCurrentServerSideSession()
    {
        using var fixture = SessionFixture.Create();
        var sessionId = fixture.Service.Create(SessionFixture.NewState());
        fixture.SetPrincipal(sessionId);

        Assert.NotNull(fixture.Service.Get());
        fixture.Service.Clear();

        Assert.Null(fixture.Service.Get());
    }

    [Fact]
    public void DismissSystemMessage_DoesNotOverwriteRefreshedTokens()
    {
        using var fixture = SessionFixture.Create();
        var sessionId = fixture.Service.Create(SessionFixture.NewState());
        fixture.SetPrincipal(sessionId);

        fixture.Service.UpdateTokens("new-access", "new-refresh", DateTimeOffset.UtcNow.AddMinutes(15));
        Assert.True(fixture.Service.DismissSystemMessage());

        var current = fixture.Service.Get();
        Assert.NotNull(current);
        Assert.True(current!.SystemMessageDismissed);
        Assert.Equal("new-access", current.AccessToken);
        Assert.Equal("new-refresh", current.RefreshToken);
    }

    [Fact]
    public void CleanupTokenRenewalPreservesLoginIdleTimestamp()
    {
        using var fixture = SessionFixture.Create();
        var initial = SessionFixture.NewState();
        initial.LastUserActivityUtc = DateTimeOffset.UtcNow.AddMinutes(-5);
        var sessionId = fixture.Service.Create(initial);
        fixture.SetPrincipal(sessionId);
        var cleanup = fixture.Service.Get()!;
        cleanup.CleanupOwnerSessionId = sessionId;
        using (fixture.Service.PushBackgroundSession(cleanup))
            fixture.Service.UpdateTokens("cleanup-access", "cleanup-refresh", DateTimeOffset.UtcNow.AddMinutes(20));
        var current = fixture.Service.Get()!;
        Assert.Equal("cleanup-access", current.AccessToken);
        Assert.Equal(initial.LastUserActivityUtc, current.LastUserActivityUtc);
    }

    [Fact]
    public void CleanupTokenRenewalCannotRecreateLoggedOutBrowserSession()
    {
        using var fixture = SessionFixture.Create();
        var sessionId = fixture.Service.Create(SessionFixture.NewState());
        fixture.SetPrincipal(sessionId);
        var cleanup = fixture.Service.Get()!;
        cleanup.CleanupOwnerSessionId = sessionId;
        fixture.Service.Clear();
        using (fixture.Service.PushBackgroundSession(cleanup))
            fixture.Service.UpdateTokens("cleanup-only-access", "cleanup-only-refresh", DateTimeOffset.UtcNow.AddMinutes(20));
        Assert.Null(fixture.Service.Get());
    }

    [Fact]
    public void CheckoutBackgroundReadsAdoptRotatedTokensWithoutRestoringCredentialsOrIdleTime()
    {
        using var fixture = SessionFixture.Create();
        var state = SessionFixture.NewState(); state.Password = "in-memory-password";
        var sessionId = fixture.Service.Create(state); fixture.SetPrincipal(sessionId);
        var cleanup = fixture.Service.Get()!;
        cleanup.Password = ""; cleanup.Info = null; cleanup.CleanupOwnerSessionId = sessionId;
        var activity = cleanup.LastUserActivityUtc;
        fixture.Service.UpdateTokens("rotated-access", "rotated-refresh", DateTimeOffset.UtcNow.AddMinutes(20));
        using (fixture.Service.PushBackgroundSession(cleanup))
        {
            var fresh = fixture.Service.Get()!;
            Assert.Equal("rotated-access", fresh.AccessToken);
            Assert.Equal("rotated-refresh", fresh.RefreshToken);
            Assert.Equal("", fresh.Password); Assert.Null(fresh.Info);
            Assert.Equal(activity, fresh.LastUserActivityUtc);
        }
        Assert.Equal("in-memory-password", fixture.Service.Get()!.Password);
    }

    [Fact]
    public async Task CheckoutReleaseAfterBrowserRotationUsesTheCurrentBearerWithoutAnotherLogin()
    {
        using var fixture = SessionFixture.Create();
        var initial = SessionFixture.NewState(); initial.AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(-1); initial.WebAdminUrl = "https://checkout.test";
        var id = fixture.Service.Create(initial); fixture.SetPrincipal(id);
        var cleanup = fixture.Service.Get()!; cleanup.CleanupOwnerSessionId = id;
        fixture.Service.UpdateTokens("current-checkout-token", "current-refresh", DateTimeOffset.UtcNow.AddMinutes(20));
        using var handler = new CheckoutReleaseHandler();
        var client = new MoveItRestClient(new ClientFactory(handler), fixture.Service,
            ConfigurationStartupState.Capture(new ConfigurationBuilder().Build()), null!, NullLogger<MoveItRestClient>.Instance);
        using (fixture.Service.PushBackgroundSession(cleanup)) await client.ReleaseTaskCheckoutV0Async("101");
        Assert.Equal("current-checkout-token", handler.Token);
        Assert.Equal(1, handler.Requests);
    }

    private sealed class CheckoutReleaseHandler : HttpMessageHandler
    {
        public string? Token;
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++; Token = request.Headers.Authorization?.Parameter;
            Assert.Equal(HttpMethod.Delete, request.Method); // No refresh/password grant may be sent.
            Assert.EndsWith("/tasks/101/checkout", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }
    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, disposeHandler: false); }

    [Fact]
    public void CleanupRefreshWithShorterTokenLifetimeStillUpdatesTheSameLogin()
    {
        using var fixture = SessionFixture.Create();
        var initial = SessionFixture.NewState(); initial.AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddHours(1);
        var sessionId = fixture.Service.Create(initial); fixture.SetPrincipal(sessionId);
        var cleanup = fixture.Service.Get()!; cleanup.CleanupOwnerSessionId = sessionId;
        using (fixture.Service.PushBackgroundSession(cleanup))
        {
            _ = fixture.Service.Get();
            fixture.Service.UpdateTokens("forced-refresh", "next-refresh", DateTimeOffset.UtcNow.AddMinutes(5));
        }
        Assert.Equal("forced-refresh", fixture.Service.Get()!.AccessToken);
        Assert.Equal("next-refresh", fixture.Service.Get()!.RefreshToken);
    }

    [Fact]
    public void BackgroundCleanupCannotAdoptTokensFromAnotherEnvironment()
    {
        using var fixture = SessionFixture.Create();
        var sessionId = fixture.Service.Create(SessionFixture.NewState()); fixture.SetPrincipal(sessionId);
        var cleanup = fixture.Service.Get()!; cleanup.CleanupOwnerSessionId = sessionId; cleanup.EnvironmentKey = "OTHER";
        fixture.Service.UpdateTokens("live-access", "live-refresh", DateTimeOffset.UtcNow.AddMinutes(20));
        using (fixture.Service.PushBackgroundSession(cleanup))
        {
            Assert.Equal("old-access", fixture.Service.Get()!.AccessToken);
            fixture.Service.UpdateTokens("other-access", "other-refresh", DateTimeOffset.UtcNow.AddMinutes(30));
        }
        Assert.Equal("live-access", fixture.Service.Get()!.AccessToken);
    }

    private sealed class SessionFixture : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly DefaultHttpContext _httpContext;

        private SessionFixture(ServiceProvider provider, DefaultHttpContext httpContext, MoveItSessionService service)
        {
            _provider = provider;
            _httpContext = httpContext;
            Service = service;
        }

        public MoveItSessionService Service { get; }

        public static SessionFixture Create(int idleMinutes = 30)
        {
            var services = new ServiceCollection();
            services.AddDistributedMemoryCache();
            var provider = services.BuildServiceProvider();
            var context = new DefaultHttpContext { RequestServices = provider };
            var accessor = new HttpContextAccessor { HttpContext = context };
            var configuredValues = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MoveIt:IdleSessionTimeoutMinutes:Value"] = idleMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }).Build();
            var runtime = ConfigurationStartupState.Capture(configuredValues);
            var service = new MoveItSessionService(accessor, provider.GetRequiredService<IDistributedCache>(), runtime);
            return new SessionFixture(provider, context, service);
        }

        public void SetPrincipal(string sessionId) => _httpContext.User = BuildPrincipal(sessionId);

        public static ClaimsPrincipal BuildPrincipal(string sessionId)
        {
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "tester"),
                new Claim(MoveItSessionService.SessionIdClaimType, sessionId)
            }, "test");
            return new ClaimsPrincipal(identity);
        }

        public static MoveItSessionState NewState() => new()
        {
            ServerKey = "NODE41",
            EnvironmentKey = "TEST",
            Username = "tester",
            AccessToken = "old-access",
            RefreshToken = "old-refresh",
            AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5),
            LastUserActivityUtc = DateTimeOffset.UtcNow
        };

        public void Dispose() => _provider.Dispose();
    }
}
