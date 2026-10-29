// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Controllers;

/// <summary>
/// Supplies the credential handoff and one-level Undo Last Change bridge used by MAUI-launched native MOVEit editors.
/// </summary>
[Authorize]
public sealed class NativeEditorController : Controller
{
    private readonly IMoveItSessionService _sessionService;
    private readonly INativeEditorUndoService _nativeEditorUndoService;
    private readonly ILogger<NativeEditorController> _logger;

    public NativeEditorController(
        IMoveItSessionService sessionService,
        INativeEditorUndoService nativeEditorUndoService,
        ILogger<NativeEditorController> logger)
    {
        _sessionService = sessionService;
        _nativeEditorUndoService = nativeEditorUndoService;
        _logger = logger;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Session()
    {
        var session = _sessionService.Get();
        if (session is null)
            return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });

        if (string.IsNullOrWhiteSpace(session.Password))
        {
            return BadRequest(new
            {
                message = "The current MOVEit password is no longer available in server memory. Sign in again before opening the native editor."
            });
        }

        if (!Uri.TryCreate(session.WebAdminUrl, UriKind.Absolute, out var webAdminUri)
            || (webAdminUri.Scheme != Uri.UriSchemeHttps && webAdminUri.Scheme != Uri.UriSchemeHttp))
        {
            return BadRequest(new { message = "The configured MOVEit Web Admin URL is invalid." });
        }

        Response.Headers["Cache-Control"] = "no-store, no-cache, max-age=0";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";

        return Json(new
        {
            username = session.Username,
            password = session.Password,
            webAdminUrl = session.WebAdminUrl.TrimEnd('/'),
            undoInitializeUrl = Url.Action(nameof(UndoInitialize), "NativeEditor"),
            undoAdvanceUrl = Url.Action(nameof(UndoAdvance), "NativeEditor"),
            undoRebindUrl = Url.Action(nameof(UndoRebind), "NativeEditor"),
            undoRestoreUrl = Url.Action(nameof(UndoRestore), "NativeEditor"),
            undoDiscardUrl = Url.Action(nameof(UndoDiscard), "NativeEditor")
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UndoInitialize([FromBody] NativeEditorUndoRequest? request, CancellationToken cancellationToken)
        => await ExecuteUndoRequestAsync(request, "initialize", (launchId, objectType, objectId, username)
            => _nativeEditorUndoService.InitializeAsync(launchId, objectType, objectId, username, cancellationToken));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UndoAdvance([FromBody] NativeEditorUndoRequest? request, CancellationToken cancellationToken)
        => await ExecuteUndoRequestAsync(request, "advance", (launchId, objectType, objectId, username)
            => _nativeEditorUndoService.AdvanceAsync(launchId, objectType, objectId, username, cancellationToken));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UndoRebind([FromBody] NativeEditorUndoRequest? request, CancellationToken cancellationToken)
    {
        var session = _sessionService.Get();
        if (session is null) return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });

        var launchId = (request?.LaunchId ?? string.Empty).Trim();
        var objectType = (request?.ObjectType ?? string.Empty).Trim();
        var previousObjectId = (request?.PreviousObjectId ?? string.Empty).Trim();
        var objectId = (request?.ObjectId ?? string.Empty).Trim();
        if (launchId.Length == 0 || objectType.Length == 0 || previousObjectId.Length == 0 || objectId.Length == 0)
            return BadRequest(new { message = "The native editor undo identity-change request is incomplete." });

        try
        {
            var state = await _nativeEditorUndoService.RebindAsync(launchId, objectType, previousObjectId, objectId, session.Username, cancellationToken);
            return Json(new { success = true, undoAvailable = state.UndoAvailable, objectType = state.ObjectType, objectId = state.ObjectId, objectName = state.ObjectName });
        }
        catch (MoveItAuthenticationException)
        {
            return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        }
        catch (Exception ex) when (ex is MoveItComException or MoveItApiException or InvalidOperationException or ArgumentException)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to rebind native-editor Undo Last Change from {PreviousObjectId} to {ObjectId}.", previousObjectId, objectId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "MAUI could not continue Undo Last Change for the new Task." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UndoRestore([FromBody] NativeEditorUndoRequest? request, CancellationToken cancellationToken)
    {
        var session = _sessionService.Get();
        if (session is null) return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });

        var launchId = (request?.LaunchId ?? string.Empty).Trim();
        var objectType = (request?.ObjectType ?? string.Empty).Trim();
        var objectId = (request?.ObjectId ?? string.Empty).Trim();
        if (launchId.Length == 0 || objectType.Length == 0 || objectId.Length == 0)
            return BadRequest(new { message = "The native editor undo request is incomplete." });

        try
        {
            var result = await _nativeEditorUndoService.RestoreAsync(launchId, objectType, objectId, session.Username, cancellationToken);
            return Json(new { success = true, undoAvailable = false, objectType = result.ObjectType, objectId = result.ObjectId, objectName = result.ObjectName, warning = result.Warning });
        }
        catch (MoveItAuthenticationException)
        {
            return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        }
        catch (Exception ex) when (ex is MoveItComException or MoveItApiException or InvalidOperationException or ArgumentException)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to restore native-editor Undo Last Change for {ObjectType} {ObjectId}.", objectType, objectId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "MAUI could not restore the last native-editor change." });
        }
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UndoDiscard([FromBody] NativeEditorUndoRequest? request, CancellationToken cancellationToken)
    {
        var session = _sessionService.Get();
        if (session is null) return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });

        var launchId = (request?.LaunchId ?? string.Empty).Trim();
        var objectType = (request?.ObjectType ?? string.Empty).Trim();
        var objectId = (request?.ObjectId ?? string.Empty).Trim();
        if (launchId.Length == 0 || objectType.Length == 0 || objectId.Length == 0)
            return BadRequest(new { message = "The native editor undo request is incomplete." });

        try
        {
            await _nativeEditorUndoService.DiscardAsync(launchId, objectType, objectId, session.Username, cancellationToken);
            return Json(new { success = true });
        }
        catch (MoveItAuthenticationException)
        {
            return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to discard native-editor Undo Last Change state for {ObjectType} {ObjectId}.", objectType, objectId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "MAUI could not discard the native-editor undo state." });
        }
    }

    private async Task<IActionResult> ExecuteUndoRequestAsync(
        NativeEditorUndoRequest? request,
        string operation,
        Func<string, string, string, string, Task<NativeEditorUndoState>> action)
    {
        var session = _sessionService.Get();
        if (session is null) return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });

        var launchId = (request?.LaunchId ?? string.Empty).Trim();
        var objectType = (request?.ObjectType ?? string.Empty).Trim();
        var objectId = (request?.ObjectId ?? string.Empty).Trim();
        if (launchId.Length == 0 || objectType.Length == 0 || objectId.Length == 0)
            return BadRequest(new { message = "The native editor undo request is incomplete." });

        try
        {
            var state = await action(launchId, objectType, objectId, session.Username);
            return Json(new { success = true, undoAvailable = state.UndoAvailable, objectType = state.ObjectType, objectId = state.ObjectId, objectName = state.ObjectName });
        }
        catch (MoveItAuthenticationException)
        {
            return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        }
        catch (Exception ex) when (ex is MoveItComException or MoveItApiException or InvalidOperationException or ArgumentException)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to {Operation} native-editor undo state for {ObjectType} {ObjectId}.", operation, objectType, objectId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "MAUI could not update the native-editor undo state." });
        }
    }

    public sealed class NativeEditorUndoRequest
    {
        public string LaunchId { get; set; } = string.Empty;
        public string ObjectType { get; set; } = string.Empty;
        public string PreviousObjectId { get; set; } = string.Empty;
        public string ObjectId { get; set; } = string.Empty;
    }
}
