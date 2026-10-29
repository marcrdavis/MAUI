using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.Concurrency;

namespace MoveItAutomationUI.Services.MoveIt.SystemSettings;

public sealed record SystemSettingsState(SystemLoggingSettings Logging, string EditVersion)
{
    public SystemEventLogSettings? EventLog { get; init; }
    public SystemTaskSettings? Tasks { get; init; }
    public SystemStateFileSettings? StateFile { get; init; }
    public SystemTamperSettings? Tamper { get; init; }
    public SystemLockoutState? Lockout { get; init; }
}
public interface ISystemSettingsService
{
    Task<SystemSettingsState> ReadAsync(CancellationToken ct);
    Task<SystemSettingsState> RecoverCheckoutAsync(CancellationToken ct);
    Task<SystemActiveLockouts> ReadActiveLockoutsAsync(CancellationToken ct);
    Task<SystemUnlockResult> UnlockAsync(string kind, IReadOnlyList<SystemLockoutSelection> selections, CancellationToken ct);
    Task<SystemLockoutState> ReadLockoutAsync(CancellationToken ct);
    Task<SystemSettingsState> SaveAsync(SystemSettingsUpdate update, CancellationToken ct);
    Task<SystemSettingsState> SaveLoggingAsync(SystemLoggingSettings logging, string editVersion, CancellationToken ct);
}

// One Globals write boundary. Short checkouts avoid abandoned editor locks; a revision
// of the complete Globals object is checked again under checkout before any write.
public sealed class SystemSettingsService(IMoveItRestClient rest, IMoveItSessionService sessions) : ISystemSettingsService
{
    private const string Type = "MOVEit Settings";
    private const string Id = "-1";
    private const string LockoutType = "System Lockout Policy";
    private const string TrustedType = "System Trusted IPs";
    private static readonly KeyedAsyncLock Saves = new(StringComparer.OrdinalIgnoreCase);

    private MoveItSessionState RequireAdmin()
    {
        var state = sessions.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        if (state.Info?.IsAdmin != true) throw new MoveItApiException("MOVEit Settings requires a MOVEit administrator.", HttpStatusCode.Forbidden);
        return state;
    }

    private static JsonObject Globals(JsonObject response) => response["Globals"] as JsonObject
        ?? throw new InvalidOperationException("MOVEit did not return a complete MOVEit Settings definition.");
    private static SystemSettingsState State(JsonObject globals)
        => new(SystemLoggingSettings.Read(globals), ObjectEditVersion.Create(Type, Id, globals))
        {
            EventLog = SystemEventLogSettings.Read(globals), Tasks = SystemTaskSettings.Read(globals),
            StateFile = SystemStateFileSettings.Read(globals), Tamper = SystemTamperSettings.Read(globals)
        };

    public async Task<SystemSettingsState> ReadAsync(CancellationToken ct)
        => await ReadCheckedOutAsync(force: false, ct);

    public async Task<SystemSettingsState> RecoverCheckoutAsync(CancellationToken ct)
        => await ReadCheckedOutAsync(force: true, ct);

    private async Task<SystemSettingsState> ReadCheckedOutAsync(bool force, CancellationToken ct)
    {
        var session = RequireAdmin();
        using var server = ObjectCheckout.ForServer(session.WebAdminUrl);
        JsonObject? response = null;
        return await ObjectCheckout.ReadAsync(Type, Id,
            async token => { response = force ? await rest.ForceCheckoutObjectV0Async(Type, Id, token) : await rest.CheckoutGlobalsV0Async(token); },
            _ => Task.FromResult(State(Globals(response!))), rest.ReleaseGlobalsCheckoutV0Async, ct);
    }

    public async Task<SystemLockoutState> ReadLockoutAsync(CancellationToken ct)
    {
        RequireAdmin();
        var current = await rest.GetSystemLockoutPolicyV0Async(ct);
        var policy = SystemLockoutPolicy.Read(current);
        var trusted = await rest.GetSystemTrustedIPsV0Async(ct);
        return new(policy, ObjectEditVersion.Create(LockoutType, Id, current), SystemTrustedIPs.Read(trusted), ObjectEditVersion.Create(TrustedType, Id, SystemTrustedIPs.Definition(trusted)));
    }

    public Task<SystemSettingsState> SaveLoggingAsync(SystemLoggingSettings logging, string editVersion, CancellationToken ct)
        => SaveAsync(new SystemSettingsUpdate(editVersion, Logging: logging), ct);

    public async Task<SystemSettingsState> SaveAsync(SystemSettingsUpdate update, CancellationToken ct)
    {
        update.Validate(); // Validate every requested domain before the first mutation.
        var session = RequireAdmin();
        using var gate = await Saves.AcquireAsync(session.WebAdminUrl.TrimEnd('/'), ct);
        async Task<JsonObject> CurrentPolicy()
        {
            var current = await rest.GetSystemLockoutPolicyV0Async(ct);
            if (string.IsNullOrWhiteSpace(update.LockoutVersion) || update.LockoutVersion != ObjectEditVersion.Create(LockoutType, Id, current))
                throw new ObjectEditConflictException(LockoutType);
            return current;
        }
        async Task<JsonObject> CurrentTrusted()
        {
            var current = await rest.GetSystemTrustedIPsV0Async(ct);
            if (string.IsNullOrWhiteSpace(update.TrustedVersion) || update.TrustedVersion != ObjectEditVersion.Create(TrustedType, Id, SystemTrustedIPs.Definition(current)))
                throw new ObjectEditConflictException(TrustedType);
            SystemTrustedIPs.Read(current);
            return current;
        }
        if (update.Lockout is not null) await CurrentPolicy(); // Reject stale revisions before Globals can commit.
        if (update.TrustedIPs is not null) await CurrentTrusted();
        var state = update.HasGlobals ? await SaveGlobalsAsync(update, ct) : await ReadAsync(ct);
        if (update.Lockout is null && update.TrustedIPs is null) return state;
        var nativeMutationAttempted = false;
        try
        {
            // Policy/list endpoints have no captured checkout operation. Serialize MAUI writers
            // and recheck both revisions immediately before this sequence of native mutations.
            var currentPolicy = update.Lockout is not null ? await CurrentPolicy() : null;
            var currentTrusted = update.TrustedIPs is not null ? await CurrentTrusted() : null;
            IReadOnlyList<string> before = currentTrusted is not null ? SystemTrustedIPs.Read(currentTrusted) : [];
            var desired = update.TrustedIPs ?? [];
            var oldByAddress = before.ToDictionary(SystemTrustedIPs.Normalize, StringComparer.OrdinalIgnoreCase);
            var newByAddress = desired.ToDictionary(SystemTrustedIPs.Normalize, StringComparer.OrdinalIgnoreCase);
            // Add exemptions before enabling policies or removing old exemptions. Keep the native
            // spelling when deleting existing addresses; each supplied capture changes one address.
            foreach (var address in newByAddress.Where(pair => !oldByAddress.ContainsKey(pair.Key)).Select(pair => pair.Value))
            {
                nativeMutationAttempted = true;
                await rest.AddSystemTrustedIPV0Async(address, ct);
            }
            if (update.Lockout is not null)
            {
                nativeMutationAttempted = true;
                await rest.UpdateSystemLockoutPolicyV0Async(update.Lockout.Apply(currentPolicy!), ct);
            }
            foreach (var address in oldByAddress.Where(pair => !newByAddress.ContainsKey(pair.Key)).Select(pair => pair.Value))
            {
                nativeMutationAttempted = true;
                await rest.RemoveSystemTrustedIPV0Async(address, ct);
            }
            var saved = await ReadLockoutAsync(ct);
            if (update.Lockout is not null && saved.Policy != update.Lockout
                || update.TrustedIPs is not null && !SystemTrustedIPs.Same(saved.TrustedIPs, desired))
                throw new SystemSettingsSaveUncertainException();
            return state with { Lockout = saved };
        }
        catch (Exception ex) when (update.HasGlobals || nativeMutationAttempted)
        {
            // Globals, policy and individual IP changes are separate transactions. Never replay
            // or pretend to roll back an accepted/ambiguous mutation after a later failure.
            throw new SystemSettingsPartialSaveException(ex);
        }
    }

    public async Task<SystemActiveLockouts> ReadActiveLockoutsAsync(CancellationToken ct)
    {
        RequireAdmin();
        var addresses = rest.GetSystemLockoutsV1Async("ip", ct);
        var users = rest.GetSystemLockoutsV1Async("user", ct);
        await Task.WhenAll(addresses, users);
        return new(SystemActiveLockoutDefinition.Read("ip", await addresses), SystemActiveLockoutDefinition.Read("user", await users));
    }

    public async Task<SystemUnlockResult> UnlockAsync(string kind, IReadOnlyList<SystemLockoutSelection> selections, CancellationToken ct)
    {
        SystemActiveLockoutDefinition.Field(kind); // Fixed native endpoint allowlist, never a browser-supplied URL.
        if (selections is null || selections.Count == 0 || selections.Any(item => item is null
            || string.IsNullOrWhiteSpace(item.Value) || item.Value.Any(char.IsControl) || string.IsNullOrWhiteSpace(item.EditVersion))
            || selections.Select(item => item.Value).Distinct(StringComparer.Ordinal).Count() != selections.Count)
            throw new ArgumentException("Select one or more lockouts to unlock.");
        var session = RequireAdmin();
        using var gate = await Saves.AcquireAsync(session.WebAdminUrl.TrimEnd('/'), ct);
        var current = SystemActiveLockoutDefinition.Read(kind, await rest.GetSystemLockoutsV1Async(kind, ct));
        var selected = selections.ToDictionary(item => item.Value, StringComparer.Ordinal);
        var present = current.Where(item => selected.ContainsKey(item.Value)).ToArray();
        // A new lockout for the same identity must be reviewed rather than unlocked using an old selection.
        if (present.Any(item => item.EditVersion != selected[item.Value].EditVersion)) throw new SystemActiveLockoutConflictException();
        var alreadyUnlocked = selections.Count - present.Length;
        if (present.Length == 0) return new(kind, current, 0, alreadyUnlocked);
        try { await rest.UnlockSystemLockoutsV1Async(kind, present.Select(item => item.Value).ToArray(), ct); }
        catch (Exception ex) when (IsAmbiguous(ex)) { throw new SystemUnlockUncertainException(ex); }
        try
        {
            var saved = SystemActiveLockoutDefinition.Read(kind, await rest.GetSystemLockoutsV1Async(kind, ct));
            if (saved.Any(item => selected.ContainsKey(item.Value))) throw new SystemUnlockUncertainException();
            return new(kind, saved, present.Length, alreadyUnlocked);
        }
        catch (SystemUnlockUncertainException) { throw; }
        catch (Exception ex) { throw new SystemUnlockUncertainException(ex); }
    }

    private static bool IsAmbiguous(Exception ex) => ex is HttpRequestException or OperationCanceledException or JsonException
        || ex is MoveItApiException api && ex is not MoveItAuthenticationException
            && (api.StatusCode is null || (int)api.StatusCode >= 500 || api.StatusCode == HttpStatusCode.RequestTimeout);

    private async Task<SystemSettingsState> SaveGlobalsAsync(SystemSettingsUpdate update, CancellationToken ct)
    {
        var session = RequireAdmin();
        using var server = ObjectCheckout.ForServer(session.WebAdminUrl);
        using var revision = ObjectEditVersion.Require(Type, Id, update.EditVersion);
        JsonObject? response = null;
        var accepted = false;
        var ownsCheckout = false;
        async Task ReleaseOwned(CancellationToken token)
        {
            if (!ownsCheckout) return;
            await rest.ReleaseGlobalsCheckoutV0Async(token);
            ownsCheckout = false;
        }
        try
        {
            return await ObjectCheckout.ExecuteAsync(Type, Id,
                async token => { response = await rest.CheckoutGlobalsV0Async(token); ownsCheckout = true; },
                async token =>
                {
                    var current = Globals(response!);
                    ObjectEditVersion.Verify(Type, Id, current);
                    var submitted = update.Apply(current);
                    JsonObject acknowledgement;
                    try { acknowledgement = await rest.UpdateGlobalsV0Async(new JsonObject { ["Globals"] = submitted }, token); }
                    catch (Exception ex) when (IsAmbiguous(ex))
                    { throw new SystemSettingsSaveUncertainException(ex); }
                    accepted = true;
                    if (MoveItRestClient.GetString(acknowledgement, "status") != "success"
                        || MoveItRestClient.GetNullableInt(acknowledgement, "errorcode") != 0
                        || MoveItRestClient.GetString(acknowledgement, "id") != Id)
                        throw new SystemSettingsSaveUncertainException();
                    // PUT may retain or end checkout, depending on the native resource/build.
                    // Explicitly finish this write checkout before requesting a fresh readback.
                    // A rejected readback checkout is never followed by release of another owner's lock.
                    using (var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                        await ReleaseOwned(cleanup.Token);
                    var savedResponse = await rest.CheckoutGlobalsV0Async(token);
                    ownsCheckout = true;
                    var saved = Globals(savedResponse);
                    if (!update.Matches(saved)) throw new SystemSettingsSaveUncertainException();
                    ObjectEditVersion.Persisted(Type, Id, saved);
                    return State(saved);
                }, ReleaseOwned, ct);
        }
        catch (SystemSettingsSaveUncertainException) { throw; }
        catch (Exception ex) when (accepted) { throw new SystemSettingsSaveUncertainException(ex); }
    }
}

public sealed class SystemSettingsSaveUncertainException(Exception? inner = null)
    : InvalidOperationException("The MOVEit Settings save result could not be confirmed. Reload MOVEit Settings and review the MOVEit copy before saving again.", inner);

public sealed class SystemSettingsPartialSaveException(Exception inner)
    : InvalidOperationException("Some changes may have been saved. Reload MOVEit Settings and review the MOVEit copy before saving again.", inner);
