// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Globalization;
using System.Text.Json.Nodes;

namespace MoveItAutomationUI.Services.MoveIt;

public interface IAutomationReportService
{
    // Purpose: Returns task runs.
    Task<ReportBatch<TaskRunReportRecord>> GetTaskRunsAsync(string? taskId, int batchSize, string? cursor, int maxRowsRemaining, CancellationToken cancellationToken = default);
    // Purpose: Returns the Task Run rows for a supported Dashboard drill-down view.
    Task<TaskRunDashboardViewResult> GetTaskRunDashboardViewAsync(string viewKey, CancellationToken cancellationToken = default);
    // Purpose: Returns file activity.
    Task<ReportBatch<FileActivityReportRecord>> GetFileActivityAsync(string? taskId, string? nominalStart, int batchSize, string? cursor, int maxRowsRemaining, CancellationToken cancellationToken = default);
    // Purpose: Returns the File Activity rows for a supported Dashboard drill-down view.
    Task<FileActivityDashboardViewResult> GetFileActivityDashboardViewAsync(string viewKey, CancellationToken cancellationToken = default);
    // Purpose: Returns one v1 File Activity page for progressive Dashboard loading and incremental refresh.
    Task<FileActivityDashboardBatch> GetFileActivityDashboardBatchAsync(DateTimeOffset windowStart, long newerThanLogId, string? olderThanLogId, int batchSize, CancellationToken cancellationToken = default);
    // Purpose: Aggregates File Activity outcomes and successful transfer volume for the Dashboard using one bounded report scan.
    Task<FileActivitySummaryCount> GetFileActivitySummaryCountAsync(DateTimeOffset windowStart, DateTimeOffset windowEnd, int batchSize, int maxRows, CancellationToken cancellationToken = default);
    // Purpose: Returns one Audit report batch for the requested predicate and cursor.
    Task<ReportBatch<AuditReportRecord>> GetAuditAsync(string? targetType, string? targetId, int batchSize, string? cursor, int maxRowsRemaining, CancellationToken cancellationToken = default);
}

public sealed record ReportBatch<T>(IReadOnlyList<T> Items, bool HasMore, string NextCursor);

public sealed record TaskRunDashboardViewResult(
    string ViewKey,
    string Label,
    string Description,
    IReadOnlyList<TaskRunReportRecord> Items,
    int MaximumRows,
    bool LimitReached);

public sealed record FileActivityDashboardViewResult(
    string ViewKey,
    string Label,
    string Description,
    IReadOnlyList<FileActivityReportRecord> Items,
    int MaximumRows);

public sealed record FileActivityDashboardBatch(
    IReadOnlyList<FileActivityReportRecord> Items,
    bool HasMore,
    string NextCursor);

public sealed record FileActivitySummaryCount(
    int FailedCount,
    int SuccessfulCount,
    int RowsExamined,
    bool LimitReached,
    double TotalBytesTransferred,
    IReadOnlyList<FileActivitySummaryBucket> Buckets);

public sealed record FileActivitySummaryBucket(
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    long FilesTransferred,
    double TotalBytesTransferred);

public sealed record TaskRunReportRecord(
    string RowId,
    string TaskId,
    string TaskName,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime,
    long? DurationSeconds,
    string Status,
    string StatusMessage,
    int? FilesSent,
    double? TotalBytesSent,
    string StartedBy,
    string RunId,
    DateTimeOffset? ScheduledTime,
    string NominalStart,
    string Node,
    string StatusCode,
    DateTimeOffset? LogStamp,
    string RecordType,
    string QueryType);

public sealed record FileActivityReportRecord(
    string RowId,
    string LogId,
    DateTimeOffset? LogTime,
    string TaskId,
    string TaskName,
    string SourceFile,
    string Action,
    string Status,
    string StatusMessage,
    double? Size,
    string SourcePath,
    string DestinationPath,
    string SourceHost,
    string DestinationHost,
    string DestinationFile,
    double? SourceBytes,
    double? DestinationBytes,
    double? TransferBytes,
    double? SourceDuration,
    double? DestinationDuration,
    string SourceFileId,
    string DestinationFileId,
    string SourceStamp,
    DateTimeOffset? StartTime,
    DateTimeOffset? ScheduledTime,
    string NominalStart,
    string Node,
    string StatusCode,
    string SourceAsxMessageId,
    string SourceAsxMdn,
    string DestinationAsxMessageId,
    string DestinationAsxMdn,
    string QueryType);

public sealed record AuditReportRecord(
    string RowId,
    string LogId,
    DateTimeOffset? LogTime,
    string Action,
    string TargetType,
    string TargetId,
    string TargetName,
    string Status,
    string StatusMessage,
    string Username,
    string IpAddress,
    string ClientIp,
    string TaskName,
    string Node,
    string StatusCode,
    string AgentBrand,
    string AgentVersion,
    string CentralVersion,
    string QueryType,
    string Hash,
    bool HasAuditDiff = false);

public sealed class AutomationReportService : IAutomationReportService
{
    private readonly IMoveItRestClient _restClient;
    private readonly IMoveItSessionService _sessionService;

    // Purpose: Initializes automation report service with its required dependencies.
    public AutomationReportService(IMoveItRestClient restClient, IMoveItSessionService sessionService)
    {
        _restClient = restClient;
        _sessionService = sessionService;
    }

    // Purpose: Returns task runs.
    public async Task<ReportBatch<TaskRunReportRecord>> GetTaskRunsAsync(
        string? taskId,
        int batchSize,
        string? cursor,
        int maxRowsRemaining,
        CancellationToken cancellationToken = default)
    {
        var predicate = BuildPredicate("TaskID>0", "TaskID", taskId, numeric: true);
        predicate = AddNumericCursor(predicate, "RunID", cursor);
        var raw = await FetchBatchAsync(
            (count, token) => _restClient.GetTaskRunReportAsync(predicate, "!RunID", count, token),
            batchSize,
            maxRowsRemaining,
            cancellationToken);
        var records = raw.Items.Select(MapTaskRun).ToArray();
        return new ReportBatch<TaskRunReportRecord>(records, raw.HasMore, records.LastOrDefault()?.RunId ?? string.Empty);
    }

    // Uses the same 24-hour Task Run query and local status interpretation as the Dashboard.
    // Status filtering is intentionally performed locally rather than by guessing at an undocumented
    // MOVEit report predicate for Task Run status.
    public async Task<TaskRunDashboardViewResult> GetTaskRunDashboardViewAsync(
        string viewKey,
        CancellationToken cancellationToken = default)
    {
        const int dashboardMaximumRows = 10000;
        var key = (viewKey ?? string.Empty).Trim().ToLowerInvariant();
        string label;
        string description;
        string requiredStatus;

        switch (key)
        {
            case "failed":
                label = "Failed — Past 24 Hours";
                description = "Failed task runs during the past 24 hours.";
                requiredStatus = "Failed";
                break;
            case "successful":
                label = "Successful — Past 24 Hours";
                description = "Successful task runs during the past 24 hours.";
                requiredStatus = "Success";
                break;
            case "no-transfers":
                label = "No Transfer — Past 24 Hours";
                description = "Task runs with no transfers during the past 24 hours.";
                requiredStatus = "No Transfers";
                break;
            default:
                throw new ArgumentException("The requested Task Run dashboard view is not supported.", nameof(viewKey));
        }

        var sinceValue = DateTime.Now.AddHours(-24).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        var raw = await _restClient.GetTaskRunReportAsync(
            $"LogStamp>\"{sinceValue}\"",
            "!StartTime",
            dashboardMaximumRows,
            cancellationToken);

        var items = raw
            .Where(item => TaskRunStatus.Normalize(TextAny(item, "Status", "Result", "Outcome", "StatusText"))
                .Equals(requiredStatus, StringComparison.OrdinalIgnoreCase))
            .Select(MapTaskRun)
            .ToArray();

        return new TaskRunDashboardViewResult(
            key,
            label,
            description,
            items,
            dashboardMaximumRows,
            raw.Count >= dashboardMaximumRows);
    }

    // Purpose: Returns file activity.
    public async Task<ReportBatch<FileActivityReportRecord>> GetFileActivityAsync(
        string? taskId,
        string? nominalStart,
        int batchSize,
        string? cursor,
        int maxRowsRemaining,
        CancellationToken cancellationToken = default)
    {
        var predicate = BuildFileActivityPredicate(taskId, nominalStart);
        predicate = AddNumericCursor(predicate, "LogID", cursor);
        var raw = await FetchBatchAsync(
            (count, token) => GetFileActivityRecordsAsync(predicate, count, token),
            batchSize,
            maxRowsRemaining,
            cancellationToken);
        return new ReportBatch<FileActivityReportRecord>(raw.Items, raw.HasMore, raw.Items.LastOrDefault()?.LogId ?? string.Empty);
    }

    // Returns one page through the same v1 File Activity report contract MAUI already uses successfully at
    // high volume. The absolute window and descending LogID cursor are both established on this path; native
    // Success/Failure and transfer-action semantics are applied to the mapped rows by the Dashboard service.
    public async Task<FileActivityDashboardBatch> GetFileActivityDashboardBatchAsync(
        DateTimeOffset windowStart,
        long newerThanLogId,
        string? olderThanLogId,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var safeBatchSize = Math.Clamp(batchSize, 1, 10000);
        var predicate = BuildFileActivityDashboardPredicate(windowStart, newerThanLogId, olderThanLogId);
        var records = await GetFileActivityRecordsAsync(predicate, safeBatchSize + 1, cancellationToken);
        var page = records.Take(safeBatchSize).ToArray();
        var hasMore = records.Count > safeBatchSize;
        var nextCursor = hasMore ? page.LastOrDefault()?.LogId ?? string.Empty : string.Empty;

        if (hasMore
            && (!long.TryParse(nextCursor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCursor) || parsedCursor <= 0))
        {
            throw new MoveItApiException("MOVEit File Activity paging did not return a usable LogID cursor.");
        }
        if (hasMore && nextCursor.Equals((olderThanLogId ?? string.Empty).Trim(), StringComparison.Ordinal))
            throw new MoveItApiException("MOVEit File Activity paging returned the same LogID cursor twice.");

        return new FileActivityDashboardBatch(page, hasMore, nextCursor);
    }

    // Aggregates the Dashboard File Activity KPIs and transfer trend in one pass over the same
    // File Activity report rows. Successful transfer actions increment both the KPI and the hourly
    // file-count bucket, so the chart total and Successful File Activity KPI share one definition.
    // The REST predicate remains limited to fields supported by the native File Activity report path;
    // Success/Failure and the upper window boundary are applied locally to the mapped records.
    public async Task<FileActivitySummaryCount> GetFileActivitySummaryCountAsync(
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        int batchSize,
        int maxRows,
        CancellationToken cancellationToken = default)
    {
        if (windowEnd <= windowStart)
            throw new ArgumentException("File Activity end time must be later than the start time.", nameof(windowEnd));

        var safeBatchSize = Math.Clamp(batchSize, 1, 10000);
        var safeMaxRows = Math.Clamp(maxRows, 1, 500000);
        var bucketCount = Math.Max(1, (int)Math.Ceiling((windowEnd - windowStart).TotalHours));
        var basePredicate = BuildFileActivityPredicate(
            logStampStart: windowStart.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));

        var files = new long[bucketCount];
        var bytes = new double[bucketCount];
        var cursor = string.Empty;
        var rowsExamined = 0;
        var failed = 0;
        var successful = 0;
        double totalBytes = 0;

        FileActivitySummaryCount BuildResult(bool limitReached)
        {
            var buckets = new FileActivitySummaryBucket[bucketCount];
            for (var index = 0; index < bucketCount; index++)
            {
                var bucketStart = windowStart.AddHours(index);
                var bucketEnd = bucketStart.AddHours(1);
                if (bucketEnd > windowEnd) bucketEnd = windowEnd;

                buckets[index] = new FileActivitySummaryBucket(
                    bucketStart,
                    bucketEnd,
                    files[index],
                    bytes[index]);
            }

            return new FileActivitySummaryCount(
                failed,
                successful,
                rowsExamined,
                limitReached,
                totalBytes,
                buckets);
        }

        while (rowsExamined < safeMaxRows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rowsAllowed = Math.Min(safeBatchSize, safeMaxRows - rowsExamined);
            var predicate = AddNumericCursor(basePredicate, "LogID", cursor);
            var records = await GetFileActivityRecordsAsync(predicate, rowsAllowed + 1, cancellationToken);
            if (records.Count == 0)
                return BuildResult(false);

            var page = records.Take(rowsAllowed).ToArray();
            foreach (var record in page)
            {
                if (!IsNativeDashboardTransferAction(record.Action) || !record.LogTime.HasValue) continue;

                var logTime = record.LogTime.Value;
                if (logTime < windowStart || logTime > windowEnd) continue;

                if (IsNativeDashboardSuccess(record.StatusCode))
                {
                    successful++;

                    var bucket = (int)Math.Floor((logTime - windowStart).TotalHours);
                    if (bucket < 0) continue;
                    if (bucket >= bucketCount) bucket = bucketCount - 1;

                    var byteCount = Math.Max(0d, record.Size ?? 0d);
                    files[bucket]++;
                    bytes[bucket] += byteCount;
                    totalBytes += byteCount;
                }
                else if (IsNativeDashboardFailure(record.StatusCode))
                {
                    failed++;
                }
            }

            rowsExamined += page.Length;
            var hasMore = records.Count > rowsAllowed;
            if (!hasMore)
                return BuildResult(false);
            if (rowsExamined >= safeMaxRows)
                return BuildResult(true);

            var nextCursor = page.LastOrDefault()?.LogId ?? string.Empty;
            if (!long.TryParse(nextCursor, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericCursor) || numericCursor <= 0)
                throw new MoveItApiException("MOVEit File Activity paging did not return a usable LogID cursor.");
            if (nextCursor.Equals(cursor, StringComparison.Ordinal))
                throw new MoveItApiException("MOVEit File Activity paging returned the same LogID cursor twice.");

            cursor = nextCursor;
        }

        return BuildResult(true);
    }

    // Retrieves one of the exact File Activity result sets used by the native Automation dashboard.
    // These predicates were captured from MOVEit Automation 2025.1 Web Admin and are intentionally
    // isolated here so the Dashboard links and report grid cannot drift into separate definitions.
    public async Task<FileActivityDashboardViewResult> GetFileActivityDashboardViewAsync(
        string viewKey,
        CancellationToken cancellationToken = default)
    {
        const int nativeMaximumRows = 1000;
        var key = (viewKey ?? string.Empty).Trim().ToLowerInvariant();
        string label;
        string description;
        string predicate;

        switch (key)
        {
            case "failed":
                label = "Failed — Past 24 Hours";
                description = "Failed file activity during the past 24 hours.";
                predicate = "(((StatusCode=out=(\"0\",\"5000\",\"5010\"))));Action=in=(\"get\",\"send\",\"append\");LogStamp>=PT-24H";
                break;
            case "successful":
                label = "Successful — Past 24 Hours";
                description = "Successful file activity during the past 24 hours.";
                predicate = "(((StatusCode==\"0\")));Action=in=(\"get\",\"send\",\"append\");LogStamp>=PT-24H";
                break;
            default:
                throw new ArgumentException("The requested File Activity dashboard view is not supported.", nameof(viewKey));
        }

        var raw = await _restClient.GetFileActivityReportV0Async(predicate, "!LogStamp", nativeMaximumRows, cancellationToken);
        return new FileActivityDashboardViewResult(
            key,
            label,
            description,
            raw.Select(MapFileActivity).ToArray(),
            nativeMaximumRows);
    }

    // Purpose: Returns one Audit report batch for the requested predicate and cursor.
    public async Task<ReportBatch<AuditReportRecord>> GetAuditAsync(
        string? targetType,
        string? targetId,
        int batchSize,
        string? cursor,
        int maxRowsRemaining,
        CancellationToken cancellationToken = default)
    {
        var parts = new List<string> { "LogID>0" };
        if (!string.IsNullOrWhiteSpace(targetType)) parts.Add($"TargetType=={QuoteRsql(targetType.Trim())}");
        if (!string.IsNullOrWhiteSpace(targetId))
        {
            var normalizedTargetId = targetId.Trim();
            parts.Add(long.TryParse(normalizedTargetId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericTargetId)
                ? $"TargetID=={numericTargetId.ToString(CultureInfo.InvariantCulture)}"
                : $"TargetID=={QuoteRsql(normalizedTargetId)}");
        }

        var predicate = AddNumericCursor(string.Join(';', parts), "LogID", cursor);
        var raw = await FetchBatchAsync(
            (count, token) => _restClient.GetAuditReportAsync(predicate, "!LogID", count, token),
            batchSize,
            maxRowsRemaining,
            cancellationToken);

        var supportsChanges = MoveItFeatureSupport.Supports(_sessionService.Get()?.Info, MoveItFeature.AuditConfigurationChanges);
        var records = raw.Items.Select(item => MapAudit(item) with { HasAuditDiff = supportsChanges && HasAuditDiff(item) }).ToArray();
        records = await EnrichAuditTargetsAsync(records, cancellationToken);
        return new ReportBatch<AuditReportRecord>(records, raw.HasMore, records.LastOrDefault()?.LogId ?? string.Empty);
    }

    // Purpose: Returns file activity records.
    private async Task<IReadOnlyList<FileActivityReportRecord>> GetFileActivityRecordsAsync(
        string predicate,
        int maxCount,
        CancellationToken cancellationToken)
    {
        var raw = await _restClient.GetFileActivityReportAsync(predicate, "!LogID", Math.Clamp(maxCount, 1, 100000), cancellationToken);
        return raw.Select(MapFileActivity).ToArray();
    }

    internal static string BuildFileActivityDashboardPredicate(DateTimeOffset windowStart, long newerThanLogId = 0, string? olderThanLogId = null)
    {
        // Native Dashboard uses an inclusive 24-hour boundary. Keep the v1 high-volume endpoint, but use
        // the same inclusive boundary semantics with the established absolute MOVEit report timestamp form.
        var predicate = BuildFileActivityPredicate();
        var windowText = windowStart.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        predicate += $";LogStamp>={QuoteRsql(windowText)}";
        if (newerThanLogId > 0)
            predicate += $";LogID>{newerThanLogId.ToString(CultureInfo.InvariantCulture)}";
        return AddNumericCursor(predicate, "LogID", olderThanLogId);
    }

    internal static bool IsNativeDashboardTransferAction(string value)
        => value.Equals("send", StringComparison.OrdinalIgnoreCase)
           || value.Equals("get", StringComparison.OrdinalIgnoreCase)
           || value.Equals("append", StringComparison.OrdinalIgnoreCase);

    internal static bool IsNativeDashboardSuccess(string statusCode)
        => long.TryParse((statusCode ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code) && code == 0;

    internal static bool IsNativeDashboardFailure(string statusCode)
        => long.TryParse((statusCode ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)
           && code != 0
           && code != 5000
           && code != 5010;

    // Purpose: Builds file activity predicate.
    private static string BuildFileActivityPredicate(
        string? taskId = null,
        string? nominalStart = null,
        string? logStampStart = null)
    {
        // This is the same baseline predicate used by Records > File Activity. Keep Dashboard-specific
        // filtering out of the REST predicate unless it is already supported by the normal report path.
        var predicate = BuildPredicate("StatusCode=out=(\"5000\",\"5010\")", "TaskID", taskId, numeric: true);
        predicate = BuildPredicate(predicate, "NominalStart", nominalStart, numeric: false);
        if (!string.IsNullOrWhiteSpace(logStampStart))
            predicate = $"{predicate};LogStamp>{QuoteRsql(logStampStart.Trim())}";
        return predicate;
    }

    private static async Task<(IReadOnlyList<T> Items, bool HasMore)> FetchBatchAsync<T>(
        Func<int, CancellationToken, Task<IReadOnlyList<T>>> fetch,
        int batchSize,
        int maxRowsRemaining,
        CancellationToken cancellationToken)
    {
        var safeBatchSize = Math.Clamp(batchSize, 1, 10000);
        var safeRemaining = Math.Max(0, maxRowsRemaining);
        if (safeRemaining == 0) return (Array.Empty<T>(), false);

        var rowsThisBatch = Math.Min(safeBatchSize, safeRemaining);
        var canHaveAnotherBatchWithinLimit = safeRemaining > rowsThisBatch;
        var requestCount = rowsThisBatch + (canHaveAnotherBatchWithinLimit ? 1 : 0);
        var items = await fetch(requestCount, cancellationToken);
        var hasMore = canHaveAnotherBatchWithinLimit && items.Count > rowsThisBatch;
        return (items.Take(rowsThisBatch).ToArray(), hasMore);
    }

    // Purpose: Enriches Audit rows with friendly target names from visible MOVEit inventories.
    private async Task<AuditReportRecord[]> EnrichAuditTargetsAsync(AuditReportRecord[] records, CancellationToken cancellationToken)
    {
        if (records.Length == 0) return records;

        // MOVEit 2025 documents TargetType in the REST Audit record but does not document TargetName,
        // while the native Audit report/database contains both. Preserve everything REST returns first,
        // then resolve missing values from the normal REST resource inventories. When TargetType itself is
        // absent, only infer it when the TargetID uniquely identifies one visible resource type. Inventory
        // reads are performed concurrently so this defensive enrichment does not serialize several REST calls.
        var needsUnknownTypeLookup = records.Any(r => string.IsNullOrWhiteSpace(r.TargetType) && !string.IsNullOrWhiteSpace(r.TargetId));
        var lookupTasks = new List<Task<AuditTargetLookup>>();

        Task<AuditTargetLookup> BuildLookupAsync(
            string type,
            Func<CancellationToken, Task<IReadOnlyList<JsonObject>>> loader,
            string[] idFields,
            string[] nameFields)
            => BuildLookupCoreAsync(type, loader, idFields, nameFields, cancellationToken);

        var capabilities = MoveItLicensePolicy.GetCapabilities(_sessionService.Get());
        var needsTask = records.Any(r => MissingName(r) && IsTargetType(r.TargetType, "task"));
        var needsHost = records.Any(r => MissingName(r) && IsTargetType(r.TargetType, "host"));
        var needsScript = records.Any(r => MissingName(r) && IsTargetType(r.TargetType, "script"));
        var needsDateList = records.Any(r => MissingName(r) && IsTargetType(r.TargetType, "date"));
        var needsGlobal = records.Any(r => MissingName(r) && IsTargetType(r.TargetType, "global"));
        var needsSsh = records.Any(r => MissingName(r) && IsTargetType(r.TargetType, "ssh"));
        var needsPgp = records.Any(r => MissingName(r) && IsTargetType(r.TargetType, "pgp"));
        var needsTls = records.Any(r => MissingName(r) && (IsTargetType(r.TargetType, "ssl") || IsTargetType(r.TargetType, "tls") || IsTargetType(r.TargetType, "cert")));

        if (needsTask || needsUnknownTypeLookup)
            lookupTasks.Add(BuildLookupAsync("task", token => _restClient.GetTasksAsync(token), new[] { "ID", "Id" }, new[] { "Name" }));
        if (needsHost || needsUnknownTypeLookup)
            lookupTasks.Add(BuildLookupAsync("host", token => _restClient.GetHostsAsync(token), new[] { "ID", "Id" }, new[] { "Name" }));
        if (needsScript || needsUnknownTypeLookup)
            lookupTasks.Add(BuildScriptLookupAsync(cancellationToken));
        if (needsDateList || needsUnknownTypeLookup)
            lookupTasks.Add(BuildLookupAsync("datelist", token => _restClient.GetDateListsAsync(token), new[] { "ID", "Id" }, new[] { "Name" }));
        if (needsGlobal || needsUnknownTypeLookup)
            lookupTasks.Add(BuildLookupAsync("globalparameter", token => _restClient.GetGlobalParametersAsync(token), new[] { "ID", "Id", "Name" }, new[] { "Name" }));
        if (needsSsh || needsUnknownTypeLookup)
            lookupTasks.Add(BuildLookupAsync("sshkey", token => _restClient.GetSshKeysAsync(token), new[] { "ID", "Id", "KeyID", "KeyId" }, new[] { "Name" }));
        if (capabilities.PgpLicensed && (needsPgp || needsUnknownTypeLookup))
            lookupTasks.Add(BuildLookupAsync("pgpkey", token => _restClient.GetPgpKeysAsync(token), new[] { "ID", "Id", "KeyID", "KeyId" }, new[] { "Name", "UID", "Uid" }));
        if (needsTls || needsUnknownTypeLookup)
            lookupTasks.Add(BuildLookupAsync("sslcert", token => _restClient.GetTlsCertificatesAsync(token), new[] { "ID", "Id", "Thumbprint", "SHA1", "KeyID", "KeyId" }, new[] { "Name", "Subject", "CN" }));

        if (lookupTasks.Count == 0) return records;
        var lookups = await Task.WhenAll(lookupTasks);

        for (var index = 0; index < records.Length; index++)
        {
            var record = records[index];
            if (string.IsNullOrWhiteSpace(record.TargetId)) continue;

            if (!string.IsNullOrWhiteSpace(record.TargetType))
            {
                if (!MissingName(record)) continue;
                var typed = lookups.FirstOrDefault(lookup => TargetTypesMatch(record.TargetType, lookup.Type));
                if (typed is not null && typed.Items.TryGetValue(record.TargetId, out var typedName) && !string.IsNullOrWhiteSpace(typedName))
                {
                    records[index] = TargetTypesMatch(record.TargetType, "task") && string.IsNullOrWhiteSpace(record.TaskName)
                        ? record with { TargetName = typedName, TaskName = typedName }
                        : record with { TargetName = typedName };
                }
                continue;
            }

            var matches = lookups
                .Where(lookup => lookup.Items.TryGetValue(record.TargetId, out _))
                .Select(lookup => new { lookup.Type, Name = lookup.Items[record.TargetId] })
                .ToArray();

            if (matches.Length == 1)
            {
                records[index] = matches[0].Type.Equals("task", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(record.TaskName)
                    ? record with { TargetType = matches[0].Type, TargetName = matches[0].Name, TaskName = matches[0].Name }
                    : record with { TargetType = matches[0].Type, TargetName = matches[0].Name };
            }
        }

        return records;
    }

    // Purpose: Builds lookup core.
    private async Task<AuditTargetLookup> BuildLookupCoreAsync(
        string type,
        Func<CancellationToken, Task<IReadOnlyList<JsonObject>>> loader,
        string[] idFields,
        string[] nameFields,
        CancellationToken cancellationToken)
    {
        var items = await loader(cancellationToken);
        return new AuditTargetLookup(type, BuildIdNameMap(items, idFields, nameFields));
    }

    // Purpose: Builds script lookup.
    private async Task<AuditTargetLookup> BuildScriptLookupAsync(CancellationToken cancellationToken)
    {
        var capabilities = MoveItLicensePolicy.GetCapabilities(_sessionService.Get());
        var standardTask = _restClient.GetStandardScriptsAsync(cancellationToken);
        if (!capabilities.CustomScriptsLicensed)
        {
            var standardScripts = await standardTask;
            return new AuditTargetLookup("script", BuildIdNameMap(standardScripts, new[] { "ID", "Id" }, new[] { "Name" }));
        }

        var customTask = _restClient.GetCustomScriptsAsync(cancellationToken);
        await Task.WhenAll(customTask, standardTask);
        var scripts = (await customTask).Concat(await standardTask);
        return new AuditTargetLookup("script", BuildIdNameMap(scripts, new[] { "ID", "Id" }, new[] { "Name" }));
    }

    // Purpose: Returns whether reported and lookup Audit target types represent the same resource type.
    private static bool TargetTypesMatch(string reportedType, string lookupType)
    {
        var left = (reportedType ?? string.Empty).Replace("_", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);
        var right = (lookupType ?? string.Empty).Replace("_", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);
        if (left.Equals(right, StringComparison.OrdinalIgnoreCase)) return true;
        if (left.Contains("date", StringComparison.OrdinalIgnoreCase) && right.Contains("date", StringComparison.OrdinalIgnoreCase)) return true;
        if (left.Contains("global", StringComparison.OrdinalIgnoreCase) && right.Contains("global", StringComparison.OrdinalIgnoreCase)) return true;
        if (left.Contains("ssh", StringComparison.OrdinalIgnoreCase) && right.Contains("ssh", StringComparison.OrdinalIgnoreCase)) return true;
        if (left.Contains("pgp", StringComparison.OrdinalIgnoreCase) && right.Contains("pgp", StringComparison.OrdinalIgnoreCase)) return true;
        if ((left.Contains("ssl", StringComparison.OrdinalIgnoreCase) || left.Contains("tls", StringComparison.OrdinalIgnoreCase) || left.Contains("cert", StringComparison.OrdinalIgnoreCase)) && right.Contains("sslcert", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private sealed record AuditTargetLookup(string Type, IReadOnlyDictionary<string, string> Items);

    // Purpose: Returns whether an Audit row needs target-name enrichment.
    private static bool MissingName(AuditReportRecord record) => string.IsNullOrWhiteSpace(record.TargetName) && !string.IsNullOrWhiteSpace(record.TargetId);

    // Purpose: Returns whether target type.
    private static bool IsTargetType(string value, string fragment)
        => (value ?? string.Empty).Contains(fragment, StringComparison.OrdinalIgnoreCase);

    // Purpose: Builds ID name map.
    private static IReadOnlyDictionary<string, string> BuildIdNameMap(
        IEnumerable<JsonObject> items,
        IReadOnlyList<string> idFields,
        IReadOnlyList<string> nameFields)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var id = TextAny(item, idFields.ToArray());
            var name = TextAny(item, nameFields.ToArray());
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name)) map[id] = name;
        }
        return map;
    }

    // Purpose: Adds numeric cursor.
    private static string AddNumericCursor(string predicate, string fieldName, string? cursor)
    {
        if (!long.TryParse((cursor ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value <= 0)
            return predicate;
        return $"{predicate};{fieldName}<{value.ToString(CultureInfo.InvariantCulture)}";
    }

    // Purpose: Builds predicate.
    private static string BuildPredicate(string basePredicate, string fieldName, string? value, bool numeric)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) return basePredicate;
        if (numeric && long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            return $"{basePredicate};{fieldName}=={number.ToString(CultureInfo.InvariantCulture)}";
        return $"{basePredicate};{fieldName}=={QuoteRsql(normalized)}";
    }

    // Purpose: Quotes RSQL.
    private static string QuoteRsql(string value) => $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    // Purpose: Maps task run.
    private static TaskRunReportRecord MapTaskRun(JsonObject item)
    {
        var taskId = Text(item, "TaskID");
        var start = Date(item, "StartTime");
        var end = Date(item, "EndTime");
        var duration = Long(item, "DurationSecond");
        if (!duration.HasValue && start.HasValue && end.HasValue)
            duration = Math.Max(0, (long)Math.Round((end.Value - start.Value).TotalSeconds));
        var runId = Text(item, "RunID");
        var statusCode = Text(item, "StatusCode");
        var status = ResolveStatus(TextAny(item, "Status", "Result", "Outcome", "StatusText"), statusCode);
        var rowId = runId.Length > 0 ? $"run-{runId}" : $"run-{taskId}-{start?.ToUnixTimeMilliseconds() ?? 0}";
        return new TaskRunReportRecord(
            rowId, taskId, Text(item, "TaskName"), start, end, duration,
            status, TextAny(item, "StatusMsg", "StatusMessage", "ErrorText"), Int(item, "FilesSent"), Double(item, "TotalBytesSent"), Text(item, "StartedBy"),
            runId, Date(item, "ScheduledTime"), Text(item, "NominalStart"), Text(item, "Node"), statusCode, Date(item, "LogStamp"), Text(item, "RecType"), Text(item, "QueryType"));
    }

    // Purpose: Maps file activity.
    private static FileActivityReportRecord MapFileActivity(JsonObject item)
    {
        var logId = Text(item, "LogID");
        var transferBytes = Double(item, "TransferBytes");
        var destinationBytes = Double(item, "DestBytes");
        var sourceBytes = Double(item, "SourceBytes");
        var size = FirstUsefulByteCount(transferBytes, destinationBytes, sourceBytes);
        var statusCode = Text(item, "StatusCode");
        var status = ResolveStatus(TextAny(item, "Status", "Result", "Outcome", "StatusText"), statusCode);
        return new FileActivityReportRecord(
            logId.Length > 0 ? $"activity-{logId}" : $"activity-{Guid.NewGuid():N}",
            logId, DateAny(item, "LogStamp", "LogTime"), Text(item, "TaskID"), Text(item, "TaskName"), TextAny(item, "SourceFile", "SourceFilename"), TextAny(item, "Action", "Activity"),
            status, TextAny(item, "StatusMsg", "StatusMessage", "ErrorText"), size, Text(item, "SourcePath"), TextAny(item, "DestPath", "DestinationPath"), Text(item, "SourceHost"), TextAny(item, "DestHost", "DestinationHost"), TextAny(item, "DestFile", "DestinationFile"),
            sourceBytes, destinationBytes, transferBytes, Double(item, "SourceDuration"), Double(item, "DestDuration"), Text(item, "SourceFileID"), Text(item, "DestFileID"), Text(item, "SourceStamp"),
            Date(item, "StartTime"), Date(item, "ScheduledTime"), Text(item, "NominalStart"), Text(item, "Node"), statusCode,
            Text(item, "SourceASxMsgID"), Text(item, "SourceASxMDN"), Text(item, "DestASxMsgID"), Text(item, "DestASxMDN"), Text(item, "QueryType"));
    }

    // Purpose: Maps audit.
    private static AuditReportRecord MapAudit(JsonObject item)
    {
        var logId = TextAny(item, "LogID", "ID");
        var action = Text(item, "Action");
        var targetType = TextAny(item, "TargetType", "Target_Type", "TargetObjectType", "ObjectType", "ResourceType", "Type");
        if (targetType.Length == 0) targetType = InferAuditTargetType(action);
        var targetId = TextAny(item, "TargetID", "TargetId", "Target_ID", "ObjectID", "ObjectId", "ResourceID", "ResourceId");
        var taskName = Text(item, "TaskName");
        var targetName = TextAny(item, "TargetName", "Target_Name", "TargetDisplayName", "ObjectName", "ResourceName", "Name");
        if (targetName.Length == 0 && IsTargetType(targetType, "task")) targetName = taskName;
        if (targetName.Length == 0 && IsTargetType(targetType, "global") && targetId.Length > 0) targetName = targetId;
        var statusCode = TextAny(item, "StatusCode", "Error");
        var status = ResolveStatus(TextAny(item, "Status", "Result", "Outcome", "StatusText"), statusCode);
        return new AuditReportRecord(
            logId.Length > 0 ? $"audit-{logId}" : $"audit-{Guid.NewGuid():N}",
            logId, DateAny(item, "LogTime", "LogStamp"), action, targetType, targetId, targetName,
            status, TextAny(item, "StatusMsg", "StatusMessage", "ErrorText", "Message"), Text(item, "Username"), Text(item, "IPAddress"), Text(item, "ClientIP"), taskName,
            Text(item, "Node"), statusCode, Text(item, "AgentBrand"), Text(item, "AgentVersion"), Text(item, "CentralVersion"), Text(item, "QueryType"), Text(item, "Hash"));
    }

    // Older servers omit this field. Accept only native true/1, never a nonempty string such as "0".
    internal static bool HasAuditDiff(JsonObject item)
        => Text(item, "HasAuditDiff") is var value && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));

    // Purpose: Infers a likely Audit target type from the action text.
    private static string InferAuditTargetType(string action)
    {
        var value = (action ?? string.Empty).Trim().ToLowerInvariant();
        if (value.StartsWith("task_", StringComparison.Ordinal) || value.Contains("task", StringComparison.Ordinal)) return "task";
        if (value.StartsWith("host_", StringComparison.Ordinal) || value.Contains("host", StringComparison.Ordinal)) return "host";
        if (value.Contains("script", StringComparison.Ordinal)) return "script";
        if (value.Contains("datelist", StringComparison.Ordinal) || value.Contains("date_list", StringComparison.Ordinal)) return "datelist";
        if (value.Contains("pgp", StringComparison.Ordinal)) return "pgpkey";
        if (value.Contains("ssh", StringComparison.Ordinal)) return "sshkey";
        if (value.Contains("ssl", StringComparison.Ordinal) || value.Contains("tls", StringComparison.Ordinal) || value.Contains("cert", StringComparison.Ordinal)) return "sslcert";
        if (value.Contains("global", StringComparison.Ordinal) && value.Contains("param", StringComparison.Ordinal)) return "globalparameter";
        return string.Empty;
    }

    // Purpose: Resolves status.
    internal static string ResolveStatus(string status, string statusCode)
    {
        var value = NormalizeStatus(status);
        if (value.Length > 0) return value;
        if (long.TryParse(statusCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
            return code == 0 ? "Success" : "Failed";
        return string.Empty;
    }

    // Purpose: Returns the first available byte-count value from the supplied candidates.
    private static double? FirstUsefulByteCount(params double?[] values)
    {
        foreach (var value in values) if (value.HasValue && value.Value > 0) return value;
        return values.FirstOrDefault(value => value.HasValue);
    }

    // Purpose: Normalizes status.
    private static string NormalizeStatus(string status)
    {
        var value = (status ?? string.Empty).Trim();
        if (value.Equals("Failure", StringComparison.OrdinalIgnoreCase)) return "Failed";
        if (value.Equals("No xfers", StringComparison.OrdinalIgnoreCase) || value.Equals("No Transfers", StringComparison.OrdinalIgnoreCase)) return "No Transfers";
        return value;
    }

    // Purpose: Returns the first nonblank text value from the candidate JSON fields.
    private static string TextAny(JsonObject item, params string[] names)
    {
        foreach (var name in names)
        {
            var value = Text(item, name);
            if (value.Length > 0) return value;
        }
        return string.Empty;
    }

    // Purpose: Returns the first valid date value from the candidate JSON fields.
    private static DateTimeOffset? DateAny(JsonObject item, params string[] names)
    {
        foreach (var name in names)
        {
            var value = Date(item, name);
            if (value.HasValue) return value;
        }
        return null;
    }

    // Purpose: Reads a text value from the supplied data.
    private static string Text(JsonObject item, string name)
    {
        if (!TryGet(item, name, out var node) || node is null) return string.Empty;
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text)) return text?.Trim() ?? string.Empty;
            return value.ToJsonString().Trim('"');
        }
        return node.ToJsonString();
    }

    // Purpose: Reads a int value from the supplied data.
    private static int? Int(JsonObject item, string name)
    {
        if (!TryGet(item, name, out var node) || node is null) return null;
        if (node is JsonValue value && value.TryGetValue<int>(out var number)) return number;
        return int.TryParse(Text(item, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    // Purpose: Reads a long value from the supplied data.
    private static long? Long(JsonObject item, string name)
    {
        if (!TryGet(item, name, out var node) || node is null) return null;
        if (node is JsonValue value && value.TryGetValue<long>(out var number)) return number;
        return long.TryParse(Text(item, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    // Purpose: Reads a double value from the supplied data.
    private static double? Double(JsonObject item, string name)
    {
        if (!TryGet(item, name, out var node) || node is null) return null;
        if (node is JsonValue value && value.TryGetValue<double>(out var number)) return number;
        return double.TryParse(Text(item, name), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    // Purpose: Reads a date value from the supplied data.
    private static DateTimeOffset? Date(JsonObject item, string name)
    {
        var text = Text(item, name);
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed) ? parsed : null;
    }

    // Purpose: Performs a case-insensitive JSON property lookup.
    private static bool TryGet(JsonObject item, string name, out JsonNode? node)
    {
        if (item.TryGetPropertyValue(name, out node)) return true;
        foreach (var pair in item)
        {
            if (pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                node = pair.Value;
                return true;
            }
        }
        node = null;
        return false;
    }
}
