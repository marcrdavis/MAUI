// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI

using System.Reflection;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Tasks;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;
using Xunit;

namespace MoveItAutomationUI.Tests.MoveIt;

/// <summary>
/// Mock-transport tests for the single Task CRUD boundary. These do not exercise
/// MOVEit's live REST or Windows COM implementation.
/// </summary>
public sealed class TaskCrudCoordinatorTests
{
    [Theory]
    [InlineData("Traditional", MoveItTaskKind.Traditional)]
    [InlineData("Tr", MoveItTaskKind.Traditional)]
    [InlineData("Advanced", MoveItTaskKind.Advanced)]
    [InlineData("COS", MoveItTaskKind.Advanced)]
    [InlineData("Synchronization", MoveItTaskKind.Synchronization)]
    [InlineData("Sync", MoveItTaskKind.Synchronization)]
    [InlineData("Sy", MoveItTaskKind.Synchronization)]
    public void NormalizesKnownMoveItTaskTypes(string type, MoveItTaskKind expected)
        => Assert.Equal(expected, MoveItTaskKinds.Parse(type));

    [Fact]
    public void UnknownTaskTypeIsNotSilentlyCreatedAsTraditional()
        => Assert.Throws<InvalidOperationException>(() => MoveItTaskKinds.Parse("Invalid"));

    [Fact]
    public async Task TraditionalSkeletonUsesRestAndPreservesGroup()
    {
        var postCount = 0;
        JsonObject? submitted = null;
        var rest = Stub<IMoveItRestClient>((method, args) => method.Name switch
        {
            nameof(IMoveItRestClient.CreateTaskDefinitionAsync) => Create(args),
            _ => throw new NotSupportedException($"Unexpected REST operation: {method.Name}")
        });
        Task<JsonObject> Create(object?[] args)
        {
            postCount++;
            submitted = ((JsonObject)args[0]!).DeepClone().AsObject();
            return Task.FromResult(new JsonObject { ["ID"] = "900", ["Name"] = "Imported" });
        }
        var coordinator = Build(rest: rest);
        var created = await coordinator.CreateEmptyAsync("Imported", "Traditional", "Finance");
        Assert.Equal("900", created.Id);
        Assert.Equal("Traditional", created.TaskType);
        Assert.Equal(1, postCount);
        Assert.Equal("Finance", submitted!["Group"]![0]!.GetValue<string>());
        Assert.Empty(submitted["steps"]!.AsArray());
        Assert.Equal(0, submitted["Active"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("Advanced", "Advanced")]
    [InlineData("Synchronization", "Synchronization")]
    public async Task RetainedComStrategiesCanStillCreateAdvancedAndSyncSkeletons(string type, string expectedComType)
    {
        var comCalls = 0;
        var com = Stub<IMoveItComClient>((method, args) => method.Name switch
        {
            nameof(IMoveItComClient.CreateTaskAsync) => Create(args),
            _ => throw new NotSupportedException($"Unexpected COM operation: {method.Name}")
        });
        Task<MoveItCreatedTask> Create(object?[] args)
        {
            comCalls++;
            Assert.Equal(expectedComType, (string)args[1]!);
            return Task.FromResult(new MoveItCreatedTask("555", "Native", expectedComType));
        }
        var result = await new NativeTaskCrudStrategy(MoveItTaskKinds.Parse(type), com).CreateEmptyAsync("Native", null, CancellationToken.None);
        Assert.Equal("555", result.Id);
        Assert.Equal(expectedComType, result.TaskType);
        Assert.Equal(1, comCalls);
    }

    [Fact]
    public async Task TraditionalCreateWithMissingIdDoesNotRetryOrFallbackToCom()
    {
        var postCount = 0;
        var listCount = 0;
        var rest = Stub<IMoveItRestClient>((method, _) => method.Name switch
        {
            nameof(IMoveItRestClient.CreateTaskDefinitionAsync) => Post(),
            nameof(IMoveItRestClient.GetTasksAsync) => List(),
            _ => throw new NotSupportedException(method.Name)
        });
        Task<JsonObject> Post()
        {
            postCount++;
            return Task.FromResult(new JsonObject { ["Name"] = "NoId" });
        }
        Task<IReadOnlyList<JsonObject>> List()
        {
            listCount++;
            return Task.FromResult((IReadOnlyList<JsonObject>)Array.Empty<JsonObject>());
        }
        // An accepted POST whose ID cannot be reconciled has a distinct, exact
        // exception type. It must never trigger another POST or COM fallback.
        var ex = await Assert.ThrowsAsync<TraditionalTaskCreationUnverifiedException>(
            () => Build(rest: rest).CreateEmptyAsync("NoId", "Traditional"));
        Assert.Equal("NoId", ex.TaskName);
        Assert.Null(ex.TaskId);
        Assert.Contains("could not be uniquely resolved", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, postCount);
        Assert.Equal(1, listCount);
    }

    [Fact]
    public async Task TraditionalCreationWithoutResponseIdReconcilesOnceByName()
    {
        var postCount = 0;
        var listingCount = 0;
        var rest = Stub<IMoveItRestClient>((method, _) => method.Name switch
        {
            nameof(IMoveItRestClient.CreateTaskDefinitionAsync) => Post(),
            nameof(IMoveItRestClient.GetTasksAsync) => List(),
            _ => throw new NotSupportedException(method.Name)
        });
        Task<JsonObject> Post()
        {
            postCount++;
            return Task.FromResult(new JsonObject { ["Name"] = "New Task" });
        }
        Task<IReadOnlyList<JsonObject>> List()
        {
            listingCount++;
            return Task.FromResult((IReadOnlyList<JsonObject>)new[]
            {
                new JsonObject { ["Name"] = "New Task", ["ID"] = "777" }
            });
        }
        var created = await Build(rest: rest).CreateEmptyAsync("New Task", "Traditional");
        Assert.Equal("777", created.Id);
        Assert.Equal(1, postCount);
        Assert.Equal(1, listingCount);
    }

    [Fact]
    public async Task SourceTimestampMutationsUseSharedCoordinatorTransport()
    {
        var requestedHost = string.Empty;
        var rest = Stub<IMoveItRestClient>((method, args) => method.Name switch
        {
            nameof(IMoveItRestClient.GetTaskNewFileTimestampsV0Async) => Read(args),
            nameof(IMoveItRestClient.UpdateTaskNewFileTimestampV0Async) => Save(args),
            _ => throw new NotSupportedException(method.Name)
        });
        Task<JsonObject> Read(object?[] args)
        {
            requestedHost = (string)args[1]!;
            return Task.FromResult(new JsonObject());
        }
        Task<JsonObject> Save(object?[] args)
        {
            Assert.Equal("80", (string)args[0]!);
            return Task.FromResult(new JsonObject { ["status"] = "success" });
        }
        var coordinator = Build(rest: rest);
        await coordinator.GetSourceTimestampsAsync("80", "21");
        await coordinator.SetSourceTimestampAsync("80", "21", "/path", "*.csv", "2026-10-11 20:00:00");
        Assert.Equal("21", requestedHost);
    }

    [Fact]
    public async Task SharedDeleteUsesVerifiedRestWithoutCom()
    {
        var deleteCalls = 0;
        var rest = Stub<IMoveItRestClient>((method, args) =>
        {
            Assert.Equal(nameof(IMoveItRestClient.DeleteTaskAsync), method.Name);
            Assert.Equal("21", (string)args[0]!); deleteCalls++; return Task.CompletedTask;
        });
        var coordinator = Build(rest);
        await coordinator.DeleteAsync(" 21 ");
        Assert.Equal(1, deleteCalls);
        await Assert.ThrowsAsync<ArgumentException>(() => coordinator.DeleteAsync(" "));
    }

    [Fact]
    public async Task NativeConfigurationImportRetainsComForAllTaskTypes()
    {
        var comCalls = 0;
        var com = Stub<IMoveItComClient>((method, args) => method.Name switch
        {
            nameof(IMoveItComClient.CreateTaskFromConfigurationAsync) => Import(args),
            _ => throw new NotSupportedException(method.Name)
        });
        Task<MoveItTaskConfiguration> Import(object?[] args)
        {
            comCalls++;
            Assert.True((bool)args[2]!);
            return Task.FromResult(new MoveItTaskConfiguration("40", "Carrier", "<Task />"));
        }
        var result = await Build(com: com).CreateFromNativeConfigurationAsync("Carrier", "<Task />", leaveDisabled: true);
        Assert.Equal("40", result.Id);
        Assert.Equal(1, comCalls);
    }

    private static TaskCrudCoordinator Build(IMoveItRestClient? rest = null, IMoveItComClient? com = null)
    {
        rest ??= Stub<IMoveItRestClient>((method, _) => throw new NotSupportedException(method.Name));
        com ??= Stub<IMoveItComClient>((method, _) => throw new NotSupportedException(method.Name));
        var composer = new TraditionalTaskDefinitionComposer(new TraditionalTaskHostProfileCatalog());
        var persistence = new TraditionalTaskPersistenceService(rest, com, composer);
        return new TaskCrudCoordinator(new ITaskCrudStrategy[]
        {
            new TraditionalTaskCrudStrategy(persistence),
            new RestV0TaskCrudStrategy(MoveItTaskKind.Advanced, rest),
            new RestV0TaskCrudStrategy(MoveItTaskKind.Synchronization, rest)
        }, persistence, rest, com);
    }

    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, CrudProxy>();
        ((CrudProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    public class CrudProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?>? Handler { get; set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => Handler!(targetMethod!, args ?? Array.Empty<object?>());
    }

    [Fact]
    public void StrategiesDescribeEditFormatsIndependentlyOfNativeSkeletonCreation()
    {
        var rest = new TraditionalTaskCrudStrategy(new TraditionalTaskPersistenceService(
            Stub<IMoveItRestClient>((method, _) => throw new NotSupportedException(method.Name)),
            Stub<IMoveItComClient>((method, _) => throw new NotSupportedException(method.Name)),
            new TraditionalTaskDefinitionComposer(new TraditionalTaskHostProfileCatalog())));
        var com = Stub<IMoveItComClient>((method, _) => throw new NotSupportedException(method.Name));
        ITaskCrudStrategy advanced = new NativeTaskCrudStrategy(MoveItTaskKind.Advanced, com);
        ITaskCrudStrategy sync = new NativeTaskCrudStrategy(MoveItTaskKind.Synchronization, com);
        Assert.Equal(MoveItTaskDefinitionFormat.RestJson, rest.DefinitionFormat);
        Assert.Equal(MoveItTaskDefinitionFormat.RestJsonV0, advanced.DefinitionFormat);
        Assert.Equal(MoveItTaskDefinitionFormat.RestJsonV0, sync.DefinitionFormat);
    }
}

