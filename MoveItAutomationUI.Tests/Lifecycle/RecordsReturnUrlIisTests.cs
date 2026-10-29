using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Tests.Lifecycle;

public sealed class RecordsReturnUrlIisTests
{
    [Theory]
    [InlineData("", "/Tasks/Index?filter=all", "/Tasks/Index?filter=all")]
    [InlineData("/MAUI", "/MAUI/Tasks/Index?filter=all", "/MAUI/Tasks/Index?filter=all")]
    [InlineData("/MAUI", "/MAUI/Records/TaskRun#details", "/MAUI/Records/TaskRun#details")]
    [InlineData("/MAUI", "/MAUI", "/MAUI")]
    [InlineData("/Apps/QA", "/Apps/QA/Resources/Scripts", "/Apps/QA/Resources/Scripts")]
    [InlineData("/Tasks", "/Tasks/Tasks/Index", "/Tasks/Tasks/Index")]
    [InlineData("/MAUI", "/Tasks/Index", "")]
    [InlineData("/MAUI", "/MAUI2/Tasks/Index", "")]
    [InlineData("/Apps/QA", "/Apps/Dev/Tasks/Index", "")]
    [InlineData("/MAUI", "/MAUI/../Tasks/Index", "")]
    [InlineData("/MAUI", "/MAUI/%2e%2e/Tasks/Index", "")]
    [InlineData("/MAUI", "//other.example/Tasks/Index", "")]
    public void RecordsBackLink_RemainsInsideCurrentIisApplication(string pathBase, string requestedUrl, string expected)
    {
        // Invoke the actual controller policy with the same Request.PathBase MVC provides.
        var controller = new RecordsController(
            null!,
            new TestOptionsSnapshot<UiOptions>(),
            new TestOptionsSnapshot<GridBehaviorOptions>(),
            new TestOptionsSnapshot<ReportBehaviorOptions>(),
            NullLogger<RecordsController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.HttpContext.Request.PathBase = new PathString(pathBase);

        var method = typeof(RecordsController).GetMethod("NormalizeReturnUrl", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Records URL validation method is missing.");
        var result = Assert.IsType<string>(method.Invoke(controller, new object?[] { requestedUrl }));
        Assert.Equal(expected, result);
    }

    private sealed class TestOptionsSnapshot<T> : IOptionsSnapshot<T> where T : class, new()
    {
        public T Value { get; } = new();
        public T Get(string? name) => Value;
    }
}
