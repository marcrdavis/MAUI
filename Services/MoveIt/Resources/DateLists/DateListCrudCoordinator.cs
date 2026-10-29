using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;

namespace MoveItAutomationUI.Services.MoveIt.Resources.DateLists;

// Authoritative Date List definition reads and v0 writes. Inventory, Information
// updates and the MAUI editor delegate here; COM export/Undo/delete remain supported.
public interface IDateListCrudCoordinator
{
    Task<JsonObject> ReadRawAsync(string id, CancellationToken ct, bool authorize = true);
    Task<DateListDefinition> ReadAsync(string id, CancellationToken ct = default);
    Task<DateListDefinition> CreateAsync(DateListEdit input, CancellationToken ct = default);
    Task<DateListDefinition> SaveAsync(string id, DateListEdit input, bool informationOnly = false, CancellationToken ct = default, Func<CancellationToken, Task>? beforeWrite = null);
    Task<IReadOnlyList<JsonObject>> GetDefinitionsAsync(CancellationToken ct = default);
    Task<MoveItDateListConfiguration> GetNativeConfigurationAsync(string id, CancellationToken ct = default);
    Task<MoveItDateListConfiguration> RestoreNativeConfigurationAsync(string id, string xml, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
}

public sealed class DateListCrudCoordinator(IMoveItRestClient rest, IMoveItSessionService sessions, IMemoryCache cache,
    IMoveItComClient? com = null, IMoveItObjectAccessService? access = null) : IDateListCrudCoordinator
{
    public Task<IReadOnlyList<JsonObject>> GetDefinitionsAsync(CancellationToken ct = default) => rest.GetDateListsAsync(ct);
    private IMoveItComClient Native => com ?? throw new InvalidOperationException("The native Date List support layer is unavailable.");
    public Task<MoveItDateListConfiguration> GetNativeConfigurationAsync(string id, CancellationToken ct = default)
        => Native.GetDateListConfigurationAsync(DateListConfiguration.Id(id), ct);
    public async Task<MoveItDateListConfiguration> RestoreNativeConfigurationAsync(string id, string xml, CancellationToken ct = default)
    {
        RequireSession(); id = DateListConfiguration.Id(id);
        if (access is not null) await access.RequireAsync("DateList", id, "Edit", ct);
        // Undo is a separate workflow. Never exchange an open Date List editor's lease
        // for a native restore or delete; close that editor before changing its object.
        if (ObjectCheckout.HasLifetimeFor("DateList", id)) throw new InvalidOperationException("Close the Date List editor before restoring Date List configuration.");
        try { return await ObjectCheckout.ExecuteNativeAsync("DateList", id, token => Native.RestoreDateListConfigurationAsync(id, xml, token), saved => saved.Xml, ct); }
        finally { Invalidate(); }
    }
    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        RequireSession(); id = DateListConfiguration.Id(id);
        if (access is not null) await access.RequireAsync("DateList", id, "Delete", ct);
        if (ObjectCheckout.HasLifetimeFor("DateList", id)) throw new InvalidOperationException("Close the Date List editor before deleting the Date List.");
        try { _ = await ObjectCheckout.ExecuteNativeAsync("DateList", id, async token => { await Native.DeleteDateListAsync(id, token); return true; }, _ => "", ct); }
        finally { Invalidate(); }
    }
    public MoveItSessionState RequireReadSession()
        => sessions.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
    public MoveItSessionState RequireSession() => RequireReadSession();
    public void Invalidate() => cache.Remove(MoveItServiceCache.UserScopedKey("MoveItAutomationUI:DateLists:", sessions));
    public async Task<JsonObject> ReadRawAsync(string id, CancellationToken ct, bool authorize = true)
    {
        RequireSession(); id = DateListConfiguration.Id(id);
        var list = DateListConfiguration.Unwrap(await rest.GetDateListAsync(id, ct), id);
        // Omitted permission fields follow the established resource inventory contract.
        var allowed = list["xAddEditDelete"] is null || MoveItRestClient.GetString(list, "xAddEditDelete").ToLowerInvariant() is "true" or "1";
        if (authorize && !ObjectCheckout.CanEditCheckedOut("DateList", id, allowed)) throw new InvalidOperationException("This Date List is read-only for your MOVEit account.");
        return list;
    }
    public async Task<DateListDefinition> ReadAsync(string id, CancellationToken ct = default)
    {
        id = DateListConfiguration.Id(id);
        return DateListConfiguration.Model(await ReadRawAsync(id, ct), id);
    }
    private async Task UniqueAsync(string name, string? previous, CancellationToken ct)
    {
        if (string.Equals(name, previous, StringComparison.OrdinalIgnoreCase)) return;
        if (await rest.DateListNameExistsAsync(name, ct)) throw new DateListNameAlreadyExistsException(name);
    }
    public async Task<DateListDefinition> CreateAsync(DateListEdit input, CancellationToken ct = default)
    {
        RequireSession(); var edit = DateListConfiguration.Validate(input);
        // Keep service/import callers under the same creation policy as New forms.
        if (access is not null) await access.RequireCreationAsync("DateList", null, ct);
        await UniqueAsync(edit.Name, null, ct);
        var id = ""; var responseReceived = false;
        try
        {
            var response = await rest.CreateDateListV0Async(DateListConfiguration.Compose(null, "", edit), ct);
            responseReceived = true;
            id = MoveItRestClient.GetString(response, "id");
            Acknowledge(response, id); id = DateListConfiguration.Id(id);
            var saved = await ReadAsync(id, ct);
            if (!DateListConfiguration.Matches(edit, saved)) throw new InvalidOperationException("Date List read-back did not match the submitted fields.");
            return saved;
        }
        // Never replay a POST, delete a possibly created DateList, or suggest another
        // creation after an ambiguous response. Retain the returned ID when available.
        catch (Exception ex) when (!responseReceived && ex is MoveItApiException api && (int?)api.StatusCode is >= 400 and < 500) { throw Rejected(api, true); }
        catch (Exception ex) { throw new DateListWriteUncertainException(id, true, ex); }
        finally { Invalidate(); }
    }
    public async Task<DateListDefinition> SaveAsync(string id, DateListEdit input, bool informationOnly = false, CancellationToken ct = default, Func<CancellationToken, Task>? beforeWrite = null)
    {
        RequireSession(); id = DateListConfiguration.Id(id);
        if (access is not null) await access.RequireAsync("DateList", id, "Edit", ct);
        var edit = DateListConfiguration.Validate(input);
        _ = await ReadRawAsync(id, ct);
        return await ObjectCheckout.ExecuteAsync("DateList", id,
            token => rest.CheckoutDateListV0Async(id, token), async token =>
            {
                var current = await ReadRawAsync(id, token, false);
                var currentModel = DateListConfiguration.Model(current, id);
                ObjectEditVersion.Verify(informationOnly ? "DateList" : "DateListEditor", id,
                    informationOnly ? ObjectEditVersion.Named(currentModel.Name, currentModel.Description) : DateListConfiguration.Revision(current));
                await UniqueAsync(edit.Name, currentModel.Name, token);
                if (informationOnly) edit = edit with { Entries = currentModel.Entries };
                if (DateListConfiguration.Matches(edit, currentModel)) return currentModel;
                var body = DateListConfiguration.Compose(current, id, edit);
                if (beforeWrite is not null) await beforeWrite(token);
                var responseReceived = false;
                try
                {
                    var response = await rest.UpdateDateListV0Async(id, body, token);
                    responseReceived = true;
                    Acknowledge(response, id);
                    var savedRaw = await ReadRawAsync(id, token, false);
                    var saved = DateListConfiguration.Model(savedRaw, id);
                    if (!DateListConfiguration.Matches(edit, saved)) throw new InvalidOperationException("Date List read-back did not match the submitted fields.");
                    ObjectEditVersion.Persisted(informationOnly ? "DateList" : "DateListEditor", id,
                        informationOnly ? ObjectEditVersion.Named(saved.Name, saved.Description) : DateListConfiguration.Revision(savedRaw));
                    return saved;
                }
                catch (Exception ex) when (!responseReceived && ex is MoveItApiException api && (int?)api.StatusCode is >= 400 and < 500) { throw Rejected(api, false); }
                catch (Exception ex) { throw new DateListWriteUncertainException(id, false, ex); }
                finally { Invalidate(); }
            }, token => rest.ReleaseDateListCheckoutV0Async(id, token), ct);
    }
    // A definitive rejection of the write may be corrected and resubmitted. A
    // later 4xx from GET is a read-back failure, never proof that PUT/POST failed.
    // Do not expose a native response which might echo user Date List entries.
    private static MoveItApiException Rejected(MoveItApiException error, bool creating)
        => error is MoveItAuthenticationException ? new MoveItAuthenticationException("MOVEit session expired. Please sign in again.")
            : ObjectCheckoutRejectedException.DescribesLock(error.Message) ? new ObjectCheckoutRejectedException(error.Message)
            : new MoveItApiException($"MOVEit rejected the Date List {(creating ? "creation" : "update")} (HTTP {(int?)error.StatusCode}). Check the Date List fields and your MOVEit permissions before retrying.", error.StatusCode);
    internal static void Acknowledge(JsonObject response, string id)
    {
        if (MoveItRestClient.GetString(response, "status") != "success" || MoveItRestClient.GetNullableInt(response, "errorcode") != 0
            || string.IsNullOrWhiteSpace(id) || MoveItRestClient.GetString(response, "id") != id)
            throw new InvalidOperationException("MOVEit did not return a complete Date List save acknowledgement.");
    }
}
