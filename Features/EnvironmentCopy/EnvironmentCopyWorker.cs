namespace MoveItAutomationUI.Features.EnvironmentCopy;

public sealed class EnvironmentCopyWorker(EnvironmentCopyStore store, IServiceScopeFactory scopes, ILogger<EnvironmentCopyWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await store.StartupAsync(stoppingToken); }
        catch (Exception ex) { logger.LogWarning("Environment copy startup reconciliation could not finish ({ErrorType}).", ex.GetType().Name); }
        await Task.WhenAll(ProcessAsync(stoppingToken), PruneAsync(stoppingToken));
    }
    private async Task PruneAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                store.PrunePlans();
                try { await store.PruneFilesAsync(ct); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { logger.LogWarning("Copy retention cleanup will retry ({ErrorType}).", ex.GetType().Name); }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
    private async Task ProcessAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var plan in store.Queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<EnvironmentCopyCoordinator>().ExecuteAsync(plan, stoppingToken);
                }
                catch (Exception ex) { logger.LogError("Environment copy {OperationId} stopped ({ErrorType}); inspect its durable report before retrying.", plan.Id, ex.GetType().Name); }
                finally { store.Forget(plan); } // Also clear protected material when the journal itself could not be read/written.
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
