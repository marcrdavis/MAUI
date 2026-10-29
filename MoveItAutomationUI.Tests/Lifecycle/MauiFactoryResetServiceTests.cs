// Project: MAUI - MOVEit Automation UI
// Tests the real appsettings writer, reset marker, write gate, and rollback flow.

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Services.Authentication;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.Lifecycle;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.Lifecycle;

public sealed class MauiFactoryResetServiceTests
{
    [Fact]
    public async Task ValidConfirmation_CommitsEmptyConfigAndMarker_ThenSchedulesShutdown()
    {
        using var site = new TestInstallation();
        var restart = new FakeRestartService { CanSchedule = true };
        var sessions = new FakeSessionService();
        var state = new MauiFactoryResetState();
        var reset = site.CreateService(restart, sessions, state, out var gate);

        await reset.ResetAsync(new DefaultHttpContext(), "RESET", CancellationToken.None);

        Assert.True(MauiFactoryResetRecovery.IsEmptySettingsDocument(site.Settings));
        Assert.Equal(MauiFactoryResetRecovery.MarkerContents, File.ReadAllText(site.Marker));
        Assert.True(File.Exists(site.DbFile)); // Deletion must NOT race active DB connections.
        Assert.True(state.IsResetting);
        Assert.True(restart.WasScheduled);
        Assert.True(sessions.WasSignedOut);
        Assert.False(gate.Gate.Wait(0)); // A settings writer cannot race the committed reset.

        MauiFactoryResetRecovery.CompletePendingReset(site.Root);
        Assert.True(Directory.Exists(site.Data));
        Assert.Empty(Directory.EnumerateFileSystemEntries(site.Data));
    }

    [Fact]
    public async Task RestartSchedulingFailure_RestoresOriginalSettingsAndReleasesGate()
    {
        using var site = new TestInstallation();
        var restart = new FakeRestartService { CanSchedule = false };
        var state = new MauiFactoryResetState();
        var reset = site.CreateService(restart, new FakeSessionService(), state, out var gate);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            reset.ResetAsync(new DefaultHttpContext(), "RESET", CancellationToken.None));

        Assert.False(MauiFactoryResetRecovery.IsEmptySettingsDocument(site.Settings));
        Assert.False(File.Exists(site.Marker));
        Assert.True(File.Exists(site.DbFile));
        Assert.False(state.IsResetting);
        Assert.True(gate.Gate.Wait(0));
        gate.Gate.Release();
    }

    [Fact]
    public async Task AnotherRestartAlreadyScheduled_LeavesConfigurationAndMarkerUntouched()
    {
        using var site = new TestInstallation();
        var restart = new FakeRestartService { CanSchedule = true };
        Assert.True(restart.ScheduleRestart("existing lifecycle operation"));
        var state = new MauiFactoryResetState();
        var sessions = new FakeSessionService();
        var reset = site.CreateService(restart, sessions, state, out var gate);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            reset.ResetAsync(new DefaultHttpContext(), "RESET", CancellationToken.None));

        Assert.True(File.Exists(site.DbFile));
        Assert.False(File.Exists(site.Marker));
        Assert.False(MauiFactoryResetRecovery.IsEmptySettingsDocument(site.Settings));
        Assert.False(state.IsResetting);
        Assert.False(sessions.WasSignedOut);
        Assert.True(gate.Gate.Wait(0));
        gate.Gate.Release();
    }

    [Theory]
    [InlineData("reset")]
    [InlineData("RESET ")]
    [InlineData("")]
    public async Task InvalidConfirmation_DoesNotTouchConfiguration(string invalid)
    {
        using var site = new TestInstallation();
        var reset = site.CreateService(new FakeRestartService(), new FakeSessionService(),
            new MauiFactoryResetState(), out _);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            reset.ResetAsync(new DefaultHttpContext(), invalid, CancellationToken.None));

        Assert.False(File.Exists(site.Marker));
        Assert.True(File.Exists(site.DbFile));
        Assert.False(MauiFactoryResetRecovery.IsEmptySettingsDocument(site.Settings));
    }

    private sealed class TestInstallation : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"maui-reset-service-{Guid.NewGuid():N}");
        public string Data => Path.Combine(Root, "Data");
        public string Settings => Path.Combine(Root, "appsettings.json");
        public string Marker => MauiFactoryResetRecovery.MarkerPath(Root);
        public string DbFile => Path.Combine(Data, "maui.db");

        public TestInstallation()
        {
            Directory.CreateDirectory(Data);
            File.WriteAllText(Settings, "{\"MoveIt\":{\"Servers\":[]}}");
            File.WriteAllText(DbFile, "test-db");
        }

        public MauiFactoryResetService CreateService(
            FakeRestartService restart, FakeSessionService sessions, MauiFactoryResetState state,
            out AppSettingsWriteCoordinator gate)
        {
            var env = new FakeWebHostEnvironment { ContentRootPath = Root };
            var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
            var store = new AppSettingsDocumentStore(env, config);
            gate = new AppSettingsWriteCoordinator();
            return new MauiFactoryResetService(env, store, gate, state, restart, sessions,
                NullLogger<MauiFactoryResetService>.Instance);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class FakeRestartService : IApplicationRestartService
    {
        public bool CanSchedule { get; set; }
        public bool WasScheduled { get; private set; }
        public bool IsRestartScheduled => WasScheduled;
        public bool ScheduleRestart(string reason)
        {
            if (!CanSchedule) return false;
            WasScheduled = true;
            return true;
        }
    }

    private sealed class FakeSessionService : IMauiApplicationSessionService
    {
        public bool WasSignedOut { get; private set; }
        public bool HasLiveMoveItSession => true;
        public Task SignInAsync(HttpContext context, MoveItSessionState state, string role,
            string? upgradeTargetVersion = null) => Task.CompletedTask;
        public bool RecordUserActivity() => true;
        public Task ResetForLoginAsync(HttpContext context, bool clearAntiforgeryCookie = false) => Task.CompletedTask;
        public Task SignOutAsync(HttpContext context)
        {
            WasSignedOut = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "MoveItAutomationUI.Tests";
        public string EnvironmentName { get; set; } = "Production";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
