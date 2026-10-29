// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Centralizes the target-version-specific authorization contract used while a MAUI upgrade is pending.

using System.Security.Claims;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.Authentication;

public static class MauiUpgradeAuthorization
{
    // Returns true when the authenticated MAUI Admin cookie was issued specifically for the current package target.
    public static bool HasCurrentAuthorization(ClaimsPrincipal user, string targetVersion)
    {
        if (user?.Identity?.IsAuthenticated != true) return false;
        if (!user.IsInRole(MauiSecurityRoles.Admin)) return false;

        var authorizedTarget = user.FindFirst(MauiAuthenticationDefaults.UpgradeTargetVersionClaimType)?.Value;
        return !string.IsNullOrWhiteSpace(targetVersion)
            && string.Equals(authorizedTarget, targetVersion, StringComparison.OrdinalIgnoreCase);
    }

    // Returns true when this cookie contains any upgrade-only target claim.
    public static bool HasUpgradeClaim(ClaimsPrincipal user) =>
        !string.IsNullOrWhiteSpace(user?.FindFirst(MauiAuthenticationDefaults.UpgradeTargetVersionClaimType)?.Value);
}
