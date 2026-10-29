// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Common schedule contract for hidden System Jobs whose cadence is owned by another MAUI settings area.

using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.SystemJobs;

public sealed record ExternallyManagedSystemJobSchedule(bool Enabled, DateTimeOffset? NextRunUtc);

public interface IExternallyManagedSystemJob
{
    string Key { get; }
    ExternallyManagedSystemJobSchedule GetSchedule(PersistedSystemJobState state, DateTimeOffset utcNow);
    DateTimeOffset? GetNextRunAfterMissedSchedule(PersistedSystemJobState state, DateTimeOffset utcNow);
    DateTimeOffset? GetNextRunAfterExecution(MauiSystemJobExecutionOutcome outcome, DateTimeOffset completedUtc);
}
