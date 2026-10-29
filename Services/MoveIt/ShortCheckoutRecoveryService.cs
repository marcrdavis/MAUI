// Durable intent for short-lived REST checkouts. Unknown ownership is never recovered by a blind POST/DELETE.
using Microsoft.AspNetCore.DataProtection;

namespace MoveItAutomationUI.Services.MoveIt;

public interface IShortCheckoutRecovery
{
    Task<ObjectEditLeaseRecord> BeginAsync(string type, string id, MoveItSessionState session, CancellationToken ct);
    Task AcquiredAsync(ObjectEditLeaseRecord record, MoveItSessionState session, CancellationToken ct);
    Task OwnershipConfirmedAsync(string type, string id, MoveItSessionState session, CancellationToken ct);
    Task CompleteAsync(ObjectEditLeaseRecord record);
    Task RequestFailedAsync(ObjectEditLeaseRecord record);
}

// Shares the existing encrypted cleanup-token format. The separate subdirectory prevents
// short operation records from being mistaken for interactive edit-duration leases.
public sealed class ShortCheckoutRecoveryService : BackgroundService, IShortCheckoutRecovery
{
    private readonly IObjectEditLeaseJournal _journal;
    private readonly ILogger<ShortCheckoutRecoveryService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, ObjectEditLeaseRecord> _records = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

    public ShortCheckoutRecoveryService(IWebHostEnvironment environment, IDataProtectionProvider protection,
        ILogger<ObjectEditLeaseJournal> journalLogger, ILogger<ShortCheckoutRecoveryService> logger)
        : this(new ObjectEditLeaseJournal(environment, protection, journalLogger, "ShortLived"), logger) { }

    internal ShortCheckoutRecoveryService(IObjectEditLeaseJournal journal, ILogger<ShortCheckoutRecoveryService> logger)
        => (_journal, _logger) = (journal, logger);

    public override async Task StartAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            foreach (var record in _journal.Load())
            {
                if (!Guid.TryParseExact(record.LeaseId, "N", out _) || string.IsNullOrWhiteSpace(record.Type)
                    || record.Id is null || !Uri.TryCreate(record.Server, UriKind.Absolute, out var uri)
                    || uri.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(record.Username))
                { _logger.LogError("An invalid short checkout journal requires administrator review."); continue; }
                record.Lost = !record.Relinquished;
                _records[record.LeaseId] = record;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _logger.LogError(ex, "Short checkout recovery records could not be loaded. Inspect MOVEit checkouts before editing affected objects."); }
        finally { _gate.Release(); }
        await base.StartAsync(ct);
    }

    public async Task<ObjectEditLeaseRecord> BeginAsync(string type, string id, MoveItSessionState session, CancellationToken ct)
    {
        var record = new ObjectEditLeaseRecord { LeaseId = Guid.NewGuid().ToString("N"), Type = type, Id = id,
            Server = session.WebAdminUrl.TrimEnd('/'), Username = session.Username, PendingRequest = true };
        _journal.ProtectSession(record, session);
        await _gate.WaitAsync(ct);
        try
        {
            // A failed local journal write must stop BEFORE any checkout request is dispatched.
            try { await _journal.WriteAsync(record, ct); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { throw new InvalidOperationException("The operation was not started because checkout recovery could not be recorded. Review Configuration > Logs and the Data folder permissions.", ex); }
            _records[record.LeaseId] = record;
            return record;
        }
        finally { _gate.Release(); }
    }

    public async Task AcquiredAsync(ObjectEditLeaseRecord record, MoveItSessionState session, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            record.Held = true; record.PendingRequest = false;
            _journal.ProtectSession(record, session);
            await _journal.WriteAsync(record, ct);
            ResolveOlder(record.Type, record.Id, session, record.LeaseId);
        }
        finally { _gate.Release(); }
    }

    // Called only AFTER a normal or explicitly confirmed interactive acquisition succeeded.
    // It retires stale local records; it never contacts MOVEit to take ownership itself.
    public async Task OwnershipConfirmedAsync(string type, string id, MoveItSessionState session, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { ResolveOlder(type, id, session, ""); }
        finally { _gate.Release(); }
    }

    private void ResolveOlder(string type, string id, MoveItSessionState session, string except)
    {
        foreach (var previous in _records.Values.Where(record => record.LeaseId != except && record.Type == type && record.Id == id
            && record.Server.Equals(session.WebAdminUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            previous.Relinquished = true;
            TryDelete(previous);
        }
    }

    public async Task CompleteAsync(ObjectEditLeaseRecord record)
    {
        await _gate.WaitAsync();
        try
        {
            // Cleanup failure on disk must not turn a CONFIRMED native release into an uncertain save.
            record.Relinquished = true; record.Held = false; record.PendingRequest = false;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await _journal.WriteAsync(record, timeout.Token); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            { _logger.LogWarning(ex, "Confirmed checkout cleanup could not be recorded locally for {Type} {Id}.", record.Type, record.Id); }
            TryDelete(record);
        }
        finally { _gate.Release(); }
    }

    public async Task RequestFailedAsync(ObjectEditLeaseRecord record)
    {
        await _gate.WaitAsync();
        try
        {
            record.Lost = true; record.PendingRequest = true; record.Held = false;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await _journal.WriteAsync(record, timeout.Token); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            { _logger.LogError(ex, "Checkout request details remain on disk for {Type} {Id}.", record.Type, record.Id); }
            Report(record);
        }
        finally { _gate.Release(); }
    }

    private void Report(ObjectEditLeaseRecord record)
    {
        if (_reported.Add(record.LeaseId))
            _logger.LogWarning("A MOVEit checkout request failed for {Type} {Id} on {Server} ({User}). Cleanup record {Record} was retained. Retry the operation to get the current MOVEit response.",
                record.Type, record.Id, record.Server, record.Username, record.LeaseId);
    }
    private void TryDelete(ObjectEditLeaseRecord record)
    {
        try { _journal.Delete(record.LeaseId); _records.Remove(record.LeaseId); _reported.Remove(record.LeaseId); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _logger.LogWarning(ex, "Local checkout journal cleanup will retry for {Type} {Id}.", record.Type, record.Id); }
    }
    internal async Task SweepAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            foreach (var record in _records.Values.ToArray())
            {
                if (record.Relinquished) TryDelete(record);
                else if (record.Lost) Report(record);
            }
        }
        finally { _gate.Release(); }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try { do { await SweepAsync(stoppingToken); } while (await timer.WaitForNextTickAsync(stoppingToken)); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
