// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Admin management endpoints for reusable task-notification message templates.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Controllers;

public sealed partial class ConfigurationController
{
    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> NotificationTemplates(CancellationToken cancellationToken = default)
    {
        try
        {
            var templates = await _notificationTemplateService.GetCurrentAsync(cancellationToken);
            return Json(new
            {
                success = true,
                templates = templates.Select(template => new
                {
                    id = template.Id,
                    name = template.Name,
                    subject = template.Subject, message = template.Message, format = template.Format
                })
            });
        }
        catch (MoveItAuthenticationException)
        {
            return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unable to load notification templates for {UserName}.", User.Identity?.Name ?? "unknown");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Notification templates could not be loaded. Review Configuration > Logs for details." });
        }
    }

    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveNotificationTemplate([FromBody] TaskNotificationTemplateSaveRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null) return BadRequest(new { message = "Template data is required." });
        try
        {
            var template = await _notificationTemplateService.SaveCurrentAsync(request, cancellationToken);
            return Json(new { success = true, template = new { id = template.Id, name = template.Name,
                subject = template.Subject, message = template.Message, format = template.Format } });
        }
        catch (MoveItAuthenticationException)
        {
            return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unable to save notification template for {UserName}.", User.Identity?.Name ?? "unknown");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "The notification template could not be saved. Review Configuration > Logs for details." });
        }
    }

    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RenameNotificationTemplate([FromBody] TaskNotificationTemplateRenameRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null) return BadRequest(new { message = "Template data is required." });
        try
        {
            var template = await _notificationTemplateService.RenameCurrentAsync(request.TemplateId, request.Name, cancellationToken);
            return Json(new
            {
                success = true,
                template = new { id = template.Id, name = template.Name },
                message = $"Notification template renamed to {template.Name}."
            });
        }
        catch (MoveItAuthenticationException)
        {
            return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unable to rename notification template {TemplateId} for {UserName}.", request.TemplateId, User.Identity?.Name ?? "unknown");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "The notification template could not be renamed. Review Configuration > Logs for details." });
        }
    }

    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteNotificationTemplate([FromBody] TaskNotificationTemplateDeleteRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null) return BadRequest(new { message = "Template data is required." });
        try
        {
            await _notificationTemplateService.DeleteCurrentAsync(request.TemplateId, cancellationToken);
            return Json(new { success = true, message = "Notification template removed." });
        }
        catch (MoveItAuthenticationException)
        {
            return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unable to remove notification template {TemplateId} for {UserName}.", request.TemplateId, User.Identity?.Name ?? "unknown");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "The notification template could not be removed. Review Configuration > Logs for details." });
        }
    }
}
