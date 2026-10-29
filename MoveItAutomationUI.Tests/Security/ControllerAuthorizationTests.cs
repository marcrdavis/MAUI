using Microsoft.AspNetCore.Authorization;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Tests.Security;

public sealed class ControllerAuthorizationTests
{
    [Theory]
    [InlineData(typeof(ScriptEditorController), nameof(ScriptEditorController.SaveTemplate))]
    [InlineData(typeof(TasksController), nameof(TasksController.SaveNotificationTemplate))]
    public void TemplateWrites_RequireTemplateAdministration(Type controller, string action)
    {
        var method = controller.GetMethod(action)!;
        Assert.Contains(method.GetCustomAttributes(inherit: true),
            attribute => attribute is AuthorizeAttribute authorization && authorization.Policy == MauiSecurityPolicies.TemplateAdmin);
    }

    [Fact]
    public void ScriptTemplateLoading_DoesNotRequireTemplateAdministration()
    {
        Assert.DoesNotContain(typeof(ScriptEditorController).GetMethod(nameof(ScriptEditorController.Templates))!.GetCustomAttributes(inherit: true),
            attribute => attribute is AuthorizeAttribute authorization && authorization.Policy == MauiSecurityPolicies.TemplateAdmin);
    }
    [Fact]
    public void ProtectedControllers_DeclareAuthorizeAttribute()
    {
        Type[] protectedControllers =
        [
            typeof(AccountController),
            typeof(ConfigurationController),
            typeof(HelpController),
            typeof(HomeController),
            typeof(HostsController),
            typeof(NativeEditorController),
            typeof(OutboundReportsController),
            typeof(RecordsController),
            typeof(RecycleBinController),
            typeof(ResourcesController),
            typeof(TaskFailureAlertsController),
            typeof(TasksController)
        ];

        Assert.All(protectedControllers, controllerType =>
            Assert.Contains(controllerType.GetCustomAttributes(inherit: true), attribute => attribute is AuthorizeAttribute));
    }

    [Fact]
    public void AccountController_ExplicitlyAllowsAnonymousLoginAndSessionExpired()
    {
        var methods = typeof(AccountController).GetMethods()
            .Where(method => method.Name is nameof(AccountController.Login) or nameof(AccountController.SessionExpired))
            .ToArray();

        Action<System.Reflection.MethodInfo> assertAllowsAnonymous = method =>
            Assert.Contains(method.GetCustomAttributes(inherit: true), attribute => attribute is AllowAnonymousAttribute);
        Assert.Collection(methods, assertAllowsAnonymous, assertAllowsAnonymous, assertAllowsAnonymous);
    }
}
