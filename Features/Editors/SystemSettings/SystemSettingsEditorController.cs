using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.SystemSettings;

namespace MoveItAutomationUI.Controllers;

[Authorize(Policy = MauiSecurityPolicies.Admin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SystemSettingsEditorController(ISystemSettingsService settings, IConfiguration configuration,
    ILogger<SystemSettingsEditorController> logger) : Controller
{
    // Reading Globals acquires a short checkout, so opening/reloading is a protected POST.
    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Shell(CancellationToken ct) => RunAsync(() => settings.ReadAsync(ct));

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Lockout(CancellationToken ct) => RunAsync(() => settings.ReadLockoutAsync(ct));

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> ActiveLockouts(CancellationToken ct) => RunAsync(() => settings.ReadActiveLockoutsAsync(ct));

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(128 * 1024)]
    public Task<IActionResult> Unlock([FromBody] SystemUnlockRequest? request, CancellationToken ct)
    {
        if (!ModelState.IsValid || request is null || request.Selections is null)
            return Task.FromResult<IActionResult>(BadRequest(new { message = "Select one or more lockouts to unlock." }));
        return RunAsync(() => settings.UnlockAsync(request.Kind, request.Selections, ct));
    }

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(128 * 1024)]
    public Task<IActionResult> Save([FromBody] SystemSettingsSaveRequest? request, CancellationToken ct)
    {
        if (!ModelState.IsValid || request is null || string.IsNullOrWhiteSpace(request.EditVersion))
            return Task.FromResult<IActionResult>(BadRequest(new { message = "MOVEit Settings changes and the current editor revision are required." }));
        return RunAsync(() => settings.SaveAsync(new SystemSettingsUpdate(request.EditVersion, request.Logging, request.EventLog, request.Tasks, request.StateFile, request.Tamper, request.Lockout, request.LockoutVersion, request.TrustedIPs, request.TrustedVersion), ct));
    }

    private async Task<IActionResult> RunAsync<T>(Func<Task<T>> action)
    {
        // Read the published setting directly, as navigation and Groups and Permissions do.
        // Editor options are not registered with the options binder in application startup.
        if (!bool.TryParse(configuration["Editors:UseMauiSystemSettingsEditor:Value"], out var enabled) || !enabled)
            return NotFound(new { message = "The MAUI Settings Editor is disabled." });
        try { return Json(new { success = true, state = await action() }); }
        catch (MoveItAuthenticationException) { return Unauthorized(new { message = "MOVEit session expired. Please sign in again." }); }
        catch (ObjectCheckoutRejectedException ex) { return Conflict(ex.Feedback()); }
        catch (ObjectEditConflictException)
        { return Conflict(new { message = "MOVEit Settings changed in MOVEit or this editing session expired. Reload MOVEit Settings before saving again.", saveBlocked = true }); }
        catch (SystemActiveLockoutConflictException ex)
        { return Conflict(new { message = ex.Message, refreshLockouts = true }); }
        catch (SystemUnlockUncertainException ex)
        {
            logger.LogWarning(ex, "MOVEit Lockouts unlock result could not be confirmed.");
            return Conflict(new { message = ex.Message, refreshLockouts = true });
        }
        catch (SystemSettingsPartialSaveException ex)
        {
            logger.LogWarning(ex, "MOVEit Settings save could not be fully confirmed.");
            return Conflict(new { message = ex.Message, saveBlocked = true });
        }
        catch (SystemSettingsSaveUncertainException ex)
        {
            logger.LogWarning(ex, "MOVEit Settings save could not be confirmed.");
            return Conflict(new { message = ex.Message, saveBlocked = true });
        }
        catch (ObjectCheckoutReleaseException ex)
        {
            logger.LogWarning(ex, "MOVEit Settings checkout cleanup could not be confirmed.");
            return Conflict(new { message = "MOVEit Settings checkout cleanup could not be confirmed. Reload MOVEit Settings before continuing.", saveBlocked = true });
        }
        catch (MoveItApiException ex)
        {
            return StatusCode(ex.StatusCode == HttpStatusCode.Forbidden ? 403 : 400, new { message = ex.Message });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "MOVEit Settings operation failed.");
            return StatusCode(500, new { message = "MOVEit Settings could not be loaded or saved. Try again, or check the MAUI logs for details." });
        }
    }
}

public sealed record SystemSettingsSaveRequest(SystemLoggingSettings? Logging, string EditVersion,
    SystemEventLogSettings? EventLog = null, SystemTaskSettings? Tasks = null, SystemStateFileSettings? StateFile = null,
    SystemTamperSettings? Tamper = null, SystemLockoutPolicy? Lockout = null, string? LockoutVersion = null,
    IReadOnlyList<string>? TrustedIPs = null, string? TrustedVersion = null);

public sealed record SystemUnlockRequest(string Kind, IReadOnlyList<SystemLockoutSelection> Selections);
