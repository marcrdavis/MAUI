// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Central authorization rules for outbound Report data sources.

namespace MoveItAutomationUI.Services.Reporting;

public static class OutboundReportSourcePolicy
{
    // Logs contain MAUI application diagnostics and are restricted to current MAUI Administrators.
    public static bool RequiresAdmin(string? gridKey)
        => string.Equals((gridKey ?? string.Empty).Trim(), "logs", StringComparison.OrdinalIgnoreCase);

    public static bool IsAllowed(string? gridKey, bool isAdmin)
        => !RequiresAdmin(gridKey) || isAdmin;

    public static void EnsureAllowed(string? gridKey, bool isAdmin)
    {
        if (!IsAllowed(gridKey, isAdmin))
            throw new InvalidOperationException("This Report source requires MAUI Administrator access.");
    }
    // JSON matching is currently a grid-only read. Scheduled Reports have no definition matcher;
    // reject this search explicitly instead of saving/executing an ordinary-text filter that returns no rows.
    public static void EnsureScheduledSearchSupported(string? gridKey, string? searchText)
    {
        var key = (gridKey ?? string.Empty).Trim();
        if ((key.Equals("tasks", StringComparison.OrdinalIgnoreCase) || key.Equals("hosts", StringComparison.OrdinalIgnoreCase))
            && searchText?.StartsWith("JSON:", StringComparison.OrdinalIgnoreCase) == true)
            throw new InvalidOperationException("JSON searches can only be sent now. Clear the JSON search before scheduling a Report.");
    }
}
