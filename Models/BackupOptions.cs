// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Automatic backup settings and defaults.

namespace MoveItAutomationUI.Models;

public sealed class BackupOptions
{
    public const string SectionName = "Backup";
    public const int DefaultFrequencyHours = 24;
    public const int DefaultRetentionDays = 30;
    public static readonly IReadOnlyList<int> SupportedFrequencyHours = new[] { 4, 8, 12, 24 };
    public static readonly IReadOnlyList<int> SupportedRetentionDays = Enumerable.Range(1, 30).ToArray();

    public SettingDefinition<bool> Enabled { get; set; } = new()
    {
        Name = "Enable Automatic Backup of MAUI and MOVEit Configuration Data",
        Value = false,
        Description = "When enabled, MAUI automatically backs up each configured MOVEit Automation environment, appsettings.json, and the MAUI SQLite metadata database when SQLite is configured.",
        Type = "Boolean",
        PossibleValues = new List<bool> { false, true }
    };

    public SettingDefinition<int> FrequencyHours { get; set; } = new()
    {
        Name = "Backup Frequency",
        Value = DefaultFrequencyHours,
        Description = "How often MAUI creates an automatic configuration backup.",
        Type = "List"
    };

    public SettingDefinition<int> RetentionDays { get; set; } = new()
    {
        Name = "Backup Retention",
        Value = DefaultRetentionDays,
        Description = "How many days MAUI backup folders are retained before the Backup Cleanup System Job removes them.",
        Type = "List",
        PossibleValues = Enumerable.Range(1, 30).ToList()
    };
}
