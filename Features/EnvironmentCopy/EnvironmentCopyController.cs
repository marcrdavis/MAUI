using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Features.EnvironmentCopy;

[Authorize(Policy = MauiSecurityPolicies.Admin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class EnvironmentCopyController(EnvironmentCopyCoordinator coordinator, EnvironmentCopyStore store, IMoveItSessionService sessions,
    ILogger<EnvironmentCopyController> logger) : Controller
{
    private string Owner => User.Identity?.Name ?? "";
    private async Task<IActionResult> Guard(Func<Task<object>> action)
    {
        // MAUI policy verifies the live browser role. Data Access accounts never confer rights on the caller.
        if (sessions.Get()?.Info?.IsAdmin != true) return StatusCode(403, new { message = "Both MOVEit and MAUI administrator access are required." });
        if (!coordinator.Enabled) return NotFound(new { message = "Copy Between Environments is disabled." });
        if (!ModelState.IsValid) return BadRequest(new { message = "Some copy options are missing or invalid. Check your selections and try again." });
        try { return Json(await action()); }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { return StatusCode(499); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (MoveItApiException) { return BadRequest(new { message = "MOVEit could not complete this read or validation. Verify the Data Access account and selected objects, then analyze again." }); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        catch (Exception ex)
        {
            logger.LogWarning("Environment copy request failed ({ErrorType}) for {Initiator}.", ex.GetType().Name, Owner);
            return StatusCode(500, new { message = "MAUI could not complete this copy request. No automatic retry was attempted. Inspect any operation report before copying again." });
        }
    }
    [HttpGet] public Task<IActionResult> Catalog() => Guard(() => Task.FromResult<object>(new { environments = coordinator.Environments(), currentEnvironment = sessions.Get()!.GetEnvironmentKey() }));
    [HttpGet] public Task<IActionResult> Connection(string environment, CancellationToken ct) => Guard(() => coordinator.ConnectionAsync(environment, ct));
    [HttpGet] public Task<IActionResult> Objects(string environment, CancellationToken ct) => Guard(() => coordinator.InventoryAsync(environment, ct));
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Selection([FromBody] CopySelectionRequest request, CancellationToken ct) => Guard(() => coordinator.SelectionAsync(request, ct));
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Analyze([FromBody] CopyAnalyzeRequest request, CancellationToken ct) => Guard(() => coordinator.AnalyzeAsync(request, Owner, ct));
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Resolve([FromBody] CopyResolveRequest request, CancellationToken ct) => Guard(() => coordinator.ResolveAsync(request, Owner, ct));
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Placement([FromBody] CopyReviewRequest request, CancellationToken ct) => Guard(() => coordinator.PlacementAsync(request, Owner, ct));
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Review([FromBody] CopyReviewRequest request, CancellationToken ct) => Guard(() => coordinator.ReviewAsync(request, Owner, ct));
    [HttpGet] public Task<IActionResult> Differences(string planId, string key, string targetId, CancellationToken ct) => Guard(() => coordinator.DifferencesAsync(planId, key, targetId, Owner, ct));
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Start([FromBody] CopyStartRequest request, CancellationToken ct) => Guard(() => coordinator.StartAsync(request, Owner, ct));
    [HttpGet] public Task<IActionResult> Status(string operationId, CancellationToken ct) => Guard(async () => await store.ReadReportAsync(operationId, Owner, ct));
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Cancel([FromBody] CopyStartRequest request) => Guard(() => { store.RequestCancel(request.PlanId, Owner); return Task.FromResult<object>(new { message = "Copy will stop before the next object write." }); });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Discard([FromBody] CopyStartRequest request) => Guard(async () => { await store.DiscardAsync(request.PlanId, Owner); return new { discarded = true }; });
    [HttpGet] public async Task<IActionResult> Report(string operationId, CancellationToken ct)
    {
        var result = await Guard(async () => await store.ReadReportAsync(operationId, Owner, ct));
        if (result is not JsonResult json) return result;
        return File(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(json.Value, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), "application/json", "MAUI-Copy-" + operationId + ".json");
    }
    [HttpGet] public async Task<IActionResult> Recovery(string operationId, string key, CancellationToken ct)
    {
        var result = await Guard(async () => await store.ReadRecoveryAsync(operationId, key, Owner, ct));
        if (result is not JsonResult { Value: CopyRecoveryDefinition recovery }) return result;
        // A separate deliberate download, never embedded in reports/comparison responses or automatically restored.
        return File(System.Text.Encoding.UTF8.GetBytes(recovery.Xml), "application/xml", "MAUI-Recovery-" + operationId + "-" + recovery.Type + ".xml");
    }
}
