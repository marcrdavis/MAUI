using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text;
using Microsoft.Extensions.Configuration;
using MoveItAutomationUI.Services.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Tasks;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class TaskRunAsOfTests
{
    [Fact]
    public void AllSuppliedDateDesignationsUseTheSelectedDateAndTime()
    {
        var tokens = "[A]|[B]|[AAA]|[AAAA]|[D]|[DD]|[H]|[HH]|[HHH]|[HHHH]|[II]|[J]|[JJJ]|[K]|[KKK]|[M]|[MM]|[MMM]|[MMMM]|[S]|[SS]|[T]|[TT]|[W]|[WW]|[X]|[XX]|[YY]|[YYYY]";
        Assert.Equal("4|5|Thu|Thursday|1|01|23|23|11|11|pm|273|273|274|274|10|10|Oct|October|5|05|0|00|39|39|40|40|26|2026", TaskRunAsOfDefinition.Expand(tokens, new(2026,10,1,23,0,5)));
    }

    [Theory]
    [InlineData("[dd][mm-][yyyy] [dd][mm][yyyy]", "05062007 05062007")]
    [InlineData("\"[dd][mm-][yyyy]\" [dd][mm][yyyy]", "\"05062007\" 05072007")]
    [InlineData("[OrigName]_[YYYY].txt", "[OrigName]_2007.txt")]
    public void PhraseOffsetsAndQuotedGroupsMatchTheSuppliedExamples(string input, string expected)
        => Assert.Equal(expected, TaskRunAsOfDefinition.Expand(input, new(2007,7,5)));

    [Fact]
    public void TimeOffsetsRollOverTheWholePhraseAndLeapMonthsRemainValid()
    {
        Assert.Equal("2027-01-01 00:00:00", TaskRunAsOfDefinition.Expand("[YYYY]-[MM]-[DD] [HH]:[TT]:[SS+]", new(2026,12,31,23,59,59)));
        Assert.Equal("2024-02-29", TaskRunAsOfDefinition.Expand("[YYYY]-[MM-]-[DD]", new(2024,3,31)));
        Assert.Equal("12 am", TaskRunAsOfDefinition.Expand("[HHHH] [II]", new(2026,1,1)));
    }

    [Fact]
    public void NestedStepsChangeOnlyFilenameAndFolderFieldsAndPreserveOtherMacros()
    {
        var task = Definition();
        task["steps"]!.AsArray().Add(JsonNode.Parse("""{"For":{"ID":"20","steps":[{"If":{"ID":"21","When":[{"steps":[{"Destination":{"ID":"22","Path":"/[YYYY]/[MM]","FileName":"[OrigName]_[DD].txt","Password":"[YYYY]"}}]}]}}]}}"""));
        var before = task.DeepClone().AsObject();
        var changes = TaskRunAsOfDefinition.Describe(task, new(2026,10,1));
        Assert.Equal(4, changes.Count);
        TaskRunAsOfDefinition.Apply(task, changes, false, true);
        Assert.Equal("[YYYY]", TaskRunAsOfDefinition.Text(TaskRunAsOfDefinition.Steps(task).Last().Step, "Password"));
        TaskRunAsOfDefinition.Apply(task, changes, true, true);
        Assert.True(JsonNode.DeepEquals(before, task));
    }

    [Fact]
    public void TransferFolderMacroClearsAndRestoresItsResolvedFolderId()
    {
        var task = Definition();
        var source = task["steps"]![0]!["Source"]!.AsObject();
        source["FolderName"] = "/[YYYY]/[MM]"; source["FolderID"] = "folder-123";
        var original = task.DeepClone();
        var changes = TaskRunAsOfDefinition.Describe(task, new(2026,10,1));
        TaskRunAsOfDefinition.Apply(task, changes, false, true);
        Assert.Equal("/2026/10", source["FolderName"]!.ToString());
        Assert.Equal("", source["FolderID"]!.ToString());
        TaskRunAsOfDefinition.Apply(task, changes, true, true);
        Assert.True(JsonNode.DeepEquals(original, task));
    }

    [Fact]
    public void RestorationDoesNotOverwriteAConcurrentEnabledStateChange()
    {
        var task = Definition(); task["Active"] = 0;
        var changes = TaskRunAsOfDefinition.Describe(task, new(2026,10,1));
        TaskRunAsOfDefinition.Apply(task, changes, false, false);
        task["Active"] = 1;
        Assert.Throws<InvalidOperationException>(() => TaskRunAsOfDefinition.Apply(task.DeepClone().AsObject(), changes, true, false));
        Assert.True(TaskRunAsOfDefinition.Enabled(task));
    }

    [Theory] [InlineData("")] [InlineData("COS")]
    public async Task CompletionRestoresFieldsAndEnabledStateThroughTheSharedCrud(string type)
    {
        var f = new Fixture(type);
        var state = await f.Run();
        Assert.True(state.Completed); Assert.True(state.Restored); Assert.True(state.Success);
        Assert.Equal(1, f.Backups); Assert.Equal(1, f.Starts); Assert.Equal(2, f.Writes);
        Assert.Equal("/[YYYY]/[MM]", f.Definition["steps"]![0]!["Source"]!["Path"]!.ToString());
        Assert.True(TaskRunAsOfDefinition.Enabled(f.Definition));
        Assert.Equal("Concurrent description", f.Definition["Info"]!["Description"]!.ToString());
        Assert.True(f.DisabledAtStart);
    }

    [Theory] [InlineData("Failure")] [InlineData("No transfers")]
    public async Task UnsuccessfulTransfersStillRestoreTheTask(string status)
    {
        var f = new Fixture { RunStatus = status };
        var state = await f.Run(); Assert.True(state.Restored); Assert.Contains(status, state.Message); Assert.Equal(2, f.Writes);
    }

    [Fact]
    public async Task OptionalRestRunFlagDoesNotOverrideTheNativeStartResult()
    {
        var f = new Fixture(); f.Definition["xRun"] = 0;
        var state = await f.Run();
        Assert.True(state.Success); Assert.True(state.Restored); Assert.Equal(1, f.Starts);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.MethodNotAllowed)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task DefinitiveNativeRunRejectionRestoresTheTemporaryChanges(HttpStatusCode status)
    {
        var f = new Fixture { StartFailure = new MoveItApiException("Not permitted", status) };
        var state = await f.Run();
        Assert.False(state.Success); Assert.True(state.Restored); Assert.Equal(1, f.Starts); Assert.Equal(2, f.Writes);
    }

    [Fact]
    public async Task RejectedStartRestoresWithoutReplayingTheRun()
    {
        var f = new Fixture { StartFailure = new MoveItApiException("Rejected", HttpStatusCode.BadRequest) };
        var state = await f.Run(); Assert.False(state.Success); Assert.True(state.Restored); Assert.Equal(1, f.Starts); Assert.Equal(2, f.Writes);
    }

    [Fact]
    public async Task LostStartAcknowledgementReconcilesHistoryWithoutStartingAgain()
    {
        var f = new Fixture { StartFailure = new HttpRequestException("Connection lost") };
        var state = await f.Run(); Assert.True(state.Success); Assert.True(state.Restored); Assert.Equal(1, f.Starts);
    }

    [Fact]
    public async Task AConcurrentEditToATemporaryFieldIsNotOverwritten()
    {
        var f = new Fixture { ChangeTemporaryField = true };
        var state = await f.Run(); Assert.False(state.Restored); Assert.False(state.Success);
        Assert.Equal("/other-user-change", f.Definition["steps"]![0]!["Source"]!["Path"]!.ToString());
        Assert.Contains("Undo", state.Message); Assert.Equal(1, f.Backups);
    }

    [Fact]
    public async Task NoDateMacrosRejectBeforeBackupOrMutation()
    {
        var f = new Fixture(); f.Definition["steps"]![0]!["Source"]!["Path"] = "/plain"; f.Definition["steps"]![0]!["Source"]!["FileMask"] = "*.*";
        var state = await f.Run(); Assert.False(state.Success); Assert.True(state.Restored); Assert.Equal(0, f.Writes); Assert.Equal(0, f.Backups); Assert.Equal(0, f.Starts);
    }

    [Fact]
    public void ProgressCannotBeReadFromAnotherSessionAndDuplicateIdsCannotRunAgain()
    {
        var store = new TaskRunAsOfProgress(); var id = Guid.NewGuid().ToString(); store.Begin("owner", "server:1", id, "1");
        Assert.Null(store.Get("other", id)); Assert.NotNull(store.Get("owner", id));
        Assert.Throws<InvalidOperationException>(() => store.Begin("owner", "server:1", Guid.NewGuid().ToString(), "1"));
        store.Release("server:1"); Assert.Throws<InvalidOperationException>(() => store.Begin("owner", "server:1", id, "1"));
    }

    [Fact]
    public async Task RunAsOfSendsTheDocumentedEqualityPredicateThroughTheRealHttpClient()
    {
        var fixture = new Fixture();
        using var handler = new ReportHandler(fixture);
        var session = Stub<IMoveItSessionService>((method, _) => method.Name == nameof(IMoveItSessionService.Get)
            ? new MoveItSessionState { WebAdminUrl = "https://moveit.test", AccessToken = "test-token", AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) }
            : throw new NotSupportedException(method.Name));
        var client = new MoveItRestClient(new ClientFactory(handler), session,
            ConfigurationStartupState.Capture(new ConfigurationBuilder().Build()), null!, NullLogger<MoveItRestClient>.Instance);
        var state = await fixture.Run(client);
        Assert.True(state.Success, state.Message);
        Assert.True(state.Restored);
        Assert.Equal(2, handler.Calls); // Both preflight and completion use the same HTTP contract.
        Assert.Equal(2, fixture.Writes);
    }

    [Fact]
    public async Task RejectedReportPreflightCannotBackUpModifyOrStartTheTask()
    {
        var fixture = new Fixture { PreflightFailure = new MoveItApiException("Invalid predicate", HttpStatusCode.UnprocessableEntity) };
        var state = await fixture.Run();
        Assert.False(state.Success); Assert.True(state.Restored);
        Assert.Equal(0, fixture.Backups); Assert.Equal(0, fixture.Writes); Assert.Equal(0, fixture.Starts);
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task PermanentReportFailureAfterStartStopsWithoutReplayingOrUnsafeRestoration(HttpStatusCode status)
    {
        var fixture = new Fixture { CompletionFailure = new MoveItApiException("Report rejected", status) };
        var state = await fixture.Run().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(state.Completed); Assert.False(state.Success); Assert.False(state.Restored);
        Assert.Equal(1, fixture.Starts); Assert.Equal(1, fixture.Writes); Assert.Equal(1, fixture.Backups);
        Assert.False(TaskRunAsOfDefinition.Enabled(fixture.Definition));
        Assert.Contains("run state", state.Message); Assert.Contains("Undo", state.Message);
    }

    private sealed class ReportHandler(Fixture fixture) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/v1/reports/taskruns", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer test-token", request.Headers.Authorization!.ToString());
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
            // Assert the on-wire syntax, not just that a mocked service was called.
            Assert.Equal("TaskID==42", body["predicate"]!.ToString());
            Assert.Equal("!StartTime", body["orderBy"]!.ToString());
            Assert.Equal(20, body["maxCount"]!.GetValue<int>());
            return new(HttpStatusCode.OK) { Content = new StringContent(new JsonObject
                { ["items"] = new JsonArray(fixture.ReportRows().Select(row => (JsonNode?)row).ToArray()) }.ToJsonString(), Encoding.UTF8, "application/json") };
        }
    }
    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, disposeHandler: false); }

    private static JsonObject Definition() => JsonNode.Parse("""{"ID":"42","Name":"Date task","Active":1,"TT":"","xRun":1,"xAlter":1,"Info":{"Description":"Original"},"steps":[{"Source":{"ID":"11","Path":"/[YYYY]/[MM]","FileMask":"[DD]*.txt","Password":"keep"}}]}""")!.AsObject();
    private sealed class Fixture
    {
        public JsonObject Definition { get; } = TaskRunAsOfTests.Definition();
        public int Backups, Starts, Writes;
        public bool DisabledAtStart, ChangeTemporaryField;
        public string RunStatus = "Success";
        public Exception? StartFailure, PreflightFailure, CompletionFailure;
        public IReadOnlyList<JsonObject> ReportRows() => Starts == 0 ? [] : [new() { ["TaskID"]="42", ["NominalStart"]="run-1", ["EndTime"]="2026-10-17T00:00:00Z", ["StatusMsg"]=RunStatus }];
        public Fixture(string type = "") => Definition["TT"] = type;
        public Task<TaskRunAsOfState> Run(IMoveItRestClient? reportClient = null)
        {
            var crud = Stub<ITaskCrudCoordinator>((method,args) => method.Name switch
            {
                nameof(ITaskCrudCoordinator.GetDefinitionAsync) => Task.FromResult(Definition.DeepClone().AsObject()),
                nameof(ITaskCrudCoordinator.UpdateEditableFreshAsync) => Update(args),
                _ => throw new NotSupportedException(method.Name)
            });
            Task Update(object?[] args)
            {
                var copy = ((Func<JsonObject,JsonObject>)args[2]!)(Definition.DeepClone().AsObject());
                ((Action<JsonObject>?)args[3])?.Invoke(copy);
                Definition.Clear(); foreach (var pair in copy) Definition[pair.Key] = pair.Value?.DeepClone(); Writes++;
                return Task.CompletedTask;
            }
            var automation = Stub<IAutomationTaskService>((method,_) =>
            {
                if (method.Name == nameof(IAutomationTaskService.InvalidateCache)) return null;
                Assert.Equal(nameof(IAutomationTaskService.RunTaskNowAsync), method.Name);
                Starts++; DisabledAtStart = !TaskRunAsOfDefinition.Enabled(Definition);
                Definition["Info"]!["Description"] = "Concurrent description";
                if (ChangeTemporaryField) Definition["steps"]![0]!["Source"]!["Path"] = "/other-user-change";
                return StartFailure is null ? Task.FromResult(new TaskRunNowResult("42","Date task","run-1")) : Task.FromException<TaskRunNowResult>(StartFailure);
            });
            var rest = Stub<IMoveItRestClient>((method,args) => method.Name switch
            {
                nameof(IMoveItRestClient.GetRunningTasksAsync) => Task.FromResult<IReadOnlyList<MoveItRunningTask>>([]),
                nameof(IMoveItRestClient.GetTaskRunReportAsync) => Report(args),
                _ => throw new NotSupportedException(method.Name)
            });
            Task<IReadOnlyList<JsonObject>> Report(object?[] args)
            {
                Assert.Equal("TaskID==42", args[0]); Assert.Equal("!StartTime", args[1]); Assert.Equal(20, args[2]);
                var failure = Starts == 0 ? PreflightFailure : CompletionFailure;
                if (failure is not null) return Task.FromException<IReadOnlyList<JsonObject>>(failure);
                return reportClient is null ? Task.FromResult(ReportRows())
                    : reportClient.GetTaskRunReportAsync((string)args[0]!, (string)args[1]!, (int)args[2]!, (CancellationToken)args[3]!);
            }
            var undo = Stub<ITaskUndoService>((method,_) => { Assert.Equal(nameof(ITaskUndoService.CaptureAsync),method.Name); Backups++; return Task.FromResult(new TaskUndoSnapshot("backup","42","Date task","user",DateTimeOffset.UtcNow,"<Task/>")); });
            return new TaskRunAsOfService(crud,automation,rest,undo,new(),NullLogger<TaskRunAsOfService>.Instance).RunAsync("42",Guid.NewGuid().ToString(),new(2026,10,1,23,0,5),"user","session","server");
        }
    }
    private static T Stub<T>(Func<MethodInfo,object?[],object?> handler) where T:class
    { var proxy=DispatchProxy.Create<T,HostSharedCrudTests.CrudProxy>(); ((HostSharedCrudTests.CrudProxy)(object)proxy).Handler=handler; return proxy; }
}
