// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

public sealed class RecycleBinOptions
{
    public const string SectionName = "RecycleBin";
    public const int DefaultRetentionDays = 30;
    public const int MinimumRetentionDays = 1;
    public const int MaximumRetentionDays = 365;
    public static readonly IReadOnlyList<int> SupportedRetentionDays = new[] { 7, 14, 30, 60, 90, 180, 365 };

    public SettingDefinition<bool> Enabled { get; set; } = new()
    {
        Name = "Enable Recycle Bin",
        Value = true,
        Description = "When enabled, MAUI attempts to save deleted Tasks, Hosts, custom Scripts, and Date Lists so they can be restored later.",
        Type = "Boolean",
        PossibleValues = new List<bool> { false, true }
    };

    public SettingDefinition<int> RetentionDays { get; set; } = new()
    {
        Name = "Recycle Bin Retention",
        Value = DefaultRetentionDays,
        Description = "Number of days deleted items remain available in the MAUI Recycle Bin before automatic cleanup.",
        Type = "List",
        PossibleValues = SupportedRetentionDays.ToList()
    };
}
