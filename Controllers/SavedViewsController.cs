// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;

namespace MoveItAutomationUI.Controllers;

[Authorize]
public sealed class SavedViewsController : Controller
{
    private readonly ISavedViewService _savedViews;
    private readonly ILogger<SavedViewsController> _logger;

    // Purpose: Initializes saved views controller with its required dependencies.
    public SavedViewsController(ISavedViewService savedViews, ILogger<SavedViewsController> logger)
    {
        _savedViews = savedViews;
        _logger = logger;
    }

    // Purpose: Returns one saved grid view owned by the current user.
    [HttpGet]
    public async Task<IActionResult> Get(string id, CancellationToken cancellationToken)
    {
        try
        {
            var view = await _savedViews.GetCurrentByIdAsync(id, cancellationToken);
            if (view is null) return NotFound(new { message = "The saved view was not found or is no longer supported." });
            return Json(new
            {
                view = new
                {
                    id = view.Summary.Id,
                    gridKey = view.Summary.GridKey,
                    gridLabel = view.Summary.GridLabel,
                    viewName = view.Summary.ViewName,
                    description = view.Summary.Description,
                    state = view.State,
                    warning = view.Warning
                }
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to load saved grid view {SavedViewId} for {Username}.", id, User.Identity?.Name ?? "unknown");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "The saved view could not be loaded." });
        }
    }

    // Purpose: Creates or updates a saved grid view for the current user.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromBody] SavedViewSaveRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _savedViews.SaveCurrentAsync(request, cancellationToken);
            if (result.Status is SavedViewSaveStatus.Conflict or SavedViewSaveStatus.ConflictOtherGrid)
            {
                var existing = result.ExistingView;
                var isUpdate = !string.IsNullOrWhiteSpace(request?.Id);
                return Conflict(new
                {
                    conflict = true,
                    canOverwrite = !isUpdate && result.Status == SavedViewSaveStatus.Conflict,
                    message = result.Status == SavedViewSaveStatus.Conflict
                        ? isUpdate
                            ? $"Another saved view named '{existing?.ViewName}' already exists. Choose a different View Name."
                            : $"A saved view named '{existing?.ViewName}' already exists for {existing?.GridLabel}. Creating this view will replace its current definition."
                        : $"A saved view named '{existing?.ViewName}' already exists for {existing?.GridLabel}. View names must be unique in My Views; choose another name.",
                    existingView = existing is null ? null : ToClientView(existing)
                });
            }

            var saved = result.View!;
            return Json(new { success = true, view = ToClientView(saved) });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to save a grid view for {Username}.", User.Identity?.Name ?? "unknown");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "The view could not be saved." });
        }
    }

    // Purpose: Deletes one saved grid view owned by the current user.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete([FromBody] SavedViewDeleteRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await _savedViews.DeleteCurrentAsync(request?.Id ?? string.Empty, cancellationToken);
            if (!deleted) return NotFound(new { message = "The saved view was not found." });
            return Json(new { success = true });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to delete saved grid view {SavedViewId} for {Username}.", request?.Id, User.Identity?.Name ?? "unknown");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "The saved view could not be deleted." });
        }
    }

    // Purpose: Converts the current value to client view.
    private object ToClientView(SavedViewSummary view)
        => new
        {
            id = view.Id,
            gridKey = view.GridKey,
            gridLabel = view.GridLabel,
            viewName = view.ViewName,
            description = view.Description,
            url = Url.Action(view.Action, view.Controller, new { savedView = view.Id }) ?? Url.Content("~/")
        };
}
