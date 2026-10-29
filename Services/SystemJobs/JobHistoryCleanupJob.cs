// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Hidden maintenance System Job that enforces the configured MAUI scheduled-job history retention policy.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;

namespace MoveItAutomationUI.Services.SystemJobs;

public interface IJobHistoryCleanupJob
{
    Task<MauiSystemJobExecutionOutcome> ExecuteAsync(CancellationToken cancellationToken = default);
}

public sealed class JobHistoryCleanupJob : IJobHistoryCleanupJob
{
    private const int DeleteBatchSize = 500;

    private readonly MetadataDbContext _db;
    private readonly IOptionsMonitor<JobHistoryOptions> _options;
    private readonly ILogger<JobHistoryCleanupJob> _logger;

    public JobHistoryCleanupJob(
        MetadataDbContext db,
        IOptionsMonitor<JobHistoryOptions> options,
        ILogger<JobHistoryCleanupJob> logger)
    {
        _db = db;
        _options = options;
        _logger = logger;
    }

    public async Task<MauiSystemJobExecutionOutcome> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var retentionDays = NormalizeRetentionDays(_options.CurrentValue.RetentionDays.Value);
        var cutoffUtc = DateTimeOffset.UtcNow.AddDays(-retentionDays);

        // SQLite cannot reliably translate DateTimeOffset comparisons. Read only the indexed history
        // identifier/timestamp pair, evaluate the cutoff in memory, then delete matching IDs in batches.
        var reportHistory = await _db.OutboundReportHistory
            .AsNoTracking()
            .Select(item => new { item.Id, item.StartedUtc })
            .ToListAsync(cancellationToken);

        var expiredIds = reportHistory
            .Where(item => item.StartedUtc < cutoffUtc)
            .Select(item => item.Id)
            .ToArray();

        var deletedReportHistory = 0;
        foreach (var batch in expiredIds.Chunk(DeleteBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            deletedReportHistory += await _db.OutboundReportHistory
                .Where(item => batch.Contains(item.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        _logger.LogInformation(
            "MAUI Job History Cleanup completed. Retention={RetentionDays} days; Report history deleted={DeletedReportHistory}.",
            retentionDays,
            deletedReportHistory);

        if (deletedReportHistory == 0)
            return MauiSystemJobExecutionOutcome.Successful($"Completed. No job history entries older than {retentionDays} days were found.");

        var entryLabel = deletedReportHistory == 1 ? "entry" : "entries";
        return MauiSystemJobExecutionOutcome.Successful(
            $"Completed. Deleted {deletedReportHistory} Report history {entryLabel} older than {retentionDays} days.");
    }

    private static int NormalizeRetentionDays(int value)
        => JobHistoryOptions.SupportedRetentionDays.Contains(value)
            ? value
            : JobHistoryOptions.DefaultRetentionDays;
}
