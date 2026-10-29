// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Globalization;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.Metadata;

namespace MoveItAutomationUI.Services.MoveIt;

public interface IAutomationTaskCalendarService
{
    DateTime GetServerToday();
    Task<TaskCalendarSnapshot> GetCalendarAsync(
        DateTime startDate,
        DateTime endDateExclusive,
        string environmentKey,
        CancellationToken cancellationToken = default);
}

public sealed record TaskCalendarEvent(
    string Id,
    string TaskId,
    string TaskName,
    string Description,
    DateTimeOffset EventTime,
    DateTimeOffset? EndTime,
    string Kind,
    string Status,
    string StatusMessage,
    long? DurationSeconds,
    string RunId,
    string Schedule,
    string Notes,
    string SourceHostType,
    string SourceHostTypes,
    string ResourceGroup,
    string ResourceGroups,
    string MauiFolder);

public sealed record TaskCalendarSnapshot(
    IReadOnlyList<TaskCalendarEvent> Events,
    int UnsupportedScheduleCount,
    bool HistoricalLimitReached,
    DateTimeOffset GeneratedUtc);

public sealed class AutomationTaskCalendarService : IAutomationTaskCalendarService
{
    private const string ObjectType = "Task";
    private const int MaximumCalendarRangeDays = 62;
    private const int MaximumHistoricalRows = 100000;

    private readonly IMoveItRestClient _restClient;
    private readonly IAutomationScheduleForecastService _scheduleForecastService;
    private readonly IAutomationHostService _hostService;
    private readonly IMoveItServerTimeService _serverTimeService;
    private readonly IMetadataRepository _metadataRepository;

    public AutomationTaskCalendarService(
        IMoveItRestClient restClient,
        IAutomationScheduleForecastService scheduleForecastService,
        IAutomationHostService hostService,
        IMoveItServerTimeService serverTimeService,
        IMetadataRepository metadataRepository)
    {
        _restClient = restClient;
        _scheduleForecastService = scheduleForecastService;
        _hostService = hostService;
        _serverTimeService = serverTimeService;
        _metadataRepository = metadataRepository;
    }

    public DateTime GetServerToday() => _serverTimeService.ConvertUtcToServerTime(DateTimeOffset.UtcNow).Date;

    public async Task<TaskCalendarSnapshot> GetCalendarAsync(
        DateTime startDate,
        DateTime endDateExclusive,
        string environmentKey,
        CancellationToken cancellationToken = default)
    {
        startDate = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Unspecified);
        endDateExclusive = DateTime.SpecifyKind(endDateExclusive.Date, DateTimeKind.Unspecified);
        if (endDateExclusive <= startDate)
            throw new ArgumentException("Calendar end date must be after the start date.");
        if ((endDateExclusive - startDate).TotalDays > MaximumCalendarRangeDays)
            throw new ArgumentOutOfRangeException(nameof(endDateExclusive), $"Calendar ranges cannot exceed {MaximumCalendarRangeDays} days.");

        var startUtc = ResolveBoundary(startDate, useLatest: false).ToUniversalTime();
        var endUtc = ResolveBoundary(endDateExclusive, useLatest: true).ToUniversalTime();
        var nowUtc = DateTimeOffset.UtcNow;

        var taskDefinitionsTask = _restClient.GetTaskHostReferenceDefinitionsAsync(cancellationToken);
        var hostTypesTask = _hostService.GetHostTypeMapAsync(cancellationToken);
        var foldersTask = _metadataRepository.GetFoldersAsync(environmentKey, ObjectType, cancellationToken);
        var forecastTask = _scheduleForecastService.GetRunsAsync(startUtc, endUtc, cancellationToken);
        var historicalTask = startUtc < nowUtc
            ? GetHistoricalRunsAsync(startUtc, endUtc < nowUtc ? endUtc : nowUtc, cancellationToken)
            : Task.FromResult<IReadOnlyList<JsonObject>>(Array.Empty<JsonObject>());

        await Task.WhenAll(taskDefinitionsTask, hostTypesTask, foldersTask, forecastTask, historicalTask);

        var taskDefinitions = await taskDefinitionsTask;
        var taskIds = taskDefinitions
            .Select(task => MoveItRestClient.GetString(task, "ID").Trim())
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var metadataByTask = await _metadataRepository.GetObjectMetadataBatchAsync(
            environmentKey,
            ObjectType,
            taskIds,
            cancellationToken);

        var folders = await foldersTask;
        var folderNames = BuildFolderPathMap(folders);
        var hostTypes = await hostTypesTask;
        var taskContext = taskDefinitions
            .Select(task => BuildTaskContext(task, hostTypes, metadataByTask, folderNames))
            .Where(context => context.TaskId.Length > 0)
            .GroupBy(context => context.TaskId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var events = new List<TaskCalendarEvent>();
        foreach (var item in await historicalTask)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var eventTime = ParseServerDate(item, "StartTime")
                ?? ParseServerDate(item, "ScheduledTime")
                ?? ParseServerDate(item, "LogStamp");
            if (!eventTime.HasValue) continue;
            var eventUtc = eventTime.Value.ToUniversalTime();
            if (eventUtc < startUtc || eventUtc >= endUtc) continue;

            var taskId = ReadText(item, "TaskID").Trim();
            var taskName = ReadText(item, "TaskName").Trim();
            taskContext.TryGetValue(taskId, out var context);
            var runId = ReadText(item, "RunID").Trim();
            var statusCode = ReadText(item, "StatusCode").Trim();
            var status = AutomationReportService.ResolveStatus(
                ReadFirstText(item, "Status", "Result", "Outcome", "StatusText"),
                statusCode);

            events.Add(new TaskCalendarEvent(
                runId.Length > 0 ? $"actual:{runId}" : $"actual:{taskId}:{eventUtc:yyyyMMddHHmmss}",
                taskId,
                taskName.Length > 0 ? taskName : context?.TaskName ?? taskId,
                context?.Description ?? string.Empty,
                eventTime.Value,
                ParseServerDate(item, "EndTime"),
                "actual",
                status,
                ReadFirstText(item, "StatusMsg", "StatusMessage", "ErrorText"),
                ReadLong(item, "DurationSecond"),
                runId,
                string.Empty,
                string.Empty,
                context?.SourceHostType ?? "Unknown",
                context?.SourceHostTypes ?? "Unknown",
                context?.ResourceGroup ?? "No Resource Group",
                context?.ResourceGroups ?? "No Resource Group",
                context?.MauiFolder ?? "All Tasks"));
        }

        var forecast = await forecastTask;
        foreach (var run in forecast.Runs)
        {
            taskContext.TryGetValue(run.TaskId, out var context);
            var utc = run.ScheduledTime.ToUniversalTime();
            events.Add(new TaskCalendarEvent(
                $"upcoming:{run.TaskId}:{utc:yyyyMMddHHmm}:{run.ScheduleId}",
                run.TaskId,
                run.TaskName,
                run.Description,
                run.ScheduledTime,
                null,
                "upcoming",
                "Upcoming",
                string.Empty,
                null,
                string.Empty,
                run.Schedule,
                run.Notes,
                context?.SourceHostType ?? "Unknown",
                context?.SourceHostTypes ?? "Unknown",
                context?.ResourceGroup ?? "No Resource Group",
                context?.ResourceGroups ?? "No Resource Group",
                context?.MauiFolder ?? "All Tasks"));
        }

        var historicalRows = await historicalTask;
        return new TaskCalendarSnapshot(
            events
                .OrderBy(item => item.EventTime.ToUniversalTime())
                .ThenBy(item => item.TaskName, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            forecast.UnsupportedScheduleCount,
            historicalRows.Count >= MaximumHistoricalRows,
            DateTimeOffset.UtcNow);
    }

    private async Task<IReadOnlyList<JsonObject>> GetHistoricalRunsAsync(
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken cancellationToken)
    {
        if (endUtc <= startUtc) return Array.Empty<JsonObject>();

        var startLocal = _serverTimeService.ConvertUtcToServerTime(startUtc)
            .ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        var endLocal = _serverTimeService.ConvertUtcToServerTime(endUtc)
            .ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        var predicate = $"LogStamp>=\"{startLocal}\";LogStamp<\"{endLocal}\"";
        return await _restClient.GetTaskRunReportAsync(
            predicate,
            "StartTime",
            MaximumHistoricalRows,
            cancellationToken);
    }

    private DateTimeOffset ResolveBoundary(DateTime localDate, bool useLatest)
    {
        var resolved = _serverTimeService.ResolveServerLocalTime(localDate);
        if (resolved.Count == 0)
            throw new InvalidOperationException($"MOVEit server local time '{localDate:yyyy-MM-dd HH:mm}' cannot be resolved.");
        return useLatest
            ? resolved.OrderByDescending(value => value.ToUniversalTime()).First()
            : resolved.OrderBy(value => value.ToUniversalTime()).First();
    }

    private TaskContext BuildTaskContext(
        JsonObject task,
        IReadOnlyDictionary<string, string> hostTypes,
        IReadOnlyDictionary<string, ObjectMetadataSnapshot> metadataByTask,
        IReadOnlyDictionary<int, string> folderNames)
    {
        var taskId = MoveItRestClient.GetString(task, "ID").Trim();
        var taskName = MoveItRestClient.GetString(task, "Name").Trim();
        var description = MoveItRestClient.GetString(task["Info"] as JsonObject, "Description").Trim();
        var groups = ReadStringValues(task["Group"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var sourceTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectSourceTypes(task["steps"], hostTypes, sourceTypes);
        CollectSourceTypes(task["NextActions"], hostTypes, sourceTypes);
        var sourceTypeList = sourceTypes.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();

        var sourceHostType = sourceTypeList.Length switch
        {
            0 => "No Source",
            1 => sourceTypeList[0],
            _ => "Multiple Source Types"
        };
        var sourceHostTypes = sourceTypeList.Length == 0 ? "No Source" : string.Join(", ", sourceTypeList);
        var resourceGroup = groups.Length switch
        {
            0 => "No Resource Group",
            1 => groups[0],
            _ => "Multiple Resource Groups"
        };
        var resourceGroups = groups.Length == 0 ? "No Resource Group" : string.Join(", ", groups);

        var folderName = "All Tasks";
        if (metadataByTask.TryGetValue(taskId, out var metadata)
            && metadata.FolderId.HasValue
            && folderNames.TryGetValue(metadata.FolderId.Value, out var path))
            folderName = path;

        return new TaskContext(taskId, taskName, description, sourceHostType, sourceHostTypes, resourceGroup, resourceGroups, folderName);
    }

    private static void CollectSourceTypes(
        JsonNode? node,
        IReadOnlyDictionary<string, string> hostTypes,
        ISet<string> sourceTypes)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj)
            {
                if (pair.Key.Equals("Source", StringComparison.OrdinalIgnoreCase) && pair.Value is JsonObject source)
                {
                    var hostId = MoveItRestClient.GetString(source, "HostID").Trim();
                    var typeCode = MoveItRestClient.GetString(source, "Type").Trim();
                    if (hostId.Length > 0 && hostId != "0" && hostTypes.TryGetValue(hostId, out var hostType) && hostType.Length > 0)
                        sourceTypes.Add(hostType);
                    else if (typeCode.Length > 0)
                        sourceTypes.Add(AutomationHostService.FriendlyHostType(typeCode));
                    else if (hostId == "0")
                        sourceTypes.Add("Local File System");
                }

                CollectSourceTypes(pair.Value, hostTypes, sourceTypes);
            }
            return;
        }

        if (node is JsonArray array)
            foreach (var item in array) CollectSourceTypes(item, hostTypes, sourceTypes);
    }

    private static IReadOnlyDictionary<int, string> BuildFolderPathMap(IReadOnlyList<MetadataFolderRecord> folders)
    {
        var byId = folders.ToDictionary(folder => folder.Id);
        var result = new Dictionary<int, string>();

        string Resolve(int id, HashSet<int> visiting)
        {
            if (result.TryGetValue(id, out var cached)) return cached;
            if (!byId.TryGetValue(id, out var folder)) return string.Empty;
            if (!visiting.Add(id)) return folder.Name;

            var parent = folder.ParentId.HasValue ? Resolve(folder.ParentId.Value, visiting) : string.Empty;
            visiting.Remove(id);
            var path = parent.Length > 0 ? $"{parent} / {folder.Name}" : folder.Name;
            result[id] = path;
            return path;
        }

        foreach (var folder in folders) _ = Resolve(folder.Id, new HashSet<int>());
        return result;
    }

    private DateTimeOffset? ParseServerDate(JsonObject item, string fieldName)
    {
        var text = ReadText(item, fieldName);
        if (text.Length == 0) return null;

        // Prefer an explicit zone supplied by MOVEit (or added safely by the shared server-time service).
        // Never let DateTimeOffset infer this web server's local zone for an unzoned MOVEit server timestamp.
        var normalized = _serverTimeService.NormalizeKnownServerLocalTimestamp(text);
        if (HasExplicitZone(normalized)
            && DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var zoned))
            return zoned;

        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var local))
            return null;

        var resolved = _serverTimeService.ResolveServerLocalTime(DateTime.SpecifyKind(local, DateTimeKind.Unspecified));
        return resolved.Count == 0
            ? null
            : resolved.OrderBy(value => value.ToUniversalTime()).First();
    }

    private static bool HasExplicitZone(string value)
    {
        if (value.EndsWith("Z", StringComparison.OrdinalIgnoreCase)) return true;
        var separator = value.IndexOf('T');
        if (separator < 0) separator = value.IndexOf(' ');
        if (separator < 0) return false;
        return value.IndexOf('+', separator + 1) >= 0 || value.IndexOf('-', separator + 1) >= 0;
    }

    private static string ReadFirstText(JsonObject item, params string[] names)
    {
        foreach (var name in names)
        {
            var value = ReadText(item, name);
            if (value.Length > 0) return value;
        }
        return string.Empty;
    }

    private static string ReadText(JsonObject item, string name)
    {
        JsonNode? node = null;
        if (!item.TryGetPropertyValue(name, out node))
        {
            foreach (var pair in item)
                if (pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    node = pair.Value;
                    break;
                }
        }

        if (node is null) return string.Empty;
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text)) return text?.Trim() ?? string.Empty;
            return value.ToJsonString().Trim('"');
        }
        return node.ToJsonString();
    }

    private static long? ReadLong(JsonObject item, string name)
        => long.TryParse(ReadText(item, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static IEnumerable<string> ReadStringValues(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                var text = item is JsonValue value && value.TryGetValue<string>(out var raw)
                    ? raw?.Trim() ?? string.Empty
                    : item?.ToJsonString().Trim('"') ?? string.Empty;
                if (text.Length > 0) yield return text;
            }
            yield break;
        }

        if (node is JsonValue single && single.TryGetValue<string>(out var valueText))
        {
            var text = valueText?.Trim() ?? string.Empty;
            if (text.Length > 0) yield return text;
        }
    }

    private sealed record TaskContext(
        string TaskId,
        string TaskName,
        string Description,
        string SourceHostType,
        string SourceHostTypes,
        string ResourceGroup,
        string ResourceGroups,
        string MauiFolder);
}
