using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.SystemJobs;

namespace MoveItAutomationUI.Tests.SystemJobs;

public sealed class SystemJobBehaviorTests
{
    [Theory]
    [InlineData("reports", true)]
    [InlineData("BACKUP-CLEANUP", true)]
    [InlineData(" host-health-check ", true)]
    [InlineData("unknown", false)]
    [InlineData(null, false)]
    public void IsKnownJob_RecognizesConfiguredKeys(string? key, bool expected)
        => Assert.Equal(expected, CreateService(new FakeSystemJobRepository()).IsKnownJob(key));

    [Theory]
    [InlineData("reports", true)]
    [InlineData("backup-cleanup", true)]
    [InlineData("host-health-check", true)]
    [InlineData("backup", false)]
    [InlineData("job-history-cleanup", false)]
    public void IsUserManageableJob_ExcludesHiddenJobs(string key, bool expected)
        => Assert.Equal(expected, CreateService(new FakeSystemJobRepository()).IsUserManageableJob(key));

    [Fact]
    public async Task GetDueJobKeysAsync_SkipsAndReschedulesMissedDailyRun()
    {
        var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var repository = FakeSystemJobRepository.WithAllStates();
        repository.States[MauiSystemJobKeys.BackupCleanup] = new PersistedSystemJobState(
            MauiSystemJobKeys.BackupCleanup, true, "01:00", now.AddMinutes(-5), null, string.Empty, string.Empty);
        var service = CreateService(repository);

        var due = await service.GetDueJobKeysAsync(now);

        Assert.DoesNotContain(MauiSystemJobKeys.BackupCleanup, due);
        SavedConfiguration? saved = null;
        var matchingConfigurationCount = 0;
        foreach (var item in repository.SavedConfigurations)
        {
            if (!string.Equals(item.Key, MauiSystemJobKeys.BackupCleanup, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            saved = item;
            matchingConfigurationCount++;
        }

        Assert.Equal(1, matchingConfigurationCount);
        Assert.NotNull(saved);
        var nextRunUtc = saved!.NextRunUtc;
        Assert.True(nextRunUtc.HasValue);
        Assert.True(nextRunUtc.Value > now);
    }

    [Fact]
    public async Task GetDueJobKeysAsync_AllowsRunWithinSchedulerGracePeriod()
    {
        var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var repository = FakeSystemJobRepository.WithAllStates();
        repository.States[MauiSystemJobKeys.BackupCleanup] = new PersistedSystemJobState(
            MauiSystemJobKeys.BackupCleanup, true, "01:00", now.AddSeconds(-30), null, string.Empty, string.Empty);
        var service = CreateService(repository);

        var due = await service.GetDueJobKeysAsync(now);

        Assert.Contains(MauiSystemJobKeys.BackupCleanup, due);
        Assert.DoesNotContain(repository.SavedConfigurations, item => item.Key == MauiSystemJobKeys.BackupCleanup);
    }

    [Fact]
    public async Task GetDueJobKeysAsync_DoesNotRunOrRescheduleDisabledJob()
    {
        var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var repository = FakeSystemJobRepository.WithAllStates();
        repository.States[MauiSystemJobKeys.BackupCleanup] = new PersistedSystemJobState(
            MauiSystemJobKeys.BackupCleanup, false, "01:00", now.AddHours(-2), null, string.Empty, string.Empty);
        var service = CreateService(repository);

        var due = await service.GetDueJobKeysAsync(now);

        Assert.DoesNotContain(MauiSystemJobKeys.BackupCleanup, due);
        Assert.DoesNotContain(repository.SavedConfigurations, item => item.Key == MauiSystemJobKeys.BackupCleanup);
    }

    private static SystemJobService CreateService(FakeSystemJobRepository repository)
    {
        var backup = new FakeBackupSystemJob();
        return new SystemJobService(
            repository,
            new SuccessfulReportsJob(),
            backup,
            new SuccessfulBackupCleanupJob(),
            new IExternallyManagedSystemJob[] { backup },
            new SuccessfulJobHistoryCleanupJob(),
            new SuccessfulHostHealthCheckJob(),
            NullLogger<SystemJobService>.Instance);
    }

    private sealed class FakeSystemJobRepository : ISystemJobRepository
    {
        public Dictionary<string, PersistedSystemJobState> States { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<SavedConfiguration> SavedConfigurations { get; } = new();

        public static FakeSystemJobRepository WithAllStates()
        {
            var repository = new FakeSystemJobRepository();
            foreach (var key in new[]
                     {
                         MauiSystemJobKeys.Reports,
                         MauiSystemJobKeys.Backup,
                         MauiSystemJobKeys.BackupCleanup,
                         MauiSystemJobKeys.JobHistoryCleanup,
                         MauiSystemJobKeys.HostHealthCheck
                     })
            {
                repository.States[key] = new PersistedSystemJobState(key, false, string.Empty, null, null, string.Empty, string.Empty);
            }
            return repository;
        }

        public Task<IReadOnlyDictionary<string, PersistedSystemJobState>> GetAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<string, PersistedSystemJobState> result = keys
                .Where(States.ContainsKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(key => key, key => States[key], StringComparer.OrdinalIgnoreCase);
            return Task.FromResult(result);
        }

        public Task SaveConfigurationAsync(string key, bool enabled, string runTime, DateTimeOffset? nextRunUtc, CancellationToken cancellationToken = default)
        {
            SavedConfigurations.Add(new SavedConfiguration(key, enabled, runTime, nextRunUtc));
            var previous = States.TryGetValue(key, out var existing)
                ? existing
                : new PersistedSystemJobState(key, enabled, runTime, nextRunUtc, null, string.Empty, string.Empty);
            States[key] = previous with { Enabled = enabled, RunTime = runTime, NextRunUtc = nextRunUtc };
            return Task.CompletedTask;
        }

        public Task RecordExecutionAsync(string key, DateTimeOffset startedUtc, string result, string message, DateTimeOffset? nextRunUtc, bool updateNextRun, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed record SavedConfiguration(string Key, bool Enabled, string RunTime, DateTimeOffset? NextRunUtc);

    private sealed class FakeBackupSystemJob : IBackupSystemJob
    {
        public string Key => MauiSystemJobKeys.Backup;
        public Task<MauiSystemJobExecutionOutcome> ExecuteAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(MauiSystemJobExecutionOutcome.Successful("ok"));
        public ExternallyManagedSystemJobSchedule GetSchedule(PersistedSystemJobState state, DateTimeOffset utcNow)
            => new(state.Enabled, state.NextRunUtc);
        public DateTimeOffset? GetNextRunAfterMissedSchedule(PersistedSystemJobState state, DateTimeOffset utcNow)
            => state.NextRunUtc;
        public DateTimeOffset? GetNextRunAfterExecution(MauiSystemJobExecutionOutcome outcome, DateTimeOffset completedUtc)
            => completedUtc.AddHours(24);
    }

    private sealed class SuccessfulReportsJob : IReportsSystemJob
    {
        public Task<MauiSystemJobExecutionOutcome> ExecuteAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(MauiSystemJobExecutionOutcome.Successful("ok"));
    }

    private sealed class SuccessfulBackupCleanupJob : IBackupCleanupJob
    {
        public Task<MauiSystemJobExecutionOutcome> ExecuteAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(MauiSystemJobExecutionOutcome.Successful("ok"));
    }

    private sealed class SuccessfulJobHistoryCleanupJob : IJobHistoryCleanupJob
    {
        public Task<MauiSystemJobExecutionOutcome> ExecuteAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(MauiSystemJobExecutionOutcome.Successful("ok"));
    }

    private sealed class SuccessfulHostHealthCheckJob : IHostHealthCheckJob
    {
        public Task<MauiSystemJobExecutionOutcome> ExecuteAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(MauiSystemJobExecutionOutcome.Successful("ok"));
    }
}
