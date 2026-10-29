// Author: Marc R. Davis (marcdavis@comcast.net)
// Task JSON endpoints are opt-in adapters, not a second Task CRUD implementation.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Tasks;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;

namespace MoveItAutomationUI.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TaskJsonEditorController(ITaskJsonValueService values, IMoveItSessionService sessions,
    IOptionsSnapshot<UiOptions> ui, ITaskUndoService undo, IMetadataRepository metadata,
    IAutomationTaskService tasks, ILogger<TaskJsonEditorController> logger) : Controller
{
    private IActionResult? Gate(string id)
    {
        if (!ui.Value.TaskJsonEditor.Value) return NotFound();
        if (sessions.Get() is null) return Unauthorized(new { message = "Your MOVEit session expired. Sign in again." });
        if (!ModelState.IsValid) return BadRequest(new { message = "The Task JSON request is invalid." });
        return string.IsNullOrWhiteSpace(id) || id.Length > 32 || !id.All(char.IsAsciiDigit)
            ? BadRequest(new { message = "Select a valid Task." }) : null;
    }
    [HttpGet]
    public async Task<IActionResult> Shell(string id, CancellationToken ct)
    {
        if (Gate(id) is { } denied) return denied;
        try { return Json(await values.ReadAsync(id, ct)); }
        catch (Exception ex) when (Handled(ex, ct)) { return Failure(id, ex); }
    }
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(1_048_576)]
    public async Task<IActionResult> Validate(string id, [FromBody] TaskJsonEditRequest? request, CancellationToken ct)
    {
        if (Gate(id) is { } denied) return denied;
        if (request is null) return BadRequest(new { message = "Changed Task values are required." });
        try { await values.ValidateAsync(id, request, ct); return Json(new { success = true }); }
        catch (Exception ex) when (Handled(ex, ct)) { return Failure(id, ex); }
    }
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(1_048_576)]
    public async Task<IActionResult> Save(string id, [FromBody] TaskJsonEditRequest? request, CancellationToken ct)
    {
        if (Gate(id) is { } denied) return denied;
        if (request is null) return BadRequest(new { message = "Changed Task values are required." });
        var attempted = false;
        try
        {
            await values.ValidateAsync(id, request, ct);
            var session = sessions.Get()!;
            // Capture each affected Task before its write; a failed capture must stop this save.
            await undo.CaptureAsync(id, session.Username, ct);
            attempted = true;
            var version = await values.SaveAsync(id, request, ct);
            var warning = "";
            try { await metadata.RecordObjectConfigurationModifiedAsync(session.GetEnvironmentKey(), "Task", id, session.Username, ct); }
            catch (Exception ex)
            {
                logger.LogWarning("Task JSON Editor saved Task {TaskId}, but its MAUI audit update failed ({ErrorType}).", id, ex.GetType().Name);
                warning = "Task saved; its MAUI audit update could not be completed.";
            }
            TaskJsonEditorDocument? document = null;
            try { document = await values.ReadAsync(id, ct); }
            catch (Exception ex)
            {
                logger.LogWarning("Task JSON Editor saved Task {TaskId}, but its refreshed definition could not be loaded ({ErrorType}).", id, ex.GetType().Name);
                warning = "Task saved; reload it before making more changes.";
            }
            return Json(new { success = true, taskId = id, editVersion = document?.EditVersion ?? version,
                document, requiresReload = document is null, warning });
        }
        catch (Exception ex) when (Handled(ex, ct)) { return Failure(id, ex); }
        finally { if (attempted) tasks.InvalidateCache(); }
    }
    private static bool Handled(Exception ex, CancellationToken ct)
        => ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException or HttpRequestException or IOException or TimeoutException
            || ex is OperationCanceledException && !ct.IsCancellationRequested;
    private IActionResult Failure(string id, Exception ex)
    {
        // A Task definition, native response body or edited value must never enter diagnostic logs.
        logger.LogWarning("Task JSON Editor operation failed for Task {TaskId} ({ErrorType}; HTTP {NativeStatus}; cause {CauseType}).",
            id, ex.GetType().Name, (ex as MoveItApiException)?.StatusCode, ex.InnerException?.GetType().Name);
        if (ex is MoveItAuthenticationException) return Unauthorized(new { message = "Your MOVEit session expired. Your unsaved edits remain in this editor." });
        if (ex is MoveItObjectAccessException) return StatusCode(403, new { message = "Your MOVEit account cannot edit this Task.", moveItSaved = false });
        if (ex is ObjectCheckoutRejectedException checkout) return Conflict(checkout.Feedback());
        if (ex is ObjectEditConflictException) return Conflict(new { message = ex.Message, editConflict = true, moveItSaved = false, requiresReload = true });
        if (ex is ObjectEditLeaseException lease) return Conflict(new { message = lease.Message, leaseLost = true, moveItSaved = lease.MayHaveSaved, requiresReload = true });
        if (ex is TraditionalTaskPartialSaveException or ObjectCheckoutReleaseException)
            return StatusCode(502, new { message = "The save result could not be confirmed. Reload this Task before retrying.", moveItSaved = true, requiresReload = true });
        if (ex is MoveItApiException or MoveItComException or HttpRequestException or IOException or TimeoutException or OperationCanceledException)
            return StatusCode(502, new { message = "The MOVEit operation did not complete. See MAUI logs for details.", moveItSaved = false });
        return BadRequest(new { message = ex.Message, moveItSaved = false });
    }
}
