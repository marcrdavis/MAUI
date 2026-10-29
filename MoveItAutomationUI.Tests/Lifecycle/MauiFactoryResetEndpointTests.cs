// Project: MAUI - MOVEit Automation UI
// Guardrails must not silently disappear from the destructive reset endpoint.

using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Tests.Lifecycle;

public sealed class MauiFactoryResetEndpointTests
{
    [Fact]
    public void FactoryResetRequiresAdminPostAndAntiforgery()
    {
        var controller = typeof(MauiFactoryResetController);
        var admin = controller.GetCustomAttribute<AuthorizeAttribute>()
            ?? throw new InvalidOperationException("Factory reset controller lacks authorization.");
        Assert.Equal(MauiSecurityPolicies.Admin, admin.Policy);

        var action = controller.GetMethod(nameof(MauiFactoryResetController.Execute))
            ?? throw new InvalidOperationException("Factory reset action is missing.");
        Assert.NotNull(action.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
    }
}
