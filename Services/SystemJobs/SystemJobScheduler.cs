// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Single hosted scheduler for MAUI background work, including scheduled Reports, automatic Backup, and maintenance jobs.

using System.Collections.Concurrent;
using System.Threading.Channels;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.Upgrade;

namespace MoveItAutomationUI.Services.SystemJobs;

public enum SystemJobQueueResult
{
    Queued,
    AlreadyActive,
    SchedulerUnavailable
}

public interface ISystemJobRuntime
{
    SystemJobQueueResult QueueRunNow(string key);
    bool IsActive(string key);
}

public sealed class SystemJobScheduler : BackgroundService, ISystemJobRuntime
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private readonly Channel<SystemJobRunRequest> _queue = Channel.CreateUnbounded<SystemJobRunRequest>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly ConcurrentDictionary<string, byte> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MauiSetupState _setupState;
    private readonly MauiUpgradeState _upgradeState;
    private readonly ILogger<SystemJobScheduler> _logger;

    public SystemJobScheduler(IServiceScopeFactory scopeFactory, MauiSetupState setupState, MauiUpgradeState upgradeState, ILogger<SystemJobScheduler> logger)
    {
        _scopeFactory = scopeFactory;
        _setupState = setupState;
        _upgradeState = upgradeState;
        _logger = logger;
    }

    public SystemJobQueueResult QueueRunNow(string key)
    {
        if (!CanRunJobs()) return SystemJobQueueResult.SchedulerUnavailable;

        var normalized = (key ?? string.Empty).Trim();
        if (normalized.Length == 0 || !_active.TryAdd(normalized, 0))
            return SystemJobQueueResult.AlreadyActive;

        if (_queue.Writer.TryWrite(new SystemJobRunRequest(normalized, false)))
            return SystemJobQueueResult.Queued;

        _active.TryRemove(normalized, out _);
        return SystemJobQueueResult.SchedulerUnavailable;
    }

    public bool IsActive(string key) => _active.ContainsKey((key ?? string.Empty).Trim());

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Different System Jobs are allowed to run concurrently so a long Host Health Check or Backup
        // cannot delay the five-minute Reports dispatcher. _active still prevents overlap of the same job.
        var running = new List<Task>();

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (CanRunJobs())
                    {
                        await QueueDueJobsAsync(stoppingToken);
                        DispatchQueuedJobs(running, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "MAUI System Job scheduler cycle failed.");
                }

                running.RemoveAll(task => task.IsCompleted);

                try
                {
                    if (!CanRunJobs())
                    {
                        // Do not wait on the queue while scheduling is suspended. A queued Run Now would
                        // otherwise make WaitToReadAsync complete immediately and create a tight loop.
                        await Task.Delay(PollInterval, stoppingToken);
                        continue;
                    }

                    // Use a cancellable queue wait rather than abandoning a WaitToReadAsync waiter
                    // every time the polling interval wins. This keeps the channel's waiter list bounded
                    // during long idle periods while still waking immediately for Run Now requests.
                    using var pollCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    pollCancellation.CancelAfter(PollInterval);
                    try
                    {
                        await _queue.Reader.WaitToReadAsync(pollCancellation.Token);
                    }
                    catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                    {
                        // Normal poll timeout.
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        finally
        {
            if (running.Count > 0)
                await Task.WhenAll(running);
        }
    }

    private bool CanRunJobs()
        => _setupState.IsConfiguredAtStartup && !_upgradeState.IsPending;

    private void DispatchQueuedJobs(List<Task> running, CancellationToken cancellationToken)
    {
        while (_queue.Reader.TryRead(out var request))
            running.Add(ExecuteRequestSafelyAsync(request, cancellationToken));
    }

    private async Task ExecuteRequestSafelyAsync(SystemJobRunRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteRequestAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal application shutdown. ExecuteRequestAsync already releases the active-job guard.
        }
    }

    private async Task QueueDueJobsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISystemJobService>();
        var due = await service.GetDueJobKeysAsync(DateTimeOffset.UtcNow, cancellationToken);
        foreach (var key in due)
        {
            if (!_active.TryAdd(key, 0)) continue;
            if (!_queue.Writer.TryWrite(new SystemJobRunRequest(key, true)))
                _active.TryRemove(key, out _);
        }
    }

    private async Task ExecuteRequestAsync(SystemJobRunRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ISystemJobService>();
            if (!service.IsKnownJob(request.Key)) return;
            await service.ExecuteAsync(request.Key, request.Scheduled, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MAUI System Job {SystemJobKey} encountered an unexpected scheduler failure.", request.Key);
        }
        finally
        {
            _active.TryRemove(request.Key, out _);
        }
    }

    private sealed record SystemJobRunRequest(string Key, bool Scheduled);
}
