using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Features.Editors.Hosts;

namespace MoveItAutomationUI.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ObjectEditLeaseController(IObjectEditLeaseService leases, IMoveItSessionService sessions,
    ITraditionalTaskEditorService tasks, IHostEditorService hosts,
    Services.MoveIt.Resources.Scripts.ScriptCrudCoordinator? scripts = null,
    Services.MoveIt.Resources.DateLists.DateListCrudCoordinator? dateLists = null,
    ILogger<ObjectEditLeaseController>? logger = null) : Controller
{
    public sealed record LeaseRequest(string Type = "", string Id = "", string LeaseId = "");
    private ObjectEditLeaseOwner? Owner()
    {
        var session = sessions.Get();
        var id = User.FindFirst(MoveItSessionService.SessionIdClaimType)?.Value;
        return session is null || string.IsNullOrEmpty(id) ? null : new(id, session);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Open([FromBody] LeaseRequest request, [FromServices] IMoveItObjectAccessService access, CancellationToken ct)
    {
        var owner = Owner(); if (owner is null) return Unauthorized();
        try
        {
            await access.RequireAsync(request.Type, request.Id, "Edit", ct);
            // Shared object permissions authorize editing. Native checkout availability
            // is decided by POST /checkout, not by an editor's cached CanAlter flag.
            var editable = request.Type switch
            {
                "Task" or "Host" => true,
                "Script" => scripts?.RequireSession() is not null,
                "DateList" => dateLists?.RequireSession() is not null,
                _ => false
            };
            if (!editable) return Forbid();
            return Json(await leases.OpenAsync(request.Type, request.Id, owner, ct));
        }
        catch (MoveItAuthenticationException) { return Unauthorized(new { message = "MOVEit session expired. Close this editor and sign in again." }); }
        catch (ObjectCheckoutRejectedException ex) { return Conflict(ex.Feedback()); }
        catch (Exception ex) when (ObjectCheckout.IsRequestFailure(ex) && !ct.IsCancellationRequested)
        {
            logger?.LogError(ex, "MOVEit checkout request failed for {Type} {Id}.", request.Type, request.Id);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = ObjectCheckout.RequestFailedMessage });
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or MoveItApiException or MoveItComException)
        { return Conflict(new { message = ex.Message }); }
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Override([FromBody] LeaseRequest request, CancellationToken ct)
    {
        var owner = Owner(); if (owner is null) return Unauthorized();
        if (owner.Session.Info?.IsAdmin != true) return Forbid();
        ObjectEditLeaseResult? recoveredLease = null;
        var completed = false;
        try
        {
            var lease = await leases.OverrideAsync(request.Type, request.Id, owner, ct);
            recoveredLease = lease;
            // Read the same editor revision under the newly acquired checkout. Retained browser
            // edits still go through normal composition, permissions and revision checks on Save.
            using var operation = await leases.EnterAsync(request.Type, request.Id, lease.LeaseId, owner, ct);
            using var lifetime = ObjectCheckout.ForLifetime(ObjectEditLeaseService.CheckoutLifetime(operation));
            var revision = request.Type switch
            {
                "Task" => (await tasks.GetDefinitionAsync(request.Id, ct)).EditVersion,
                "Host" => (await hosts.GetHostAsync(request.Id, ct)).EditVersion,
                "Script" => scripts is not null ? (await scripts.ReadAsync(request.Id, ct)).EditVersion : throw new ArgumentException("Script editor is unavailable."),
                "DateList" => dateLists is not null ? (await dateLists.ReadAsync(request.Id, ct)).EditVersion : throw new ArgumentException("Date List editor is unavailable."),
                _ => throw new ArgumentException("Unsupported object type.")
            };
            if (string.IsNullOrWhiteSpace(revision)) throw new InvalidOperationException("The current editor revision is unavailable.");
            completed = true;
            return Json(new { success = true, leaseId = lease.LeaseId, editVersion = revision });
        }
        catch (MoveItAuthenticationException) { return Unauthorized(new { message = "MOVEit session expired. Close this editor and sign in again." }); }
        catch (ObjectCheckoutRejectedException ex) { return Conflict(ex.Feedback()); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or MoveItApiException or MoveItComException
            || ObjectCheckout.IsRequestFailure(ex) && !ct.IsCancellationRequested)
        {
            logger?.LogError(ex, "Checkout override failed for {Type} {Id} on {Server} during {Stage}.", request.Type, request.Id,
                owner.Session.WebAdminUrl, recoveredLease is null ? "acquisition" : "editor revision read");
            return StatusCode(ObjectCheckout.IsRequestFailure(ex) ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status409Conflict,
                new { message = ObjectCheckout.OverrideFailedMessage });
        }
        finally
        {
            // A failed fresh read must not strand a new checkout unknown to the browser.
            if (!completed && recoveredLease is not null)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                try { await leases.CloseAsync(recoveredLease.LeaseId, owner, cleanup.Token); }
                catch (Exception ex) when (ex is InvalidOperationException or MoveItApiException or MoveItComException or OperationCanceledException or HttpRequestException or IOException)
                { /* Durable cleanup remains eligible for the next background sweep. */ }
            }
        }
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Recover([FromBody] LeaseRequest request,
        [FromServices] IResourceGroupManagementService groups,
        [FromServices] IAutomationGlobalParameterService parameters,
        [FromServices] Services.MoveIt.SystemSettings.ISystemSettingsService settings, CancellationToken ct)
    {
        var owner = Owner(); if (owner is null) return Unauthorized();
        if (owner.Session.Info?.IsAdmin != true) return Forbid();
        try
        {
            // Singleton datasets have short checkout lifetimes, not an entity editor lease.
            // Reuse their authoritative read/release paths; never replay the failed user operation.
            if (request.Type == "MOVEit Settings") await settings.RecoverCheckoutAsync(ct);
            else if (request.Type == "Resource Groups") await groups.RecoverCheckoutAsync(ct);
            else if (request.Type == "Global Parameters") await parameters.RecoverCheckoutAsync(ct);
            else return BadRequest(new { message = "Select a supported checkout to recover." });
            return Json(new { success = true });
        }
        catch (MoveItAuthenticationException) { return Unauthorized(new { message = "MOVEit session expired. Close this editor and sign in again." }); }
        catch (ObjectCheckoutRejectedException ex) { return Conflict(ex.Feedback()); }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException
            || ObjectCheckout.IsRequestFailure(ex) && !ct.IsCancellationRequested)
        {
            logger?.LogError(ex, "MOVEit checkout override failed for {Type}.", request.Type);
            return StatusCode(ObjectCheckout.IsRequestFailure(ex) ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status409Conflict,
                new { message = ObjectCheckout.OverrideFailedMessage });
        }
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Heartbeat([FromBody] LeaseRequest request, CancellationToken ct)
    {
        var owner = Owner(); if (owner is null) return Unauthorized();
        try { await leases.HeartbeatAsync(request.LeaseId, owner, ct); return Json(new { success = true }); }
        catch (ObjectEditLeaseException ex) { return Conflict(new { message = ex.Message, leaseLost = true }); }
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Close([FromBody] LeaseRequest request)
    {
        var owner = Owner(); if (owner is null) return Unauthorized();
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await leases.CloseAsync(request.LeaseId, owner, cleanup.Token); return Json(new { success = true }); }
        catch (Exception ex) when (ex is InvalidOperationException or MoveItApiException or MoveItComException || ObjectCheckout.IsRequestFailure(ex))
        { return Conflict(new { message = ObjectCheckout.ReleaseFailedMessage }); }
    }
}
