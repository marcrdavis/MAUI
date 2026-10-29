using System.Reflection;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Tasks;
using MoveItAutomationUI.Services.MoveIt.Tasks.AdvancedTasks;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;
using static MoveItAutomationUI.Services.MoveIt.TraditionalTasks.TraditionalTaskDefinitionEditorOperations;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class AdvancedTaskEditorTests
{
    public static JsonObject Capture(int index)
    {
        using var stream=typeof(AdvancedTaskEditorTests).Assembly.GetManifestResourceStream("MoveItAutomationUI.Tests.MoveIt.Fixtures.AdvancedTaskCaptures.json")!;
        return JsonNode.Parse(stream)![index]!.DeepClone().AsObject();
    }
    private static (JsonArray Steps,JsonArray Actions) Patches(JsonObject task)
    {
        var steps=new JsonArray();var actions=new JsonArray();
        foreach(var wrapper in AdvancedTaskDefinition.Flatten(task["steps"]))
        {
            var kind=AdvancedTaskDefinition.Kind(wrapper);var body=wrapper[kind]!.AsObject();var id=GetString(body,"ID");
            if(kind is "Source" or "Process" or "Destination")steps.Add(new JsonObject{["clientKey"]=id,["id"]=id,["kind"]=kind,["patch"]=null});
            if(kind is "Email" or "RunTask")actions.Add(new JsonObject{["clientKey"]=id,["id"]=id,["taskId"]=GetString(body,"TaskID"),["wait"]=GetBool(body,"Wait"),
                ["hostId"]=GetString(body,"HostID"),["addressTo"]=GetString(body,"AddressTo"),["subject"]=GetString(body,"Subject"),["message"]=GetString(body,"Message"),["isHtml"]=GetBool(body,"IsHTML")});
        }
        return(steps,actions);
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void CapturedInsertMoveCloneAndCriteriaRoundTripThroughOneComposer(int index)
    {
        var original=Capture(0);var expected=Capture(index);var proposed=original.DeepClone().AsObject();
        var flow=AdvancedTaskDefinition.Project(expected["steps"]);var patches=Patches(original);
        void NewNodes(JsonArray nodes)
        {
            foreach(var n in nodes.OfType<JsonObject>())
            {
                var id=GetString(n,"id");if(id is "22" or "23" or "24" or "25"){n["id"]="";n["order"]=int.Parse(id);if(id=="23")n["cloneFromId"]="21";}
                if(n["steps"] is JsonArray children)NewNodes(children);
                foreach(var b in(n["when"] as JsonArray ?? []).OfType<JsonObject>())NewNodes(b["steps"]!.AsArray());
                if(n["otherwise"] is JsonObject o)NewNodes(o["steps"]!.AsArray());
            }
        }
        NewNodes(flow);
        AdvancedTaskDefinition.Apply(proposed,flow,patches.Steps,patches.Actions,new TraditionalTaskDefinitionComposer(new TraditionalTaskHostProfileCatalog()));
        AdvancedTaskDefinition.Verify(expected,proposed,original);
        Assert.Equal(expected["NextEID"]!.ToString(),proposed["NextEID"]!.ToString());
        Assert.True(JsonNode.DeepEquals(original,Capture(0)),"The authoritative source fixture is not mutated.");
    }
    [Theory]
    [InlineData("[OrigName]", "single")] [InlineData("[OrigName]", "And")] [InlineData("[OrigName]", "Or")]
    [InlineData("[TaskName]", "single")] [InlineData("[TaskName]", "And")] [InlineData("[TaskName]", "Or")]
    public void CapturedNotMatchesConditionComposesAndVerifiesForFilenameAndCustomMacro(string item, string grouping)
    {
        var original = Capture(0); var proposed = original.DeepClone().AsObject();
        var flow = AdvancedTaskDefinition.Project(original["steps"]); var patches = Patches(original);
        var comparison = new JsonObject { ["a"] = item, ["b"] = "file2.txt", ["test"] = "NOTMASK" };
        var criteria = grouping == "single" ? new JsonObject { ["comp"] = comparison }
            : new JsonObject { [grouping] = new JsonObject { ["comp"] = new JsonArray(
                new JsonObject { ["a"] = "[ErrorCodeFile]", ["b"] = 0, ["test"] = "NEQ" }, comparison) } };
        // Replace the existing condition inside a File Loop, preserving its children/Otherwise.
        flow[1]!["steps"]![0]!["when"]![0]!["criteria"] = criteria;
        AdvancedTaskDefinition.Apply(proposed, flow, patches.Steps, patches.Actions,
            new TraditionalTaskDefinitionComposer(new TraditionalTaskHostProfileCatalog()));
        var saved = proposed["steps"]![1]!["For"]!["steps"]![0]!["If"]!["When"]![0]!["Criteria"]!;
        Assert.True(JsonNode.DeepEquals(criteria, saved));
        AdvancedTaskDefinition.Verify(proposed, proposed.DeepClone().AsObject(), original);
        var incorrect = proposed.DeepClone().AsObject();
        var changed = incorrect["steps"]![1]!["For"]!["steps"]![0]!["If"]!["When"]![0]!["Criteria"]!;
        (grouping == "single" ? changed["comp"]! : changed[grouping]!["comp"]![1]!)["test"] = "MASK";
        Assert.Throws<AdvancedTaskReadbackException>(() => AdvancedTaskDefinition.Verify(proposed, incorrect, original));
        Assert.True(JsonNode.DeepEquals(original, Capture(0)));
    }

    [Fact]
    public void ReadbackAcceptsLineEndingsFlagsAndUnchangedNativeFields()
    {
        var original = Capture(0);
        original["steps"]![0]!["Source"]!["NativeRuntimeLabel"] = "before";
        var proposed = original.DeepClone().AsObject();
        proposed["steps"]![2]!["Comment"]!["Text"] = "First line\nSecond line";
        proposed["steps"]![0]!["Source"]!["NewFilesOnly"] = true;
        var saved = proposed.DeepClone().AsObject();
        saved["steps"]![2]!["Comment"]!["Text"] = "First line\r\nSecond line";
        saved["steps"]![0]!["Source"]!["NewFilesOnly"] = 1;
        saved["steps"]![0]!["Source"]!["NativeRuntimeLabel"] = "after";
        saved["steps"]![0]!["Source"]!["InvalidOverride"] = 1;
        foreach (var step in AdvancedTaskDefinition.Flatten(saved["steps"])) step[AdvancedTaskDefinition.Kind(step)]!.AsObject().Remove("Disabled");
        AdvancedTaskDefinition.Verify(proposed, saved, original);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("path")]
    [InlineData("disabled")]
    [InlineData("criteria")]
    [InlineData("order")]
    [InlineData("missing")]
    [InlineData("otherwise")]
    public void ReadbackStillRejectsLostEditsAndChangedTopology(string mismatch)
    {
        var original = Capture(0);
        var proposed = original.DeepClone().AsObject();
        proposed["steps"]![2]!["Comment"]!["Text"] = "Submitted comment";
        proposed["steps"]![0]!["Source"]!["Path"] = "/submitted";
        proposed["steps"]![0]!["Source"]!["Disabled"] = 1;
        var saved = proposed.DeepClone().AsObject();
        var firstIf = saved["steps"]![1]!["For"]!["steps"]![0]!["If"]!.AsObject();
        switch (mismatch)
        {
            case "text": saved["steps"]![2]!["Comment"]!["Text"] = "Not saved"; break;
            case "path": saved["steps"]![0]!["Source"]!["Path"] = "/old"; break;
            case "disabled": saved["steps"]![0]!["Source"]!.AsObject().Remove("Disabled"); break;
            case "criteria": firstIf["When"]![0]!["Criteria"]!["And"]!["comp"]![0]!["b"] = "wrong mask"; break;
            case "order": var items=saved["steps"]!.AsArray();var item=items[0]!.DeepClone();items.RemoveAt(0);items.Add(item);break;
            case "missing": saved["steps"]!.AsArray().RemoveAt(0); break;
            case "otherwise": firstIf["Otherwise"] = new JsonObject { ["steps"] = new JsonArray() }; break;
        }
        var exception = Assert.Throws<AdvancedTaskReadbackException>(() => AdvancedTaskDefinition.Verify(proposed, saved, original));
        Assert.Contains("steps", exception.Message);
        Assert.DoesNotContain("Submitted comment", exception.Message);
        Assert.DoesNotContain("/submitted", exception.Message);
    }

    [Fact]
    public void ReadbackDoesNotCoerceCommentTextIntoBooleanFlags()
    {
        var original = Capture(0);
        var proposed = original.DeepClone().AsObject();
        proposed["steps"]![2]!["Comment"]!["Text"] = "true";
        var saved = proposed.DeepClone().AsObject();
        saved["steps"]![2]!["Comment"]!["Text"] = "1";
        Assert.Throws<AdvancedTaskReadbackException>(() => AdvancedTaskDefinition.Verify(proposed, saved, original));
    }

    [Fact]
    public void ReadbackUsesCommonParameterShapesWithoutLosingValuesOrRemovals()
    {
        var original = Capture(0);
        var proposed = original.DeepClone().AsObject();
        var run = AdvancedTaskDefinition.Flatten(proposed["steps"]).First(w => AdvancedTaskDefinition.Kind(w) == "RunTask")["RunTask"]!.AsObject();
        run["Parameters"] = JsonNode.Parse("""{"Parameter":[{"Name":"Input","value":"new value","secure":false}]}""");
        var saved = proposed.DeepClone().AsObject();
        var persisted = AdvancedTaskDefinition.Flatten(saved["steps"]).First(w => AdvancedTaskDefinition.Kind(w) == "RunTask")["RunTask"]!.AsObject();
        persisted["Parameters"] = JsonNode.Parse("""{"any":[{"name":"Input","displayValue":"new value","secure":false}]}""");
        AdvancedTaskDefinition.Verify(proposed, saved, original);
        persisted["Parameters"]!["any"]![0]!["displayValue"] = "old value";
        Assert.Throws<AdvancedTaskReadbackException>(() => AdvancedTaskDefinition.Verify(proposed, saved, original));
        run["Parameters"] = new JsonObject { ["Parameter"] = new JsonArray() };
        Assert.Throws<AdvancedTaskReadbackException>(() => AdvancedTaskDefinition.Verify(proposed, saved, original));
    }

    [Fact]
    public void NestedSourcePatchUsesExistingHostComposerAndPreservesUnownedFields()
    {
        var task=Capture(0);var source=task["steps"]![0]!["Source"]!.AsObject();source["NativeOnly"]="preserved";source["Password"]="encrypted-value";
        var patches=Patches(task);patches.Steps[0]!["patch"]=new JsonObject{["Type"]="Share",["HostID"]=source["HostID"]!.DeepClone(),["Path"]="/changed",["FileMask"]="*.*"};
        // Native-only and suppressed values are not client-owned fields.
        patches.Steps[0]!["patch"]!.AsObject().Remove("NativeOnly");patches.Steps[0]!["patch"]!.AsObject().Remove("Password");
        AdvancedTaskDefinition.Apply(task,AdvancedTaskDefinition.Project(task["steps"]),patches.Steps,patches.Actions,new TraditionalTaskDefinitionComposer(new TraditionalTaskHostProfileCatalog()));
        Assert.Equal("/changed",task["steps"]![0]!["Source"]!["Path"]!.ToString());
        Assert.Equal("preserved",task["steps"]![0]!["Source"]!["NativeOnly"]!.ToString());
        Assert.Equal("encrypted-value",task["steps"]![0]!["Source"]!["Password"]!.ToString());
    }

    private static JsonObject CapturedHostPatch(string kind)
    {
        var wrapper = AdvancedTaskDefinition.Flatten(Capture(0)["steps"])
            .First(step => AdvancedTaskDefinition.Kind(step) == kind);
        var body = wrapper[kind]!.AsObject();
        var patch = new JsonObject();
        foreach (var field in body)
            if (field.Value is not null && TraditionalTaskHostCapabilities.IsPatchFieldAllowed(kind, "Share", field.Key))
                patch[field.Key] = field.Value.DeepClone();
        return patch;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BlankFilespecUsesSharedValidationForExistingAndNewAdvancedSources(bool isNew)
    {
        var task = Capture(0);
        var original = task.DeepClone();
        var flow = AdvancedTaskDefinition.Project(task["steps"]);
        var patches = Patches(task);
        var patch = CapturedHostPatch("Source");
        patch["FileMask"] = "";
        patches.Steps[0]!["patch"] = patch;
        if (isNew) { flow[0]!["id"] = ""; patches.Steps[0]!["id"] = ""; }
        var error = Assert.Throws<InvalidOperationException>(() => AdvancedTaskDefinition.Apply(task, flow, patches.Steps, patches.Actions,
            new TraditionalTaskDefinitionComposer(new TraditionalTaskHostProfileCatalog())));
        Assert.Contains("Source File(s) is required", error.Message);
        Assert.True(JsonNode.DeepEquals(original, task));
    }

    [Theory]
    [InlineData("Source", false)]
    [InlineData("Destination", false)]
    [InlineData("Source", true)]
    [InlineData("Destination", true)]
    public void NewHostStepsUseCapturedFieldsAtRootAndInsideLoops(string kind, bool nested)
    {
        var task = Capture(0);
        task["steps"] = new JsonArray();
        task["NextEID"] = 11;
        var host = new JsonObject { ["key"] = "new-host", ["id"] = "", ["kind"] = kind, ["disabled"] = true, ["order"] = 2 };
        var flow = nested
            ? new JsonArray(new JsonObject { ["key"] = "new-loop", ["id"] = "", ["kind"] = "For", ["order"] = 1, ["steps"] = new JsonArray(host) })
            : new JsonArray(host);
        var patch = CapturedHostPatch(kind);
        patch.Remove("ID");
        var patches = new JsonArray(new JsonObject { ["clientKey"] = "new-host", ["id"] = "", ["kind"] = kind, ["patch"] = patch });
        var requestedFlow = flow.DeepClone();
        var requestedPatches = patches.DeepClone();

        AdvancedTaskDefinition.Apply(task, flow, patches, null, new TraditionalTaskDefinitionComposer(new TraditionalTaskHostProfileCatalog()));

        var saved = AdvancedTaskDefinition.Flatten(task["steps"])
            .First(step => AdvancedTaskDefinition.Kind(step) == kind)[kind]!.AsObject();
        Assert.Equal(nested ? "12" : "11", saved["ID"]!.ToString());
        Assert.Equal(nested ? "13" : "12", task["NextEID"]!.ToString());
        Assert.Equal("1", saved["Disabled"]!.ToString());
        foreach (var field in patch)
            Assert.True(JsonNode.DeepEquals(field.Value, saved[field.Key]), $"Captured {kind}.{field.Key} must survive new-step composition.");
        Assert.True(JsonNode.DeepEquals(requestedFlow, flow));
        Assert.True(JsonNode.DeepEquals(requestedPatches, patches));
    }

    [Theory]
    [InlineData("Source")]
    [InlineData("Destination")]
    public void NewHostStepsRequireExplicitOverrideCredentials(string kind)
    {
        var task = JsonNode.Parse("""{"ID":"1","TT":"COS","NextEID":1,"steps":[]}""")!.AsObject();
        var flow = new JsonArray(new JsonObject { ["key"] = "new-host", ["id"] = "", ["kind"] = kind });
        var patch = new JsonObject { ["Type"] = "FTP", ["HostID"] = "2", ["Path"] = "/files", ["UseDefUser"] = 0, ["Username"] = "user" };
        if (kind == "Source") patch["FileMask"] = "*.*";
        var patches = new JsonArray(new JsonObject { ["clientKey"] = "new-host", ["id"] = "", ["kind"] = kind, ["patch"] = patch });
        var composer = new TraditionalTaskDefinitionComposer(new TraditionalTaskHostProfileCatalog());
        var error = Assert.Throws<InvalidOperationException>(() => AdvancedTaskDefinition.Apply(task, flow, patches, null, composer));
        Assert.Contains("Password", error.Message);
        Assert.Empty(task["steps"]!.AsArray());
        Assert.Equal("1", task["NextEID"]!.ToString());
        patch["Password"] = "explicit-new-password";
        AdvancedTaskDefinition.Apply(task, flow, patches, null, composer);
        Assert.Equal("explicit-new-password", task["steps"]![0]![kind]!["Password"]!.ToString());
        Assert.Equal("1", task["steps"]![0]![kind]!["UseDefConnTimeoutSecs"]!.ToString());
    }

    [Fact]
    public async Task NewSourceAndNestedDestinationSaveThroughV0ThenRemainEditable()
    {
        var task = Capture(0);
        var flow = AdvancedTaskDefinition.Project(task["steps"]);
        var source = flow[0]!.AsObject();
        var destination = flow[1]!["steps"]![0]!["when"]![0]!["steps"]![0]!.AsObject();
        source["id"] = ""; source["order"] = 1;
        destination["id"] = ""; destination["order"] = 2;
        var patches = Patches(task);
        foreach (var item in patches.Steps.OfType<JsonObject>().Where(item => GetString(item, "kind") is "Source" or "Destination"))
        {
            item["id"] = "";
            item["patch"] = CapturedHostPatch(GetString(item, "kind"));
        }
        var events = new List<string>();
        var rest = DispatchProxy.Create<IMoveItRestClient, Proxy>();
        ((Proxy)(object)rest).InvokeCall = (method, args) =>
        {
            events.Add(method.Name);
            if (method.Name == nameof(IMoveItRestClient.GetTaskAsync)) return Task.FromResult(task.DeepClone().AsObject());
            if (method.Name is nameof(IMoveItRestClient.CheckoutTaskV0Async) or nameof(IMoveItRestClient.ReleaseTaskCheckoutV0Async)) return Task.CompletedTask;
            if (method.Name == nameof(IMoveItRestClient.UpdateTaskV0Async))
            {
                task = ((JsonObject)args![1]!).DeepClone().AsObject();
                return Task.FromResult(new JsonObject { ["id"] = task["ID"]!.DeepClone(), ["status"] = "success", ["errorcode"] = 0, ["refreshRequired"] = false });
            }
            throw new NotSupportedException(method.Name);
        };
        var persistence = new TaskJsonPersistenceService(rest);
        var composer = new TraditionalTaskDefinitionComposer(new TraditionalTaskHostProfileCatalog());
        await persistence.UpdateFreshAsync(task["ID"]!.ToString(), MoveItTaskKind.Advanced, draft =>
        {
            AdvancedTaskDefinition.Apply(draft, flow, patches.Steps, patches.Actions, composer);
            return draft;
        }, requireReadback: true);
        Assert.Equal("22", task["steps"]![0]!["Source"]!["ID"]!.ToString());
        Assert.Equal("23", task["steps"]![1]!["For"]!["steps"]![0]!["If"]!["When"]![0]!["steps"]![0]!["Destination"]!["ID"]!.ToString());
        Assert.Equal("24", task["NextEID"]!.ToString());

        flow = AdvancedTaskDefinition.Project(task["steps"]);
        patches = Patches(task);
        foreach (var item in patches.Steps.OfType<JsonObject>().Where(item => GetString(item, "kind") is "Source" or "Destination"))
            item["patch"] = new JsonObject { ["Type"] = "Share", ["HostID"] = CapturedHostPatch(GetString(item, "kind"))["HostID"]!.DeepClone(), ["Path"] = "/edited-after-save" };
        await persistence.UpdateFreshAsync(task["ID"]!.ToString(), MoveItTaskKind.Advanced, draft =>
        {
            AdvancedTaskDefinition.Apply(draft, flow, patches.Steps, patches.Actions, composer);
            return draft;
        }, requireReadback: true);
        foreach (var wrapper in AdvancedTaskDefinition.Flatten(task["steps"]).Where(step => AdvancedTaskDefinition.Kind(step) is "Source" or "Destination"))
            Assert.Equal("/edited-after-save", wrapper[AdvancedTaskDefinition.Kind(wrapper)]!["Path"]!.ToString());
        Assert.Equal("24", task["NextEID"]!.ToString());
        var lifecycle = new[] { nameof(IMoveItRestClient.CheckoutTaskV0Async), nameof(IMoveItRestClient.GetTaskAsync), nameof(IMoveItRestClient.UpdateTaskV0Async), nameof(IMoveItRestClient.GetTaskAsync), nameof(IMoveItRestClient.ReleaseTaskCheckoutV0Async) };
        Assert.Equal(lifecycle.Concat(lifecycle), events);
    }
    [Fact]
    public void DuplicateIdsInvalidCriteriaAndMissingTemplatesRejectBeforePersistence()
    {
        var task=Capture(0);var patches=Patches(task);var flow=AdvancedTaskDefinition.Project(task["steps"]);
        flow.Add(flow[0]!.DeepClone());
        Assert.Throws<InvalidOperationException>(()=>AdvancedTaskDefinition.Apply(task,flow,patches.Steps,patches.Actions,new TraditionalTaskDefinitionComposer(new TraditionalTaskHostProfileCatalog())));
        Assert.Throws<InvalidOperationException>(()=>AdvancedTaskDefinition.ValidateCriteria(JsonNode.Parse("""{"And":{"comp":[]}}""")!.AsObject()));
        task=Capture(0);flow=AdvancedTaskDefinition.Project(task["steps"]);flow[0]!["id"]="unknown";
        Assert.Throws<InvalidOperationException>(()=>AdvancedTaskDefinition.Apply(task,flow,patches.Steps,patches.Actions,new TraditionalTaskDefinitionComposer(new TraditionalTaskHostProfileCatalog())));
    }
    [Fact]
    public void RenameWaitAndBranchBookkeepingUseCapturedValues()
    {
        var task=Capture(0);var patches=Patches(task);var flow=AdvancedTaskDefinition.Project(task["steps"]);
        patches.Actions[0]!["wait"]=false;
        var loop=flow[1]!.AsObject();loop["steps"]![4]!["fields"]!["RenameTo"]="[OrigName].new";loop["steps"]![4]!["fields"]!["DelRename"]=0;
        AdvancedTaskDefinition.Apply(task,flow,patches.Steps,patches.Actions,new TraditionalTaskDefinitionComposer(new TraditionalTaskHostProfileCatalog()));
        var steps=task["steps"]![1]!["For"]!["steps"]!;
        Assert.Equal("0",steps[3]!["RunTask"]!["Wait"]!.ToString());
        Assert.Equal("[OrigName].new",steps[4]!["UpdOrig"]!["RenameTo"]!.ToString());
        Assert.Equal("13",steps[0]!["If"]!["When"]![0]!["__parentStepId"]!.ToString());
        Assert.Equal("0",steps[0]!["If"]!["When"]![0]!["__branchIndex"]!.ToString());
    }
    [Fact]
    public void ClonedHostStepsReuseSharedNewCredentialValidation()
    {
        var task=JsonNode.Parse("""{"ID":"1","TT":"COS","NextEID":12,"steps":[{"Source":{"ID":"11","HostID":"2","Type":"FTP","Path":"/in","FileMask":"*.*","UseDefUser":0,"Username":"user","Password":null}}]}""")!.AsObject();
        var flow=AdvancedTaskDefinition.Project(task["steps"]);var copied=flow[0]!.DeepClone().AsObject();copied["key"]="clone";copied["id"]="";copied["cloneFromId"]="11";copied["order"]=1;flow.Add(copied);
        var patches=Patches(task);var patch=JsonNode.Parse("""{"Type":"FTP","HostID":"2","Path":"/in","FileMask":"*.*","UseDefUser":0,"Username":"user"}""")!.AsObject();
        patches.Steps.Add(new JsonObject{["clientKey"]="clone",["id"]="",["kind"]="Source",["patch"]=patch});
        var composer=new TraditionalTaskDefinitionComposer(new TraditionalTaskHostProfileCatalog());
        Assert.Throws<InvalidOperationException>(()=>AdvancedTaskDefinition.Apply(task,flow,patches.Steps,patches.Actions,composer));
        patch["Password"]="explicit-clone-password";
        AdvancedTaskDefinition.Apply(task,flow,patches.Steps,patches.Actions,composer);
        Assert.Equal("explicit-clone-password",task["steps"]![1]!["Source"]!["Password"]!.ToString());
        Assert.Equal("12",task["steps"]![1]!["Source"]!["ID"]!.ToString());
    }
    [Fact]
    public async Task AdvancedSaveAcquiresAndReleasesTheSharedCheckoutAndVerifiesV0Readback()
    {
        var task=Capture(0);var events=new List<string>();var rest=DispatchProxy.Create<IMoveItRestClient,Proxy>();
        ((Proxy)(object)rest).InvokeCall=(method,args)=>{
            events.Add(method.Name);
            if(method.Name==nameof(IMoveItRestClient.GetTaskAsync))return Task.FromResult(task.DeepClone().AsObject());
            if(method.Name is nameof(IMoveItRestClient.CheckoutTaskV0Async) or nameof(IMoveItRestClient.ReleaseTaskCheckoutV0Async))return Task.CompletedTask;
            if(method.Name==nameof(IMoveItRestClient.UpdateTaskV0Async)){task=((JsonObject)args![1]!).DeepClone().AsObject();return Task.FromResult(new JsonObject{["id"]=task["ID"]!.DeepClone(),["status"]="success",["errorcode"]=0});}
            throw new NotSupportedException(method.Name);
        };
        await new TaskJsonPersistenceService(rest).UpdateFreshAsync(task["ID"]!.ToString(),MoveItTaskKind.Advanced,draft=>{draft["Name"]="Edited Advanced";return draft;},requireReadback:true);
        Assert.Equal(new[]{nameof(IMoveItRestClient.CheckoutTaskV0Async),nameof(IMoveItRestClient.GetTaskAsync),nameof(IMoveItRestClient.UpdateTaskV0Async),nameof(IMoveItRestClient.GetTaskAsync),nameof(IMoveItRestClient.ReleaseTaskCheckoutV0Async)},events);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedSaveAcceptsNormalizedReadbackAndReportsRealMismatchWithoutRetry(bool loseEdit)
    {
        var task = Capture(0);
        var writes = 0;
        var releases = 0;
        var rest = DispatchProxy.Create<IMoveItRestClient, Proxy>();
        ((Proxy)(object)rest).InvokeCall = (method, args) =>
        {
            if (method.Name == nameof(IMoveItRestClient.CheckoutTaskV0Async)) return Task.CompletedTask;
            if (method.Name == nameof(IMoveItRestClient.ReleaseTaskCheckoutV0Async)) { releases++; return Task.CompletedTask; }
            if (method.Name == nameof(IMoveItRestClient.GetTaskAsync)) return Task.FromResult(task.DeepClone().AsObject());
            if (method.Name == nameof(IMoveItRestClient.UpdateTaskV0Async))
            {
                writes++;
                task = ((JsonObject)args![1]!).DeepClone().AsObject();
                task["steps"]![2]!["Comment"]!["Text"] = loseEdit ? "old text" : "First line\r\nSecond line";
                return Task.FromResult(new JsonObject { ["id"] = task["ID"]!.DeepClone(), ["status"] = "success", ["errorcode"] = 0 });
            }
            throw new NotSupportedException(method.Name);
        };
        Task Save() => new TaskJsonPersistenceService(rest).UpdateFreshAsync(task["ID"]!.ToString(), MoveItTaskKind.Advanced,
            draft => { draft["steps"]![2]!["Comment"]!["Text"] = "First line\nSecond line"; return draft; }, requireReadback: true);
        if (loseEdit)
        {
            var failure = await Assert.ThrowsAsync<TraditionalTaskPartialSaveException>(Save);
            Assert.Contains("Advanced flow verification", failure.Message);
            Assert.Contains("Comment.Text", failure.Message);
            Assert.DoesNotContain("First line", failure.Message);
        }
        else await Save();
        Assert.Equal(1, writes);
        Assert.Equal(1, releases);
    }

    public class Proxy:DispatchProxy
    {
        public Func<MethodInfo,object?[]?,object?> InvokeCall {get;set;}=null!;
        protected override object? Invoke(MethodInfo? targetMethod,object?[]? args)=>InvokeCall(targetMethod!,args);
    }
}
