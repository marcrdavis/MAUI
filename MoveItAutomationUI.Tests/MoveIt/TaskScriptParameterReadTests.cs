using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class TaskScriptParameterReadTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FallbackReadUsesCurrentSessionAndNeverReturnsSecureValues(bool secure)
    {
        var calls = new List<string>();
        var globals = ScriptCrudTests.Stub<IAutomationGlobalParameterService>((method, args) =>
        {
            calls.Add(method.Name);
            Assert.Equal(nameof(IAutomationGlobalParameterService.GetAsync), method.Name);
            Assert.Equal("PGPRecipientKey", args[0]);
            return Task.FromResult(new AutomationGlobalParameter { Name = "PGPRecipientKey", Value = "private-value", DisplayValue = "also-private", Secure = secure });
        });
        var result = Assert.IsType<JsonResult>(await Controller().ScriptGlobalParameter(globals, " PGPRecipientKey ", default));
        var body = JsonSerializer.SerializeToNode(result.Value)!;
        Assert.True(body["success"]!.GetValue<bool>());
        Assert.True(body["present"]!.GetValue<bool>());
        Assert.Equal(secure ? "" : "private-value", body["parameter"]!["value"]!.ToString());
        Assert.Equal(secure, body["parameter"]!["secure"]!.GetValue<bool>());
        Assert.Null(body["parameter"]!["displayValue"]);
        Assert.Equal(nameof(IAutomationGlobalParameterService.GetAsync), Assert.Single(calls));
        if (secure) Assert.DoesNotContain("private", body.ToJsonString());
    }

    [Fact]
    public async Task ExpiredSessionDoesNotReadGlobals()
    {
        var globals = ScriptCrudTests.Stub<IAutomationGlobalParameterService>((method, _) => throw new InvalidOperationException(method.Name));
        Assert.IsType<UnauthorizedObjectResult>(await Controller(expired: true).ScriptGlobalParameter(globals, "Key", default));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task EmptyNamesNeverReadGlobals(string name)
    {
        var globals = ScriptCrudTests.Stub<IAutomationGlobalParameterService>((method, _) => throw new InvalidOperationException(method.Name));
        Assert.IsType<BadRequestObjectResult>(await Controller().ScriptGlobalParameter(globals, name, default));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, true)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    public async Task OnlyNativeNotFoundMeansNoGlobalFallback(HttpStatusCode status, bool absent)
    {
        var globals = ScriptCrudTests.Stub<IAutomationGlobalParameterService>((_, _) =>
            Task.FromException<AutomationGlobalParameter>(new MoveItApiException("Native read failure", status)));
        var result = await Controller().ScriptGlobalParameter(globals, "Key", default);
        if (absent)
        {
            var body = JsonSerializer.SerializeToNode(Assert.IsType<JsonResult>(result).Value)!;
            Assert.True(body["success"]!.GetValue<bool>());
            Assert.False(body["present"]!.GetValue<bool>());
        }
        else Assert.IsAssignableFrom<ObjectResult>(result);
    }

    [Fact]
    public void FallbackEndpointIsReadOnlyAndNotCacheable()
    {
        var method = typeof(TraditionalTaskEditorController).GetMethod(nameof(TraditionalTaskEditorController.ScriptGlobalParameter))!;
        Assert.Single(method.GetCustomAttributes(typeof(HttpGetAttribute), false));
        var cache = Assert.IsType<ResponseCacheAttribute>(Assert.Single(method.GetCustomAttributes(typeof(ResponseCacheAttribute), false)));
        Assert.True(cache.NoStore);
        Assert.Equal(ResponseCacheLocation.None, cache.Location);
        var permission = Assert.IsType<MoveItObjectPermissionAttribute>(Assert.Single(method.GetCustomAttributes(typeof(MoveItObjectPermissionAttribute), false)));
        Assert.Equal("GlobalParameter", permission.Type);
        Assert.Equal("Read", permission.Operation);
        Assert.Equal("name", permission.Ids);
    }

    private static TraditionalTaskEditorController Controller(bool expired = false)
    {
        var sessions = ScriptCrudTests.Stub<IMoveItSessionService>((_, _) => expired ? null : new MoveItSessionState
            { Username = "tester", ServerKey = "parameters", Info = new MoveItInfo() });
        return new(null!, null!, null!, null!, null!, null!, null!, null!, null!, sessions, null!, null!,
            new Snapshot<MetadataUiOptions>(new()), NullLogger<TraditionalTaskEditorController>.Instance);
    }
    private sealed class Snapshot<T>(T value) : IOptionsSnapshot<T> where T : class
    {
        public T Value => value;
        public T Get(string? name) => value;
    }
}
