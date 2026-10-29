// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Globalization;
using System.Text.Json.Nodes;

namespace MoveItAutomationUI.Services.MoveIt;

// Formats MOVEit Automation schedule definitions for compact grid display. The interval wording is shared
// with Upcoming Runs so the Tasks grid and forecast grid describe the same underlying schedule consistently.
internal static class AutomationScheduleDescriptionFormatter
{
    public static string DescribeTaskSchedules(JsonNode? schedulesNode)
    {
        var schedules = GetObjectList((schedulesNode as JsonObject)?["Schedule"] ?? schedulesNode);
        if (schedules.Count == 0) return string.Empty;

        var descriptions = schedules
            .Select(DescribeSchedule)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        return string.Join("; ", descriptions);
    }

    public static string DescribeInterval(TimeOnly start, TimeOnly end, int everyMinutes, int multi)
    {
        static string Format(TimeOnly time)
            => DateTime.Today.Add(time.ToTimeSpan()).ToString("h:mm tt", CultureInfo.InvariantCulture);

        var spansMidnight = end < start;
        var suffix = multi switch
        {
            1 => " · overnight (before midnight)",
            2 => " · overnight (after midnight)",
            _ when spansMidnight => " · overnight",
            _ => string.Empty
        };

        return everyMinutes <= 0 || start == end
            ? $"At {Format(start)}{suffix}"
            : $"Every {everyMinutes} min, {Format(start)}–{Format(end)}{suffix}";
    }

    private static string DescribeSchedule(JsonObject schedule)
    {
        var multi = GetInt(schedule, "Multi");
        var intervals = GetObjectList((schedule["Frequency"] as JsonObject)?["Interval"] ?? schedule["Frequency"]);
        var intervalDescriptions = new List<string>();

        foreach (var interval in intervals)
        {
            var startText = MoveItRestClient.GetString(interval, "StartTime").Trim();
            var endText = MoveItRestClient.GetString(interval, "EndTime").Trim();
            if (!TryParseTime(startText, out var start) || !TryParseTime(endText, out var end)) continue;

            var everyMinutes = GetInt(interval, "EveryMinutes");
            if (start == end) everyMinutes = 0;
            intervalDescriptions.Add(DescribeInterval(start, end, everyMinutes, multi));
        }

        var criteria = DescribeDateCriteria(schedule);
        var timing = intervalDescriptions.Count > 0
            ? string.Join(", ", intervalDescriptions)
            : intervals.Count > 0 ? "Schedule details unavailable" : string.Empty;
        var description = (criteria, timing) switch
        {
            ({ Length: > 0 }, { Length: > 0 }) => $"{criteria} · {timing}",
            ({ Length: > 0 }, _) => criteria,
            (_, { Length: > 0 }) => timing,
            _ => "Configured schedule"
        };

        if (IsOne(schedule, "OnlyUntilFirstSuccess"))
            description += " · until first success";

        return description;
    }

    private static string DescribeDateCriteria(JsonObject schedule)
    {
        var parts = new List<string>();
        var days = schedule["Days"] as JsonObject;
        var weekdayValues = GetStringValues(days?["DayOfWeek"]);
        var weekdays = weekdayValues
            .Select(ParseDayOfWeek)
            .Where(day => day.HasValue)
            .Select(day => day!.Value)
            .Distinct()
            .ToHashSet();

        if (weekdays.Count > 0)
            parts.Add(DescribeWeekdays(weekdays));

        var monthDays = GetIntValues(days?["DayOfMonth"])
            .Where(day => day is >= 1 and <= 31)
            .Distinct()
            .OrderBy(day => day)
            .ToList();
        if (monthDays.Count > 0)
            parts.Add(monthDays.Count == 1 ? $"Day {monthDays[0]}" : $"Days {string.Join(", ", monthDays)}");

        var dateListRefs = GetObjectList(schedule["DateListRef"]);
        var includeCount = dateListRefs.Count(item => IsOne(item, "Run"));
        var excludeCount = dateListRefs.Count - includeCount;
        if (includeCount > 0) parts.Add(includeCount == 1 ? "Date list" : "Date lists");
        if (excludeCount > 0) parts.Add(excludeCount == 1 ? "excluding date list" : "excluding date lists");

        if (parts.Count == 0) return "One time";
        return string.Join(" / ", parts);
    }

    private static string DescribeWeekdays(IReadOnlySet<DayOfWeek> weekdays)
    {
        if (weekdays.Count == 7) return "Daily";

        var workWeek = new HashSet<DayOfWeek>
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday
        };
        if (weekdays.SetEquals(workWeek)) return "Mon–Fri";

        var weekend = new HashSet<DayOfWeek> { DayOfWeek.Saturday, DayOfWeek.Sunday };
        if (weekdays.SetEquals(weekend)) return "Sat–Sun";

        var order = new[]
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
            DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
        };
        return string.Join(", ", order.Where(weekdays.Contains).Select(day => day.ToString()[..3]));
    }

    private static DayOfWeek? ParseDayOfWeek(string value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0) return null;
        if (Enum.TryParse<DayOfWeek>(text, true, out var parsed)) return parsed;

        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            if (text.Length >= 3 && day.ToString().StartsWith(text, StringComparison.OrdinalIgnoreCase)) return day;
        }

        return null;
    }

    private static bool TryParseTime(string value, out TimeOnly time)
    {
        foreach (var format in new[] { "HH:mm", "H:mm", "HH:mm:ss", "H:mm:ss" })
        {
            if (TimeOnly.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
                return true;
        }

        return TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
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
}
