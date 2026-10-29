using Microsoft.AspNetCore.Http;
using MoveItAutomationUI.Services.Lifecycle;
using Xunit;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class MauiIisApplicationPathTests
{
    [Theory]
    [InlineData("", "/Setup", "/Setup")]
    [InlineData("", "/Upgrade", "/Upgrade")]
    [InlineData("", "/Account/Login", "/Account/Login")]
    [InlineData("/MAUI", "/Setup", "/MAUI/Setup")]
    [InlineData("/MAUI", "/Upgrade", "/MAUI/Upgrade")]
    [InlineData("/MAUI", "/Account/Login", "/MAUI/Account/Login")]
    [InlineData("/MAUI", "/Account/SessionExpired", "/MAUI/Account/SessionExpired")]
    [InlineData("/dev/tools/MAUI", "/Setup", "/dev/tools/MAUI/Setup")]
    [InlineData("/Tasks", "/Tasks/Index", "/Tasks/Tasks/Index")]
    [InlineData("/Records", "/Records/TaskRun", "/Records/Records/TaskRun")]
    [InlineData("/dev/Records", "/Records/TaskRun", "/dev/Records/Records/TaskRun")]
    public void LifecycleRedirectRespectsIisApplicationPath(string applicationPath, string target, string expected)
    {
        var context = new DefaultHttpContext();
        context.Request.PathBase = new PathString(applicationPath);
        Assert.Equal(expected, MauiLifecyclePaths.ForApplication(context.Request, target));
    }

    [Fact]
    public void SetupAndUpgradePredicatesRemainApplicationRelative()
    {
        var context = new DefaultHttpContext();
        context.Request.PathBase = new PathString("/MAUI");
        context.Request.Path = new PathString("/Setup/TestMoveIt");
        Assert.True(MauiLifecyclePaths.IsSetup(context.Request.Path));
        Assert.False(MauiLifecyclePaths.IsUpgrade(context.Request.Path));
        context.Request.Path = new PathString("/Upgrade/RestartStatus");
        Assert.True(MauiLifecyclePaths.IsUpgrade(context.Request.Path));
    }
}
