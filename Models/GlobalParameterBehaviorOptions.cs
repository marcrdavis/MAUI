// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

public sealed class GlobalParameterBehaviorOptions
{
    public const string SectionName = "GlobalParameters";

    public SettingDefinition<int> CacheRefreshSeconds { get; set; } = new()
    {
        Name = "Global Parameter Cache Refresh Seconds",
        Value = 30,
        Description = "Maximum age of cached Global Parameter data for non-forced reads. Set to 0 to disable age-based refresh; Refresh Now still forces a live read.",
        Type = "Numeric"
    };

    public SettingDefinition<int> UiRefreshIntervalSeconds { get; set; } = new()
    {
        Name = "UI Global Parameter Refresh Interval",
        Value = 60,
        Description = "Number of seconds between automatic Global Parameter grid requests while the page is visible. Automatic requests honor the backend cache age; Refresh Now forces a live MOVEit read. Set to 0 to disable automatic UI refresh.",
        Type = "Numeric"
    };
}
