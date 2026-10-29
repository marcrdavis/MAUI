using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Reporting;

namespace MoveItAutomationUI.Tests.Reporting;

public sealed class ReportScheduleCalculatorTests
{
    private readonly ReportScheduleCalculator _calculator = new();

    [Fact]
    public void ValidateAndNormalize_Now_RemovesUnusedScheduleDetails()
    {
        var result = _calculator.ValidateAndNormalize(new ReportScheduleDefinition
        {
            DeliveryMode = OutboundReportDeliveryModes.Now,
            TimeOfDay = "12:00",
            TimeZoneId = "UTC"
        }, "UTC");

        Assert.Equal(OutboundReportDeliveryModes.Now, result.DeliveryMode);
        Assert.Equal("08:00", result.TimeOfDay);
        Assert.Equal(string.Empty, result.TimeZoneId);
    }

    [Fact]
    public void ValidateAndNormalize_Weekly_SortsAndDeduplicatesDays()
    {
        var result = _calculator.ValidateAndNormalize(new ReportScheduleDefinition
        {
            DeliveryMode = OutboundReportDeliveryModes.Recurring,
            Frequency = OutboundReportFrequencies.Weekly,
            TimeOfDay = "08:15",
            TimeZoneId = "UTC",
            DaysOfWeek = new List<int> { 5, 1, 1 }
        }, "UTC");

        Assert.Equal(new[] { 1, 5 }, result.DaysOfWeek);
        Assert.Equal("08:15", result.TimeOfDay);
        Assert.Equal("UTC", result.TimeZoneId);
    }

    [Fact]
    public void ValidateAndNormalize_RejectsNonFiveMinuteTime()
    {
        var schedule = new ReportScheduleDefinition
        {
            DeliveryMode = OutboundReportDeliveryModes.Recurring,
            Frequency = OutboundReportFrequencies.Daily,
            TimeOfDay = "08:13",
            TimeZoneId = "UTC"
        };

        Assert.Throws<ArgumentException>(() => _calculator.ValidateAndNormalize(schedule, "UTC"));
    }

    [Fact]
    public void ValidateAndNormalize_RejectsWeeklyScheduleWithoutDay()
    {
        var schedule = new ReportScheduleDefinition
        {
            DeliveryMode = OutboundReportDeliveryModes.Recurring,
            Frequency = OutboundReportFrequencies.Weekly,
            TimeOfDay = "08:15",
            TimeZoneId = "UTC"
        };

        Assert.Throws<ArgumentException>(() => _calculator.ValidateAndNormalize(schedule, "UTC"));
    }

    [Fact]
    public void GetNextRunUtc_Daily_ReturnsNextUtcOccurrence()
    {
        var schedule = new ReportScheduleDefinition
        {
            DeliveryMode = OutboundReportDeliveryModes.Recurring,
            Frequency = OutboundReportFrequencies.Daily,
            TimeOfDay = "08:15",
            TimeZoneId = "UTC"
        };
        var after = new DateTimeOffset(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);

        var next = _calculator.GetNextRunUtc(schedule, after);

        Assert.Equal(new DateTimeOffset(2026, 10, 7, 8, 15, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void GetNextRunUtc_Once_ReturnsNullWhenAllDatesArePast()
    {
        var schedule = new ReportScheduleDefinition
        {
            DeliveryMode = OutboundReportDeliveryModes.Once,
            TimeOfDay = "08:15",
            TimeZoneId = "UTC",
            Dates = new List<string> { "2026-10-06" }
        };

        var next = _calculator.GetNextRunUtc(schedule, new DateTimeOffset(2026, 10, 7, 8, 0, 0, TimeSpan.Zero));

        Assert.Null(next);
    }

    [Theory]
    [InlineData("Eastern Daylight Time", "Eastern Standard Time")]
    [InlineData(" UTC ", "UTC")]
    public void NormalizeTimeZoneId_NormalizesExpectedValues(string input, string expected)
        => Assert.Equal(expected, ReportScheduleCalculator.NormalizeTimeZoneId(input));
}
