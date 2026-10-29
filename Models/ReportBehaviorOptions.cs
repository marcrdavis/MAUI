// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

/// <summary>
/// Controls MOVEit Automation native-report batch retrieval and automatic refresh for the MAUI Records workspace.
/// MOVEit calls these Task Run, File Activity, and Audit data sets reports; MAUI presents them as Records
/// to distinguish them from MAUI-generated Reports. The existing configuration section name remains "Reports".
/// Batches are intentionally bounded because very large payloads can stall both MOVEit Web Admin requests and the browser grid.
/// </summary>
public sealed class ReportBehaviorOptions
{
    public const string SectionName = "Reports";
    public const int MaximumEntriesPerReport = 10000;
    public const int TaskRunDefaultBatchSize = 2000;
    public const int FileActivityDefaultBatchSize = 1000;
    public const int AuditDefaultBatchSize = 1000;

    public ReportTypeBehaviorOptions TaskRun { get; set; } = ReportTypeBehaviorOptions.Create("Task Run", TaskRunDefaultBatchSize);
    public ReportTypeBehaviorOptions FileActivity { get; set; } = ReportTypeBehaviorOptions.Create("File Activity", FileActivityDefaultBatchSize);
    public ReportTypeBehaviorOptions Audit { get; set; } = ReportTypeBehaviorOptions.Create("Audit", AuditDefaultBatchSize);
}

public sealed class ReportTypeBehaviorOptions
{
    private static readonly List<int> BatchChoices = new() { 100, 200, 500, 1000, 2000, 5000, 10000 };

    public SettingDefinition<int> BatchResultLimit { get; set; } = new()
    {
        Name = "Limit single batch result entries to",
        Value = 1000,
        Description = "Maximum rows retrieved for one report batch. Higher limits can significantly reduce page and grid performance.",
        Type = "List"
    };

    public SettingDefinition<int> UiRefreshIntervalSeconds { get; set; } = new()
    {
        Name = "UI Report Refresh Interval",
        Value = 60,
        Description = "Number of seconds between live report-grid refreshes while the page is visible. Set to 0 to disable automatic UI refresh.",
        Type = "Numeric",
        PossibleValues = new List<int>()
    };

    // Purpose: Creates report defaults for one report type with a validated batch size.
    public static ReportTypeBehaviorOptions Create(string label, int defaultBatchSize) => new()
    {
        BatchResultLimit = new SettingDefinition<int>
        {
            Name = "Limit single batch result entries to",
            Value = BatchChoices.Contains(defaultBatchSize) ? defaultBatchSize : 1000,
            Description = $"Maximum {label} rows retrieved for one batch. Higher limits can significantly reduce page and grid performance.",
            Type = "List"
        },
        UiRefreshIntervalSeconds = new SettingDefinition<int>
        {
            Name = $"UI {label} Refresh Interval",
            Value = 60,
            Description = $"Number of seconds between live {label} grid refreshes while the page is visible. Set to 0 to disable automatic UI refresh.",
            Type = "Numeric",
            PossibleValues = new List<int>()
        }
    };
}
