// Project: MAUI - MOVEit Automation UI
// Regression coverage for login antiforgery failures under IIS application PathBase.
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Services.Authentication;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Lifecycle;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.Upgrade;

namespace MoveItAutomationUI.Tests.Lifecycle;

public sealed class LoginAntiforgeryRecoveryTests
{
    [Theory]
    [InlineData("", "/Account/Login?formIssue=AF-OTHER")]
    [InlineData("/maui", "/maui/Account/Login?formIssue=AF-OTHER")]
    [InlineData("/MAUI", "/MAUI/Account/Login?formIssue=AF-OTHER")]
    [InlineData("/Applications/QA", "/Applications/QA/Account/Login?formIssue=AF-OTHER")]
    [InlineData("/Account", "/Account/Account/Login?formIssue=AF-OTHER")]
    public async Task InvalidLoginToken_UsesCanonicalLoginUrlAndShowsFailure(string pathBase, string expected)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.PathBase = new PathString(pathBase);
        httpContext.Request.Path = "/Account/Login";
        // Emulates a previous browser identity that still exists when Login POST begins.
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "previous-user") }, "Cookies"));

        var sessions = new TrackingSessionService();
        var antiforgery = new RejectingAntiforgery();
        var controller = new AccountController(
            null!, null!, sessions, null!, antiforgery,
            null!, null!, new MauiUpgradeState(), NullLogger<AccountController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new MemoryTempDataProvider())
        };

        var result = await controller.Login("test-user", "bad-password", "prod", CancellationToken.None);
        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal(expected, redirect.Url);
        Assert.NotEqual(string.IsNullOrEmpty(pathBase) ? "/" : pathBase, redirect.Url);
        Assert.True(sessions.ResetCalled);
        Assert.True(sessions.ClearedAntiforgeryCookie);
        Assert.False(antiforgery.HadAuthenticatedPrincipalWhenValidated);
        // Diagnostic transport must remain independent of cookie-backed TempData, which can
        // be unreadable when Data Protection is the reason token validation failed.
        Assert.False(controller.TempData.ContainsKey("LoginError"));
        Assert.False(controller.TempData.ContainsKey("LoginUsername"));
        Assert.False(controller.TempData.ContainsKey("LoginServer"));
    }

    [Fact]
    public async Task LoginGet_AntiforgeryErrorPageAlwaysRendersWithAnonymousPrincipal()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "GET";
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "previous-user") }, "Cookies"));
        var controller = new AccountController(
            new EmptyServerCatalog(), null!, new TrackingSessionService(), null!,
            new RejectingAntiforgery(), null!, null!, new MauiUpgradeState(),
            NullLogger<AccountController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new MemoryTempDataProvider())
        };

        var result = await controller.Login(formIssue: MauiAntiforgeryFailure.Identity);

        Assert.IsType<ViewResult>(result);
        Assert.False(httpContext.User.Identity?.IsAuthenticated ?? false);
    }

    [Theory]
    [InlineData("", "/Home/Index")]
    [InlineData("/maui", "/maui/Home/Index")]
    [InlineData("/Applications/QA", "/Applications/QA/Home/Index")]
    public async Task LoginGet_WithLiveSessionKeepsAuthenticatedPrincipalAndRedirects(string pathBase, string expected)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "GET";
        httpContext.Request.PathBase = new PathString(pathBase);
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "existing-user") }, "Cookies"));
        var sessions = new TrackingSessionService { HasLiveMoveItSession = true };
        var controller = CreateController(httpContext, sessions);

        var result = await controller.Login();

        Assert.Equal(expected, Assert.IsType<RedirectResult>(result).Url);
        Assert.True(httpContext.User.Identity?.IsAuthenticated == true);
        Assert.False(sessions.ResetCalled);
    }

    [Fact]
    public async Task LoginGet_WithStaleSessionResetsAuthenticationAndRendersAnonymousForm()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "GET";
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "previous-user") }, "Cookies"));
        var sessions = new TrackingSessionService();
        var controller = CreateController(httpContext, sessions);

        var result = await controller.Login();

        Assert.IsType<ViewResult>(result);
        Assert.True(sessions.ResetCalled);
        Assert.False(httpContext.User.Identity?.IsAuthenticated ?? false);
    }

    [Fact]
    public async Task LoginGet_ExistingSessionFailureMessageStillUsesTempData()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "GET";
        var sessions = new TrackingSessionService();
        var controller = CreateController(httpContext, sessions);
        controller.TempData["LoginError"] = "The MOVEit session expired.";

        var result = await controller.Login();

        Assert.IsType<ViewResult>(result);
        Assert.Equal("The MOVEit session expired.", (string)controller.ViewBag.LoginError);
        Assert.True(sessions.ResetCalled);
        Assert.False(httpContext.User.Identity?.IsAuthenticated ?? false);
    }

    private static AccountController CreateController(HttpContext context, TrackingSessionService sessions)
    {
        return new AccountController(
            new EmptyServerCatalog(), null!, sessions, null!, new RejectingAntiforgery(),
            null!, null!, new MauiUpgradeState(), NullLogger<AccountController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
            TempData = new TempDataDictionary(context, new MemoryTempDataProvider())
        };
    }

    private sealed class EmptyServerCatalog : IMoveItServerCatalog
    {
        public IReadOnlyList<MoveItServerDefinition> GetServers() => Array.Empty<MoveItServerDefinition>();
        public MoveItServerDefinition? FindByKey(string? key) => null;
        public MoveItServerDefinition? FindForLogin(string? keyOrDisplayName) => null;
    }

    private sealed class TrackingSessionService : IMauiApplicationSessionService
    {
        public bool HasLiveMoveItSession { get; set; }
        public bool ResetCalled { get; private set; }
        public bool ClearedAntiforgeryCookie { get; private set; }

        public Task SignInAsync(HttpContext context, MoveItSessionState state, string role, string? upgradeTargetVersion = null)
            => throw new NotSupportedException();

        public bool RecordUserActivity() => false;

        public Task ResetForLoginAsync(HttpContext context, bool clearAntiforgeryCookie = false)
        {
            ResetCalled = true;
            ClearedAntiforgeryCookie = clearAntiforgeryCookie;
            return Task.CompletedTask;
        }

        public Task SignOutAsync(HttpContext context) => throw new NotSupportedException();
    }

    private sealed class RejectingAntiforgery : IAntiforgery
    {
        public bool HadAuthenticatedPrincipalWhenValidated { get; private set; }
        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext context) => throw new NotSupportedException();
        public AntiforgeryTokenSet GetTokens(HttpContext context) => throw new NotSupportedException();
        public Task<bool> IsRequestValidAsync(HttpContext context) => Task.FromResult(false);
        public void SetCookieTokenAndHeader(HttpContext context) => throw new NotSupportedException();
        public Task ValidateRequestAsync(HttpContext context)
        {
            HadAuthenticatedPrincipalWhenValidated = context.User.Identity?.IsAuthenticated == true;
            return Task.FromException(new AntiforgeryValidationException("Simulated invalid token"));
        }
    }

    private sealed class MemoryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
