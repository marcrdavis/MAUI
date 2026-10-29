// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Authenticated MAUI administrator upgrade workflow.

using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Upgrade;
using MoveItAutomationUI.Services.Lifecycle;

namespace MoveItAutomationUI.Controllers;

[Authorize(Roles = MauiSecurityRoles.Admin)]
public sealed class UpgradeController : Controller
{
    private readonly IMauiUpgradeService _upgradeService;
    private readonly MauiUpgradeState _upgradeState;
    private readonly IApplicationRestartService _restartService;
    private readonly ILogger<UpgradeController> _logger;

    // Purpose: Initializes upgrade controller with its required dependencies.
    public UpgradeController(
        IMauiUpgradeService upgradeService,
        MauiUpgradeState upgradeState,
        IApplicationRestartService restartService,
        ILogger<UpgradeController> logger)
    {
        _upgradeService = upgradeService;
        _upgradeState = upgradeState;
        _restartService = restartService;
        _logger = logger;
    }

    // Purpose: Renders the default page for this workspace.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index()
    {
        if (!_upgradeState.IsPending) return NotFound();
        return View(_upgradeService.GetViewModel());
    }

    // Purpose: Executes the pending MAUI upgrade and streams progress to the client.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task Run(CancellationToken cancellationToken)
    {
        if (!_upgradeState.IsPending)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "application/x-ndjson; charset=utf-8";
        Response.Headers["Cache-Control"] = "no-store";
        Response.Headers["X-Accel-Buffering"] = "no";

        async Task WriteEventAsync(object payload)
        {
            var json = JsonSerializer.Serialize(payload);
            await Response.WriteAsync(json + "\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }

        try
        {
            var result = await _upgradeService.ExecuteAsync(
                User.Identity?.Name ?? string.Empty,
                progress => WriteEventAsync(new
                {
                    type = "progress",
                    stage = progress.Stage,
                    message = progress.Message
                }),
                cancellationToken);

            await WriteEventAsync(new
            {
                type = "success",
                message = "Upgrade completed successfully.",
                fromVersion = result.FromVersion,
                toVersion = result.ToVersion,
                schemaVersion = result.SchemaVersion,
                configurationVersion = result.ConfigurationVersion,
                backupFile = result.BackupFile,
                databaseBackupFile = result.DatabaseBackupFile,
                restartRequired = result.RestartRequired,
                restartReasons = result.RestartReasons
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("MAUI upgrade request was cancelled by the client or request host.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MAUI upgrade execution failed.");
            try
            {
                await WriteEventAsync(new
                {
                    type = "error",
                    message = ex.Message
                });
            }
            catch
            {
                // The response connection may already be gone. The application log retains the failure.
            }
        }
    }

    // Purpose: Requests the controlled application restart required by the completed upgrade.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Restart()
    {
        if (!_upgradeState.PendingRestart)
            return Conflict(new { message = "The completed upgrade does not require an application restart." });

        var scheduled = _restartService.ScheduleRestart(
            $"Required after upgrade to {_upgradeState.TargetVersion}.");
        var alreadyScheduled = !scheduled && _restartService.IsRestartScheduled;
        if (!scheduled && !alreadyScheduled)
        {
            _logger.LogError("Unable to queue the controlled MAUI application restart after upgrade to {TargetVersion}.", _upgradeState.TargetVersion);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "MAUI could not queue the application restart. Retry Restart MAUI."
            });
        }

        _logger.LogInformation(
            "Controlled MAUI application restart {RestartState} after upgrade to {TargetVersion}.",
            scheduled ? "scheduled" : "was already scheduled",
            _upgradeState.TargetVersion);

        return Ok(new
        {
            accepted = true,
            alreadyScheduled
        });
    }

    // Purpose: Lets the Upgrade page determine when a controlled restart has completed.
    [AllowAnonymous]
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult RestartStatus()
    {
        return Ok(new
        {
            ready = !_upgradeState.IsPending && !_upgradeState.PendingRestart,
            requiresAttention = !string.IsNullOrWhiteSpace(_upgradeState.StartupError),
            version = _upgradeState.InstalledVersion
        });
    }
}

