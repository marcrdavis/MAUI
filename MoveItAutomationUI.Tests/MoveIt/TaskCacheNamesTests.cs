using System.Reflection;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Tasks;
using MoveItAutomationUI.Services.MoveIt.Tasks.AdvancedTasks;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;

namespace MoveItAutomationUI.Tests.MoveIt;

// Regression for built-in Task 222222. Exercise the shared patch/save/readback path;
// no special-case system Task IDs and no replacement of unrelated empty strings.
public sealed class TaskCacheNamesTests
{
    [Theory]
    [InlineData("", null, "random")]
    [InlineData("", "", "random")]
    [InlineData("", "random", "random")]
    [InlineData("", "orig", "orig")]
    [InlineData("COS", null, "random")]
    [InlineData("COS", "orig", "orig")]
    public async Task EmptyEditorChoiceUsesFreshValueOrNativeDefault(string type, string? current, string expected)
    {
        var fixture = new Fixture(type, current);
        var before = fixture.Saved.DeepClone().AsObject();
        var request = fixture.Request("");
        await fixture.Patch.ApplyEditorPatchAsync("222222", request);

        Assert.Equal(expected, fixture.Saved["CacheNames"]!.GetValue<string>());
        Assert.Equal("Changed notes", fixture.Saved["Info"]!["Notes"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(before["Parameters"], fixture.Saved["Parameters"]));
        Assert.Equal("", fixture.Saved["Parameters"]!["Parameter"]![4]!["value"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(before["steps"], fixture.Saved["steps"]));
        // The shared schedule composer omits a zero EveryMinutes for a single daily run.
        before["Schedules"]!["Schedule"]![0]!["Frequency"]!["Interval"]![0]!.AsObject().Remove("EveryMinutes");
        Assert.True(JsonNode.DeepEquals(before["Schedules"], fixture.Saved["Schedules"]));
        Assert.Equal("", request["task"]!["cacheNames"]!.GetValue<string>());
        Assert.Equal(new[] { "GET", "checkout", "GET", type == "COS" ? "PUT-v0" : "PUT-v1", "GET", "release" }, fixture.Calls);
    }

    [Theory]
    [InlineData("random", "orig")]
    [InlineData("orig", "random")]
    public async Task ExplicitChoiceOverridesFreshValueAndIsVerified(string current, string requested)
    {
        var fixture = new Fixture("", current);
        await fixture.Patch.ApplyEditorPatchAsync("222222", fixture.Request(requested));
        Assert.Equal(requested, fixture.Saved["CacheNames"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task DefaultOmittedOrEmptyInReadbackDoesNotCreateFalsePartialSave(string? readback)
    {
        var fixture = new Fixture("", null) { ProjectDefault = true, DefaultProjection = readback };
        await fixture.Patch.ApplyEditorPatchAsync("222222", fixture.Request(""));
        Assert.Equal("random", fixture.Saved["CacheNames"]!.GetValue<string>());
    }

    [Fact]
    public async Task MissingOriginalNamesInReadbackStillFailsVerification()
    {
        var fixture = new Fixture("", "orig") { ProjectDefault = true };
        await Assert.ThrowsAsync<TraditionalTaskPatchPartialSaveException>(() =>
            fixture.Patch.ApplyEditorPatchAsync("222222", fixture.Request("orig")));
        Assert.Equal(1, fixture.Calls.Count(call => call.StartsWith("PUT-", StringComparison.Ordinal)));
        Assert.Equal("release", fixture.Calls[^1]);
    }

    [Fact]
    public async Task InvalidEnumIsRejectedBeforePutAndCheckoutIsReleased()
    {
        var fixture = new Fixture("", "random");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Patch.ApplyEditorPatchAsync("222222", fixture.Request("invalid")));
        Assert.Contains("Cache File Names", error.Message);
        Assert.Equal(new[] { "GET", "checkout", "GET", "release" }, fixture.Calls);
    }

    [Theory]
    [InlineData(null, "random")]
    [InlineData("", "random")]
    [InlineData("orig", "orig")]
    public async Task EditorProjectionShowsAValidChoice(string? current, string expected)
    {
        var fixture = new Fixture("", current);
        var model = await new TraditionalTaskEditorService(fixture.Rest, fixture.Crud).GetDefinitionAsync("222222");
        Assert.Equal(expected, model.Task.Advanced.CacheNames);
    }

    private sealed class Fixture
    {
        public JsonObject Saved { get; private set; }
        public List<string> Calls { get; } = new();
        public IMoveItRestClient Rest { get; }
        public ITaskCrudCoordinator Crud { get; }
        public TaskDefinitionPatchService Patch { get; }
        public bool ProjectDefault { get; init; }
        public string? DefaultProjection { get; init; }
        private bool _written;

        public Fixture(string type, string? cacheNames)
        {
            Saved = JsonNode.Parse("""
                {"Name":"Trim old stats from database","ID":"222222","AR":0,"Active":1,
                 "Info":{"Description":"This task trims the statistics database periodically.","Notes":"NOTES"},
                 "UseDefStateCaching":1,"Parameters":{"Parameter":[
                 {"value":"40","Name":"DaysToKeep","Secure":0},
                 {"value":"200","Name":"DaysToKeepAudit","Secure":0},
                 {"value":"C:\\ProgramData\\MOVEit\\MOVEitAutomation\\MICStats","Name":"DirArchive","Secure":0},
                 {"value":"C:\\ProgramData\\MOVEit\\MOVEitAutomation\\MICStats\\Logs","Name":"DirLog","Secure":0},
                 {"value":"","Name":"DSNArchive","Secure":0}]},
                 "NextEID":12,"Watched":0,"InvalidOverride":0,
                 "Schedules":{"Schedule":[{"OnlyUntilFirstSuccess":0,"FailIfNoSuccessInSched":0,"RunEvenIfNotif":0,
                 "Frequency":{"Interval":[{"StartTime":"02:15","EndTime":"02:15","value":"","EveryMinutes":0}]},
                 "Days":{"DayOfWeek":["Monday","Tuesday","Wednesday","Thursday","Friday","Saturday","Sunday"]}}]},
                 "steps":[{"Process":{"ID":"11","Disabled":0,"ScriptID":"100001","Run":"PerFile","IsDest":0}}],"Group":[]}
                """)!.AsObject();
            Saved["TT"] = type;
            if (cacheNames is not null) Saved["CacheNames"] = cacheNames;
            Rest = Stub<IMoveItRestClient>((method, args) => method.Name switch
            {
                nameof(IMoveItRestClient.GetTaskAsync) => Read(),
                nameof(IMoveItRestClient.CheckoutTaskV0Async) => Record("checkout"),
                nameof(IMoveItRestClient.ReleaseTaskCheckoutV0Async) => Record("release"),
                nameof(IMoveItRestClient.UpdateTaskAsync) => Write(args, "PUT-v1"),
                nameof(IMoveItRestClient.UpdateTaskV0Async) => WriteV0(args),
                _ when method.ReturnType == typeof(Task<IReadOnlyList<JsonObject>>)
                    => Task.FromResult<IReadOnlyList<JsonObject>>(Array.Empty<JsonObject>()),
                _ => throw new NotSupportedException(method.Name)
            });
            var persistence = new TaskJsonPersistenceService(Rest);
            Crud = Stub<ITaskCrudCoordinator>((method, args) => method.Name switch
            {
                nameof(ITaskCrudCoordinator.GetDefinitionAsync) => Rest.GetTaskAsync((string)args[0]!),
                nameof(ITaskCrudCoordinator.GetNativeConfigurationAsync) => Task.FromResult(
                    new MoveItTaskConfiguration("222222", "Trim old stats from database", "<Task ID=\"222222\"><Schedules /></Task>")),
                nameof(ITaskCrudCoordinator.UpdateEditableFreshAsync) => persistence.UpdateFreshAsync(
                    (string)args[0]!, (MoveItTaskKind)args[1]!, (Func<JsonObject, JsonObject>)args[2]!,
                    (Action<JsonObject>?)args[3], (bool)args[4]!, (CancellationToken)args[5]!),
                _ => throw new NotSupportedException(method.Name)
            });
            Patch = new TaskDefinitionPatchService(Rest, Crud, new TraditionalTaskDefinitionComposer(new TraditionalTaskHostProfileCatalog()));
        }

        public JsonObject Request(string cacheNames) => new()
        {
            ["task"] = new JsonObject { ["name"] = Saved["Name"]!.ToString(), ["description"] = Saved["Info"]!["Description"]!.ToString(),
                ["operatorNotes"] = "Changed notes", ["enabled"] = true, ["cacheNames"] = cacheNames,
                ["useDefaultStateCaching"] = true, ["useGlobalTaskLogRetention"] = true, ["autoRerun"] = false },
            ["steps"] = JsonNode.Parse("""[{"clientKey":"11","kind":"Process","id":"11","index":0,"patch":null}]"""),
            ["advancedFlow"] = AdvancedTaskDefinition.Project(Saved["steps"]),
            ["schedules"] = JsonNode.Parse("""
                [{"index":0,"daysOfWeek":["Monday","Tuesday","Wednesday","Thursday","Friday","Saturday","Sunday"],
                  "intervals":[{"startTime":"02:15","endTime":"02:15","everyMinutes":0}],
                  "onlyUntilFirstSuccess":false,"failIfNoSuccessInSchedule":false,"runEvenIfNotifications":false}]
                """),
            ["nextActions"] = new JsonArray()
        };

        private Task<JsonObject> Read()
        {
            Calls.Add("GET"); var result = Saved.DeepClone().AsObject();
            if (_written && ProjectDefault)
            {
                if (DefaultProjection is null) result.Remove("CacheNames"); else result["CacheNames"] = DefaultProjection;
            }
            return Task.FromResult(result);
        }
        private Task Record(string operation) { Calls.Add(operation); return Task.CompletedTask; }
        private Task Write(object?[] args, string operation)
        {
            Calls.Add(operation); _written = true;
            Saved = ((JsonObject)args[1]!).DeepClone().AsObject();
            Assert.Contains(Saved["CacheNames"]!.GetValue<string>(), new[] { "random", "orig" });
            return Task.CompletedTask;
        }
        private Task<JsonObject> WriteV0(object?[] args)
        {
            Write(args, "PUT-v0");
            return Task.FromResult(new JsonObject { ["id"] = "222222", ["status"] = "success", ["errorcode"] = 0 });
        }
    }

    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, HostSharedCrudTests.CrudProxy>();
        ((HostSharedCrudTests.CrudProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}
