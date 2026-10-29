using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Features.Editors.DateLists;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Resources.DateLists;

namespace MoveItAutomationUI.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DateListEditorController(DateListCrudCoordinator dateLists,
    IDateListUndoService undo, IMetadataRepository metadata, IAutomationDateListService inventory,
    IMetadataValueValidationService validation, IUserPreferencesService preferences, IOptionsSnapshot<MetadataUiOptions> metadataOptions,
    IOptionsSnapshot<UiOptions> ui, ILogger<DateListEditorController> logger) : Controller
{
    [HttpGet]
    [MoveItObjectPermission("DateList", "Open", "id")]
    public async Task<IActionResult> Shell(string id, CancellationToken ct)
    {
        try
        {
            var session = dateLists.RequireReadSession();
            id = DateListConfiguration.Id(id);
            var raw = await dateLists.ReadRawAsync(id, ct, false);
            var readOnly = !ObjectCheckout.CanEditCheckedOut("DateList", id,
                raw["xAddEditDelete"] is null || MoveItRestClient.GetString(raw, "xAddEditDelete").ToLowerInvariant() is "true" or "1");
            if (HttpContext?.RequestServices?.GetService<IMoveItObjectAccessService>() is { } access)
                readOnly = !(await access.GetObjectAsync("DateList", id, ct)).CanEdit;
            var definition = DateListConfiguration.Model(raw, id);
            var references = inventory.GetAssignedTasksAsync(id, ct);
            var local = metadata.GetObjectMetadataAsync(session.GetEnvironmentKey(), "DateList", id, ct);
            await Task.WhenAll(references, local);
            var record = await local;
            var canUndo = false; // The grid keeps its independent history.
            return Json(new { success = true, dateList = definition, canUndo, readOnly, tasks = await references,
                metadata = record.Values, fields = metadataOptions.Value.DateLists.Fields, audit = Audit(record),
                preferences = readOnly ? new DateListEditorPreferences(false) : await preferences.GetCurrentDateListEditorPreferencesAsync(ct),
                checkoutObjectsForDurationOfEdit = !readOnly && ui.Value.CheckoutObjectsForDurationOfEdit.Value });
        }
        catch (MoveItAuthenticationException) { return Unauthorized(new { message = "MOVEit session expired. Please sign in again." }); }
        catch (Exception ex) when (ex is InvalidOperationException or MoveItApiException or ArgumentException)
        { return BadRequest(new { message = ex.Message }); }
    }
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(16 * 1024 * 1024)]
    [MoveItObjectPermission("DateList", "Save", "id")]
    public async Task<IActionResult> Save(string? id, [FromBody] DateListEditorSaveRequest? request, CancellationToken ct)
    {
        if (request is null || request.Entries is null) return BadRequest(new { message = "Date List data is required." });
        var saved = false;
        try
        {
            var session = dateLists.RequireSession();
            var creating = string.IsNullOrWhiteSpace(id);
            var values = creating ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : await validation.FilterAndValidateAsync(session.GetEnvironmentKey(), metadataOptions.Value.DateLists, request.Metadata, ct);
            var moveItChanged = creating;
            DateListUndoSnapshot? undoSnapshot = null;
            var definition = request.Definition();
            using var revision = string.IsNullOrWhiteSpace(id) ? null : ObjectEditVersion.Require("DateListEditor", id, request.EditVersion);
            var dateList = string.IsNullOrWhiteSpace(id) ? await dateLists.CreateAsync(definition, ct)
                : await dateLists.SaveAsync(id, definition, ct: ct, beforeWrite: async token => { undoSnapshot = await undo.CaptureAsync(id, session.Username, token); moveItChanged = true; });
            saved = true;
            try
            {
                if (creating) await metadata.RecordObjectCreatedAsync(session.GetEnvironmentKey(), "DateList", dateList.Id, session.Username, ct);
                else
                {
                    await metadata.SaveObjectMetadataAsync(session.GetEnvironmentKey(), "DateList", dateList.Id, values, session.Username, ct);
                    if (moveItChanged) await metadata.RecordObjectConfigurationModifiedAsync(session.GetEnvironmentKey(), "DateList", dateList.Id, session.Username, ct);
                }
                var local = await metadata.GetObjectMetadataAsync(session.GetEnvironmentKey(), "DateList", dateList.Id, ct);
                var canUndo = undoSnapshot is not null;
                return Json(new { success = true, dateList, canUndo, undoSnapshotId = undoSnapshot?.SnapshotId ?? "", metadata = local.Values, audit = Audit(local), warning = "",
                    checkoutObjectsForDurationOfEdit = ui.Value.CheckoutObjectsForDurationOfEdit.Value });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "DateList {DateListId} local metadata/audit save could not be confirmed.", dateList.Id);
                return BadRequest(new { message = moveItChanged
                    ? "The Date List was saved in MOVEit, but MAUI could not confirm its metadata/audit save. Reload Date List before saving again."
                    : "MAUI could not save the Date List metadata. Review the values and try again.", dateListId = dateList.Id, moveItSaved = moveItChanged });
            }
        }
        catch (ObjectCheckoutRejectedException ex) { return Conflict(ex.Feedback()); }
        catch (ObjectEditConflictException ex) { return Conflict(new { message = ex.Message, editConflict = true, moveItSaved = false }); }
        catch (ObjectEditLeaseException ex) { return Conflict(new { message = ex.Message, leaseLost = true, editConflict = true, moveItSaved = ex.MayHaveSaved }); }
        catch (DateListWriteUncertainException ex) { logger.LogWarning("DateList write could not be confirmed for {DateListId}; error type {ErrorType}.", ex.DateListId, ex.InnerException?.GetType().Name); return Conflict(new { message = ex.Message, dateListId = ex.DateListId, moveItSaved = true }); }
        catch (ObjectCheckoutReleaseException ex) { return Conflict(new { message = ex.Message, moveItSaved = true }); }
        catch (MoveItAuthenticationException) { return Unauthorized(new { message = "MOVEit session expired. Please sign in again." }); }
        catch (Exception ex) when (ex is InvalidOperationException or MoveItApiException or MoveItComException or ArgumentException)
        { return BadRequest(new { message = ex.Message, moveItSaved = saved }); }
    }
    private static object Audit(ObjectMetadataSnapshot local) => new
    { local.CreatedUtc, local.CreatedBy, local.ModifiedUtc, local.ModifiedBy };

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePreferences([FromBody] DateListEditorPreferences request, CancellationToken ct)
    {
        try { dateLists.RequireSession(); return Json(new { success = true, preferences = await preferences.SaveCurrentDateListEditorPreferencesAsync(request, ct) }); }
        catch (MoveItAuthenticationException) { return Unauthorized(new { message = "MOVEit session expired. Please sign in again." }); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { logger.LogError(ex, "Date List Auto Save preference could not be saved."); return BadRequest(new { message = "The Auto Save preference could not be saved. Try again." }); }
    }
}
