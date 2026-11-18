using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Tasks;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class BulkTaskStateTests
{
    [Fact]
    public async Task ReopeningTaskInformationReadsTheAuthoritativeCrudDefinition()
    {
        var reads = 0;
        var rest = Stub<IMoveItRestClient>((method, _) => throw new Exception("Information must not reuse grid inventory: " + method.Name));
        var crud = Stub<ITaskCrudCoordinator>((method, _) =>
        {
            Assert.Equal(nameof(ITaskCrudCoordinator.GetDefinitionAsync), method.Name);
            return Task.FromResult(new JsonObject { ["ID"] = "1", ["Name"] = ++reads == 1 ? "First" : "Concurrent change", ["TT"] = "Tr" });
        });
        var session = Stub<IMoveItSessionService>((_, _) => new MoveItSessionState { ServerKey = "Test", Username = "tester" });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new AutomationTaskService(rest, crud, session, cache, new Snapshot(), NullLogger<AutomationTaskService>.Instance);
        Assert.Equal("First", (await service.GetBasicDetailsAsync("1")).Name);
        Assert.Equal("Concurrent change", (await service.GetBasicDetailsAsync("1")).Name);
        Assert.Equal(2, reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnablingUnscheduledTasksWorksForSingleAndBulkSelections(bool bulk)
    {
        var tasks = new List<JsonObject>
        {
            new() { ["ID"] = "1", ["Name"] = "Traditional", ["Active"] = 0, ["TT"] = "Tr", ["Scheduled"] = "UNSCHEDULED" },
            new() { ["ID"] = "2", ["Name"] = "Advanced", ["Active"] = 0, ["TT"] = "COS", ["Scheduled"] = "UNSCHEDULED" },
            new() { ["ID"] = "3", ["Name"] = "Sync", ["Active"] = 0, ["TT"] = "Sy", ["Scheduled"] = "UNSCHEDULED" },
            new() { ["ID"] = "4", ["Name"] = "Incomplete", ["Active"] = 0, ["TT"] = "Tr", ["Scheduled"] = "INCOMPLETE" }
        };
        var rest = Stub<IMoveItRestClient>((method, _) => method.Name switch
        {
            nameof(IMoveItRestClient.GetTaskGridDefinitionsAsync) => Task.FromResult<IReadOnlyList<JsonObject>>(tasks),
            nameof(IMoveItRestClient.GetRunningTasksAsync) => Task.FromResult<IReadOnlyList<MoveItRunningTask>>([]),
            nameof(IMoveItRestClient.GetTaskRunsAsync) => Task.FromResult<IReadOnlyList<MoveItTaskRunRecord>>([]),
            _ => throw new NotSupportedException(method.Name)
        });
        var writes = new List<string>();
        var crud = Stub<ITaskCrudCoordinator>((method, args) =>
        {
            Assert.Equal(nameof(ITaskCrudCoordinator.SetEnabledAsync), method.Name);
            var id = Assert.IsType<string>(args[0]);
            Assert.True(Assert.IsType<bool>(args[1]));
            writes.Add(id);
            tasks.Single(task => task["ID"]!.ToString() == id)["Active"] = 1;
            return Task.CompletedTask;
        });
        var session = Stub<IMoveItSessionService>((_, _) => new MoveItSessionState { ServerKey = "Test", Username = "tester" });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new AutomationTaskService(rest, crud, session, cache, new Snapshot(), NullLogger<AutomationTaskService>.Instance);

        string[] selected = bulk ? ["1", "2", "3", "4"] : ["1"];
        string[] expectedWrites = bulk ? ["1", "2", "3"] : ["1"];
        var result = await service.SetTasksEnabledAsync(selected, true);

        Assert.Equal(expectedWrites, writes);
        Assert.Equal(expectedWrites.Length, result.Succeeded);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(bulk ? 1 : 0, result.Failed);
        if (bulk)
            Assert.Equal("Task is incomplete and cannot be enabled.", Assert.Single(result.Items, item => item.Id == "4").Message);

        var refreshed = await service.GetTasksAsync();
        foreach (var id in expectedWrites)
        {
            var task = Assert.Single(refreshed, item => item.Id == id);
            Assert.True(task.Enabled);
            Assert.Equal("Ready", task.Status);
            Assert.Equal("Unscheduled", task.Scheduled);
        }
    }

    [Theory]
    [InlineData(true,0,-1)][InlineData(false,0,-1)]
    [InlineData(true,3,-1)][InlineData(false,3,-1)]
    [InlineData(true,2,10)][InlineData(false,2,10)]
    public async Task LargeBatchesRetainPerItemOutcomesAsLiveStatesChange(bool enabled,int already,int failure)
    {
        var tasks=Enumerable.Range(0,16).Select(i=>new JsonObject {
            ["ID"]=i.ToString(),["Name"]=$"Task {i}",["Active"]=i<already?(enabled?1:0):(enabled?0:1),["TT"]="Tr",["Scheduled"]="Scheduled"
        }).ToList();
        var writes=new List<string>();
        var rest=Stub<IMoveItRestClient>((method,_)=>method.Name switch {
            nameof(IMoveItRestClient.GetTaskGridDefinitionsAsync)=>Task.FromResult<IReadOnlyList<JsonObject>>(tasks),
            nameof(IMoveItRestClient.GetRunningTasksAsync)=>Task.FromResult<IReadOnlyList<MoveItRunningTask>>(Array.Empty<MoveItRunningTask>()),
            nameof(IMoveItRestClient.GetTaskRunsAsync)=>Task.FromResult<IReadOnlyList<MoveItTaskRunRecord>>(Array.Empty<MoveItTaskRunRecord>()),
            _=>throw new NotSupportedException(method.Name)
        });
        var crud=Stub<ITaskCrudCoordinator>((method,args)=>{
            Assert.Equal(nameof(ITaskCrudCoordinator.SetEnabledAsync),method.Name);
            var id=(string)args[0]!;writes.Add(id);
            if(id==failure.ToString()) return Task.FromException(new MoveItComException("Rejected by MOVEit"));
            tasks[int.Parse(id)]["Active"]=enabled?1:0;
            return Task.CompletedTask;
        });
        var session=Stub<IMoveItSessionService>((_,_)=>new MoveItSessionState{ServerKey="Test",Username="tester"});
        using var cache=new MemoryCache(new MemoryCacheOptions());
        var service=new AutomationTaskService(rest,crud,session,cache,new Snapshot(),NullLogger<AutomationTaskService>.Instance);
        var ids=tasks.Select(t=>t["ID"]!.ToString()).Concat(new[]{"15","0"}).ToArray();
        var result=await service.SetTasksEnabledAsync(ids,enabled);
        Assert.Equal(16,result.Items.Count);Assert.Equal(16-already,writes.Count);
        Assert.Equal(already,result.Skipped);Assert.Equal(failure<0?0:1,result.Failed);
        Assert.Equal(16-already-(failure<0?0:1),result.Succeeded);
        Assert.Equal(16,writes.Count+already);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task FirstUncertainMutationInvalidatesInventoryEvenWithoutAnAcknowledgedSuccess(bool information)
    {
        var reads = 0;
        var task = new JsonObject { ["ID"] = "1", ["Name"] = "Before", ["Active"] = 1, ["TT"] = "Tr", ["Scheduled"] = "Scheduled" };
        var rest = Stub<IMoveItRestClient>((method, _) => method.Name switch
        {
            nameof(IMoveItRestClient.GetTaskGridDefinitionsAsync) => Read(),
            nameof(IMoveItRestClient.GetRunningTasksAsync) => Task.FromResult<IReadOnlyList<MoveItRunningTask>>(Array.Empty<MoveItRunningTask>()),
            nameof(IMoveItRestClient.GetTaskRunsAsync) => Task.FromResult<IReadOnlyList<MoveItTaskRunRecord>>(Array.Empty<MoveItTaskRunRecord>()),
            nameof(IMoveItRestClient.TaskNameExistsForOtherTaskAsync) => Task.FromResult(false),
            _ => throw new NotSupportedException(method.Name)
        });
        Task<IReadOnlyList<JsonObject>> Read() { reads++; return Task.FromResult<IReadOnlyList<JsonObject>>([task.DeepClone().AsObject()]); }
        var crud = Stub<ITaskCrudCoordinator>((method, _) =>
        {
            Assert.Equal(information ? nameof(ITaskCrudCoordinator.UpdateBasicDetailsAsync) : nameof(ITaskCrudCoordinator.SetEnabledAsync), method.Name);
            if (information) task["Name"] = "After"; else task["Active"] = 0;
            return Task.FromException(new TraditionalTaskPartialSaveException("1", "Readback failed after PUT", new HttpRequestException()));
        });
        var session = Stub<IMoveItSessionService>((_, _) => new MoveItSessionState { ServerKey = "Test", Username = "tester" });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new AutomationTaskService(rest, crud, session, cache, new Snapshot(), NullLogger<AutomationTaskService>.Instance);
        await service.GetTasksAsync();
        if (information) await Assert.ThrowsAsync<TraditionalTaskPartialSaveException>(() => service.UpdateBasicDetailsAsync("1", "After", "", ""));
        else Assert.Equal(1, (await service.SetTasksEnabledAsync(["1"], false)).Failed);
        var beforeReload = reads;
        var refreshed = await service.GetTasksAsync();
        Assert.Equal(beforeReload + 1, reads);
        Assert.Collection(refreshed, saved => { Assert.Equal(information ? "After" : "Before", saved.Name); Assert.Equal(information, saved.Enabled); });
    }

    [Theory]
    [InlineData("add", true, true, 0, "new@example.test", 5, 1)]
    [InlineData("add", true, true, null, " ; ", 4, -1)]
    [InlineData("update", true, true, null, "", 4, 2)]
    [InlineData("update", true, false, null, "new@example.test", 4, 2)]
    [InlineData("update", false, false, null, "new@example.test", 4, 0)]
    [InlineData("update", false, false, 0, "new@example.test", 4, 1)]
    [InlineData("remove", true, false, null, "", 2, 0)]
    public async Task NotificationActionsApplyOnlyToTheirIntendedTargets(
        string mode, bool failure, bool preserveRecipients, int? index, string addressTo,
        int expectedCount, int expectedChangedSubjects)
    {
        var task = JsonNode.Parse("""
            {"ID":"1","Name":"Task","TT":"Tr","NextActions":{"NextAction":[
              {"Type":"Email","DoAfter":"Task","DoIfFailure":1,"AddressTo":"one@example.test","Subject":"First"},
              {"Type":"Email","DoAfter":"Task","DoIfFailure":1,"AddressTo":"two@example.test","Subject":"Second"},
              {"Type":"Email","DoAfter":"FileDone","DoIfFailure":1,"AddressTo":"file@example.test","Subject":"File"},
              {"Type":"Task","DoAfter":"Task","DoIfFailure":1,"TaskID":"2"}
            ]}}
            """)!.AsObject();
        var original = task.DeepClone();
        var writes = 0;
        var rest = Stub<IMoveItRestClient>((method, _) => throw new NotSupportedException(method.Name));
        var crud = Stub<ITaskCrudCoordinator>((method, args) => method.Name switch
        {
            nameof(ITaskCrudCoordinator.GetDefinitionAsync) => Task.FromResult(task.DeepClone().AsObject()),
            nameof(ITaskCrudCoordinator.UpdateTraditionalFreshAsync) => Apply((Func<JsonObject, JsonObject>)args[1]!),
            _ => throw new NotSupportedException(method.Name)
        });
        Task Apply(Func<JsonObject, JsonObject> patch)
        {
            writes++;
            task = patch(task.DeepClone().AsObject());
            return Task.CompletedTask;
        }
        var session = Stub<IMoveItSessionService>((_, _) => new MoveItSessionState { ServerKey = "Test", Username = "tester" });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new AutomationTaskService(rest, crud, session, cache, new Snapshot(), NullLogger<AutomationTaskService>.Instance);
        var request = new TaskNotificationUpdateRequest(mode, index, false, failure, !failure, false,
            false, "100", addressTo, preserveRecipients, "Changed", "Body");

        if (expectedChangedSubjects < 0)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateNotificationAsync("1", request));
            Assert.Equal("Enter at least one recipient.", error.Message);
            Assert.Equal(0, writes);
            return;
        }
        var result = await service.UpdateNotificationAsync("1", request);
        Assert.Equal(expectedChangedSubjects > 0 || mode == "remove", result.Changed);
        Assert.Equal(result.Changed ? 1 : 0, writes);

        var actions = task["NextActions"]!["NextAction"]!.AsArray();
        var originalActions = original["NextActions"]!["NextAction"]!.AsArray();
        Assert.Equal(expectedCount, actions.Count);
        var changed = actions.Where(action => (string?)action?["Subject"] == "Changed").ToArray();
        Assert.Equal(expectedChangedSubjects, changed.Length);
        Assert.True(JsonNode.DeepEquals(originalActions[2], actions[mode == "remove" ? 0 : 2]));
        Assert.True(JsonNode.DeepEquals(originalActions[3], actions[mode == "remove" ? 1 : 3]));
        if (mode == "add")
        {
            Assert.True(JsonNode.DeepEquals(originalActions[0], actions[0]));
            Assert.True(JsonNode.DeepEquals(originalActions[1], actions[1]));
        }
        if (mode == "update" && preserveRecipients)
            Assert.Equal(new[] { "one@example.test", "two@example.test" }, changed.Select(action => (string?)action?["AddressTo"]));
        else
            Assert.All(changed, action => Assert.Equal(addressTo, (string?)action?["AddressTo"]));
    }

    private sealed class Snapshot:IOptionsSnapshot<TaskBehaviorOptions> {
        public TaskBehaviorOptions Value {get;}=new(); public TaskBehaviorOptions Get(string? name)=>Value;
    }
    private static T Stub<T>(Func<MethodInfo,object?[],object?> handler) where T:class {
        var value=DispatchProxy.Create<T,Proxy>();((Proxy)(object)value).Handler=handler;return value;
    }
    public class Proxy:DispatchProxy {
        public Func<MethodInfo,object?[],object?> Handler {get;set;}=null!;
        protected override object? Invoke(MethodInfo? method,object?[]? args)=>Handler(method!,args??Array.Empty<object?>());
    }
}
