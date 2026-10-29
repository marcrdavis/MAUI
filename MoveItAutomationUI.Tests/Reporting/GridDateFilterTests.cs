using System.Globalization;
using System.Reflection;
using System.Text.Json;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Reporting;

namespace MoveItAutomationUI.Tests.Reporting;

public sealed class GridDateFilterTests
{
    public static IEnumerable<object[]> CapturedFormats()
    {
        using var stream = typeof(GridDateFilterTests).Assembly.GetManifestResourceStream("MoveItAutomationUI.Tests.Reporting.Fixtures.GridDateFilterCases.json")!;
        using var document = JsonDocument.Parse(stream);
        foreach (var item in document.RootElement.GetProperty("cases").EnumerateArray())
            yield return [item.GetProperty("filter").GetString()!, item.GetProperty("raw").GetString()!,
                item.TryGetProperty("display", out var display) ? display.GetString()! : string.Empty,
                item.GetProperty("matches").GetBoolean()];
    }

    [Theory, MemberData(nameof(CapturedFormats))]
    public void ScheduledReportsMatchBrowserDateTimeCases(string expression, string raw, string display, bool expected)
    {
        Assert.True(GridDateFilterMacros.TryParseFilter(expression, new DateOnly(2026, 10, 21), out var filter));
        var hasDate = GridDateFilterMacros.TryReadGridDateTime(raw, display, out var value);
        Assert.Equal(expected, filter.Matches(hasDate ? value : null));
    }

    [Theory]
    [InlineData("2026-12-31", "[nextmonth]", "2027-01-01", "2027-02-01")]
    [InlineData("2026-03-31", "[nextquarter]", "2026-04-01", "2026-07-01")]
    [InlineData("2026-04-01", "[nextquarter]", "2026-07-01", "2026-10-01")]
    [InlineData("2027-01-01", "[nextquarter]", "2027-04-01", "2027-07-01")]
    [InlineData("2027-01-01", "[nextyear]", "2028-01-01", "2029-01-01")]
    [InlineData("2024-01-31", "[nextmonth]", "2024-02-01", "2024-03-01")]
    public void NewMacrosFollowCalendarBoundaries(string today, string macro, string start, string end)
    {
        Assert.True(GridDateFilterMacros.TryResolve(macro, DateOnly.Parse(today, CultureInfo.InvariantCulture), out var range));
        Assert.Equal(DateOnly.Parse(start, CultureInfo.InvariantCulture), range.Start);
        Assert.Equal(DateOnly.Parse(end, CultureInfo.InvariantCulture), range.EndExclusive);
    }

    [Theory]
    [InlineData("2026-02-30")]
    [InlineData("2026-13-01")]
    [InlineData(">not a date")]
    [InlineData("10/21")]
    [InlineData("October 2026")]
    [InlineData("10/2026")]
    [InlineData("2026")]
    [InlineData("!")]
    [InlineData("[nextdecade]")]
    [InlineData("10/21/2026 13:00 PM")]
    [InlineData("10/21/2026 00:30 PM")]
    [InlineData("2026-10-21T25:00:00")]
    [InlineData("2026-10-21T01:60:00")]
    [InlineData("2026-10-21T01:00:00+15:00")]
    public void InvalidOrPartialDatesRemainTextFilters(string expression)
        => Assert.False(GridDateFilterMacros.TryParseFilter(expression, new DateOnly(2026, 10, 21), out _));

    [Theory]
    [InlineData("Oct 21, 2026", "Oct 21, 2026")]
    [InlineData(">=Oct 21, 2026 2:30 PM, <Nov 1, 2026", ">=Oct 21, 2026 2:30 PM|<Nov 1, 2026")]
    [InlineData("Wed, Oct 21, 2026 or [nextmonth]", "Wed, Oct 21, 2026|[nextmonth]")]
    [InlineData("2026-10-21,", "2026-10-21,")]
    [InlineData("[today] or", "[today] or")]
    public void AlternativesPreserveDatePunctuation(string expression, string expected)
        => Assert.Equal(expected.Split('|'), GridDateFilterMacros.SplitAlternatives(expression));

    [Fact]
    public void ExplicitOffsetsUseApplicationLocalTimeAndKeepSubsecondPrecision()
    {
        Assert.True(GridDateFilterMacros.TryReadGridDateTime("2026-10-22T02:00:00.1234567+02:00", null, out var value));
        var expected = new DateTimeOffset(2026, 10, 22, 2, 0, 0, 123, TimeSpan.FromHours(2)).LocalDateTime;
        Assert.Equal(expected, value);
    }

    [Fact]
    public void OutOfRangeFutureMacroDoesNotThrow()
        => Assert.False(GridDateFilterMacros.TryResolve("[nextyear]", DateOnly.MaxValue, out _));

    [Theory]
    [InlineData("Date", "<2026-10-21, 2026-11-10", "0,2")]
    [InlineData("Date", "Oct 21, 2026, Nov 10, 2026", "1,2")]
    [InlineData("Date", "!2026-11-10", "0,1,3")]
    [InlineData("Date", "Oct", "0")]
    [InlineData("Date", "2026", "0,1,2")]
    [InlineData("Count", ">=2, <1", "0,2,3")]
    [InlineData("Name", "!Failed", "1,2")]
    public void ReportRegenerationUsesDateAlternativesWithoutChangingTextOrNumericFilters(string column, string expression, string expected)
    {
        var dates = new[] { "2026-10-20T23:59:59", "2026-10-21T14:30:00", "2026-11-10T12:00:00", "2027-01-05T00:00:00" };
        var display = new[] { "Oct 20, 2026", "10/21/2026", "Nov 10, 2026", "Jan 5, 2027" };
        var names = new[] { "Failed", "Success", "Disabled", "Failed" };
        var rows = dates.Select((value, index) => new ReportGridSnapshotRow
        {
            Id = index.ToString(CultureInfo.InvariantCulture),
            Values = new() { ["Date"] = display[index], ["Name"] = names[index], ["Count"] = index.ToString(CultureInfo.InvariantCulture) },
            RawValues = new() { ["Date"] = value, ["Count"] = index.ToString(CultureInfo.InvariantCulture) }
        }).ToArray();
        var state = new ReportGridViewState
        {
            Columns = [new() { Key = "Date", Label = "Date", Type = "datetime" }, new() { Key = "Name", Label = "Name" }, new() { Key = "Count", Label = "Count", Type = "number" }],
            Filters = new() { [column] = expression },
            FilterTypes = new() { ["Date"] = "datetime", ["Name"] = "text", ["Count"] = "number" }
        };
        var method = typeof(OutboundReportDataSource).GetMethod("ApplyViewState", BindingFlags.Static | BindingFlags.NonPublic)!;
        var report = (OutboundReportData)method.Invoke(null, [state, rows])!;
        Assert.Equal(expected.Split(','), report.Rows.Select(row => row.Id));
    }
}
