// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// First-run configuration endpoints. No MOVEit credentials are collected or stored here.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;

namespace MoveItAutomationUI.Controllers;

[AllowAnonymous]
public sealed class SetupController : Controller
{
    private readonly IInitialSetupService _setupService;
    private readonly MauiSetupState _setupState;
    private readonly ILogger<SetupController> _logger;

    // Purpose: Initializes setup controller with its required dependencies.
    public SetupController(
        IInitialSetupService setupService,
        MauiSetupState setupState,
        ILogger<SetupController> logger)
    {
        _setupService = setupService;
        _setupState = setupState;
        _logger = logger;
    }

    // Purpose: Renders the default page for this workspace.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index()
    {
        if (_setupState.IsConfiguredAtStartup) return NotFound();
        return View(_setupService.GetViewModel());
    }

    // Purpose: Tests the proposed MAUI metadata database without saving it.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestDatabase([FromBody] SetupDatabaseRequest? request, CancellationToken cancellationToken)
    {
        if (_setupState.IsConfiguredAtStartup) return NotFound();
        if (request is null) return BadRequest(new { message = "Database configuration is required." });
        try
        {
            var result = await _setupService.TestDatabaseAsync(request, cancellationToken);
            return Json(new { success = true, message = result.Message, result.Provider, result.Database, result.DataSource });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Microsoft.Data.SqlClient.SqlException or Microsoft.Data.Sqlite.SqliteException)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // Purpose: Tests the proposed MOVEit Automation connection without saving it.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestMoveIt([FromBody] SetupMoveItRequest? request, CancellationToken cancellationToken)
    {
        if (_setupState.IsConfiguredAtStartup) return NotFound();
        if (request is null) return BadRequest(new { message = "MOVEit configuration is required." });
        try
        {
            var message = await _setupService.TestMoveItAsync(request, cancellationToken);
            return Json(new { success = true, message });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // Purpose: Completes Initial Setup after both required connection tests pass.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete([FromBody] InitialSetupRequest? request, CancellationToken cancellationToken)
    {
        if (_setupState.IsConfiguredAtStartup) return NotFound();
        if (request is null) return BadRequest(new { message = "Initial setup configuration is required." });
        try
        {
            var result = await _setupService.CompleteAsync(request, cancellationToken);
            return Json(new
            {
                success = true,
                restartRequired = result.RestartRequired,
                result.ServerKey,
                result.EnvironmentKey,
                result.DatabaseProvider,
                message = "Initial configuration and database setup completed successfully. MAUI is ready for login.",
                loginUrl = Url.Action("Login", "Account")
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (AppSettingsDeploymentException ex)
        {
            // Only the curated, non-sensitive deployment guidance is displayed before login.
            _logger.LogError(ex, "Unable to save Initial Setup configuration under the deployment file ACL.");
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException
            or Microsoft.Data.Sqlite.SqliteException
            or HttpRequestException
            or TaskCanceledException)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to complete initial MAUI setup.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Initial setup could not be saved. Review the application log for details." });
        }
    }
}
