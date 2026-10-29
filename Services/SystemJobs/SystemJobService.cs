// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Defines MAUI-authored System Jobs and coordinates their persisted schedule/runtime state.

using System.Globalization;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.SystemJobs;

public interface ISystemJobService
{
    Task<IReadOnlyList<MauiSystemJobState>> GetJobsAsync(Func<string, bool>? isRunning = null, CancellationToken cancellationToken = default);
    Task<MauiSystemJobState> UpdateAsync(MauiSystemJobUpdateRequest request, Func<string, bool>? isRunning = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetDueJobKeysAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default);
    Task<MauiSystemJobExecutionOutcome> ExecuteAsync(string key, bool scheduledExecution, CancellationToken cancellationToken = default);
    bool IsKnownJob(string? key);
    bool IsUserManageableJob(string? key);
}

public sealed class SystemJobService : ISystemJobService
{
    private static readonly SemaphoreSlim InitializationGate = new(1, 1);
    // The scheduler polls every 30 seconds. Allow one full extra poll before classifying a
    // persisted due time as missed, so normal scheduler jitter is not mistaken for downtime.
    private static readonly TimeSpan ScheduledRunGracePeriod = TimeSpan.FromMinutes(1);
    private static readonly IReadOnlyList<MauiSystemJobDefinition> Definitions = new[]
    {
        new MauiSystemJobDefinition(
            MauiSystemJobKeys.Reports,
            "Reports",
            "Runs scheduled Reports when they are due. Report execution history remains available in Reports.",
            MauiSystemJobScheduleKinds.Interval,
            DefaultEnabled: true,
            DefaultRunTime: string.Empty,
            IntervalMinutes: 5),
        new MauiSystemJobDefinition(
            MauiSystemJobKeys.Backup,
            "Backup",
            "Runs automatic MAUI/MOVEit backups using Configuration > Settings > Backup.",
            MauiSystemJobScheduleKinds.ExternallyManaged,
            DefaultEnabled: false,
            DefaultRunTime: string.Empty,
            IntervalMinutes: 0,
            VisibleInScheduler: false),
        new MauiSystemJobDefinition(
            MauiSystemJobKeys.BackupCleanup,
            "Backup Cleanup",
            "Removes MAUI backup folders older than the Backup Retention setting in Configuration > Settings > Backup.",
            MauiSystemJobScheduleKinds.Daily,
            DefaultEnabled: true,
            DefaultRunTime: "01:00",
            IntervalMinutes: 0),
        new MauiSystemJobDefinition(
            MauiSystemJobKeys.JobHistoryCleanup,
            "Job History Cleanup",
            "Removes MAUI scheduled-job history older than the Job History Retention setting in Configuration > Settings > General.",
            MauiSystemJobScheduleKinds.Daily,
            DefaultEnabled: true,
            DefaultRunTime: "02:00",
            IntervalMinutes: 0,
            VisibleInScheduler: false),
        new MauiSystemJobDefinition(
            MauiSystemJobKeys.HostHealthCheck,
            "Host Health Check",
            "Checks all MOVEit Hosts. Supported Host types are tested; unsupported types are recorded as Unknown.",
            MauiSystemJobScheduleKinds.Daily,
            DefaultEnabled: false,
            DefaultRunTime: "06:00",
            IntervalMinutes: 0)
    };

    private readonly ISystemJobRepository _repository;
    private readonly IReportsSystemJob _reportsJob;
    private readonly IBackupSystemJob _backupJob;
    private readonly IBackupCleanupJob _backupCleanupJob;
    private readonly IReadOnlyDictionary<string, IExternallyManagedSystemJob> _externallyManagedJobs;
    private readonly IJobHistoryCleanupJob _jobHistoryCleanupJob;
    private readonly IHostHealthCheckJob _hostHealthCheck;
    private readonly ILogger<SystemJobService> _logger;

    public SystemJobService(
        ISystemJobRepository repository,
        IReportsSystemJob reportsJob,
        IBackupSystemJob backupJob,
        IBackupCleanupJob backupCleanupJob,
        IEnumerable<IExternallyManagedSystemJob> externallyManagedJobs,
        IJobHistoryCleanupJob jobHistoryCleanupJob,
        IHostHealthCheckJob hostHealthCheck,
        ILogger<SystemJobService> logger)
    {
        _repository = repository;
        _reportsJob = reportsJob;
        _backupJob = backupJob;
        _backupCleanupJob = backupCleanupJob;
        _externallyManagedJobs = externallyManagedJobs.ToDictionary(job => job.Key, StringComparer.OrdinalIgnoreCase);
        _jobHistoryCleanupJob = jobHistoryCleanupJob;
        _hostHealthCheck = hostHealthCheck;
        _logger = logger;
    }

    public bool IsKnownJob(string? key)
        => Definitions.Any(definition => definition.Key.Equals((key ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));

    public bool IsUserManageableJob(string? key)
        => Definitions.Any(definition => definition.VisibleInScheduler
                                         && definition.Key.Equals((key ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));

    public async Task<IReadOnlyList<MauiSystemJobState>> GetJobsAsync(Func<string, bool>? isRunning = null, CancellationToken cancellationToken = default)
    {
        var stored = await LoadStatesAsync(cancellationToken: cancellationToken);
        return Definitions
            .Where(definition => definition.VisibleInScheduler)
            .Select(definition => BuildState(definition, stored[definition.Key], isRunning?.Invoke(definition.Key) == true))
            .ToArray();
    }

    public async Task<MauiSystemJobState> UpdateAsync(MauiSystemJobUpdateRequest request, Func<string, bool>? isRunning = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var definition = GetDefinition(request.Key);
        if (!definition.VisibleInScheduler)
            throw new ArgumentException($"System Job '{definition.Name}' is not editable from Scheduler.", nameof(request));

        var runTime = definition.IsDaily ? NormalizeRunTime(request.RunTime) : string.Empty;
        DateTimeOffset? nextRunUtc = request.Enabled ? CalculateNextRunUtc(definition, runTime, DateTimeOffset.UtcNow) : null;
        await _repository.SaveConfigurationAsync(definition.Key, request.Enabled, runTime, nextRunUtc, cancellationToken);
        var stored = (await _repository.GetAsync(new[] { definition.Key }, cancellationToken))[definition.Key];
        return BuildState(definition, stored, isRunning?.Invoke(definition.Key) == true);
    }

    public async Task<IReadOnlyList<string>> GetDueJobKeysAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {
        var stored = await LoadStatesAsync(utcNow, cancellationToken);
        var due = new List<string>();
        var missedBeforeUtc = utcNow.Subtract(ScheduledRunGracePeriod);

        foreach (var definition in Definitions)
        {
            if (!stored.TryGetValue(definition.Key, out var state)
                || !state.Enabled
                || !state.NextRunUtc.HasValue
                || state.NextRunUtc.Value > utcNow)
            {
                continue;
            }

            if (state.NextRunUtc.Value < missedBeforeUtc)
            {
                await RescheduleMissedJobAsync(definition, state, utcNow, cancellationToken);
                continue;
            }

            due.Add(definition.Key);
        }

        return due;
    }

    private async Task RescheduleMissedJobAsync(
        MauiSystemJobDefinition definition,
        PersistedSystemJobState state,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        var scheduledRunUtc = state.NextRunUtc;
        if (!scheduledRunUtc.HasValue)
        {
            _logger.LogWarning(
                "MAUI System Job {SystemJobKey} ({SystemJobName}) was asked to reschedule a missed run without a scheduled NextRunUtc; no reschedule was performed.",
                definition.Key,
                definition.Name);
            return;
        }

        var missedRunUtc = scheduledRunUtc.Value;
        var runTime = definition.IsDaily
            ? NormalizeRunTime(string.IsNullOrWhiteSpace(state.RunTime) ? definition.DefaultRunTime : state.RunTime)
            : string.Empty;

        var nextRunUtc = definition.IsExternallyManaged
            ? GetExternallyManagedJob(definition).GetNextRunAfterMissedSchedule(state, utcNow)
            : CalculateNextRunUtc(definition, runTime, utcNow);

        await _repository.SaveConfigurationAsync(
            definition.Key,
            state.Enabled,
            runTime,
            nextRunUtc,
            cancellationToken);

        _logger.LogInformation(
            "MAUI System Job {SystemJobKey} ({SystemJobName}) missed its scheduled run at {MissedRunUtc}; execution was skipped and the next run was rescheduled for {NextRunUtc}.",
            definition.Key,
            definition.Name,
            missedRunUtc,
            nextRunUtc);
    }

    public async Task<MauiSystemJobExecutionOutcome> ExecuteAsync(string key, bool scheduledExecution, CancellationToken cancellationToken = default)
    {
        var definition = GetDefinition(key);
        var stored = await LoadStatesAsync(cancellationToken: cancellationToken);
        var currentState = stored[definition.Key];
        var runTime = definition.IsDaily
            ? NormalizeRunTime(string.IsNullOrWhiteSpace(currentState.RunTime) ? definition.DefaultRunTime : currentState.RunTime)
            : string.Empty;
        var startedUtc = DateTimeOffset.UtcNow;

        MauiSystemJobExecutionOutcome outcome;
        try
        {
            outcome = definition.Key switch
            {
                MauiSystemJobKeys.Reports => await _reportsJob.ExecuteAsync(cancellationToken),
                MauiSystemJobKeys.Backup => await _backupJob.ExecuteAsync(cancellationToken),
                MauiSystemJobKeys.BackupCleanup => await _backupCleanupJob.ExecuteAsync(cancellationToken),
                MauiSystemJobKeys.JobHistoryCleanup => await _jobHistoryCleanupJob.ExecuteAsync(cancellationToken),
                MauiSystemJobKeys.HostHealthCheck => await _hostHealthCheck.ExecuteAsync(cancellationToken),
                _ => throw new InvalidOperationException($"System Job '{definition.Key}' has no execution handler.")
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MAUI System Job {SystemJobKey} ({SystemJobName}) failed.", definition.Key, definition.Name);
            outcome = MauiSystemJobExecutionOutcome.Failed(ex.Message);
        }

        if (definition.IsExternallyManaged)
        {
            // Externally managed jobs still use the common engine/runtime state, while their schedule
            // provider remains authoritative for whether and how often they run.
            var completedUtc = DateTimeOffset.UtcNow;
            var scheduleProvider = GetExternallyManagedJob(definition);
            var nextRunUtc = scheduleProvider.GetNextRunAfterExecution(outcome, completedUtc);
            await _repository.RecordExecutionAsync(
                definition.Key,
                startedUtc,
                outcome.Result,
                outcome.Message,
                nextRunUtc,
                updateNextRun: true,
                cancellationToken);
            return outcome;
        }

        // A manual Run Now normally preserves the existing schedule. If that schedule is already due,
        // advance it so a manual run cannot be followed immediately by a duplicate scheduled execution.
        var advanceSchedule = currentState.Enabled
                              && (scheduledExecution || (currentState.NextRunUtc.HasValue && currentState.NextRunUtc.Value <= startedUtc));
        DateTimeOffset? nextScheduledRunUtc = advanceSchedule ? CalculateNextRunUtc(definition, runTime, DateTimeOffset.UtcNow) : null;

        await _repository.RecordExecutionAsync(
            definition.Key,
            startedUtc,
            outcome.Result,
            outcome.Message,
            nextScheduledRunUtc,
            updateNextRun: advanceSchedule,
            cancellationToken);

        return outcome;
    }

    private async Task<IReadOnlyDictionary<string, PersistedSystemJobState>> LoadStatesAsync(
        DateTimeOffset? synchronizationUtcNow = null,
        CancellationToken cancellationToken = default)
    {
        var keys = Definitions.Select(definition => definition.Key).ToArray();
        var stored = await _repository.GetAsync(keys, cancellationToken);

        if (!Definitions.All(definition => stored.ContainsKey(definition.Key)))
        {
            await InitializationGate.WaitAsync(cancellationToken);
            try
            {
                // Another request may have registered the missing definitions while this request waited.
                stored = await _repository.GetAsync(keys, cancellationToken);
                var missing = Definitions.Where(definition => !stored.ContainsKey(definition.Key)).ToArray();
                if (missing.Length > 0)
                {
                    var now = synchronizationUtcNow ?? DateTimeOffset.UtcNow;
                    foreach (var definition in missing)
                    {
                        var runTime = definition.IsDaily ? NormalizeRunTime(definition.DefaultRunTime) : string.Empty;
                        DateTimeOffset? nextRunUtc = null;
                        if (definition.DefaultEnabled && !definition.IsExternallyManaged)
                        {
                            // New jobs are scheduled for their next normal occurrence. The scheduler does
                            // not execute a synthetic catch-up run merely because MAUI has just started.
                            nextRunUtc = CalculateNextRunUtc(definition, runTime, now);
                        }
                        await _repository.SaveConfigurationAsync(definition.Key, definition.DefaultEnabled, runTime, nextRunUtc, cancellationToken);
                    }
                    stored = await _repository.GetAsync(keys, cancellationToken);
                }
            }
            finally
            {
                InitializationGate.Release();
            }
        }

        return await SynchronizeExternallyManagedJobsAsync(stored, synchronizationUtcNow ?? DateTimeOffset.UtcNow, cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, PersistedSystemJobState>> SynchronizeExternallyManagedJobsAsync(
        IReadOnlyDictionary<string, PersistedSystemJobState> stored,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        Dictionary<string, PersistedSystemJobState>? updated = null;

        foreach (var definition in Definitions.Where(item => item.IsExternallyManaged))
        {
            var currentState = (updated ?? stored)[definition.Key];
            var scheduleProvider = GetExternallyManagedJob(definition);
            var schedule = scheduleProvider.GetSchedule(currentState, utcNow);
            if (currentState.Enabled == schedule.Enabled && SameInstant(currentState.NextRunUtc, schedule.NextRunUtc))
                continue;

            await _repository.SaveConfigurationAsync(
                definition.Key,
                schedule.Enabled,
                string.Empty,
                schedule.NextRunUtc,
                cancellationToken);

            updated ??= new Dictionary<string, PersistedSystemJobState>(stored, StringComparer.OrdinalIgnoreCase);
            updated[definition.Key] = currentState with
            {
                Enabled = schedule.Enabled,
                RunTime = string.Empty,
                NextRunUtc = schedule.NextRunUtc
            };
        }

        return updated ?? stored;
    }

    private IExternallyManagedSystemJob GetExternallyManagedJob(MauiSystemJobDefinition definition)
    {
        if (_externallyManagedJobs.TryGetValue(definition.Key, out var job)) return job;
        throw new InvalidOperationException($"System Job '{definition.Key}' is externally managed but has no schedule provider.");
    }

    private static bool SameInstant(DateTimeOffset? left, DateTimeOffset? right)
    {
        if (!left.HasValue || !right.HasValue) return left.HasValue == right.HasValue;
        return left.Value.ToUniversalTime() == right.Value.ToUniversalTime();
    }

    private static MauiSystemJobState BuildState(MauiSystemJobDefinition definition, PersistedSystemJobState state, bool isRunning)
    {
        var runTime = definition.IsDaily
            ? NormalizeRunTime(string.IsNullOrWhiteSpace(state.RunTime) ? definition.DefaultRunTime : state.RunTime)
            : string.Empty;
        return new MauiSystemJobState(
            definition.Key,
            definition.Name,
            definition.Description,
            state.Enabled,
            runTime,
            DescribeSchedule(definition, runTime),
            definition.ScheduleEditable,
            state.NextRunUtc,
            state.LastRunUtc,
            state.LastResult,
            state.LastMessage,
            isRunning);
    }

    private static MauiSystemJobDefinition GetDefinition(string? key)
    {
        var normalized = (key ?? string.Empty).Trim();
        return Definitions.FirstOrDefault(definition => definition.Key.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"Unknown MAUI System Job '{normalized}'.", nameof(key));
    }

    public static string NormalizeRunTime(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (!TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            throw new ArgumentException("Run Time must be supplied in HH:mm format.");
        return time.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    private static string DescribeSchedule(MauiSystemJobDefinition definition, string runTime)
    {
        if (definition.IsInterval) return $"Every {definition.IntervalMinutes} minutes";
        if (definition.IsExternallyManaged) return "Managed by Settings";
        var time = TimeOnly.ParseExact(runTime, "HH:mm", CultureInfo.InvariantCulture);
        return $"Daily at {DateTime.Today.Add(time.ToTimeSpan()).ToString("h:mm tt", CultureInfo.CurrentCulture)}";
    }

    private static DateTimeOffset CalculateNextRunUtc(MauiSystemJobDefinition definition, string runTime, DateTimeOffset utcNow)
    {
        if (definition.IsExternallyManaged)
            throw new InvalidOperationException($"System Job '{definition.Key}' has an externally managed schedule.");

        if (definition.IsInterval)
        {
            if (definition.IntervalMinutes <= 0) throw new InvalidOperationException($"System Job '{definition.Key}' has an invalid interval.");
            var utc = utcNow.ToUniversalTime();
            var minute = (utc.Minute / definition.IntervalMinutes + 1) * definition.IntervalMinutes;
            var hourStart = new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
            return hourStart.AddMinutes(minute);
        }

        var time = TimeOnly.ParseExact(runTime, "HH:mm", CultureInfo.InvariantCulture);
        var localNow = TimeZoneInfo.ConvertTime(utcNow, TimeZoneInfo.Local);
        var localTarget = DateTime.SpecifyKind(localNow.Date.Add(time.ToTimeSpan()), DateTimeKind.Unspecified);
        if (localTarget <= localNow.DateTime) localTarget = localTarget.AddDays(1);
        if (TimeZoneInfo.Local.IsInvalidTime(localTarget)) localTarget = localTarget.AddHours(1);
        var offset = TimeZoneInfo.Local.GetUtcOffset(localTarget);
        return new DateTimeOffset(localTarget, offset).ToUniversalTime();
    }
}
