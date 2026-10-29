using System.Reflection;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;
using Xunit;

namespace MoveItAutomationUI.Tests.MoveIt;

/// <summary>
/// Phase 2 persistence tests use interface proxies, not a live MOVEit server.
/// They verify API call sequencing and the pre-/post-PUT failure boundary.
/// </summary>
public sealed class TraditionalTaskPersistenceTests
{
    [Fact]
    public async Task SharedTraditionalCrud_ReadsThroughRest()
    {
        var read = 0;
        var rest = Fake<IMoveItRestClient>((method, arguments) => method.Name switch
        {
            nameof(IMoveItRestClient.CheckoutTaskV0Async) or nameof(IMoveItRestClient.ReleaseTaskCheckoutV0Async) => Task.CompletedTask,
            nameof(IMoveItRestClient.GetTaskAsync) => Read(),
            _ => throw new NotSupportedException(method.Name)
        });
        Task<JsonObject> Read()
        {
            read++;
            return Task.FromResult(new JsonObject { ["ID"] = "17", ["TT"] = "Tr" });
        }
        var service = Service(rest);
        var task = await service.GetDefinitionAsync(" 17 ");
        Assert.Equal("17", task["ID"]!.ToString());
        Assert.Equal(1, read);
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetDefinitionAsync("  "));
    }

    [Fact]
    public async Task SharedTraditionalRead_RejectsAdvancedTask()
    {
        var rest = Fake<IMoveItRestClient>((method, _) => method.Name switch
        {
            nameof(IMoveItRestClient.CheckoutTaskV0Async) or nameof(IMoveItRestClient.ReleaseTaskCheckoutV0Async) => Task.CompletedTask,
            nameof(IMoveItRestClient.GetTaskAsync) => Task.FromResult(new JsonObject { ["ID"] = "17", ["TT"] = "COS" }),
            _ => throw new NotSupportedException(method.Name)
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(rest).GetDefinitionAsync("17"));
    }

    [Fact]
    public async Task UpdateFresh_PreservesUnknownFields_AndVerifiesReadback()
    {
        var original = JsonNode.Parse("""
            {"ID":"100","Name":"Old","TT":"Tr","FutureMOVEitField":{"token":"keep"},"Active":1}
            """)!.AsObject();
        JsonObject? sent = null;
        var gets = 0;
        var puts = 0;
        var rest = Fake<IMoveItRestClient>((method, arguments) => method.Name switch
        {
            nameof(IMoveItRestClient.CheckoutTaskV0Async) or nameof(IMoveItRestClient.ReleaseTaskCheckoutV0Async) => Task.CompletedTask,
            nameof(IMoveItRestClient.GetTaskAsync) => Task.FromResult(++gets == 1
                ? original.DeepClone().AsObject()
                : sent!.DeepClone().AsObject()),
            nameof(IMoveItRestClient.UpdateTaskAsync) => OnUpdate(arguments),
            _ => throw new NotSupportedException(method.Name)
        });
        Task OnUpdate(object?[] arguments)
        {
            puts++;
            sent = ((JsonObject)arguments[1]!).DeepClone().AsObject();
            return Task.CompletedTask;
        }
        var service = Service(rest);
        await service.UpdateFreshAsync("100", task =>
        {
            task["Name"] = "New";
            return task;
        }, saved => Assert.Equal("New", saved["Name"]!.GetValue<string>()));

        Assert.Equal(2, gets);
        Assert.Equal(1, puts);
        Assert.Equal("Old", original["Name"]!.GetValue<string>());
        Assert.Equal("keep", sent!["FutureMOVEitField"]!["token"]!.GetValue<string>());
        Assert.Equal("New", sent["Name"]!.GetValue<string>());
    }

    [Fact]
    public async Task UpdateFresh_SpreadsheetMode_DoesNotChangeOriginalReadbackCount()
    {
        var gets = 0;
        var puts = 0;
        var rest = Fake<IMoveItRestClient>((method, _) => method.Name switch
        {
            nameof(IMoveItRestClient.CheckoutTaskV0Async) or nameof(IMoveItRestClient.ReleaseTaskCheckoutV0Async) => Task.CompletedTask,
            nameof(IMoveItRestClient.GetTaskAsync) => Read(),
            nameof(IMoveItRestClient.UpdateTaskAsync) => Save(),
            _ => throw new NotSupportedException(method.Name)
        });
        Task<JsonObject> Read()
        {
            gets++;
            return Task.FromResult(new JsonObject { ["ID"] = "34", ["TT"] = "Tr" });
        }
        Task Save()
        {
            puts++;
            return Task.CompletedTask;
        }
        await Service(rest).UpdateFreshAsync("34", task => task);
        Assert.Equal(1, gets);
        Assert.Equal(1, puts);
    }

    [Fact]
    public async Task UpdateFresh_PrePutValidationFailure_DoesNotCallPut()
    {
        var puts = 0;
        var rest = Fake<IMoveItRestClient>((method, _) => method.Name switch
        {
            nameof(IMoveItRestClient.CheckoutTaskV0Async) or nameof(IMoveItRestClient.ReleaseTaskCheckoutV0Async) => Task.CompletedTask,
            nameof(IMoveItRestClient.GetTaskAsync) => Task.FromResult(new JsonObject { ["ID"] = "10", ["TT"] = "COS" }),
            nameof(IMoveItRestClient.UpdateTaskAsync) => Put(),
            _ => throw new NotSupportedException(method.Name)
        });
        Task Put() { puts++; return Task.CompletedTask; }
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Service(rest).UpdateFreshAsync("10", task => task));
        Assert.Contains("not a Traditional", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, puts);
    }

    [Fact]
    public async Task UpdateFresh_LostReadbackAfterAcceptedPut_IsPartialSaveNotConfirmedFailure()
    {
        var gets = 0;
        var puts = 0;
        var rest = Fake<IMoveItRestClient>((method, _) => method.Name switch
        {
            nameof(IMoveItRestClient.CheckoutTaskV0Async) or nameof(IMoveItRestClient.ReleaseTaskCheckoutV0Async) => Task.CompletedTask,
            nameof(IMoveItRestClient.GetTaskAsync) => Read(),
            nameof(IMoveItRestClient.UpdateTaskAsync) => Put(),
            _ => throw new NotSupportedException(method.Name)
        });
        Task<JsonObject> Read()
        {
            if (++gets == 1) return Task.FromResult(new JsonObject { ["ID"] = "10", ["TT"] = "Tr" });
            throw new InvalidOperationException("Connection lost after MOVEit accepted PUT.");
        }
        Task Put() { puts++; return Task.CompletedTask; }
        var ex = await Assert.ThrowsAsync<TraditionalTaskPartialSaveException>(
            () => Service(rest).UpdateFreshAsync("10", task => task, requireReadback: true));
        Assert.Equal("10", ex.TaskId);
        Assert.Equal(1, puts);
    }

    [Fact]
    public async Task UpdateFresh_RejectsMismatchedTaskIdBeforePut()
    {
        var puts = 0;
        var rest = Fake<IMoveItRestClient>((method, _) => method.Name switch
        {
            nameof(IMoveItRestClient.CheckoutTaskV0Async) or nameof(IMoveItRestClient.ReleaseTaskCheckoutV0Async) => Task.CompletedTask,
            nameof(IMoveItRestClient.GetTaskAsync) => Task.FromResult(new JsonObject { ["ID"] = "10", ["TT"] = "Tr" }),
            nameof(IMoveItRestClient.UpdateTaskAsync) => Put(),
            _ => throw new NotSupportedException(method.Name)
        });
        Task Put() { puts++; return Task.CompletedTask; }
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(rest).UpdateFreshAsync("10", task => { task["ID"] = "11"; return task; }));
        Assert.Equal(0, puts);
    }

    [Fact]
    public async Task CreateDefinition_SendsDetachedCopyOfAnalyzedPayload()
    {
        JsonObject? received = null;
        var rest = Fake<IMoveItRestClient>((method, arguments) => method.Name switch
        {
            nameof(IMoveItRestClient.CreateTaskDefinitionAsync) => Create(arguments),
            _ => throw new NotSupportedException(method.Name)
        });
        Task<JsonObject> Create(object?[] arguments)
        {
            received = (JsonObject)arguments[0]!;
            received["Name"] = "MOVEit altered its own copy";
            return Task.FromResult(new JsonObject { ["ID"] = "199" });
        }
        var original = new JsonObject { ["Name"] = "Original" };
        var result = await Service(rest).CreateDefinitionAsync(original);
        Assert.Equal("199", result["ID"]!.GetValue<string>());
        Assert.Equal("Original", original["Name"]!.GetValue<string>());
    }

    [Fact]
    public async Task CreateDisabledDefinition_UsesRestOnly_AndPreservesGroup()
    {
        JsonObject? observed = null;
        var rest = Fake<IMoveItRestClient>((method, arguments) => method.Name switch
        {
            nameof(IMoveItRestClient.CreateTaskDefinitionAsync) => Create(arguments),
            _ => throw new NotSupportedException(method.Name)
        });
        Task<JsonObject> Create(object?[] args)
        {
            observed = ((JsonObject)args[0]!).DeepClone().AsObject();
            return Task.FromResult(new JsonObject { ["ID"] = "99" });
        }
        var result = await Service(rest).CreateDisabledDefinitionAsync("Demo", "Operations");
        Assert.Equal("99", result["ID"]!.GetValue<string>());
        Assert.Equal("Demo", observed!["Name"]!.GetValue<string>());
        Assert.Equal(0, observed["Active"]!.GetValue<int>());
        Assert.Equal("Operations", observed["Group"]!.AsArray()[0]!.GetValue<string>());
        Assert.Empty(observed["steps"]!.AsArray());
    }

    [Fact]
    public async Task CreateDisabledDefinition_RejectsEmptyTaskName_BeforeRest()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Service().CreateDisabledDefinitionAsync(" "));
    }

    [Fact]
    public void Merger_PreservesUnknownFields_AndUsesRequestedFieldNames()
    {
        var current = JsonNode.Parse("""
            {"id":"4","name":"old","info":{"Description":"old"},"schedules":{},"Steps":[],"nextactions":{"NextAction":[1]},"SecretFromMOVEit":"retain","group":["Prod"],"active":1}
            """)!.AsObject();
        var requested = JsonNode.Parse("""
            {"Name":"new","Info":{"Description":"new"},"Schedules":{"Schedule":[]},"steps":[]}
            """)!.AsObject();
        var result = TraditionalTaskDefinitionMerger.PrepareSpreadsheetUpdate(current, requested, true);
        Assert.Equal("new", result["Name"]!.GetValue<string>());
        Assert.Equal("retain", result["SecretFromMOVEit"]!.GetValue<string>());
        Assert.Equal("Prod", result["group"]!.AsArray()[0]!.GetValue<string>());
        Assert.Null(result["nextactions"]);
        Assert.Equal(0, result["active"]!.GetValue<int>());
        Assert.Null(result["name"]);
        Assert.Equal("old", current["name"]!.GetValue<string>());
    }

    [Fact]
    public void Merger_RejectsMissingOwnedFieldsWithoutDeletingLiveData()
    {
        var current = new JsonObject { ["Name"] = "Original", ["Info"] = new JsonObject() };
        var requested = new JsonObject { ["Name"] = "Revised" };
        var ex = Assert.Throws<InvalidOperationException>(() =>
            TraditionalTaskDefinitionMerger.MergeOwnedProperties(current, requested, new[] { "Name", "Info" }));
        Assert.Contains("Info", ex.Message);
        Assert.NotNull(current["Info"]);
    }

    [Fact]
    public void NativeScheduleXml_PreservesUnrelatedTaskElements()
    {
        const string xml = """
            <Task ID="123" Name="Demo"><Info Description="retain"/><Schedules><Schedule ID="1001"><Frequency><Interval ID="2001" StartTime="00:00" EndTime="23:59" EveryMinutes="15"/></Frequency></Schedule></Schedules><UnknownVendorElement value="unchanged" /></Task>
            """;
        var requested = JsonNode.Parse("""
            [{"id":"1001","serverIndex":0,"onlyUntilFirstSuccess":false,"failIfNoSuccessInSchedule":false,"runEvenIfNotifications":false,"intervals":[{"id":"2001","startTime":"01:00","endTime":"02:00","everyMinutes":15}]}]
            """)!.AsArray();
        var updated = TraditionalTaskScheduleXml.ApplySchedules(xml, requested);
        Assert.Contains("UnknownVendorElement", updated);
        Assert.Contains("unchanged", updated);
        var schedules = TraditionalTaskScheduleXml.ReadSchedules(updated);
        Assert.Single(schedules);
        Assert.Equal("1001", schedules[0]["ID"]!.ToString());
    }

    [Fact]
    public async Task NativeScheduleAdapter_UsesComCheckoutTransformInsteadOfRestPut()
    {
        const string xml = """
            <Task ID="123" Name="Demo"><Schedules><Schedule ID="1001"><Frequency><Interval ID="2001" StartTime="00:00" EndTime="23:59" EveryMinutes="15"/></Frequency></Schedule></Schedules><KeepMe/></Task>
            """;
        var nativeCalls = 0;
        var com = Fake<IMoveItComClient>((method, arguments) => method.Name switch
        {
            nameof(IMoveItComClient.ModifyTaskConfigurationAsync) => Update(arguments),
            _ => throw new NotSupportedException(method.Name)
        });
        Task<MoveItTaskConfiguration> Update(object?[] arguments)
        {
            nativeCalls++;
            var transform = (Func<string, string>)arguments[1]!;
            var updatedXml = transform(xml);
            return Task.FromResult(new MoveItTaskConfiguration("123", "Demo", updatedXml));
        }
        var schedules = JsonNode.Parse("""
            [{"id":"1001","intervals":[{"id":"2001","startTime":"03:00","endTime":"04:00","everyMinutes":15}]}]
            """)!.AsArray();
        var updated = await Service(com: com).UpdateNativeSchedulesAsync("123", schedules);
        Assert.Equal(1, nativeCalls);
        Assert.Contains("KeepMe", updated.Xml);
        Assert.Contains("03:00", updated.Xml);
    }

    private static TraditionalTaskPersistenceService Service(
        IMoveItRestClient? rest = null, IMoveItComClient? com = null)
        => new(rest ?? Fake<IMoveItRestClient>((m, _) => throw new NotSupportedException(m.Name)),
            com ?? Fake<IMoveItComClient>((m, _) => throw new NotSupportedException(m.Name)),
            new TraditionalTaskDefinitionComposer(new TraditionalTaskHostProfileCatalog()));

    private static T Fake<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, InterfaceStub>();
        ((InterfaceStub)(object)proxy).Handler = handler;
        return proxy;
    }

    public class InterfaceStub : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?>? Handler { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => Handler!(targetMethod!, args ?? Array.Empty<object?>());
    }

    [Fact]
    public async Task ResolvedRestCreate_UsesOnePostAndReconcilesOmittedId()
    {
        var posts = 0;
        var lists = 0;
        var rest = Fake<IMoveItRestClient>((method, _) => method.Name switch
        {
            nameof(IMoveItRestClient.CreateTaskDefinitionAsync) => Post(),
            nameof(IMoveItRestClient.GetTasksAsync) => List(),
            _ => throw new NotSupportedException(method.Name)
        });
        Task<JsonObject> Post()
        {
            posts++;
            return Task.FromResult(new JsonObject { ["Name"] = "Imported" });
        }
        Task<IReadOnlyList<JsonObject>> List()
        {
            lists++;
            return Task.FromResult((IReadOnlyList<JsonObject>)new[]
            {
                new JsonObject { ["ID"] = "91", ["Name"] = "Imported" }
            });
        }
        var created = await Service(rest).CreateResolvedDefinitionAsync(new JsonObject { ["Name"] = "Imported" });
        Assert.Equal("91", created.Id);
        Assert.Equal(1, posts);
        Assert.Equal(1, lists);
    }

    [Fact]
    public async Task ResolvedRestCreate_AmbiguousIdDoesNotRepeatPost()
    {
        var posts = 0;
        var rest = Fake<IMoveItRestClient>((method, _) => method.Name switch
        {
            nameof(IMoveItRestClient.CreateTaskDefinitionAsync) => Post(),
            nameof(IMoveItRestClient.GetTasksAsync) => Task.FromResult((IReadOnlyList<JsonObject>)new[]
            {
                new JsonObject { ["ID"] = "41", ["Name"] = "Imported" },
                new JsonObject { ["ID"] = "42", ["Name"] = "Imported" }
            }),
            _ => throw new NotSupportedException(method.Name)
        });
        Task<JsonObject> Post()
        {
            posts++;
            return Task.FromResult(new JsonObject());
        }
        var error = await Assert.ThrowsAsync<TraditionalTaskCreationUnverifiedException>(() =>
            Service(rest).CreateResolvedDefinitionAsync(new JsonObject { ["Name"] = "Imported" }));
        Assert.Contains("uniquely resolved", error.Message);
        Assert.Equal(1, posts);
    }

    [Fact]
    public async Task ResolvedRestCreate_VerifiesProcessThroughSharedReadback()
    {
        var posts = 0;
        var reads = 0;
        var rest = Fake<IMoveItRestClient>((method, _) => method.Name switch
        {
            nameof(IMoveItRestClient.CreateTaskDefinitionAsync) => Post(),
            nameof(IMoveItRestClient.CheckoutTaskV0Async) or nameof(IMoveItRestClient.ReleaseTaskCheckoutV0Async) => Task.CompletedTask,
            nameof(IMoveItRestClient.GetTaskAsync) => Read(),
            _ => throw new NotSupportedException(method.Name)
        });
        Task<JsonObject> Post()
        {
            posts++;
            return Task.FromResult(new JsonObject { ["ID"] = "55", ["Name"] = "Adhoc" });
        }
        Task<JsonObject> Read()
        {
            reads++;
            return Task.FromResult(new JsonObject { ["ID"] = "55", ["TT"] = "Tr", ["Name"] = "Adhoc" });
        }
        var verified = false;
        var created = await Service(rest).CreateResolvedDefinitionAsync(new JsonObject { ["Name"] = "Adhoc" },
            verifyPersisted: (id, task) =>
            {
                verified = true;
                Assert.Equal("55", id);
                Assert.Equal("Adhoc", task["Name"]!.GetValue<string>());
            });
        Assert.Equal("55", created.Id);
        Assert.True(verified);
        Assert.Equal(1, posts);
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task ResolvedRestCreate_FailedReadbackIsIndeterminateAndNotRetried()
    {
        var posts = 0;
        var rest = Fake<IMoveItRestClient>((method, _) => method.Name switch
        {
            nameof(IMoveItRestClient.CreateTaskDefinitionAsync) => Post(),
            nameof(IMoveItRestClient.CheckoutTaskV0Async) or nameof(IMoveItRestClient.ReleaseTaskCheckoutV0Async) => Task.CompletedTask,
            nameof(IMoveItRestClient.GetTaskAsync) => Task.FromException<JsonObject>(new InvalidOperationException("read failed")),
            _ => throw new NotSupportedException(method.Name)
        });
        Task<JsonObject> Post()
        {
            posts++;
            return Task.FromResult(new JsonObject { ["ID"] = "56" });
        }
        var error = await Assert.ThrowsAsync<TraditionalTaskCreationUnverifiedException>(() =>
            Service(rest).CreateResolvedDefinitionAsync(new JsonObject { ["Name"] = "Adhoc" }, requireReadback: true));
        Assert.Equal("56", error.TaskId);
        Assert.Equal(1, posts);
    }
}

