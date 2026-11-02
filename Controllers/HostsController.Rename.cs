using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Controllers;

public sealed partial class HostsController
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Host", "Edit", "HostId")]
    public async Task<IActionResult> RenameHost([FromBody] HostRenameRequest? request, CancellationToken cancellationToken = default)
    {
        var id = (request?.HostId ?? "").Trim();
        var name = (request?.Name ?? "").Trim();
        if (!ModelState.IsValid || id.Length is 0 or > 32 || !id.All(char.IsAsciiDigit))
            return BadRequest(new { message = "Select a valid Host." });
        if (name.Length is 0 or > 254) return BadRequest(new { message = "Host Name must contain 1–254 characters." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();
        var attempted = false; var saved = false;
        try
        {
            await _hostService.ValidateRenameAsync(id, name, request!.OriginalName, cancellationToken);
            await _hostUndoService.CaptureAsync(id, session.Username, cancellationToken);
            attempted = true;
            await _hostService.RenameAsync(id, name, request.OriginalName, cancellationToken);
            saved = true;
            await _metadataRepository.RecordObjectConfigurationModifiedAsync(session.GetEnvironmentKey(), ObjectType, id, session.Username, cancellationToken);
            return Json(new { success = true, hostId = id, hostName = name });
        }
        catch (HostPartialSaveException ex)
        {
            _logger.LogWarning("Rename Host {HostId} has an unverified save outcome ({Stage}; {Reason}).", id, ex.VerificationStage, ex.VerificationReason);
            return StatusCode(502, new { message = ex.Message, moveItSaved = true, outcomeUnknown = true });
        }
        catch (Exception ex) when (saved && ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Host {HostId} was renamed, but its audit update failed ({ErrorType}). Database cause: {DatabaseCause}",
                id, ex.GetType().Name, ex.GetBaseException().Message);
            return StatusCode(500, new { message = "The Host was renamed, but MAUI could not finish its audit update. Close this dialog and refresh the Hosts grid.", moveItSaved = true });
        }
        catch (HostNameAlreadyExistsException ex) { return Conflict(new { message = ex.Message }); }
        catch (ObjectEditConflictException)
        {
            return Conflict(new { message = "The Host name changed in MOVEit. Close this dialog and refresh the Hosts grid before renaming it.", editConflict = true });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItAuthenticationException or MoveItComException or InvalidOperationException or ArgumentException)
        { return MauiControllerExceptionMapper.Map(ex); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning("Rename Host {HostId} failed ({ErrorType}).", id, ex.GetType().Name);
            return StatusCode(500, new { message = attempted
                ? "The Host rename result could not be confirmed. Close this dialog and refresh the Hosts grid before trying again."
                : "MAUI could not rename the Host. See Configuration > Logs for details.", moveItSaved = attempted, outcomeUnknown = attempted });
        }
    }
}
