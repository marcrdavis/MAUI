// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Shared server-side relative-date filter semantics used when scheduled Reports regenerate grid data.
// Keep calendar/operator semantics synchronized with wwwroot/js/grid-date-macros.js.

using System.Globalization;
using System.Text.RegularExpressions;

namespace MoveItAutomationUI.Services.Reporting;

internal readonly record struct GridDateFilterRange(DateOnly Start, DateOnly EndExclusive, bool Negate, string Macro)
{
    public bool Contains(DateOnly value) => value >= Start && value < EndExclusive;
}

internal readonly record struct GridDateFilter(DateTime Start, DateTime EndExclusive, bool IsRange, string Operator, bool Negate)
{
    public bool Matches(DateTime? value)
    {
        var match = value is { } date && (Operator switch
        {
            "<" => date < Start,
            "<=" => IsRange ? date < EndExclusive : date <= Start,
            ">" => IsRange ? date >= EndExclusive : date > Start,
            ">=" => date >= Start,
            _ => IsRange ? date >= Start && date < EndExclusive : date == Start
        });
        return Negate ? !match : match;
    }
}

internal static class GridDateFilterMacros
{
    private static readonly Regex TodayOffsetRegex = new(
        @"^\[today(?<sign>[+-])(?<days>\d+)d\]$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // Require all three calendar fields, as in the browser parser. TryParse alone can
    // infer an omitted day/year and change a previously literal partial-text filter.
    private static readonly Regex CompleteDateRegex = new(
        @"^(?:(?:Mon(?:day)?|Tue(?:sday)?|Wed(?:nesday)?|Thu(?:rsday)?|Fri(?:day)?|Sat(?:urday)?|Sun(?:day)?),?\s+)?(?:\d{4}-\d{1,2}-\d{1,2}|\d{1,2}/\d{1,2}/\d{4}|[\p{L}\p{N}.]+\s+\d{1,2},?\s+\d{4}|\d{1,2}\s+[\p{L}\p{N}.]+\s+\d{4})(?=$|[T\s])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // Resolves one supported macro against MAUI's application/server-local calendar date.
    public static bool TryResolve(string? rawValue, DateOnly today, out GridDateFilterRange range)
    {
        range = default;
        var value = (rawValue ?? string.Empty).Trim().ToLowerInvariant();
        if (value.Length == 0) return false;

        var negate = false;
        if (value.StartsWith("not:", StringComparison.Ordinal))
        {
            negate = true;
            value = value[4..].Trim();
        }
        else if (value.StartsWith("!", StringComparison.Ordinal))
        {
            negate = true;
            value = value[1..].Trim();
        }

        DateOnly start;
        DateOnly endExclusive;
        switch (value)
        {
            case "[today]":
                start = today;
                endExclusive = AddDaysSafe(start, 1);
                break;
            case "[yesterday]":
                start = AddDaysSafe(today, -1);
                endExclusive = today;
                break;
            case "[tomorrow]":
                start = AddDaysSafe(today, 1);
                endExclusive = AddDaysSafe(start, 1);
                break;
            case "[thisweek]":
                start = StartOfWeek(today);
                endExclusive = AddDaysSafe(start, 7);
                break;
            case "[lastweek]":
                endExclusive = StartOfWeek(today);
                start = AddDaysSafe(endExclusive, -7);
                break;
            case "[thismonth]":
                start = new DateOnly(today.Year, today.Month, 1);
                endExclusive = start.AddMonths(1);
                break;
            case "[lastmonth]":
                endExclusive = new DateOnly(today.Year, today.Month, 1);
                start = endExclusive.AddMonths(-1);
                break;
            case "[nextmonth]":
            case "[nextquarter]":
            case "[nextyear]":
                try
                {
                    start = value switch
                    {
                        "[nextmonth]" => new DateOnly(today.Year, today.Month, 1).AddMonths(1),
                        "[nextquarter]" => new DateOnly(today.Year, (today.Month - 1) / 3 * 3 + 1, 1).AddMonths(3),
                        _ => new DateOnly(today.Year, 1, 1).AddYears(1)
                    };
                    endExclusive = start.AddMonths(value == "[nextquarter]" ? 3 : value == "[nextyear]" ? 12 : 1);
                }
                catch (ArgumentOutOfRangeException) { return false; }
                break;
            default:
            {
                var match = TodayOffsetRegex.Match(value);
                if (!match.Success || !int.TryParse(match.Groups["days"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var days) || days > 36500)
                    return false;
                if (match.Groups["sign"].Value == "-") days = -days;
                start = AddDaysSafe(today, days);
                endExclusive = AddDaysSafe(start, 1);
                break;
            }
        }

        range = new GridDateFilterRange(start, endExclusive, negate, value);
        return true;
    }

    // Reads a grid datetime using the raw value first, then the formatted display value. DateTimeOffset
    // values are converted to MAUI's application-local clock, matching the grid's displayed date semantics.
    public static bool TryReadGridDate(string? rawValue, string? displayValue, out DateOnly date)
    {
        if (TryReadGridDateTime(rawValue, displayValue, out var value))
        {
            date = DateOnly.FromDateTime(value);
            return true;
        }
        date = default;
        return false;
    }

    // Date-only targets cover a whole day; timestamp targets compare one wall-clock instant.
    // Explicit offsets are converted to application-local time, as with displayed grid cells.
    public static bool TryParseFilter(string? rawValue, DateOnly today, out GridDateFilter filter)
    {
        filter = default;
        var value = (rawValue ?? string.Empty).Trim();
        var negate = value.StartsWith("not:", StringComparison.OrdinalIgnoreCase) || value.StartsWith('!');
        if (negate) value = value[(value.StartsWith('!') ? 1 : 4)..].Trim();
        var match = Regex.Match(value, @"^(<=|>=|<|>)\s*");
        var operation = match.Success ? match.Groups[1].Value : string.Empty;
        if (match.Success) value = value[match.Length..];
        if (TryResolve(value, today, out var range))
        {
            filter = new GridDateFilter(range.Start.ToDateTime(TimeOnly.MinValue), range.EndExclusive.ToDateTime(TimeOnly.MinValue), true, operation, negate);
            return true;
        }
        if (!TryReadGridDateTime(value, null, out var start)) return false;
        var isRange = !Regex.IsMatch(value, @"[T\s]\d{1,2}:\d{2}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        DateTime end;
        try { end = isRange ? start.Date.AddDays(1) : start; }
        catch (ArgumentOutOfRangeException) { return false; }
        filter = new GridDateFilter(start, end, isRange, operation, negate);
        return true;
    }

    public static bool TryReadGridDateTime(string? rawValue, string? displayValue, out DateTime date)
    {
        foreach (var candidate in new[] { rawValue, displayValue })
        {
            var text = (candidate ?? string.Empty).Trim();
            // Partial dates remain text filters; never infer a year from today's date.
            if (text.Length == 0 || !CompleteDateRegex.IsMatch(text)) continue;
            // .NET's permissive parser accepts 00:30 PM and 13:00 PM. Reject those
            // explicitly so scheduled Reports agree with the browser's 12-hour rules.
            var clock = Regex.Match(text, @"[T\s](?<hour>\d{1,2}):\d{2}(?::\d{2}(?:\.\d{1,7})?)?\s*(?<suffix>.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (clock.Success)
            {
                if (!int.TryParse(clock.Groups["hour"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var hour)) continue;
                var suffix = string.Join(" ", clock.Groups["suffix"].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
                var format = CultureInfo.CurrentCulture.DateTimeFormat;
                var isTwelveHour = new[] { "AM", "PM", format.AMDesignator, format.PMDesignator }
                    .Where(period => period.Length > 0)
                    .Any(period => suffix.StartsWith(string.Join(" ", period.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)), StringComparison.OrdinalIgnoreCase));
                if (isTwelveHour && (hour is < 1 or > 12)) continue;
            }

            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out var offset)
                || DateTimeOffset.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out offset))
            {
                var localTime = offset.LocalDateTime;
                date = new DateTime(localTime.Ticks - localTime.Ticks % TimeSpan.TicksPerMillisecond);
                return true;
            }

            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var local)
                || DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out local))
            {
                date = new DateTime(local.Ticks - local.Ticks % TimeSpan.TicksPerMillisecond);
                return true;
            }
        }

        date = default;
        return false;
    }

    // Date punctuation must survive comma-separated OR lists (Oct 21, 2026).
    public static IReadOnlyList<string> SplitAlternatives(string? rawValue)
    {
        var value = (rawValue ?? string.Empty).Trim();
        if (value.Length == 0) return Array.Empty<string>();
        var parts = new List<string>();
        foreach (var term in Regex.Split(value, @"\s+or\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var start = 0;
            for (var index = 0; index < term.Length; index++)
            {
                if (term[index] != ',') continue;
                var left = term[start..index];
                var right = term[(index + 1)..];
                if (Regex.IsMatch(left, @"[\p{L}.]+\s+\d{1,2}$") && Regex.IsMatch(right, @"^\s*\d{4}\b")) continue;
                if (Regex.IsMatch(left, @"\b(?:Mon(?:day)?|Tue(?:sday)?|Wed(?:nesday)?|Thu(?:rsday)?|Fri(?:day)?|Sat(?:urday)?|Sun(?:day)?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                    && Regex.IsMatch(right, @"^\s*[\p{L}.]+\s+\d")) continue;
                parts.Add(term[start..index].Trim());
                start = index + 1;
            }
            parts.Add(term[start..].Trim());
        }
        return parts.Count > 1 && parts.All(part => part.Length > 0) ? parts : new[] { value };
    }

    private static DateOnly StartOfWeek(DateOnly value)
    {
        // MAUI defines [thisweek]/[lastweek] as Monday-Sunday so saved reports do not vary by
        // Windows culture settings on the web server.
        var daysSinceMonday = ((int)value.DayOfWeek + 6) % 7;
        return AddDaysSafe(value, -daysSinceMonday);
    }

    private static DateOnly AddDaysSafe(DateOnly value, int days)
    {
        try { return value.AddDays(days); }
        catch (ArgumentOutOfRangeException) { return days < 0 ? DateOnly.MinValue : DateOnly.MaxValue; }
    }
}
