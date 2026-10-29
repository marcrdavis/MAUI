using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Features.Editors.Scripts;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Resources.Scripts;

namespace MoveItAutomationUI.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ScriptEditorController(ScriptCrudCoordinator scripts, ScriptTemplateService templates,
    IScriptUndoService undo, IMetadataRepository metadata, IAutomationScriptService inventory,
    IMetadataValueValidationService validation, IUserPreferencesService preferences, IOptionsSnapshot<MetadataUiOptions> metadataOptions,
    IOptionsSnapshot<UiOptions> ui, ILogger<ScriptEditorController> logger, ScriptMetadataSaveService metadataSaves) : Controller
{
    [HttpGet]
    [MoveItObjectPermission("Script", "Open", "id")]
    public async Task<IActionResult> Shell(string id, CancellationToken ct)
    {
        try
        {
            var session = scripts.RequireReadSession();
            var identity = await inventory.GetScriptAsync(ScriptConfiguration.Id(id), ct);
            var definition = identity.IsBuiltIn ? null : ReadDefinitionAsync(identity.Id, ct);
            var references = inventory.GetAssignedTasksAsync(identity.Id, ct);
            var local = metadata.GetObjectMetadataAsync(session.GetEnvironmentKey(), "Script", identity.Id, ct);
            var reads = new List<Task> { references, local };
            if (definition is not null) reads.Add(definition);
            await Task.WhenAll(reads);
            var record = await local;
            // Built-ins expose Properties/Tasks only: never read custom Source, acquire
            // checkout, seed templates or advertise native write/Undo capabilities.
            object script = identity.IsBuiltIn ? new { id = identity.Id, name = identity.Name, description = identity.Description,
                language = identity.LanguageCode, languageLabel = identity.Language, source = "", editVersion = "", resourceGroups = identity.Group } : await definition!;
            // Read-only configuration locks the whole editor, including MAUI metadata.
            var canUndo = false;
            var permissionReadOnly = identity.IsBuiltIn || (HttpContext?.RequestServices?.GetService<IMoveItObjectAccessService>() is { } access
                && !(await access.GetObjectAsync("Script", identity.Id, ct)).CanEdit);
            return Json(new { success = true, script, canUndo, readOnly = identity.IsBuiltIn, permissionReadOnly, tasks = await references,
                metadata = record.Values, fields = metadataOptions.Value.Scripts.Fields, audit = Audit(record),
                preferences = await preferences.GetCurrentScriptEditorPreferencesAsync(ct),
                checkoutObjectsForDurationOfEdit = !identity.IsBuiltIn && !permissionReadOnly && ui.Value.CheckoutObjectsForDurationOfEdit.Value });
        }
        catch (MoveItAuthenticationException) { return Unauthorized(new { message = "MOVEit session expired. Please sign in again." }); }
        catch (Exception ex) when (ex is InvalidOperationException or MoveItApiException or ArgumentException)
        { return BadRequest(new { message = ex.Message }); }
    }
    private async Task<ScriptDefinition> ReadDefinitionAsync(string id, CancellationToken ct)
        => ScriptConfiguration.Model(await scripts.ReadRawAsync(id, ct, false), id);

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(16 * 1024 * 1024)]
    [MoveItObjectPermission("Script", "Save", "id")]
    public async Task<IActionResult> Save(string? id, [FromBody] ScriptEditorSaveRequest? request, CancellationToken ct)
    {
        if (request is null) return BadRequest(new { message = "Script data is required." });
        var saved = false;
        try
        {
            var session = scripts.RequireSession();
            var creating = string.IsNullOrWhiteSpace(id);
            if (!creating && (await inventory.GetScriptAsync(ScriptConfiguration.Id(id), ct)).IsBuiltIn)
                return BadRequest(new { message = "Built-in MOVEit Scripts are read-only.", moveItSaved = false });
            var values = creating ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : await validation.FilterAndValidateAsync(session.GetEnvironmentKey(), metadataOptions.Value.Scripts, request.Metadata, ct);
            var moveItChanged = creating;
            ScriptUndoSnapshot? undoSnapshot = null;
            var definition = request.Definition();
            using var revision = string.IsNullOrWhiteSpace(id) ? null : ObjectEditVersion.Require("ScriptEditor", id, request.EditVersion);
            var script = string.IsNullOrWhiteSpace(id) ? await scripts.CreateAsync(definition, ct)
                : await scripts.SaveAsync(id, definition, ct: ct, beforeWrite: async token => { undoSnapshot = await undo.CaptureAsync(id, session.Username, token); moveItChanged = true; });
            saved = true;
            try
            {
                if (creating) await metadata.RecordObjectCreatedAsync(session.GetEnvironmentKey(), "Script", script.Id, session.Username, ct);
                else
                {
                    await metadata.SaveObjectMetadataAsync(session.GetEnvironmentKey(), "Script", script.Id, values, session.Username, ct);
                    if (moveItChanged) await metadata.RecordObjectConfigurationModifiedAsync(session.GetEnvironmentKey(), "Script", script.Id, session.Username, ct);
                }
                var local = await metadata.GetObjectMetadataAsync(session.GetEnvironmentKey(), "Script", script.Id, ct);
                var canUndo = undoSnapshot is not null;
                return Json(new { success = true, script, canUndo, undoSnapshotId = undoSnapshot?.SnapshotId ?? "", metadata = local.Values, audit = Audit(local), warning = "",
                    checkoutObjectsForDurationOfEdit = ui.Value.CheckoutObjectsForDurationOfEdit.Value });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Script {ScriptId} local metadata/audit save could not be confirmed.", script.Id);
                return BadRequest(new { message = moveItChanged
                    ? "The Script was saved in MOVEit, but MAUI could not confirm its metadata/audit save. Reload Script before saving again."
                    : "MAUI could not save the Script metadata. Review the values and try again.", scriptId = script.Id, moveItSaved = moveItChanged });
            }
        }
        catch (ObjectCheckoutRejectedException ex) { return Conflict(ex.Feedback()); }
        catch (ObjectEditConflictException ex) { return Conflict(new { message = ex.Message, editConflict = true, moveItSaved = false }); }
        catch (ObjectEditLeaseException ex) { return Conflict(new { message = ex.Message, leaseLost = true, editConflict = true, moveItSaved = ex.MayHaveSaved }); }
        catch (ScriptWriteUncertainException ex) { logger.LogWarning("Script write could not be confirmed for {ScriptId}; error type {ErrorType}.", ex.ScriptId, ex.InnerException?.GetType().Name); return Conflict(new { message = ex.Message, scriptId = ex.ScriptId, moveItSaved = true }); }
        catch (ObjectCheckoutReleaseException ex) { return Conflict(new { message = ex.Message, moveItSaved = true }); }
        catch (MoveItAuthenticationException) { return Unauthorized(new { message = "MOVEit session expired. Please sign in again." }); }
        catch (Exception ex) when (ex is InvalidOperationException or MoveItApiException or MoveItComException or ArgumentException)
        { return BadRequest(new { message = ex.Message, moveItSaved = saved }); }
    }
    // MAUI metadata is editable even when MOVEit owns a read-only built-in Script.
    // This action is deliberately separate from Save's native checkout middleware.
    [HttpPost, ValidateAntiForgeryToken]
    [MoveItObjectPermission("Script", "Edit", "id")]
    public async Task<IActionResult> SaveMetadata(string id, [FromBody] ScriptMetadataSaveRequest? request, CancellationToken ct)
    {
        if (request is null) return BadRequest(new { message = "Script metadata is required.", moveItSaved = false });
        try
        {
            var local = await metadataSaves.SaveBuiltInAsync(id, request.Metadata, ct);
            return Json(new { success = true, metadata = local.Values, audit = Audit(local) });
        }
        catch (MoveItAuthenticationException) { return Unauthorized(new { message = "MOVEit session expired. Please sign in again.", moveItSaved = false }); }
        catch (Exception ex) when (ex is InvalidOperationException or MoveItApiException or ArgumentException)
        { return BadRequest(new { message = ex.Message, moveItSaved = false }); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { logger.LogError(ex, "Built-in Script {ScriptId} MAUI metadata save failed.", id); return BadRequest(new { message = "MAUI could not confirm the Script metadata save. Review the values and try again.", moveItSaved = false }); }
    }
    private static object Audit(ObjectMetadataSnapshot local) => new
    { local.CreatedUtc, local.CreatedBy, local.ModifiedUtc, local.ModifiedBy };

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePreferences([FromBody] ScriptEditorPreferences request, CancellationToken ct)
    {
        try { scripts.RequireReadSession(); return Json(new { success = true, preferences = await preferences.SaveCurrentScriptEditorPreferencesAsync(request, ct) }); }
        catch (MoveItAuthenticationException) { return Unauthorized(new { message = "MOVEit session expired. Please sign in again." }); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { logger.LogError(ex, "Script Auto Save preference could not be saved."); return BadRequest(new { message = "The Auto Save preference could not be saved. Try again." }); }
    }
    [HttpGet]
    public async Task<IActionResult> Templates(CancellationToken ct)
    {
        try { return Json(new { success = true, templates = await templates.GetAsync(ct) }); }
        catch (MoveItAuthenticationException) { return Unauthorized(new { message = "MOVEit session expired. Please sign in again." }); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { logger.LogError(ex, "Script templates could not be loaded."); return StatusCode(500, new { message = "Script templates could not be loaded. Review Configuration > Logs for details." }); }
    }
    [Authorize(Policy = MauiSecurityPolicies.TemplateAdmin)]
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(16 * 1024 * 1024)]
    public async Task<IActionResult> SaveTemplate([FromBody] ScriptTemplateSave? request, CancellationToken ct)
    {
        if (request is null) return BadRequest(new { message = "Template data is required." });
        try { scripts.RequireSession(); return Json(new { success = true, template = await templates.SaveAsync(request, ct) }); }
        catch (MoveItAuthenticationException) { return Unauthorized(new { message = "MOVEit session expired. Please sign in again." }); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { logger.LogError(ex, "Script template could not be saved."); return StatusCode(500, new { message = "Script template could not be saved. Review Configuration > Logs for details." }); }
    }
    public sealed record TemplateIdRequest(string TemplateId = "");
    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteTemplate([FromBody] TemplateIdRequest request, CancellationToken ct)
    {
        try { await templates.DeleteAsync(request.TemplateId, ct); return Json(new { success = true }); }
        catch (MoveItAuthenticationException) { return Unauthorized(new { message = "MOVEit session expired. Please sign in again." }); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { logger.LogError(ex, "Script template could not be removed."); return StatusCode(500, new { message = "Script template could not be removed. Review Configuration > Logs for details." }); }
    }
}
