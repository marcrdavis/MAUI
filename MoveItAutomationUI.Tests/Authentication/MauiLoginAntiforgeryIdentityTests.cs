// Project: MAUI - MOVEit Automation UI
// Tests actual ASP.NET Core request-token validation across stale login/logout principals.
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using MoveItAutomationUI.Services.Authentication;

namespace MoveItAutomationUI.Tests.Authentication;

public sealed class MauiLoginAntiforgeryIdentityTests
{
    [Theory]
    [InlineData("")]
    [InlineData("/maui")]
    [InlineData("/Applications/QA")]
    public async Task LoginToken_ValidatesDespiteAuthenticationIdentityTransition(string pathBase)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddAntiforgery(options => options.Cookie.Name = MauiAuthenticationDefaults.AntiforgeryCookieName);
        using var provider = services.BuildServiceProvider();
        var antiforgery = provider.GetRequiredService<IAntiforgery>();

        // Login GET may arrive with a principal left over from an earlier authenticated session.
        var loginGet = CreateContext(provider, "GET", pathBase, "previous-user");
        MauiLoginAntiforgeryIdentity.UseAnonymousPrincipal(loginGet);
        var tokens = antiforgery.GetAndStoreTokens(loginGet);
        var formToken = Assert.IsType<string>(tokens.RequestToken);
        var cookieHeader = loginGet.Response.Headers["Set-Cookie"].ToString().Split(';')[0];
        Assert.Contains(MauiAuthenticationDefaults.AntiforgeryCookieName, cookieHeader);

        // Demonstrate the original failure: a valid anonymous login form rejected because
        // POST was associated with an authenticated cookie principal.
        var unnormalizedPost = CreateContext(provider, "POST", pathBase, "previous-user");
        SetFormAndCookie(unnormalizedPost, cookieHeader, formToken);
        var error = await Assert.ThrowsAsync<AntiforgeryValidationException>(() =>
            antiforgery.ValidateRequestAsync(unnormalizedPost));
        Assert.Equal(MauiAntiforgeryFailure.Identity, MauiAntiforgeryFailure.Classify(error));

        // The corrected Login POST explicitly validates against the same anonymous identity.
        var loginPost = CreateContext(provider, "POST", pathBase, "previous-user");
        SetFormAndCookie(loginPost, cookieHeader, formToken);
        MauiLoginAntiforgeryIdentity.UseAnonymousPrincipal(loginPost);
        await antiforgery.ValidateRequestAsync(loginPost);

        // A real forged request token is still rejected; no antiforgery enforcement is removed.
        var invalidPost = CreateContext(provider, "POST", pathBase, "previous-user");
        SetFormAndCookie(invalidPost, cookieHeader, "invalid-token");
        MauiLoginAntiforgeryIdentity.UseAnonymousPrincipal(invalidPost);
        await Assert.ThrowsAsync<AntiforgeryValidationException>(() =>
            antiforgery.ValidateRequestAsync(invalidPost));
    }

    private static DefaultHttpContext CreateContext(IServiceProvider provider, string method, string pathBase, string? identity)
    {
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Method = method;
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("maui.test");
        context.Request.PathBase = new PathString(pathBase);
        context.Request.Path = "/Account/Login";
        if (!string.IsNullOrWhiteSpace(identity))
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, identity),
                new Claim(ClaimTypes.Name, identity)
            }, CookieAuthenticationDefaults.AuthenticationScheme));
        }
        return context;
    }

    private static void SetFormAndCookie(DefaultHttpContext httpContext, string cookieHeader, string formToken)
    {
        httpContext.Request.ContentType = "application/x-www-form-urlencoded";
        httpContext.Request.Headers.Cookie = cookieHeader;
        httpContext.Request.Form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["__RequestVerificationToken"] = formToken
        });
    }
}
