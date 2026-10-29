// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Shared grid defaults for first-class MAUI Report administration.
// "Reports" here means MAUI-created/scheduled Reports; MOVEit Automation native report/history data is exposed as Records.

namespace MoveItAutomationUI.Models;

public static class ReportsGridDefaults
{
    public const string GridKey = "reports";

    public static readonly IReadOnlyList<string> DefaultColumns = new[]
    {
        "ReportName", "Source", "Schedule", "Enabled", "NextRun", "LastRun", "LastStatus"
    };

    public static readonly IReadOnlyList<string> AvailableColumns = new[]
    {
        "Id", "ReportName", "Description", "Source", "Environment", "Recipients", "Schedule", "Enabled",
        "NextRun", "LastRun", "LastStatus", "LastMessage", "Owner", "Created", "Modified"
    };

    public static readonly IReadOnlyList<string> SearchColumns = new[]
    {
        "ReportName", "Description", "Source", "Environment", "Recipients", "Schedule", "LastStatus", "LastMessage", "Owner"
    };

    public static GridOptions Create() => new()
    {
        DefaultColumns = DefaultColumns.ToList(),
        AvailableColumns = AvailableColumns.ToList(),
        BulkActions = new List<string> { "Run Reports", "Export Reports", "Delete Reports" },
        AddActions = new List<string> { "Import Reports" },
        SearchColumns = SearchColumns.ToList()
    };
}
