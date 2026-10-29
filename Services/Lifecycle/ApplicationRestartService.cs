// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Requests a graceful application-process restart after a lifecycle operation has safely completed.

using System.Threading.Channels;

namespace MoveItAutomationUI.Services.Lifecycle;

public interface IApplicationRestartService
{
    bool IsRestartScheduled { get; }

    // Purpose: Queues one graceful application restart without requiring IIS/app-pool administration rights.
    bool ScheduleRestart(string reason);
}

public sealed class ApplicationRestartService : BackgroundService, IApplicationRestartService
{
    private readonly Channel<string> _requests = Channel.CreateBounded<string>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<ApplicationRestartService> _logger;
    private int _restartScheduled;

    public bool IsRestartScheduled => Volatile.Read(ref _restartScheduled) == 1;

    public ApplicationRestartService(
        IHostApplicationLifetime lifetime,
        ILogger<ApplicationRestartService> logger)
    {
        _lifetime = lifetime;
        _logger = logger;
    }

    // Purpose: Queues one graceful restart request; duplicate requests are harmless.
    public bool ScheduleRestart(string reason)
    {
        if (Interlocked.CompareExchange(ref _restartScheduled, 1, 0) != 0)
            return false;

        reason = string.IsNullOrWhiteSpace(reason) ? "MAUI application restart requested." : reason.Trim();
        if (_requests.Writer.TryWrite(reason)) return true;

        Interlocked.Exchange(ref _restartScheduled, 0);
        return false;
    }

    // Purpose: Delays shutdown briefly so the restart-request HTTP response can reach the browser first.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var reason = await _requests.Reader.ReadAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            _logger.LogWarning("Stopping the MAUI application for a controlled restart. Reason: {Reason}", reason);
            _lifetime.StopApplication();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown before a queued restart requires no additional action.
        }
    }
}
