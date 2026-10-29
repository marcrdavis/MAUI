// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Restricts first-run installations to Initial Setup and redirects stale Setup page requests after configuration is active.

using MoveItAutomationUI.Services.Configuration;

namespace MoveItAutomationUI.Services.Lifecycle;

public sealed class InitialSetupAccessMiddleware
{
    private readonly RequestDelegate _next;

    // Purpose: Initializes the first-run setup access gate.
    public InitialSetupAccessMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    // Purpose: Enforces exclusive Setup access before routing and authentication execute.
    public async Task InvokeAsync(HttpContext context, MauiSetupState setupState)
    {
        var isSetupPath = MauiLifecyclePaths.IsSetup(context.Request.Path);

        if (setupState.IsConfiguredAtStartup && isSetupPath)
        {
            // A browser may still have the Setup page open after in-process activation.
            // Browser navigation back to Setup should recover naturally to the normal MAUI login page,
            // while Setup POST/API endpoints remain unavailable once configuration is active.
            if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
            {
                context.Response.Redirect(MauiLifecyclePaths.ForApplication(context.Request, MauiLifecyclePaths.Login));
                return;
            }

            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (!setupState.IsConfiguredAtStartup && !isSetupPath)
        {
            context.Response.Redirect(MauiLifecyclePaths.ForApplication(context.Request, MauiLifecyclePaths.Setup));
            return;
        }

        await _next(context);
    }
}
