using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Backup;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.SystemJobs;

namespace MoveItAutomationUI.Tests.SystemJobs;

public sealed class BackupSchedulingTests
{
    private static readonly DateTimeOffset LastCompletedUtc = new(2026, 11, 17, 12, 13, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(24)]
    public async Task GetDueJobKeysAsync_RunsAdvancedBackupAfterUpgradePauseAndReload(int frequencyHours)
    {
        var backup = new FakeBackupService { LatestCompletedUtc = LastCompletedUtc };
        var options = CreateOptions(frequencyHours);
        var repository = CreateRepository(LastCompletedUtc.AddHours(frequencyHours));
        var service = CreateService(repository, backup, options);

        // The upgrade gate/restart kept scheduling paused beyond the ordinary grace period.
        var resumedUtc = LastCompletedUtc.AddHours(frequencyHours).AddMinutes(5);
        Assert.DoesNotContain(MauiSystemJobKeys.Backup, await service.GetDueJobKeysAsync(resumedUtc));
        var nextUtc = LastCompletedUtc.AddHours(frequencyHours * 2);
        Assert.Equal(nextUtc, repository.Backup.NextRunUtc);

        // Recreate the service, as after a restart, using the persisted advanced time.
        service = CreateService(repository, backup, options);
        Assert.DoesNotContain(MauiSystemJobKeys.Backup, await service.GetDueJobKeysAsync(nextUtc.AddSeconds(-30)));
        Assert.Equal(nextUtc, repository.Backup.NextRunUtc);

        for (var cycle = 0; cycle < 3; cycle++)
        {
            var startedUtc = nextUtc.AddSeconds(30);
            Assert.Contains(MauiSystemJobKeys.Backup, await service.GetDueJobKeysAsync(startedUtc));
            Assert.Equal(nextUtc, repository.Backup.NextRunUtc);

            // Successful I/O supplies the completion marker; retain the real provider's next-run rule.
            backup.LatestCompletedUtc = startedUtc.AddMinutes(2);
            var job = new BackupSystemJob(backup, options);
            var followingUtc = job.GetNextRunAfterExecution(
                MauiSystemJobExecutionOutcome.Successful("fixture"), backup.LatestCompletedUtc.Value);
            Assert.Equal(backup.LatestCompletedUtc.Value.AddHours(frequencyHours), followingUtc);
            await repository.RecordExecutionAsync(MauiSystemJobKeys.Backup, startedUtc,
                MauiSystemJobResults.Successful, "fixture", followingUtc, updateNextRun: true);
            nextUtc = followingUtc!.Value;
            service = CreateService(repository, backup, options);
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(24)]
    public async Task GetDueJobKeysAsync_SkipsAnotherGenuinelyMissedBackupThenRunsTheFollowingInterval(int frequencyHours)
    {
        var backup = new FakeBackupService { LatestCompletedUtc = LastCompletedUtc };
        var repository = CreateRepository(LastCompletedUtc.AddHours(frequencyHours));
        var service = CreateService(repository, backup, CreateOptions(frequencyHours));

        for (var missed = 1; missed <= 2; missed++)
        {
            var lateUtc = LastCompletedUtc.AddHours(frequencyHours * missed).AddMinutes(5);
            Assert.DoesNotContain(MauiSystemJobKeys.Backup, await service.GetDueJobKeysAsync(lateUtc));
            Assert.Equal(LastCompletedUtc.AddHours(frequencyHours * (missed + 1)), repository.Backup.NextRunUtc);
        }

        var followingUtc = LastCompletedUtc.AddHours(frequencyHours * 3);
        Assert.Contains(MauiSystemJobKeys.Backup, await service.GetDueJobKeysAsync(followingUtc.AddSeconds(30)));
        Assert.Equal(followingUtc, repository.Backup.NextRunUtc);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(24)]
    public void GetSchedule_UsesNewManualBackupCompletionInsteadOfOldAdvancedTime(int frequencyHours)
    {
        var originalDue = LastCompletedUtc.AddHours(frequencyHours);
        var repository = CreateRepository(originalDue.AddHours(frequencyHours));
        var completedUtc = originalDue.AddMinutes(10);
        var backup = new FakeBackupService { LatestCompletedUtc = completedUtc };
        var job = new BackupSystemJob(backup, CreateOptions(frequencyHours));

        var schedule = job.GetSchedule(repository.Backup, completedUtc.AddSeconds(30));

        Assert.True(schedule.Enabled);
        Assert.Equal(completedUtc.AddHours(frequencyHours), schedule.NextRunUtc);
    }

    [Theory]
    [InlineData(MauiSystemJobResults.Failed)]
    [InlineData(MauiSystemJobResults.CompletedWithWarnings)]
    public async Task GetDueJobKeysAsync_PreservesAndRunsFailureRetryAfterCompletion(string result)
    {
        var backup = new FakeBackupService { LatestCompletedUtc = LastCompletedUtc };
        var options = CreateOptions(24);
        var repository = CreateRepository(LastCompletedUtc.AddHours(24));
        var completedUtc = LastCompletedUtc.AddHours(24).AddMinutes(10);
        var job = new BackupSystemJob(backup, options);
        var retryUtc = job.GetNextRunAfterExecution(new MauiSystemJobExecutionOutcome(result, "fixture"), completedUtc);
        Assert.Equal(completedUtc.AddMinutes(15), retryUtc);
        await repository.RecordExecutionAsync(MauiSystemJobKeys.Backup, completedUtc.AddMinutes(-10),
            result, "fixture", retryUtc, updateNextRun: true);

        var service = CreateService(repository, backup, options);
        Assert.DoesNotContain(MauiSystemJobKeys.Backup, await service.GetDueJobKeysAsync(retryUtc!.Value.AddSeconds(-30)));
        Assert.Equal(retryUtc, repository.Backup.NextRunUtc);
        Assert.Contains(MauiSystemJobKeys.Backup, await service.GetDueJobKeysAsync(retryUtc.Value.AddSeconds(30)));
    }

    [Fact]
    public async Task GetDueJobKeysAsync_DisabledBackupDoesNotRunOrKeepAnAdvancedTime()
    {
        var backup = new FakeBackupService { LatestCompletedUtc = LastCompletedUtc };
        var options = CreateOptions(24);
        options.CurrentValue.Enabled.Value = false;
        var repository = CreateRepository(LastCompletedUtc.AddHours(48));
        var service = CreateService(repository, backup, options);

        Assert.DoesNotContain(MauiSystemJobKeys.Backup, await service.GetDueJobKeysAsync(LastCompletedUtc.AddHours(49)));
        Assert.False(repository.Backup.Enabled);
        Assert.Null(repository.Backup.NextRunUtc);
    }

    private static IOptionsMonitor<BackupOptions> CreateOptions(int frequencyHours)
    {
        var options = new BackupOptions();
        options.Enabled.Value = true;
        options.FrequencyHours.Value = frequencyHours;
        return new FixedOptions(options);
    }

    private static FakeSystemJobRepository CreateRepository(DateTimeOffset nextRunUtc)
    {
        var repository = new FakeSystemJobRepository();
        repository.States[MauiSystemJobKeys.Backup] = new PersistedSystemJobState(
            MauiSystemJobKeys.Backup, true, string.Empty, nextRunUtc, LastCompletedUtc.AddMinutes(-2),
            MauiSystemJobResults.Successful, "fixture");
        return repository;
    }

    private static SystemJobService CreateService(FakeSystemJobRepository repository,
        FakeBackupService backupService, IOptionsMonitor<BackupOptions> options)
    {
        var backup = new BackupSystemJob(backupService, options);
        var unrelated = new UnusedJobs();
        return new SystemJobService(repository, unrelated, backup, unrelated,
            new IExternallyManagedSystemJob[] { backup }, unrelated, unrelated, NullLogger<SystemJobService>.Instance);
    }

    private sealed class FakeSystemJobRepository : ISystemJobRepository
    {
        public Dictionary<string, PersistedSystemJobState> States { get; } = new(StringComparer.OrdinalIgnoreCase);
        public PersistedSystemJobState Backup => States[MauiSystemJobKeys.Backup];

        public FakeSystemJobRepository()
        {
            foreach (var key in new[] { MauiSystemJobKeys.Reports, MauiSystemJobKeys.Backup,
                         MauiSystemJobKeys.BackupCleanup, MauiSystemJobKeys.JobHistoryCleanup, MauiSystemJobKeys.HostHealthCheck })
                States[key] = new PersistedSystemJobState(key, false, string.Empty, null, null, string.Empty, string.Empty);
        }

        public Task<IReadOnlyDictionary<string, PersistedSystemJobState>> GetAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, PersistedSystemJobState>>(keys.Where(States.ContainsKey)
                .ToDictionary(key => key, key => States[key], StringComparer.OrdinalIgnoreCase));

        public Task SaveConfigurationAsync(string key, bool enabled, string runTime, DateTimeOffset? nextRunUtc, CancellationToken cancellationToken = default)
        {
            States[key] = States[key] with { Enabled = enabled, RunTime = runTime, NextRunUtc = nextRunUtc };
            return Task.CompletedTask;
        }

        public Task RecordExecutionAsync(string key, DateTimeOffset startedUtc, string result, string message,
            DateTimeOffset? nextRunUtc, bool updateNextRun, CancellationToken cancellationToken = default)
        {
            States[key] = States[key] with { LastRunUtc = startedUtc, LastResult = result, LastMessage = message,
                NextRunUtc = updateNextRun ? nextRunUtc : States[key].NextRunUtc };
            return Task.CompletedTask;
        }
    }

    private sealed class FakeBackupService : IMauiBackupService
    {
        public DateTimeOffset? LatestCompletedUtc { get; set; }
        public DateTimeOffset? GetLatestCompletedBackupUtc() => LatestCompletedUtc;
        public Task<MauiBackupResult?> RunAutomaticIfDueAsync(int frequencyHours, string initiatedBy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MauiBackupResult> RunManualAsync(string initiatedBy, MoveItSessionState currentUserSession, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IReadOnlyList<MauiBackupDescriptor> ListBackups() => throw new NotSupportedException();
        public Task<string> CreateDownloadArchiveAsync(string backupId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RemoveBackupAsync(string backupId, string initiatedBy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MauiBackupCleanupResult> CleanupExpiredBackupsAsync(int retentionDays, string initiatedBy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MauiBackupRestoreResult> RestoreBackupAsync(string backupId, IReadOnlyList<string> moveItEnvironments, string initiatedBy, MoveItSessionState? currentUserSession, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedOptions : IOptionsMonitor<BackupOptions>
    {
        public FixedOptions(BackupOptions currentValue) => CurrentValue = currentValue;
        public BackupOptions CurrentValue { get; }
        public BackupOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<BackupOptions, string?> listener) => null;
    }

    private sealed class UnusedJobs : IReportsSystemJob, IBackupCleanupJob, IJobHistoryCleanupJob, IHostHealthCheckJob
    {
        public Task<MauiSystemJobExecutionOutcome> ExecuteAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
