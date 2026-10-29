// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

/// <summary>
/// Keeps Script-specific cache and UI refresh settings independent from Tasks and Hosts.
/// </summary>
public sealed class ScriptBehaviorOptions
{
    public const string SectionName = "Scripts";

    public SettingDefinition<int> CacheRefreshSeconds { get; set; } = new()
    {
        Name = "Script Cache Refresh Seconds",
        Value = 30,
        Description = "Maximum age of cached Script data for non-forced reads. Set to 0 to disable age-based refresh; Refresh Now still forces a live read.",
        Type = "Numeric",
        PossibleValues = new List<int>()
    };

    public SettingDefinition<int> UiRefreshIntervalSeconds { get; set; } = new()
    {
        Name = "UI Script Refresh Interval",
        Value = 60,
        Description = "Seconds between automatic Script grid refresh requests while the page is visible. Requests honor the backend cache age; set to 0 to disable automatic refresh.",
        Type = "Numeric",
        PossibleValues = new List<int>()
    };
}
