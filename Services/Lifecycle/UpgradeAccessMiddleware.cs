// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Suspends normal application access while an authenticated administrator upgrade is pending.

using Microsoft.AspNetCore.Mvc.Controllers;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.Authentication;
using MoveItAutomationUI.Services.Upgrade;

namespace MoveItAutomationUI.Services.Lifecycle;

public sealed class UpgradeAccessMiddleware
{
    private readonly RequestDelegate _next;

    // Purpose: Initializes the authenticated upgrade access gate.
    public UpgradeAccessMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    // Purpose: Enforces pending-upgrade access using a fresh, target-version-specific authorization claim.
    public async Task InvokeAsync(
        HttpContext context,
        MauiSetupState setupState,
        MauiUpgradeState upgradeState)
    {
        // Do not resolve authenticated session services during anonymous first-run Setup:
        // their MOVEit runtime configuration must not be initialized from the empty seed.
        if (!setupState.IsConfiguredAtStartup)
        {
            await _next(context);
            return;
        }

        // Use routed MVC endpoint metadata as well as the request path so lifecycle routes remain
        // identifiable even when conventional route defaults produce a shorter URL.
        var actionDescriptor = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        var endpointController = actionDescriptor?.ControllerName ?? string.Empty;
        var endpointAction = actionDescriptor?.ActionName ?? string.Empty;

        var isUpgradePath = MauiLifecyclePaths.IsUpgrade(context.Request.Path)
            || endpointController.Equals("Upgrade", StringComparison.OrdinalIgnoreCase);
        var isLoginPath = MauiLifecyclePaths.IsLogin(context.Request.Path)
            || IsControllerAction(endpointController, endpointAction, "Account", "Login");
        var isLogoutPath = MauiLifecyclePaths.IsLogout(context.Request.Path)
            || IsControllerAction(endpointController, endpointAction, "Account", "Logout");
        var isSessionExpiredPath = MauiLifecyclePaths.IsSessionExpired(context.Request.Path)
            || IsControllerAction(endpointController, endpointAction, "Account", "SessionExpired");
        var hasUpgradeClaim = MauiUpgradeAuthorization.HasUpgradeClaim(context.User);
        var hasCurrentUpgradeAuthorization = MauiUpgradeAuthorization.HasCurrentAuthorization(context.User, upgradeState.TargetVersion);
        var isRestartStatus = IsControllerAction(endpointController, endpointAction, "Upgrade", "RestartStatus");

        // The Upgrade page polls this endpoint across the process recycle. It must remain reachable
        // both before shutdown and after the restarted process determines that no upgrade is pending.
        if (isRestartStatus)
        {
            await _next(context);
            return;
        }

        if (!upgradeState.IsPending)
        {
            // Upgrade-only authorization must never turn into a normal MAUI session after the application restarts.
            // Clear it and require the normal login path once the target package is current.
            if (hasUpgradeClaim)
            {
                await context.RequestServices.GetRequiredService<IMauiApplicationSessionService>().ResetForLoginAsync(context);
                if (!isLoginPath)
                {
                    context.Response.Redirect(MauiLifecyclePaths.ForApplication(context.Request, MauiLifecyclePaths.Login));
                    return;
                }
            }

            if (isUpgradePath)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await _next(context);
            return;
        }

        if (isLoginPath || isLogoutPath || isSessionExpiredPath)
        {
            await _next(context);
            return;
        }

        if (isUpgradePath)
        {
            // The MOVEit credentials were already validated by Account/Login. Upgrade execution only touches
            // MAUI configuration/database state, so do not require the transient MOVEit token cache here.
            if (!hasCurrentUpgradeAuthorization)
            {
                context.Response.Redirect(MauiLifecyclePaths.ForApplication(context.Request, MauiLifecyclePaths.Login));
                return;
            }

            await _next(context);
            return;
        }

        if (hasCurrentUpgradeAuthorization)
        {
            context.Response.Redirect(MauiLifecyclePaths.ForApplication(context.Request, MauiLifecyclePaths.Upgrade));
            return;
        }

        context.Response.Redirect(MauiLifecyclePaths.ForApplication(context.Request, MauiLifecyclePaths.Login));
    }

    // Purpose: Matches the MVC endpoint selected by routing, independent of the URL shape used to reach it.
    private static bool IsControllerAction(
        string endpointController,
        string endpointAction,
        string expectedController,
        string expectedAction)
        => endpointController.Equals(expectedController, StringComparison.OrdinalIgnoreCase)
            && endpointAction.Equals(expectedAction, StringComparison.OrdinalIgnoreCase);
}
