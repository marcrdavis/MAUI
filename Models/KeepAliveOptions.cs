// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Live settings for the optional local HTTP keep-alive worker.

namespace MoveItAutomationUI.Models;

public sealed class KeepAliveOptions
{
    public const string SectionName = "KeepAlive";
    public const int DefaultIntervalMinutes = 5;
    public const int MinimumIntervalMinutes = 1;
    public const int MaximumIntervalMinutes = 20;

    public SettingDefinition<bool> Enabled { get; set; } = new()
    {
        Name = "Keep Alive Service", Value = true, Type = "Boolean",
        Description = "When enabled - this service keeps the app pool alive by pinging an endpoint on the server at a set interval. This is necessary to allow scheduled jobs to run when the app pool might otherwise be cold."
    };

    public SettingDefinition<int> IntervalMinutes { get; set; } = new()
    {
        Name = "Ping Interval", Value = DefaultIntervalMinutes, Type = "Number",
        Description = "Minutes between local keep-alive requests (1–20)."
    };

    // A malformed hand-edited interval must not cause a tight background loop.
    public TimeSpan GetInterval()
    {
        var minutes = IntervalMinutes?.Value ?? DefaultIntervalMinutes;
        return TimeSpan.FromMinutes(minutes is >= MinimumIntervalMinutes and <= MaximumIntervalMinutes
            ? minutes : DefaultIntervalMinutes);
    }
}
