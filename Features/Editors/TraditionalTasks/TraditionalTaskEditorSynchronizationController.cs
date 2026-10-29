// Author: Marc R. Davis (marcdavis@comcast.net)
// Thin MAUI seams for Sync actions; native operations stay in the shared Tasks layer.
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Tasks;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;

namespace MoveItAutomationUI.Controllers;

public sealed partial class TraditionalTaskEditorController
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> SyncPreview(string id, [FromServices] ITaskCrudCoordinator tasks, CancellationToken cancellationToken)
    {
        if (_sessionService.Get() is null) return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        try { return Json(await tasks.GetSynchronizationPreviewAsync(id, cancellationToken)); }
        catch (MoveItAuthenticationException) { return Unauthorized(new { message = "MOVEit session expired. Please sign in again." }); }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException)
        { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [MoveItObjectPermission("Task", "Edit", "id")]
    public async Task<IActionResult> SyncAction(string id, [FromBody] SyncActionRequest? request,
        [FromServices] ITaskCrudCoordinator tasks, CancellationToken cancellationToken)
    {
        id = (id ?? "").Trim();
        if (id.Length == 0) return BadRequest(new { message = "Task ID is required." });
        var session = _sessionService.Get();
        if (session is null) return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        if (request is null || request.Action is not ("swap" or "clear-listings" or "accept-preview"))
            return BadRequest(new { message = "Select a valid Sync action." });
        if (request.Action != "accept-preview" && !request.Confirmed)
            return BadRequest(new { message = "Confirm the Sync action before continuing." });
        var dispatched = false;
        try
        {
            using var edit = ObjectEditVersion.Require("Task", id, request.EditVersion);
            var definition = await _editorService.GetDefinitionAsync(id, cancellationToken);
            if (definition.Task.Type != "Synchronization") return BadRequest(new { message = "This action requires a Sync Task." });
            if (!definition.Task.CanAlter) return Forbid();
            if (request.Action == "swap") await _taskUndoService.CaptureAsync(id, session.Username, cancellationToken);
            dispatched = true;
            if (request.Action == "swap") await tasks.SwapSynchronizationFoldersAsync(id, cancellationToken);
            else if (request.Action == "clear-listings") await tasks.ClearSynchronizationListingsAsync(id, cancellationToken);
            else await tasks.SaveSynchronizationPreviewAsync(id, cancellationToken);
            try
            {
                await _metadataRepository.RecordObjectConfigurationModifiedAsync(session.GetEnvironmentKey(), ObjectType, id, session.Username, cancellationToken);
                await _taskService.GetTaskSnapshotAsync(true, cancellationToken);
            }
            catch (Exception ex)
            {
                throw new SynchronizationActionPartialSaveException("The Sync action completed, but MAUI could not refresh its audit or Task cache. Reopen the Task before continuing.", ex);
            }
            return Json(new { success = true, taskId = id });
        }
        catch (MoveItAuthenticationException) { return Unauthorized(new { message = "MOVEit session expired. Please sign in again." }); }
        catch (ObjectEditLeaseException ex)
        {
            return Conflict(new { message = ex.Message, leaseLost = true, editConflict = true, moveItSaved = ex.MayHaveSaved });
        }
        catch (Exception ex) when (ex is ObjectEditConflictException or ObjectCheckoutReleaseException)
        { return Conflict(new { message = ex.Message, moveItSaved = false, editConflict = true }); }
        catch (Exception ex) when (ex is SynchronizationActionPartialSaveException or TraditionalTaskPartialSaveException)
        { return StatusCode(StatusCodes.Status409Conflict, new { message = ex.Message, moveItSaved = true }); }
        catch (OperationCanceledException) when (dispatched)
        { return StatusCode(StatusCodes.Status409Conflict, new { message = "The Sync action was interrupted. Reopen the Task before retrying.", moveItSaved = true }); }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException)
        { return BadRequest(new { message = ex.Message }); }
    }
    public sealed record SyncActionRequest(string Action, bool Confirmed = false, string EditVersion = "");
}
