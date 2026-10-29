// Project: MAUI - MOVEit Automation UI
// Controller and source contracts for the Settings restart operation.

using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Lifecycle;

namespace MoveItAutomationUI.Tests.Lifecycle;

public sealed class MauiApplicationRestartTests
{
    [Fact]
    public void RestartRequiresAdminPostAndAntiforgery_ButStatusIsAnonymousReadOnly()
    {
        var type = typeof(MauiApplicationRestartController);
        var admin = type.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(admin);
        Assert.Equal(MauiSecurityPolicies.Admin, admin!.Policy);

        var restart = type.GetMethod(nameof(MauiApplicationRestartController.Restart));
        Assert.NotNull(restart);
        Assert.NotNull(restart!.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(restart.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.Null(restart.GetCustomAttribute<AllowAnonymousAttribute>());

        var status = type.GetMethod(nameof(MauiApplicationRestartController.Status));
        Assert.NotNull(status);
        Assert.NotNull(status!.GetCustomAttribute<HttpGetAttribute>());
        Assert.NotNull(status.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(status.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    [Fact]
    public void RestartSchedulesExistingHostServiceWithoutModifyingConfiguration()
    {
        var service = new FakeRestartService();
        var controller = CreateController(service);

        var response = Assert.IsType<OkObjectResult>(controller.Restart());
        Assert.True(Read<bool>(response.Value!, "accepted"));
        Assert.False(string.IsNullOrWhiteSpace(Read<string>(response.Value!, "instanceId")));
        Assert.True(service.IsRestartScheduled);
        Assert.Equal(1, service.ScheduleCalls);

        var status = Assert.IsType<OkObjectResult>(controller.Status());
        Assert.Equal(Read<string>(response.Value!, "instanceId"), Read<string>(status.Value!, "instanceId"));
    }

    [Fact]
    public void RestartAlreadyScheduledReturnsConflictAndDoesNotQueueAnother()
    {
        var service = new FakeRestartService { Scheduled = true };
        var response = CreateController(service).Restart();
        Assert.IsType<ConflictObjectResult>(response);
        Assert.Equal(0, service.ScheduleCalls);
    }

    [Fact]
    public void RestartCannotBeQueuedReturnsServiceUnavailable()
    {
        var service = new FakeRestartService { CanSchedule = false };
        var response = Assert.IsType<ObjectResult>(CreateController(service).Restart());
        Assert.Equal(503, response.StatusCode);
        Assert.False(service.IsRestartScheduled);
        Assert.Equal(1, service.ScheduleCalls);
    }

    private static T Read<T>(object response, string property)
        => (T)(response.GetType().GetProperty(property)?.GetValue(response)
            ?? throw new InvalidOperationException($"Missing response property {property}."));

    private static MauiApplicationRestartController CreateController(IApplicationRestartService service)
        => new(service, NullLogger<MauiApplicationRestartController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private sealed class FakeRestartService : IApplicationRestartService
    {
        public bool CanSchedule { get; set; } = true;
        public bool Scheduled { get; set; }
        public int ScheduleCalls { get; private set; }
        public bool IsRestartScheduled => Scheduled;

        public bool ScheduleRestart(string reason)
        {
            ScheduleCalls++;
            if (!CanSchedule || Scheduled) return false;
            Scheduled = true;
            return true;
        }
    }
}
