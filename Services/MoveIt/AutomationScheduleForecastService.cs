// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Globalization;
using System.Text.Json.Nodes;

namespace MoveItAutomationUI.Services.MoveIt;

public interface IAutomationScheduleForecastService
{
    // Calculates scheduled Task occurrences for MAUI's standard Upcoming Runs forecast window.
    Task<UpcomingRunForecast> GetUpcomingRunsAsync(CancellationToken cancellationToken = default);

    // Calculates future scheduled Task occurrences for an explicit absolute range. Calendar views use
    // this path so their forecast horizon follows the visible date range instead of a fixed window.
    Task<UpcomingRunForecast> GetRunsAsync(DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken = default);
}

public sealed record UpcomingRunOccurrence(
    string TaskId,
    string TaskName,
    string Description,
    DateTimeOffset ScheduledTime,
    string Schedule,
    string Notes,
    string ScheduleId);

public sealed record UpcomingRunForecast(
    IReadOnlyList<UpcomingRunOccurrence> Runs,
    int UnsupportedScheduleCount);

public sealed class AutomationScheduleForecastService : IAutomationScheduleForecastService
{
    private const int ForecastHours = 24;
    private const int MaximumForecastDays = 93;

    private readonly IMoveItRestClient _rest;
    private readonly IMoveItServerTimeService _serverTimeService;
    private readonly ILogger<AutomationScheduleForecastService> _logger;

    public AutomationScheduleForecastService(
        IMoveItRestClient rest,
        IMoveItServerTimeService serverTimeService,
        ILogger<AutomationScheduleForecastService> logger)
    {
        _rest = rest;
        _serverTimeService = serverTimeService;
        _logger = logger;
    }

    public Task<UpcomingRunForecast> GetUpcomingRunsAsync(CancellationToken cancellationToken = default)
    {
        var generatedUtc = DateTimeOffset.UtcNow;
        // Preserve the existing Upcoming Runs boundary behavior: current instant excluded, 24-hour endpoint included.
        return CalculateRunsAsync(generatedUtc, generatedUtc.AddHours(ForecastHours), includeStart: false, includeEnd: true, cancellationToken);
    }

    public Task<UpcomingRunForecast> GetRunsAsync(
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken cancellationToken = default)
        => CalculateRunsAsync(startUtc, endUtc, includeStart: true, includeEnd: false, cancellationToken);

    private async Task<UpcomingRunForecast> CalculateRunsAsync(
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        bool includeStart,
        bool includeEnd,
        CancellationToken cancellationToken)
    {
        startUtc = startUtc.ToUniversalTime();
        endUtc = endUtc.ToUniversalTime();
        if (endUtc <= startUtc) return new UpcomingRunForecast(Array.Empty<UpcomingRunOccurrence>(), 0);
        if (endUtc - startUtc > TimeSpan.FromDays(MaximumForecastDays))
            throw new ArgumentOutOfRangeException(nameof(endUtc), $"Schedule forecast ranges cannot exceed {MaximumForecastDays} days.");

        // Forecasting is intentionally future-only. A Calendar range may begin in the past, in which case
        // historical runs come from MOVEit's Task Run report and projection begins at the current instant.
        var forecastStartUtc = startUtc > DateTimeOffset.UtcNow ? startUtc : DateTimeOffset.UtcNow;
        if (endUtc <= forecastStartUtc) return new UpcomingRunForecast(Array.Empty<UpcomingRunOccurrence>(), 0);

        var startLocal = _serverTimeService.ConvertUtcToServerTime(forecastStartUtc);
        var endLocal = _serverTimeService.ConvertUtcToServerTime(endUtc);

        var taskDefinitionsTask = _rest.GetTaskScheduleDefinitionsAsync(cancellationToken);
        var dateListsTask = _rest.GetDateListsWithEntriesAsync(cancellationToken);
        await Task.WhenAll(taskDefinitionsTask, dateListsTask);

        var tasks = await taskDefinitionsTask;
        var dateLists = BuildDateListMap(await dateListsTask);
        var runsByMinute = new Dictionary<string, UpcomingRunOccurrence>(StringComparer.OrdinalIgnoreCase);
        var unsupportedCount = 0;

        // Include the previous local date so an unsplit overnight interval that began before midnight
        // can still contribute post-midnight occurrences inside the requested forecast window.
        var firstDate = startLocal.Date.AddDays(-1);
        var lastDate = endLocal.Date;

        foreach (var task in tasks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var taskId = MoveItRestClient.GetString(task, "ID").Trim();
            var taskName = MoveItRestClient.GetString(task, "Name").Trim();
            if (taskId.Length == 0 || taskName.Length == 0) continue;
            if (!IsOne(task, "Active")) continue;

            var scheduledState = MoveItRestClient.GetString(task, "Scheduled").Trim();
            if (scheduledState.Equals("DISABLED", StringComparison.OrdinalIgnoreCase)
                || scheduledState.Equals("INCOMPLETE", StringComparison.OrdinalIgnoreCase)
                || scheduledState.Equals("UNSCHEDULED", StringComparison.OrdinalIgnoreCase))
                continue;

            var description = MoveItRestClient.GetString(task["Info"] as JsonObject, "Description").Trim();
            var schedules = GetObjectList((task["Schedules"] as JsonObject)?["Schedule"] ?? task["Schedules"]);
            if (schedules.Count == 0) continue;

            foreach (var schedule in schedules)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var scheduleId = MoveItRestClient.GetString(schedule, "ID").Trim();
                var scheduleCountedUnsupported = false;
                void MarkUnsupported(string reason)
                {
                    if (!scheduleCountedUnsupported)
                    {
                        unsupportedCount++;
                        scheduleCountedUnsupported = true;
                    }

                    LogUnsupported(taskId, taskName, scheduleId, reason);
                }

                var multi = GetInt(schedule, "Multi");
                if (multi is < 0 or > 2)
                {
                    MarkUnsupported($"The schedule contains an unsupported Multi value ({multi}).");
                    continue;
                }

                var dateListRefs = GetObjectList(schedule["DateListRef"]);
                if (multi != 0 && dateListRefs.Count > 0)
                {
                    MarkUnsupported("MOVEit does not support combining Date Lists with an overnight schedule.");
                    continue;
                }

                if (dateListRefs.Any(reference =>
                    {
                        var id = MoveItRestClient.GetString(reference, "ID").Trim();
                        return id.Length == 0 || !dateLists.ContainsKey(id);
                    }))
                {
                    MarkUnsupported("The schedule references a Date List that could not be loaded for the current MOVEit session.");
                    continue;
                }

                var intervals = GetObjectList((schedule["Frequency"] as JsonObject)?["Interval"] ?? schedule["Frequency"]);
                if (intervals.Count == 0)
                {
                    MarkUnsupported("The schedule did not contain a Frequency.Interval definition.");
                    continue;
                }

                if (!HasPositiveDateCriteria(schedule, dateListRefs))
                {
                    // MOVEit defines a schedule with no days/date criteria as a one-time run at the first
                    // occurrence of its start time. REST does not expose enough state to determine whether
                    // that one-time occurrence already happened, so omit it rather than inventing a run.
                    MarkUnsupported("The schedule has no day/date criteria and represents a one-time run whose completed state is not exposed by REST.");
                    continue;
                }

                var parsedIntervals = new List<ScheduleInterval>();

                foreach (var interval in intervals)
                {
                    if (!TryParseInterval(interval, out var parsed, out var intervalError))
                    {
                        MarkUnsupported(intervalError);
                        continue;
                    }

                    // MOVEit may return an overnight range either as its documented Multi=1/Multi=2 split
                    // or as a single interval whose EndTime is earlier than StartTime. Support both forms,
                    // but reject a crossing interval that is already marked as one half of a Multi split.
                    if (parsed.SpansMidnight && multi != 0)
                    {
                        MarkUnsupported("The schedule combines a crossing-midnight interval with a Multi split marker.");
                        continue;
                    }

                    parsedIntervals.Add(parsed);
                }

                if (parsedIntervals.Count == 0) continue;

                if (dateListRefs.Count > 0 && parsedIntervals.Any(interval => interval.SpansMidnight))
                {
                    MarkUnsupported("MOVEit does not support combining Date Lists with an overnight interval.");
                    continue;
                }

                var onlyUntilFirstSuccess = IsOne(schedule, "OnlyUntilFirstSuccess");

                for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
                {
                    // A documented Multi=2 record is the post-midnight half of a schedule that began
                    // on the preceding date. Unsplit overnight intervals use the start date directly.
                    var scheduleDate = multi == 2 ? date.AddDays(-1) : date;
                    if (!DateMatchesSchedule(schedule, dateListRefs, dateLists, scheduleDate)) continue;

                    foreach (var interval in parsedIntervals)
                    {
                        foreach (var localRun in ExpandInterval(date, interval))
                        {
                            foreach (var scheduledTime in _serverTimeService.ResolveServerLocalTime(localRun))
                            {
                                var utc = scheduledTime.ToUniversalTime();
                                if ((includeStart ? utc < forecastStartUtc : utc <= forecastStartUtc)
                                    || (includeEnd ? utc > endUtc : utc >= endUtc))
                                    continue;

                                var notes = onlyUntilFirstSuccess ? "Conditional — only until first success" : string.Empty;
                                var occurrence = new UpcomingRunOccurrence(
                                    taskId,
                                    taskName,
                                    description,
                                    scheduledTime,
                                    DescribeInterval(interval, multi),
                                    notes,
                                    scheduleId);

                                var key = $"{taskId}|{utc:yyyyMMddHHmm}";
                                if (!runsByMinute.TryGetValue(key, out var existing))
                                {
                                    runsByMinute[key] = occurrence;
                                    continue;
                                }

                                // MOVEit does not run the same Task more than once in the same minute simply
                                // because multiple schedules match. Prefer an unconditional occurrence when
                                // one matching schedule is conditional and another is not.
                                var mergedNotes = string.IsNullOrWhiteSpace(existing.Notes) || string.IsNullOrWhiteSpace(notes)
                                    ? string.Empty
                                    : existing.Notes;
                                runsByMinute[key] = existing with
                                {
                                    Schedule = "Multiple schedules",
                                    Notes = mergedNotes,
                                    ScheduleId = string.Empty
                                };
                            }
                        }
                    }
                }
            }
        }

        var runs = runsByMinute.Values
            .OrderBy(item => item.ScheduledTime.ToUniversalTime())
            .ThenBy(item => item.TaskName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new UpcomingRunForecast(runs, unsupportedCount);
    }

    private void LogUnsupported(string taskId, string taskName, string scheduleId, string reason)
    {
        _logger.LogWarning(
            "Upcoming Runs omitted Task {TaskId} ({TaskName}) schedule {ScheduleId}: {Reason}",
            taskId,
            taskName,
            string.IsNullOrWhiteSpace(scheduleId) ? "(unknown)" : scheduleId,
            reason);
    }

    private static Dictionary<string, IReadOnlyList<DatePattern>> BuildDateListMap(IReadOnlyList<JsonObject> dateLists)
    {
        var result = new Dictionary<string, IReadOnlyList<DatePattern>>(StringComparer.OrdinalIgnoreCase);
        foreach (var list in dateLists)
        {
            var id = MoveItRestClient.GetString(list, "ID").Trim();
            if (id.Length == 0) continue;

            var patterns = new List<DatePattern>();
            if (list["Entries"] is JsonArray entries)
            {
                foreach (var entry in entries.OfType<JsonObject>())
                {
                    var value = MoveItRestClient.GetString(entry, "Date").Trim();
                    if (TryParseDatePattern(value, out var pattern)) patterns.Add(pattern);
                }
            }

            result[id] = patterns;
        }

        return result;
    }

    private static bool HasPositiveDateCriteria(JsonObject schedule, IReadOnlyList<JsonObject> dateListRefs)
    {
        var days = schedule["Days"] as JsonObject;
        var hasWeek = GetStringValues(days?["DayOfWeek"]).Count > 0;
        var hasMonth = GetIntValues(days?["DayOfMonth"]).Count > 0;
        var hasIncludeList = dateListRefs.Any(item => IsOne(item, "Run"));
        return hasWeek || hasMonth || hasIncludeList;
    }

    private static bool DateMatchesSchedule(
        JsonObject schedule,
        IReadOnlyList<JsonObject> dateListRefs,
        IReadOnlyDictionary<string, IReadOnlyList<DatePattern>> dateLists,
        DateTime date)
    {
        // Exclude Date Lists override all other day selectors.
        foreach (var reference in dateListRefs.Where(item => !IsOne(item, "Run")))
        {
            var id = MoveItRestClient.GetString(reference, "ID").Trim();
            if (dateLists.TryGetValue(id, out var patterns) && patterns.Any(pattern => pattern.Matches(date)))
                return false;
        }

        var days = schedule["Days"] as JsonObject;
        var weekdayValues = GetStringValues(days?["DayOfWeek"]);
        var monthValues = GetIntValues(days?["DayOfMonth"]);
        var dayMatch = weekdayValues.Any(value => DayOfWeekMatches(value, date.DayOfWeek))
            || monthValues.Contains(date.Day);

        var includeMatch = false;
        foreach (var reference in dateListRefs.Where(item => IsOne(item, "Run")))
        {
            var id = MoveItRestClient.GetString(reference, "ID").Trim();
            if (dateLists.TryGetValue(id, out var patterns) && patterns.Any(pattern => pattern.Matches(date)))
            {
                includeMatch = true;
                break;
            }
        }

        return dayMatch || includeMatch;
    }

    private static bool DayOfWeekMatches(string value, DayOfWeek day)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0) return false;

        if (Enum.TryParse<DayOfWeek>(text, true, out var parsed)) return parsed == day;
        if (text.Length >= 3 && day.ToString().StartsWith(text, StringComparison.OrdinalIgnoreCase)) return true;

        // MOVEit's documented REST contract uses weekday names. Do not infer an undocumented encoding.
        return false;
    }

    private static bool TryParseInterval(JsonObject interval, out ScheduleInterval parsed, out string error)
    {
        parsed = default;
        error = string.Empty;

        var startText = MoveItRestClient.GetString(interval, "StartTime").Trim();
        var endText = MoveItRestClient.GetString(interval, "EndTime").Trim();
        if (!TryParseTime(startText, out var start) || !TryParseTime(endText, out var end))
        {
            error = $"The schedule interval contains an invalid StartTime/EndTime ('{startText}' / '{endText}').";
            return false;
        }

        var everyMinutes = GetInt(interval, "EveryMinutes");
        if (start != end && everyMinutes <= 0)
        {
            error = "The schedule interval has a time range but EveryMinutes is not greater than zero.";
            return false;
        }

        if (start == end) everyMinutes = 0;
        parsed = new ScheduleInterval(start, end, everyMinutes);
        return true;
    }

    private static IEnumerable<DateTime> ExpandInterval(DateTime startDate, ScheduleInterval interval)
    {
        var first = startDate.Date.Add(interval.Start.ToTimeSpan());
        var lastDate = interval.SpansMidnight ? startDate.Date.AddDays(1) : startDate.Date;
        var last = lastDate.Add(interval.End.ToTimeSpan());

        yield return first;
        if (interval.EveryMinutes <= 0) yield break;

        for (var current = first.AddMinutes(interval.EveryMinutes); current <= last; current = current.AddMinutes(interval.EveryMinutes))
            yield return current;
    }

    private static string DescribeInterval(ScheduleInterval interval, int multi)
        => AutomationScheduleDescriptionFormatter.DescribeInterval(interval.Start, interval.End, interval.EveryMinutes, multi);

    private static bool TryParseTime(string value, out TimeOnly time)
    {
        foreach (var format in new[] { "HH:mm", "H:mm", "HH:mm:ss", "H:mm:ss" })
        {
            if (TimeOnly.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
                return true;
        }

        return TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
    }

    private static bool TryParseDatePattern(string value, out DatePattern pattern)
    {
        pattern = default;
        var parts = (value ?? string.Empty).Trim().Split('-', StringSplitOptions.TrimEntries);
        if (parts.Length != 3) return false;

        static int? ParsePart(string text, bool allowWildcard)
        {
            if (allowWildcard && text == "*") return null;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : int.MinValue;
        }

        var year = ParsePart(parts[0], true);
        var month = ParsePart(parts[1], true);
        var day = ParsePart(parts[2], false);
        if (year == int.MinValue || month == int.MinValue || !day.HasValue || day.Value == int.MinValue) return false;
        if (month.HasValue && (month.Value < 1 || month.Value > 12)) return false;
        if (day.Value < 1 || day.Value > 31) return false;

        pattern = new DatePattern(year, month, day.Value);
        return true;
    }

    private static IReadOnlyList<JsonObject> GetObjectList(JsonNode? node)
    {
        if (node is JsonArray array) return array.OfType<JsonObject>().ToList();
        if (node is JsonObject obj) return new[] { obj };
        return Array.Empty<JsonObject>();
    }

    private static IReadOnlyList<string> GetStringValues(JsonNode? node)
    {
        if (node is JsonArray array)
            return array.Select(item => item?.ToString().Trim() ?? string.Empty).Where(item => item.Length > 0).ToList();

        var value = node?.ToString().Trim() ?? string.Empty;
        return value.Length == 0 ? Array.Empty<string>() : new[] { value };
    }

    private static IReadOnlyList<int> GetIntValues(JsonNode? node)
    {
        var result = new List<int>();
        foreach (var value in GetStringValues(node))
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) result.Add(parsed);
        }
        return result;
    }

    private static int GetInt(JsonObject obj, string key)
    {
        var value = MoveItRestClient.GetString(obj, key).Trim();
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
    }

    private static bool IsOne(JsonObject? obj, string key)
        => obj is not null && MoveItRestClient.GetString(obj, key).Trim() is "1" or "true" or "True";

    private readonly record struct ScheduleInterval(TimeOnly Start, TimeOnly End, int EveryMinutes)
    {
        public bool SpansMidnight => End < Start;
    }

    private readonly record struct DatePattern(int? Year, int? Month, int Day)
    {
        public bool Matches(DateTime date)
            => (!Year.HasValue || Year.Value == date.Year)
               && (!Month.HasValue || Month.Value == date.Month)
               && Day == date.Day;
    }
}
