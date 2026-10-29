// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Centralizes Host connection-test result mapping to persisted MAUI Health metadata.

using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Services.Metadata;

public interface IHostHealthService
{
    Task SaveAsync(string environmentKey, string hostId, string health, DateTimeOffset checkedUtc, string detail, CancellationToken cancellationToken = default);
    Task<string> SaveConnectionTestResultAsync(string environmentKey, HostConnectionTestResult result, DateTimeOffset checkedUtc, CancellationToken cancellationToken = default);
}

public sealed class HostHealthService : IHostHealthService
{
    private readonly IHostHealthRepository _repository;

    public HostHealthService(IHostHealthRepository repository)
    {
        _repository = repository;
    }

    public Task SaveAsync(string environmentKey, string hostId, string health, DateTimeOffset checkedUtc, string detail, CancellationToken cancellationToken = default)
        => _repository.SaveAsync(environmentKey, hostId, health, checkedUtc, detail, cancellationToken);

    public async Task<string> SaveConnectionTestResultAsync(
        string environmentKey,
        HostConnectionTestResult result,
        DateTimeOffset checkedUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        var health = result.Status switch
        {
            HostConnectionTestService.StatusPassed => HostHealthStates.Healthy,
            HostConnectionTestService.StatusUnsupported => HostHealthStates.Unknown,
            _ => HostHealthStates.Unhealthy
        };

        await _repository.SaveAsync(
            environmentKey,
            result.HostId,
            health,
            checkedUtc,
            result.Message,
            cancellationToken);

        return health;
    }
}
