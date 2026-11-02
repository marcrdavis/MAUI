using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Tasks;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class NativePortabilityReuseImportTests
{
    [Theory]
    [InlineData("Task")] [InlineData("Host")]
    public async Task ControllerResponseIncludesReuseCountAndReadOnlyItems(string type)
    {
        var native = Proxy<IMauiNativePortabilityService>((method, _) => method.Name == "ImportAsync"
            ? Task.FromResult(new NativePortabilityImportResult(true, type, 1, 0, 0,
                [new("Script", "2", "Source", "Existing", "21", false, false, false, "", Reused: true)], "", ReusedCount: 1)) : throw Unexpected(method));
        var session = Proxy<IMoveItSessionService>((method, _) => method.Name == "Get" ? new MoveItSessionState { Username = "admin" } : throw Unexpected(method));
        object Dependency(Type dependency)
        {
            if (dependency == typeof(IMauiNativePortabilityService)) return native;
            if (dependency == typeof(IMoveItSessionService)) return session;
            if (dependency == typeof(ILogger<TasksController>)) return NullLogger<TasksController>.Instance;
            if (dependency == typeof(ILogger<HostsController>)) return NullLogger<HostsController>.Instance;
            var proxy = DispatchProxy.Create(dependency, typeof(CallProxy));
            ((CallProxy)proxy).Handler = (method, _) => dependency.IsGenericType && dependency.GetGenericTypeDefinition() == typeof(IOptionsSnapshot<>)
                ? Activator.CreateInstance(dependency.GetGenericArguments()[0]) : throw Unexpected(method);
            return proxy;
        }
        var controllerType = type == "Task" ? typeof(TasksController) : typeof(HostsController);
        var constructor = Assert.Single(controllerType.GetConstructors());
        var controller = constructor.Invoke(constructor.GetParameters().Select(parameter => Dependency(parameter.ParameterType)).ToArray());
        using var stream = new MemoryStream([1]);
        var request = new NativePortabilityImportRequest { File = new FormFile(stream, 0, 1, "File", "test.maui") };
        var action = controller is TasksController tasks ? await tasks.ImportTask(request) : await ((HostsController)controller).ImportHost(request);
        var json = JsonSerializer.SerializeToNode(Assert.IsType<JsonResult>(action).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(1, json["importedCount"]!.GetValue<int>()); Assert.Equal(1, json["reusedCount"]!.GetValue<int>());
        Assert.True(json["items"]![0]!["reused"]!.GetValue<bool>());
    }

    [Fact]
    public async Task ReuseDefaultsAreServerVerifiedAndDoNotChangeTaskChoices()
    {
        var fixture = new Fixture();
        var preview = await fixture.Service.PreflightImportAsync(fixture.Package, "Task", "");
        Assert.False(Assert.Single(preview.Items, item => item.ObjectType == "Task").CanReuseExisting);
        Assert.True(Assert.Single(preview.Items, item => item.ObjectType == "Task").CanRename);
        Assert.All(preview.Items.Where(item => item.Dependency), item =>
        {
            Assert.True(item.CanReuseExisting); Assert.NotEmpty(item.ConflictTargetId); Assert.NotEmpty(item.ReuseTargetName);
        });
    }

    [Fact]
    public async Task ReusedDependenciesNeverReachNativeWriteUndoMetadataOrAudit()
    {
        var fixture = new Fixture();
        var result = await fixture.Import();
        Assert.True(result.Success); Assert.Equal(1, result.ImportedCount); Assert.Equal(1, result.OverwrittenCount);
        Assert.Equal(0, result.RenamedCount); Assert.Equal(4, result.ReusedCount); Assert.Equal(5, result.Items.Count);
        Assert.All(result.Items.Where(item => item.Reused), item =>
        {
            Assert.False(item.Selected); Assert.False(item.Overwritten); Assert.False(item.MetadataApplied); Assert.Empty(item.Warning);
        });
        Assert.Equal(1, fixture.Commits); Assert.Equal(3, fixture.EncodedXml.Count);
        Assert.Equal(new[] { "undo:Task:100", "commit", "metadata:Task:100", "audit:Task:100" }, fixture.Writes);
        Assert.Equal(new[] { "Host:20:Read", "Script:21:Read", "DateList:22:Read", "KeyCertificate:ssh|9:Read", "create:Task", "Task:100:Edit" }, fixture.Access);
        var rewritten = XDocument.Parse(fixture.EncodedXml[^1]);
        Assert.Empty(rewritten.Root!.Element("Hosts")!.Elements()); Assert.Empty(rewritten.Root.Element("Scripts")!.Elements());
        Assert.Empty(rewritten.Root.Element("DateLists")!.Elements()); Assert.Empty(rewritten.Root.Element("SSHClientKeys")!.Elements());
        Assert.Equal("20", rewritten.Descendants("Source").Single().Attribute("HostID")!.Value);
        Assert.Equal("21", rewritten.Descendants("Process").Single().Attribute("ScriptID")!.Value);
        Assert.Equal("22", rewritten.Descendants("Task").Single().Attribute("SchedDateListID")!.Value);
        Assert.Equal("0", fixture.CommittedModel!["Tasks"]!["Task"]![0]!["Active"]!.ToString());
        Assert.Empty(((JsonArray)fixture.CommittedModel["Hosts"]!["hosts"]!));
    }

    [Theory]
    [InlineData(4)] [InlineData(5)]
    public async Task ChangedDestinationIsRejectedBeforeCommit(int changeAt)
    {
        var fixture = new Fixture { ChangeHostAt = changeAt };
        await Assert.ThrowsAsync<InvalidOperationException>(fixture.Import);
        Assert.Equal(0, fixture.Commits);
        Assert.DoesNotContain(fixture.Writes, entry => entry.StartsWith("metadata:", StringComparison.Ordinal) || entry.StartsWith("audit:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AuthoritativeReencodeReintroducingAnExcludedObjectIsRejected()
    {
        var fixture = new Fixture { ReintroduceHost = true };
        await Assert.ThrowsAsync<InvalidOperationException>(fixture.Import);
        Assert.Equal(0, fixture.Commits); Assert.Empty(fixture.Writes);
    }

    [Fact]
    public async Task LostCommitResponseIsNotRetriedOrReportedAsSuccess()
    {
        var fixture = new Fixture { LoseCommitResponse = true };
        await Assert.ThrowsAsync<NativePortabilityImportOutcomeUnknownException>(fixture.Import);
        Assert.Equal(1, fixture.Commits); Assert.Equal(new[] { "undo:Task:100", "commit" }, fixture.Writes);
    }

    [Fact]
    public async Task UnavailableReuseReadAccessRejectsTheEntireImportBeforeWrites()
    {
        var fixture = new Fixture { DenyReuseRead = true };
        await Assert.ThrowsAsync<MoveItObjectAccessException>(fixture.Import);
        Assert.Equal(0, fixture.Commits); Assert.Empty(fixture.Writes);
    }

    [Fact]
    public async Task PostCommitRefreshFailurePreservesCompletedStateAndReuseReport()
    {
        var fixture = new Fixture { FailReconciliation = true };
        var result = await fixture.Import();
        Assert.True(result.Success); Assert.Equal(4, result.ReusedCount); Assert.Equal(1, fixture.Commits);
        Assert.Contains("completed", result.Warning, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "undo:Task:100", "commit" }, fixture.Writes);
    }

    private sealed class Fixture
    {
        private const string NativeXml = """
            <Exported><Tasks><Task ID="100" Name="Task" TT="0" Active="1" SchedDateListID="3"><Source HostID="1"/><Process ScriptID="2"/></Task></Tasks>
            <Hosts><FTP ID="1" Name="Partner" DefClientKeyID="9" DefPassword="protected"/></Hosts>
            <Scripts><Script ID="2" Name="Script" Lang="PS1"/></Scripts><DateLists><DateList ID="3" Name="Dates"/></DateLists>
            <SSHClientKeys><SSHClientKey ID="9" Name="Key">protected-key</SSHClientKey></SSHClientKeys></Exported>
            """;
        public readonly MauiNativePortabilityService Service;
        public readonly byte[] Package;
        public readonly List<string> EncodedXml = [];
        public readonly List<string> Writes = [];
        public readonly List<string> Access = [];
        public int Commits;
        public int ChangeHostAt;
        public bool ReintroduceHost, LoseCommitResponse, FailReconciliation, DenyReuseRead;
        public JsonObject? CommittedModel;
        private int _hostReads;

        public Fixture()
        {
            var session = Proxy<IMoveItSessionService>((method, _) => method.Name == "Get"
                ? new MoveItSessionState { EnvironmentKey = "test", ServerKey = "test", Username = "admin" } : throw Unexpected(method));
            var rest = Proxy<IMoveItRestClient>((method, args) =>
            {
                if (method.Name == "EncodeNativeImportAsync")
                {
                    EncodedXml.Add((string)args[0]!);
                    var rewritten = EncodedXml.Count >= 3;
                    return Task.FromResult(Decoded(rewritten && !ReintroduceHost));
                }
                if (method.Name == "ImportNativeConfigurationAsync")
                {
                    Commits++; Writes.Add("commit"); CommittedModel = (JsonObject)((JsonObject)args[0]!).DeepClone();
                    Assert.False(((CancellationToken)args[2]!).CanBeCanceled);
                    return LoseCommitResponse ? Task.FromException(new HttpRequestException("response lost")) : Task.CompletedTask;
                }
                throw Unexpected(method);
            });
            var tasks = Proxy<IAutomationTaskService>((method, _) => method.Name == "GetTaskSnapshotAsync"
                ? Task.FromResult(new TaskCacheSnapshot([new() { Id = "100", Name = "Task", Type = "Traditional", Status = "Idle", CanEditMoveItFields = true }], DateTimeOffset.UtcNow)) : throw Unexpected(method));
            var hosts = Proxy<IAutomationHostService>((method, _) =>
            {
                if (method.Name != "GetHostSnapshotAsync") throw Unexpected(method);
                _hostReads++;
                if (FailReconciliation && _hostReads == 6) return Task.FromException<HostCacheSnapshot>(new HttpRequestException("refresh failed"));
                return Task.FromResult(new HostCacheSnapshot([new() { Id = ChangeHostAt > 0 && _hostReads >= ChangeHostAt ? "23" : "20", Name = "Partner", TypeCode = "FTP", CanEditDelete = false }], DateTimeOffset.UtcNow));
            });
            var scripts = Proxy<IAutomationScriptService>((method, _) => method.Name == "GetScriptSnapshotAsync"
                ? Task.FromResult(new ScriptCacheSnapshot([new() { Id = "21", Name = "Script", LanguageCode = "PS1", CanEditDelete = false }], DateTimeOffset.UtcNow)) : throw Unexpected(method));
            var dates = Proxy<IAutomationDateListService>((method, _) => method.Name == "GetSnapshotAsync"
                ? Task.FromResult(new DateListCacheSnapshot([new() { Id = "22", Name = "Dates", CanEditDelete = false }], DateTimeOffset.UtcNow)) : throw Unexpected(method));
            var keys = Proxy<IAutomationKeyCertificateService>((method, _) => method.Name == "GetSnapshotAsync"
                ? Task.FromResult(new KeyCertificateCacheSnapshot([new() { Id = "9", RowId = "ssh|9", Name = "Key", Kind = KeyCertificateKind.Ssh, CanEditDelete = false }], DateTimeOffset.UtcNow)) : throw Unexpected(method));
            var taskUndo = Proxy<ITaskUndoService>((method, args) =>
            {
                Assert.Equal("CaptureAsync", method.Name); Writes.Add("undo:Task:" + args[0]);
                return Task.FromResult(new TaskUndoSnapshot("snapshot", "100", "Task", "admin", DateTimeOffset.UtcNow, "<Task/>"));
            });
            var metadata = Proxy<IMetadataRepository>((method, args) =>
            {
                Assert.Equal("RecordObjectConfigurationModifiedAsync", method.Name);
                Writes.Add($"audit:{args[1]}:{args[2]}"); return Task.CompletedTask;
            });
            var portable = Proxy<IMauiMetadataPortabilityService>((method, args) => method.Name switch
            {
                "ParseFromEnvelope" => new PortableMetadataPackage(1, (string)args[1]!, []),
                "ValidateAsync" => Task.FromResult(new MetadataImportSummary(1, 0, 0, [], false)),
                "ApplyCompatibleAsync" => ApplyMetadata(args),
                _ => throw Unexpected(method)
            });
            var access = Proxy<IMoveItObjectAccessService>((method, args) =>
            {
                if (method.Name == "RequireAsync")
                {
                    Access.Add($"{args[0]}:{args[1]}:{args[2]}");
                    if (DenyReuseRead && (string)args[2]! == "Read") return Task.FromException(new MoveItObjectAccessException("Resource no longer available"));
                }
                else if (method.Name == "RequireCreationAsync") { Access.Add("create:" + args[0]); return Task.FromResult(""); }
                else throw Unexpected(method);
                return Task.CompletedTask;
            });
            Service = new(rest, Never<ITaskCrudCoordinator>(), tasks, hosts, scripts, dates, keys, taskUndo,
                Never<IHostUndoService>(), Never<IScriptUndoService>(), Never<IDateListUndoService>(), metadata, portable, session,
                NullLogger<MauiNativePortabilityService>.Instance, access);
            var metadataObjects = new[] { ("Task", "100", "Task"), ("Host", "1", "Partner"), ("Script", "2", "Script"), ("DateList", "3", "Dates"), ("KeyCertificate", "ssh|9", "Key") }
                .Select(item => new XElement("Object", new XAttribute("ObjectType", item.Item1), new XAttribute("SourceId", item.Item2), new XAttribute("SourceName", item.Item3)));
            Package = MauiNativePortablePackage.Build("Task", "server-key", true, DateTimeOffset.UtcNow, 0,
                [new("Task", "100", "Task")], NativeXml, metadataObjects.ToArray());
        }

        private Task<int> ApplyMetadata(object?[] args) { Writes.Add($"metadata:{args[1]}:{args[2]}"); return Task.FromResult(0); }
        public Task<NativePortabilityImportResult> Import() => Service.ImportAsync(Package, "Task", "",
            [new("Task", "100", "overwrite", "", "100"), new("Host", "1", "useexisting", "", "20"),
             new("Script", "2", "useexisting", "", "21"), new("DateList", "3", "useexisting", "", "22"), new("SSHClientKey", "9", "useexisting", "", "ssh|9")], true, true, "admin");

        private static JsonObject Decoded(bool reused) => JsonNode.Parse(reused ? """
            {"Tasks":{"Task":[{"ID":"100","Name":"Task","TT":"0","Active":"1","Source":[{"HostID":"20"}],"Process":[{"ScriptID":"21"}],"SchedDateListID":"22"}]},
             "Hosts":{"hosts":[]},"Scripts":{"Script":[]},"DateLists":{"DateList":[]},"SSHClientKeys":{"SSHClientKey":[]},
             "Duplicated":[{"Type":"Task","ID":"100","Reason":["id","name"]}]}
            """ : """
            {"Tasks":{"Task":[{"ID":"100","Name":"Task","TT":"0","Active":"1"}]},"Hosts":{"hosts":[{"FTP":{"ID":"1","Name":"Partner"}}]},
             "Scripts":{"Script":[{"ID":"2","Name":"Script","Lang":"PS1"}]},"DateLists":{"DateList":[{"ID":"3","Name":"Dates"}]},
             "SSHClientKeys":{"SSHClientKey":[{"ID":"9","Name":"Key"}]},
             "Duplicated":[{"Type":"Task","ID":"100","Reason":["id","name"]},{"Type":"Host","ID":"1","Reason":["name"]},
              {"Type":"Script","ID":"2","Reason":["name"]},{"Type":"DateList","ID":"3","Reason":["name"]}]}
            """)!.AsObject();
    }

    private static InvalidOperationException Unexpected(MethodInfo method) => new("Unexpected service call: " + method.Name);
    private static T Never<T>() where T : class => Proxy<T>((method, _) => throw Unexpected(method));
    private static T Proxy<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, CallProxy>(); ((CallProxy)(object)proxy).Handler = handler; return proxy;
    }
    public class CallProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args ?? []);
    }
}
