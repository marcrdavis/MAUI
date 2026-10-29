// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Shared comparison rules for MAUI package/install version strings.

namespace MoveItAutomationUI.Services.Upgrade;

public static class MauiVersionRules
{
    // Purpose: Returns whether the installed version is newer than the deployed package version.
    public static bool IsNewer(string installedVersion, string targetVersion)
    {
        return TryParse(installedVersion, out var installed)
            && TryParse(targetVersion, out var target)
            && installed > target;
    }

    // Purpose: Returns whether the installed version is older than the package's minimum supported version.
    public static bool IsOlder(string installedVersion, string minimumVersion)
    {
        if (string.IsNullOrWhiteSpace(minimumVersion)) return false;
        return TryParse(installedVersion, out var installed)
            && TryParse(minimumVersion, out var minimum)
            && installed < minimum;
    }

    // Purpose: Parses MAUI semantic-style version strings while ignoring release/build suffixes.
    public static bool TryParse(string? text, out Version version)
    {
        var value = text?.Trim().TrimStart('v', 'V') ?? string.Empty;
        var suffix = value.IndexOfAny(new[] { '-', '+' });
        if (suffix >= 0) value = value[..suffix];
        if (Version.TryParse(value, out var parsed) && parsed is not null)
        {
            version = parsed;
            return true;
        }

        version = new Version(0, 0);
        return false;
    }
}
