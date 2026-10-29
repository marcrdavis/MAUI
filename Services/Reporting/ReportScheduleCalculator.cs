// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Calculates one-time and recurring outbound-report execution times in the MOVEit server time zone.

using System.Globalization;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.Reporting;

public interface IReportScheduleCalculator
{
    ReportScheduleDefinition ValidateAndNormalize(ReportScheduleDefinition schedule, string serverTimeZoneName);
    DateTimeOffset? GetNextRunUtc(ReportScheduleDefinition schedule, DateTimeOffset afterUtc);
    string Describe(ReportScheduleDefinition schedule);
}

public sealed class ReportScheduleCalculator : IReportScheduleCalculator
{
    public ReportScheduleDefinition ValidateAndNormalize(ReportScheduleDefinition schedule, string serverTimeZoneName)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var mode = NormalizeMode(schedule.DeliveryMode);
        if (mode == OutboundReportDeliveryModes.Now)
            return new ReportScheduleDefinition { DeliveryMode = OutboundReportDeliveryModes.Now };

        var time = ParseTime(schedule.TimeOfDay);
        var timeZoneId = NormalizeTimeZoneId(string.IsNullOrWhiteSpace(schedule.TimeZoneId) ? serverTimeZoneName : schedule.TimeZoneId);
        _ = ResolveTimeZone(timeZoneId);

        var normalized = new ReportScheduleDefinition
        {
            DeliveryMode = mode,
            TimeOfDay = time.ToString("hh\\:mm", CultureInfo.InvariantCulture),
            TimeZoneId = timeZoneId
        };

        if (mode == OutboundReportDeliveryModes.Once)
        {
            normalized.Dates = (schedule.Dates ?? new List<string>())
                .Select(ParseDate)
                .Distinct()
                .OrderBy(value => value)
                .Select(value => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .ToList();
            if (normalized.Dates.Count == 0) throw new ArgumentException("Select at least one delivery date.");
            return normalized;
        }

        var frequency = NormalizeFrequency(schedule.Frequency);
        normalized.Frequency = frequency;
        if (frequency == OutboundReportFrequencies.Weekly)
        {
            normalized.DaysOfWeek = (schedule.DaysOfWeek ?? new List<int>()).Distinct().OrderBy(value => value).ToList();
            if (normalized.DaysOfWeek.Count == 0 || normalized.DaysOfWeek.Any(value => value is < 0 or > 6))
                throw new ArgumentException("Select at least one valid weekday for a weekly report.");
        }
        else if (frequency == OutboundReportFrequencies.Monthly)
        {
            normalized.DaysOfMonth = (schedule.DaysOfMonth ?? new List<int>()).Distinct().OrderBy(value => value).ToList();
            if (normalized.DaysOfMonth.Count == 0 || normalized.DaysOfMonth.Any(value => value is < 1 or > 31))
                throw new ArgumentException("Select at least one valid day of the month for a monthly report.");
        }
        return normalized;
    }

    public DateTimeOffset? GetNextRunUtc(ReportScheduleDefinition schedule, DateTimeOffset afterUtc)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        if (schedule.DeliveryMode == OutboundReportDeliveryModes.Now) return null;
        var zone = ResolveTimeZone(schedule.TimeZoneId);
        var time = ParseTime(schedule.TimeOfDay);
        var localAfter = TimeZoneInfo.ConvertTime(afterUtc, zone).DateTime;
        var startDate = localAfter.Date.AddDays(-1);

        if (schedule.DeliveryMode == OutboundReportDeliveryModes.Once)
        {
            foreach (var date in schedule.Dates.Select(ParseDate).OrderBy(value => value))
            {
                var candidate = ResolveLocal(zone, date.ToDateTime(TimeOnly.FromTimeSpan(time)));
                if (candidate.HasValue && candidate.Value > afterUtc) return candidate;
            }
            return null;
        }

        for (var offset = 0; offset <= 800; offset++)
        {
            var date = DateOnly.FromDateTime(startDate.AddDays(offset));
            if (!MatchesRecurringDate(schedule, date)) continue;
            var candidate = ResolveLocal(zone, date.ToDateTime(TimeOnly.FromTimeSpan(time)));
            if (candidate.HasValue && candidate.Value > afterUtc) return candidate;
        }
        return null;
    }

    public string Describe(ReportScheduleDefinition schedule)
    {
        if (schedule.DeliveryMode == OutboundReportDeliveryModes.Now) return "Send now";
        var time = DateTime.Today.Add(ParseTime(schedule.TimeOfDay)).ToString("h:mm tt", CultureInfo.CurrentCulture);
        if (schedule.DeliveryMode == OutboundReportDeliveryModes.Once)
        {
            var dates = schedule.Dates.Select(ParseDate).OrderBy(value => value).Select(value => value.ToString("MMM d, yyyy", CultureInfo.CurrentCulture));
            return $"Once: {string.Join(", ", dates)} at {time}";
        }
        if (schedule.Frequency == OutboundReportFrequencies.Daily) return $"Daily at {time}";
        if (schedule.Frequency == OutboundReportFrequencies.Weekly)
        {
            var days = schedule.DaysOfWeek.Select(value => CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)value));
            return $"Weekly ({string.Join(", ", days)}) at {time}";
        }
        var monthDays = schedule.DaysOfMonth.Select(value => value.ToString(CultureInfo.InvariantCulture));
        return $"Monthly ({string.Join(", ", monthDays)}) at {time}";
    }

    private static bool MatchesRecurringDate(ReportScheduleDefinition schedule, DateOnly date)
    {
        if (schedule.Frequency == OutboundReportFrequencies.Daily) return true;
        if (schedule.Frequency == OutboundReportFrequencies.Weekly) return schedule.DaysOfWeek.Contains((int)date.DayOfWeek);
        return schedule.DaysOfMonth.Contains(date.Day);
    }

    private static DateTimeOffset? ResolveLocal(TimeZoneInfo zone, DateTime local)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local)) return null;
        if (zone.IsAmbiguousTime(local))
        {
            return zone.GetAmbiguousTimeOffsets(local)
                .Select(offset => new DateTimeOffset(local, offset))
                .OrderBy(value => value.UtcDateTime)
                .First()
                .ToUniversalTime();
        }
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }

    private static string NormalizeMode(string value)
    {
        if (string.Equals(value, OutboundReportDeliveryModes.Now, StringComparison.OrdinalIgnoreCase)) return OutboundReportDeliveryModes.Now;
        if (string.Equals(value, OutboundReportDeliveryModes.Once, StringComparison.OrdinalIgnoreCase)) return OutboundReportDeliveryModes.Once;
        if (string.Equals(value, OutboundReportDeliveryModes.Recurring, StringComparison.OrdinalIgnoreCase)) return OutboundReportDeliveryModes.Recurring;
        throw new ArgumentException("Report delivery must be Now, Once, or Recurring.");
    }

    private static string NormalizeFrequency(string value)
    {
        if (string.Equals(value, OutboundReportFrequencies.Daily, StringComparison.OrdinalIgnoreCase)) return OutboundReportFrequencies.Daily;
        if (string.Equals(value, OutboundReportFrequencies.Weekly, StringComparison.OrdinalIgnoreCase)) return OutboundReportFrequencies.Weekly;
        if (string.Equals(value, OutboundReportFrequencies.Monthly, StringComparison.OrdinalIgnoreCase)) return OutboundReportFrequencies.Monthly;
        throw new ArgumentException("Recurring report frequency must be Daily, Weekly, or Monthly.");
    }

    private static TimeSpan ParseTime(string value)
    {
        if (!TimeSpan.TryParseExact((value ?? string.Empty).Trim(), new[] { "h\\:mm", "hh\\:mm" }, CultureInfo.InvariantCulture, out var result)
            || result < TimeSpan.Zero || result >= TimeSpan.FromDays(1))
            throw new ArgumentException("Select a valid report delivery time.");
        if (result.Minutes % 5 != 0)
            throw new ArgumentException("Report delivery times must use 5-minute increments (00, 05, 10, ... 55).");
        return result;
    }

    private static DateOnly ParseDate(string value)
    {
        if (!DateOnly.TryParseExact((value ?? string.Empty).Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
            throw new ArgumentException($"Report date '{value}' is invalid.");
        return result;
    }

    public static string NormalizeTimeZoneId(string value)
    {
        var result = (value ?? string.Empty).Trim();
        const string daylightSuffix = " Daylight Time";
        if (result.EndsWith(daylightSuffix, StringComparison.OrdinalIgnoreCase))
            result = result[..^daylightSuffix.Length] + " Standard Time";
        if (result.Length == 0) throw new ArgumentException("MOVEit server time-zone information is unavailable.");
        return result;
    }

    private static TimeZoneInfo ResolveTimeZone(string value)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(value); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new ArgumentException($"Time zone '{value}' is not available on this MAUI server.", ex);
        }
    }
}
