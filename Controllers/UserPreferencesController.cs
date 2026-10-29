// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;

namespace MoveItAutomationUI.Controllers;

[Authorize]
public sealed class UserPreferencesController : Controller
{
    private readonly IUserPreferencesService _preferences;
    private readonly ILogger<UserPreferencesController> _logger;

    // Purpose: Initializes user preferences controller with its required dependencies.
    public UserPreferencesController(IUserPreferencesService preferences, ILogger<UserPreferencesController> logger)
    {
        _preferences = preferences;
        _logger = logger;
    }

    // Purpose: Returns the current user-specific settings or preferences.
    [HttpGet]
    public async Task<IActionResult> Current(CancellationToken cancellationToken)
    {
        var preferences = await _preferences.GetCurrentAsync(cancellationToken);
        return Json(new
        {
            preferences = new
            {
                theme = preferences.Theme,
                startPage = preferences.StartPage,
                gridPageSize = preferences.GridPageSize
            },
            choices = new
            {
                themes = new[] { UserPreferenceThemes.Light, UserPreferenceThemes.Dark },
                startPages = UserPreferenceStartPages.Allowed,
                gridPageSizes = _preferences.GetGridPageSizeChoices()
            }
        });
    }

    // Purpose: Saves the current user preference set after validation.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save([FromBody] UserPreferencesSaveRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var saved = await _preferences.SaveCurrentAsync(request, cancellationToken);
            return Json(new
            {
                success = true,
                preferences = new
                {
                    theme = saved.Theme,
                    startPage = saved.StartPage,
                    gridPageSize = saved.GridPageSize
                }
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to save MAUI user preferences for {Username}.", User.Identity?.Name ?? "unknown");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "The user preferences could not be saved." });
        }
    }

    // Purpose: Updates the current user theme preference.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Theme([FromBody] UserPreferenceValueRequest<string>? request, CancellationToken cancellationToken)
    {
        try
        {
            var saved = await _preferences.UpdateCurrentThemeAsync(request?.Value ?? string.Empty, cancellationToken);
            return Json(new { success = true, theme = saved.Theme });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to save MAUI theme preference for {Username}.", User.Identity?.Name ?? "unknown");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "The theme preference could not be saved." });
        }
    }

    // Purpose: Saves one current-user grid column layout in MAUI preferences.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GridLayout([FromBody] UserGridLayoutSaveRequest? request, CancellationToken cancellationToken)
    {
        try
        {
            if (request is null) return BadRequest(new { message = "Grid layout request is required." });
            var saved = await _preferences.SaveCurrentGridLayoutAsync(request.GridKey, request.Layout, cancellationToken);
            return Json(new
            {
                success = true,
                gridKey = saved.GridKey,
                layout = saved.Layout
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to save MAUI grid layout preference {GridKey} for {Username}.", request?.GridKey, User.Identity?.Name ?? "unknown");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "The grid layout preference could not be saved." });
        }
    }

    // Purpose: Removes one current-user grid column layout so MAUI grid defaults apply again.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetGridLayout([FromBody] UserGridLayoutResetRequest? request, CancellationToken cancellationToken)
    {
        try
        {
            if (request is null) return BadRequest(new { message = "Grid layout request is required." });
            await _preferences.DeleteCurrentGridLayoutAsync(request.GridKey, cancellationToken);
            return Json(new { success = true, gridKey = request.GridKey });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to reset MAUI grid layout preference {GridKey} for {Username}.", request?.GridKey, User.Identity?.Name ?? "unknown");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "The grid layout preference could not be reset." });
        }
    }

    // Purpose: Updates the current user grid page-size preference.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GridPageSize([FromBody] UserPreferenceValueRequest<int>? request, CancellationToken cancellationToken)
    {
        try
        {
            var saved = await _preferences.UpdateCurrentGridPageSizeAsync(request?.Value ?? 0, cancellationToken);
            return Json(new { success = true, gridPageSize = saved.GridPageSize });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to save MAUI grid page-size preference for {Username}.", User.Identity?.Name ?? "unknown");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "The grid page-size preference could not be saved." });
        }
    }
}
