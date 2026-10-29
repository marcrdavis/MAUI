// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// A dedicated administrative endpoint for the irreversible installation reset.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Services.Lifecycle;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Controllers;

[Authorize(Policy = MauiSecurityPolicies.Admin)]
public sealed class MauiFactoryResetController : Controller
{
    private readonly MauiFactoryResetService _reset;
    private readonly ILogger<MauiFactoryResetController> _logger;

    public MauiFactoryResetController(MauiFactoryResetService reset, ILogger<MauiFactoryResetController> logger)
    {
        _reset = reset;
        _logger = logger;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Execute([FromBody] FactoryResetRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || !string.Equals(request.Confirmation, MauiFactoryResetService.RequiredConfirmation, StringComparison.Ordinal))
            return BadRequest(new { message = "Type RESET exactly to confirm factory reset." });

        try
        {
            await _reset.ResetAsync(HttpContext, request.Confirmation, cancellationToken);
            return Json(new
            {
                success = true,
                message = "Factory reset accepted. MAUI is stopping; the Data directory will be cleared before its next startup.",
                setupUrl = MauiLifecyclePaths.BrowserUrl(Request.PathBase, MauiLifecyclePaths.Setup)
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status408RequestTimeout, new { message = "The reset request was interrupted before completion." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Factory Reset could not begin.");
            return Conflict(new { message = "Factory reset could not begin. Check whether an application restart or reset is already pending." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Factory Reset preparation failed; configuration rollback was attempted.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Factory reset could not complete safely. Contact the deployment administrator before retrying." });
        }
    }

    public sealed class FactoryResetRequest
    {
        public string? Confirmation { get; set; }
    }
}
