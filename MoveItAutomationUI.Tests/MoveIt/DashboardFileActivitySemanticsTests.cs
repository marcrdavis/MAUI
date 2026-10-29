// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Verifies Dashboard File Activity semantics and incremental paging predicates.

using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class DashboardFileActivitySemanticsTests
{
    [Theory]
    [InlineData("get", true)]
    [InlineData("send", true)]
    [InlineData("append", true)]
    [InlineData("GET", true)]
    [InlineData("Upload", false)]
    [InlineData("Download", false)]
    [InlineData("rename", false)]
    public void NativeDashboardTransferAction_MatchesCapturedMoveItActions(string action, bool expected)
    {
        Assert.Equal(expected, AutomationReportService.IsNativeDashboardTransferAction(action));
    }

    [Theory]
    [InlineData("0", true, false)]
    [InlineData("1", false, true)]
    [InlineData("400", false, true)]
    [InlineData("5000", false, false)]
    [InlineData("5010", false, false)]
    [InlineData("", false, false)]
    [InlineData("not-a-number", false, false)]
    public void NativeDashboardStatusCode_MatchesCapturedMoveItBuckets(string statusCode, bool success, bool failure)
    {
        Assert.Equal(success, AutomationReportService.IsNativeDashboardSuccess(statusCode));
        Assert.Equal(failure, AutomationReportService.IsNativeDashboardFailure(statusCode));
    }

    [Fact]
    public void DashboardPredicate_UsesEstablishedV1WindowAndIncrementalLogIdBounds()
    {
        var windowStart = new DateTimeOffset(2026, 10, 10, 9, 30, 45, TimeSpan.FromHours(-4));

        var predicate = AutomationReportService.BuildFileActivityDashboardPredicate(
            windowStart,
            newerThanLogId: 123456,
            olderThanLogId: "234567");

        Assert.Contains("StatusCode=out=(\"5000\",\"5010\")", predicate, StringComparison.Ordinal);
        Assert.Contains("LogStamp>=\"2026-10-10T09:30:45\"", predicate, StringComparison.Ordinal);
        Assert.Contains("LogID>123456", predicate, StringComparison.Ordinal);
        Assert.Contains("LogID<234567", predicate, StringComparison.Ordinal);
    }
}
