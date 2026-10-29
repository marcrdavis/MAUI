// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Hidden System Job that runs automatic backups using the schedule configured under Configuration > Settings > Backup.

using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Backup;

namespace MoveItAutomationUI.Services.SystemJobs;

public interface IBackupSystemJob : IExternallyManagedSystemJob
{
    Task<MauiSystemJobExecutionOutcome> ExecuteAsync(CancellationToken cancellationToken = default);
}

public sealed class BackupSystemJob : IBackupSystemJob
{
    private static readonly TimeSpan FailureRetryDelay = TimeSpan.FromMinutes(15);

    private readonly IMauiBackupService _backupService;
    private readonly IOptionsMonitor<BackupOptions> _options;

    public BackupSystemJob(IMauiBackupService backupService, IOptionsMonitor<BackupOptions> options)
    {
        _backupService = backupService;
        _options = options;
    }

    public string Key => MauiSystemJobKeys.Backup;

    public ExternallyManagedSystemJobSchedule GetSchedule(PersistedSystemJobState state, DateTimeOffset utcNow)
    {
        var options = _options.CurrentValue;
        if (!options.Enabled.Value)
            return new ExternallyManagedSystemJobSchedule(false, null);

        var frequencyHours = NormalizeFrequency(options.FrequencyHours.Value);
        var latestCompletedUtc = _backupService.GetLatestCompletedBackupUtc();
        var calculatedNextRunUtc = latestCompletedUtc?.AddHours(frequencyHours) ?? utcNow;
        var nextRunUtc = calculatedNextRunUtc;

        // A failed/incomplete automatic attempt records its exact retry time after execution completes.
        // Preserve that persisted value here instead of recalculating from LastRunUtc, which represents
        // the start of the attempt and could shorten the intended 15-minute retry after a long backup.
        var lastAttemptNeedsRetry = LastAttemptNeedsRetry(state, latestCompletedUtc);
        if (lastAttemptNeedsRetry)
        {
            nextRunUtc = state.NextRunUtc ?? utcNow.Add(FailureRetryDelay);
        }
        else if (calculatedNextRunUtc <= utcNow
                 && state.NextRunUtc.HasValue
                 && state.NextRunUtc.Value > utcNow)
        {
            // The common scheduler may have intentionally skipped an overdue Backup run after MAUI
            // startup/resume. Preserve that future cadence instead of reintroducing the stale due time.
            nextRunUtc = state.NextRunUtc.Value;
        }

        return new ExternallyManagedSystemJobSchedule(true, nextRunUtc);
    }

    public DateTimeOffset? GetNextRunAfterMissedSchedule(PersistedSystemJobState state, DateTimeOffset utcNow)
    {
        var options = _options.CurrentValue;
        if (!options.Enabled.Value) return null;

        var frequencyHours = NormalizeFrequency(options.FrequencyHours.Value);
        var latestCompletedUtc = _backupService.GetLatestCompletedBackupUtc();

        if (LastAttemptNeedsRetry(state, latestCompletedUtc))
        {
            // A missed retry is not executed immediately when MAUI resumes. Start a fresh retry window.
            return utcNow.Add(FailureRetryDelay);
        }

        var nextRunUtc = state.NextRunUtc
                         ?? latestCompletedUtc?.AddHours(frequencyHours)
                         ?? utcNow;
        while (nextRunUtc <= utcNow)
            nextRunUtc = nextRunUtc.AddHours(frequencyHours);

        return nextRunUtc;
    }

    public async Task<MauiSystemJobExecutionOutcome> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        if (!options.Enabled.Value)
            return MauiSystemJobExecutionOutcome.Successful("Skipped. Automatic Backup is disabled.");

        var frequencyHours = NormalizeFrequency(options.FrequencyHours.Value);
        var result = await _backupService.RunAutomaticIfDueAsync(frequencyHours, "scheduler", cancellationToken);
        if (result is null)
            return MauiSystemJobExecutionOutcome.Successful("Skipped. Automatic Backup is no longer due.");

        if (result.Success)
            return MauiSystemJobExecutionOutcome.Successful("Automatic Backup completed successfully.");

        var detail = result.Messages.LastOrDefault() ?? "Automatic Backup did not complete successfully.";
        if (result.Messages.Any(message => message.StartsWith("Backup incomplete:", StringComparison.OrdinalIgnoreCase)))
            return MauiSystemJobExecutionOutcome.CompletedWithWarnings(detail);

        return MauiSystemJobExecutionOutcome.Failed(detail);
    }

    public DateTimeOffset? GetNextRunAfterExecution(MauiSystemJobExecutionOutcome outcome, DateTimeOffset completedUtc)
    {
        var options = _options.CurrentValue;
        if (!options.Enabled.Value) return null;

        var frequencyHours = NormalizeFrequency(options.FrequencyHours.Value);
        if (string.Equals(outcome.Result, MauiSystemJobResults.Successful, StringComparison.OrdinalIgnoreCase))
        {
            var latestCompletedUtc = _backupService.GetLatestCompletedBackupUtc();
            return latestCompletedUtc?.AddHours(frequencyHours) ?? completedUtc.AddHours(frequencyHours);
        }

        return completedUtc.Add(FailureRetryDelay);
    }

    private static bool LastAttemptNeedsRetry(PersistedSystemJobState state, DateTimeOffset? latestCompletedUtc)
        => state.LastRunUtc.HasValue
           && !string.Equals(state.LastResult, MauiSystemJobResults.Successful, StringComparison.OrdinalIgnoreCase)
           && (!latestCompletedUtc.HasValue || state.LastRunUtc.Value >= latestCompletedUtc.Value);

    private static int NormalizeFrequency(int value)
        => BackupOptions.SupportedFrequencyHours.Contains(value) ? value : BackupOptions.DefaultFrequencyHours;
}
