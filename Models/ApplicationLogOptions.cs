// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

public sealed class ApplicationLogOptions
{
    public const string SectionName = "ApplicationLogs";
    public const int DefaultRetentionDays = 30;
    public const int MinimumRetentionDays = 1;
    public const int MaximumRetentionDays = 365;
    public static readonly IReadOnlyList<int> SupportedRetentionDays = new[] { 7, 14, 30, 60, 90, 180, 365 };

    public SettingDefinition<int> RetentionDays { get; set; } = new()
    {
        Name = "Log Retention",
        Value = DefaultRetentionDays,
        Description = "Number of days MAUI retains application log events in the metadata database before automatic cleanup.",
        Type = "List",
        PossibleValues = SupportedRetentionDays.ToList()
    };
}

public static class ApplicationLogGridDefaults
{
    public const string GridKey = "logs";

    public static GridOptions Create() => new()
    {
        DefaultColumns = new List<string> { "Timestamp", "Level", "User", "Category", "Message" },
        AvailableColumns = new List<string> { "Id", "Timestamp", "Level", "User", "Environment", "Category", "EventId", "EventName", "RequestPath", "TraceId", "Message", "Exception", "Properties" },
        BulkActions = new List<string>(),
        AddActions = new List<string>(),
        SearchColumns = new List<string> { "Level", "User", "Environment", "Category", "EventName", "RequestPath", "Message", "Exception", "Properties" }
    };
}
