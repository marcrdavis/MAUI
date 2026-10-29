// Author: MAUI project
// Validates user-supplied browser return URLs within the current IIS Application.
// External MOVEit REST routes and generated MVC actions must not use this policy.

using Microsoft.AspNetCore.Http;

namespace MoveItAutomationUI.Services.Lifecycle;

public static class MauiApplicationReturnUrls
{
    // A website-local URL can still leave a nested IIS application. Validate both
    // the active PathBase boundary and dot-segments before rendering Back links.
    public static bool IsWithinApplication(string? value, PathString pathBase)
    {
        var candidate = (value ?? string.Empty).Trim();
        if (candidate.Length == 0 || !candidate.StartsWith("/", StringComparison.Ordinal)
            || candidate.StartsWith("//", StringComparison.Ordinal) || candidate.Contains('\\'))
            return false;
        if (!Uri.TryCreate(candidate, UriKind.Relative, out _)) return false;

        var pathEnd = candidate.IndexOfAny(new[] { '?', '#' });
        var path = pathEnd < 0 ? candidate : candidate[..pathEnd];
        // Browsers can collapse both plain and encoded dot segments, or interpret
        // encoded slashes as path separators; these are not safe Back targets.
        foreach (var segment in path.Split('/'))
        {
            var decoded = Uri.UnescapeDataString(segment);
            if (decoded is "." or ".." || decoded.Contains('/') || decoded.Contains('\\'))
                return false;
        }

        if (!pathBase.HasValue) return true;
        var appBase = pathBase.Value!;
        return path.Equals(appBase, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(appBase + "/", StringComparison.OrdinalIgnoreCase);
    }
}
