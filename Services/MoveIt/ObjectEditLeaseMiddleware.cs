using Microsoft.AspNetCore.Mvc.Controllers;
using MoveItAutomationUI.Services.Configuration;

namespace MoveItAutomationUI.Services.MoveIt;

// Establish the server scope for every shared CRUD checkout and borrow lifetime leases per request.
public sealed class ObjectEditLeaseMiddleware(RequestDelegate next, ILogger<ObjectEditLeaseMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, MauiSetupState setup)
    {
        if (!setup.IsConfiguredAtStartup || context.User.Identity?.IsAuthenticated != true)
        { await next(context); return; }
        var endpoint = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        if (endpoint?.ControllerName is "Upgrade" or "Setup") { await next(context); return; }
        var sessionId = context.User.FindFirst(MoveItSessionService.SessionIdClaimType)?.Value;
        var sessions = context.RequestServices.GetRequiredService<IMoveItSessionService>();
        var session = sessions.Get();
        var leases = context.RequestServices.GetRequiredService<IObjectEditLeaseService>();
        if (endpoint?.ControllerName == "Account" && endpoint.ActionName is "Logout" or "SessionExpired" && sessionId is not null)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { await leases.ReleaseSessionAsync(sessionId, cleanup.Token); }
            catch (OperationCanceledException)
            {
                // A busy save/run can outlast logout's cleanup budget. Logout must still revoke
                // the browser session; the durable lease remains available for background recovery.
                logger.LogWarning("Editing checkout cleanup exceeded the logout budget; remaining records are retained for recovery.");
            }
        }
        if (session is null || sessionId is null) { await next(context); return; }
        using var server = ObjectCheckout.ForServer(session.WebAdminUrl);
        using var recovery = context.RequestServices.GetService<IShortCheckoutRecovery>() is { } journal
            ? ObjectCheckout.ForRecovery(journal, session) : null;
        var token = context.Request.Headers["X-MAUI-Edit-Lease"].ToString();
        var save = endpoint?.ControllerName is "TraditionalTaskEditor" or "HostEditor" or "ScriptEditor" or "DateListEditor" or "TaskJsonEditor"
            && endpoint.ActionName is "Save" or "SyncAction" or "RunAsOf" && HttpMethods.IsPost(context.Request.Method);
        if ((endpoint?.ControllerName is "ScriptEditor" or "DateListEditor") && string.IsNullOrWhiteSpace(context.Request.Query["id"])) save = false;
        if (endpoint?.ControllerName == "ObjectEditLease" || string.IsNullOrEmpty(token) && !save)
        { await next(context); return; }
        try
        {
            var owner = new ObjectEditLeaseOwner(sessionId, session);
            using var operation = save
                ? await leases.EnterAsync(endpoint?.ControllerName switch { "HostEditor" => "Host", "ScriptEditor" => "Script", "DateListEditor" => "DateList", _ => "Task" },
                    context.Request.Query["id"].ToString(), token, owner, context.RequestAborted)
                : await leases.EnterTokenAsync(token, owner, context.RequestAborted);
            using var lifetime = ObjectCheckout.ForLifetime(ObjectEditLeaseService.CheckoutLifetime(operation));
            await next(context);
        }
        catch (ObjectEditLeaseException ex) when (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new { message = ex.Message, leaseLost = true, editConflict = true, moveItSaved = ex.MayHaveSaved });
        }
    }
}
