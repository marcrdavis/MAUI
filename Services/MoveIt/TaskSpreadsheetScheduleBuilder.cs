// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Scheduling normalization/validation adapted from the proven ImportTasksFromCSV project.

using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MoveItAutomationUI.Services.MoveIt;

internal static class TaskSpreadsheetScheduleBuilder
{
    private const string ColDow = "SCHED_DOW";
    private const string ColDom = "SCHED_DOM";
    private const string ColStart = "SCHED_START";
    private const string ColEnd = "SCHED_END";
    private const string ColEvery = "SCHED_EVERY_MIN";
    private const string ColRepeat = "SCHED_REPEAT";
    private const string ColUntilSuccess = "SCHED_ONLY_UNTIL_SUCCESS";
    private const string ColFailIfNoFiles = "SCHED_FAIL_IF_NO_FILES";
    private const string ColPollWithNotif = "SCHED_POLL_WITH_NOTIF";
    private const string ColPollWithNotify = "SCHED_POLL_WITH_NOTIFY";

    private static readonly Dictionary<string, string> DayOfWeekMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SUN"] = "Sunday", ["MON"] = "Monday", ["TUE"] = "Tuesday", ["TUES"] = "Tuesday",
        ["WED"] = "Wednesday", ["WEDS"] = "Wednesday", ["THU"] = "Thursday", ["THUR"] = "Thursday",
        ["THURS"] = "Thursday", ["FRI"] = "Friday", ["SAT"] = "Saturday",
        ["SUNDAY"] = "Sunday", ["MONDAY"] = "Monday", ["TUESDAY"] = "Tuesday", ["WEDNESDAY"] = "Wednesday",
        ["THURSDAY"] = "Thursday", ["FRIDAY"] = "Friday", ["SATURDAY"] = "Saturday"
    };

    internal sealed record BuildResult(JsonObject? Schedules, string Error)
    {
        public bool Success => string.IsNullOrWhiteSpace(Error);
        public static BuildResult Ok(JsonObject? schedules) => new(schedules, string.Empty);
        public static BuildResult Fail(string error) => new(null, error);
    }

    public static BuildResult TryBuild(IReadOnlyDictionary<string, string> row, string? customDateListId = null)
    {
        var anyProvided = new[] { ColDow, ColDom, ColStart, ColEnd, ColEvery, ColRepeat, ColUntilSuccess, ColFailIfNoFiles, ColPollWithNotif, ColPollWithNotify }
            .Any(column => !string.IsNullOrWhiteSpace(Get(row, column)));
        if (!anyProvided) return BuildResult.Ok(null);

        var dowRaw = Get(row, ColDow).Trim();
        var domRaw = Get(row, ColDom).Trim();
        if (dowRaw.Length > 0 && domRaw.Length > 0)
            return BuildResult.Fail("Schedule error: specify only one of SCHED_DOW or SCHED_DOM per row.");

        var repeat = ParseYesNo(Get(row, ColRepeat), false);
        var onlyUntilSuccess = ParseYesNo(Get(row, ColUntilSuccess), false);
        var failIfNoFiles = ParseYesNo(Get(row, ColFailIfNoFiles), false);
        var pollWithNotif = ParseYesNo(FirstNonBlank(Get(row, ColPollWithNotify), Get(row, ColPollWithNotif)), false);

        var intervals = new JsonArray();
        var start = string.IsNullOrWhiteSpace(Get(row, ColStart)) ? "00:00" : NormalizeTime(Get(row, ColStart));
        if (start is null) return BuildResult.Fail("Schedule error: SCHED_START must be a valid time in HH:MM format.");
        var end = string.IsNullOrWhiteSpace(Get(row, ColEnd)) ? "23:59" : NormalizeTime(Get(row, ColEnd));
        if (end is null) return BuildResult.Fail("Schedule error: SCHED_END must be a valid time in HH:MM format.");
        var every = 1;
        var everyRaw = Get(row, ColEvery).Trim();
        if (everyRaw.Length > 0 && (!int.TryParse(everyRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out every) || every <= 0))
            return BuildResult.Fail("Schedule error: SCHED_EVERY_MIN must be a positive integer.");
        if (!repeat) { end = start; every = 1440; }
        intervals.Add(new JsonObject { ["StartTime"] = start, ["EndTime"] = end, ["EveryMinutes"] = every });

        var days = new JsonObject();
        JsonArray? dateListRefs = null;
        if (dowRaw.Length > 0)
        {
            var mapped = ParseDow(dowRaw);
            if (mapped is null) return BuildResult.Fail($"Schedule error: invalid SCHED_DOW value: '{dowRaw}'. Use ALL, full day names, or abbreviations such as MON, TUE/TUES, and THU/THUR.");
            days["DayOfWeek"] = ToArray(mapped);
        }
        else if (domRaw.Length > 0)
        {
            if (IsDayOfWeekOnlyExpression(domRaw))
                return BuildResult.Fail($"Schedule error: SCHED_DOM contains day-of-week values ('{domRaw}'). Move those values to SCHED_DOW.");
            var dom = ParseDom(domRaw);
            if (dom is not null && dom.Count > 0)
                days["DayOfMonth"] = ToArray(dom);
            else
            {
                var customDateListName = GetCustomDateListName(row);
                if (string.IsNullOrWhiteSpace(customDateListName))
                    return BuildResult.Fail($"Schedule error: invalid SCHED_DOM value: '{domRaw}'. Use ALL, numbers, ranges like 1-5, or an existing custom Date List title.");
                if (HasOvernightInterval(intervals))
                    return BuildResult.Fail("Schedule error: SCHED_DOM custom Date List schedules cannot use an overnight interval in SCHED_START/SCHED_END. Split the schedule instead.");
                days["DayOfWeek"] = new JsonArray();
                days["DayOfMonth"] = new JsonArray();
                var reference = new JsonObject { ["Run"] = 1 };
                if (!string.IsNullOrWhiteSpace(customDateListId)) reference["ID"] = customDateListId;
                dateListRefs = new JsonArray(reference);
            }
        }
        else
        {
            days["DayOfWeek"] = ToArray(new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" });
        }

        var schedule = new JsonObject
        {
            ["FailIfNoSuccessInSched"] = failIfNoFiles ? 1 : 0,
            ["OnlyUntilFirstSuccess"] = onlyUntilSuccess ? 1 : 0,
            ["RunEvenIfNotif"] = pollWithNotif ? 1 : 0,
            ["Frequency"] = new JsonObject { ["Interval"] = intervals },
            ["Days"] = days
        };
        if (dateListRefs is not null) schedule["DateListRef"] = dateListRefs;
        return BuildResult.Ok(new JsonObject { ["Schedule"] = new JsonArray(schedule) });
    }

    public static string? GetCustomDateListName(IReadOnlyDictionary<string, string> row)
    {
        var raw = Get(row, ColDom).Trim();
        if (raw.Length == 0 || ParseDom(raw) is not null || IsDayOfWeekOnlyExpression(raw)) return null;
        return Regex.IsMatch(raw, "[A-Za-z]") ? raw : null;
    }

    private static string Get(IReadOnlyDictionary<string, string> row, string key)
        => row.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;
    private static string FirstNonBlank(params string[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    private static bool ParseYesNo(string value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        return value.Trim().Equals("Y", StringComparison.OrdinalIgnoreCase)
            || value.Trim().Equals("YES", StringComparison.OrdinalIgnoreCase)
            || value.Trim().Equals("TRUE", StringComparison.OrdinalIgnoreCase)
            || value.Trim() == "1";
    }
    private static string? NormalizeTime(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var formats = new[] { "H:mm", "HH:mm", "h:mm tt", "hh:mm tt" };
        return DateTime.TryParseExact(raw.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dt)
            ? dt.ToString("HH:mm", CultureInfo.InvariantCulture) : null;
    }
    private static int ToMinutes(string value)
    {
        var parts = value.Split(':');
        return int.Parse(parts[0], CultureInfo.InvariantCulture) * 60 + int.Parse(parts[1], CultureInfo.InvariantCulture);
    }
    private static bool HasOvernightInterval(JsonArray intervals)
    {
        foreach (var node in intervals.OfType<JsonObject>())
        {
            var start = node["StartTime"]?.ToString();
            var end = node["EndTime"]?.ToString();
            if (!string.IsNullOrWhiteSpace(start) && !string.IsNullOrWhiteSpace(end) && ToMinutes(end) < ToMinutes(start)) return true;
        }
        return false;
    }
    private static IReadOnlyList<string>? ParseDow(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.Trim().ToUpperInvariant();
        if (text == "ALL") return new[] { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
        var tokens = text.Split(',', StringSplitOptions.None).Select(value => value.Trim()).ToList();
        if (tokens.Count == 0 || tokens.Any(value => value.Length == 0)) return null;
        var result = new List<string>();
        foreach (var token in tokens)
        {
            if (!DayOfWeekMap.TryGetValue(token, out var mapped)) return null;
            if (!result.Contains(mapped, StringComparer.OrdinalIgnoreCase)) result.Add(mapped);
        }
        return result;
    }
    private static bool IsDayOfWeekOnlyExpression(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var words = Regex.Matches(raw.ToUpperInvariant(), @"[A-Z]+").Cast<Match>().Select(match => match.Value).ToList();
        if (words.Count == 0) return false;
        var foundDay = false;
        foreach (var word in words)
        {
            if (DayOfWeekMap.ContainsKey(word)) { foundDay = true; continue; }
            if (word is "AND" or "OR" or "TO" or "THRU" or "THROUGH") continue;
            return false;
        }
        return foundDay;
    }
    private static List<int>? ParseDom(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.Trim().ToUpperInvariant();
        if (text == "ALL") return Enumerable.Range(1, 31).ToList();
        if (!Regex.IsMatch(text, @"^\d{1,2}(?:\s*-\s*\d{1,2})?(?:\s*,\s*\d{1,2}(?:\s*-\s*\d{1,2})?)*$")) return null;
        var days = new SortedSet<int>();
        foreach (var token in text.Split(',', StringSplitOptions.None).Select(value => value.Trim()))
        {
            if (token.Contains('-'))
            {
                var parts = token.Split('-', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2 || !int.TryParse(parts[0], out var first) || !int.TryParse(parts[1], out var last) || first < 1 || first > 31 || last < first || last > 31) return null;
                for (var rangeDay = first; rangeDay <= last; rangeDay++) days.Add(rangeDay);
            }
            else if (!int.TryParse(token, out var singleDay) || singleDay < 1 || singleDay > 31) return null;
            else days.Add(singleDay);
        }
        return days.ToList();
    }
    private static JsonArray ToArray<T>(IEnumerable<T> values)
    {
        var array = new JsonArray();
        foreach (var value in values) array.Add(JsonValue.Create(value));
        return array;
    }
}
