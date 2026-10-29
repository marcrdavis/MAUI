// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Administrator endpoints for MAUI-authored System Jobs shown under Configuration > Settings > Scheduler.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.SystemJobs;

namespace MoveItAutomationUI.Controllers;

[Authorize(Policy = MauiSecurityPolicies.Admin)]
public sealed class SystemJobsController : Controller
{
    private readonly ISystemJobService _service;
    private readonly ISystemJobRuntime _runtime;
    private readonly ILogger<SystemJobsController> _logger;

    public SystemJobsController(ISystemJobService service, ISystemJobRuntime runtime, ILogger<SystemJobsController> logger)
    {
        _service = service;
        _runtime = runtime;
        _logger = logger;
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> List(CancellationToken cancellationToken = default)
    {
        var jobs = await _service.GetJobsAsync(_runtime.IsActive, cancellationToken);
        return Json(new { success = true, jobs = jobs.Select(ToPayload) });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update([FromBody] MauiSystemJobUpdateRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null) return BadRequest(new { message = "System Job settings are required." });
        if (!_service.IsUserManageableJob(request.Key))
            return BadRequest(new { message = "Unknown or non-editable MAUI System Job." });
        if (_runtime.IsActive(request.Key))
            return StatusCode(StatusCodes.Status409Conflict, new { message = "This System Job is currently queued or running. Wait for it to finish before changing its schedule." });
        try
        {
            var job = await _service.UpdateAsync(request, _runtime.IsActive, cancellationToken);
            _logger.LogInformation("MAUI System Job {SystemJobKey} settings updated by {Username}. Enabled={Enabled}; Schedule={Schedule}.", job.Key, User.Identity?.Name ?? "unknown", job.Enabled, job.ScheduleDisplay);
            return Json(new { success = true, job = ToPayload(job), message = "System Job settings saved." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult RunNow([FromBody] MauiSystemJobRunRequest? request)
    {
        var key = (request?.Key ?? string.Empty).Trim();
        if (!_service.IsUserManageableJob(key)) return BadRequest(new { message = "Unknown or non-runnable MAUI System Job." });
        var queueResult = _runtime.QueueRunNow(key);
        if (queueResult == SystemJobQueueResult.AlreadyActive)
            return StatusCode(StatusCodes.Status409Conflict, new { message = "This System Job is already queued or running." });
        if (queueResult == SystemJobQueueResult.SchedulerUnavailable)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "System Jobs are temporarily unavailable while MAUI setup, restart, or upgrade work is pending." });

        _logger.LogInformation("MAUI System Job {SystemJobKey} queued for immediate execution by {Username}.", key, User.Identity?.Name ?? "unknown");
        return Json(new { success = true, queued = true, message = "System Job queued." });
    }

    private static object ToPayload(MauiSystemJobState job) => new
    {
        key = job.Key,
        name = job.Name,
        description = job.Description,
        enabled = job.Enabled,
        runTime = job.RunTime,
        scheduleDisplay = job.ScheduleDisplay,
        scheduleEditable = job.ScheduleEditable,
        nextRunUtc = job.NextRunUtc,
        nextRunDisplay = FormatDate(job.NextRunUtc),
        lastRunUtc = job.LastRunUtc,
        lastRunDisplay = FormatDate(job.LastRunUtc),
        lastResult = job.IsRunning ? "Running" : job.LastResult,
        lastMessage = job.LastMessage,
        isRunning = job.IsRunning
    };

    private static string FormatDate(DateTimeOffset? value)
        => value.HasValue ? value.Value.ToLocalTime().ToString("MMM d, yyyy  h:mm tt", System.Globalization.CultureInfo.CurrentCulture) : "—";
}
