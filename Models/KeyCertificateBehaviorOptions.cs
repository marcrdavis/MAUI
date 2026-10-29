// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

public sealed class KeyCertificateBehaviorOptions
{
    public const string SectionName = "KeysAndCertificates";

    public SettingDefinition<int> CacheRefreshSeconds { get; set; } = new()
    {
        Name = "Keys and Certificates Cache Refresh Seconds",
        Value = 30,
        Description = "Maximum age of cached Keys and Certificates data for non-forced reads. Set to 0 to disable age-based refresh; Refresh Now still forces a live read.",
        Type = "Numeric"
    };

    public SettingDefinition<int> UiRefreshIntervalSeconds { get; set; } = new()
    {
        Name = "UI Keys and Certificates Refresh Interval",
        Value = 60,
        Description = "Seconds between automatic Keys and Certificates grid refresh requests while the page is visible. Requests honor the backend cache age; set to 0 to disable automatic refresh.",
        Type = "Numeric"
    };
}
