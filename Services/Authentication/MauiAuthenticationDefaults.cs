// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.Authentication;

public static class MauiAuthenticationDefaults
{
    public const string AuthenticationCookieName = ".MoveItAutomationUI.Auth";
    public const string AntiforgeryCookieName = ".MoveItAutomationUI.Antiforgery";
    public const string UserDisplayCookieName = "MoveItUiUser";
    public const string ServerDisplayCookieName = "MoveItUiServer";
    public const string ServerKeyClaimType = "MoveItServerKey";
    public const string ServerNameClaimType = "MoveItServerName";
    public const string UpgradeTargetVersionClaimType = "MauiUpgradeTargetVersion";

    // Returns the shared browser-authentication and server-side idle-session lifetime.
    public static TimeSpan GetSessionLifetime(MoveItOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return GetSessionLifetime(options.IdleSessionTimeoutMinutes.Value);
    }

    // Allows the effective startup-sensitive runtime snapshot to supply the timeout after Setup.
    public static TimeSpan GetSessionLifetime(int minutes)
    {
        if (!MoveItOptions.SupportedIdleSessionTimeoutMinutes.Contains(minutes)) minutes = 30;
        return TimeSpan.FromMinutes(minutes);
    }
}
