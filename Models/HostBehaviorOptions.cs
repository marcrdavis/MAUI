// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

/// <summary>
/// Keeps Host-specific UI/runtime behavior settings in this configuration model.
/// Mirrors the Task cache and automatic refresh controls while remaining independently configurable.
/// </summary>
public sealed class HostBehaviorOptions
{
    public const string SectionName = "Hosts";

    public SettingDefinition<int> CacheRefreshSeconds { get; set; } = new()
    {
        Name = "Host Cache Refresh Seconds",
        Value = 30,
        Description = "Maximum age of cached Host data for non-forced reads. Set to 0 to disable age-based refresh; Refresh Now still forces a live read.",
        Type = "Numeric",
        PossibleValues = new List<int>()
    };

    public SettingDefinition<int> UiRefreshIntervalSeconds { get; set; } = new()
    {
        Name = "UI Host Refresh Interval",
        Value = 10,
        Description = "Seconds between automatic Host grid refresh requests while the page is visible. Requests honor the backend cache age; set to 0 to disable automatic refresh.",
        Type = "Numeric",
        PossibleValues = new List<int>()
    };
}
