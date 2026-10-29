using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Features.Editors.Scripts;
using MoveItAutomationUI.Services.Metadata;
using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Resources.Scripts;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class ScriptCrudTests
{
    [Theory]
    [InlineData("PS1", "$x = 'C:\\files\\*.txt'\r\n# [OrigName] \"quote\"\n\tWrite-Output $x\n")]
    [InlineData("VBScript", "' comment\r\nDim x\r\nx = \"C:\\test\"\r\n")]
    public async Task SavePreservesLiteralSourceAndUnknownProperties(string language, string source)
    {
        using var f = new Fixture(); var initial = await f.Crud.ReadAsync("42");
        using var revision = ObjectEditVersion.Require("ScriptEditor", "42", initial.EditVersion);
        var saved = await f.Crud.SaveAsync("42", new(" Renamed ", "line one\nline two", language, source));
        Assert.Equal(source, saved.Source); Assert.Equal("Renamed", saved.Name);
        Assert.Equal("Production", saved.ResourceGroups);
        Assert.Equal("Production", f.Submitted!["Group"]![0]!.ToString()); Assert.Equal("keep", f.Submitted["NativeOnly"]!.ToString());
        Assert.Equal("retain", f.Submitted["xCustomSetting"]!.ToString()); Assert.Null(f.Submitted["xAddEditDelete"]);
        Assert.Equal(new[] { "GET", "GET", "checkout", "GET", "PUT", "GET", "release" }, f.Calls);
        Assert.Equal(saved.EditVersion, ObjectEditVersion.SavedVersion);
    }
    [Fact]
    public async Task InformationOnlyPatchPreservesSourceAndLanguageUnderCheckout()
    {
        using var f = new Fixture(); f.Raw["Lang"] = "VBScript"; f.Raw["Source"] = "' Original\r\n";
        using var revision = ObjectEditVersion.Require("Script", "42", ObjectEditVersion.Create("Script", "42", ObjectEditVersion.Named("Original", "Old")));
        f.OnCheckout = () => f.Raw["NativeOnly"] = "Concurrent unowned change";
        await f.Crud.SaveAsync("42", new("New", "New description"), informationOnly: true);
        Assert.Equal("VBScript", f.Submitted!["Lang"]!.ToString()); Assert.Equal("' Original\r\n", f.Submitted["Source"]!.ToString());
        Assert.Equal("Concurrent unowned change", f.Submitted["NativeOnly"]!.ToString());
    }
    [Fact]
    public async Task ChangedSourceRejectsBeforePutAndBeforeUndoCapture()
    {
        using var f = new Fixture(); var initial = await f.Crud.ReadAsync("42");
        using var revision = ObjectEditVersion.Require("ScriptEditor", "42", initial.EditVersion);
        f.OnCheckout = () => f.Raw["Source"] = "# Another editor";
        await Assert.ThrowsAsync<ObjectEditConflictException>(() => f.Crud.SaveAsync("42", new("New"), beforeWrite: _ => throw new Exception("Must not capture Undo")));
        Assert.Equal(0, f.Writes); Assert.Equal("release", f.Calls[^1]);
    }
    [Fact]
    public async Task CheckoutDenialNeverWritesOrReleasesAnotherOwnersCheckout()
    {
        using var f = new Fixture { RejectCheckout = true };
        await Assert.ThrowsAsync<MoveItApiException>(() => f.Crud.SaveAsync("42", new("New")));
        Assert.Equal(0, f.Writes); Assert.DoesNotContain("release", f.Calls);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ReadbackFailureRetainsKnownIdAndNeverReplaysPut(bool notFound)
    {
        using var f = new Fixture { RejectReadback = true, ReadbackNotFound = notFound };
        var failure = await Assert.ThrowsAsync<ScriptWriteUncertainException>(() => f.Crud.SaveAsync("42", new("New")));
        Assert.Equal("42", failure.ScriptId); Assert.Equal(1, f.Writes); Assert.Equal("release", f.Calls[^1]);
    }
    [Fact]
    public async Task CreationUsesCapturedEnvelopeAndNeverChecksOutEmptyId()
    {
        using var f = new Fixture(); var saved = await f.Crud.CreateAsync(new("New Script", "Description", "VBScript", "Dim x\r\nx = 1"));
        Assert.Equal("42", saved.Id); Assert.Equal("", f.Created!["Script"]!["ID"]!.ToString());
        Assert.IsType<JsonArray>(f.Created["Script"]!["Group"]); Assert.Equal("VBScript", f.Created["Script"]!["Lang"]!.ToString());
        Assert.DoesNotContain("checkout", f.Calls); Assert.Equal(1, f.Creates);
    }
    [Fact]
    public async Task ScriptCreationCarriesItsSelectedResourceGroup()
    {
        using var f = new Fixture();
        await f.Crud.CreateAsync(new("New Script", ResourceGroup: "Script Group"));
        Assert.Equal("Script Group", f.Created!["Script"]!["Group"]![0]!.ToString());
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FailedCreateReadbackReportsKnownIdWithoutRollbackOrRetry(bool notFound)
    {
        using var f = new Fixture { RejectReadback = true, ReadbackNotFound = notFound };
        var failure = await Assert.ThrowsAsync<ScriptWriteUncertainException>(() => f.Crud.CreateAsync(new("New Script", Source: "# Script")));
        Assert.Equal("42", failure.ScriptId); Assert.Equal(1, f.Creates);
    }
    [Fact]
    public async Task DefinitiveWriteRejectionDoesNotExposeSourceOrBecomeUncertain()
    {
        using var f = new Fixture { RejectWrite = true };
        var error = await Assert.ThrowsAsync<MoveItApiException>(() => f.Crud.CreateAsync(new("New", Source: "# PRIVATE_SOURCE")));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, error.StatusCode);
        Assert.DoesNotContain("PRIVATE_SOURCE", error.Message); Assert.Null(error.InnerException);
        Assert.Equal(1, f.Creates);
    }
    [Fact]
    public async Task UnlicensedScriptsAreRejectedBeforeAnyTransportCall()
    {
        using var f = new Fixture(); f.State.Info = new MoveItInfo { LicenseInformationAvailable = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Crud.CreateAsync(new("Blocked", Source: "# Script")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Crud.ReadAsync("42"));
        Assert.Empty(f.Calls);
    }
    [Fact]
    public async Task DuplicateBuiltInNameIsRejectedBeforePost()
    {
        using var f = new Fixture { Duplicate = true };
        await Assert.ThrowsAsync<ScriptNameAlreadyExistsException>(() => f.Crud.CreateAsync(new("Reserved")));
        Assert.Equal(0, f.Creates);
    }
    [Fact]
    public async Task ReadOnlyPermissionCannotBeBypassedByPostingDirectly()
    {
        using var f = new Fixture(); f.Raw["xAddEditDelete"] = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Crud.SaveAsync("42", new("New")));
        Assert.Equal(0, f.Writes); Assert.DoesNotContain("checkout", f.Calls);
    }
    [Theory]
    [InlineData("0")] [InlineData("42/checkout")] [InlineData("-1")] [InlineData("0042")]
    public async Task InvalidIdsAreRejectedWithoutTransport(string id)
    {
        using var f = new Fixture(); await Assert.ThrowsAsync<ArgumentException>(() => f.Crud.ReadAsync(id)); Assert.Empty(f.Calls);
    }
    [Fact]
    public void RevisionIgnoresOnlyKnownReadOnlyFieldsAndLineEndings()
    {
        var a = JsonNode.Parse("{\"ID\":\"42\",\"Lang\":\"PS1\",\"Source\":\"a\\r\\nb\",\"xAddEditDelete\":true,\"UsedByCount\":0}")!.AsObject();
        var b = a.DeepClone().AsObject(); b["Source"] = "a\nb"; b["xAddEditDelete"] = false; b["UsedByCount"] = 99;
        Assert.True(JsonNode.DeepEquals(ScriptConfiguration.Revision(a), ScriptConfiguration.Revision(b)));
        b["NativeOnly"] = "changed"; Assert.False(JsonNode.DeepEquals(ScriptConfiguration.Revision(a), ScriptConfiguration.Revision(b)));
        Assert.Throws<InvalidOperationException>(() => ScriptConfiguration.Unwrap(new() { ["ID"] = "42", ["Lang"] = "PS1" }, "42"));
    }
    [Fact]
    public void AckRequiresExplicitSuccessZeroErrorAndMatchingId()
    {
        foreach (var json in new[] { "{}", "{\"id\":\"42\",\"status\":\"success\"}", "{\"id\":\"99\",\"status\":\"success\",\"errorcode\":0}", "{\"id\":\"42\",\"status\":\"error\",\"errorcode\":0}" })
            Assert.Throws<InvalidOperationException>(() => ScriptCrudCoordinator.Acknowledge(JsonNode.Parse(json)!.AsObject(), "42"));
    }
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    public async Task EditorMetadataOnlyAndNativeSavesKeepAuditAndUncertaintySeparate(bool nativeChange, bool failMetadata, bool existingHistory)
    {
        using var f = new Fixture();
        var model = await f.Crud.ReadAsync("42");
        var captures = 0;
        var audits = 0;
        var values = new Dictionary<string, string> { ["Owner"] = "Operations" };
        var undo = Stub<IScriptUndoService>((method, _) =>
        {
            if (method.Name == nameof(IScriptUndoService.GetAvailableScriptIdsAsync))
                return Task.FromResult<IReadOnlySet<string>>(captures > 0 || existingHistory ? new HashSet<string> { "42" } : new HashSet<string>());
            Assert.Equal(nameof(IScriptUndoService.CaptureAsync), method.Name);
            captures++;
            return Task.FromResult(new ScriptUndoSnapshot("editor-save-snapshot", "42", "Original", "Marc", DateTimeOffset.UtcNow, "<Script />"));
        });
        var validation = Stub<IMetadataValueValidationService>((_, _) => Task.FromResult(values));
        var metadata = Stub<IMetadataRepository>((method, args) =>
        {
            if (method.Name == nameof(IMetadataRepository.GetObjectMetadataAsync))
                return Task.FromResult(new ObjectMetadataSnapshot(null, values, null, "", null, ""));
            if (method.Name == nameof(IMetadataRepository.SaveObjectMetadataAsync))
            {
                Assert.Same(values, args[3]);
                if (failMetadata) throw new InvalidOperationException("Metadata unavailable");
            }
            if (method.Name == nameof(IMetadataRepository.RecordObjectConfigurationModifiedAsync)) audits++;
            return Task.CompletedTask;
        });
        var controller = new ScriptEditorController(f.Crud, null!, undo, metadata, f.Inventory, validation, null!,
            new Snapshot<MetadataUiOptions>(new()), new Snapshot<UiOptions>(new()), NullLogger<ScriptEditorController>.Instance, null!);
        var result = await controller.Save("42", new ScriptEditorSaveRequest
        {
            Name = nativeChange ? "Renamed" : model.Name, Description = model.Description,
            Language = model.Language, Source = model.Source, EditVersion = model.EditVersion, Metadata = values
        }, default);
        Assert.Equal(nativeChange ? 1 : 0, f.Writes);
        Assert.Equal(nativeChange ? 1 : 0, captures);
        Assert.Equal(nativeChange && !failMetadata ? 1 : 0, audits);
        if (failMetadata)
        {
            var failure = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal(nativeChange, failure.Value!.GetType().GetProperty("moveItSaved")!.GetValue(failure.Value));
        }
        else
        {
            var success = Assert.IsType<JsonResult>(result);
            Assert.Equal(nativeChange, success.Value!.GetType().GetProperty("canUndo")!.GetValue(success.Value));
            Assert.Equal(nativeChange ? "editor-save-snapshot" : "", success.Value!.GetType().GetProperty("undoSnapshotId")!.GetValue(success.Value));
        }
    }
    [Fact]
    public async Task NewScriptDoesNotAdvertiseUndoOrCaptureAnInitialSnapshot()
    {
        using var f = new Fixture();
        var metadata = Stub<IMetadataRepository>((method, _) => method.Name == nameof(IMetadataRepository.GetObjectMetadataAsync)
            ? Task.FromResult(new ObjectMetadataSnapshot(null, new Dictionary<string, string>(), null, "", null, "")) : Task.CompletedTask);
        var undo = Stub<IScriptUndoService>((_, _) => throw new InvalidOperationException("New Scripts have no Undo history"));
        var controller = new ScriptEditorController(f.Crud, null!, undo, metadata, f.Inventory, null!, null!,
            new Snapshot<MetadataUiOptions>(new()), new Snapshot<UiOptions>(new()), NullLogger<ScriptEditorController>.Instance, null!);
        var success = Assert.IsType<JsonResult>(await controller.Save(null, new ScriptEditorSaveRequest { Name = "New Script" }, default));
        Assert.False(Assert.IsType<bool>(success.Value!.GetType().GetProperty("canUndo")!.GetValue(success.Value)));
        Assert.Equal(1, f.Creates);
    }
    [Fact]
    public async Task EditorValidatesMetadataBeforeCheckoutOrNativeWrite()
    {
        using var f = new Fixture();
        var validation = Stub<IMetadataValueValidationService>((_, _) => throw new InvalidOperationException("Required Owner"));
        var controller = new ScriptEditorController(f.Crud, null!, null!, null!, f.Inventory, validation, null!,
            new Snapshot<MetadataUiOptions>(new()), new Snapshot<UiOptions>(new()), NullLogger<ScriptEditorController>.Instance, null!);
        Assert.IsType<BadRequestObjectResult>(await controller.Save("42", new ScriptEditorSaveRequest { Name = "New" }, default));
        Assert.Empty(f.Calls);
    }
    [Fact]
    public async Task BuiltInViewerNeedsNoCustomLicenseSourceReadCheckoutOrUndo()
    {
        using var f = new Fixture();
        f.State.Info = new MoveItInfo { LicenseInformationAvailable = true };
        var item = new AutomationScript { Id = "100001", Name = "Built-In", Description = "Fixed description", IsBuiltIn = true };
        var inventory = Stub<IAutomationScriptService>((method, _) => method.Name == nameof(IAutomationScriptService.GetScriptAsync)
            ? Task.FromResult(item) : (object)Task.FromResult<IReadOnlyList<AutomationScriptTaskReference>>([new("123", "Related Task")]));
        var metadata = Stub<IMetadataRepository>((_, _) => Task.FromResult(new ObjectMetadataSnapshot(null, new Dictionary<string, string>(), null, "", null, "")));
        var controller = new ScriptEditorController(f.Crud, null!, null!, metadata, inventory, null!, Stub<IUserPreferencesService>((_, _) => Task.FromResult(new ScriptEditorPreferences(false))),
            new Snapshot<MetadataUiOptions>(new()), new Snapshot<UiOptions>(new()), NullLogger<ScriptEditorController>.Instance, null!);
        var result = Assert.IsType<JsonResult>(await controller.Shell("100001", default));
        var data = System.Text.Json.JsonSerializer.SerializeToNode(result.Value)!;
        Assert.True(data["readOnly"]!.GetValue<bool>());
        Assert.True(data["permissionReadOnly"]!.GetValue<bool>());
        Assert.False(data["canUndo"]!.GetValue<bool>());
        Assert.False(data["checkoutObjectsForDurationOfEdit"]!.GetValue<bool>());
        Assert.Equal("Built-In", data["script"]!["name"]!.GetValue<string>());
        Assert.Empty(f.Calls);
    }
    [Fact]
    public async Task BuiltInSaveIsRejectedBeforeValidationCheckoutOrAnyWrite()
    {
        using var f = new Fixture();
        var inventory = Stub<IAutomationScriptService>((_, _) => Task.FromResult(new AutomationScript { Id = "100001", IsBuiltIn = true }));
        var controller = new ScriptEditorController(f.Crud, null!, null!, null!, inventory, null!, null!,
            new Snapshot<MetadataUiOptions>(new()), new Snapshot<UiOptions>(new()), NullLogger<ScriptEditorController>.Instance, null!);
        Assert.IsType<BadRequestObjectResult>(await controller.Save("100001", new ScriptEditorSaveRequest { Name = "Changed" }, default));
        Assert.Empty(f.Calls);
    }
    private sealed class Snapshot<T>(T value) : IOptionsSnapshot<T> where T : class
    {
        public T Value => value;
        public T Get(string? name) => value;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());
        public ScriptCrudCoordinator Crud { get; }
        public IAutomationScriptService Inventory { get; } = Stub<IAutomationScriptService>((_, args) => Task.FromResult(new AutomationScript { Id = (string)args[0]! }));
        public MoveItSessionState State { get; } = new() { ServerKey = "ScriptTests", Username = "tester" };
        public JsonObject Raw { get; private set; } = new() { ["ID"] = "42", ["Name"] = "Original", ["Description"] = "Old", ["Lang"] = "PS1", ["Source"] = "# Original", ["Group"] = new JsonArray("Production"), ["NativeOnly"] = "keep", ["xCustomSetting"] = "retain", ["xAddEditDelete"] = true };
        public JsonObject? Submitted { get; private set; }
        public JsonObject? Created { get; private set; }
        public List<string> Calls { get; } = [];
        public bool RejectCheckout { get; init; }
        public bool RejectReadback { get; init; }
        public bool ReadbackNotFound { get; init; }
        public bool Duplicate { get; init; }
        public bool RejectWrite { get; init; }
        public Action? OnCheckout { get; set; }
        public int Writes { get; private set; }
        public int Creates { get; private set; }
        public Fixture()
        {
            var sessions = Stub<IMoveItSessionService>((_, _) => State);
            var rest = Stub<IMoveItRestClient>((method, args) =>
            {
                switch (method.Name)
                {
                    case nameof(IMoveItRestClient.GetCustomScriptAsync):
                        Calls.Add("GET"); if (RejectReadback && Writes + Creates > 0) { if (ReadbackNotFound) throw new MoveItApiException("No readback", HttpStatusCode.NotFound); throw new HttpRequestException("No readback"); } return Task.FromResult(Raw.DeepClone().AsObject());
                    case nameof(IMoveItRestClient.CustomScriptNameExistsAsync): return Task.FromResult(false);
                    case nameof(IMoveItRestClient.StandardScriptNameExistsAsync): return Task.FromResult(Duplicate);
                    case nameof(IMoveItRestClient.CheckoutCustomScriptV0Async):
                        Calls.Add("checkout"); if (RejectCheckout) throw new MoveItApiException("Owned elsewhere", HttpStatusCode.Conflict); OnCheckout?.Invoke(); return Task.CompletedTask;
                    case nameof(IMoveItRestClient.ReleaseCustomScriptCheckoutV0Async): Calls.Add("release"); return Task.CompletedTask;
                    case nameof(IMoveItRestClient.UpdateCustomScriptV0Async):
                        Calls.Add("PUT"); Writes++; Submitted = ((JsonObject)args[1]!)["Script"]!.DeepClone().AsObject(); Raw = Submitted.DeepClone().AsObject(); return Task.FromResult(Ack());
                    case nameof(IMoveItRestClient.CreateCustomScriptV0Async):
                        Calls.Add("POST"); Creates++; if (RejectWrite) throw new MoveItApiException("Invalid PRIVATE_SOURCE body", HttpStatusCode.UnprocessableEntity); Created = ((JsonObject)args[0]!).DeepClone().AsObject(); Raw = Created["Script"]!.DeepClone().AsObject(); Raw["ID"] = "42"; return Task.FromResult(Ack());
                    default: throw new NotSupportedException(method.Name);
                }
            });
            Crud = new(rest, sessions, _cache);
        }
        private static JsonObject Ack() => new() { ["id"] = "42", ["status"] = "success", ["errorcode"] = 0 };
        public void Dispose() => _cache.Dispose();
    }
    internal static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    { var value = DispatchProxy.Create<T, Proxy>(); ((Proxy)(object)value).Handler = handler; return value; }
    public class Proxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args ?? []);
    }
}
