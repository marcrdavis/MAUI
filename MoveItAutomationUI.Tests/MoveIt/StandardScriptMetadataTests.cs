using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Controllers;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Resources.Scripts;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class StandardScriptMetadataTests
{
    // Synthetic replies exercise the published Standard Script/Parameter schema, not captured server payloads.
    private static JsonObject Script(string parameters = """{"any":[]}""")
        => new() { ["ID"] = "100001", ["Name"] = "Example", ["Description"] = "Script purpose", ["ProcessDesc"] = "Process purpose", ["Params"] = JsonNode.Parse(parameters) };
    [Fact]
    public void DefinitionMetadataIncludesRequiredOptionalOutputsDefaultsAndChoices()
    {
        var result = StandardScriptMetadataReader.Read(Script("""{"any":[{"ParamName":"Required_Input","ParamDesc":"What to supply","Required":true,"InOut":"In","Type":"Text"},{"ParamName":"Optional_Input","Required":false,"InOut":"In","Default":"Default.txt","Choices":{"Choice":["a","b"]}},{"ParamName":"Output_Name","InOut":"Out"}]}"""), "100001");
        Assert.True(result.ParametersAvailable); Assert.Equal("Script purpose", result.Description); Assert.Equal("Process purpose", result.ProcessDescription);
        Assert.Equal(3, result.Parameters.Count); Assert.True(result.Parameters[0].Required);
        Assert.Equal("What to supply", result.Parameters[0].Description); Assert.Null(result.Parameters[0].DefaultValue);
        Assert.False(result.Parameters[1].Required); Assert.Equal("Default.txt", result.Parameters[1].DefaultValue);
        Assert.Equal(new[] { "a", "b" }, result.Parameters[1].Choices); Assert.Equal("Out", result.Parameters[2].Direction);
    }
    [Theory]
    [InlineData("{}")] [InlineData("[]")] [InlineData("{\"any\":[]}")]
    public void ExplicitEmptyCollectionsRemainDistinguishableFromUnavailableDefinitions(string parameters)
    {
        var result = StandardScriptMetadataReader.Read(Script(parameters), "100001");
        Assert.True(result.ParametersAvailable); Assert.Empty(result.Parameters);
    }
    [Theory]
    [InlineData("null")]
    [InlineData("{\"any\":[{\"name\":\"ExistingValue\",\"value\":\"Not a definition\"}]}")]
    [InlineData("{\"any\":[{\"ParamName\":\"A\",\"Required\":\"unknown\"}]}")]
    [InlineData("{\"any\":[{\"ParamName\":\"A\"},{\"ParamName\":\"a\"}]}")]
    public void MissingUnsupportedOrAmbiguousMetadataNeverInventsParameterRequirements(string parameters)
    {
        var result = StandardScriptMetadataReader.Read(Script(parameters), "100001");
        Assert.False(result.ParametersAvailable); Assert.Empty(result.Parameters); Assert.Equal("Script purpose", result.Description);
    }
    [Fact]
    public void ReaderRejectsWrongIdentityAndPreservesTheNativeReply()
    {
        var raw = Script("""{"any":[{"ParamName":"Input","Required":1,"Default":"[OrigName]"}]}""");
        var before = raw.ToJsonString();
        Assert.Throws<InvalidOperationException>(() => StandardScriptMetadataReader.Read(raw, "999"));
        Assert.Equal("[OrigName]", StandardScriptMetadataReader.Read(raw, "100001").Parameters[0].DefaultValue);
        Assert.Equal(before, raw.ToJsonString());
    }
    private sealed class Snapshot<T>(T value) : IOptionsSnapshot<T> where T : class
    {
        public T Value => value;
        public T Get(string? name) => value;
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task TaskEndpointRequiresALiveSessionAndUsesTheSharedScriptRead(bool expired)
    {
        var reads = 0;
        var scripts = ScriptCrudTests.Stub<IScriptCrudCoordinator>((method, _) =>
        {
            reads++; Assert.Equal(nameof(IScriptCrudCoordinator.ReadBuiltInMetadataAsync), method.Name);
            return Task.FromResult(StandardScriptMetadataReader.Read(Script(), "100001"));
        });
        var sessions = ScriptCrudTests.Stub<IMoveItSessionService>((_, _) => expired ? null : new MoveItSessionState { Username = "reader" });
        var controller = new TraditionalTaskEditorController(null!, null!, null!, null!, null!, null!, null!, null!, null!,
            sessions, null!, null!, new Snapshot<MetadataUiOptions>(new()), NullLogger<TraditionalTaskEditorController>.Instance);
        var result = await controller.ScriptMetadata(scripts, "100001", default);
        if (expired) { Assert.IsType<UnauthorizedObjectResult>(result); Assert.Equal(0, reads); }
        else { Assert.IsType<JsonResult>(result); Assert.Equal(1, reads); }
    }
    [Fact]
    public async Task SharedReadNeedsALiveSessionButNoCustomScriptLicenseCheckoutOrWrites()
    {
        var calls = new List<string>(); var expired = false;
        var rest = ScriptCrudTests.Stub<IMoveItRestClient>((method, args) =>
        {
            calls.Add(method.Name); Assert.Equal("100001", args[0]);
            Assert.Equal(nameof(IMoveItRestClient.GetStandardScriptAsync), method.Name);
            return Task.FromResult(Script());
        });
        var sessions = ScriptCrudTests.Stub<IMoveItSessionService>((_, _) => expired ? null : new MoveItSessionState
            { Username = "reader", Info = new MoveItInfo { LicenseInformationAvailable = true } });
        using var cache = new MemoryCache(new MemoryCacheOptions()); var service = new ScriptCrudCoordinator(rest, sessions, cache);
        Assert.True((await service.ReadBuiltInMetadataAsync("100001")).ParametersAvailable);
        Assert.Equal(new[] { nameof(IMoveItRestClient.GetStandardScriptAsync) }, calls);
        expired = true;
        await Assert.ThrowsAsync<MoveItAuthenticationException>(() => service.ReadBuiltInMetadataAsync("100001"));
        Assert.Single(calls);
    }
}
