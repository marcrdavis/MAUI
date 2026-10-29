// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

public sealed class DateListBehaviorOptions
{
    public const string SectionName = "DateLists";
    public SettingDefinition<int> CacheRefreshSeconds { get; set; } = new() { Name = "Date List Cache Refresh Seconds", Value = 30, Description = "Maximum age of cached Date List data for non-forced reads. Set to 0 to disable age-based refresh; Refresh Now still forces a live read.", Type = "Numeric" };
    public SettingDefinition<int> UiRefreshIntervalSeconds { get; set; } = new() { Name = "UI Date List Refresh Interval", Value = 60, Description = "Number of seconds between automatic Date List grid requests while the page is visible. Automatic requests honor the backend cache age; Refresh Now forces a live MOVEit read.", Type = "Numeric" };
}
