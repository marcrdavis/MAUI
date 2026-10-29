// RC2 login URL regression coverage for site-root and nested IIS virtual applications.
using Microsoft.AspNetCore.Http;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Lifecycle;

namespace MoveItAutomationUI.Tests.Lifecycle;

public sealed class LoginVirtualApplicationRedirectTests
{
    [Theory]
    [InlineData("", "Dashboard", "/Home/Index")]
    [InlineData("/MAUI", "Dashboard", "/MAUI/Home/Index")]
    [InlineData("/Apps/QA", "Dashboard", "/Apps/QA/Home/Index")]
    [InlineData("/Tasks", "Dashboard", "/Tasks/Home/Index")]
    [InlineData("/Records", "Dashboard", "/Records/Home/Index")]
    [InlineData("/MAUI", "Tasks", "/MAUI/Tasks/Index")]
    [InlineData("/Apps/QA", "Hosts", "/Apps/QA/Hosts/Index")]
    [InlineData("/MAUI", "invalid", "/MAUI/Home/Index")]
    [InlineData("/MAUI", null, "/MAUI/Home/Index")]
    public void LoginDestination_UsesCanonicalRouteWithinCurrentApplication(string pathBase, string? startPage, string expected)
    {
        var destination = MauiLifecyclePaths.BrowserUrl(new PathString(pathBase), UserPreferenceStartPages.ResolvePath(startPage));
        Assert.Equal(expected, destination);
    }

    [Theory]
    [InlineData("https://untrusted.invalid")]
    [InlineData("//untrusted.invalid")]
    [InlineData("Home/Index")]
    [InlineData("")]
    public void BrowserUrl_RejectsNonApplicationPaths(string path)
        => Assert.Throws<ArgumentException>(() => MauiLifecyclePaths.BrowserUrl(new PathString("/MAUI"), path));
}
