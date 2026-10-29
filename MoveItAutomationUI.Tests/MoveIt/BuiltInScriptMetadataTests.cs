using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Features.Editors.Scripts;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Resources.Scripts;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class BuiltInScriptMetadataTests
{
    [Fact]
    public async Task BuiltInMetadataSavesWithoutCustomLicenseOrNativeOperations()
    {
        var f = new Fixture(); var result = await f.Service.SaveBuiltInAsync("100001", new Dictionary<string, string> { ["Owner"] = "Operations" });
        Assert.Equal("Operations", result.Values["Owner"]); Assert.Equal(1, f.Saves);
        Assert.Equal(new[] { "GetScriptAsync" }, f.NativeCalls);
    }
    [Fact]
    public async Task CustomScriptCannotUseBuiltInMetadataEndpoint()
    {
        var f = new Fixture { BuiltIn = false };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.SaveBuiltInAsync("42", new Dictionary<string, string>()));
        Assert.Equal(0, f.Saves);
    }
    [Fact]
    public async Task ValidationFailureNeverWritesMetadata()
    {
        var f = new Fixture { Invalid = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.SaveBuiltInAsync("100001", new Dictionary<string, string>()));
        Assert.Equal(0, f.Saves);
    }
    [Fact]
    public async Task ExpiredSessionDoesNotReadOrWriteAnything()
    {
        var f = new Fixture { Expired = true };
        await Assert.ThrowsAsync<MoveItAuthenticationException>(() => f.Service.SaveBuiltInAsync("100001", null));
        Assert.Empty(f.NativeCalls); Assert.Equal(0, f.Saves);
    }
    [Fact]
    public async Task EditorMetadataActionUsesSharedRoutineWithoutCheckoutMiddleware()
    {
        var f = new Fixture();
        var controller = new ScriptEditorController(null!, null!, null!, null!, null!, null!, null!, null!, null!, NullLogger<ScriptEditorController>.Instance, f.Service);
        Assert.IsType<JsonResult>(await controller.SaveMetadata("100001", new ScriptMetadataSaveRequest { Metadata = new() { ["Owner"] = "Editor" } }, default));
        Assert.Equal("Editor", f.Values["Owner"]); Assert.Equal(1, f.Saves);
        Assert.Equal(new[] { "GetScriptAsync" }, f.NativeCalls);
    }
    [Fact]
    public async Task InformationSavesOnlyMetadataEvenWhenNativeFieldsArePosted()
    {
        var f = new Fixture();
        var controller = new ResourcesController(f.Inventory, null!, null!, null!, f.Metadata, f.Validation, null!, null!, null!, null!, f.Sessions,
            new Snapshot<UiOptions>(new()), new Snapshot<GridBehaviorOptions>(new()), new Snapshot<MetadataUiOptions>(new()),
            new Snapshot<ScriptBehaviorOptions>(new()), new Snapshot<DateListBehaviorOptions>(new()), new Snapshot<GlobalParameterBehaviorOptions>(new()),
            new Snapshot<KeyCertificateBehaviorOptions>(new()), NullLogger<ResourcesController>.Instance, f.Service);
        Assert.IsType<JsonResult>(await controller.SaveScriptDetails("100001", new ScriptDetailsUpdateRequest
            { Name = "Must not rename native Script", Description = "Must not replace native description", Metadata = new() { ["Owner"] = "Information" } }, default));
        Assert.Equal("Information", f.Values["Owner"]); Assert.Equal(1, f.Saves);
        Assert.All(f.NativeCalls, call => Assert.Equal("GetScriptAsync", call));
    }
    private sealed class Snapshot<T>(T value) : IOptionsSnapshot<T> where T : class
    {
        public T Value => value;
        public T Get(string? name) => value;
    }
    private sealed class Fixture
    {
        public bool BuiltIn { get; init; } = true;
        public bool Invalid { get; init; }
        public bool Expired { get; init; }
        public int Saves { get; private set; }
        public Dictionary<string, string> Values { get; private set; } = new();
        public List<string> NativeCalls { get; } = [];
        public IMoveItSessionService Sessions { get; }
        public IAutomationScriptService Inventory { get; }
        public IMetadataRepository Metadata { get; }
        public IMetadataValueValidationService Validation { get; }
        public ScriptMetadataSaveService Service { get; }
        public Fixture()
        {
            Sessions = ScriptCrudTests.Stub<IMoveItSessionService>((_, _) => Expired ? null : new MoveItSessionState
                { Username = "tester", ServerKey = "MetadataTests", EnvironmentKey = "test", Info = new MoveItInfo { LicenseInformationAvailable = true } });
            Inventory = ScriptCrudTests.Stub<IAutomationScriptService>((method, args) =>
            {
                NativeCalls.Add(method.Name);
                if (method.Name != nameof(IAutomationScriptService.GetScriptAsync)) throw new InvalidOperationException("Native write/checkout must never be called.");
                return Task.FromResult(new AutomationScript { Id = (string)args[0]!, Name = "Built-In", IsBuiltIn = BuiltIn });
            });
            Validation = ScriptCrudTests.Stub<IMetadataValueValidationService>((_, args) => Invalid
                ? throw new InvalidOperationException("Required Owner") : Task.FromResult(new Dictionary<string, string>((IReadOnlyDictionary<string, string>?)args[2] ?? new Dictionary<string, string>())));
            Metadata = ScriptCrudTests.Stub<IMetadataRepository>((method, args) =>
            {
                if (method.Name == nameof(IMetadataRepository.SaveObjectMetadataAsync)) { Saves++; Values = new((IReadOnlyDictionary<string, string>)args[3]!); return Task.CompletedTask; }
                if (method.Name == nameof(IMetadataRepository.GetObjectMetadataAsync)) return Task.FromResult(new ObjectMetadataSnapshot(null, Values, null, "", null, ""));
                throw new InvalidOperationException("Native audit and Undo must not be altered by MAUI metadata saves.");
            });
            Service = new(Sessions, Inventory, Metadata, Validation, new Snapshot<MetadataUiOptions>(new()));
        }
    }
}
