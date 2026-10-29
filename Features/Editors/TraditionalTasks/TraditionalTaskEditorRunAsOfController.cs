using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Tasks;

namespace MoveItAutomationUI.Controllers;

public sealed partial class TraditionalTaskEditorController
{
    public sealed record RunAsOfRequest(string JobId, string DateTime, string EditVersion);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [MoveItObjectPermission("Task", "EditRun", "id")]
    public async Task<IActionResult> RunAsOf(string id, [FromBody] RunAsOfRequest? request, [FromServices] ITaskRunAsOfService runs, CancellationToken cancellationToken)
    {
        var session = _sessionService.Get();
        if (session is null) return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        if (request is null || !System.DateTime.TryParseExact(request.DateTime, ["yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var selected))
            return BadRequest(new { message = "Select a valid date and time." });
        try
        {
            var definition = await _editorService.GetDefinitionAsync(id, cancellationToken);
            if (!definition.Task.CanAlter || !definition.Task.CanRun) return Forbid();
            if (definition.Task.Type == "Synchronization") return BadRequest(new { message = "Run As Of is available for Traditional and Advanced Tasks." });
            using var edit = ObjectEditVersion.Require("Task", id, request.EditVersion);
            var owner = User.FindFirst(MoveItSessionService.SessionIdClaimType)?.Value ?? session.Username;
            return Json(await runs.RunAsync(id, request.JobId, selected, session.Username, owner, session.GetEnvironmentKey()));
        }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException)
        { return MauiControllerExceptionMapper.Map(ex); }
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult RunAsOfProgress(string jobId, [FromServices] TaskRunAsOfProgress progress)
    {
        var session = _sessionService.Get();
        if (session is null) return Unauthorized();
        var owner = User.FindFirst(MoveItSessionService.SessionIdClaimType)?.Value ?? session.Username;
        var state = progress.Get(owner, jobId);
        return state is null ? NotFound() : Json(state);
    }
}
