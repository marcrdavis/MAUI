// Project: MAUI - MOVEit Automation UI
// Centralizes the anonymous principal contract of the Login form and its POST.

using System.Security.Claims;

namespace MoveItAutomationUI.Services.Authentication;

public static class MauiLoginAntiforgeryIdentity
{
    // [AllowAnonymous] bypasses authorization, not authentication. A previously valid MAUI
    // authentication cookie may otherwise cause the antiforgery request token to be issued
    // or validated under a different claims principal across login/logout transitions.
    // Use only for the anonymous Login GET and POST; other secured endpoints remain bound
    // to the actual authenticated user for their normal antiforgery checks.
    public static void UseAnonymousPrincipal(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
    }
}
