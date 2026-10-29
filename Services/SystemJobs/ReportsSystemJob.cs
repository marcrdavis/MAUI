// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// System Job dispatcher for all scheduled MAUI Reports.

using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Reporting;

namespace MoveItAutomationUI.Services.SystemJobs;

public interface IReportsSystemJob
{
    Task<MauiSystemJobExecutionOutcome> ExecuteAsync(CancellationToken cancellationToken = default);
}

public sealed class ReportsSystemJob : IReportsSystemJob
{
    private readonly IOutboundReportRepository _repository;
    private readonly IOutboundReportEmailService _email;
    private readonly IOutboundReportService _service;
    private readonly ILogger<ReportsSystemJob> _logger;

    public ReportsSystemJob(
        IOutboundReportRepository repository,
        IOutboundReportEmailService email,
        IOutboundReportService service,
        ILogger<ReportsSystemJob> logger)
    {
        _repository = repository;
        _email = email;
        _service = service;
        _logger = logger;
    }

    public async Task<MauiSystemJobExecutionOutcome> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var due = await _repository.GetDueAsync(DateTimeOffset.UtcNow, cancellationToken);
        if (due.Count == 0)
            return MauiSystemJobExecutionOutcome.Successful("Completed. No scheduled Reports were due.");

        var emailStatus = _email.GetConfigurationStatus();
        if (!emailStatus.IsConfigured)
        {
            return MauiSystemJobExecutionOutcome.CompletedWithWarnings(
                $"Completed with warnings. {due.Count} scheduled Report{(due.Count == 1 ? string.Empty : "s")} remain pending because SMTP is not configured. {emailStatus.Message}");
        }

        var successful = 0;
        var failed = 0;
        var failureDetails = new List<string>();

        foreach (var report in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await _service.ExecuteScheduledAsync(report, cancellationToken);
                if (result.Success)
                {
                    successful++;
                }
                else
                {
                    failed++;
                    failureDetails.Add($"{report.Title}: {result.Message}");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A damaged Report must not prevent later due Reports from running. This matches the
                // established Report behavior while the System Job records the aggregate warning.
                failed++;
                failureDetails.Add($"{report.Title}: {ex.Message}");
                _logger.LogError(ex, "Unexpected failure while the Reports System Job processed Report {ReportId} ({ReportTitle}).", report.Id, report.Title);
            }
        }

        var summary = $"Completed. {due.Count} scheduled Report{(due.Count == 1 ? string.Empty : "s")} processed: {successful} successful, {failed} failed.";
        if (failed == 0) return MauiSystemJobExecutionOutcome.Successful(summary);

        var details = string.Join(" | ", failureDetails.Take(5));
        if (failureDetails.Count > 5) details += $" | {failureDetails.Count - 5} additional failure(s).";
        return MauiSystemJobExecutionOutcome.CompletedWithWarnings($"{summary} {details}".Trim());
    }
}
