using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Tasks;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class TaskOutcomeViewTests
{
    private static readonly DateTimeOffset Started = new(2026, 10, 15, 22, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Success", "Success", "success")]
    [InlineData("Failure", "Failed", "failed")]
    [InlineData("No xfers", "No Transfers", "no-transfers")]
    [InlineData("No Transfers", "No Transfers", "no-transfers")]
    [InlineData("No Transfer", "No Transfers", "no-transfers")]
    [InlineData("Successful", "Success", "success")]
    [InlineData("Failed", "Failed", "failed")]
    [InlineData("  sUcCeSs  ", "Success", "success")]
    public async Task OutcomeViewsIncludeEnabledAndDisabledTasksOfEveryType(string nativeStatus, string expectedStatus, string filter)
    {
        var definitions = new List<JsonObject>();
        var history = new List<MoveItTaskRunRecord>();
        foreach (var type in new[] { "Tr", "COS", "Sy" })
        foreach (var enabled in new[] { false, true })
        {
            var id = $"{type}-{enabled}";
            definitions.Add(Definition(id, type, enabled));
            // Deliberately unsorted: the grid must use the latest result, even with zero files.
            history.Add(Run(id, nativeStatus, Started));
            history.Add(Run(id, "Failure", Started.AddHours(-1)));
        }
        using var fixture = new Fixture(definitions, () => history);
        var tasks = await fixture.Service.GetTasksAsync();

        Assert.Equal(6, tasks.Count);
        Assert.All(tasks, task =>
        {
            Assert.Equal(expectedStatus, task.Status);
            Assert.Equal("Manually started result", task.LastResult);
            Assert.Equal(Started, task.LastRun);
            Assert.Equal(0, task.FilesSent);
            Assert.Equal(task.Id.EndsWith("True", StringComparison.Ordinal), task.Enabled);
        });
        Assert.Equal(tasks.Select(task => task.Id).OrderBy(id => id), Filter(tasks, filter).Select(task => task.Id).OrderBy(id => id));
        foreach (var other in new[] { "running", "success", "failed", "no-transfers" }.Where(other => other != filter))
            Assert.Empty(Filter(tasks, other));
        Assert.Equal(6, Filter(tasks, "all").Count);
        Assert.Equal(3, tasks.Count(task => !task.Enabled));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunningStateTakesPrecedenceOverPreviousOutcomeWithoutChangingEnabled(bool enabled)
    {
        var running = new[] { new MoveItRunningTask("1", "Manual Task", "tester", "Copying", "", "", null, null, "", "") };
        using var fixture = new Fixture([Definition("1", "COS", enabled)], () => [Run("1", "Success", Started)], running);
        var tasks = await fixture.Service.GetTasksAsync();
        Assert.Equal("Running", tasks[0].Status);
        Assert.Equal(enabled, tasks[0].Enabled);
        Assert.Equal("Copying", tasks[0].LastResult);
        Assert.Collection(Filter(tasks, "running"), task => Assert.Equal("1", task.Id));
        Assert.Empty(Filter(tasks, "success"));
    }

    [Theory]
    [InlineData("", true, "Ready")]
    [InlineData("", false, "Disabled")]
    [InlineData("Unrecognized", true, "Ready")]
    [InlineData("Unrecognized", false, "Disabled")]
    public async Task MissingOrUnknownHistoryDoesNotInventAnOutcome(string nativeStatus, bool enabled, string expected)
    {
        using var fixture = new Fixture([Definition("1", "Tr", enabled)],
            () => nativeStatus.Length == 0 ? [] : [Run("1", nativeStatus, Started)]);
        var tasks = await fixture.Service.GetTasksAsync();
        Assert.Equal(expected, tasks[0].Status);
        foreach (var filter in new[] { "success", "failed", "no-transfers" }) Assert.Empty(Filter(tasks, filter));
    }

    [Fact]
    public async Task ForcedRefreshMovesADisabledTaskBetweenOutcomeViews()
    {
        IReadOnlyList<MoveItTaskRunRecord> history = [Run("1", "Success", Started)];
        using var fixture = new Fixture([Definition("1", "Sy", false)], () => history);
        var first = await fixture.Service.GetTaskSnapshotAsync(true);
        Assert.Collection(Filter(first.Tasks, "success"), task => Assert.Equal("1", task.Id));
        history = [Run("1", "Failure", Started.AddMinutes(1)), Run("1", "Success", Started)];
        var failed = await fixture.Service.GetTaskSnapshotAsync(true);
        Assert.Empty(Filter(failed.Tasks, "success"));
        Assert.Collection(Filter(failed.Tasks, "failed"), task => Assert.Equal("1", task.Id));
        history = [Run("1", "No xfers", Started.AddMinutes(2)), Run("1", "Failure", Started.AddMinutes(1))];
        var noTransfers = await fixture.Service.GetTaskSnapshotAsync(true);
        Assert.Empty(Filter(noTransfers.Tasks, "failed"));
        Assert.Collection(Filter(noTransfers.Tasks, "no-transfers"), task => Assert.Equal("1", task.Id));
        Assert.False(noTransfers.Tasks[0].Enabled);
    }

    private static IReadOnlyList<AutomationTask> Filter(IReadOnlyList<AutomationTask> tasks, string filter)
        => (IReadOnlyList<AutomationTask>)typeof(TasksController).GetMethod("ApplyTaskFilter", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [tasks, filter])!;

    private static JsonObject Definition(string id, string type, bool enabled) => new()
    {
        ["ID"] = id, ["Name"] = "Manual Task " + id, ["TT"] = type, ["Active"] = enabled ? 1 : 0,
        ["Scheduled"] = enabled ? "ENABLED" : "DISABLED"
    };

    private static MoveItTaskRunRecord Run(string id, string status, DateTimeOffset started)
        => new(id, "Manual Task " + id, status, "Manually started result", 0, 0, started, started.AddSeconds(1), started.ToString("O"));

    private sealed class Fixture : IDisposable
    {
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());
        public AutomationTaskService Service { get; }
        public Fixture(IReadOnlyList<JsonObject> definitions, Func<IReadOnlyList<MoveItTaskRunRecord>> history,
            IReadOnlyList<MoveItRunningTask>? running = null)
        {
            var rest = Stub<IMoveItRestClient>((method, _) => method.Name switch
            {
                nameof(IMoveItRestClient.GetTaskGridDefinitionsAsync) => Task.FromResult(definitions),
                nameof(IMoveItRestClient.GetTaskRunsAsync) => Task.FromResult(history()),
                nameof(IMoveItRestClient.GetRunningTasksAsync) => Task.FromResult(running ?? Array.Empty<MoveItRunningTask>()),
                _ => throw new NotSupportedException(method.Name)
            });
            var crud = Stub<ITaskCrudCoordinator>((method, _) => throw new InvalidOperationException("Outcome views must not mutate tasks: " + method.Name));
            var session = Stub<IMoveItSessionService>((_, _) => new MoveItSessionState { ServerKey = "Test", Username = "tester" });
            Service = new AutomationTaskService(rest, crud, session, _cache, new Options(), NullLogger<AutomationTaskService>.Instance);
        }
        public void Dispose() => _cache.Dispose();
    }

    private sealed class Options : IOptionsSnapshot<TaskBehaviorOptions>
    {
        public TaskBehaviorOptions Value { get; } = new();
        public TaskBehaviorOptions Get(string? name) => Value;
    }
    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var value = DispatchProxy.Create<T, Proxy>();
        ((Proxy)(object)value).Handler = handler;
        return value;
    }
    public class Proxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args ?? []);
    }
}
