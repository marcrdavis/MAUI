// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Controllers;

[Authorize]
[Route("Scheduler/[action]")]
public sealed class MoveItSchedulerController : Controller
{
    private const string SchedulerEnabled = "SCHEDULER_ENABLED";
    private const string SchedulerUnknown = "SCHEDULER_STATUS_UNKNOWN";

    private readonly IMoveItRestClient _restClient;
    private readonly ILogger<MoveItSchedulerController> _logger;

    // Purpose: Initializes scheduler control with the authenticated MOVEit REST client.
    public MoveItSchedulerController(IMoveItRestClient restClient, ILogger<MoveItSchedulerController> logger)
    {
        _restClient = restClient;
        _logger = logger;
    }

    // Returns MOVEit's current scheduler state for the global footer/disabled indicator.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Status(CancellationToken cancellationToken)
    {
        try
        {
            var status = await _restClient.GetSchedulerStatusAsync(cancellationToken);
            return Json(BuildStatusPayload(status));
        }
        catch (MoveItAuthenticationException)
        {
            return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        }
        catch (MoveItApiException ex)
        {
            _logger.LogWarning(ex, "Unable to retrieve MOVEit scheduler status for {Username}.", User.Identity?.Name ?? "unknown");
            return ApiError(ex, "The MOVEit scheduler status could not be retrieved.");
        }
    }

    // Enables the scheduler. MAUI Admin authorization is required here; MOVEit additionally enforces
    // its own MOVEit Admin Windows user-group requirement for the native scheduler operation.
    [HttpPost]
    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(CancellationToken cancellationToken)
    {
        try
        {
            await _restClient.StartSchedulerAsync(cancellationToken);
            _logger.LogInformation("MOVEit task scheduler started by {Username}.", User.Identity?.Name ?? "unknown");
            return Json(new { success = true });
        }
        catch (MoveItAuthenticationException)
        {
            return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        }
        catch (MoveItApiException ex)
        {
            _logger.LogWarning(ex, "MOVEit task scheduler start request failed for {Username}.", User.Identity?.Name ?? "unknown");
            return ApiError(ex, "The MOVEit task scheduler could not be started.");
        }
    }

    // Disables new scheduled launches. Running tasks are left to MOVEit to complete normally.
    [HttpPost]
    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Stop(CancellationToken cancellationToken)
    {
        try
        {
            await _restClient.StopSchedulerAsync(cancellationToken);
            _logger.LogInformation("MOVEit task scheduler stopped by {Username}.", User.Identity?.Name ?? "unknown");
            return Json(new { success = true });
        }
        catch (MoveItAuthenticationException)
        {
            return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        }
        catch (MoveItApiException ex)
        {
            _logger.LogWarning(ex, "MOVEit task scheduler stop request failed for {Username}.", User.Identity?.Name ?? "unknown");
            return ApiError(ex, "The MOVEit task scheduler could not be stopped.");
        }
    }

    // Converts MOVEit's scheduler enum to a small UI contract without losing the native reason code.
    private static object BuildStatusPayload(string? rawStatus)
    {
        var status = string.IsNullOrWhiteSpace(rawStatus) ? SchedulerUnknown : rawStatus.Trim();
        var enabled = status.Equals(SchedulerEnabled, StringComparison.OrdinalIgnoreCase);
        var disabled = status.StartsWith("SCHEDULER_DISABLED_", StringComparison.OrdinalIgnoreCase);
        var displayStatus = status.ToUpperInvariant() switch
        {
            "SCHEDULER_ENABLED" => "Running",
            "SCHEDULER_DISABLED_BY_OPERATOR" => "Stopped by operator",
            "SCHEDULER_DISABLED_STARTUP" => "Disabled at startup",
            "SCHEDULER_DISABLED_FAILOVER" => "Disabled by failover",
            _ => "Status unknown"
        };

        return new
        {
            status,
            isEnabled = enabled,
            isDisabled = disabled,
            displayStatus
        };
    }

    // Preserves MOVEit's meaningful HTTP status while replacing an opaque 403 with actionable guidance.
    private IActionResult ApiError(MoveItApiException ex, string fallbackMessage)
    {
        if (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "MOVEit Scheduler control requires membership in the MOVEit Admin Windows user group."
            });
        }

        var statusCode = ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : StatusCodes.Status502BadGateway;
        return StatusCode(statusCode, new { message = string.IsNullOrWhiteSpace(ex.Message) ? fallbackMessage : ex.Message });
    }
}
