using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Features.Editors.DateLists;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Resources.DateLists;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class DateListCrudTests
{
    [Theory]
    [InlineData("2028-02-29")] [InlineData("2045-*-31")] [InlineData("1999-12-31")]
    public void DatesPreserveExactNativeFormat(string date) => Assert.Equal(date, DateListConfiguration.Date(date));
    [Theory]
    [InlineData("2027-02-29")] [InlineData("2028-13-01")] [InlineData("2028-*-00")]
    [InlineData("2028-1-01")] [InlineData("2028-01-1")] [InlineData("0000-*-01")] [InlineData("2028-04-31")]
    public void InvalidDatesAreRejected(string date) => Assert.Throws<ArgumentException>(() => DateListConfiguration.Date(date));
    [Theory]
    [InlineData("0")] [InlineData("0042")] [InlineData("-42")] [InlineData("42/checkout")]
    public async Task InvalidIdsNeverReachTransport(string id)
    {
        using var f = new Fixture(); await Assert.ThrowsAsync<ArgumentException>(() => f.Crud.ReadAsync(id)); Assert.Empty(f.Calls);
    }
    [Fact]
    public async Task SavePreservesGroupUnknownFieldsAndExistingEntryAnnotations()
    {
        using var f = new Fixture(); var initial = await f.Crud.ReadAsync("42");
        using var version = ObjectEditVersion.Require("DateListEditor", "42", initial.EditVersion);
        var saved = await f.Crud.SaveAsync("42", new(" Renamed ", "Notes", ["2028-01-16", "2040-*-31"]));
        Assert.Equal("Renamed", saved.Name); Assert.Equal(new[] { "2028-01-16", "2040-*-31" }, saved.Entries);
        Assert.Equal("Production", f.Submitted!["Group"]![0]!.ToString()); Assert.Equal("retain", f.Submitted["NativeOnly"]!.ToString());
        Assert.Equal("Existing comment", f.Submitted["Entries"]![0]!["Comment"]!.ToString()); Assert.Null(f.Submitted["xAddEditDelete"]);
        Assert.Equal(new[] { "GET", "GET", "checkout", "GET", "PUT", "GET", "release" }, f.Calls);
        Assert.Equal(saved.EditVersion, ObjectEditVersion.SavedVersion);
    }
    [Fact]
    public async Task InformationSaveRetainsFreshDatesAndUnownedFields()
    {
        using var f = new Fixture(); using var version = ObjectEditVersion.Require("DateList", "42", ObjectEditVersion.Create("DateList", "42", ObjectEditVersion.Named("Original", "Old")));
        f.OnCheckout = () => f.Raw["Entries"]!.AsArray().Add(new JsonObject { ["Date"] = "2029-*-15", ["Comment"] = "External" });
        await f.Crud.SaveAsync("42", new("Renamed", "Updated"), informationOnly: true);
        Assert.Equal(2, f.Submitted!["Entries"]!.AsArray().Count); Assert.Equal("External", f.Submitted["Entries"]![1]!["Comment"]!.ToString());
    }
    [Fact]
    public async Task ConcurrentNativeChangeRejectsBeforePutAndUndo()
    {
        using var f = new Fixture(); var initial = await f.Crud.ReadAsync("42");
        using var version = ObjectEditVersion.Require("DateListEditor", "42", initial.EditVersion);
        f.OnCheckout = () => f.Raw["Entries"]![0]!["Date"] = "2029-01-01";
        await Assert.ThrowsAsync<ObjectEditConflictException>(() => f.Crud.SaveAsync("42", new("New"), beforeWrite: _ => throw new Exception("Must not capture Undo")));
        Assert.Equal(0, f.Writes); Assert.Equal("release", f.Calls[^1]);
    }
    [Fact]
    public async Task CheckoutDenialNeverWritesOrReleasesOtherCheckout()
    {
        using var f = new Fixture { RejectCheckout = true };
        await Assert.ThrowsAsync<MoveItApiException>(() => f.Crud.SaveAsync("42", new("New")));
        Assert.Equal(0, f.Writes); Assert.DoesNotContain("release", f.Calls);
    }
    [Fact]
    public async Task MetadataOnlySaveDoesNotCaptureUndoOrWriteMoveIt()
    {
        using var f = new Fixture(); var current = await f.Crud.ReadAsync("42");
        using var version = ObjectEditVersion.Require("DateListEditor", "42", current.EditVersion);
        await f.Crud.SaveAsync("42", new(current.Name, current.Description, current.Entries), beforeWrite: _ => throw new Exception("No native change"));
        Assert.Equal(0, f.Writes);
    }
    [Fact]
    public async Task CreateUsesCapturedEnvelopeAndNoEmptyIdCheckout()
    {
        using var f = new Fixture(); var saved = await f.Crud.CreateAsync(new("New", "Description", ["2040-01-16"]));
        Assert.Equal("42", saved.Id); Assert.Equal("", f.Created!["DateList"]!["ID"]!.ToString());
        Assert.IsType<JsonArray>(f.Created["DateList"]!["Group"]); Assert.Equal("2040-01-16", f.Created["DateList"]!["Entries"]![0]!["Date"]!.ToString());
        Assert.DoesNotContain("checkout", f.Calls); Assert.Equal(1, f.Creates);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ReadbackFailureRetainsIdAndNeverReplaysCreate(bool notFound)
    {
        using var f = new Fixture { RejectReadback = true, ReadbackNotFound = notFound };
        var error = await Assert.ThrowsAsync<DateListWriteUncertainException>(() => f.Crud.CreateAsync(new("New")));
        Assert.Equal("42", error.DateListId); Assert.Equal(1, f.Creates);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ReadbackFailureNeverReplaysPut(bool notFound)
    {
        using var f = new Fixture { RejectReadback = true, ReadbackNotFound = notFound };
        var error = await Assert.ThrowsAsync<DateListWriteUncertainException>(() => f.Crud.SaveAsync("42", new("New")));
        Assert.Equal("42", error.DateListId); Assert.Equal(1, f.Writes); Assert.Equal("release", f.Calls[^1]);
    }
    [Fact]
    public async Task PermissionDenialAndDuplicateNameStopBeforeCheckoutOrCreate()
    {
        using var f = new Fixture { Duplicate = true }; f.Raw["xAddEditDelete"] = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Crud.SaveAsync("42", new("New")));
        await Assert.ThrowsAsync<DateListNameAlreadyExistsException>(() => f.Crud.CreateAsync(new("Duplicate")));
        Assert.Equal(0, f.Writes); Assert.Equal(0, f.Creates); Assert.DoesNotContain("checkout", f.Calls);
    }
    [Theory]
    [InlineData("Create")] [InlineData("Save")] [InlineData("Restore")] [InlineData("Delete")]
    public async Task DirectCrudCallsRequireEffectiveRightsBeforeAnyNativeOperation(string operation)
    {
        var checks = new List<string>();
        var access = ScriptCrudTests.Stub<IMoveItObjectAccessService>((method, args) =>
        {
            Assert.Equal("DateList", args[0]); checks.Add(method.Name);
            return method.Name == nameof(IMoveItObjectAccessService.RequireCreationAsync)
                ? Task.FromException<string>(new MoveItObjectAccessException("Denied"))
                : Task.FromException(new MoveItObjectAccessException("Denied"));
        });
        using var f = new Fixture(access);
        await Assert.ThrowsAsync<MoveItObjectAccessException>(async () =>
        {
            switch (operation)
            {
                case "Create": await f.Crud.CreateAsync(new("Name")); break;
                case "Save": await f.Crud.SaveAsync("42", new("Name")); break;
                case "Restore": await f.Crud.RestoreNativeConfigurationAsync("42", "<DateList />"); break;
                case "Delete": await f.Crud.DeleteAsync("42"); break;
            }
        });
        Assert.Equal(new[] { operation == "Create" ? nameof(IMoveItObjectAccessService.RequireCreationAsync) : nameof(IMoveItObjectAccessService.RequireAsync) }, checks);
        Assert.Empty(f.Calls); Assert.Equal(0, f.Writes); Assert.Equal(0, f.Creates);
    }
    [Fact]
    public async Task AuthorizedServiceSaveKeepsCheckoutAndReadbackLifecycle()
    {
        var access = ScriptCrudTests.Stub<IMoveItObjectAccessService>((method, args) =>
        {
            Assert.Equal(nameof(IMoveItObjectAccessService.RequireAsync), method.Name);
            Assert.Equal("DateList", args[0]); Assert.Equal("42", args[1]); Assert.Equal("Edit", args[2]);
            return Task.CompletedTask;
        });
        using var f = new Fixture(access);
        var saved = await f.Crud.SaveAsync("42", new("Authorized"));
        Assert.Equal("Authorized", saved.Name); Assert.Equal(1, f.Writes);
        Assert.Equal(new[] { "GET", "checkout", "GET", "PUT", "GET", "release" }, f.Calls);
    }
    [Fact]
    public void MissingEntriesAndIncompleteAcknowledgementsCannotBeSaved()
    {
        Assert.Throws<InvalidOperationException>(() => DateListConfiguration.Unwrap(new() { ["ID"] = "42" }, "42"));
        foreach (var json in new[] { "{}", "{\"id\":\"42\",\"status\":\"success\"}", "{\"id\":\"99\",\"status\":\"success\",\"errorcode\":0}" })
            Assert.Throws<InvalidOperationException>(() => DateListCrudCoordinator.Acknowledge(JsonNode.Parse(json)!.AsObject(), "42"));
    }
    [Fact]
    public async Task NewDateListHasNoFabricatedUndoHistory()
    {
        using var f = new Fixture();
        var metadata = ScriptCrudTests.Stub<IMetadataRepository>((method, _) => method.Name == nameof(IMetadataRepository.GetObjectMetadataAsync)
            ? Task.FromResult(new ObjectMetadataSnapshot(null, new Dictionary<string, string>(), null, "", null, "")) : Task.CompletedTask);
        var undo = ScriptCrudTests.Stub<IDateListUndoService>((_, _) => throw new InvalidOperationException("No Undo after creation"));
        var controller = new DateListEditorController(f.Crud, undo, metadata, null!, null!, null!,
            new Snapshot<MetadataUiOptions>(new()), new Snapshot<UiOptions>(new()), NullLogger<DateListEditorController>.Instance);
        var result = Assert.IsType<JsonResult>(await controller.Save(null, new DateListEditorSaveRequest { Name = "New Dates", Entries = ["2040-01-16"] }, default));
        Assert.False(Assert.IsType<bool>(result.Value!.GetType().GetProperty("canUndo")!.GetValue(result.Value)));
        Assert.Equal(1, f.Creates);
    }
    [Fact]
    public async Task InvalidMetadataStopsBeforeCheckoutOrNativeWrites()
    {
        using var f = new Fixture();
        var validation = ScriptCrudTests.Stub<IMetadataValueValidationService>((_, _) => throw new InvalidOperationException("Required Owner"));
        var controller = new DateListEditorController(f.Crud, null!, null!, null!, validation, null!,
            new Snapshot<MetadataUiOptions>(new()), new Snapshot<UiOptions>(new()), NullLogger<DateListEditorController>.Instance);
        Assert.IsType<BadRequestObjectResult>(await controller.Save("42", new DateListEditorSaveRequest { Name = "Changed" }, default));
        Assert.Empty(f.Calls);
    }
    private sealed class Snapshot<T>(T value) : IOptionsSnapshot<T> where T : class
    {
        public T Value => value;
        public T Get(string? name) => value;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly MemoryCache cache = new(new MemoryCacheOptions());
        public DateListCrudCoordinator Crud { get; }
        public JsonObject Raw { get; private set; } = new() { ["ID"] = "42", ["Name"] = "Original", ["Description"] = "Old", ["Entries"] = new JsonArray(new JsonObject { ["Date"] = "2028-01-16", ["Comment"] = "Existing comment" }), ["Group"] = new JsonArray("Production"), ["NativeOnly"] = "retain", ["xAddEditDelete"] = true };
        public JsonObject? Submitted { get; private set; }
        public JsonObject? Created { get; private set; }
        public List<string> Calls { get; } = [];
        public bool RejectCheckout { get; init; }
        public bool RejectReadback { get; init; }
        public bool ReadbackNotFound { get; init; }
        public bool Duplicate { get; init; }
        public Action? OnCheckout { get; set; }
        public int Writes { get; private set; }
        public int Creates { get; private set; }
        public Fixture(IMoveItObjectAccessService? access = null)
        {
            var session = ScriptCrudTests.Stub<IMoveItSessionService>((_, _) => new MoveItSessionState { ServerKey = "DateListTests", Username = "tester" });
            var rest = ScriptCrudTests.Stub<IMoveItRestClient>((method, args) =>
            {
                switch (method.Name)
                {
                    case nameof(IMoveItRestClient.GetDateListAsync):
                        Calls.Add("GET"); if (RejectReadback && Writes + Creates > 0) { if (ReadbackNotFound) throw new MoveItApiException("Readback failed", HttpStatusCode.NotFound); throw new HttpRequestException("Readback failed"); } return Task.FromResult(Raw.DeepClone().AsObject());
                    case nameof(IMoveItRestClient.DateListNameExistsAsync): return Task.FromResult(Duplicate);
                    case nameof(IMoveItRestClient.CheckoutDateListV0Async):
                        Calls.Add("checkout"); if (RejectCheckout) throw new MoveItApiException("Owned elsewhere", HttpStatusCode.Conflict); OnCheckout?.Invoke(); return Task.CompletedTask;
                    case nameof(IMoveItRestClient.ReleaseDateListCheckoutV0Async): Calls.Add("release"); return Task.CompletedTask;
                    case nameof(IMoveItRestClient.UpdateDateListV0Async):
                        Calls.Add("PUT"); Writes++; Submitted = ((JsonObject)args[1]!)["DateList"]!.DeepClone().AsObject(); Raw = Submitted.DeepClone().AsObject(); return Task.FromResult(Ack());
                    case nameof(IMoveItRestClient.CreateDateListV0Async):
                        Calls.Add("POST"); Creates++; Created = ((JsonObject)args[0]!).DeepClone().AsObject(); Raw = Created["DateList"]!.DeepClone().AsObject(); Raw["ID"] = "42"; return Task.FromResult(Ack());
                    default: throw new NotSupportedException(method.Name);
                }
            });
            Crud = new(rest, session, cache, access: access);
        }
        private static JsonObject Ack() => new() { ["id"] = "42", ["status"] = "success", ["errorcode"] = 0 };
        public void Dispose() => cache.Dispose();
    }
}
