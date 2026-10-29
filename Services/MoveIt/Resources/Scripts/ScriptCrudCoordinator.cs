using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;

namespace MoveItAutomationUI.Services.MoveIt.Resources.Scripts;

// Authoritative custom-Script definition reads and v0 writes. Inventory, Information
// updates and the MAUI editor delegate here; COM export/Undo/delete remain supported.
public interface IScriptCrudCoordinator
{
    Task<StandardScriptMetadata> ReadBuiltInMetadataAsync(string id, CancellationToken ct = default);
    Task<ScriptDefinition> ReadAsync(string id, CancellationToken ct = default);
    Task<JsonObject> ReadRawAsync(string id, CancellationToken ct, bool authorize = true);
    Task<ScriptDefinition> CreateAsync(ScriptEdit input, CancellationToken ct = default);
    Task<ScriptDefinition> SaveAsync(string id, ScriptEdit input, bool informationOnly = false, CancellationToken ct = default, Func<CancellationToken, Task>? beforeWrite = null);
    Task<IReadOnlyList<JsonObject>> GetCustomDefinitionsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<JsonObject>> GetBuiltInDefinitionsAsync(CancellationToken ct = default);
    Task<MoveItScriptConfiguration> GetNativeConfigurationAsync(string id, CancellationToken ct = default);
    Task<MoveItScriptConfiguration> RestoreNativeConfigurationAsync(string id, string xml, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
}

public sealed class ScriptCrudCoordinator(IMoveItRestClient rest, IMoveItSessionService sessions, IMemoryCache cache,
    IMoveItComClient? com = null, IMoveItObjectAccessService? access = null) : IScriptCrudCoordinator
{
    public Task<IReadOnlyList<JsonObject>> GetCustomDefinitionsAsync(CancellationToken ct = default) => rest.GetCustomScriptsAsync(ct);
    public Task<IReadOnlyList<JsonObject>> GetBuiltInDefinitionsAsync(CancellationToken ct = default) => rest.GetStandardScriptsAsync(ct);
    public async Task<StandardScriptMetadata> ReadBuiltInMetadataAsync(string id, CancellationToken ct = default)
    {
        RequireReadSession(); id = ScriptConfiguration.Id(id);
        // Unfiltered per-Script GET retains Params without enlarging every recurring inventory response.
        return StandardScriptMetadataReader.Read(await rest.GetStandardScriptAsync(id, ct), id);
    }
    private IMoveItComClient Native => com ?? throw new InvalidOperationException("The native Script support layer is unavailable.");
    public Task<MoveItScriptConfiguration> GetNativeConfigurationAsync(string id, CancellationToken ct = default)
        => Native.GetScriptConfigurationAsync(ScriptConfiguration.Id(id), ct);
    public async Task<MoveItScriptConfiguration> RestoreNativeConfigurationAsync(string id, string xml, CancellationToken ct = default)
    {
        RequireSession(); id = ScriptConfiguration.Id(id);
        if (access is not null) await access.RequireAsync("Script", id, "Edit", ct);
        // Undo is a separate workflow. Never exchange an open source editor's lease
        // for a native restore or delete; close that editor before changing its object.
        if (ObjectCheckout.HasLifetimeFor("Script", id)) throw new InvalidOperationException("Close the Script editor before restoring Script configuration.");
        try { return await ObjectCheckout.ExecuteNativeAsync("Script", id, token => Native.RestoreScriptConfigurationAsync(id, xml, token), saved => saved.Xml, ct); }
        finally { Invalidate(); }
    }
    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        RequireSession(); id = ScriptConfiguration.Id(id);
        if (access is not null) await access.RequireAsync("Script", id, "Delete", ct);
        if (ObjectCheckout.HasLifetimeFor("Script", id)) throw new InvalidOperationException("Close the Script editor before deleting the Script.");
        try { _ = await ObjectCheckout.ExecuteNativeAsync("Script", id, async token => { await Native.DeleteScriptAsync(id, token); return true; }, _ => "", ct); }
        finally { Invalidate(); }
    }
    // Built-in viewing needs an authenticated session, not the Custom Scripts license.
    public MoveItSessionState RequireReadSession()
        => sessions.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
    public MoveItSessionState RequireSession()
    {
        var session = RequireReadSession();
        var block = MoveItLicensePolicy.GetCustomScriptBlock(session);
        if (!string.IsNullOrWhiteSpace(block)) throw new InvalidOperationException(block);
        return session;
    }
    public void Invalidate() => cache.Remove(MoveItServiceCache.UserScopedKey("MoveItAutomationUI:Scripts:", sessions));
    public async Task<JsonObject> ReadRawAsync(string id, CancellationToken ct, bool authorize = true)
    {
        RequireSession(); id = ScriptConfiguration.Id(id);
        var script = ScriptConfiguration.Unwrap(await rest.GetCustomScriptAsync(id, ct), id);
        // Omitted permission fields follow the established resource inventory contract.
        var allowed = script["xAddEditDelete"] is null || MoveItRestClient.GetString(script, "xAddEditDelete").ToLowerInvariant() is "true" or "1";
        if (authorize && !ObjectCheckout.CanEditCheckedOut("Script", id, allowed)) throw new InvalidOperationException("This custom Script is read-only for your MOVEit account.");
        return script;
    }
    public async Task<ScriptDefinition> ReadAsync(string id, CancellationToken ct = default)
    {
        id = ScriptConfiguration.Id(id);
        return ScriptConfiguration.Model(await ReadRawAsync(id, ct), id);
    }
    private async Task UniqueAsync(string name, string? previous, CancellationToken ct)
    {
        if (string.Equals(name, previous, StringComparison.OrdinalIgnoreCase)) return;
        var custom = rest.CustomScriptNameExistsAsync(name, ct);
        var standard = rest.StandardScriptNameExistsAsync(name, ct);
        await Task.WhenAll(custom, standard);
        if (await custom || await standard) throw new ScriptNameAlreadyExistsException(name);
    }
    public async Task<ScriptDefinition> CreateAsync(ScriptEdit input, CancellationToken ct = default)
    {
        RequireSession(); var edit = ScriptConfiguration.Validate(input);
        if (access is not null) edit = edit with { ResourceGroup = await access.RequireCreationAsync("Script", edit.ResourceGroup, ct) };
        await UniqueAsync(edit.Name, null, ct);
        var id = ""; var responseReceived = false;
        try
        {
            var response = await rest.CreateCustomScriptV0Async(ScriptConfiguration.Compose(null, "", edit), ct);
            responseReceived = true;
            id = MoveItRestClient.GetString(response, "id");
            Acknowledge(response, id); id = ScriptConfiguration.Id(id);
            var saved = await ReadAsync(id, ct);
            if (!ScriptConfiguration.Matches(edit, saved)) throw new InvalidOperationException("Script read-back did not match the submitted fields.");
            return saved;
        }
        // Never replay a POST, delete a possibly created Script, or suggest another
        // creation after an ambiguous response. Retain the returned ID when available.
        catch (Exception ex) when (!responseReceived && ex is MoveItApiException api && (int?)api.StatusCode is >= 400 and < 500) { throw Rejected(api, true); }
        catch (Exception ex) { throw new ScriptWriteUncertainException(id, true, ex); }
        finally { Invalidate(); }
    }
    public async Task<ScriptDefinition> SaveAsync(string id, ScriptEdit input, bool informationOnly = false, CancellationToken ct = default, Func<CancellationToken, Task>? beforeWrite = null)
    {
        RequireSession(); id = ScriptConfiguration.Id(id);
        if (access is not null) await access.RequireAsync("Script", id, "Edit", ct);
        var edit = ScriptConfiguration.Validate(input);
        _ = await ReadRawAsync(id, ct);
        return await ObjectCheckout.ExecuteAsync("Script", id,
            token => rest.CheckoutCustomScriptV0Async(id, token), async token =>
            {
                var current = await ReadRawAsync(id, token, false);
                var currentModel = ScriptConfiguration.Model(current, id);
                ObjectEditVersion.Verify(informationOnly ? "Script" : "ScriptEditor", id,
                    informationOnly ? ObjectEditVersion.Named(currentModel.Name, currentModel.Description) : ScriptConfiguration.Revision(current));
                await UniqueAsync(edit.Name, currentModel.Name, token);
                if (informationOnly) edit = edit with { Language = currentModel.Language, Source = currentModel.Source };
                if (ScriptConfiguration.Matches(edit, currentModel)) return currentModel;
                var body = ScriptConfiguration.Compose(current, id, edit);
                if (beforeWrite is not null) await beforeWrite(token);
                var responseReceived = false;
                try
                {
                    var response = await rest.UpdateCustomScriptV0Async(id, body, token);
                    responseReceived = true;
                    Acknowledge(response, id);
                    var savedRaw = await ReadRawAsync(id, token, false);
                    var saved = ScriptConfiguration.Model(savedRaw, id);
                    if (!ScriptConfiguration.Matches(edit, saved)) throw new InvalidOperationException("Script read-back did not match the submitted fields.");
                    ObjectEditVersion.Persisted(informationOnly ? "Script" : "ScriptEditor", id,
                        informationOnly ? ObjectEditVersion.Named(saved.Name, saved.Description) : ScriptConfiguration.Revision(savedRaw));
                    return saved;
                }
                catch (Exception ex) when (!responseReceived && ex is MoveItApiException api && (int?)api.StatusCode is >= 400 and < 500) { throw Rejected(api, false); }
                catch (Exception ex) { throw new ScriptWriteUncertainException(id, false, ex); }
                finally { Invalidate(); }
            }, token => rest.ReleaseCustomScriptCheckoutV0Async(id, token), ct);
    }
    // A definitive rejection of the write may be corrected and resubmitted. A
    // later 4xx from GET is a read-back failure, never proof that PUT/POST failed.
    // Do not expose a native response which might echo user Script Source.
    private static MoveItApiException Rejected(MoveItApiException error, bool creating)
        => error is MoveItAuthenticationException ? new MoveItAuthenticationException("MOVEit session expired. Please sign in again.")
            : ObjectCheckoutRejectedException.DescribesLock(error.Message) ? new ObjectCheckoutRejectedException(error.Message)
            : new MoveItApiException($"MOVEit rejected the Script {(creating ? "creation" : "update")} (HTTP {(int?)error.StatusCode}). Check the Script fields and your MOVEit permissions before retrying.", error.StatusCode);
    internal static void Acknowledge(JsonObject response, string id)
    {
        if (MoveItRestClient.GetString(response, "status") != "success" || MoveItRestClient.GetNullableInt(response, "errorcode") != 0
            || string.IsNullOrWhiteSpace(id) || MoveItRestClient.GetString(response, "id") != id)
            throw new InvalidOperationException("MOVEit did not return a complete Script save acknowledgement.");
    }
}
