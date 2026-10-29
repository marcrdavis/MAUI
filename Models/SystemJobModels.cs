// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// System Job contracts used by Configuration > Settings > Scheduler.

namespace MoveItAutomationUI.Models;

public static class MauiSystemJobKeys
{
    public const string Reports = "reports";
    public const string Backup = "backup";
    public const string BackupCleanup = "backup-cleanup";
    public const string JobHistoryCleanup = "job-history-cleanup";
    public const string HostHealthCheck = "host-health-check";
}

public static class MauiSystemJobResults
{
    public const string Successful = "Successful";
    public const string CompletedWithWarnings = "Completed with Warnings";
    public const string Failed = "Failed";
}

public static class MauiSystemJobScheduleKinds
{
    public const string Daily = "Daily";
    public const string Interval = "Interval";
    public const string ExternallyManaged = "ExternallyManaged";
}

public sealed record MauiSystemJobDefinition(
    string Key,
    string Name,
    string Description,
    string ScheduleKind,
    bool DefaultEnabled,
    string DefaultRunTime,
    int IntervalMinutes,
    bool VisibleInScheduler = true)
{
    public bool IsDaily => ScheduleKind == MauiSystemJobScheduleKinds.Daily;
    public bool IsInterval => ScheduleKind == MauiSystemJobScheduleKinds.Interval;
    public bool IsExternallyManaged => ScheduleKind == MauiSystemJobScheduleKinds.ExternallyManaged;
    public bool ScheduleEditable => VisibleInScheduler && IsDaily;
}

public sealed record MauiSystemJobState(
    string Key,
    string Name,
    string Description,
    bool Enabled,
    string RunTime,
    string ScheduleDisplay,
    bool ScheduleEditable,
    DateTimeOffset? NextRunUtc,
    DateTimeOffset? LastRunUtc,
    string LastResult,
    string LastMessage,
    bool IsRunning);

public sealed class MauiSystemJobUpdateRequest
{
    public string Key { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string RunTime { get; set; } = string.Empty;
}

public sealed class MauiSystemJobRunRequest
{
    public string Key { get; set; } = string.Empty;
}

public sealed record MauiSystemJobExecutionOutcome(string Result, string Message)
{
    public static MauiSystemJobExecutionOutcome Successful(string message) => new(MauiSystemJobResults.Successful, message);
    public static MauiSystemJobExecutionOutcome CompletedWithWarnings(string message) => new(MauiSystemJobResults.CompletedWithWarnings, message);
    public static MauiSystemJobExecutionOutcome Failed(string message) => new(MauiSystemJobResults.Failed, message);
}

public static class HostHealthStates
{
    public const string Healthy = "Healthy";
    public const string Unhealthy = "Unhealthy";
    public const string Unknown = "Unknown";
}

public static class HostHealthMetadata
{
    // Reserved system-managed fields. They intentionally do not appear in the editable Metadata Fields catalog.
    public const string HealthField = "__MAUI_Health";
    public const string LastCheckUtcField = "__MAUI_LastHealthCheckUtc";
    public const string DetailField = "__MAUI_HealthDetail";
}
