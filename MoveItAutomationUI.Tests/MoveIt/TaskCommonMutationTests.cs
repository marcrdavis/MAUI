using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Tasks;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;

namespace MoveItAutomationUI.Tests.MoveIt;

// Exercise the production coordinator/persistence lifecycle, with fake native transports.
public sealed class TaskCommonMutationTests
{
    [Theory]
    [InlineData("")] [InlineData("Tr")] [InlineData("COS")] [InlineData("Sy")]
    public async Task InformationChangesOnlyNameDescriptionAndNotesThroughTheCorrectRestVersion(string type)
    {
        var fixture = new Fixture(type); var before = fixture.Saved.DeepClone().AsObject();
        var revision = ObjectEditVersion.Create("TaskInformation", "1", ObjectEditVersion.TaskInformation(fixture.Saved));
        using var edit = ObjectEditVersion.Require("TaskInformation", "1", revision);
        await fixture.Crud.UpdateBasicDetailsAsync(" 1 ", " Renamed ", "Line one\nLine two", "", CancellationToken.None);
        before["Name"] = "Renamed"; before["Info"]!["Description"] = "Line one\nLine two"; before["Info"]!["Notes"] = "";
        Assert.True(JsonNode.DeepEquals(before, fixture.Submitted));
        Assert.True(JsonNode.DeepEquals(before, fixture.Saved));
        Assert.Equal(new[] { "checkout", "GET", type is "" or "Tr" ? "PUT-v1" : "PUT-v0", "GET", "release" }, fixture.Calls);
        Assert.Equal(0, fixture.ComReads); Assert.Equal(0, fixture.ComWrites);
        Assert.Equal(ObjectEditVersion.Create("TaskInformation", "1", ObjectEditVersion.TaskInformation(fixture.Saved)), ObjectEditVersion.SavedVersion);
    }

    [Theory]
    [InlineData("", true)] [InlineData("", false)] [InlineData("Tr", true)] [InlineData("Tr", false)]
    [InlineData("COS", true)] [InlineData("COS", false)] [InlineData("Sy", true)] [InlineData("Sy", false)]
    public async Task EnabledChangesOnlyActiveAndPreservesTheCompleteFreshDefinition(string type, bool enabled)
    {
        var fixture = new Fixture(type); fixture.Saved["Active"] = enabled ? 0 : 1;
        var expected = fixture.Saved.DeepClone().AsObject(); expected["Active"] = enabled ? 1 : 0;
        await fixture.Crud.SetEnabledAsync("1", enabled);
        Assert.True(JsonNode.DeepEquals(expected, fixture.Submitted));
        Assert.Equal(new[] { "checkout", "GET", type is "" or "Tr" ? "PUT-v1" : "PUT-v0", "GET", "release" }, fixture.Calls);
        Assert.Equal(0, fixture.ComReads); Assert.Equal(0, fixture.ComWrites);
    }

    [Theory]
    [InlineData("Tr", "Name")] [InlineData("Tr", "Description")] [InlineData("Tr", "Notes")]
    [InlineData("COS", "Name")] [InlineData("COS", "Description")] [InlineData("COS", "Notes")]
    [InlineData("Sy", "Name")] [InlineData("Sy", "Description")] [InlineData("Sy", "Notes")]
    public async Task StaleInformationFieldsRejectUnderCheckoutBeforeAnyPut(string type, string field)
    {
        var fixture = new Fixture(type);
        using var edit = ObjectEditVersion.Require("TaskInformation", "1", ObjectEditVersion.Create("TaskInformation", "1", ObjectEditVersion.TaskInformation(fixture.Saved)));
        fixture.OnCheckout = task => { if (field == "Name") task[field] = "Concurrent"; else task["Info"]![field] = "Concurrent"; };
        await Assert.ThrowsAsync<ObjectEditConflictException>(() => fixture.Crud.UpdateBasicDetailsAsync("1", "Rename", "Description", "Notes"));
        Assert.Equal(new[] { "checkout", "GET", "release" }, fixture.Calls); Assert.Null(fixture.Submitted);
        Assert.Equal(0, fixture.ComReads); Assert.Equal(0, fixture.ComWrites);
    }

    [Theory]
    [InlineData("Tr")] [InlineData("COS")] [InlineData("Sy")]
    public async Task InformationPatchRetainsUnrelatedConcurrentChangesFromTheCheckedOutRead(string type)
    {
        var fixture = new Fixture(type);
        using var edit = ObjectEditVersion.Require("TaskInformation", "1", ObjectEditVersion.Create("TaskInformation", "1", ObjectEditVersion.TaskInformation(fixture.Saved)));
        fixture.OnCheckout = task => { task["NativeOnly"]!["Fresh"] = "concurrent"; task["Active"] = 1; };
        await fixture.Crud.UpdateBasicDetailsAsync("1", "Renamed", "New description", "New notes");
        Assert.Equal("concurrent", fixture.Submitted!["NativeOnly"]!["Fresh"]!.ToString());
        Assert.Equal("1", fixture.Submitted["Active"]!.ToString()); Assert.Equal(0, fixture.ComWrites);
    }

    [Theory]
    [InlineData("Tr", false)] [InlineData("COS", false)] [InlineData("Sy", false)]
    [InlineData("Tr", true)] [InlineData("COS", true)] [InlineData("Sy", true)]
    public async Task FailedCheckoutOrPermissionCannotWriteOrReleaseAnotherOwnersCheckout(string type, bool deniedPermission)
    {
        var fixture = new Fixture(type);
        if (deniedPermission) fixture.Saved["xAlter"] = 0;
        else fixture.CheckoutFailure = new MoveItApiException("Already checked out", HttpStatusCode.Conflict);
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Crud.SetEnabledAsync("1", true));
        Assert.Equal(deniedPermission ? new[] { "checkout", "GET", "release" } : new[] { "checkout" }, fixture.Calls);
        Assert.Null(fixture.Submitted); Assert.Equal(0, fixture.ComWrites);
    }

    [Theory]
    [InlineData("Tr", "write")] [InlineData("COS", "ack")] [InlineData("Sy", "ack")]
    [InlineData("Tr", "readback")] [InlineData("COS", "readback")] [InlineData("Sy", "readback")]
    [InlineData("Tr", "enabled")] [InlineData("COS", "enabled")] [InlineData("Sy", "enabled")]
    [InlineData("Tr", "release")] [InlineData("COS", "release")] [InlineData("Sy", "release")]
    [InlineData("Tr", "name")] [InlineData("COS", "name")] [InlineData("Sy", "name")]
    public async Task UncertainWritesReadbackAndReleaseFailuresNeverRetryOrFallback(string type, string failure)
    {
        var fixture = new Fixture(type) { Failure = failure };
        if (failure == "name")
            await Assert.ThrowsAsync<TraditionalTaskPartialSaveException>(() => fixture.Crud.UpdateBasicDetailsAsync("1", "Renamed", "Updated", "Notes"));
        else await Assert.ThrowsAsync<TraditionalTaskPartialSaveException>(() => fixture.Crud.SetEnabledAsync("1", true));
        Assert.Equal(1, fixture.Writes); Assert.Equal("release", fixture.Calls.Last());
        Assert.Equal(0, fixture.ComWrites); Assert.Equal(0, fixture.ComReads);
    }

    [Fact]
    public async Task FullEditorRevisionStillProtectsSuppressedCredentialsWithRetainedNativeReads()
    {
        var fixture = new Fixture("COS");
        using var edit = ObjectEditVersion.Require("Task", "1", ObjectEditVersion.Create("Task", "1", ObjectEditVersion.Definition(fixture.Saved, "<Task ID=\"1\" Password=\"old\" />")));
        await Assert.ThrowsAsync<ObjectEditConflictException>(() => fixture.Crud.SetEnabledAsync("1", true));
        Assert.Equal(new[] { "checkout", "GET", "release" }, fixture.Calls);
        Assert.Equal(1, fixture.ComReads); Assert.Equal(0, fixture.ComWrites);
    }

    [Fact]
    public async Task InformationAcceptsTransportNewlinesAndCreatesMissingInfoWithoutResettingOtherFields()
    {
        var fixture = new Fixture("Tr") { Failure = "newlines" }; fixture.Saved.Remove("Info");
        await fixture.Crud.UpdateBasicDetailsAsync("1", "Name", "One\nTwo", "Notes");
        Assert.Equal("One\r\nTwo", fixture.Saved["Info"]!["Description"]!.ToString());
        Assert.Equal("keep", fixture.Submitted!["NativeOnly"]!["Marker"]!.ToString());
    }

    [Fact]
    public async Task InvalidNamesAndUnknownKindsFailBeforePutWithoutComFallback()
    {
        var fixture = new Fixture("Tr");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Crud.UpdateBasicDetailsAsync("1", " ", "", ""));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Crud.UpdateBasicDetailsAsync("1", new string('x', 255), "", ""));
        Assert.Empty(fixture.Calls);
        fixture.Saved["TT"] = "unknown";
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Crud.SetEnabledAsync("1", true));
        Assert.Equal(new[] { "checkout", "GET", "release" }, fixture.Calls); Assert.Equal(0, fixture.ComWrites);
    }

    [Theory] [InlineData("Tr")] [InlineData("COS")] [InlineData("Sy")]
    public async Task MissingTaskIdentityCannotAuthorizeAnUpdate(string type)
    {
        var fixture = new Fixture(type); fixture.Saved.Remove("ID");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Crud.SetEnabledAsync("1", true));
        Assert.Equal(new[] { "checkout", "GET", "release" }, fixture.Calls);
        Assert.Equal(0, fixture.Writes); Assert.Null(fixture.Submitted); Assert.Equal(0, fixture.ComWrites);
    }

    [Theory] [InlineData("Tr")] [InlineData("COS")] [InlineData("Sy")]
    public async Task MissingTaskIdentityAfterPutIsUnverifiedAndNeverReplayed(string type)
    {
        var fixture = new Fixture(type) { Failure = "missing-id" };
        var error = await Assert.ThrowsAsync<TraditionalTaskPartialSaveException>(() => fixture.Crud.SetEnabledAsync("1", true));
        Assert.Equal("1", error.TaskId); Assert.Contains("readback", error.Message);
        Assert.Equal(new[] { "checkout", "GET", type == "Tr" ? "PUT-v1" : "PUT-v0", "GET", "release" }, fixture.Calls);
        Assert.Equal(1, fixture.Writes); Assert.Equal(0, fixture.ComWrites);
    }

    private sealed class Fixture
    {
        public ITaskCrudCoordinator Crud { get; }
        public JsonObject Saved { get; private set; }
        public JsonObject? Submitted { get; private set; }
        public List<string> Calls { get; } = [];
        public int ComReads { get; private set; }
        public int ComWrites { get; private set; }
        public int Writes { get; private set; }
        public Action<JsonObject>? OnCheckout { get; set; }
        public Exception? CheckoutFailure { get; set; }
        public string? Failure { get; set; }
        public Fixture(string type)
        {
            Saved = type == "COS" ? AdvancedTaskEditorTests.Capture(0) : JsonNode.Parse("""{"steps":[{"Source":{"ID":"11","HostID":"42","Type":"Share","Path":"/a","FileMask":"*.*","Password":null,"SyF":1,"SyT":0,"SyDel":1}},{"Source":{"ID":"12","HostID":"43","Type":"Share","Path":"/b","FileMask":"*.*","Password":null,"SyF":0,"SyT":1,"SyDel":0}}]}""")!.AsObject();
            Saved["ID"] = "1"; Saved["TT"] = type; Saved["Name"] = "Original"; Saved["Active"] = 0; Saved["xAlter"] = 1;
            Saved["Info"] = new JsonObject { ["Description"] = "Old description", ["Notes"] = "Old notes", ["NativeInfo"] = "keep" };
            Saved["NativeOnly"] = new JsonObject { ["Marker"] = "keep" }; Saved["SameSecs"] = -1;
            Saved["Group"] = new JsonArray("Production");
            Saved["Parameters"] = JsonNode.Parse("""{"Parameter":[{"Name":"Token","Secure":1,"value":null}]}""");
            Saved["Schedules"] = JsonNode.Parse("""{"Schedule":[{"ID":"91","Multi":1,"NativePair":"keep","Days":{"DayOfWeek":["Monday"]},"Frequency":{"Interval":[{"StartTime":"23:00","EndTime":"23:59","EveryMinutes":15}]}}]}""");
            var rest = Stub<IMoveItRestClient>((method, args) =>
            {
                switch (method.Name)
                {
                    case nameof(IMoveItRestClient.CheckoutTaskV0Async):
                        Calls.Add("checkout"); if (CheckoutFailure is not null) throw CheckoutFailure;
                        OnCheckout?.Invoke(Saved); return Task.CompletedTask;
                    case nameof(IMoveItRestClient.ReleaseTaskCheckoutV0Async):
                        Calls.Add("release"); if (Failure == "release") throw new HttpRequestException("Release failed");
                        return Task.CompletedTask;
                    case nameof(IMoveItRestClient.GetTaskAsync):
                        Calls.Add("GET"); if (Writes > 0 && Failure == "readback") throw new HttpRequestException("Readback failed");
                        return Task.FromResult(Saved.DeepClone().AsObject());
                    case nameof(IMoveItRestClient.UpdateTaskAsync): Write(args, "PUT-v1"); return Task.CompletedTask;
                    case nameof(IMoveItRestClient.UpdateTaskV0Async):
                        Write(args, "PUT-v0"); return Task.FromResult(Failure == "ack" ? new JsonObject() : new JsonObject { ["id"] = "1", ["status"] = "success", ["errorcode"] = 0 });
                    default: throw new NotSupportedException(method.Name);
                }
            });
            void Write(object?[] args, string operation)
            {
                Calls.Add(operation); Writes++; Submitted = ((JsonObject)args[1]!).DeepClone().AsObject();
                Saved = Submitted.DeepClone().AsObject();
                if (Failure == "write") throw new HttpRequestException("Write response lost");
                if (Failure == "missing-id") Saved.Remove("ID");
                if (Failure == "enabled") Saved["Active"] = 0;
                if (Failure == "name") Saved["Name"] = "Wrong name";
                if (Failure == "newlines") Saved["Info"]!["Description"] = Saved["Info"]!["Description"]!.ToString().Replace("\n", "\r\n");
            }
            var com = Stub<IMoveItComClient>((method, _) =>
            {
                if (method.Name == nameof(IMoveItComClient.GetTaskConfigurationAsync))
                { ComReads++; return Task.FromResult(new MoveItTaskConfiguration("1", "Original", "<Task ID=\"1\" Password=\"new\" />")); }
                ComWrites++; throw new InvalidOperationException("COM writes must not be used: " + method.Name);
            });
            var traditional = new TraditionalTaskPersistenceService(rest, com, new TraditionalTaskDefinitionComposer(new TraditionalTaskHostProfileCatalog()));
            Crud = new TaskCrudCoordinator([new TraditionalTaskCrudStrategy(traditional), new RestV0TaskCrudStrategy(MoveItTaskKind.Advanced, rest),
                new RestV0TaskCrudStrategy(MoveItTaskKind.Synchronization, rest)], traditional, rest, com);
        }
    }
    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var result = DispatchProxy.Create<T, Proxy>(); ((Proxy)(object)result).Handler = handler; return result;
    }
    public class Proxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args ?? []);
    }
}
