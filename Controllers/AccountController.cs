// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Authentication;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.Lifecycle;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.Security;
using MoveItAutomationUI.Services.Upgrade;

namespace MoveItAutomationUI.Controllers;

[Authorize]
public sealed class AccountController : Controller
{
    private readonly IMoveItServerCatalog _servers;
    private readonly IMoveItRestClient _restClient;
    private readonly IMauiApplicationSessionService _applicationSessionService;
    private readonly IMoveItSessionService _moveItSessionService;
    private readonly IAntiforgery _antiforgery;
    private readonly IMauiSecurityService _securityService;
    private readonly IUserPreferencesService _userPreferences;
    private readonly MauiUpgradeState _upgradeState;
    private readonly ILogger<AccountController> _logger;
    private readonly IMoveItObjectAccessService? _objectAccess;

    // Receives the MOVEit authentication, MAUI session, security, and preference services used by the login flow.
    public AccountController(
        IMoveItServerCatalog servers,
        IMoveItRestClient restClient,
        IMauiApplicationSessionService applicationSessionService,
        IMoveItSessionService moveItSessionService,
        IAntiforgery antiforgery,
        IMauiSecurityService securityService,
        IUserPreferencesService userPreferences,
        MauiUpgradeState upgradeState,
        ILogger<AccountController> logger, IMoveItObjectAccessService? objectAccess = null)
    {
        _servers = servers;
        _restClient = restClient;
        _applicationSessionService = applicationSessionService;
        _moveItSessionService = moveItSessionService;
        _antiforgery = antiforgery;
        _securityService = securityService;
        _userPreferences = userPreferences;
        _upgradeState = upgradeState;
        _logger = logger;
        _objectAccess = objectAccess;
    }

    // Displays the login form. Standard ASP.NET cookie authentication controls the application login state;
    // MOVEit bearer/refresh tokens remain only in the server-side token cache.
    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    // Purpose: Render the login page and clear any stale MAUI authentication state.
    public async Task<IActionResult> Login(bool expired = false, string? formIssue = null)
    {
        if (expired)
        {
            ViewBag.SessionExpiredMessage = "Your session has expired. Please sign in again.";
        }

        // Antiforgery diagnostics must not depend on Data Protection-backed TempData.
        // A fixed, allowlisted error code is safe to carry over the recovery redirect.
        var antiforgeryMessage = MauiAntiforgeryFailure.GetPublicMessage(formIssue);

        if (antiforgeryMessage is not null)
        {
            ViewBag.LoginError = antiforgeryMessage;
            ModelState.AddModelError(string.Empty, antiforgeryMessage);
        }
        else if (TempData.TryGetValue("LoginError", out var loginError) && loginError is not null)
        {
            // Clears a stale application auth cookie when a MOVEit token/session problem sends the user back here
            // before showing the error so the browser cannot immediately bounce back into the protected UI.
            await _applicationSessionService.ResetForLoginAsync(HttpContext);
            ViewBag.LoginError = loginError.ToString();
            ModelState.AddModelError(string.Empty, loginError.ToString()!);
        }
        else if (User.Identity?.IsAuthenticated == true)
        {
            if (_upgradeState.IsPending)
            {
                if (MauiUpgradeAuthorization.HasCurrentAuthorization(User, _upgradeState.TargetVersion))
                {
                    return RedirectToAction("Index", "Upgrade");
                }

                // A normal/stale MAUI cookie is never sufficient to authorize an upgrade. Force one fresh
                // MOVEit/Admin login and issue an authorization claim tied to this exact target package.
                await _applicationSessionService.ResetForLoginAsync(HttpContext);
                const string upgradeAdminMessage = "A MAUI upgrade is pending. A MAUI administrator must sign in and complete the upgrade before normal access can resume.";
                ViewBag.LoginError = upgradeAdminMessage;
                ModelState.AddModelError(string.Empty, upgradeAdminMessage);
            }
            else if (_applicationSessionService.HasLiveMoveItSession)
            {
                // Explicitly select the Dashboard action within the current IIS application.
                // The application root defaults to Account/Login, not the Dashboard.
                return Redirect(MauiLifecyclePaths.BrowserUrl(Request.PathBase, UserPreferenceStartPages.ResolvePath(null)));
            }
            else
            {
                // Resets local authentication when the cookie survives but its server-side MOVEit session does not.
                await _applicationSessionService.ResetForLoginAsync(HttpContext);
            }
        }
        else if (_upgradeState.IsPending)
        {
            const string upgradePendingMessage = "A MAUI upgrade is pending. A MAUI administrator may sign in to complete the upgrade; all other access is temporarily blocked.";
            ViewBag.LoginError = upgradePendingMessage;
            ModelState.AddModelError(string.Empty, upgradePendingMessage);
        }

        // [AllowAnonymous] does not clear a previously authenticated cookie principal.
        // Issue login form tokens against the same anonymous identity as the login POST.
        MauiLoginAntiforgeryIdentity.UseAnonymousPrincipal(HttpContext);
        PopulateServerChoices(null);
        return View();
    }

    // Authenticates against the MOVEit server selected by the user. A successful token response is the
    // active-node check; the passive HA node is expected not to issue a usable token. The resulting MOVEit
    // tokens are stored server-side before creating the normal ASP.NET authentication cookie.
    [AllowAnonymous]
    [HttpPost]
    // Purpose: Authenticate the selected MOVEit server and establish the MAUI session.
    public async Task<IActionResult> Login(string username, string password, string server, CancellationToken cancellationToken)
    {
        // Keep the anonymous sign-in security boundary consistent with the Login GET.
        // All other authenticated operations retain normal identity-bound CSRF validation.
        MauiLoginAntiforgeryIdentity.UseAnonymousPrincipal(HttpContext);

        // Validate the login antiforgery token explicitly so a token left open across an application restart
        // can recover to a fresh login form instead of being rejected by MVC with a generic HTTP 400 page.
        try
        {
            await _antiforgery.ValidateRequestAsync(HttpContext);
        }
        catch (AntiforgeryValidationException ex)
        {
            // Diagnostics describe validation category and token PRESENCE only, never contents.
            // This executes before MOVEit authentication and must not fail silently.
            var formTokenPresent = Request.HasFormContentType
                && Request.Form.ContainsKey("__RequestVerificationToken");
            var cookiePresent = Request.Cookies.ContainsKey(MauiAuthenticationDefaults.AntiforgeryCookieName);
            var diagnosticCode = MauiAntiforgeryFailure.Classify(ex);
            _logger.LogWarning(
                ex,
                "Login antiforgery validation failed before MOVEit authentication. Code={DiagnosticCode}, PathBase={PathBase}, Path={Path}, AntiforgeryCookiePresent={CookiePresent}, FormTokenPresent={FormTokenPresent}.",
                diagnosticCode,
                Request.PathBase.Value ?? string.Empty,
                Request.Path.Value ?? string.Empty,
                cookiePresent,
                formTokenPresent);

            // Cookie-backed TempData may be unreadable when Data Protection itself fails.
            // Clear the rejected cookie, then redirect using an allowlisted status code only.
            await _applicationSessionService.ResetForLoginAsync(HttpContext, clearAntiforgeryCookie: true);
            var loginUrl = MauiLifecyclePaths.BrowserUrl(Request.PathBase, MauiLifecyclePaths.Login);
            return Redirect(loginUrl + "?formIssue=" + diagnosticCode);
        }

        username = MauiUsername.Normalize(username);
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            ModelState.AddModelError(string.Empty, "Enter a username and password to continue.");
            PopulateServerChoices(server);
            return View();
        }

        try
        {
            _logger.LogInformation("Login attempt for MOVEit user {Username} using configured server key {ServerKey}.", username.Trim(), server);

            // Uses the MOVEit password-grant authentication flow. The REST client returns the MOVEit token
            // state; it does not control the browser's application authentication state.
            var moveItState = await _restClient.AuthenticateAsync(server, username.Trim(), password, cancellationToken);

            // Read the native session/server capability snapshot once per successful login. A failure of this
            // optional v0 information endpoint must not prevent a user who successfully authenticated from using MAUI.
            // MOVEit's administrator flag is retained as capability metadata only; MAUI Admin/User authorization is database-backed and independent.
            try
            {
                moveItState.Info = await _restClient.GetInfoAsync(moveItState, cancellationToken);
                _logger.LogInformation(
                    "Loaded MOVEit server information for {Username}: version {Version}, build {Build}, MOVEit admin {IsAdmin}.",
                    moveItState.Username,
                    moveItState.Info.AppVersion,
                    moveItState.Info.AppBuildNumber,
                    moveItState.Info.IsAdmin);
            }
            catch (MoveItApiException ex)
            {
                _logger.LogWarning(ex, "MOVEit /api/v0/info was unavailable after login; continuing without session capability metadata.");
            }

            // A pending upgrade uses a deliberately read-only authorization path. Do not run normal
            // successful-login enrollment here because an upgrade login must not modify SecurityUsers before
            // Begin Upgrade is selected. Existing MAUI Admin role data is authoritative. An empty security
            // table is treated as bootstrap state so the first installation administrator cannot be locked out.
            if (_upgradeState.IsPending)
            {
                var configuredRole = _securityService.GetRole(moveItState.Username);
                var noConfiguredUsers = _securityService.GetUsers().Count == 0;
                var isUpgradeAdmin = configuredRole.Equals(MauiSecurityRoles.Admin, StringComparison.OrdinalIgnoreCase)
                    || noConfiguredUsers;

                if (!isUpgradeAdmin)
                {
                    SetLoginError(
                        "A MAUI upgrade is pending. A MAUI administrator must complete the upgrade before normal access can resume.",
                        username,
                        server);
                    _logger.LogInformation(
                        "Blocked upgrade login for {Username}: database-backed MAUI role {Role}.",
                        moveItState.Username,
                        configuredRole);
                    return View();
                }

                await _applicationSessionService.SignInAsync(HttpContext, moveItState, MauiSecurityRoles.Admin, _upgradeState.TargetVersion);
                _logger.LogInformation(
                    "Authenticated {Username} for the pending MAUI upgrade and issued fresh authorization for target {TargetVersion} without modifying MAUI security data.",
                    moveItState.Username,
                    _upgradeState.TargetVersion);
                return RedirectToAction("Index", "Upgrade");
            }

            // Warm only the authenticated principal's effective creation grants. Failure does
            // not reject a valid login; subsequent UI/action checks fail closed and may retry.
            if (_objectAccess is not null)
            {
                try
                {
                    using var permissionSession = _moveItSessionService.PushBackgroundSession(moveItState);
                    _ = await _objectAccess.GetCreationAsync(true, cancellationToken);
                }
                catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or HttpRequestException)
                { _logger.LogWarning(ex, "MOVEit creation permissions could not be loaded at login."); }
            }

            // Normal MAUI authorization is layered on top of successful MOVEit authentication. SecurityUsers
            // in the MAUI metadata database is the only role authority: the first successful user becomes Admin,
            // later unseen users start as User, and LastLoginUtc is updated on every successful sign-in.
            var securityUser = await _securityService.RecordSuccessfulLoginAsync(
                moveItState.Username,
                cancellationToken);

            await _applicationSessionService.SignInAsync(HttpContext, moveItState, securityUser.Role);

            _logger.LogInformation("Application login completed for MOVEit user {Username} on {ServerName} with MAUI role {Role}.", moveItState.Username, moveItState.ServerDisplayName, securityUser.Role);

            var preferences = await _userPreferences.GetAsync(moveItState.GetEnvironmentKey(), moveItState.Username, cancellationToken);
            // Preserve the selected Dashboard/Tasks/Hosts start page using a canonical route
            // beneath the active IIS PathBase rather than relying on the application root.
            var destination = MauiLifecyclePaths.BrowserUrl(Request.PathBase, UserPreferenceStartPages.ResolvePath(preferences.StartPage));
            _logger.LogInformation("Successful login redirect target: {Destination}.", destination);
            return Redirect(destination);
        }
        catch (MoveItApiException ex)
        {
            SetLoginError(ex.Message, username, server);
            _logger.LogWarning(ex, "MOVEit login failed for user {Username}.", username.Trim());
            return View();
        }
        catch (HttpRequestException ex)
        {
            SetLoginError($"Unable to reach MOVEit Web Admin: {ex.Message}", username, server);
            _logger.LogWarning(ex, "MOVEit Web Admin could not be reached during login.");
            return View();
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            SetLoginError($"The MOVEit login request timed out: {ex.Message}", username, server);
            _logger.LogWarning(ex, "MOVEit login request timed out.");
            return View();
        }
        catch (Exception ex)
        {
            SetLoginError($"Unexpected login error: {ex.Message}", username, server);
            _logger.LogError(ex, "Unexpected application login failure.");
            return View();
        }
    }


    // Displays a normal authenticated access-denied page when a User attempts an Admin-only MAUI action.
    [Authorize]
    [HttpGet]
    public IActionResult AccessDenied()
    {
        return View();
    }

    // Records deliberate browser activity in the authoritative server-side idle timestamp.
    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Activity()
    {
        return _applicationSessionService.RecordUserActivity() ? NoContent() : Unauthorized();
    }

    // Terminates an idle/expired MAUI session and returns the browser to Login with a clear one-time message.
    // This endpoint is anonymous intentionally: the browser may discover expiration only after the auth cookie
    // has already become invalid. When a live principal/session is still available, LogoutAsync also attempts
    // normal MOVEit token revocation before local authentication state is cleared.
    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> SessionExpired(CancellationToken cancellationToken)
    {
        var username = User.Identity?.IsAuthenticated == true ? User.Identity.Name : null;

        try
        {
            await _restClient.LogoutAsync(cancellationToken);
        }
        finally
        {
            await _applicationSessionService.SignOutAsync(HttpContext);
        }

        if (!string.IsNullOrWhiteSpace(username))
        {
            _logger.LogInformation("Application session expired for MOVEit user {Username}; returning to Login.", username);
        }
        else
        {
            _logger.LogInformation("Expired application session detected; returning to Login.");
        }

        return RedirectToAction(nameof(Login), new { expired = true });
    }

    // Revokes the MOVEit access token when possible, removes local session state, and signs out the app cookie.
    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await _restClient.LogoutAsync(cancellationToken);
        await _applicationSessionService.SignOutAsync(HttpContext);
        return RedirectToAction(nameof(Login));
    }

    // Keeps a login failure visible after a POST and preserve the user's non-sensitive form selections.
    private void SetLoginError(string message, string? username, string? selectedServer)
    {
        ViewBag.LoginError = message;
        ModelState.AddModelError(string.Empty, message);
        ViewBag.Username = MauiUsername.Normalize(username);
        PopulateServerChoices(selectedServer);
    }

    // Dismisses the shared System Message for the remainder of the current authenticated MAUI session.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult DismissSystemMessage()
    {
        if (!_moveItSessionService.DismissSystemMessage()) return Unauthorized();
        return Json(new { success = true });
    }

    // Populates the login dropdown from the configured MOVEit Web Admin connections.
    private void PopulateServerChoices(string? selectedServer)
    {
        var servers = _servers.GetServers();
        ViewBag.Servers = servers;
        ViewBag.DefaultServer = !string.IsNullOrWhiteSpace(selectedServer) && _servers.FindByKey(selectedServer) is not null
            ? selectedServer
            : servers.FirstOrDefault()?.Key ?? string.Empty;
    }
}
