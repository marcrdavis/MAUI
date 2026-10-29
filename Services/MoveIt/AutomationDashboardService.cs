// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using MoveItAutomationUI.Services.Concurrency;

namespace MoveItAutomationUI.Services.MoveIt;

public interface IAutomationDashboardService
{
    // Purpose: Returns the Dashboard task, host, and recent-activity summary.
    Task<DashboardSummary> GetSummaryAsync(CancellationToken cancellationToken = default);
    // Purpose: Returns the Dashboard File Activity summary for the configured time window.
    Task<DashboardFileActivitySummary> GetFileActivitySummaryAsync(CancellationToken cancellationToken = default);
    // Purpose: Returns running snapshot.
    Task<DashboardRunningSnapshot> GetRunningSnapshotAsync(CancellationToken cancellationToken = default);
}

// Core Dashboard data deliberately comes from the same Task/Host services used by the working inventory pages.
// It is kept separate from File Activity reporting so a slow statistics query can never hold the Dashboard hostage.
public sealed record DashboardSummary(
    int TotalTasks,
    int Hosts,
    int FailedTasks,
    int SuccessfulTasks,
    int NoTransferTasks,
    IReadOnlyList<DashboardRecentTaskRun> RecentTaskActivity,
    DateTimeOffset RefreshedUtc);

public sealed record DashboardFileActivitySummary(
    int FailedFileActivities,
    int SuccessfulFileActivities,
    bool FailedLimitReached,
    bool SuccessfulLimitReached,
    bool IsComplete,
    int RowsLoaded,
    DashboardTransferTrend TransferTrend,
    DateTimeOffset RefreshedUtc);

public sealed record DashboardRecentTaskRun(
    string TaskId,
    string TaskName,
    DateTimeOffset? StartTime,
    string Status,
    string StatusMessage,
    int? FilesSent,
    double? TotalBytesSent);

public sealed record DashboardTransferTrend(
    int LookbackHours,
    long TotalFilesTransferred,
    double TotalBytesTransferred,
    IReadOnlyList<DashboardTransferTrendPoint> Points);

public sealed record DashboardTransferTrendPoint(
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    long FilesTransferred,
    double TotalBytesTransferred);

public sealed record DashboardRunningSnapshot(
    IReadOnlyList<DashboardRunningTask> Items,
    DateTimeOffset RefreshedUtc);

// StartTime is intentionally retained as MOVEit returns it from the documented v1 running-task endpoint.
// dashboard.js handles unzoned values as local wall-clock values and offset/Z timestamps as absolute times.
public sealed record DashboardRunningTask(
    string TaskId,
    string TaskName,
    string StartTime,
    string StartedBy,
    string CurrentStatus,
    long? BytesTransferred,
    long? TotalBytes,
    string LastErrorText);

public sealed class AutomationDashboardService : IAutomationDashboardService
{
    private const int DashboardLookbackHours = 24;
    private const int DashboardTaskRunMaxCount = 10000;
    // File Activity pages stay on MAUI's established v1 report contract and descending LogID order. Initial
    // history is loaded progressively in 10K pages; subsequent refreshes read only rows above the last
    // completed high-water LogID. Native Dashboard outcome/action semantics are applied locally.
    private const int DashboardFileActivityBatchSize = 10000;
    private const int DashboardFileActivityMaxRows = 100000;
    private const string DashboardFileActivityCachePrefix = "MoveItAutomationUI:Dashboard:FileActivity:";
    private static readonly TimeSpan DashboardFileActivityCacheResidence = TimeSpan.FromHours(12);
    private static readonly KeyedAsyncLock DashboardFileActivityLocks = new(StringComparer.OrdinalIgnoreCase);

    private readonly IAutomationTaskService _taskService;
    private readonly IAutomationHostService _hostService;
    private readonly IAutomationReportService _reportService;
    private readonly IMoveItRestClient _restClient;
    private readonly IMoveItSessionService _sessionService;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<AutomationDashboardService> _logger;

    // Purpose: Initializes automation dashboard service with its required dependencies.
    public AutomationDashboardService(
        IAutomationTaskService taskService,
        IAutomationHostService hostService,
        IAutomationReportService reportService,
        IMoveItRestClient restClient,
        IMoveItSessionService sessionService,
        IMemoryCache memoryCache,
        ILogger<AutomationDashboardService> logger)
    {
        _taskService = taskService;
        _hostService = hostService;
        _reportService = reportService;
        _restClient = restClient;
        _sessionService = sessionService;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    // Loads Task/Host counts plus one 24-hour Task Run dataset. The same Task Run rows drive the three Task KPI buckets
    // and Recent Task Activity, so the Dashboard does not issue duplicate historical-report queries.
    public async Task<DashboardSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        // Capture one rolling window for all Task Run analytics in this refresh. Keeping the lookback in one
        // constant keeps Task and File Activity Dashboard periods aligned for a future common timeframe option.
        var windowEnd = DateTimeOffset.Now;
        var windowStart = windowEnd.AddHours(-DashboardLookbackHours);
        var predicate = $"LogStamp>\"{DashboardSinceValue(windowStart)}\"";

        var taskCountTask = _taskService.GetTaskCountAsync(cancellationToken);
        var hostCountTask = _hostService.GetHostCountAsync(cancellationToken);
        var taskRunsTask = _restClient.GetTaskRunReportAsync(
            predicate,
            "!StartTime",
            DashboardTaskRunMaxCount,
            cancellationToken);

        await Task.WhenAll(taskCountTask, hostCountTask, taskRunsTask);
        var mappedRuns = (await taskRunsTask).Select(ToRecentTaskRun).ToArray();

        var failed = 0;
        var successful = 0;
        var noTransfer = 0;
        foreach (var item in mappedRuns)
        {
            if (item.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase)) failed++;
            else if (item.Status.Equals("Success", StringComparison.OrdinalIgnoreCase)) successful++;
            else if (item.Status.Equals("No Transfers", StringComparison.OrdinalIgnoreCase)) noTransfer++;
        }

        var recent = mappedRuns
            .Where(item => item.StartTime.HasValue)
            .OrderByDescending(item => item.StartTime)
            .Take(8)
            .ToArray();

        return new DashboardSummary(
            await taskCountTask,
            await hostCountTask,
            failed,
            successful,
            noTransfer,
            recent,
            DateTimeOffset.UtcNow);
    }

    // File Activity Dashboard state is cached per MOVEit environment/user. A missing snapshot is built
    // progressively in 10K pages so the browser can render a useful lower-bound count after the first page.
    // Once complete, normal refreshes read only rows newer than the last completed LogID.
    public async Task<DashboardFileActivitySummary> GetFileActivitySummaryAsync(CancellationToken cancellationToken = default)
    {
        var cacheKey = GetFileActivityCacheKey();
        using var refreshLease = await DashboardFileActivityLocks.AcquireAsync(cacheKey, cancellationToken);

        if (!_memoryCache.TryGetValue<DashboardFileActivityCacheState>(cacheKey, out var state) || state is null)
        {
            state = new DashboardFileActivityCacheState();
            _memoryCache.Set(cacheKey, state, new MemoryCacheEntryOptions { SlidingExpiration = DashboardFileActivityCacheResidence });
        }

        try
        {
            await AdvanceFileActivityStateAsync(state, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Dashboard File Activity refresh failed during {Phase}; retained rows={RetainedRows}, initial rows examined={InitialRowsLoaded}, completed high-water LogID={HighWaterLogId}, historical cursor={HistoricalCursor}, incremental floor={IncrementalFloor}, incremental cursor={IncrementalCursor}.",
                state.Phase,
                state.Items.Count,
                state.InitialRowsLoaded,
                state.HighWaterLogId,
                state.OlderThanLogId,
                state.CatchUpFloorLogId,
                state.CatchUpOlderThanLogId);
            throw;
        }

        PruneExpiredFileActivity(state, CurrentFileActivityWindowStart());
        ClearAgedSafetyLimit(state);
        state.RefreshedUtc = DateTimeOffset.UtcNow;
        _memoryCache.Set(cacheKey, state, new MemoryCacheEntryOptions { SlidingExpiration = DashboardFileActivityCacheResidence });

        var summary = BuildFileActivitySummary(state);
        if (state.LimitReached && !state.LimitWarningLogged)
        {
            state.LimitWarningLogged = true;
            _logger.LogWarning(
                "Dashboard File Activity initial scan reached the {MaxRows} row safety limit for the past 24 hours; KPI and transfer-trend values are lower bounds until the omitted historical rows age out of the rolling window or the cache is rebuilt.",
                DashboardFileActivityMaxRows);
        }

        return summary;
    }

    // Advances at most one MOVEit report page. The browser immediately renders that page and, when IsComplete
    // is false, requests the next page. This keeps the UI responsive without background work or duplicate scans.
    private async Task AdvanceFileActivityStateAsync(DashboardFileActivityCacheState state, CancellationToken cancellationToken)
    {
        switch (state.Phase)
        {
            case FileActivityLoadPhase.InitialHistory:
                await LoadInitialHistoryPageAsync(state, cancellationToken);
                return;
            case FileActivityLoadPhase.CatchUp:
                await LoadCatchUpPageAsync(state, cancellationToken);
                return;
            case FileActivityLoadPhase.Complete:
                if (state.HighWaterLogId <= 0)
                {
                    // An empty completed snapshot has no useful high-water mark. Re-enter the historical path
                    // so a later burst larger than one page is still loaded progressively and without gaps.
                    state.ResetForInitialHistory();
                    await LoadInitialHistoryPageAsync(state, cancellationToken);
                    return;
                }

                state.BeginCatchUp(state.HighWaterLogId, CurrentFileActivityWindowStart());
                await LoadCatchUpPageAsync(state, cancellationToken);
                return;
            default:
                throw new InvalidOperationException("Dashboard File Activity cache is in an unknown load state.");
        }
    }

    private async Task LoadInitialHistoryPageAsync(DashboardFileActivityCacheState state, CancellationToken cancellationToken)
    {
        var remaining = DashboardFileActivityMaxRows - state.InitialRowsLoaded;
        if (remaining <= 0)
        {
            state.LimitReached = true;
            if (state.LimitReachedUtc == default) state.LimitReachedUtc = DateTimeOffset.UtcNow;
            state.BeginCatchUp(state.InitialHighWaterLogId, CurrentFileActivityWindowStart());
            return;
        }

        if (state.InitialWindowStart == default)
            state.InitialWindowStart = CurrentFileActivityWindowStart();

        var rowsRequested = Math.Min(DashboardFileActivityBatchSize, remaining);
        var startedUtc = DateTimeOffset.UtcNow;
        var page = await _reportService.GetFileActivityDashboardBatchAsync(state.InitialWindowStart, 0, state.OlderThanLogId, rowsRequested, cancellationToken);
        var accepted = AddFileActivityRows(state, page.Items);
        state.InitialRowsLoaded += page.Items.Count;

        if (state.InitialHighWaterLogId <= 0)
            state.InitialHighWaterLogId = HighestLogId(page.Items);

        _logger.LogDebug(
            "Dashboard File Activity historical page loaded {ReturnedRows} rows ({AcceptedRows} retained) in {ElapsedMs} ms; cumulative rows {RowsLoaded}; more={HasMore}.",
            page.Items.Count,
            accepted,
            Math.Max(0, (DateTimeOffset.UtcNow - startedUtc).TotalMilliseconds),
            state.InitialRowsLoaded,
            page.HasMore);

        if (page.HasMore && state.InitialRowsLoaded < DashboardFileActivityMaxRows)
        {
            state.OlderThanLogId = page.NextCursor;
            if (!state.ProgressiveLoadLogged)
            {
                state.ProgressiveLoadLogged = true;
                _logger.LogInformation(
                    "Dashboard File Activity exceeds one {BatchSize}-row page. MAUI is returning progressive lower-bound Dashboard values while the remaining native MOVEit pages load.",
                    DashboardFileActivityBatchSize);
            }
            return;
        }

        if (page.HasMore)
        {
            state.LimitReached = true;
            if (state.LimitReachedUtc == default) state.LimitReachedUtc = DateTimeOffset.UtcNow;
        }

        // A one-page initial load is already a coherent native snapshot. Avoid a second REST call merely
        // to prove that no rows arrived during that short request; the next normal refresh will pick them up.
        if (!state.ProgressiveLoadLogged && !page.HasMore)
        {
            state.HighWaterLogId = state.InitialHighWaterLogId;
            state.MarkComplete();
            return;
        }

        // Multi-page builds can take many seconds, so perform one bounded catch-up pass above the high-water
        // captured from the first page before declaring the progressive snapshot complete.
        state.BeginCatchUp(state.InitialHighWaterLogId, CurrentFileActivityWindowStart());
    }

    private async Task LoadCatchUpPageAsync(DashboardFileActivityCacheState state, CancellationToken cancellationToken)
    {
        if (state.CatchUpFloorLogId <= 0)
        {
            state.MarkComplete();
            return;
        }

        var startedUtc = DateTimeOffset.UtcNow;
        var page = await _reportService.GetFileActivityDashboardBatchAsync(
            state.CatchUpWindowStart,
            state.CatchUpFloorLogId,
            state.CatchUpOlderThanLogId,
            DashboardFileActivityBatchSize,
            cancellationToken);
        var accepted = AddFileActivityRows(state, page.Items, state.CatchUpFloorLogId);
        state.CatchUpRowsLoaded += page.Items.Count;
        state.CatchUpNewestLogId = Math.Max(state.CatchUpNewestLogId, HighestLogId(page.Items));

        _logger.LogDebug(
            "Dashboard File Activity incremental page loaded {ReturnedRows} rows ({AcceptedRows} retained) in {ElapsedMs} ms above LogID {FloorLogId}; more={HasMore}.",
            page.Items.Count,
            accepted,
            Math.Max(0, (DateTimeOffset.UtcNow - startedUtc).TotalMilliseconds),
            state.CatchUpFloorLogId,
            page.HasMore);

        if (page.HasMore)
        {
            state.CatchUpOlderThanLogId = page.NextCursor;
            return;
        }

        state.HighWaterLogId = Math.Max(state.CatchUpFloorLogId, state.CatchUpNewestLogId);
        state.MarkComplete();

        if (state.ProgressiveLoadLogged && !state.InitialCompletionLogged)
        {
            state.InitialCompletionLogged = true;
            _logger.LogInformation(
                "Dashboard File Activity progressive load completed with {SnapshotRows} retained rows and high-water LogID {HighWaterLogId}.",
                state.Items.Count,
                state.HighWaterLogId);
        }
    }

    private static int AddFileActivityRows(DashboardFileActivityCacheState state, IReadOnlyList<FileActivityReportRecord> records, long minimumExclusiveLogId = 0)
    {
        var accepted = 0;
        foreach (var record in records)
        {
            if (!long.TryParse(record.LogId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var logId) || logId <= 0) continue;
            if (logId <= minimumExclusiveLogId) continue;
            if (!record.LogTime.HasValue) continue;
            if (!AutomationReportService.IsNativeDashboardTransferAction(record.Action)) continue;
            var successful = AutomationReportService.IsNativeDashboardSuccess(record.StatusCode);
            if (!successful && !AutomationReportService.IsNativeDashboardFailure(record.StatusCode)) continue;

            state.Items[logId] = new DashboardFileActivityCacheItem(
                record.LogTime.Value,
                successful,
                Math.Max(0d, record.Size ?? 0d));
            accepted++;
        }
        return accepted;
    }

    private static long HighestLogId(IReadOnlyList<FileActivityReportRecord> records)
    {
        long highest = 0;
        foreach (var record in records)
        {
            if (long.TryParse(record.LogId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > highest)
                highest = parsed;
        }
        return highest;
    }

    private static DateTimeOffset CurrentFileActivityWindowStart()
    {
        var now = DateTimeOffset.Now;
        var windowEnd = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, now.Offset);
        return windowEnd.AddHours(-DashboardLookbackHours);
    }

    private static void PruneExpiredFileActivity(DashboardFileActivityCacheState state, DateTimeOffset windowStart)
    {
        if (state.Items.Count == 0) return;
        var expired = state.Items
            .Where(pair => pair.Value.LogTime < windowStart)
            .Select(pair => pair.Key)
            .ToArray();
        foreach (var key in expired) state.Items.Remove(key);
    }

    private void ClearAgedSafetyLimit(DashboardFileActivityCacheState state)
    {
        if (!state.LimitReached || state.LimitReachedUtc == default) return;
        if (DateTimeOffset.UtcNow - state.LimitReachedUtc < TimeSpan.FromHours(DashboardLookbackHours)) return;

        state.LimitReached = false;
        state.LimitReachedUtc = default;
        state.LimitWarningLogged = false;
        _logger.LogInformation(
            "Dashboard File Activity safety-limit lower-bound state cleared after the omitted historical rows aged out of the {LookbackHours}-hour window.",
            DashboardLookbackHours);
    }

    private static DashboardFileActivitySummary BuildFileActivitySummary(DashboardFileActivityCacheState state)
    {
        var now = DateTimeOffset.Now;
        var windowEnd = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, now.Offset);
        var windowStart = windowEnd.AddHours(-DashboardLookbackHours);
        var files = new long[DashboardLookbackHours];
        var bytes = new double[DashboardLookbackHours];
        var successful = 0;
        var failed = 0;
        double totalBytes = 0;

        foreach (var item in state.Items.Values)
        {
            if (item.LogTime < windowStart || item.LogTime > windowEnd) continue;

            if (item.Successful)
            {
                successful++;
                var bucket = (int)Math.Floor((item.LogTime - windowStart).TotalHours);
                if (bucket < 0) continue;
                if (bucket >= DashboardLookbackHours) bucket = DashboardLookbackHours - 1;
                files[bucket]++;
                bytes[bucket] += item.Size;
                totalBytes += item.Size;
            }
            else
            {
                failed++;
            }
        }

        var points = new DashboardTransferTrendPoint[DashboardLookbackHours];
        for (var index = 0; index < DashboardLookbackHours; index++)
        {
            points[index] = new DashboardTransferTrendPoint(
                windowStart.AddHours(index),
                windowStart.AddHours(index + 1),
                files[index],
                bytes[index]);
        }

        var transferTrend = new DashboardTransferTrend(
            DashboardLookbackHours,
            successful,
            totalBytes,
            points);

        return new DashboardFileActivitySummary(
            failed,
            successful,
            state.LimitReached,
            state.LimitReached,
            state.Phase == FileActivityLoadPhase.Complete,
            state.Items.Count,
            transferTrend,
            state.RefreshedUtc == default ? DateTimeOffset.UtcNow : state.RefreshedUtc);
    }

    private string GetFileActivityCacheKey()
    {
        var session = _sessionService.Get() ?? throw new MoveItAuthenticationException("MOVEit session is not authenticated.");
        return $"{DashboardFileActivityCachePrefix}{session.GetEnvironmentKey()}:{session.Username}";
    }

    private enum FileActivityLoadPhase
    {
        InitialHistory,
        CatchUp,
        Complete
    }

    private sealed class DashboardFileActivityCacheState
    {
        public Dictionary<long, DashboardFileActivityCacheItem> Items { get; } = new();
        public FileActivityLoadPhase Phase { get; set; } = FileActivityLoadPhase.InitialHistory;
        public string OlderThanLogId { get; set; } = string.Empty;
        public DateTimeOffset InitialWindowStart { get; set; }
        public int InitialRowsLoaded { get; set; }
        public long InitialHighWaterLogId { get; set; }
        public long HighWaterLogId { get; set; }
        public long CatchUpFloorLogId { get; set; }
        public long CatchUpNewestLogId { get; set; }
        public string CatchUpOlderThanLogId { get; set; } = string.Empty;
        public DateTimeOffset CatchUpWindowStart { get; set; }
        public int CatchUpRowsLoaded { get; set; }
        public bool LimitReached { get; set; }
        public DateTimeOffset LimitReachedUtc { get; set; }
        public bool LimitWarningLogged { get; set; }
        public bool ProgressiveLoadLogged { get; set; }
        public bool InitialCompletionLogged { get; set; }
        public DateTimeOffset RefreshedUtc { get; set; }

        public void BeginCatchUp(long floorLogId, DateTimeOffset windowStart)
        {
            if (floorLogId <= 0)
            {
                MarkComplete();
                return;
            }

            Phase = FileActivityLoadPhase.CatchUp;
            CatchUpFloorLogId = floorLogId;
            CatchUpNewestLogId = floorLogId;
            CatchUpOlderThanLogId = string.Empty;
            CatchUpWindowStart = windowStart;
            CatchUpRowsLoaded = 0;
        }

        public void MarkComplete()
        {
            Phase = FileActivityLoadPhase.Complete;
            CatchUpFloorLogId = 0;
            CatchUpNewestLogId = 0;
            CatchUpOlderThanLogId = string.Empty;
            CatchUpWindowStart = default;
            CatchUpRowsLoaded = 0;
        }

        public void ResetForInitialHistory()
        {
            Items.Clear();
            Phase = FileActivityLoadPhase.InitialHistory;
            OlderThanLogId = string.Empty;
            InitialWindowStart = default;
            InitialRowsLoaded = 0;
            InitialHighWaterLogId = 0;
            HighWaterLogId = 0;
            CatchUpFloorLogId = 0;
            CatchUpNewestLogId = 0;
            CatchUpOlderThanLogId = string.Empty;
            CatchUpWindowStart = default;
            CatchUpRowsLoaded = 0;
            LimitReached = false;
            LimitReachedUtc = default;
            LimitWarningLogged = false;
            ProgressiveLoadLogged = false;
            InitialCompletionLogged = false;
        }
    }

    // Value type keeps the 24-hour snapshot compact even on high-volume systems; outcome is normalized once
    // when the row is admitted instead of reparsing StatusCode on every Dashboard render.
    private readonly record struct DashboardFileActivityCacheItem(
        DateTimeOffset LogTime,
        bool Successful,
        double Size);

    // Returns the current running-task snapshot using only MOVEit Automation's documented v1 endpoint.
    public async Task<DashboardRunningSnapshot> GetRunningSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var running = await _restClient.GetRunningTasksAsync(cancellationToken);
        var items = running
            .Select(active => new DashboardRunningTask(
                active.TaskId,
                active.TaskName,
                active.TimeStarted,
                active.StartedBy,
                FirstNonBlank(active.Status, "Running"),
                active.CurFileBytes,
                active.TotFileBytes,
                active.LastErrorText))
            .OrderByDescending(item => ParseWallClockForSort(item.StartTime))
            .ThenBy(item => item.TaskName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new DashboardRunningSnapshot(items, DateTimeOffset.UtcNow);
    }

    // MOVEit running-task timestamps can be returned without a zone. For comparison/sorting we preserve
    // that clock value instead of assuming UTC. Zoned values are converted to their own local clock first.
    private static DateTime? ParseWallClockForSort(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0) return null;

        if (HasExplicitZone(text) && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var offset))
            return offset.LocalDateTime;

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified)
            : null;
    }

    // Purpose: Returns whether explicit zone.
    private static bool HasExplicitZone(string value)
    {
        if (value.EndsWith("Z", StringComparison.OrdinalIgnoreCase)) return true;
        var t = value.IndexOf('T');
        if (t < 0) t = value.IndexOf(' ');
        if (t < 0) return false;
        return value.IndexOf('+', t + 1) >= 0 || value.IndexOf('-', t + 1) >= 0;
    }

    // Purpose: Builds a MOVEit-local timestamp boundary for the Dashboard reporting window.
    private static string DashboardSinceValue(DateTimeOffset since)
        => since.LocalDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

    // Purpose: Converts the current value to recent task run.
    private static DashboardRecentTaskRun ToRecentTaskRun(JsonObject item)
    {
        var status = TaskRunStatus.Normalize(TextAny(item, "Status", "Result", "Outcome", "StatusText"));
        return new DashboardRecentTaskRun(
            TextAny(item, "TaskID", "TaskId"),
            TextAny(item, "TaskName"),
            ParseReportDate(TextAny(item, "StartTime")),
            status,
            TextAny(item, "StatusMsg", "StatusMessage", "ErrorText"),
            ParseNullableInt(TextAny(item, "FilesSent")),
            ParseNullableDouble(TextAny(item, "TotalBytesSent")));
    }

    // Purpose: Returns the first nonblank text value from the candidate JSON fields.
    private static string TextAny(JsonObject item, params string[] names)
    {
        foreach (var name in names)
        {
            foreach (var pair in item)
            {
                if (!pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase) || pair.Value is null) continue;
                if (pair.Value is JsonValue value)
                {
                    if (value.TryGetValue<string>(out var text)) return text?.Trim() ?? string.Empty;
                    return value.ToJsonString().Trim('"');
                }
                return pair.Value.ToJsonString();
            }
        }
        return string.Empty;
    }

    // Purpose: Parses report date.
    private static DateTimeOffset? ParseReportDate(string value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed) ? parsed : null;

    // Purpose: Parses nullable int.
    private static int? ParseNullableInt(string value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    // Purpose: Parses nullable double.
    private static double? ParseNullableDouble(string value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    // Purpose: Returns the first nonblank string from the supplied candidates.
    private static string FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
