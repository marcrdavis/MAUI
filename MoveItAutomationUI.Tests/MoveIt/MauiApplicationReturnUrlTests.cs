using Microsoft.AspNetCore.Http;
using MoveItAutomationUI.Services.Lifecycle;
using Xunit;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class MauiApplicationReturnUrlTests
{
    [Theory]
    [InlineData("", "/Tasks/Index?filter=all", true)]
    [InlineData("/MAUI", "/MAUI/Tasks/Index?filter=all", true)]
    [InlineData("/MAUI", "/MAUI/Records/TaskRun#details", true)]
    [InlineData("/MAUI", "/MAUI", true)]
    [InlineData("/Apps/QA", "/Apps/QA/Resources/Scripts", true)]
    [InlineData("/Tasks", "/Tasks/Tasks/Index", true)]
    [InlineData("/MAUI", "/Tasks/Index", false)]
    [InlineData("/MAUI", "/MAUI2/Tasks/Index", false)]
    [InlineData("/Apps/QA", "/Apps/Dev/Tasks/Index", false)]
    [InlineData("/MAUI", "/MAUI/../Tasks/Index", false)]
    [InlineData("/MAUI", "/MAUI/%2e%2e/Tasks/Index", false)]
    [InlineData("/MAUI", "/MAUI/%2FTasks/Index", false)]
    [InlineData("/MAUI", "//other.example/Tasks/Index", false)]
    [InlineData("/MAUI", "https://other.example/MAUI/Tasks/Index", false)]
    [InlineData("/MAUI", "/MAUI\\..\\Tasks/Index", false)]
    [InlineData("", "/Tasks/../Hosts/Index", false)]
    public void ReturnUrlMustStayWithinActiveIisApplication(string pathBase, string candidate, bool expected)
    {
        Assert.Equal(expected, MauiApplicationReturnUrls.IsWithinApplication(candidate, new PathString(pathBase)));
    }
}
