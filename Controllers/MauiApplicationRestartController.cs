// Project: MAUI - MOVEit Automation UI
// Administrative application restart, without changing configuration or local data.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Lifecycle;

namespace MoveItAutomationUI.Controllers;

[Authorize(Policy = MauiSecurityPolicies.Admin)]
public sealed class MauiApplicationRestartController : Controller
{
    // Identifies this process for browser polling; it changes only after the host restarts.
    private static readonly string ProcessInstanceId = Guid.NewGuid().ToString("N");

    private readonly IApplicationRestartService _restart;
    private readonly ILogger<MauiApplicationRestartController> _logger;

    public MauiApplicationRestartController(
        IApplicationRestartService restart,
        ILogger<MauiApplicationRestartController> logger)
    {
        _restart = restart;
        _logger = logger;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Restart()
    {
        if (_restart.IsRestartScheduled)
            return Conflict(new { message = "An application restart is already scheduled." });

        // Log the initiating principal before queueing shutdown; an exception after queueing
        // could otherwise report failure even though StopApplication is already scheduled.
        _logger.LogInformation("Application restart requested by MAUI administrator {User}.", User.Identity?.Name ?? "(unknown)");
        if (!_restart.ScheduleRestart("Requested by a MAUI administrator from Advanced Settings."))
        {
            if (_restart.IsRestartScheduled)
                return Conflict(new { message = "An application restart is already scheduled." });

            _logger.LogError("Unable to schedule a restart from Advanced Settings.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "MAUI could not schedule the restart. Please try again." });
        }

        return Ok(new { accepted = true, instanceId = ProcessInstanceId });
    }

    // Public process-generation probe: must remain reachable after the old session is lost.
    // Returns no deployment configuration, tokens, or user information.
    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Status()
        => Ok(new { instanceId = ProcessInstanceId });
}
