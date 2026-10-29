// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Services.Authentication;

public interface IMauiApplicationSessionService
{
    // Indicates whether the authenticated browser principal still has usable server-side MOVEit token state.
    bool HasLiveMoveItSession { get; }

    // Establishes the server-side MOVEit session and encrypted MAUI authentication cookie.
    Task SignInAsync(HttpContext httpContext, MoveItSessionState moveItState, string role, string? upgradeTargetVersion = null);

    // Advances the authoritative idle timestamp after deliberate browser activity.
    bool RecordUserActivity();

    // Clears stale local authentication state before rendering a fresh login form.
    Task ResetForLoginAsync(HttpContext httpContext, bool clearAntiforgeryCookie = false);

    // Clears local MOVEit/session state and browser cookies during an explicit logout.
    Task SignOutAsync(HttpContext httpContext);
}

public sealed class MauiApplicationSessionService : IMauiApplicationSessionService
{
    private readonly IMoveItSessionService _sessionService;
    private readonly ConfigurationStartupState _runtimeConfiguration;

    public bool HasLiveMoveItSession => _sessionService.IsAuthenticated;

    // Centralizes MAUI browser/session authentication mechanics so controllers only coordinate login decisions.
    public MauiApplicationSessionService(
        IMoveItSessionService sessionService,
        ConfigurationStartupState runtimeConfiguration)
    {
        _sessionService = sessionService;
        _runtimeConfiguration = runtimeConfiguration;
    }

    // Creates the opaque MOVEit session reference, claims principal, auth cookie, and display-only identity cookies.
    public async Task SignInAsync(HttpContext httpContext, MoveItSessionState moveItState, string role, string? upgradeTargetVersion = null)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(moveItState);

        moveItState.LastUserActivityUtc = DateTimeOffset.UtcNow;
        var createdSessionId = _sessionService.Create(moveItState);
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, moveItState.Username),
            new(ClaimTypes.Role, role),
            new(MoveItSessionService.SessionIdClaimType, createdSessionId),
            new(MauiAuthenticationDefaults.ServerKeyClaimType, moveItState.ServerKey),
            new(MauiAuthenticationDefaults.ServerNameClaimType, moveItState.ServerDisplayName)
        };
        if (!string.IsNullOrWhiteSpace(upgradeTargetVersion))
        {
            // A pending upgrade is authorized by a fresh MOVEit/Admin login for one exact package target.
            // Upgrade access must not depend on the transient MOVEit token cache surviving the redirect to /Upgrade.
            claims.Add(new Claim(MauiAuthenticationDefaults.UpgradeTargetVersionClaimType, upgradeTargetVersion.Trim()));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var isUpgradeAuthorization = !string.IsNullOrWhiteSpace(upgradeTargetVersion);
        var authProperties = new AuthenticationProperties
        {
            // Upgrade authorization is intentionally session-only and non-sliding. After the target package
            // restarts, UpgradeAccessMiddleware clears this cookie before normal application access resumes.
            IsPersistent = !isUpgradeAuthorization,
            AllowRefresh = !isUpgradeAuthorization,
            ExpiresUtc = DateTimeOffset.UtcNow.Add(MauiAuthenticationDefaults.GetSessionLifetime(_runtimeConfiguration.IdleSessionTimeoutMinutes))
        };

        await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);

        httpContext.Response.Cookies.Append(
            MauiAuthenticationDefaults.UserDisplayCookieName,
            moveItState.Username,
            BuildDisplayCookieOptions(httpContext));
        httpContext.Response.Cookies.Append(
            MauiAuthenticationDefaults.ServerDisplayCookieName,
            moveItState.ServerDisplayName,
            BuildDisplayCookieOptions(httpContext));
    }

    // Records deliberate browser activity without allowing background requests to extend the idle window.
    public bool RecordUserActivity() => _sessionService.RecordUserActivity();

    // Removes a stale local session/cookie and resets the current request principal so antiforgery treats the login form as anonymous.
    public async Task ResetForLoginAsync(HttpContext httpContext, bool clearAntiforgeryCookie = false)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        _sessionService.Clear();
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        if (clearAntiforgeryCookie)
        {
            httpContext.Response.Cookies.Delete(
                MauiAuthenticationDefaults.AntiforgeryCookieName,
                new CookieOptions { Path = CookiePath(httpContext) });
        }

        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
    }

    // Clears local MOVEit state, the auth cookie, and display-only cookies after remote token revocation has been attempted.
    public async Task SignOutAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        _sessionService.Clear();
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        httpContext.Response.Cookies.Delete(MauiAuthenticationDefaults.UserDisplayCookieName, new CookieOptions { Path = CookiePath(httpContext) });
        httpContext.Response.Cookies.Delete(MauiAuthenticationDefaults.ServerDisplayCookieName, new CookieOptions { Path = CookiePath(httpContext) });
    }

    // Returns the common non-sensitive display-cookie settings used by the shell.
    private static string CookiePath(HttpContext context) =>
        context.Request.PathBase.HasValue ? context.Request.PathBase.Value! : "/";

    private static CookieOptions BuildDisplayCookieOptions(HttpContext context) => new()
    {
        HttpOnly = false,
        SameSite = SameSiteMode.Lax,
        IsEssential = true,
        Path = CookiePath(context)
    };
}
