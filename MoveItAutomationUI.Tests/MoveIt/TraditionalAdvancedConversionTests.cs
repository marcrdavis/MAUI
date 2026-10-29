using System.Reflection;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Tasks;
using MoveItAutomationUI.Services.MoveIt.Tasks.AdvancedTasks;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class TraditionalAdvancedConversionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConvertingAnOpenCloneDoesNotReleaseOrInvalidateItsSourceLease(bool failReadback)
    {
        var source = Capture("Traditional"); source["ID"] = "384673020"; source["Name"] = "Original Clone";
        using var fixture = new Fixture(source) { Failure = failReadback ? "readback" : null };
        var acquires = 0; var releases = 0; var revisions = 0;
        var transport = Stub<IObjectEditLeaseTransport>((method, _) =>
        {
            if (method.Name == nameof(IObjectEditLeaseTransport.AcquireAsync)) { acquires++; return Task.CompletedTask; }
            if (method.Name == nameof(IObjectEditLeaseTransport.ReleaseAsync)) { releases++; return Task.CompletedTask; }
            revisions++; return Task.FromResult("unchanged source");
        });
        var records = new Dictionary<string, ObjectEditLeaseRecord>();
        var journal = Stub<IObjectEditLeaseJournal>((method, args) =>
        {
            if (method.Name == nameof(IObjectEditLeaseJournal.WriteAsync)) { var record = (ObjectEditLeaseRecord)args[0]!; records[record.LeaseId] = record; return Task.CompletedTask; }
            if (method.Name == nameof(IObjectEditLeaseJournal.Delete)) records.Remove((string)args[0]!);
            return null;
        });
        var owner = new ObjectEditLeaseOwner("conversion-login", new() { WebAdminUrl = "https://" + Guid.NewGuid().ToString("N"),
            Username = "miadmin", LastUserActivityUtc = DateTimeOffset.UtcNow });
        using var server = ObjectCheckout.ForServer(owner.Session.WebAdminUrl);
        using var service = new ObjectEditLeaseService(transport, journal, new LeaseOptions(),
            ConfigurationStartupState.Capture(new ConfigurationBuilder().Build()), TimeProvider.System, NullLogger<ObjectEditLeaseService>.Instance);
        var lease = await service.OpenAsync("Task", "384673020", owner, default);
        using (var operation = await service.EnterAsync("Task", "384673020", lease.LeaseId, owner, default))
        using (ObjectCheckout.ForLifetime(ObjectEditLeaseService.CheckoutLifetime(operation)))
        {
            if (failReadback)
            {
                var error = await Assert.ThrowsAsync<TaskAdvancedConversionOutcomeException>(() => fixture.Crud.CreateAdvancedFromTraditionalAsync("384673020"));
                Assert.Equal("900", error.TaskId);
            }
            else Assert.Equal("900", (await fixture.Crud.CreateAdvancedFromTraditionalAsync("384673020")).Id);
            Assert.Equal(1, acquires); Assert.Equal(0, releases); Assert.Equal(0, revisions);
            Assert.True(records[lease.LeaseId].Held); Assert.False(records[lease.LeaseId].PendingRequest);
        }
        Assert.Equal(0, fixture.Releases); Assert.Equal(1, fixture.Creates);
        await service.HeartbeatAsync(lease.LeaseId, owner, default);
        // An uncertain result for the NEW object must not block a later save of the source clone.
        using (var operation = await service.EnterAsync("Task", "384673020", lease.LeaseId, owner, default))
        using (ObjectCheckout.ForLifetime(ObjectEditLeaseService.CheckoutLifetime(operation)))
            Assert.True(await ObjectCheckout.ExecuteAsync("Task", "384673020", _ => throw new Exception("Borrow the source checkout"),
                _ => Task.FromResult(true), _ => throw new Exception("Lease owns cleanup"), default));
        await service.CloseAsync(lease.LeaseId, owner, default);
        Assert.Equal(2, acquires); Assert.Equal(2, releases); Assert.Empty(records);
    }

    private sealed class LeaseOptions : IOptionsMonitor<UiOptions>
    {
        public UiOptions CurrentValue { get; } = new() { CheckoutObjectsForDurationOfEdit = new() { Value = true } };
        public UiOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<UiOptions, string?> listener) => null;
    }

    private static JsonObject Capture(string key)
    {
        using var stream = typeof(TraditionalAdvancedConversionTests).Assembly.GetManifestResourceStream("MoveItAutomationUI.Tests.MoveIt.Fixtures.TraditionalToAdvancedCapture.json")!;
        return JsonNode.Parse(stream)![key]!.DeepClone().AsObject();
    }

    [Fact]
    public void SuppliedTraditionalPayloadConvertsToSuppliedAdvancedPayload()
    {
        var source = Capture("Traditional"); var before = source.DeepClone(); var expected = Capture("Advanced");
        var plan = TraditionalTaskAdvancedConversion.Build(source, expected["Name"]!.ToString());
        NormalizeBranchMetadata(expected);
        Assert.True(JsonNode.DeepEquals(expected, plan.Definition), plan.Definition.ToJsonString());
        Assert.True(JsonNode.DeepEquals(before, source));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void EveryTaskOutcomeCombinationRunsItsActionExactlyOnce(int flags)
    {
        var source = Capture("Traditional");
        source["NextActions"] = new JsonObject { ["NextAction"] = new JsonArray(new JsonObject
        {
            ["Type"] = "Task", ["TaskID"] = "77", ["DoAfter"] = "Task",
            ["DoIfSuccess"] = flags & 1, ["DoIfFailure"] = (flags >> 1) & 1, ["DoIfNoAction"] = (flags >> 2) & 1,
            ["Parameters"] = new JsonObject { ["Parameter"] = new JsonArray(new JsonObject { ["Name"] = "Input", ["value"] = "[OrigName]", ["Secure"] = 0 }) }
        }) };
        var task = TraditionalTaskAdvancedConversion.Build(source, "Copy Advanced").Definition;
        var conditions = AdvancedTaskDefinition.Flatten(task["steps"]).Where(step => step["If"] is JsonObject).ToList();
        if (flags is 0 or 7) Assert.Empty(conditions);
        else Assert.Collection(conditions, item => Assert.NotNull(item["If"]));
        var actions = AdvancedTaskDefinition.Flatten(task["steps"]).Where(step => step["RunTask"] is JsonObject).ToList();
        Assert.Equal(flags == 7 ? 1 : System.Numerics.BitOperations.PopCount((uint)flags), actions.Count);
        Assert.All(actions, action =>
        {
            var body = action["RunTask"]!.AsObject();
            Assert.Null(body["DoAfter"]); Assert.Null(body["Type"]); Assert.Null(body["DoIfSuccess"]);
            Assert.Equal("0", body["Wait"]!.ToString());
            Assert.Equal("[OrigName]", body["Parameters"]!["Parameter"]![0]!["value"]!.ToString());
        });
        foreach (var outcome in new[] { "Success", "Failure", "No xfers" }.Select((name, index) => (name, index)))
        {
            var matching = conditions.SelectMany(step => step["If"]!["When"]!.AsArray().OfType<JsonObject>())
                .Where(branch => branch["Criteria"]!["comp"]!["b"]!.ToString() == outcome.name).ToList();
            if (flags != 7 && (flags & (1 << outcome.index)) != 0) Assert.Collection(matching, branch => Assert.NotEmpty(branch["steps"]!.AsArray()));
            else Assert.Empty(matching);
        }
    }

    [Fact]
    public void MultipleStepsOnceProcessesSchedulesAndOpaqueValuesKeepTheirSemantics()
    {
        var source = Capture("Traditional");
        source["Schedules"]!["Schedule"]!.AsArray().Add(source["Schedules"]!["Schedule"]![0]!.DeepClone());
        source["Schedules"]!["Schedule"]![1]!["ID"] = "12";
        source["steps"] = JsonNode.Parse("""
        [
          {"Destination":{"ID":"15","Type":"Share","HostID":"5","Path":"/out1"}},
          {"Source":{"ID":"11","Type":"Share","HostID":"1","Path":"/in1","NativeOnly":"keep"}},
          {"Process":{"ID":"19","ScriptID":"once","Run":"Once","IsDest":0}},
          {"Destination":{"ID":"20","Type":"Share","HostID":"6","Path":"/out2"}},
          {"Process":{"ID":"21","ScriptID":"per-file","Run":"PerFile","IsDest":0}},
          {"Source":{"ID":"22","Type":"Share","HostID":"2","Path":"/in2"}},
          {"Process":{"ID":"23","ScriptID":"process-destination","Run":"Once","IsDest":1}}
        ]
        """);
        var result = TraditionalTaskAdvancedConversion.Build(source, "Multiple Advanced").Definition;
        var steps = result["steps"]!.AsArray();
        Assert.Equal(new[] { "Source", "Source", "For", "Process", "If", "RunTask" }, steps.OfType<JsonObject>().Select(AdvancedTaskDefinition.Kind));
        Assert.Equal("keep", steps[0]!["Source"]!["NativeOnly"]!.ToString());
        Assert.Equal(new[] { "Process", "Process", "Destination", "Destination" }, steps[2]!["For"]!["steps"]!.AsArray().OfType<JsonObject>().Select(AdvancedTaskDefinition.Kind));
        Assert.Equal("per-file", steps[2]!["For"]!["steps"]![0]!["Process"]!["ScriptID"]!.ToString());
        Assert.Equal("process-destination", steps[2]!["For"]!["steps"]![1]!["Process"]!["ScriptID"]!.ToString());
        Assert.Equal("once", steps[3]!["Process"]!["ScriptID"]!.ToString());
        Assert.True(JsonNode.DeepEquals(source["Schedules"], result["Schedules"]));
        Assert.True(JsonNode.DeepEquals(source["Parameters"], result["Parameters"]));
        var ids = AdvancedTaskDefinition.Flatten(result["steps"]).Select(step => step[AdvancedTaskDefinition.Kind(step)]!["ID"]!.ToString()).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count()); Assert.DoesNotContain("12", ids);
        Assert.True(long.Parse(result["NextEID"]!.ToString()) > ids.Max(long.Parse));
    }

    [Theory]
    [InlineData("Sy")] [InlineData("COS")]
    public void NonTraditionalTasksRejectBeforeConversion(string type)
    {
        var source = Capture("Traditional"); source["TT"] = type;
        Assert.Throws<InvalidOperationException>(() => TraditionalTaskAdvancedConversion.Build(source, "Wrong Advanced"));
    }

    [Fact]
    public async Task SharedCrudUsesCheckoutUniqueNamesNativeCreationAndReadbackWithoutModifyingTheSource()
    {
        var source = Capture("Traditional"); source["steps"]![0]!["Source"]!["UseDefUser"] = 0;
        source["steps"]![0]!["Source"]!["Username"] = "user";
        using var fixture = new Fixture(source) { DuplicateNames = 2, ReencryptSecrets = true };
        var created = await fixture.Crud.CreateAdvancedFromTraditionalAsync(source["ID"]!.ToString());
        Assert.Equal("900", created.Id); Assert.EndsWith(" Advanced (3)", created.Name); Assert.Equal("Advanced", created.TaskType);
        Assert.Equal(1, fixture.Creates); Assert.Equal(1, fixture.Releases);
        Assert.Equal(new[] { "checkout", "source", "name", "name", "name", "native-source", "create", "readback", "release" }, fixture.Events);
        var xml = XElement.Parse(fixture.CreatedXml!);
        Assert.Equal("0", (string?)xml.Attribute("Active")); Assert.Equal("COS", (string?)xml.Attribute("TT"));
        Assert.Equal("stored-password", (string?)xml.Element("Source")!.Attribute("Password"));
        Assert.Equal("stored-parameter", xml.Element("Parameters")!.Element("Parameter")!.Value);
        Assert.Null(xml.Element("NextActions")); Assert.Equal("", source["TT"]!.ToString());
    }

    [Fact]
    public async Task LongNamesRemainWithinTheNativeLimitAndKeepTheAdvancedSuffix()
    {
        var source = Capture("Traditional"); source["Name"] = new string('x', 254);
        using var fixture = new Fixture(source);
        var created = await fixture.Crud.CreateAdvancedFromTraditionalAsync(source["ID"]!.ToString());
        Assert.Equal(254, created.Name.Length); Assert.EndsWith(" Advanced", created.Name);
    }

    [Theory]
    [InlineData("readback")] [InlineData("enabled")] [InlineData("secret")] [InlineData("release")]
    public async Task PostCreateFailuresKeepTheKnownIdAndNeverReplayOrDelete(string failure)
    {
        var source = Capture("Traditional"); source["steps"]![0]!["Source"]!["UseDefUser"] = 0;
        using var fixture = new Fixture(source) { Failure = failure };
        var error = await Assert.ThrowsAsync<TaskAdvancedConversionOutcomeException>(() => fixture.Crud.CreateAdvancedFromTraditionalAsync("498499684"));
        Assert.Equal("900", error.TaskId); Assert.Equal(1, fixture.Creates); Assert.Equal(1, fixture.Releases);
        Assert.DoesNotContain("delete", fixture.Events);
    }

    private static void NormalizeBranchMetadata(JsonObject task)
    {
        foreach (var wrapper in AdvancedTaskDefinition.Flatten(task["steps"]))
            if (wrapper["If"] is JsonObject condition)
                foreach (var branch in condition["When"]!.AsArray().Select((node, index) => (node, index)))
                {
                    branch.node!["__parentStepId"] = condition["ID"]!.ToString();
                    branch.node!["__branchIndex"] = branch.index;
                }
    }

    [Fact]
    public async Task SuppliedEachFileCaptureMatchesThePlanAndSharedNativeCreation()
    {
        var source = Capture("Traditional"); var before = source.DeepClone();
        // The new capture moves the Failure Email from Task completion to Each File.
        source["NextActions"]!["NextAction"]![1]!["DoAfter"] = "FileDone";
        var expected = Capture("EachFileAdvanced"); expected["ID"] = ""; // Capture is an already saved Task.
        var plan = TraditionalTaskAdvancedConversion.Build(source, expected["Name"]!.ToString());
        Assert.True(JsonNode.DeepEquals(expected, plan.Definition), plan.Definition.ToJsonString());
        before!["NextActions"]!["NextAction"]![1]!["DoAfter"] = "FileDone";
        Assert.True(JsonNode.DeepEquals(before, source));
        using var fixture = new Fixture(source) { ReencryptSecrets = true };
        var created = await fixture.Crud.CreateAdvancedFromTraditionalAsync(source["ID"]!.ToString());
        Assert.Equal("900", created.Id); Assert.Equal(1, fixture.Creates); Assert.Equal(1, fixture.Releases);
        var xml = XElement.Parse(fixture.CreatedXml!);
        var loop = xml.Element("For")!;
        Assert.Equal(new[] { "Process", "Destination", "If" }, loop.Elements().Select(element => element.Name.LocalName));
        var fileCondition = loop.Element("If")!.Element("When")!;
        Assert.Equal("[ErrorCodeFile]", (string?)fileCondition.Element("Criteria")!.Element("comp")!.Attribute("a"));
        Assert.Equal("0", (string?)fileCondition.Element("Criteria")!.Element("comp")!.Attribute("b"));
        Assert.Equal("NGT", (string?)fileCondition.Element("Criteria")!.Element("comp")!.Attribute("test"));
        Assert.Equal("Failure", (string?)fileCondition.Element("Email")!.Attribute("Subject"));
        Assert.Equal(new[] { "Success", "No xfers", "Failure" }, xml.Element("If")!.Elements("When")
            .Select(branch => (string?)branch.Element("Criteria")!.Element("comp")!.Attribute("b")));
    }

    [Theory]
    [InlineData(0, "Email")] [InlineData(1, "Email")] [InlineData(2, "Email")] [InlineData(3, "Email")]
    [InlineData(4, "Email")] [InlineData(5, "Email")] [InlineData(6, "Email")] [InlineData(7, "Email")]
    [InlineData(0, "Task")] [InlineData(1, "Task")] [InlineData(2, "Task")] [InlineData(3, "Task")]
    [InlineData(4, "Task")] [InlineData(5, "Task")] [InlineData(6, "Task")] [InlineData(7, "Task")]
    public void EveryEachFileOutcomeCombinationRunsOnceForMatchingFileCodes(int flags, string type)
    {
        var source = Capture("Traditional");
        var action = source["NextActions"]!["NextAction"]![type == "Email" ? 0 : 3]!.DeepClone().AsObject();
        action["DoAfter"] = "FileDone"; action["DoIfSuccess"] = flags & 1;
        action["DoIfFailure"] = (flags >> 1) & 1; action["DoIfNoAction"] = (flags >> 2) & 1;
        action["Parameters"] = new JsonObject { ["Parameter"] = new JsonArray(new JsonObject { ["Name"] = "File", ["value"] = "[OrigName]" }) };
        source["NextActions"] = new JsonObject { ["NextAction"] = action };
        var result = TraditionalTaskAdvancedConversion.Build(source, "File action Advanced").Definition;
        var root = result["steps"]!.AsArray();
        Assert.Equal(new[] { "Source", "For" }, root.OfType<JsonObject>().Select(AdvancedTaskDefinition.Kind));
        var loop = root[1]!["For"]!["steps"]!.AsArray();
        Assert.Equal((flags & 3) == 0 ? new[] { "Process", "Destination" } : new[] { "Process", "Destination", "If" },
            loop.OfType<JsonObject>().Select(AdvancedTaskDefinition.Kind));
        var branches = loop.Last()?["If"]?["When"]?.AsArray().OfType<JsonObject>().ToList() ?? [];
        Assert.Equal(System.Numerics.BitOperations.PopCount((uint)(flags & 3)), branches.Count);
        foreach (var branch in branches)
        {
            var comp = branch["Criteria"]!["comp"]!;
            Assert.Equal("[ErrorCodeFile]", comp["a"]!.ToString()); Assert.Equal("0", comp["b"]!.ToString());
            Assert.Contains(comp["test"]!.ToString(), new[] { "NEQ", "NGT" });
            Assert.Collection(branch["steps"]!.AsArray(), wrapper =>
            {
                var body = wrapper![type == "Email" ? "Email" : "RunTask"]!;
                Assert.Null(body["DoIfNoAction"]); Assert.Null(body["DoAfter"]); Assert.Null(body["Type"]);
                Assert.Equal("[OrigName]", body["Parameters"]!["Parameter"]![0]!["value"]!.ToString());
            });
        }
        foreach (var code in new[] { -1, 0, 1, 100 })
        {
            var executions = branches.Where(branch => branch["Criteria"]!["comp"]!["test"]!.ToString() == "NEQ" ? code == 0 : code > 0)
                .Sum(branch => branch["steps"]!.AsArray().Count);
            Assert.Equal(code == 0 && (flags & 1) != 0 || code > 0 && (flags & 2) != 0 ? 1 : 0, executions);
        }
        var ids = AdvancedTaskDefinition.Flatten(result["steps"]).Select(step => step[AdvancedTaskDefinition.Kind(step)]!["ID"]!.ToString()).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.True(long.Parse(result["NextEID"]!.ToString()) > ids.Max(long.Parse));
    }

    [Fact]
    public void MixedEachFileActionsKeepTheirOrderBeforeOnceProcessesAndTaskActions()
    {
        var source = Capture("Traditional");
        var actions = source["NextActions"]!["NextAction"]!.AsArray();
        actions[0]!["DoAfter"] = "FileDone"; actions[3]!["DoAfter"] = "FileDone";
        actions[4]!["DoAfter"] = "FileDone";
        source["steps"]!.AsArray().Add(new JsonObject { ["Process"] = new JsonObject { ["ID"] = "30", ["ScriptID"] = "once", ["Run"] = "Once", ["IsDest"] = 0 } });
        var result = TraditionalTaskAdvancedConversion.Build(source, "Mixed Advanced").Definition;
        var steps = result["steps"]!.AsArray();
        Assert.Equal(new[] { "Source", "For", "Process", "If" }, steps.OfType<JsonObject>().Select(AdvancedTaskDefinition.Kind));
        var branches = steps[1]!["For"]!["steps"]![2]!["If"]!["When"]!.AsArray();
        Assert.Equal(new[] { "Email", "RunTask" }, branches[0]!["steps"]!.AsArray().OfType<JsonObject>().Select(AdvancedTaskDefinition.Kind));
        Assert.Equal(new[] { "353799693", "508667141" }, branches[1]!["steps"]!.AsArray().Select(wrapper => wrapper!["RunTask"]!["TaskID"]!.ToString()));
        Assert.Equal("once", steps[2]!["Process"]!["ScriptID"]!.ToString());
        Assert.Equal(new[] { "Failure", "No xfers" }, steps[3]!["If"]!["When"]!.AsArray().Select(branch => branch!["Criteria"]!["comp"]!["b"]!.ToString()));
    }

    [Theory]
    [InlineData(1)] [InlineData(3)] [InlineData(4)] [InlineData(7)]
    public async Task NativeCreationAndReadbackSupportEachFileSuccessCombinedAndDroppedOutcomes(int flags)
    {
        var source = Capture("Traditional");
        var action = source["NextActions"]!["NextAction"]![3]!.DeepClone().AsObject();
        action["DoAfter"] = "FileDone"; action["DoIfSuccess"] = flags & 1;
        action["DoIfFailure"] = (flags >> 1) & 1; action["DoIfNoAction"] = (flags >> 2) & 1;
        source["NextActions"] = new JsonObject { ["NextAction"] = action };
        using var fixture = new Fixture(source) { ReencryptSecrets = true };
        await fixture.Crud.CreateAdvancedFromTraditionalAsync(source["ID"]!.ToString());
        var loop = XElement.Parse(fixture.CreatedXml!).Element("For")!;
        Assert.Equal(System.Numerics.BitOperations.PopCount((uint)(flags & 3)), loop.Descendants("RunTask").Count());
        Assert.All(loop.Descendants("comp"), comp => Assert.Equal("[ErrorCodeFile]", (string?)comp.Attribute("a")));
        Assert.Equal(1, fixture.Creates); Assert.Equal(1, fixture.Releases);
    }

    [Theory]
    [InlineData(0)] [InlineData(4)]
    public void DroppedEachFileActionsDoNotCreateAnEmptyLoop(int flags)
    {
        var source = Capture("Traditional"); source["steps"] = new JsonArray();
        source["NextActions"] = new JsonObject { ["NextAction"] = new JsonObject
        {
            ["Type"] = "Task", ["TaskID"] = "77", ["DoAfter"] = "FileDone",
            ["DoIfSuccess"] = 0, ["DoIfFailure"] = 0, ["DoIfNoAction"] = flags >> 2
        } };
        Assert.Empty(TraditionalTaskAdvancedConversion.Build(source, "Empty Advanced").Definition["steps"]!.AsArray());
    }

    // Only transport fixtures translate generic XML/JSON. Production conversion has one topology planner.
    private static XElement ToXml(string name, JsonObject value)
    {
        var element = new XElement(name);
        foreach (var field in value)
        {
            if (field.Value is null) continue;
            if (field.Key == "steps") foreach (var wrapper in field.Value.AsArray().OfType<JsonObject>()) element.Add(ToXml(AdvancedTaskDefinition.Kind(wrapper), wrapper[AdvancedTaskDefinition.Kind(wrapper)]!.AsObject()));
            else if (field.Value is JsonObject obj) element.Add(ToXml(field.Key, obj));
            else if (field.Value is JsonArray array) foreach (var item in array) element.Add(item is JsonObject child ? ToXml(field.Key, child) : new XElement(field.Key, item?.ToString()));
            else if (field.Key == "value") element.Value = field.Value.ToString();
            else element.SetAttributeValue(field.Key, field.Value.ToString());
        }
        return element;
    }
    private static JsonObject FromXml(XElement element)
    {
        var body = new JsonObject();
        foreach (var attribute in element.Attributes()) body[attribute.Name.LocalName] = attribute.Name.LocalName.Contains("Password") ? null : attribute.Value;
        var steps = element.Elements().Where(child => AdvancedTaskDefinition.Kinds.Contains(child.Name.LocalName)).ToList();
        if (steps.Count > 0) body["steps"] = new JsonArray(steps.Select(child => (JsonNode)new JsonObject { [child.Name.LocalName] = FromXml(child) }).ToArray());
        foreach (var group in element.Elements().Except(steps).GroupBy(child => child.Name.LocalName))
            body[group.Key] = group.Key is "When" or "Parameter" or "Schedule" or "Interval" or "DayOfWeek" || group.Count() > 1
                ? new JsonArray(group.Select(child => (JsonNode)FromXml(child)).ToArray()) : FromXml(group.First());
        if (!element.HasElements && element.Value.Length > 0) body["value"] = element.Value;
        return body;
    }

    private sealed class Fixture : IDisposable
    {
        public TaskCrudCoordinator Crud { get; }
        public List<string> Events { get; } = [];
        public int Creates { get; private set; }
        public int Releases { get; private set; }
        public int DuplicateNames { get; set; }
        public bool ReencryptSecrets { get; set; }
        public string? Failure { get; set; }
        public string? CreatedXml { get; private set; }
        private string? _savedXml;
        public Fixture(JsonObject source)
        {
            var native = ToXml("Task", source);
            native.Element("Source")!.SetAttributeValue("Password", "stored-password");
            var parameter = native.Element("Parameters")!.Element("Parameter")!;
            parameter.SetAttributeValue("Secure", "1"); parameter.Value = "stored-parameter";
            var restSource = source.DeepClone().AsObject();
            restSource["Parameters"]!["Parameter"]![0]!["Secure"] = 1;
            restSource["Parameters"]!["Parameter"]![0]!["value"] = null;
            var rest = Stub<IMoveItRestClient>((method, args) =>
            {
                switch (method.Name)
                {
                    case nameof(IMoveItRestClient.CheckoutTaskV0Async): Events.Add("checkout"); return Task.CompletedTask;
                    case nameof(IMoveItRestClient.ReleaseTaskCheckoutV0Async): Events.Add("release"); Releases++; return Failure == "release" ? Task.FromException(new HttpRequestException("Release unavailable")) : Task.CompletedTask;
                    case nameof(IMoveItRestClient.TaskNameExistsForOtherTaskAsync): Events.Add("name"); return Task.FromResult(DuplicateNames-- > 0);
                    case nameof(IMoveItRestClient.GetTaskAsync):
                        if ((string)args[0]! == source["ID"]!.ToString()) { Events.Add("source"); return Task.FromResult(restSource.DeepClone().AsObject()); }
                        Events.Add("readback");
                        if (Failure == "readback") return Task.FromException<JsonObject>(new HttpRequestException("Readback unavailable"));
                        return Task.FromResult(FromXml(XElement.Parse(_savedXml!)));
                    default: throw new NotSupportedException(method.Name);
                }
            });
            var com = Stub<IMoveItComClient>((method, args) =>
            {
                if (method.Name == nameof(IMoveItComClient.GetTaskConfigurationAsync))
                { Events.Add("native-source"); return Task.FromResult(new MoveItTaskConfiguration(source["ID"]!.ToString(), source["Name"]!.ToString(), native.ToString())); }
                if (method.Name == nameof(IMoveItComClient.CreateTaskFromConfigurationAsync))
                {
                    Events.Add("create"); Creates++; Assert.True((bool)args[2]!); CreatedXml = (string)args[1]!;
                    var saved = XElement.Parse(CreatedXml); saved.SetAttributeValue("ID", "900");
                    if (Failure == "enabled") saved.SetAttributeValue("Active", "1");
                    if (Failure == "secret") saved.Element("Source")!.SetAttributeValue("Password", "");
                    if (ReencryptSecrets) { saved.Element("Source")!.SetAttributeValue("Password", "new-cipher"); saved.Element("Parameters")!.Element("Parameter")!.Value = "new-parameter-cipher"; }
                    _savedXml = saved.ToString();
                    return Task.FromResult(new MoveItTaskConfiguration("900", (string)args[0]!, _savedXml));
                }
                throw new NotSupportedException(method.Name);
            });
            var traditional = Stub<ITraditionalTaskPersistenceService>((method, _) => throw new NotSupportedException(method.Name));
            Crud = new TaskCrudCoordinator([new TraditionalTaskCrudStrategy(traditional), new NativeTaskCrudStrategy(MoveItTaskKind.Advanced, com),
                new NativeTaskCrudStrategy(MoveItTaskKind.Synchronization, com)], traditional, rest, com);
        }
        public void Dispose() { }
    }
    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    { var value = DispatchProxy.Create<T, Proxy>(); ((Proxy)(object)value).Handler = handler; return value; }
    public class Proxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args ?? []);
    }
}
