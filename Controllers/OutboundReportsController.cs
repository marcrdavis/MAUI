// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// HTTP endpoints for creating/sending Reports and managing global SMTP operations.
// Saved/scheduled Report administration is owned by ReportsController.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Reporting;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Controllers;

[Authorize]
public sealed class OutboundReportsController : Controller
{
    private readonly IOutboundReportService _reports;
    private readonly IOutboundReportEmailService _email;
    private readonly IReportConfigurationStore _configuration;
    private readonly ILogger<OutboundReportsController> _logger;

    public OutboundReportsController(
        IOutboundReportService reports,
        IOutboundReportEmailService email,
        IReportConfigurationStore configuration,
        ILogger<OutboundReportsController> logger)
    {
        _reports = reports;
        _email = email;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromBody] CreateOutboundReportRequest? request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequest(new { message = "Report details are required." });
        try { return Json(await _reports.CreateAsync(request, cancellationToken)); }
        catch (MoveItAuthenticationException ex)
        {
            return StatusCode(StatusCodes.Status401Unauthorized, new { message = ex.Message });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Net.Mail.SmtpException)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (DbUpdateException ex)
        {
            var traceId = HttpContext.TraceIdentifier;
            _logger.LogError(ex,
                "Unable to save outbound report metadata for user {UserName}. Error={ErrorType}: {ErrorMessage}. TraceId={TraceId}",
                User.Identity?.Name ?? "unknown", ex.GetType().Name, ex.GetBaseException().Message, traceId);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = $"Unable to save the report definition to the MAUI metadata database. Verify that the current MAUI database schema is installed and try again. Reference: {traceId}."
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return StatusCode(499);
        }
        catch (Exception ex)
        {
            var traceId = HttpContext.TraceIdentifier;
            _logger.LogError(ex,
                "Unable to create or send outbound report for user {UserName}. Error={ErrorType}: {ErrorMessage}. TraceId={TraceId}",
                User.Identity?.Name ?? "unknown", ex.GetType().Name, ex.GetBaseException().Message, traceId);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = $"The report could not be processed because of an unexpected server error. Review the MAUI application log and reference {traceId}."
            });
        }
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult EmailStatus()
    {
        var status = _email.GetConfigurationStatus();
        return Json(new { configured = status.IsConfigured, message = status.Message });
    }

    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearSmtpPassword(CancellationToken cancellationToken)
    {
        try
        {
            var configurationRevision = await _configuration.ClearSmtpPasswordAsync(cancellationToken);
            return Json(new { success = true, message = "SMTP password cleared.", configurationRevision });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestSmtp([FromBody] SmtpTestRequest? request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequest(new { message = "Test recipient is required." });
        try
        {
            await _email.SendTestAsync(request.Recipient, cancellationToken);
            return Json(new { success = true, message = "SMTP test message sent." });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.Net.Mail.SmtpException)
        {
            return BadRequest(new { message = ex.Message });
        }
    }


}
