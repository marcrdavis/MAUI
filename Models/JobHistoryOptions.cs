// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Retention policy for execution history owned by MAUI scheduled jobs.

namespace MoveItAutomationUI.Models;

public sealed class JobHistoryOptions
{
    public const string SectionName = "JobHistory";
    public const int DefaultRetentionDays = 30;
    public static readonly IReadOnlyList<int> SupportedRetentionDays = new[] { 7, 14, 30, 60, 90, 180, 365 };

    public SettingDefinition<int> RetentionDays { get; set; } = new()
    {
        Name = "Job History Retention",
        Value = DefaultRetentionDays,
        Description = "Number of days MAUI retains execution history for scheduled jobs, including individual Report run history.",
        Type = "List",
        PossibleValues = SupportedRetentionDays.ToList()
    };
}
