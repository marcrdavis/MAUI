// Project: MAUI - MOVEit Automation UI
using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Features.AuditChanges;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Controllers;

[Authorize]
public sealed class AuditChangesController(IMoveItRestClient rest, IMoveItSessionService sessions, ILogger<AuditChangesController> logger) : Controller
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Details(long logId, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || logId <= 0) return BadRequest(new { message = "Select a valid audit record." });
        var session = sessions.Get();
        if (session is null) return Unauthorized(new { message = "Your MOVEit session has expired. Sign in again." });
        if (!MoveItFeatureSupport.Supports(session.Info, MoveItFeature.AuditConfigurationChanges))
            return BadRequest(new { message = "Configuration changes require MOVEit Automation 2026 or later." });
        try
        {
            // Current-user REST authorization remains authoritative. No Data Access credentials or checkout.
            var record = await rest.GetAuditChangesAsync(logId, cancellationToken);
            return Json(AuditChangeComparer.Compare(record, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (MoveItAuthenticationException)
        { return Unauthorized(new { message = "Your MOVEit session has expired. Sign in again." }); }
        catch (Exception ex) when (ex is MoveItApiException or HttpRequestException or OperationCanceledException or InvalidOperationException)
        {
            // Avoid logging native snapshots or exception response bodies containing configuration values.
            logger.LogWarning("Audit comparison {LogId} could not be loaded ({ErrorType}).", logId, ex.GetType().Name);
            var status = ex is MoveItApiException api ? api.StatusCode : null;
            if (status == HttpStatusCode.Forbidden) return StatusCode(403, new { message = "You do not have permission to view these configuration changes." });
            if (status == HttpStatusCode.NotFound) return NotFound(new { message = "Configuration changes are no longer available for this audit record." });
            var message = ex is InvalidOperationException && ex.Message == "This audit comparison is too large to display in MAUI." ? ex.Message
                : ex is OperationCanceledException ? "Loading configuration changes timed out. Close and try again."
                : "Configuration changes could not be loaded. Close and try again.";
            return StatusCode(502, new { message });
        }
    }
}
