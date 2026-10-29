// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

/// <summary>
/// Keeps Task-specific UI/runtime behavior settings in this configuration model.
/// Retains setting metadata so the Configuration page can use it later.
/// </summary>
public sealed class TaskBehaviorOptions
{
    public const string SectionName = "Tasks";

    public SettingDefinition<int> CacheRefreshSeconds { get; set; } = new()
    {
        Name = "Task Cache Refresh Seconds",
        Value = 30,
        Description = "Maximum age of cached Task data for non-forced reads. Set to 0 to disable age-based refresh; Refresh Now still forces a live read.",
        Type = "Numeric",
        PossibleValues = new List<int>()
    };

    public SettingDefinition<int> UiRefreshIntervalSeconds { get; set; } = new()
    {
        Name = "UI Task Refresh Interval",
        Value = 10,
        Description = "Seconds between automatic Task grid refresh requests while the page is visible. Requests honor the backend cache age; set to 0 to disable automatic refresh.",
        Type = "Numeric",
        PossibleValues = new List<int>()
    };
}
