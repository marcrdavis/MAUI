using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Features.Editors.Hosts;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.SystemSettings;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class CheckoutRecoveryControllerTests
{
    [Theory]
    [InlineData("Task", false)]
    [InlineData("Host", false)]
    [InlineData("Task", true)]
    [InlineData("Host", true)]
    public async Task OpeningUsesTheNativeCheckoutResponseWithoutAnEditorAvailabilitySnapshot(string type, bool nativeLock)
    {
        var session = new MoveItSessionState { Info = new MoveItAutomationUI.Models.MoveItInfo { IsAdmin = false } };
        var sessions = Stub<IMoveItSessionService>((method, _) => method.Name == nameof(IMoveItSessionService.Get) ? session : Never(method, []));
        var accessCalls = 0; var leaseCalls = 0;
        var access = Stub<IMoveItObjectAccessService>((method, _) =>
        { Assert.Equal(nameof(IMoveItObjectAccessService.RequireAsync), method.Name); accessCalls++; return Task.CompletedTask; });
        Exception failure = nativeLock
            ? new ObjectCheckoutRejectedException("This section has been checked out to alice since 2026-10-24 17:27:53")
            : new HttpRequestException("Connection refused");
        var leases = Stub<IObjectEditLeaseService>((method, _) =>
        { Assert.Equal(nameof(IObjectEditLeaseService.OpenAsync), method.Name); leaseCalls++; return Task.FromException<ObjectEditLeaseResult>(failure); });
        var controller = new ObjectEditLeaseController(leases, sessions, Stub<ITraditionalTaskEditorService>(Never), Stub<IHostEditorService>(Never));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(MoveItSessionService.SessionIdClaimType, "session")], "test")) } };
        var result = Assert.IsAssignableFrom<ObjectResult>(await controller.Open(new(Type:type, Id:"101"), access, default));
        var body = System.Text.Json.JsonSerializer.SerializeToNode(result.Value)!;
        Assert.True(accessCalls == 1 && leaseCalls == 1);
        Assert.Equal(nativeLock ? 409 : 503, result.StatusCode);
        if (nativeLock)
        {
            Assert.True(body["checkoutConflict"]!.GetValue<bool>());
            Assert.Equal("This object has been checked out to alice since 2026-10-24 17:27:53", body["message"]!.ToString());
        }
        else
        {
            Assert.Null(body["checkoutConflict"]);
            Assert.Equal(ObjectCheckout.RequestFailedMessage, body["message"]!.ToString());
        }
    }

    [Theory]
    [InlineData("MOVEit Settings", false)]
    [InlineData("Resource Groups", false)]
    [InlineData("Global Parameters", false)]
    [InlineData("MOVEit Settings", true)]
    [InlineData("Resource Groups", true)]
    [InlineData("Global Parameters", true)]
    public async Task OnlyMoveItAdministratorsCanInvokeDatasetRecovery(string type, bool admin)
    {
        var calls = new List<string>();
        var session = new MoveItSessionState { Info = new MoveItAutomationUI.Models.MoveItInfo { IsAdmin = admin } };
        var sessions = Stub<IMoveItSessionService>((method, _) => method.Name == nameof(IMoveItSessionService.Get) ? session : throw new NotSupportedException(method.Name));
        var controller = new ObjectEditLeaseController(Stub<IObjectEditLeaseService>(Never), sessions,
            Stub<ITraditionalTaskEditorService>(Never), Stub<IHostEditorService>(Never));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(MoveItSessionService.SessionIdClaimType, "session")], "test")) } };
        var groups = Stub<IResourceGroupManagementService>((method, _) => { Assert.Equal(nameof(IResourceGroupManagementService.RecoverCheckoutAsync), method.Name); calls.Add("Resource Groups"); return Task.CompletedTask; });
        var parameters = Stub<IAutomationGlobalParameterService>((method, _) => { Assert.Equal(nameof(IAutomationGlobalParameterService.RecoverCheckoutAsync), method.Name); calls.Add("Global Parameters"); return Task.CompletedTask; });
        var settings = Stub<ISystemSettingsService>((method, _) => { Assert.Equal(nameof(ISystemSettingsService.RecoverCheckoutAsync), method.Name); calls.Add("MOVEit Settings"); return Task.FromResult<SystemSettingsState>(null!); });
        var result = await controller.Recover(new(Type: type), groups, parameters, settings, default);
        if (admin) { Assert.IsType<JsonResult>(result); Assert.Equal(type, Assert.Single(calls)); }
        else { Assert.IsType<ForbidResult>(result); Assert.Empty(calls); }
    }
    private static object? Never(MethodInfo method, object?[] _) => throw new InvalidOperationException("Recovery must not call " + method.Name);
    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var result = DispatchProxy.Create<T, HostSharedCrudTests.CrudProxy>();
        ((HostSharedCrudTests.CrudProxy)(object)result).Handler = handler; return result;
    }
}
