// Shared edit-duration checkout leases. Browser heartbeats never count as user activity.
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Concurrency;
using MoveItAutomationUI.Services.Configuration;

namespace MoveItAutomationUI.Services.MoveIt;

public sealed record ObjectEditLeaseResult(bool Required, string LeaseId = "");
public sealed record ObjectEditLeaseOwner(string SessionId, MoveItSessionState Session);

public interface IObjectEditLeaseService
{
    Task<ObjectEditLeaseResult> OpenAsync(string type, string id, ObjectEditLeaseOwner owner, CancellationToken ct);
    Task<ObjectEditLeaseResult> OverrideAsync(string type, string id, ObjectEditLeaseOwner owner, CancellationToken ct);
    Task HeartbeatAsync(string leaseId, ObjectEditLeaseOwner owner, CancellationToken ct);
    Task CloseAsync(string leaseId, ObjectEditLeaseOwner owner, CancellationToken ct);
    Task ReleaseSessionAsync(string sessionId, CancellationToken ct);
    Task<IDisposable> EnterAsync(string type, string id, string? leaseId, ObjectEditLeaseOwner owner, CancellationToken ct);
    Task<IDisposable> EnterTokenAsync(string leaseId, ObjectEditLeaseOwner owner, CancellationToken ct);
}

// Transport is deliberately shared by all editors; future resource types extend this adapter.
public interface IObjectEditLeaseTransport
{
    Task AcquireAsync(string type, string id, MoveItSessionState session, CancellationToken ct);
    Task ForceAcquireAsync(string type, string id, MoveItSessionState session, CancellationToken ct);
    Task ReleaseAsync(string type, string id, MoveItSessionState session, CancellationToken ct);
    Task<string> RevisionAsync(string type, string id, MoveItSessionState session, CancellationToken ct);
}

public sealed class ObjectEditLeaseTransport(IServiceScopeFactory scopes, IShortCheckoutRecovery? checkoutRecovery = null) : IObjectEditLeaseTransport
{
    private async Task<T> RunAsync<T>(MoveItSessionState session, Func<IMoveItRestClient, IMoveItComClient, Task<T>> action)
    {
        using var scope = scopes.CreateScope();
        using var authentication = scope.ServiceProvider.GetRequiredService<IMoveItSessionService>().PushBackgroundSession(session);
        return await action(scope.ServiceProvider.GetRequiredService<IMoveItRestClient>(), scope.ServiceProvider.GetRequiredService<IMoveItComClient>());
    }
    public Task AcquireAsync(string type, string id, MoveItSessionState session, CancellationToken ct)
        => RunAsync(session, async (rest, _) =>
        {
            if (type == "Task") await rest.CheckoutTaskV0Async(id, ct);
            else if (type == "Host") await rest.CheckoutHostV0Async(id, ct);
            else if (type == "Script") await rest.CheckoutCustomScriptV0Async(id, ct);
            else if (type == "DateList") await rest.CheckoutDateListV0Async(id, ct);
            else throw new ArgumentException("Unsupported checkout object type.");
            if (checkoutRecovery is not null) await checkoutRecovery.OwnershipConfirmedAsync(type, id, session, ct);
            return true;
        });
    public Task ReleaseAsync(string type, string id, MoveItSessionState session, CancellationToken ct)
        => RunAsync(session, async (rest, _) =>
        {
            if (type == "Task") await rest.ReleaseTaskCheckoutV0Async(id, ct);
            else if (type == "Host") await rest.ReleaseHostCheckoutV0Async(id, ct);
            else if (type == "Script") await rest.ReleaseCustomScriptCheckoutV0Async(id, ct);
            else if (type == "DateList") await rest.ReleaseDateListCheckoutV0Async(id, ct);
            else throw new ArgumentException("Unsupported checkout object type.");
            return true;
        });
    public Task ForceAcquireAsync(string type, string id, MoveItSessionState session, CancellationToken ct)
    {
        if (session.Info?.IsAdmin != true) throw new InvalidOperationException("Only a MOVEit administrator can override checkout.");
        return RunAsync(session, async (rest, _) =>
        {
            await rest.ForceCheckoutObjectV0Async(type, id, ct);
            if (checkoutRecovery is not null) await checkoutRecovery.OwnershipConfirmedAsync(type, id, session, ct);
            return true;
        });
    }
    public Task<string> RevisionAsync(string type, string id, MoveItSessionState session, CancellationToken ct)
        => RunAsync(session, async (rest, com) =>
        {
            if (type == "DateList")
            {
                var raw = Resources.DateLists.DateListConfiguration.Unwrap(await rest.GetDateListAsync(id, ct), id);
                return ObjectEditVersion.Create("DateListEditor", id, Resources.DateLists.DateListConfiguration.Revision(raw));
            }
            if (type == "Script")
            {
                var raw = Resources.Scripts.ScriptConfiguration.Unwrap(await rest.GetCustomScriptAsync(id, ct), id);
                return ObjectEditVersion.Create("ScriptEditor", id, Resources.Scripts.ScriptConfiguration.Revision(raw));
            }
            var xml = type == "Task" ? (await com.GetTaskConfigurationAsync(id, ct)).Xml : (await com.GetHostConfigurationAsync(id, ct)).Xml;
            return ObjectEditVersion.Create(type, id, ObjectEditVersion.Definition(new System.Text.Json.Nodes.JsonObject(), xml));
        });
}

public sealed class ObjectEditLeaseRecord
{
    public string LeaseId { get; set; } = "";
    public string SessionHash { get; set; } = "";
    public string Type { get; set; } = "";
    public string Id { get; set; } = "";
    public string Server { get; set; } = "";
    public string Username { get; set; } = "";
    public DateTimeOffset ExpiresUtc { get; set; }
    public DateTimeOffset SessionExpiresUtc { get; set; }
    // Internal write-ahead marker for an API request whose response has not arrived.
    // Keep the existing on-disk name so deployed cleanup records remain readable.
    public bool Held { get; set; }
    [JsonPropertyName("Uncertain")] public bool PendingRequest { get; set; }
    // A rejected revision or explicit takeover retires our old ownership permanently.
    // Crash recovery must never POST/DELETE on behalf of that stale editor.
    public bool Relinquished { get; set; }
    // Only cleanup tokens are encrypted on disk. Passwords are never persisted.
    public string ProtectedSession { get; set; } = "";
    [JsonIgnore] public MoveItSessionState? Session { get; set; }
    [JsonIgnore] public IDisposable? Reservation { get; set; }
    [JsonIgnore] public bool Lost { get; set; }
}

public interface IObjectEditLeaseJournal
{
    IEnumerable<ObjectEditLeaseRecord> Load();
    Task WriteAsync(ObjectEditLeaseRecord record, CancellationToken ct);
    void Delete(string leaseId);
    void ProtectSession(ObjectEditLeaseRecord record, MoveItSessionState session);
    MoveItSessionState ReadSession(ObjectEditLeaseRecord record);
}

public sealed class ObjectEditLeaseJournal : IObjectEditLeaseJournal
{
    private readonly string _directory;
    private readonly IDataProtector _protector;
    private readonly ILogger<ObjectEditLeaseJournal> _logger;
    public ObjectEditLeaseJournal(IWebHostEnvironment environment, IDataProtectionProvider protection, ILogger<ObjectEditLeaseJournal> logger, string? subdirectory = null)
    {
        _directory = Path.Combine(environment.ContentRootPath, "Data", "ObjectEditLeases");
        if (subdirectory is not null) _directory = Path.Combine(_directory, subdirectory);
        _protector = protection.CreateProtector("MAUI.ObjectEditLease.CleanupTokens.v1");
        _logger = logger;
    }
    private string FilePath(string id)
        => Path.Combine(_directory, Guid.ParseExact(id, "N").ToString("N") + ".json");
    public IEnumerable<ObjectEditLeaseRecord> Load()
    {
        if (!Directory.Exists(_directory)) yield break;
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            ObjectEditLeaseRecord? record = null;
            try { record = JsonSerializer.Deserialize<ObjectEditLeaseRecord>(File.ReadAllText(file))
                ?? throw new InvalidOperationException("Invalid checkout recovery record."); }
            catch (Exception ex) { _logger.LogError(ex, "Unable to read checkout recovery record {Record}; inspect MOVEit checkouts. Other records will still be recovered.", Path.GetFileName(file)); }
            if (record is not null) yield return record;
        }
    }
    public async Task WriteAsync(ObjectEditLeaseRecord record, CancellationToken ct)
    {
        Directory.CreateDirectory(_directory);
        var path = FilePath(record.LeaseId);
        var temp = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(record), ct);
            File.Move(temp, path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public void Delete(string leaseId) => File.Delete(FilePath(leaseId));
    public void ProtectSession(ObjectEditLeaseRecord record, MoveItSessionState session)
    {
        // Do not serialize MoveItSessionState: it also contains a password and unrelated user data.
        record.ProtectedSession = _protector.Protect(JsonSerializer.Serialize(new CleanupTokens(
            session.ServerKey, session.EnvironmentKey, session.WebAdminUrl, session.Username,
            session.AccessToken, session.RefreshToken, session.AccessTokenExpiresUtc, session.CleanupOwnerSessionId)));
    }
    public MoveItSessionState ReadSession(ObjectEditLeaseRecord record)
    {
        var tokens = JsonSerializer.Deserialize<CleanupTokens>(_protector.Unprotect(record.ProtectedSession))
            ?? throw new InvalidOperationException("Checkout recovery authentication is unavailable.");
        if (tokens.WebAdminUrl.TrimEnd('/') != record.Server || tokens.Username != record.Username)
            throw new InvalidOperationException("Checkout recovery identity does not match its record.");
        return new MoveItSessionState { ServerKey = tokens.ServerKey, EnvironmentKey = tokens.EnvironmentKey,
            WebAdminUrl = tokens.WebAdminUrl, Username = tokens.Username, AccessToken = tokens.AccessToken,
            RefreshToken = tokens.RefreshToken, AccessTokenExpiresUtc = tokens.AccessTokenExpiresUtc, CleanupOwnerSessionId = tokens.OwnerSessionId };
    }
    private sealed record CleanupTokens(string ServerKey, string EnvironmentKey, string WebAdminUrl, string Username,
        string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresUtc, string OwnerSessionId);
}

public sealed class ObjectEditLeaseService : BackgroundService, IObjectEditLeaseService
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, ObjectEditLeaseRecord> _records = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _cleanupReported = new(StringComparer.Ordinal);
    private readonly KeyedAsyncLock _gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly IObjectEditLeaseTransport _transport;
    private readonly IObjectEditLeaseJournal _journal;
    private readonly IOptionsMonitor<UiOptions> _options;
    private readonly ConfigurationStartupState _startup;
    private readonly TimeProvider _clock;
    private readonly ILogger<ObjectEditLeaseService> _logger;
    public ObjectEditLeaseService(IObjectEditLeaseTransport transport, IObjectEditLeaseJournal journal,
        IOptionsMonitor<UiOptions> options, ConfigurationStartupState startup, TimeProvider clock, ILogger<ObjectEditLeaseService> logger)
    { _transport = transport; _journal = journal; _options = options; _startup = startup; _clock = clock; _logger = logger; }
    private DateTimeOffset Now => _clock.GetUtcNow();
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Key(string server, string type, string id) => server.TrimEnd('/') + "|" + type + ":" + id;
    private static void Validate(string type, string id)
    {
        // Host 0 is MOVEit's built-in Local File System Host and participates in the same lease lifecycle.
        if (type is not ("Task" or "Host" or "Script" or "DateList") || !long.TryParse(id, out var number) || number < 0 || number == 0 && (type != "Host" || id != "0"))
            throw new ArgumentException("A supported object type and valid object ID are required.");
    }
    private void Refresh(ObjectEditLeaseRecord record, ObjectEditLeaseOwner owner)
    {
        record.Session = owner.Session;
        record.Session.CleanupOwnerSessionId = owner.SessionId;
        record.ExpiresUtc = Now + Lifetime;
        record.SessionExpiresUtc = owner.Session.LastUserActivityUtc + TimeSpan.FromMinutes(_startup.IdleSessionTimeoutMinutes);
        _journal.ProtectSession(record, owner.Session);
    }
    private ObjectEditLeaseRecord Owned(string leaseId, ObjectEditLeaseOwner owner)
    {
        if (!_records.TryGetValue(leaseId, out var record) || record.SessionHash != Hash(owner.SessionId)
            || record.Server != owner.Session.WebAdminUrl.TrimEnd('/') || record.Username != owner.Session.Username)
            throw new ObjectEditLeaseException(ObjectCheckout.EditorEndedMessage);
        return record;
    }
    private Task PersistAsync(ObjectEditLeaseRecord record, CancellationToken ct)
    {
        if (record.Session is not null) _journal.ProtectSession(record, record.Session);
        return _journal.WriteAsync(record, ct);
    }
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // Startup restores only cleanup authority, never an interactive login or an editable lease.
        try
        {
            foreach (var record in _journal.Load())
            {
                try
                {
                    if (record.Relinquished) { _journal.Delete(record.LeaseId); continue; }
                    Validate(record.Type, record.Id);
                    record.Session = _journal.ReadSession(record);
                    record.ExpiresUtc = DateTimeOffset.MinValue;
                    record.Lost = true;
                    using var server = ObjectCheckout.ForServer(record.Server);
                    if (record.Held && !record.PendingRequest)
                        record.Reservation = await ObjectCheckout.ReserveAsync(record.Type, record.Id, record.LeaseId, cancellationToken);
                    _records[record.LeaseId] = record;
                }
                catch (Exception ex) { _logger.LogError(ex, "Unable to recover checkout for {Type} {Id} on {Server}; reopen it in MAUI for confirmed checkout recovery. Other records will still be recovered.", record.Type, record.Id, record.Server); }
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Unable to restore all checkout recovery records. Reopen affected objects in MAUI and use confirmed administrator checkout recovery."); }
        await base.StartAsync(cancellationToken);
    }
    public async Task<ObjectEditLeaseResult> OpenAsync(string type, string id, ObjectEditLeaseOwner owner, CancellationToken ct)
    {
        Validate(type, id);
        if (!_options.CurrentValue.CheckoutObjectsForDurationOfEdit.Value) return new(false);
        var key = Key(owner.Session.WebAdminUrl, type, id);
        using var gate = await _gates.AcquireAsync(key, ct);
        var existing = _records.Values.FirstOrDefault(r => Key(r.Server, r.Type, r.Id).Equals(key, StringComparison.OrdinalIgnoreCase));
        // MOVEit decides whether checkout is available. A local cleanup record is not
        // evidence of a native lock or its current owner, even for the same username.
        return await AcquireRecordAsync(type, id, owner, ct, previous: existing);
    }

    // Explicit administrator recovery uses the captured force:true POST; ordinary acquisition and cleanup never force.
    // Retire stale MAUI ownership without DELETE: another native editor may now own that checkout.
    public async Task<ObjectEditLeaseResult> OverrideAsync(string type, string id, ObjectEditLeaseOwner owner, CancellationToken ct)
    {
        if (owner.Session.Info?.IsAdmin != true) throw new InvalidOperationException("Only a MOVEit administrator can override checkout.");
        Validate(type, id);
        using var gate = await _gates.AcquireAsync(Key(owner.Session.WebAdminUrl, type, id), ct);
        var existing = _records.Values.FirstOrDefault(r => Key(r.Server, r.Type, r.Id).Equals(Key(owner.Session.WebAdminUrl, type, id), StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Held = false; existing.PendingRequest = false; existing.Lost = true; existing.Relinquished = true;
            await PersistAsync(existing, ct);
            Remove(existing);
        }
        return await AcquireRecordAsync(type, id, owner, ct, force: true);
    }

    private async Task<ObjectEditLeaseResult> AcquireRecordAsync(string type, string id, ObjectEditLeaseOwner owner, CancellationToken ct,
        bool force = false, ObjectEditLeaseRecord? previous = null)
    {
        var record = new ObjectEditLeaseRecord { LeaseId = Guid.NewGuid().ToString("N"), Type = type, Id = id,
            Server = owner.Session.WebAdminUrl.TrimEnd('/'), Username = owner.Session.Username, SessionHash = Hash(owner.SessionId), PendingRequest = true };
        Refresh(record, owner);
        using var server = ObjectCheckout.ForServer(record.Server);
        var previousState = (Held: previous?.Held ?? false, Pending: previous?.PendingRequest ?? false,
            Lost: previous?.Lost ?? false, Retired: previous?.Relinquished ?? false, Reserved: previous?.Reservation is not null);
        record.Reservation = await ObjectCheckout.ReserveAsync(type, id, record.LeaseId, ct, previous?.LeaseId);
        try
        {
            // Suspend the old editor's cleanup authority before asking MOVEit. If the
            // response is lost, it must not later DELETE a newer user's checkout.
            if (previous is not null)
            {
                previous.Held = false; previous.PendingRequest = true; previous.Relinquished = true;
                await PersistAsync(previous, ct);
            }
            // Journal intent before contacting MOVEit. A lost acknowledgement cannot justify deleting another user's checkout.
            await PersistAsync(record, ct);
            _records[record.LeaseId] = record;
            if (force) await _transport.ForceAcquireAsync(type, id, record.Session!, ct);
            else await _transport.AcquireAsync(type, id, record.Session!, ct);
            record.Held = true; record.PendingRequest = false;
            await PersistAsync(record, CancellationToken.None);
            if (previous is not null) await RetireAsync(previous);
            return new(true, record.LeaseId);
        }
        catch (Exception ex)
        {
            var dispatched = _records.ContainsKey(record.LeaseId);
            if (!dispatched || !record.Held && ObjectCheckout.IsDefinitiveRejection(ex))
            {
                // A rejected second open must not revoke the first editor's valid
                // session. The new request acquired nothing; restore its reservation.
                if (previous is not null)
                {
                    if (previousState.Reserved)
                        previous.Reservation = await ObjectCheckout.ReserveAsync(type, id, previous.LeaseId, CancellationToken.None, record.LeaseId);
                    previous.Held = previousState.Held; previous.PendingRequest = previousState.Pending;
                    previous.Lost = previousState.Lost; previous.Relinquished = previousState.Retired;
                    try { await PersistAsync(previous, CancellationToken.None); }
                    catch (Exception journalError)
                    {
                        previous.Held = false; previous.PendingRequest = true; previous.Lost = true;
                        DropReservation(previous);
                        _logger.LogError(journalError, "Could not restore the local editor record for {Type} {Id} after a rejected checkout.", type, id);
                    }
                }
                await RetireAsync(record);
            }
            else
            {
                if (previous is not null) await RetireAsync(previous);
                record.Lost = true; record.ExpiresUtc = Now;
                if (!record.Held) DropReservation(record);
                _logger.LogError(ex, "MOVEit checkout request failed for {Type} {Id}; cleanup details were retained.", type, id);
            }
            throw;
        }
    }

    private async Task RetireAsync(ObjectEditLeaseRecord record)
    {
        record.Held = false; record.PendingRequest = false; record.Lost = true; record.Relinquished = true;
        try { await PersistAsync(record, CancellationToken.None); }
        catch (Exception ex) { _logger.LogError(ex, "Could not retire the old local editor record for {Type} {Id}.", record.Type, record.Id); }
        Remove(record);
    }

    private static void DropReservation(ObjectEditLeaseRecord record)
    {
        record.Reservation?.Dispose();
        record.Reservation = null;
    }
    public async Task HeartbeatAsync(string leaseId, ObjectEditLeaseOwner owner, CancellationToken ct)
    {
        var record = Owned(leaseId, owner);
        using var gate = await _gates.AcquireAsync(Key(record.Server, record.Type, record.Id), ct);
        record = Owned(leaseId, owner);
        if (!record.Held || record.Lost || record.ExpiresUtc <= Now || record.SessionExpiresUtc <= Now)
            throw new ObjectEditLeaseException(ObjectCheckout.EditorEndedMessage);
        Refresh(record, owner);
        await PersistAsync(record, ct);
    }
    public async Task CloseAsync(string leaseId, ObjectEditLeaseOwner owner, CancellationToken ct)
    {
        if (!_records.ContainsKey(leaseId)) return;
        var record = Owned(leaseId, owner);
        using var gate = await _gates.AcquireAsync(Key(record.Server, record.Type, record.Id), ct);
        if (!_records.ContainsKey(leaseId)) return;
        record.Session = owner.Session;
        record.Session.CleanupOwnerSessionId = owner.SessionId;
        // The browser stops heartbeats on Close. If release fails, retry on the next sweep
        // rather than retaining an apparently active editor for another five minutes.
        record.Lost = true; record.ExpiresUtc = Now;
        await ReleaseAsync(record, ct);
    }
    private void Remove(ObjectEditLeaseRecord record)
    {
        _records.TryRemove(record.LeaseId, out _);
        _cleanupReported.TryRemove(record.LeaseId, out _);
        DropReservation(record);
        try { _journal.Delete(record.LeaseId); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _logger.LogWarning(ex, "Could not remove a completed local editor record for {Type} {Id}.", record.Type, record.Id); }
    }
    private async Task ReleaseAsync(ObjectEditLeaseRecord record, CancellationToken ct)
    {
        if (record.Relinquished) { Remove(record); return; }
        if (record.PendingRequest && !record.Held)
        {
            // No completed acquisition is recorded. Do not issue DELETE for an
            // unanswered request; the next open uses an ordinary native checkout.
            DropReservation(record);
            throw new ObjectEditLeaseException(ObjectCheckout.ReleaseFailedMessage);
        }
        if (record.Held)
        {
            // Write release intent first. Cleanup must not blindly delete if
            // the process dies after MOVEit releases but before the journal entry is removed.
            record.Held = false; record.PendingRequest = true;
            try
            {
                await PersistAsync(record, ct);
                await _transport.ReleaseAsync(record.Type, record.Id, record.Session ?? _journal.ReadSession(record), ct);
            }
            catch { DropReservation(record); throw; }
        }
        await RetireAsync(record);
    }
    public async Task ReleaseSessionAsync(string sessionId, CancellationToken ct)
    {
        foreach (var record in _records.Values.Where(r => r.SessionHash == Hash(sessionId)))
        {
            using var gate = await _gates.AcquireAsync(Key(record.Server, record.Type, record.Id), ct);
            if (!_records.ContainsKey(record.LeaseId)) continue;
            record.ExpiresUtc = Now; record.Lost = true;
            try { record.Session = _journal.ReadSession(record); await ReleaseAsync(record, ct); }
            catch (Exception ex) { _logger.LogError(ex, "Session checkout release failed for {Type} {Id}; reopen the object in MAUI for confirmed checkout recovery.", record.Type, record.Id); }
        }
    }
    public async Task<IDisposable> EnterAsync(string type, string id, string? leaseId, ObjectEditLeaseOwner owner, CancellationToken ct)
    {
        var key = Key(owner.Session.WebAdminUrl, type, id);
        var gate = await _gates.AcquireAsync(key, ct);
        try
        {
            if (string.IsNullOrWhiteSpace(leaseId))
            {
                if (_options.CurrentValue.CheckoutObjectsForDurationOfEdit.Value || _records.Values.Any(r => Key(r.Server, r.Type, r.Id) == key && r.Held && !r.Lost))
                    throw new ObjectEditLeaseException(ObjectCheckout.EditorEndedMessage);
                return gate;
            }
            var record = Owned(leaseId, owner);
            if (record.Type != type || record.Id != id || !record.Held || record.Lost || record.ExpiresUtc <= Now || record.SessionExpiresUtc <= Now)
                throw new ObjectEditLeaseException(ObjectCheckout.EditorEndedMessage);
            Refresh(record, owner);
            await PersistAsync(record, ct);
            return new Operation(gate, record, this);
        }
        catch { gate.Dispose(); throw; }
    }
    public Task<IDisposable> EnterTokenAsync(string leaseId, ObjectEditLeaseOwner owner, CancellationToken ct)
    {
        var record = Owned(leaseId, owner);
        return EnterAsync(record.Type, record.Id, leaseId, owner, ct);
    }
    public static IObjectCheckoutLifetime? CheckoutLifetime(IDisposable operation) => operation as IObjectCheckoutLifetime;
    private sealed class Operation(IDisposable gate, ObjectEditLeaseRecord record, ObjectEditLeaseService service) : IDisposable, IObjectCheckoutLifetime
    {
        public string Owner => record.LeaseId;
        private string? _nativeRevision;
        public void VerifyNativeRevision(string type, string id, string xml)
        {
            if (_nativeRevision is null || record.Type != type || record.Id != id
                || _nativeRevision != ObjectEditVersion.Create(type, id, ObjectEditVersion.Definition(new System.Text.Json.Nodes.JsonObject(), xml)))
                throw new ObjectEditConflictException(type);
        }
        public async Task<T> ExecuteNativeAsync<T>(string type, string id, Func<CancellationToken, Task<T>> edit, Func<T, string> savedXml, CancellationToken ct)
        {
            if (record.Type != type || record.Id != id || record.Lost || !record.Held) throw new ObjectEditLeaseException("The editing checkout is unavailable.");
            _nativeRevision = await service._transport.RevisionAsync(type, id, record.Session!, ct);
            // COM owns CheckoutItem/ModifyItem/cleanup. Hand off our REST checkout, and compare the
            // XML inside the COM checkout before transforming it; never force a COM checkout.
            try
            {
                record.Held = false; record.PendingRequest = true;
                await service.PersistAsync(record, ct);
                await service._transport.ReleaseAsync(type, id, record.Session!, ct);
                record.PendingRequest = false;
                await service.PersistAsync(record, ct);
            }
            catch (Exception ex)
            {
                _nativeRevision = null;
                record.Lost = true; record.ExpiresUtc = service.Now;
                if (!record.Held) DropReservation(record);
                service._logger.LogError(ex, "Could not hand off editing checkout for native {Type} {Id} update.", type, id);
                throw new ObjectEditLeaseException("Unable to complete the checkout request. No changes were submitted. Reload the object before saving.");
            }
            T result = default!; Exception? failure = null; string? expected = null;
            try
            {
                result = await edit(ct);
                expected = ObjectEditVersion.Create(type, id, ObjectEditVersion.Definition(new System.Text.Json.Nodes.JsonObject(), savedXml(result)));
            }
            catch (Exception ex) { failure = ex; }
            finally { _nativeRevision = null; }
            if ((failure is MoveItApiException or MoveItComException) && ObjectCheckoutRejectedException.DescribesLock(failure.Message))
            {
                await service.RetireAsync(record);
                throw new ObjectCheckoutRejectedException(failure.Message);
            }
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                record.PendingRequest = true;
                await service.PersistAsync(record, cleanup.Token);
                await service._transport.AcquireAsync(type, id, record.Session!, cleanup.Token);
                record.Held = true; record.PendingRequest = false;
                await service.PersistAsync(record, cleanup.Token);
                if (expected is not null && expected != await service._transport.RevisionAsync(type, id, record.Session!, cleanup.Token))
                    throw new ObjectEditConflictException(type);
            }
            catch (Exception ex)
            {
                record.Lost = true; record.ExpiresUtc = service.Now;
                if (!record.Held) DropReservation(record);
                service._logger.LogError(ex, "Could not restore editing checkout after native {Type} {Id} update.", type, id);
                throw new ObjectEditLeaseException("The update may have completed, but checkout could not be reacquired. Reload the object before saving again.", true);
            }
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
            return result;
        }
        public async Task<T> ExecuteAsync<T>(string type, string id, Func<CancellationToken, Task<T>> edit, CancellationToken ct)
        {
            if (record.Type != type || record.Id != id || record.Lost || !record.Held) throw new ObjectEditLeaseException("The editing checkout is unavailable.");
            // A PUT may release checkout before its response arrives. Persist that ambiguity
            // BEFORE invoking the save so crash cleanup cannot issue DELETE without a
            // completed acquisition; an older Held=true record could release a later editor.
            record.Held = false; record.PendingRequest = true;
            try { await service.PersistAsync(record, ct); }
            catch (Exception ex)
            {
                // No PUT was dispatched: retain our last acknowledged checkout state.
                record.Held = true; record.PendingRequest = false;
                record.Lost = true; record.ExpiresUtc = service.Now;
                service._logger.LogError(ex, "Could not record checkout recovery before {Type} {Id} save; no write was attempted.", type, id);
                throw new ObjectEditLeaseException("The save was not started because its checkout record could not be written. Reload the object before saving again.");
            }
            T result = default!; Exception? failure = null;
            try { result = await edit(ct); } catch (Exception ex) { failure = ex; }
            if ((failure is MoveItApiException or MoveItComException) && ObjectCheckoutRejectedException.DescribesLock(failure.Message))
            {
                // A native lock rejection retires this editor's old ownership, including
                // when another administrator checked out the object without changing its revision.
                await service.RetireAsync(record);
                throw new ObjectCheckoutRejectedException(failure.Message);
            }
            if (failure is ObjectEditConflictException && ObjectEditVersion.SavedVersion is null)
            {
                // Revision validation rejected this write. Do not DELETE/reacquire a checkout
                // that may have been taken over by another native editor. Recovery is explicit.
                await service.RetireAsync(record);
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
            // Native PUT/COM saves may release checkout. Use the established release contract, then
            // reacquire immediately and compare across that gap before reporting a safe continuation.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                var before = await service._transport.RevisionAsync(type, id, record.Session!, cleanup.Token);
                record.Held = false; record.PendingRequest = true;
                await service.PersistAsync(record, cleanup.Token);
                await service._transport.ReleaseAsync(type, id, record.Session!, cleanup.Token);
                await service._transport.AcquireAsync(type, id, record.Session!, cleanup.Token);
                record.Held = true; record.PendingRequest = false;
                await service.PersistAsync(record, cleanup.Token);
                var after = await service._transport.RevisionAsync(type, id, record.Session!, cleanup.Token);
                if (before != after) throw new ObjectEditConflictException(type);
            }
            catch (Exception ex)
            {
                record.Lost = true; record.ExpiresUtc = service.Now;
                if (!record.Held) DropReservation(record);
                service._logger.LogError(ex, "Could not maintain editing checkout after {Type} {Id} save.", type, id);
                throw new ObjectEditLeaseException("The save may have completed, but checkout could not be reacquired. Reload the object before saving again.", true);
            }
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
            return result;
        }
        public void Dispose()
        {
            if (!record.Lost) record.ExpiresUtc = service.Now + Lifetime;
            gate.Dispose();
        }
    }
    public async Task SweepAsync(CancellationToken ct)
    {
        foreach (var record in _records.Values.Where(r => r.ExpiresUtc <= Now || r.SessionExpiresUtc <= Now))
        {
            // A long-running Run As Of/Save must not block cleanup of unrelated expired objects.
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attempt.CancelAfter(TimeSpan.FromMilliseconds(100));
            IDisposable gate;
            try { gate = await _gates.AcquireAsync(Key(record.Server, record.Type, record.Id), attempt.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { continue; }
            using var heldGate = gate;
            if (!_records.ContainsKey(record.LeaseId) || record.ExpiresUtc > Now && record.SessionExpiresUtc > Now) continue;
            record.Lost = true;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                // Cleanup may renew its existing tokens; it cannot use a retained password to log in again.
                record.Session = _journal.ReadSession(record);
                await ReleaseAsync(record, timeout.Token);
            }
            catch (Exception ex)
            {
                if (_cleanupReported.TryAdd(record.LeaseId, 0))
                    _logger.LogWarning(ex, "Checkout recovery is pending for {Type} {Id} on {Server}. Reload the object in MAUI; a MOVEit administrator can use Override Checkout.", record.Type, record.Id, record.Server);
            }
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try { do { await SweepAsync(stoppingToken); } while (await timer.WaitForNextTickAsync(stoppingToken)); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Best effort on controlled restart; durable records remain if shutdown interrupts cleanup.
        foreach (var record in _records.Values) { record.ExpiresUtc = Now; }
        try { await SweepAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            // Even an exhausted host shutdown budget must signal the worker to stop.
            await base.StopAsync(cancellationToken);
        }
    }
}
