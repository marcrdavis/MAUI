// Common save-scoped checkout lifecycle. Never force a checkout or release one we did not acquire.
namespace MoveItAutomationUI.Services.MoveIt;

public static class ObjectCheckout
{
    public const string RequestFailedMessage = "Unable to complete the MOVEit checkout request. Please try again.";
    public const string OverrideFailedMessage = "Unable to override the checkout. Please try again.";
    public const string ReleaseFailedMessage = "Unable to release the checkout. Please try again.";
    public const string EditorEndedMessage = "This editing session has ended. Reload the object before saving.";

    internal static bool IsDefinitiveRejection(Exception error)
        => error is ObjectCheckoutRejectedException or MoveItAuthenticationException
            || error is MoveItApiException api && (int?)api.StatusCode is >= 400 and < 500 and not 408 and not 429;

    internal static bool IsRequestFailure(Exception error)
        => error is HttpRequestException or IOException or TimeoutException or OperationCanceledException
            || error is MoveItApiException api && (api.StatusCode is null || (int?)api.StatusCode is >= 500 or 408 or 429);

    private static readonly object Sync = new();
    private static readonly Dictionary<string, Gate> Gates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> Reservations = new(StringComparer.OrdinalIgnoreCase);
    private static readonly AsyncLocal<string?> Server = new();
    private static readonly AsyncLocal<IObjectCheckoutLifetime?> Lifetime = new();
    private static readonly AsyncLocal<RecoveryContext?> Recovery = new();
    private sealed record RecoveryContext(IShortCheckoutRecovery Journal, MoveItSessionState Session);

    public static IDisposable ForRecovery(IShortCheckoutRecovery journal, MoveItSessionState session)
    {
        var previous = Recovery.Value;
        var server = ForServer(session.WebAdminUrl);
        Recovery.Value = new(journal, session);
        return new Lease(() => { Recovery.Value = previous; server.Dispose(); });
    }

    internal static string Key(string type, string id) => (Server.Value ?? "") + "|" + type + ":" + id.Trim();
    public static IDisposable ForServer(string server)
    {
        var previous = Server.Value; Server.Value = server.TrimEnd('/');
        return new Lease(() => Server.Value = previous);
    }
    public static IDisposable ForLifetime(IObjectCheckoutLifetime? lifetime)
    {
        var previous = Lifetime.Value; Lifetime.Value = lifetime;
        return new Lease(() => Lifetime.Value = previous);
    }
    public static bool HasLifetimeFor(string type, string id)
    {
        lock (Sync) return Lifetime.Value is { } lifetime && Reservations.GetValueOrDefault(Key(type, id)) == lifetime.Owner;
    }
    // Native availability flags can become false while checkout is held. A verified lease
    // was authorized before acquisition; native PUT/COM still enforces the account's ACL.
    public static bool CanEditCheckedOut(string type, string id, bool nativePermission)
        => nativePermission || HasLifetimeFor(type, id);
    public static void VerifyNativeLifetimeRevision(string type, string id, string xml)
        => Lifetime.Value?.VerifyNativeRevision(type, id, xml);
    public static async Task<T> ExecuteNativeAsync<T>(string type, string id, Func<CancellationToken, Task<T>> edit,
        Func<T, string> savedXml, CancellationToken ct)
    {
        using var serial = await EnterAsync(Key(type, id), ct);
        string? owner;
        lock (Sync) owner = Reservations.GetValueOrDefault(Key(type, id));
        if (owner is null) return await edit(ct);
        if (Lifetime.Value?.Owner != owner) throw new InvalidOperationException("This object is reserved by an open MAUI editor.");
        return await Lifetime.Value.ExecuteNativeAsync(type, id, edit, savedXml, ct);
    }

    internal static async Task<IDisposable> ReserveAsync(string type, string id, string owner, CancellationToken ct, string? replacingOwner = null)
    {
        var key = Key(type, id);
        using var serial = await EnterAsync(key, ct);
        lock (Sync)
        {
            if (Reservations.TryGetValue(key, out var current) && current != replacingOwner)
                throw new InvalidOperationException("This object is already open in another MAUI editor.");
            Reservations[key] = owner;
        }
        return new Lease(() => { lock (Sync) { if (Reservations.GetValueOrDefault(key) == owner) Reservations.Remove(key); } });
    }

    public static async Task<T> ExecuteAsync<T>(string objectType, string id, Func<CancellationToken, Task> acquire,
        Func<CancellationToken, Task<T>> edit, Func<CancellationToken, Task> release, CancellationToken cancellationToken)
        => await ExecuteCoreAsync(objectType, id, acquire, edit, release, cancellationToken, readOnly: false);

    // Browsing borrows the verified opening checkout without the release/reacquire cycle used for writes.
    public static async Task<T> ReadAsync<T>(string objectType, string id, Func<CancellationToken, Task> acquire,
        Func<CancellationToken, Task<T>> read, Func<CancellationToken, Task> release, CancellationToken cancellationToken,
        Func<TimeSpan>? cleanupTimeout = null)
        => await ExecuteCoreAsync(objectType, id, acquire, read, release, cancellationToken, readOnly: true, cleanupTimeout: cleanupTimeout);

    private static async Task<T> ExecuteCoreAsync<T>(string objectType, string id, Func<CancellationToken, Task> acquire,
        Func<CancellationToken, Task<T>> edit, Func<CancellationToken, Task> release, CancellationToken cancellationToken, bool readOnly,
        Func<TimeSpan>? cleanupTimeout = null)
    {
        var key = Key(objectType, id);
        using var serial = await EnterAsync(key, cancellationToken);
        string? reservation;
        lock (Sync) reservation = Reservations.GetValueOrDefault(key);
        if (reservation is not null)
        {
            if (Lifetime.Value?.Owner != reservation)
                throw new InvalidOperationException("This object is reserved by an open MAUI editor. Close that editor before performing this operation.");
            if (readOnly) return await edit(cancellationToken);
            return await Lifetime.Value.ExecuteAsync(objectType, id, edit, cancellationToken);
        }
        var recovery = Recovery.Value;
        var record = recovery is null ? null : await recovery.Journal.BeginAsync(objectType, id, recovery.Session, cancellationToken);
        var acquired = false;
        Exception? failure = null;
        try
        {
            try { await acquire(cancellationToken); acquired = true; }
            catch (Exception ex)
            {
                if (record is not null)
                {
                    if (IsDefinitiveRejection(ex))
                        await recovery!.Journal.CompleteAsync(record); // Native rejection acquired nothing.
                    else
                    {
                        await recovery!.Journal.RequestFailedAsync(record);
                        throw new InvalidOperationException(RequestFailedMessage, ex);
                    }
                }
                throw;
            }
            if (record is not null) await recovery!.Journal.AcquiredAsync(record, recovery.Session, cancellationToken);
            return await edit(cancellationToken);
        }
        catch (Exception ex) when (acquired && (ex is MoveItApiException or MoveItComException)
            && ObjectCheckoutRejectedException.DescribesLock(ex.Message))
        {
            // Another editor took ownership after acquisition. Retire our local intent
            // without DELETE, which could release that editor's newer checkout.
            acquired = false;
            if (record is not null) await recovery!.Journal.CompleteAsync(record);
            throw new ObjectCheckoutRejectedException(ex.Message);
        }
        catch (Exception ex) { failure = ex; throw; }
        finally
        {
            if (acquired)
            {
                // Request cancellation must not cancel checkout cleanup. Ambiguous ownership is
                // journaled, never resolved by blindly repeating POST/DELETE in the background.
                // Host diagnostics may still be finishing natively after their HTTP request is cancelled.
                // Their adapter supplies the native operation budget; ordinary writes retain 15 seconds.
                using var cleanup = new CancellationTokenSource(cleanupTimeout?.Invoke() ?? TimeSpan.FromSeconds(15));
                try
                {
                    await release(cleanup.Token);
                    if (record is not null) await recovery!.Journal.CompleteAsync(record);
                }
                catch (Exception ex)
                {
                    if (record is not null) await recovery!.Journal.RequestFailedAsync(record);
                    throw new ObjectCheckoutReleaseException(objectType, id,
                        failure is null ? ex : new AggregateException(failure, ex));
                }
            }
        }
    }

    // Native ModifyItem/PUT may already have released the checkout. Only the documented
    // native error code proves this desired cleanup state; HTTP conflicts alone do not.
    public static bool IsReleased(bool successful, int? nativeErrorCode)
        => successful || nativeErrorCode == 4015;

    private static async Task<IDisposable> EnterAsync(string key, CancellationToken ct)
    {
        Gate gate;
        lock (Sync)
        {
            if (!Gates.TryGetValue(key, out gate!)) Gates[key] = gate = new Gate();
            gate.Users++;
        }
        try { await gate.Semaphore.WaitAsync(ct); }
        catch { Remove(key, gate, false); throw; }
        return new Lease(() => Remove(key, gate, true));
    }
    private static void Remove(string key, Gate gate, bool release)
    {
        lock (Sync)
        {
            if (release) gate.Semaphore.Release();
            if (--gate.Users == 0) { Gates.Remove(key); gate.Semaphore.Dispose(); }
        }
    }
    private sealed class Gate { public readonly SemaphoreSlim Semaphore = new(1, 1); public int Users; }
    private sealed class Lease(Action release) : IDisposable { public void Dispose() => release(); }
}

public interface IObjectCheckoutLifetime
{
    string Owner { get; }
    Task<T> ExecuteAsync<T>(string type, string id, Func<CancellationToken, Task<T>> edit, CancellationToken ct);
    Task<T> ExecuteNativeAsync<T>(string type, string id, Func<CancellationToken, Task<T>> edit, Func<T, string> savedXml, CancellationToken ct);
    void VerifyNativeRevision(string type, string id, string xml);
}

public sealed class ObjectEditLeaseException(string message, bool mayHaveSaved = false) : InvalidOperationException(message)
{
    public bool MayHaveSaved { get; } = mayHaveSaved;
}

// A native rejection inside an HTTP-success response is definitive: no lease was acquired.
public sealed class ObjectCheckoutRejectedException(string message) : MoveItApiException(NormalizeMessage(message))
{
    public static string NormalizeMessage(string message)
    {
        const string native = "This section has been checked out to ";
        var position = message.IndexOf(native, StringComparison.OrdinalIgnoreCase);
        return position < 0 ? message : "This object has been checked out to " + message[(position + native.Length)..];
    }
    // Use native text when MOVEit supplies owner/time; never infer ownership from a username alone.
    public static bool DescribesLock(string message)
        // Native 4015 says "This section is not checked out". It describes an
        // unlocked object and must never turn a failed request into an override prompt.
        => message.Contains("checked out", StringComparison.OrdinalIgnoreCase)
            && !message.Contains("not checked out", StringComparison.OrdinalIgnoreCase)
            && !message.Contains("not been checked out", StringComparison.OrdinalIgnoreCase);
    public object Feedback() => new { message = Message, checkoutConflict = DescribesLock(Message), moveItSaved = false };
}

public sealed class ObjectCheckoutReleaseException : InvalidOperationException
{
    public ObjectCheckoutReleaseException(string objectType, string id, Exception inner)
        : base($"Unable to release the {objectType} checkout. Reload the {objectType} before saving again.", inner) { }
}
