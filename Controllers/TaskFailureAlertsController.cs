// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Controllers;

[Authorize]
public sealed class TaskFailureAlertsController : Controller
{
    private readonly ITaskFailureAlertService _alerts;
    private readonly UiOptions _uiOptions;

    public TaskFailureAlertsController(
        ITaskFailureAlertService alerts,
        IOptionsSnapshot<UiOptions> uiOptions)
    {
        _alerts = alerts;
        _uiOptions = uiOptions.Value;
    }

    // Polls for failed Task Runs created since this login session's previous alert poll.
    // The first enabled poll establishes a watermark and intentionally does not replay historical failures.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Poll(CancellationToken cancellationToken = default)
    {
        if (!_uiOptions.TaskFailureAlertsEnabled.Value)
        {
            await _alerts.ResetAsync(cancellationToken);
            return Json(new { enabled = false, failures = Array.Empty<object>() });
        }

        try
        {
            var result = await _alerts.PollAsync(cancellationToken);
            return Json(new
            {
                enabled = true,
                initialized = result.Initialized,
                failures = result.Failures.Select(item => new
                {
                    runId = item.RunId,
                    taskId = item.TaskId,
                    taskName = item.TaskName,
                    message = item.Message
                })
            });
        }
        catch (Exception ex) when (ex is MoveItApiException or ArgumentException or InvalidOperationException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }
}
