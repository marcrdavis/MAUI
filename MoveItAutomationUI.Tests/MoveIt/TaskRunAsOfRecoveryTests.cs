using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Tasks;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class TaskRunAsOfRecoveryTests
{
    [Fact]
    public async Task RecoveryMaterialIsProtectedAndPreparedButUnwrittenTaskNeedsNoRestore()
    {
        using var f = new Fixture(); var record = await f.Prepare(RunAsOfRecoveryStage.Prepared, apply: false);
        var text = await File.ReadAllTextAsync(f.PathFor(record));
        Assert.DoesNotContain("access-token", text); Assert.DoesNotContain("password-never-persist", text); Assert.DoesNotContain("/[YYYY]", text);
        using var restarted = f.Create(); await restarted.LoadAsync(default); await restarted.ReconcileAsync(default);
        Assert.False(File.Exists(f.PathFor(record))); Assert.True(f.Writes == 0);
    }
    [Theory]
    [InlineData(RunAsOfRecoveryStage.Prepared)]
    [InlineData(RunAsOfRecoveryStage.Applied)]
    [InlineData(RunAsOfRecoveryStage.RestoreReady)]
    [InlineData(RunAsOfRecoveryStage.Restoring)]
    [InlineData(RunAsOfRecoveryStage.Running)]
    public async Task RestartRestoresVerifiedTemporaryFieldsAndPreservesUnrelatedEdits(RunAsOfRecoveryStage stage)
    {
        using var f = new Fixture(); var record = await f.Prepare(stage); f.Current["Info"]!["Description"] = "Another user's description";
        using var restarted = f.Create(); await restarted.LoadAsync(default); await restarted.ReconcileAsync(default);
        Assert.True(f.Writes == 1); Assert.True(TaskRunAsOfDefinition.Enabled(f.Current));
        Assert.Equal("/[YYYY]", f.Current["steps"]![0]!["Source"]!["Path"]!.ToString());
        Assert.Equal("Another user's description", f.Current["Info"]!["Description"]!.ToString()); Assert.False(File.Exists(f.PathFor(record)));
    }
    [Fact]
    public async Task ActiveRunIsNotRestoredUntilCompletion()
    {
        using var f = new Fixture(); var record = await f.Prepare(RunAsOfRecoveryStage.Running); f.Running = true;
        using var restarted = f.Create(); await restarted.LoadAsync(default); await restarted.ReconcileAsync(default);
        Assert.True(f.Writes == 0); Assert.True(File.Exists(f.PathFor(record)));
        f.Running = false; await restarted.ReconcileAsync(default);
        Assert.True(f.Writes == 1); Assert.False(File.Exists(f.PathFor(record)));
    }
    [Fact]
    public async Task RefreshedRecoveryTokensSurviveAnotherRestartWhileWaitingForCompletion()
    {
        using var f = new Fixture(); var record = await f.Prepare(RunAsOfRecoveryStage.Running);
        f.Running = true; f.RotateTokens = true;
        using (var restarted = f.Create()) { await restarted.LoadAsync(default); await restarted.ReconcileAsync(default); }
        f.RotateTokens = false;
        using var restartedAgain = f.Create(); await restartedAgain.LoadAsync(default); await restartedAgain.ReconcileAsync(default);
        Assert.Equal("refreshed-access-token", f.LastRecoveryToken); Assert.True(f.Writes == 0); Assert.True(File.Exists(f.PathFor(record)));
    }
    [Fact]
    public async Task AnUnreadableRecordDoesNotPreventOtherTaskRecovery()
    {
        using var f = new Fixture(); var record = await f.Prepare(RunAsOfRecoveryStage.Prepared, apply: false);
        var unreadable = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(f.PathFor(record))!, Guid.NewGuid().ToString("N") + ".recovery");
        await File.WriteAllTextAsync(unreadable, "not-a-protected-record");
        using var restarted = f.Create(); await restarted.LoadAsync(default); await restarted.ReconcileAsync(default);
        Assert.True(File.Exists(unreadable)); Assert.False(File.Exists(f.PathFor(record))); Assert.True(f.Writes == 0);
    }
    [Fact]
    public async Task ExpiredAuthenticationIsRetainedAndReportedOncePerRecoveryProcess()
    {
        using var f = new Fixture(); var record = await f.Prepare(RunAsOfRecoveryStage.Running); f.AuthenticationDenied = true;
        using (var restarted = f.Create())
        { await restarted.LoadAsync(default); await restarted.ReconcileAsync(default); await restarted.ReconcileAsync(default); }
        Assert.Single(f.Warnings); Assert.True(f.Writes == 0); Assert.True(File.Exists(f.PathFor(record)));
        using var restartedAgain = f.Create(); await restartedAgain.LoadAsync(default); await restartedAgain.ReconcileAsync(default); await restartedAgain.ReconcileAsync(default);
        Assert.True(f.Warnings.Count == 2); Assert.True(f.Writes == 0); Assert.True(File.Exists(f.PathFor(record)));
    }
    [Fact]
    public async Task LostRestoreResponseIsReconciledWithoutRepeatingTheWrite()
    {
        using var f = new Fixture(); var record = await f.Prepare(RunAsOfRecoveryStage.Running); f.LoseRestoreResponse = true;
        using var restarted = f.Create(); await restarted.LoadAsync(default); await restarted.ReconcileAsync(default);
        Assert.True(File.Exists(f.PathFor(record))); Assert.True(f.Writes == 1);
        await restarted.ReconcileAsync(default);
        Assert.True(f.Writes == 1); Assert.False(File.Exists(f.PathFor(record)));
    }
    [Fact]
    public async Task ConcurrentFieldChangeIsPreservedAndRequiresReview()
    {
        using var f = new Fixture(); var record = await f.Prepare(RunAsOfRecoveryStage.Running);
        f.Current["steps"]![0]!["Source"]!["Path"] = "/other-edit";
        using var restarted = f.Create(); await restarted.LoadAsync(default); await restarted.ReconcileAsync(default); await restarted.ReconcileAsync(default);
        Assert.True(f.Writes == 0); Assert.True(File.Exists(f.PathFor(record))); Assert.Equal("/other-edit", f.Current["steps"]![0]!["Source"]!["Path"]!.ToString());
    }
    [Fact]
    public async Task UncertainStartWithMultipleRunsCannotGuessWhichRunCompleted()
    {
        using var f = new Fixture(); var record = await f.Prepare(RunAsOfRecoveryStage.StartPending); record.NominalStart = "";
        // Rewrite the checkpoint without a start acknowledgement, as before a process failure.
        await f.Initial.CheckpointAsync(record, RunAsOfRecoveryStage.StartPending, f.Session);
        f.History = [Row("run-one"), Row("run-two")];
        using var restarted = f.Create(); await restarted.LoadAsync(default); await restarted.ReconcileAsync(default);
        Assert.True(f.Writes == 0); Assert.True(File.Exists(f.PathFor(record)));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownOrDeniedCheckoutAvailabilityCannotTriggerRestore(bool explicitDenial)
    {
        using var f = new Fixture(); var record = await f.Prepare(RunAsOfRecoveryStage.Running);
        if (explicitDenial) f.Current["xAlter"] = 0; else f.Current.Remove("xAlter");
        using var restarted = f.Create(); await restarted.LoadAsync(default); await restarted.ReconcileAsync(default);
        Assert.True(f.Writes == 0); Assert.True(File.Exists(f.PathFor(record)));
    }
    [Fact]
    public async Task AnOperationStillRunningInThisProcessCannotBeRecoveredByItsWorker()
    {
        using var f = new Fixture(); var record = await f.Prepare(RunAsOfRecoveryStage.Running);
        await f.Initial.ReconcileAsync(default); Assert.True(f.Writes == 0); Assert.True(File.Exists(f.PathFor(record)));
    }
    [Fact]
    public async Task PendingRecoveryBlocksAnotherRunButManualOriginalRestorationClearsIt()
    {
        using var f = new Fixture(); var record = await f.Prepare(RunAsOfRecoveryStage.Running);
        using var restarted = f.Create(); await restarted.LoadAsync(default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => restarted.EnsureCanBeginAsync(f.Session, f.Current, default));
        TaskRunAsOfDefinition.Apply(f.Current, record.Changes, true, true);
        await restarted.EnsureCanBeginAsync(f.Session, f.Current, default); Assert.False(File.Exists(f.PathFor(record)));
    }
    private static JsonObject Row(string nominal) => new() { ["TaskID"] = "42", ["NominalStart"] = nominal, ["EndTime"] = "2026-10-24T00:00:00Z" };
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MAUI-RunAsOf-Recovery-" + Guid.NewGuid().ToString("N"));
        public JsonObject Current { get; } = JsonNode.Parse("""{"ID":"42","Name":"Dates","Active":1,"TT":"","xAlter":1,"Info":{"Description":"Original"},"steps":[{"Source":{"ID":"11","Path":"/[YYYY]","FileMask":"*.*"}}]}""")!.AsObject();
        public MoveItSessionState Session { get; } = new() { WebAdminUrl = "https://moveit.test", Username = "user", AccessToken = "access-token", Password = "password-never-persist" };
        public bool Running, LoseRestoreResponse, RotateTokens, AuthenticationDenied;
        public List<string> Warnings { get; } = [];
        public string LastRecoveryToken = "";
        private MoveItSessionState? _recoverySession;
        public int Writes;
        public IReadOnlyList<JsonObject> History = [Row("run-one")];
        private readonly IDataProtectionProvider _protection = new EphemeralDataProtectionProvider();
        private readonly IWebHostEnvironment _environment;
        private readonly IObjectEditLeaseJournal _tokens;
        private readonly ServiceProvider _services;
        public TaskRunAsOfRecoveryService Initial { get; }
        public Fixture()
        {
            _environment = DispatchProxy.Create<IWebHostEnvironment, EnvironmentCopyJournalTests.EnvironmentStub>();
            ((EnvironmentCopyJournalTests.EnvironmentStub)(object)_environment).Root = Root;
            _tokens = new ObjectEditLeaseJournal(_environment, _protection, NullLogger<ObjectEditLeaseJournal>.Instance);
            var tasks = Stub<ITaskCrudCoordinator>((method, args) => method.Name switch
            {
                nameof(ITaskCrudCoordinator.GetDefinitionAsync) => AuthenticationDenied
                    ? Task.FromException<JsonObject>(new MoveItAuthenticationException("Credentials expired")) : Task.FromResult(Current.DeepClone().AsObject()),
                nameof(ITaskCrudCoordinator.UpdateEditableFreshAsync) => Update(args),
                _ => throw new NotSupportedException(method.Name)
            });
            Task Update(object?[] args)
            {
                var next = ((Func<JsonObject, JsonObject>)args[2]!)(Current.DeepClone().AsObject());
                ((Action<JsonObject>?)args[3])?.Invoke(next);
                Current.Clear(); foreach (var pair in next) Current[pair.Key] = pair.Value?.DeepClone(); Writes++;
                return LoseRestoreResponse ? Task.FromException(new HttpRequestException("Lost response")) : Task.CompletedTask;
            }
            var rest = Stub<IMoveItRestClient>((method, _) => method.Name switch
            {
                nameof(IMoveItRestClient.GetRunningTasksAsync) => RunningTasks(),
                nameof(IMoveItRestClient.GetTaskRunReportAsync) => Task.FromResult(History),
                _ => throw new NotSupportedException("Recovery must never call " + method.Name)
            });
            Task<IReadOnlyList<MoveItRunningTask>> RunningTasks()
            {
                if (RotateTokens && _recoverySession is not null)
                { _recoverySession.AccessToken = "refreshed-access-token"; _recoverySession.RefreshToken = "refreshed-refresh-token"; }
                return Task.FromResult<IReadOnlyList<MoveItRunningTask>>(Running ? [new("42", "Dates", "user", "Running", "2026-10-24T00:00:00Z", "run-one", null, null, "", "")] : []);
            }
            var sessions = Stub<IMoveItSessionService>((method, args) =>
            {
                if (method.Name != nameof(IMoveItSessionService.PushBackgroundSession)) throw new NotSupportedException(method.Name);
                _recoverySession = (MoveItSessionState)args[0]!; LastRecoveryToken = _recoverySession.AccessToken; return new Scope();
            });
            _services = new ServiceCollection().AddSingleton(tasks).AddSingleton(rest).AddSingleton(sessions).BuildServiceProvider();
            Initial = Create();
        }
        public TaskRunAsOfRecoveryService Create() => new(_environment, _protection, _tokens, _services.GetRequiredService<IServiceScopeFactory>(), new RecoveryLogger(Warnings));
        public async Task<RunAsOfRecoveryRecord> Prepare(RunAsOfRecoveryStage stage, bool apply = true)
        {
            var changes = TaskRunAsOfDefinition.Describe(Current, new DateTime(2026, 10, 1));
            var record = await Initial.BeginAsync(Guid.NewGuid().ToString(), "42", MoveItTaskKind.Traditional, true, changes, [], "original-snapshot", Session, default);
            if (apply) TaskRunAsOfDefinition.Apply(Current, changes, false, true);
            await Initial.CheckpointAsync(record, stage, Session, stage == RunAsOfRecoveryStage.Running ? "run-one" : "");
            return record;
        }
        public string PathFor(RunAsOfRecoveryRecord record) => System.IO.Path.Combine(Root, "Data", "RunAsOfRecovery", record.JobId + ".recovery");
        public void Dispose() { Initial.Dispose(); _services.Dispose(); if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
    private sealed class RecoveryLogger(List<string> warnings) : ILogger<TaskRunAsOfRecoveryService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (level == LogLevel.Warning) warnings.Add(formatter(state, exception)); }
    }
    private sealed class Scope : IDisposable { public void Dispose() { } }
    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, HostSharedCrudTests.CrudProxy>(); ((HostSharedCrudTests.CrudProxy)(object)proxy).Handler = handler; return proxy;
    }
}
