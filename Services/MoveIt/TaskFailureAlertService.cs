// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Distributed;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.Authentication;
using MoveItAutomationUI.Services.Concurrency;

namespace MoveItAutomationUI.Services.MoveIt;

public sealed record TaskFailureAlertItem(
    string RunId,
    string TaskId,
    string TaskName,
    string Message);

public sealed record TaskFailureAlertPollResult(
    bool Initialized,
    IReadOnlyList<TaskFailureAlertItem> Failures);

public interface ITaskFailureAlertService
{
    Task<TaskFailureAlertPollResult> PollAsync(CancellationToken cancellationToken = default);
    Task ResetAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Detects newly-created failed MOVEit Task Run records for one authenticated MAUI login session.
/// The watermark is kept in a separate distributed-cache entry so alert polling cannot overwrite
/// access-token/session state that may be updated concurrently by the MOVEit REST client.
/// </summary>
public sealed class TaskFailureAlertService : ITaskFailureAlertService
{
    private const string CacheKeyPrefix = "MoveItAutomationUI:TaskFailureAlert:";
    private const int MaximumRowsPerPoll = 100000;
    private static readonly KeyedAsyncLock SessionLocks = new(StringComparer.Ordinal);

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IDistributedCache _cache;
    private readonly IMoveItRestClient _restClient;
    private readonly ConfigurationStartupState _runtimeConfiguration;

    public TaskFailureAlertService(
        IHttpContextAccessor httpContextAccessor,
        IDistributedCache cache,
        IMoveItRestClient restClient,
        ConfigurationStartupState runtimeConfiguration)
    {
        _httpContextAccessor = httpContextAccessor;
        _cache = cache;
        _restClient = restClient;
        _runtimeConfiguration = runtimeConfiguration;
    }

    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        var sessionId = GetCurrentSessionId();
        if (sessionId.Length == 0) return;
        using var lease = await SessionLocks.AcquireAsync(sessionId, cancellationToken);
        _cache.Remove(CacheKeyPrefix + sessionId);
    }

    public async Task<TaskFailureAlertPollResult> PollAsync(CancellationToken cancellationToken = default)
    {
        var sessionId = GetCurrentSessionId();
        if (sessionId.Length == 0)
            throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");

        using var lease = await SessionLocks.AcquireAsync(sessionId, cancellationToken);
        var watermark = ReadWatermark(sessionId);

        // First poll establishes the current high-water mark without raising historical failures.
        if (!watermark.HasValue)
        {
            var latestRunId = await GetLatestRunIdAsync(cancellationToken);
            WriteWatermark(sessionId, latestRunId);
            return new TaskFailureAlertPollResult(true, Array.Empty<TaskFailureAlertItem>());
        }

        var rows = await _restClient.GetTaskRunReportAsync(
            $"TaskID>0;RunID>{watermark.Value.ToString(CultureInfo.InvariantCulture)}",
            "!RunID",
            MaximumRowsPerPoll,
            cancellationToken);

        if (rows.Count == 0)
            return new TaskFailureAlertPollResult(true, Array.Empty<TaskFailureAlertItem>());

        var maxRunId = watermark.Value;
        var failures = new List<TaskFailureAlertItem>();
        foreach (var row in rows)
        {
            if (!TryRunId(row, out var runId) || runId <= watermark.Value) continue;
            if (runId > maxRunId) maxRunId = runId;

            var status = AutomationReportService.ResolveStatus(
                TextAny(row, "Status", "Result", "Outcome", "StatusText"),
                Text(row, "StatusCode"));
            if (!status.Equals("Failed", StringComparison.OrdinalIgnoreCase)) continue;

            var taskId = Text(row, "TaskID");
            var taskName = Text(row, "TaskName");
            var message = TextAny(row, "StatusMsg", "StatusMessage", "ErrorText");
            failures.Add(new TaskFailureAlertItem(
                runId.ToString(CultureInfo.InvariantCulture),
                taskId,
                taskName.Length > 0 ? taskName : taskId.Length > 0 ? $"Task {taskId}" : "Task",
                message));
        }

        // Advance across successful/no-transfer runs too so only genuinely new Task Runs are queried next time.
        WriteWatermark(sessionId, maxRunId);

        // MOVEit returns the newest rows first; display a grouped alert oldest-to-newest for easier reading.
        failures.Sort((left, right) => ParseRunId(left.RunId).CompareTo(ParseRunId(right.RunId)));
        return new TaskFailureAlertPollResult(true, failures);
    }

    private string GetCurrentSessionId()
        => _httpContextAccessor.HttpContext?.User.FindFirst(MoveItSessionService.SessionIdClaimType)?.Value?.Trim() ?? string.Empty;

    private async Task<long> GetLatestRunIdAsync(CancellationToken cancellationToken)
    {
        var rows = await _restClient.GetTaskRunReportAsync("TaskID>0", "!RunID", 1, cancellationToken);
        return rows.Count > 0 && TryRunId(rows[0], out var runId) ? runId : 0;
    }

    private long? ReadWatermark(string sessionId)
    {
        var value = _cache.GetString(CacheKeyPrefix + sessionId);
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var watermark) && watermark >= 0
            ? watermark
            : null;
    }

    private void WriteWatermark(string sessionId, long watermark)
    {
        _cache.SetString(
            CacheKeyPrefix + sessionId,
            watermark.ToString(CultureInfo.InvariantCulture),
            new DistributedCacheEntryOptions
            {
                SlidingExpiration = MauiAuthenticationDefaults.GetSessionLifetime(_runtimeConfiguration.IdleSessionTimeoutMinutes)
            });
    }

    private static bool TryRunId(JsonObject row, out long runId)
        => long.TryParse(Text(row, "RunID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out runId) && runId >= 0;

    private static long ParseRunId(string value)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static string TextAny(JsonObject item, params string[] names)
    {
        foreach (var name in names)
        {
            var value = Text(item, name);
            if (value.Length > 0) return value;
        }
        return string.Empty;
    }

    private static string Text(JsonObject item, string name)
    {
        if (item[name] is null) return string.Empty;
        try { return item[name]!.GetValue<string>()?.Trim() ?? string.Empty; }
        catch { return item[name]!.ToString().Trim(); }
    }
}
