// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class QuickTransferStatusContractTests
{
    [Fact]
    public void SelectRunRecord_PrefersNominalStartIdentity()
    {
        var started = new DateTimeOffset(2026, 10, 10, 20, 15, 0, TimeSpan.Zero);
        var history = new[]
        {
            Run("1", started.AddMinutes(-1), "2026-10-10T20:14:00Z"),
            Run("2", started, "2026-10-10T20:15:00Z")
        };

        var selected = QuickTransferService.SelectRunRecord(history, " 2026-10-10T20:15:00Z ", started);

        Assert.NotNull(selected);
        Assert.Equal("2", selected?.RunId);
    }

    [Fact]
    public void SelectRunRecord_UsesOnlyHistoryRowWhenNominalStartIsMissing()
    {
        var started = new DateTimeOffset(2026, 10, 10, 20, 15, 0, TimeSpan.Zero);
        var history = new[] { Run("1", started, string.Empty) };

        var selected = QuickTransferService.SelectRunRecord(history, "2026-10-10T20:15:00Z", started);

        Assert.NotNull(selected);
        Assert.Equal("1", selected?.RunId);
    }

    [Fact]
    public void SelectRunRecord_DoesNotUseSingleRowThatClearlyPredatesStart()
    {
        var started = new DateTimeOffset(2026, 10, 10, 20, 15, 0, TimeSpan.Zero);
        var history = new[] { Run("old", started.AddMinutes(-5), string.Empty) };

        var selected = QuickTransferService.SelectRunRecord(history, "2026-10-10T20:15:00Z", started);

        Assert.Null(selected);
    }

    [Fact]
    public void SelectRunRecord_UsesUniqueTimeCorrelatedFallback()
    {
        var started = new DateTimeOffset(2026, 10, 10, 20, 15, 0, TimeSpan.Zero);
        var history = new[]
        {
            Run("older", started.AddHours(-1), string.Empty),
            Run("current", started.AddSeconds(2), string.Empty)
        };

        var selected = QuickTransferService.SelectRunRecord(history, "2026-10-10T20:15:00Z", started);

        Assert.NotNull(selected);
        Assert.Equal("current", selected?.RunId);
    }

    [Fact]
    public void SelectRunRecord_DoesNotGuessWhenMultipleFallbackRowsArePlausible()
    {
        var started = new DateTimeOffset(2026, 10, 10, 20, 15, 0, TimeSpan.Zero);
        var history = new[]
        {
            Run("first", started.AddSeconds(1), string.Empty),
            Run("second", started.AddSeconds(4), string.Empty)
        };

        var selected = QuickTransferService.SelectRunRecord(history, "2026-10-10T20:15:00Z", started);

        Assert.Null(selected);
    }

    [Fact]
    public void SelectRunRecord_DoesNotUseConflictingNominalStartAsFallback()
    {
        var started = new DateTimeOffset(2026, 10, 10, 20, 15, 0, TimeSpan.Zero);
        var history = new[] { Run("other", started.AddSeconds(2), "2026-10-10T20:16:00Z") };

        var selected = QuickTransferService.SelectRunRecord(history, "2026-10-10T20:15:00Z", started);

        Assert.Null(selected);
    }

    [Fact]
    public void SelectRunRecord_DoesNotUseLateSingleFallbackRow()
    {
        var started = new DateTimeOffset(2026, 10, 10, 20, 15, 0, TimeSpan.Zero);
        var history = new[] { Run("late", started.AddMinutes(20), string.Empty) };

        var selected = QuickTransferService.SelectRunRecord(history, "2026-10-10T20:15:00Z", started);

        Assert.Null(selected);
    }

    [Fact]
    public void SelectRunRecord_DoesNotUseIdentitylessRowWithoutStartTime()
    {
        var started = new DateTimeOffset(2026, 10, 10, 20, 15, 0, TimeSpan.Zero);
        var history = new[]
        {
            new TaskRunReportRecord(
                "run-unknown", "123", "ADHOC_TEST", null, null, null,
                "Success", string.Empty, 1, 1024d, "tester", "unknown", null, string.Empty,
                "1", "0", null, "end", string.Empty)
        };

        var selected = QuickTransferService.SelectRunRecord(history, "2026-10-10T20:15:00Z", started);

        Assert.Null(selected);
    }

    [Fact]
    public void NominalStartsEquivalent_AcceptsEquivalentTimestampFormatting()
    {
        var equivalent = QuickTransferService.NominalStartsEquivalent(
            "2026-10-10T20:15:00+00:00",
            "2026-10-10 20:15:00Z");

        Assert.True(equivalent);
    }

    [Fact]
    public void IsSelectableQuickTransferPgpKey_RejectsExpiredPgpKey()
    {
        var item = new AutomationKeyCertificate
        {
            Kind = KeyCertificateKind.Pgp,
            Id = "123",
            ExpirationDate = "2000-01-01"
        };

        Assert.False(QuickTransferService.IsSelectableQuickTransferPgpKey(item));
    }

    [Fact]
    public void IsSelectableQuickTransferPgpKey_AcceptsUnexpiredPgpKey()
    {
        var item = new AutomationKeyCertificate
        {
            Kind = KeyCertificateKind.Pgp,
            Id = "123",
            ExpirationDate = "2099-12-31"
        };

        Assert.True(QuickTransferService.IsSelectableQuickTransferPgpKey(item));
    }

    [Fact]
    public void SelectRunningRun_PrefersExactNominalStart()
    {
        var started = new DateTimeOffset(2026, 10, 10, 20, 15, 0, TimeSpan.Zero);
        var running = new[]
        {
            Running("123", "2026-10-10T20:15:00Z", "2026-10-10T20:15:00Z"),
            Running("123", "2026-10-10T20:16:00Z", "2026-10-10T20:16:00Z")
        };

        var selected = QuickTransferService.SelectRunningRun(running, "123", "2026-10-10T20:16:00+00:00", started);

        Assert.NotNull(selected);
        Assert.Equal("2026-10-10T20:16:00Z", selected?.NominalStart);
    }

    [Fact]
    public void SelectRunningRun_UsesOnlyUnambiguousCurrentRun()
    {
        var started = new DateTimeOffset(2026, 10, 10, 20, 15, 0, TimeSpan.Zero);
        var running = new[]
        {
            Running("123", "2026-10-10T20:15:02Z", "2026-10-10T20:15:02Z")
        };

        var selected = QuickTransferService.SelectRunningRun(running, "123", string.Empty, started);

        Assert.NotNull(selected);
    }

    [Fact]
    public void SelectRunningRun_DoesNotGuessAcrossMultipleCurrentRuns()
    {
        var started = new DateTimeOffset(2026, 10, 10, 20, 15, 0, TimeSpan.Zero);
        var running = new[]
        {
            Running("123", "2026-10-10T20:15:01Z", "2026-10-10T20:15:01Z"),
            Running("123", "2026-10-10T20:15:03Z", "2026-10-10T20:15:03Z")
        };

        var selected = QuickTransferService.SelectRunningRun(running, "123", string.Empty, started);

        Assert.Null(selected);
    }

    [Fact]
    public void SelectRunningRun_DoesNotSubstituteDifferentKnownNominalStart()
    {
        var started = new DateTimeOffset(2026, 10, 10, 20, 15, 0, TimeSpan.Zero);
        var running = new[]
        {
            Running("123", "2026-10-10T20:16:00Z", "2026-10-10T20:16:00Z")
        };

        var selected = QuickTransferService.SelectRunningRun(running, "123", "2026-10-10T20:15:00Z", started);

        Assert.Null(selected);
    }

    [Fact]
    public void SelectRunningRun_UsesUniqueIdentitylessCandidateWithinWindow()
    {
        var started = new DateTimeOffset(2026, 10, 10, 20, 15, 0, TimeSpan.Zero);
        var running = new[]
        {
            Running("123", "2026-10-10T20:15:02Z", string.Empty)
        };

        var selected = QuickTransferService.SelectRunningRun(running, "123", "2026-10-10T20:15:00Z", started);

        Assert.NotNull(selected);
    }

    [Fact]
    public void SelectRunningRun_DoesNotUseLateSingleRunWithoutIdentity()
    {
        var started = new DateTimeOffset(2026, 10, 10, 20, 15, 0, TimeSpan.Zero);
        var running = new[]
        {
            Running("123", "2026-10-10T20:35:00Z", string.Empty)
        };

        var selected = QuickTransferService.SelectRunningRun(running, "123", string.Empty, started);

        Assert.Null(selected);
    }

    private static TaskRunReportRecord Run(string runId, DateTimeOffset startTime, string nominalStart)
        => new(
            $"run-{runId}", "123", "ADHOC_TEST", startTime, startTime.AddSeconds(5), 5,
            "Success", string.Empty, 1, 1024d, "tester", runId, startTime, nominalStart,
            "1", "0", startTime.AddSeconds(5), "end", string.Empty);

    private static MoveItRunningTask Running(string taskId, string timeStarted, string nominalStart)
        => new(taskId, "ADHOC_TEST", "tester", "Running", timeStarted, nominalStart, 512, 1024, string.Empty, string.Empty);
}
