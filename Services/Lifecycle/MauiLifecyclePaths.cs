// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Central lifecycle route constants used by setup, login, and upgrade access gates.

namespace MoveItAutomationUI.Services.Lifecycle;

public static class MauiLifecyclePaths
{
    public const string Setup = "/Setup";
    public const string Upgrade = "/Upgrade";
    public const string Login = "/Account/Login";
    public const string Logout = "/Account/Logout";
    public const string SessionExpired = "/Account/SessionExpired";

    // Explicit redirect destination for an IIS site-root or virtual/nested application.
    // Request.Path is already application-relative; redirects must include Request.PathBase.
    public static string BrowserUrl(PathString pathBase, string applicationRoute)
    {
        if (string.IsNullOrEmpty(applicationRoute) || applicationRoute[0] != '/' || applicationRoute.StartsWith("//", StringComparison.Ordinal))
            throw new ArgumentException("An application-relative route is required.", nameof(applicationRoute));
        return pathBase.Add(new PathString(applicationRoute)).ToUriComponent();
    }

    // Purpose: Builds a browser-facing lifecycle URL within the current IIS application.
    // Request.Path is already application-relative; redirects must add Request.PathBase.
    public static string ForApplication(HttpRequest request, string appRelativePath)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.PathBase.Add(new PathString(appRelativePath)).Value ?? appRelativePath;
    }

    // Purpose: Returns true when the request targets the Initial Setup surface.
    public static bool IsSetup(PathString path) => path.StartsWithSegments(Setup);

    // Purpose: Returns true when the request targets the Upgrade surface.
    public static bool IsUpgrade(PathString path) => path.StartsWithSegments(Upgrade);

    // Purpose: Returns true when the request targets the login endpoint.
    public static bool IsLogin(PathString path) => path.StartsWithSegments(Login);

    // Purpose: Returns true when the request targets the logout endpoint.
    public static bool IsLogout(PathString path) => path.StartsWithSegments(Logout);

    // Purpose: Returns true when the request targets the session-expired cleanup endpoint.
    public static bool IsSessionExpired(PathString path) => path.StartsWithSegments(SessionExpired);
}
