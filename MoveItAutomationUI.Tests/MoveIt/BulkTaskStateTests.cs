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
