using System.Reflection;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Tasks;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class TaskJsonEditorTests
{
    private static TaskJsonValueChange Change(string path, JsonNode? value) => new(path.Split('/'),value);
    private static JsonObject Definition(string tt = "") => JsonNode.Parse("""
        {"ID":"900","Name":"Example","TT":"","Active":0,"CacheNames":"random","NextEID":12,
         "Info":{"Description":"Before","Notes":""},"NativeFutureField":"preserved","Group":["GroupA"],
         "Parameters":{"Parameter":[{"Name":"Private","Secure":1,"value":"NEVER_SEND"},{"Name":"Public","Secure":0,"value":"hello"}]},
         "steps":[{"Source":{"ID":"11","HostID":"2","Type":"FTP","FileMask":"*.*","ClientSecret":"KEEP_SECRET","Account":"KEEP_ACCOUNT_SECRET","RetryCount":0}}],
         "Schedules":{"Schedule":[{"OnlyUntilFirstSuccess":0,"FailIfNoSuccessInSched":0,"RunEvenIfNotif":0,
         "Days":{"DayOfWeek":["Monday"]},"Frequency":{"Interval":[{"StartTime":"02:15","EndTime":"02:15","EveryMinutes":0}]}}]}}
        """)!.AsObject().WithType(tt);

    [Fact]
    public async Task ReadMasksCredentialsAndSecureParametersWithoutCheckoutOrModifyingSource()
    {
        var state=new State();var shell=await state.Service.ReadAsync("900",default);
        Assert.Null(shell.Definition["steps"]![0]!["Source"]!["ClientSecret"]);
        Assert.Null(shell.Definition["steps"]![0]!["Source"]!["Account"]);
        Assert.Null(shell.Definition["Parameters"]!["Parameter"]![0]!["value"]);
        Assert.Equal("hello",shell.Definition["Parameters"]!["Parameter"]![1]!["value"]!.ToString());
        Assert.Equal("KEEP_SECRET",state.Task["steps"]![0]!["Source"]!["ClientSecret"]!.ToString());
        Assert.Contains(shell.ReadOnlyPaths,p=>p.SequenceEqual(new[]{"ID"}));
        Assert.True(state.Checkouts==0 && state.Releases==0 && state.Puts==0);
    }
    [Theory]
    [InlineData("Secure", "value")][InlineData("secure", "VALUE")][InlineData("SECURE", "DisplayValue")]
    public void SecureParameterCasingNeverExposesTheValue(string flag,string field)
    {
        var task = new JsonObject { ["Parameters"] = new JsonObject { ["Parameter"] = new JsonArray(new JsonObject { [flag] = 1, [field] = "NEVER_SEND" }) } };
        _ = TaskJsonValues.ReadOnlyPaths(task);
        Assert.Null(task["Parameters"]!["Parameter"]![0]![field]);
    }
    [Theory]
    [InlineData("ID")][InlineData("TT")][InlineData("NextEID")][InlineData("Group/0")]
    [InlineData("steps/0/Source/ID")][InlineData("steps/0/Source/HostID")][InlineData("steps/0/Source/Type")]
    [InlineData("steps/0/Source/ClientSecret")][InlineData("steps/0/Source/Account")][InlineData("Parameters/Parameter/0/value")][InlineData("Parameters/Parameter/0/Secure")]
    public void ProtectedValuesCannotBeChangedByForgedRequests(string path)
        => Assert.Throws<InvalidOperationException>(()=>TaskJsonValues.Apply(Definition(),new[]{Change(path,JsonValue.Create("forged"))}));

    [Theory]
    [InlineData("Info/Description","42")][InlineData("Active","2")][InlineData("Active","0.5")]
    [InlineData("Info/Missing","\"x\"")][InlineData("steps/01/Source/FileMask","\"x\"")]
    [InlineData("Schedules/Schedule/0/OnlyUntilFirstSuccess","2")][InlineData("Info/Description","null")]
    public void InvalidTypesFlagsAndPathsFailBeforeWriting(string path,string json)
        => Assert.Throws<InvalidOperationException>(()=>TaskJsonValues.Apply(Definition(),new[]{Change(path,JsonNode.Parse(json))}));

    [Fact]
    public void NativeStringEncodedFlagsKeepTheirTypeAndStillRequireZeroOrOne()
    {
        var task=Definition();task["Active"]="0";
        Assert.Throws<InvalidOperationException>(()=>TaskJsonValues.Apply(task,[Change("Active",JsonValue.Create("2"))]));
        var changed=TaskJsonValues.Apply(task,[Change("Active",JsonValue.Create("1"))]);
        Assert.Equal("1",changed["Active"]!.GetValue<string>());
    }
    [Fact]
    public void ArraysObjectsDuplicatePathsAndNullEntriesCannotReplaceStructure()
    {
        Assert.Throws<InvalidOperationException>(()=>TaskJsonValues.Apply(Definition(),new[]{Change("steps",new JsonArray())}));
        Assert.Throws<InvalidOperationException>(()=>TaskJsonValues.Apply(Definition(),new[]{Change("Info",new JsonObject())}));
        var change=Change("Info/Description",JsonValue.Create("After"));
        Assert.Throws<InvalidOperationException>(()=>TaskJsonValues.Apply(Definition(),new[]{change,change}));
        Assert.Throws<InvalidOperationException>(()=>TaskJsonValues.Apply(Definition(),new TaskJsonValueChange[]{null!}));
    }
    [Fact]
    public void BooleansMultilineTextAndUnknownExistingScalarsKeepTheirTypesAndUntouchedValues()
    {
        var task=Definition();task["NativeFlag"]=true;
        var saved=TaskJsonValues.Apply(task,new[]{Change("NativeFlag",JsonValue.Create(false)),Change("Info/Notes",JsonValue.Create("first\nsecond")),Change("NativeFutureField",JsonValue.Create("edited"))});
        Assert.False(saved["NativeFlag"]!.GetValue<bool>());Assert.Equal("first\nsecond",saved["Info"]!["Notes"]!.ToString());
        Assert.Equal("preserved",task["NativeFutureField"]!.ToString());Assert.Equal("KEEP_SECRET",saved["steps"]![0]!["Source"]!["ClientSecret"]!.ToString());
    }
    [Fact]
    public void LimitsAndUnsafeNumbersAreBounded()
    {
        var task=Definition();task["Large"]=9007199254740992L;
        Assert.Contains(TaskJsonValues.ReadOnlyPaths(task,false),p=>p.SequenceEqual(new[]{"Large"}));
        Assert.Throws<InvalidOperationException>(()=>TaskJsonValues.Apply(task,Array.Empty<TaskJsonValueChange>()));
        Assert.Throws<InvalidOperationException>(()=>TaskJsonValues.Apply(task,Enumerable.Repeat(Change("Active",JsonValue.Create(1)),2001).ToArray()));
        task["TooLarge"]=new string('x',TaskJsonValues.MaximumDefinitionBytes);
        Assert.Throws<InvalidOperationException>(()=>TaskJsonValues.Project(task,"<Task/>"));
    }
    [Fact]
    public async Task ValidationReadsCurrentDefinitionButDoesNotCheckoutWriteOrCaptureUndo()
    {
        var state=new State();var shell=await state.Service.ReadAsync("900",default);
        await state.Service.ValidateAsync("900",new(shell.EditVersion,[Change("Info/Description",JsonValue.Create("After"))]),default);
        Assert.True(state.Checkouts==0 && state.Puts==0 && state.Releases==0);
    }
    [Theory]
    [InlineData("",false)][InlineData("COS",true)][InlineData("Sy",true)]
    public async Task ValueSaveUsesSharedTypeSelectedTransportAndReadback(string type,bool v0)
    {
        var state=new State(type);var shell=await state.Service.ReadAsync("900",default);
        var version=await state.Service.SaveAsync("900",new(shell.EditVersion,[Change("Info/Description",JsonValue.Create("After"))]),default);
        Assert.NotEqual(shell.EditVersion,version);Assert.Equal("After",state.Task["Info"]!["Description"]!.ToString());
        Assert.Equal("preserved",state.Task["NativeFutureField"]!.ToString());Assert.Equal("KEEP_SECRET",state.Submitted!["steps"]![0]!["Source"]!["ClientSecret"]!.ToString());
        Assert.True(state.Puts==1 && state.Checkouts==1 && state.Releases==1);Assert.Equal(v0,state.UsedV0);
    }
    [Fact]
    public async Task SavedValueMustMatchReadbackAndAnUnknownOutcomeIsNeverRetried()
    {
        var state=new State { DropEdits=true };var shell=await state.Service.ReadAsync("900",default);
        await Assert.ThrowsAsync<TraditionalTaskPartialSaveException>(()=>state.Service.SaveAsync("900",new(shell.EditVersion,[Change("Info/Description",JsonValue.Create("After"))]),default));
        Assert.True(state.Puts==1&&state.Releases==1);
        state=new State { LoseResponse=true };shell=await state.Service.ReadAsync("900",default);
        await Assert.ThrowsAsync<TraditionalTaskPartialSaveException>(()=>state.Service.SaveAsync("900",new(shell.EditVersion,[Change("Info/Description",JsonValue.Create("After"))]),default));
        Assert.True(state.Puts==1&&state.Releases==1);
    }
    [Fact]
    public async Task ConcurrentChangesAreDetectedAgainUnderCheckout()
    {
        var state=new State();var shell=await state.Service.ReadAsync("900",default);
        state.OnCheckout=()=>state.Task["Info"]!["Description"]="Concurrent";
        await Assert.ThrowsAsync<ObjectEditConflictException>(()=>state.Service.SaveAsync("900",new(shell.EditVersion,[Change("Info/Description",JsonValue.Create("After"))]),default));
        Assert.True(state.Puts==0&&state.Checkouts==1&&state.Releases==1);
    }
    [Fact]
    public async Task ExpiredRevisionDeniedAccessAndDuplicateNamesNeverWrite()
    {
        var state=new State();
        await Assert.ThrowsAsync<ObjectEditConflictException>(()=>state.Service.ValidateAsync("900",new("expired",[Change("Active",JsonValue.Create(1))]),default));
        var shell=await state.Service.ReadAsync("900",default);state.Duplicate=true;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>state.Service.SaveAsync("900",new(shell.EditVersion,[Change("Name",JsonValue.Create("Taken"))]),default));
        state.Denied=true;
        await Assert.ThrowsAsync<MoveItObjectAccessException>(()=>state.Service.ReadAsync("900",default));
        Assert.True(state.Checkouts==0&&state.Puts==0);
    }
    [Fact]
    public async Task OvernightPairsAndUnknownNativeAttributesSurviveMixedValueEdits()
    {
        var state=new State();state.Xml="""
            <Task ID="900" Name="Example" NativeTaskSetting="keep"><Schedules NativeSetting="keep"><Schedule ID="21" Multi="1" OnlyUntilFirstSuccess="0" FailIfNoSuccessInSched="0" RunEvenIfNotif="0" Future="preserve"><Days><DayOfWeek>Monday</DayOfWeek></Days><Frequency><Interval ID="31" StartTime="22:00" EndTime="23:59" EveryMinutes="15" FutureInterval="keep"/></Frequency></Schedule><Schedule ID="21" Multi="2" OnlyUntilFirstSuccess="0" FailIfNoSuccessInSched="0" RunEvenIfNotif="0"><Days><DayOfWeek>Tuesday</DayOfWeek></Days><Frequency><Interval ID="31" StartTime="00:00" EndTime="02:00" EveryMinutes="15"/></Frequency></Schedule></Schedules><Info Description="Before"/></Task>
            """;
        var shell=await state.Service.ReadAsync("900",default);
        Assert.True(shell.Definition["Schedules"]!["Schedule"]!.AsArray().Count==2);
        var request=new TaskJsonEditRequest(shell.EditVersion,[Change("Info/Description",JsonValue.Create("After")),Change("Schedules/Schedule/0/OnlyUntilFirstSuccess",JsonValue.Create(1)),Change("Schedules/Schedule/1/OnlyUntilFirstSuccess",JsonValue.Create(1))]);
        var version=await state.Service.SaveAsync("900",request,default);
        Assert.False(string.IsNullOrWhiteSpace(version));var root=XElement.Parse(state.Xml);
        Assert.Equal("keep",root.Attribute("NativeTaskSetting")!.Value);
        Assert.Equal("keep",root.Element("Schedules")!.Attribute("NativeSetting")!.Value);
        Assert.Equal("preserve",root.Descendants("Schedule").First().Attribute("Future")!.Value);
        Assert.Equal("keep",root.Descendants("Interval").First().Attribute("FutureInterval")!.Value);
        Assert.All(root.Descendants("Schedule"),s=>Assert.Equal("1",s.Attribute("OnlyUntilFirstSuccess")!.Value));
        Assert.True(root.Descendants("Schedule").Count()==2&&state.NativeWrites==1&&state.Puts==1);
    }
    [Theory]
    [InlineData("1", "true")][InlineData("true", "1")][InlineData("0", "false")][InlineData("false", "0")]
    [InlineData("0", "null")][InlineData("false", "null")][InlineData("\"\"", "null")]
    public void NativeFlagAndDefaultNormalizationDoesNotProduceFalseSaveFailures(string expected,string actual)
        => Assert.True(TaskJsonValues.Equivalent(JsonNode.Parse(expected),JsonNode.Parse(actual),"Disabled"));

    [Fact]
    public void NativePerFileDefaultAndNewlineNormalizationUseTheSharedTaskRules()
    {
        Assert.True(TaskJsonValues.Equivalent(JsonValue.Create("PerFile"),null,"Run"));
        Assert.True(TaskJsonValues.Equivalent(JsonValue.Create("PerFile"),JsonValue.Create("perfile"),"Run"));
        Assert.True(TaskJsonValues.Equivalent(JsonValue.Create("Line1\r\nLine2"),JsonValue.Create("Line1\nLine2"),"Message"));
        Assert.False(TaskJsonValues.Equivalent(JsonValue.Create("Once"),null,"Run"));
    }

    [Fact]
    public void MissingDefaultsStillRequireTheirContainerAndGenuineLostEditsFailVerification()
    {
        var saved=Definition();saved["steps"]![0]!["Source"]!.AsObject().Remove("Disabled");
        TaskJsonValues.Verify(saved,[Change("steps/0/Source/Disabled",JsonValue.Create(0))]);
        Assert.False(TaskJsonValues.Equivalent(JsonValue.Create(1),null,"Disabled"));
        Assert.False(TaskJsonValues.Equivalent(JsonValue.Create(true),JsonValue.Create(2),"Disabled"));
        Assert.Throws<InvalidOperationException>(()=>TaskJsonValues.Verify(saved,[Change("missing/Disabled",JsonValue.Create(0))]));
    }
    [Fact]
    public void NativeScheduleLeafChangesPreserveTheRestOfTheSubtree()
    {
        const string xml="<Task><Schedules Extra='keep'><Schedule OnlyUntilFirstSuccess='0'><Days><DayOfWeek>Monday</DayOfWeek><DayOfWeek>Tuesday</DayOfWeek></Days><Frequency><Interval StartTime='01:00' EveryMinutes='1' Keep='yes'/></Frequency></Schedule></Schedules></Task>";
        var result=XElement.Parse(TaskJsonValues.NativeSchedules(xml,[Change("Schedules/Schedule/0/Days/DayOfWeek/1",JsonValue.Create("Wednesday")),Change("Schedules/Schedule/0/Frequency/Interval/0/EveryMinutes",JsonValue.Create(15))]));
        Assert.Equal("keep",result.Attribute("Extra")!.Value);Assert.Equal("Wednesday",result.Descendants("DayOfWeek").Last().Value);
        Assert.Equal("15",result.Descendants("Interval").First().Attribute("EveryMinutes")!.Value);Assert.Equal("yes",result.Descendants("Interval").First().Attribute("Keep")!.Value);
    }
    [Fact]
    public void EndpointsAreAuthenticatedNoStoreAndWritesRequireAntiforgery()
    {
        Assert.NotNull(typeof(TaskJsonEditorController).GetCustomAttribute<AuthorizeAttribute>());
        Assert.True(typeof(TaskJsonEditorController).GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
        foreach(var action in new[]{"Validate","Save"})
            Assert.NotNull(typeof(TaskJsonEditorController).GetMethod(action)!.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.False(new UiOptions().TaskJsonEditor.Value);
    }
    [Fact]
    public async Task DisabledFeatureDoesNotReadTasksOrPrepareCheckout()
    {
        var never=Stub<ITaskJsonValueService>((m,_)=>throw new InvalidOperationException(m.Name));
        var controller=new TaskJsonEditorController(never,Stub<IMoveItSessionService>((m,_)=>throw new InvalidOperationException(m.Name)),new Snapshot(new UiOptions()),
            Stub<ITaskUndoService>((m,_)=>throw new InvalidOperationException(m.Name)),Stub<IMetadataRepository>((m,_)=>throw new InvalidOperationException(m.Name)),
            Stub<IAutomationTaskService>((m,_)=>throw new InvalidOperationException(m.Name)),Microsoft.Extensions.Logging.Abstractions.NullLogger<TaskJsonEditorController>.Instance);
        Assert.IsType<NotFoundResult>(await controller.Shell("900",default));
        Assert.IsType<NotFoundResult>(await controller.Save("900",null,default));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EveryTaskCapturesUndoBeforeSavingAndCaptureFailurePreventsTheWrite(bool failCapture)
    {
        var events=new List<string>();var options=new UiOptions();options.TaskJsonEditor.Value=true;
        var session=new MoveItSessionState{Username="editor",ServerKey="test"};
        var values=Stub<ITaskJsonValueService>((m,a)=>
        {
            var id=(string)a[0]!;
            if(m.Name==nameof(ITaskJsonValueService.ValidateAsync)){events.Add("validate:"+id);return Task.CompletedTask;}
            if(m.Name==nameof(ITaskJsonValueService.SaveAsync)){events.Add("save:"+id);return Task.FromResult("v2");}
            if(m.Name==nameof(ITaskJsonValueService.ReadAsync))return Task.FromResult(new TaskJsonEditorDocument(id,"Example","Traditional","v2",Definition(),Array.Empty<string[]>(),false));
            throw new NotSupportedException(m.Name);
        });
        var undo=Stub<ITaskUndoService>((m,a)=>
        {
            if(m.Name!=nameof(ITaskUndoService.CaptureAsync))throw new NotSupportedException(m.Name);
            var id=(string)a[0]!;events.Add("undo:"+id);
            return failCapture?Task.FromException<TaskUndoSnapshot>(new IOException("snapshot unavailable"))
                :Task.FromResult(new TaskUndoSnapshot("snapshot-"+id,id,"Example","editor",DateTimeOffset.UtcNow,"<Task/>"));
        });
        var controller=new TaskJsonEditorController(values,Stub<IMoveItSessionService>((m,_)=>m.Name==nameof(IMoveItSessionService.Get)?session:throw new NotSupportedException(m.Name)),
            new Snapshot(options),undo,Stub<IMetadataRepository>((_,_)=>Task.CompletedTask),Stub<IAutomationTaskService>((_,_)=>null),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TaskJsonEditorController>.Instance);
        foreach(var id in new[]{"900","901"})
        {
            var result=await controller.Save(id,new TaskJsonEditRequest("v1",[Change("Info/Description",JsonValue.Create("After"))]),default);
            if(failCapture)Assert.Equal(502,Assert.IsType<ObjectResult>(result).StatusCode);
            else Assert.IsType<JsonResult>(result);
        }
        Assert.Equal(failCapture?new[]{"validate:900","undo:900","validate:901","undo:901"}
            :new[]{"validate:900","undo:900","save:900","validate:901","undo:901","save:901"},events);
    }
    private sealed class Snapshot(UiOptions value) : IOptionsSnapshot<UiOptions>
    {public UiOptions Value=>value;public UiOptions Get(string? name)=>value;}
    private static T Stub<T>(Func<MethodInfo,object?[],object?> handler) where T:class
    {var proxy=DispatchProxy.Create<T,TaskCrudCoordinatorTests.CrudProxy>();((TaskCrudCoordinatorTests.CrudProxy)(object)proxy).Handler=handler;return proxy;}
    private sealed class State
    {
        public JsonObject Task;public string Xml;public int Puts,Checkouts,Releases,NativeWrites;public bool UsedV0,Duplicate,Denied,DropEdits,LoseResponse;public Action? OnCheckout;public JsonObject? Submitted;
        public TaskJsonValueService Service {get;}
        public State(string type="")
        {
            Task=Definition(type);Xml="<Task ID='900' Name='Example'><Info Description='Before'/></Task>";
            var rest=Stub<IMoveItRestClient>((m,a)=>m.Name switch {
                nameof(IMoveItRestClient.GetTaskAsync)=>System.Threading.Tasks.Task.FromResult(Task.DeepClone().AsObject()),
                nameof(IMoveItRestClient.TaskNameExistsForOtherTaskAsync)=>System.Threading.Tasks.Task.FromResult(Duplicate),
                nameof(IMoveItRestClient.CheckoutTaskV0Async)=>Checkout(),nameof(IMoveItRestClient.ReleaseTaskCheckoutV0Async)=>Release(),
                nameof(IMoveItRestClient.UpdateTaskAsync)=>Put((JsonObject)a[1]!,false),
                nameof(IMoveItRestClient.UpdateTaskV0Async)=>PutV0((JsonObject)a[1]!),_=>throw new NotSupportedException(m.Name)});
            var com=Stub<IMoveItComClient>((m,a)=>m.Name switch {
                nameof(IMoveItComClient.GetTaskConfigurationAsync)=>System.Threading.Tasks.Task.FromResult(Config()),
                nameof(IMoveItComClient.ModifyTaskConfigurationAsync)=>Modify((Func<string,string>)a[1]!),_=>throw new NotSupportedException(m.Name)});
            var access=Stub<IMoveItObjectAccessService>((m,_)=>m.Name==nameof(IMoveItObjectAccessService.RequireAsync)?Denied?System.Threading.Tasks.Task.FromException(new MoveItObjectAccessException("denied")):System.Threading.Tasks.Task.CompletedTask:throw new NotSupportedException(m.Name));
            var composer=new TraditionalTaskDefinitionComposer(new TraditionalTaskHostProfileCatalog());
            var traditional=new TraditionalTaskPersistenceService(rest,com,composer);
            var crud=new TaskCrudCoordinator(new ITaskCrudStrategy[]{new TraditionalTaskCrudStrategy(traditional),new RestV0TaskCrudStrategy(MoveItTaskKind.Advanced,rest),new RestV0TaskCrudStrategy(MoveItTaskKind.Synchronization,rest)},traditional,rest,com,new TaskJsonPersistenceService(rest,com,access),access);
            Service=new(crud,rest,access,new Snapshot(new UiOptions()));
        }
        private Task Checkout(){Checkouts++;OnCheckout?.Invoke();return System.Threading.Tasks.Task.CompletedTask;}
        private Task Release(){Releases++;return System.Threading.Tasks.Task.CompletedTask;}
        private Task Put(JsonObject value,bool v0){Puts++;UsedV0=v0;Submitted=value.DeepClone().AsObject();if(!DropEdits){Task=value.DeepClone().AsObject();var root=XElement.Parse(Xml);root.Element("Info")?.SetAttributeValue("Description",Task["Info"]?["Description"]?.ToString()??"");Xml=root.ToString(SaveOptions.DisableFormatting);}if(LoseResponse)throw new HttpRequestException("response lost");return System.Threading.Tasks.Task.CompletedTask;}
        private async Task<JsonObject> PutV0(JsonObject value){await Put(value,true);return new(){["id"]="900",["status"]="success",["errorcode"]=0};}
        private MoveItTaskConfiguration Config()=>new("900","Example",Xml);
        private Task<MoveItTaskConfiguration> Modify(Func<string,string> transform){NativeWrites++;Xml=transform(Xml);Task["Schedules"]=new JsonObject{["Schedule"]=new JsonArray(TraditionalTaskScheduleXml.ReadSchedules(Xml).Select(s=>(JsonNode?)s.DeepClone()).ToArray())};return System.Threading.Tasks.Task.FromResult(Config());}
    }
}

internal static class TaskJsonTestDefinitionExtensions
{
    public static JsonObject WithType(this JsonObject value,string type){value["TT"]=type;return value;}
}
