using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class GlobalParameterPresentationTests
{
    [Fact]
    public async Task ReferencesIncludeNestedStepsAndProcessParametersWithoutDuplicates()
    {
        IReadOnlyList<JsonObject> definitions = [
            Parse("""{"ID":"3","Name":"Zulu","steps":[{"For":{"steps":[{"If":{"When":[{"steps":[{"Destination":{"Path":"[pArM: Endpoint ]"}}]}]}}]}}]}"""),
            Parse("""{"ID":"1","Name":"Alpha","Parameters":{"Parameter":[{"value":"[ParmDisplay:Endpoint]"}]}}"""),
            Parse("""{"ID":"1","Name":"Alpha","NextActions":{"NextAction":[{"Message":"[Parm:Endpoint]"}]}}"""),
            Parse("""{"ID":"2","Name":"Other","steps":[{"Source":{"Path":"[Parm:Endpoint2] Endpoint"}}]}"""),
            Parse("""{"Name":"Missing ID","steps":[{"Source":{"Path":"[Parm:Endpoint]"}}]}""")];
        var calls = new List<string>();
        var rest = ScriptCrudTests.Stub<IMoveItRestClient>((method, _) => { calls.Add(method.Name); return Task.FromResult(definitions); });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new AutomationGlobalParameterService(rest, null!, null!, cache, new Snapshot<GlobalParameterBehaviorOptions>(new()));
        var references = await service.GetAssignedTasksAsync(" endpoint ");
        Assert.Equal(new[] { "1", "3" }, references.Select(task => task.Id));
        Assert.Equal(new[] { nameof(IMoveItRestClient.GetTaskGlobalParameterReferenceDefinitionsAsync) }, calls);
    }

    [Theory]
    [InlineData("[Parm:Name", false)]
    [InlineData("[Parm:NameSuffix]", false)]
    [InlineData("[ParmDisplay: Name ]", true)]
    [InlineData("[Parm:Other][Parm:NAME]", true)]
    public void ReferencesUseExactCaseInsensitiveMacroNames(string text, bool expected)
        => Assert.Equal(expected, TaskPreflightService.CollectGlobalParameterNames(new JsonObject { ["Path"] = text }).Contains("Name"));

    [Fact]
    public async Task InvalidMetadataIsRejectedBeforeNativeParameterMutation()
    {
        var nativeCalls = 0;
        var parameters = ScriptCrudTests.Stub<IAutomationGlobalParameterService>((_, _) => { nativeCalls++; throw new InvalidOperationException("Native operations must not run."); });
        var sessions = ScriptCrudTests.Stub<IMoveItSessionService>((_, _) => new MoveItSessionState { Username = "tester", ServerKey = "test", EnvironmentKey = "test" });
        var validation = ScriptCrudTests.Stub<IMetadataValueValidationService>((_, _) => throw new ArgumentException("Owner is required."));
        var controller = new ResourcesController(null!, null!, parameters, null!, null!, validation, null!, null!, null!, null!, sessions,
            new Snapshot<UiOptions>(new()), new Snapshot<GridBehaviorOptions>(new()), new Snapshot<MetadataUiOptions>(new()),
            new Snapshot<ScriptBehaviorOptions>(new()), new Snapshot<DateListBehaviorOptions>(new()), new Snapshot<GlobalParameterBehaviorOptions>(new()),
            new Snapshot<KeyCertificateBehaviorOptions>(new()), NullLogger<ResourcesController>.Instance, null!);
        var result = await controller.SaveGlobalParameterDetails(new GlobalParameterDetailsUpdateRequest
            { OriginalName = "Endpoint", Name = "Renamed", Value = "replacement", Metadata = new() { ["Owner"] = "" } }, default);
        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, nativeCalls);
    }

    private static JsonObject Parse(string json) => (JsonObject)JsonNode.Parse(json)!;
    private sealed class Snapshot<T>(T value) : IOptionsSnapshot<T> where T : class
    {
        public T Value => value;
        public T Get(string? name) => value;
    }
}
