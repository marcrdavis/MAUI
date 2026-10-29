// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.Security;

namespace MoveItAutomationUI.Controllers;

[Authorize]
public sealed class RecycleBinController : Controller
{
    private readonly IRecycleBinService _recycleBin;
    private readonly IMoveItSessionService _session;
    private readonly IMauiSecurityService _security;
    private readonly ILogger<RecycleBinController> _logger;

    // Purpose: Initializes recycle bin controller with its required dependencies.
    public RecycleBinController(IRecycleBinService recycleBin, IMoveItSessionService session, IMauiSecurityService security, ILogger<RecycleBinController> logger)
    { _recycleBin = recycleBin; _session = session; _security = security; _logger = logger; }

    // Purpose: Returns the current Recycle Bin items visible to the signed-in user.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Items(CancellationToken cancellationToken)
    {
        var state = _session.Get();
        if (state is null) return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        if (!_recycleBin.Enabled) return NotFound(new { message = "The MAUI Recycle Bin is disabled." });
        try
        {
            var isAdmin = _security.IsAdmin(User.Identity?.Name);
            var items = await _recycleBin.GetVisibleItemsAsync(state.Username, isAdmin, cancellationToken);
            return Json(new { success = true, isAdmin, items = items.Select(item => new { id = item.Id, type = item.ObjectType, name = item.ObjectName, deletedBy = item.DeletedBy, deletedOn = item.DeletedUtc }) });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to load the MAUI Recycle Bin.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "MAUI could not load the Recycle Bin. Review the application log for details." });
        }
    }

    // Purpose: Validates restore and returns the information required before execution.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PreflightRestore([FromBody] RecycleSelectionRequest? request, CancellationToken cancellationToken)
    {
        var state = _session.Get();
        if (state is null) return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        try
        {
            var isAdmin = _security.IsAdmin(User.Identity?.Name);
            var result = await _recycleBin.PreflightRestoreAsync(request?.Ids ?? new List<string>(), state.Username, isAdmin, cancellationToken);
            return Json(new { success = true, selectedCount = result.SelectedCount, conflicts = result.Conflicts.Select(item => new { recycleItemId = item.RecycleItemId, type = item.ObjectType, name = item.ObjectName, existingObjectId = item.ExistingObjectId, canOverwrite = item.CanOverwrite, message = item.Message }) });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        { return BadRequest(new { message = ex.Message }); }
    }

    // Purpose: Restores the selected Recycle Bin item or items after preflight validation.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore([FromBody] RecycleRestoreRequest? request, CancellationToken cancellationToken)
    {
        var state = _session.Get();
        if (state is null) return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        try
        {
            var isAdmin = _security.IsAdmin(User.Identity?.Name);
            var result = await _recycleBin.RestoreAsync(request?.Ids ?? new List<string>(), state.Username, isAdmin, request?.OverwriteConflicts == true, cancellationToken);
            return Json(new { success = result.Failed == 0, succeeded = result.Succeeded, failed = result.Failed, items = result.Items.Select(item => new { id = item.RecycleItemId, type = item.ObjectType, name = item.ObjectName, success = item.Success, overwritten = item.Overwritten, newObjectId = item.NewObjectId, message = item.Message }) });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        { return BadRequest(new { message = ex.Message }); }
    }

    // Purpose: Empties the MAUI Recycle Bin after authorization and validation.
    [HttpPost]
    [ActionName("Empty")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EmptyRecycleBin(CancellationToken cancellationToken)
    {
        if (_session.Get() is null) return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        if (!_security.IsAdmin(User.Identity?.Name)) return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only a MAUI Admin can empty the Recycle Bin." });
        try
        {
            var count = await _recycleBin.EmptyAsync(true, cancellationToken);
            _logger.LogWarning("MAUI Recycle Bin emptied by {Username}; {Count} item(s) permanently removed.", User.Identity?.Name ?? "unknown", count);
            return Json(new { success = true, count, message = count == 1 ? "1 Recycle Bin item was permanently removed." : $"{count} Recycle Bin items were permanently removed." });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }
}

public sealed class RecycleSelectionRequest { public List<string> Ids { get; set; } = new(); }
public sealed class RecycleRestoreRequest { public List<string> Ids { get; set; } = new(); public bool OverwriteConflicts { get; set; } }
