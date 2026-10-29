// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Visible daily System Job that enforces Configuration > Settings > Backup retention.

using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Backup;

namespace MoveItAutomationUI.Services.SystemJobs;

public interface IBackupCleanupJob
{
    Task<MauiSystemJobExecutionOutcome> ExecuteAsync(CancellationToken cancellationToken = default);
}

public sealed class BackupCleanupJob : IBackupCleanupJob
{
    private readonly IMauiBackupService _backupService;
    private readonly IOptionsMonitor<BackupOptions> _options;
    private readonly ILogger<BackupCleanupJob> _logger;

    public BackupCleanupJob(
        IMauiBackupService backupService,
        IOptionsMonitor<BackupOptions> options,
        ILogger<BackupCleanupJob> logger)
    {
        _backupService = backupService;
        _options = options;
        _logger = logger;
    }

    public async Task<MauiSystemJobExecutionOutcome> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var configured = _options.CurrentValue.RetentionDays.Value;
        var retentionDays = BackupOptions.SupportedRetentionDays.Contains(configured)
            ? configured
            : BackupOptions.DefaultRetentionDays;

        var result = await _backupService.CleanupExpiredBackupsAsync(
            retentionDays,
            "Backup Cleanup System Job",
            cancellationToken);

        if (result.Warnings.Count > 0)
        {
            _logger.LogWarning(
                "MAUI Backup Cleanup completed with warnings. Retention={RetentionDays} days; Removed={RemovedCount}; Skipped={SkippedCount}.",
                retentionDays, result.RemovedCount, result.SkippedCount);
            return MauiSystemJobExecutionOutcome.CompletedWithWarnings(
                $"Completed with warnings. Removed {result.RemovedCount} backup(s); skipped {result.SkippedCount}. Retention is {retentionDays} day(s). Review Configuration > Logs for details.");
        }

        if (result.RemovedCount == 0)
            return MauiSystemJobExecutionOutcome.Successful($"Completed. No backups older than {retentionDays} day(s) were found.");

        return MauiSystemJobExecutionOutcome.Successful(
            $"Completed. Removed {result.RemovedCount} backup(s) older than {retentionDays} day(s).");
    }
}
