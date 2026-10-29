// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI

namespace MoveItAutomationUI.Models;

// One spelling for domain separators; preserve the supplied case for native authentication/display.
public static class MauiUsername
{
    public static string Normalize(string? username) => (username ?? string.Empty).Trim().Replace('/', '\\');

    // Invariant casing makes identity comparisons independent of culture and database collation.
    public static string GetKey(string? username) => Normalize(username).ToUpperInvariant();
}
