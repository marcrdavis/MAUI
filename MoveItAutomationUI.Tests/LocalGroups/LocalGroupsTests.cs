using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Features.LocalGroups;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Tests.LocalGroups;

public sealed class LocalGroupsTests
{
    private const string Sid = "S-1-5-21-100-200-300-1001";
    [Theory]
    [InlineData("S-1-5-32-544")]
    [InlineData("S-1-5-32-547")]
    [InlineData("S-1-5-32-551")]
    [InlineData("S-1-5-18")]
    [InlineData("S-1-5-21-100-200-300-512")]
    [InlineData("S-1-5-21-100-200-300-4294967296")]
    [InlineData("S-1-5-21-100-200-300-+1001")]
    [InlineData("S-1-5-21-100-200-300- 1001")]
    public void DefaultReservedAndMalformedGroupTargetsAreBlocked(string sid)
        => Assert.Throws<ArgumentException>(() => LocalGroupsProtocol.Validate(new() { Operation = "delete", GroupSid = sid, GroupName = "Operators", ExpectedVersion = new('A',64), ConfirmDelete = "Operators" }));
    [Theory]
    [InlineData("")]
    [InlineData("Bad/Name")]
    [InlineData("Bad\\Name")]
    [InlineData(" Name")]
    [InlineData("Name.")]
    [InlineData("Name\n")]
    public void InvalidCreateNamesAreRejected(string name) => Assert.Throws<ArgumentException>(() => LocalGroupsProtocol.Validate(new() { Operation = "create", GroupName = name }));
    [Theory]
    [InlineData("CORP\\TransferTeam")]
    [InlineData("SERVER01\\LocalUser")]
    [InlineData(".\\LocalGroup")]
    public void CommandsAreFlatAndReadableByTheFrameworkServiceContract(string principal)
    {
        var command = new AgentCommand { Id = Guid.NewGuid().ToString("D"), ComputerName = "SERVER01", RequestedBy = "CORP\\Admin", ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(30),
            Operation = "add-member", GroupName = "Operators", GroupSid = Sid, ExpectedVersion = new('A',64), Principal = principal };
        LocalGroupsProtocol.Validate(command);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(command, LocalGroupsProtocol.Json);
        using var stream = new MemoryStream(bytes);
        var service = Assert.IsType<ServiceCommand>(new DataContractJsonSerializer(typeof(ServiceCommand)).ReadObject(stream));
        Assert.Equal(command.Id, service.Id); Assert.Equal(1, service.ProtocolVersion); Assert.Equal("add-member", service.Operation);
        Assert.Equal(command.Principal, service.Principal); Assert.Equal(command.GroupName, service.GroupName); Assert.Contains("T", service.ExpiresUtc!);
        using var json = JsonDocument.Parse(bytes); Assert.False(json.RootElement.TryGetProperty("request", out _));
    }
    [DataContract]
    public sealed class ServiceCommand
    {
        [DataMember(Name="id")] public string? Id { get; set; }
        [DataMember(Name="protocolVersion")] public int ProtocolVersion { get; set; }
        [DataMember(Name="operation")] public string? Operation { get; set; }
        [DataMember(Name="groupName")] public string? GroupName { get; set; }
        [DataMember(Name="principal")] public string? Principal { get; set; }
        [DataMember(Name="expiresUtc")] public string? ExpiresUtc { get; set; }
    }
    [Theory]
    [InlineData("resolve")]
    [InlineData("add-member")]
    public void DomainOnlyServiceFailureExplainsHowToEnableLocalAccounts(string operation)
    {
        var result = new AgentResult
        {
            ComputerName = "SERVER01",
            Message = "Only domain users and domain security groups can be added. Local, built-in and well-known identities are not accepted."
        };

        var failure = LocalGroupsService.OperationFailure(operation, result);

        Assert.Contains("SERVER01", failure.Message);
        Assert.Contains("1.0.4 or later", failure.Message);
        Assert.False(failure.OutcomeUncertain);
    }

    [Fact]
    public void NativeMembershipFailureRemainsVisible()
    {
        var result = new AgentResult { Message = "Windows rejected this account type.", OutcomeUncertain = true };
        var failure = LocalGroupsService.OperationFailure("add-member", result);
        Assert.Equal(result.Message, failure.Message);
        Assert.True(failure.OutcomeUncertain);
    }

    [Fact]
    public void FrameworkResultIncludingNullMemberDisplayNameUsesTheExpectedContract()
    {
        var json = "{\"protocolVersion\":1,\"computerName\":\"SERVER01\",\"id\":\"11111111-2222-4333-8444-555555555555\",\"success\":true,\"outcomeUncertain\":false,\"message\":\"Loaded.\",\"groups\":[{\"sid\":\""+Sid+"\",\"name\":\"Operators\",\"description\":\"\",\"protected\":false,\"members\":[{\"sid\":\"S-1-5-21-400-500-600-1200\",\"account\":\"CORP\\\\TransferTeam\",\"type\":\"Group\",\"displayName\":null}],\"version\":\""+new string('A',64)+"\"}]}";
        var result = JsonSerializer.Deserialize<AgentResult>(json, LocalGroupsProtocol.Json)!;
        LocalGroupsProtocol.ValidateResult(result,"SERVER01");
        Assert.Equal("Group",Assert.Single(Assert.Single(result.Groups!).Members).Type);
        Assert.Null(Assert.Single(result.Groups!).Members[0].DisplayName);
    }
    [Fact]
    public void IndependentAgentAuthenticationAndFeatureDefaultsAreDeclared()
    {
        var auth = Assert.IsType<AuthorizeAttribute>(Assert.Single(typeof(LocalGroupsAgentController).GetCustomAttributes(typeof(AuthorizeAttribute), true)));
        Assert.Equal(LocalGroupsProtocol.AgentScheme, auth.AuthenticationSchemes);
        Assert.False(new LocalGroupsOptions().Enabled.Value);
        Assert.Equal("api/local-groups-agent", LocalGroupsProtocol.AgentRoute);
    }
    [Fact]
    public void RegistrationsCannotShareCertificatesAndMustIdentifyAConfiguredServer()
    {
        var settings = new LocalGroupsSettings { Enabled = true, Agents = [Agent(), new() { ServerKey="B", CertificateSha256=new('A',64) }] };
        Assert.Throws<ArgumentException>(() => settings.NormalizeAndValidate(["A","B"]));
        settings.Agents.RemoveAt(1); settings.NormalizeAndValidate(["A"]);
        settings.Agents[0].ServerKey = "UNKNOWN"; Assert.Throws<ArgumentException>(() => settings.NormalizeAndValidate(["A"]));
    }
    [Fact]
    public void DeletionReferencesAndIncompleteInventoriesFailClosed()
    {
        JsonObject Acl(string name) => new() { ["UserGroupName"]=name,["ResourceGroupName"]="Finance" };
        foreach (var name in new[] { "Operators", "SERVER01\\Operators", ".\\Operators", "OTHER\\Operators", "SERVER01.example.com\\Operators" })
            Assert.Throws<InvalidOperationException>(() => LocalGroupsService.RequireUnassigned([Acl(name)],"Operators","SERVER01"));
        Assert.Throws<InvalidOperationException>(() => LocalGroupsService.RequireUnassigned([new JsonObject()],"Operators","SERVER01"));
        LocalGroupsService.RequireUnassigned([Acl("CORP\\Different")],"Operators","SERVER01");
    }
    [Fact]
    public void BindingIgnoresForgedBrowserBindingAndDoesNotFollowAChangedTarget()
    {
        var settings = new LocalGroupsSettings { Agents=[Agent()] };
        var old = LocalGroupsBinding.Compute("A","ENV","https://old/","old");
        settings.Agents[0].ConnectionBinding = "forged";
        var document = new JsonObject { [LocalGroupsOptions.SectionName]=JsonSerializer.SerializeToNode(new LocalGroupsOptions { Agents=[new() { ServerKey="A",CertificateSha256=new('A',64),ConnectionBinding=old }] }) };
        LocalGroupsBinding.BindRegistrations(settings,document,[new() { Key="A",EnvironmentKey="ENV",WebAdminUrl="https://new/",ServerHost="new" }]);
        Assert.Equal(old,settings.Agents[0].ConnectionBinding);
        settings.Agents[0].CertificateSha256=new('B',64);
        LocalGroupsBinding.BindRegistrations(settings,document,[new() { Key="A",EnvironmentKey="ENV",WebAdminUrl="https://new/",ServerHost="new" }]);
        Assert.Equal(LocalGroupsBinding.Compute("A","ENV","https://new/","new"),settings.Agents[0].ConnectionBinding);
    }
    [Fact]
    public async Task LostDeliveryRedeliversTheSameIdAndDuplicateResultsAreIdempotent()
    {
        using var f = new BrokerFixture(); var agent=Agent(); int authorizations=0;
        var awaiting=f.Broker.ExecuteAsync(agent,"SERVER01","CORP\\Admin",new(){Operation="list"},_=>{authorizations++;return Task.CompletedTask;},default);
        var first=await f.Broker.PollAsync(agent,"SERVER01",default);var second=await f.Broker.PollAsync(agent,"SERVER01",default);
        Assert.NotNull(first);Assert.Equal(first.Id,second!.Id);Assert.Equal(2,authorizations);
        var result=Reply(first);f.Broker.Complete(agent,result);Assert.Same(result,await awaiting);f.Broker.Complete(agent,result);
        var changed=Reply(first);changed.Success=false;Assert.Throws<ArgumentException>(()=>f.Broker.Complete(agent,changed));
    }
    [Fact]
    public async Task AnotherRegistrationCannotCompleteOrReceiveTheCommand()
    {
        using var f=new BrokerFixture();var agent=Agent();var awaiting=f.Broker.ExecuteAsync(agent,"SERVER01","Admin",new(){Operation="list"},_=>Task.CompletedTask,default);
        var command=(await f.Broker.PollAsync(agent,"SERVER01",default))!;
        var other=Agent();other.ServerKey="B";other.CertificateSha256=new('B',64);
        Assert.Throws<ArgumentException>(()=>f.Broker.Complete(other,Reply(command)));
        f.Broker.Complete(agent,Reply(command));await awaiting;
    }
    [Fact]
    public async Task RecoveryCannotDispatchAnUnexpiredCommandWithoutItsLiveAuthorization()
    {
        using var f = new BrokerFixture();
        var agent = Agent();
        var awaiting = f.Broker.ExecuteAsync(agent, "SERVER01", "Admin", new() { Operation = "create", GroupName = "Operators" }, _ => Task.CompletedTask, default);
        var command=(await f.Broker.PollAsync(agent,"SERVER01",default))!;
        var recovered=new LocalGroupsBroker(f.Clock,new LocalGroupsLedger(f.Directory),NullLogger<LocalGroupsBroker>.Instance);
        using var stop=new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>recovered.PollAsync(agent,"SERVER01",stop.Token));
        f.Clock.Now=f.Clock.Now.AddSeconds(36);
        Assert.Equal(command.Id,(await recovered.PollAsync(agent,"SERVER01",default))!.Id);
        recovered.Complete(agent,Reply(command));f.Broker.Complete(agent,Reply(command));await awaiting;
    }
    [Fact]
    public async Task FailedDispatchAuthorizationNeverSendsACommand()
    {
        using var f=new BrokerFixture();var agent=Agent();var awaiting=f.Broker.ExecuteAsync(agent,"SERVER01","Admin",new(){Operation="list"},_=>throw new UnauthorizedAccessException("Revoked"),default);
        using var stop=new CancellationTokenSource(TimeSpan.FromMilliseconds(40));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>f.Broker.PollAsync(agent,"SERVER01",stop.Token));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>awaiting);
    }
    [Fact]
    public async Task BrowserDisconnectCannotCancelAnIssuedMutationOrCreateASecondWrite()
    {
        using var f = new BrokerFixture(); var agent = Agent(); using var stop = new CancellationTokenSource();
        var waiting = f.Broker.ExecuteAsync(agent,"SERVER01", "Admin", new() { Operation = "create", GroupName = "Operators" }, _ => Task.CompletedTask, stop.Token);
        var command = (await f.Broker.PollAsync(agent,"SERVER01", default))!;
        stop.Cancel(); Assert.False(waiting.IsCompleted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Broker.ExecuteAsync(agent,"SERVER01", "Admin", new() { Operation = "create", GroupName = "Operators" }, _ => Task.CompletedTask, default));
        var inventoryBusy = await Assert.ThrowsAsync<LocalGroupsOperationException>(() => f.Broker.ExecuteAsync(agent, "SERVER01", "Admin", new(), _ => Task.CompletedTask, default));
        Assert.True(inventoryBusy.Retryable);
        Assert.False(inventoryBusy.OutcomeUncertain);
        f.Broker.Complete(agent, Reply(command)); Assert.True((await waiting).Success);
    }
    [Fact]
    public async Task CompletedReceiptsSurviveRecycleAndAcknowledgeDuplicatesWithoutPersistingSnapshots()
    {
        using var f = new BrokerFixture(); var agent = Agent();
        var waiting = f.Broker.ExecuteAsync(agent,"SERVER01", "Admin", new() { Operation = "get", GroupSid = Sid }, _ => Task.CompletedTask, default);
        var command = (await f.Broker.PollAsync(agent,"SERVER01", default))!;
        var result = Reply(command); result.Groups = [new() { Sid = Sid, Name = "Operators", Description = "Snapshot not retained", Version = new('A', 64) }];
        f.Broker.Complete(agent, result); await waiting;
        var record = Assert.Single(new LocalGroupsLedger(f.Directory).Read(f.Clock.Now));
        Assert.Null(record.Result!.Groups);
        var recovered = new LocalGroupsBroker(f.Clock, new LocalGroupsLedger(f.Directory), NullLogger<LocalGroupsBroker>.Instance);
        recovered.Complete(agent, result);
    }
    [Fact]
    public async Task RetentionRemovesDurableFilesWithoutRequiringAnApplicationRestart()
    {
        using var f = new BrokerFixture(); var agent = Agent();
        var waiting = f.Broker.ExecuteAsync(agent,"SERVER01", "Admin", new(), _ => Task.CompletedTask, default);
        var command = (await f.Broker.PollAsync(agent,"SERVER01", default))!;
        f.Broker.Complete(agent, Reply(command)); await waiting;
        Assert.Single(System.IO.Directory.EnumerateFiles(f.Directory, "*.json"));
        f.Clock.Now = f.Clock.Now.AddDays(3);
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Broker.ExecuteAsync(agent,"SERVER01", "Admin", new(), _ => Task.CompletedTask, stop.Token));
        Assert.Empty(System.IO.Directory.EnumerateFiles(f.Directory, "*.json"));
    }
    [Fact]
    public async Task CancelledIssuedReadAcceptsItsLateResultBeforeAllowingAnotherRequest()
    {
        using var f = new BrokerFixture(); var agent = Agent(); using var stop = new CancellationTokenSource();
        var waiting = f.Broker.ExecuteAsync(agent,"SERVER01", "Admin", new() { Operation = "get", GroupSid = Sid }, _ => Task.CompletedTask, stop.Token);
        var command = (await f.Broker.PollAsync(agent,"SERVER01", default))!;
        stop.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Broker.ExecuteAsync(agent, "SERVER01", "Admin", new() { Operation = "get", GroupSid = Sid }, _ => Task.CompletedTask, default));
        f.Broker.Complete(agent, Reply(command)); f.Broker.Complete(agent, Reply(command));
        var next = f.Broker.ExecuteAsync(agent,"SERVER01", "Admin", new(), _ => Task.CompletedTask, default);
        var fresh = (await f.Broker.PollAsync(agent,"SERVER01", default))!;
        Assert.NotEqual(command.Id, fresh.Id);
        f.Broker.Complete(agent, Reply(fresh)); Assert.True((await next).Success);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AbandonedInventoryCannotBlockOrCompleteTheNextLoad(bool restarted)
    {
        using var f = new BrokerFixture();
        var agent = Agent();
        using var stop = new CancellationTokenSource();
        var first = f.Broker.ExecuteAsync(agent, "SERVER01", "Admin", new(), _ => Task.CompletedTask, stop.Token);
        var abandoned = Assert.IsType<AgentCommand>(await f.Broker.PollAsync(agent, "SERVER01", default));
        var broker = restarted ? new LocalGroupsBroker(f.Clock, new LocalGroupsLedger(f.Directory), NullLogger<LocalGroupsBroker>.Instance) : f.Broker;
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Empty(System.IO.Directory.EnumerateFiles(f.Directory, "*.json"));

        var next = broker.ExecuteAsync(agent, "SERVER01", "Admin", new(), _ => Task.CompletedTask, default);
        var command = Assert.IsType<AgentCommand>(await broker.PollAsync(agent, "SERVER01", default));
        Assert.NotEqual(abandoned.Id, command.Id);
        broker.Complete(agent, Reply(abandoned));
        Assert.False(next.IsCompleted);
        broker.Complete(agent, Reply(command));
        Assert.True((await next).Success);
    }
    [Fact]
    public async Task AuthenticatedPollReportsTheComputerWithoutAConfiguredName()
    {
        using var f = new BrokerFixture(); var agent = Agent();
        Assert.Null(f.Broker.Connection(agent));
        using var stop = new CancellationTokenSource();
        var polling = f.Broker.PollAsync(agent, "SERVER01", stop.Token);
        Assert.Equal("SERVER01", f.Broker.Connection(agent)!.ComputerName);
        Assert.Equal(f.Clock.Now, f.Broker.Connection(agent)!.LastSeenUtc);
        var changed = Agent(); changed.ConnectionBinding = new('C', 64);
        Assert.Null(f.Broker.Connection(changed));
        stop.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => polling);
    }
    [Fact]
    public async Task AReportedComputerCannotTakeOverAnotherComputersPendingCommand()
    {
        using var f = new BrokerFixture(); var agent = Agent();
        var waiting = f.Broker.ExecuteAsync(agent, "SERVER01", "Admin", new(), _ => Task.CompletedTask, default);
        var command = (await f.Broker.PollAsync(agent, "SERVER01", default))!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Broker.PollAsync(agent, "SERVER02", default));
        var wrong = Reply(command); wrong.ComputerName = "SERVER02";
        Assert.Throws<ArgumentException>(() => f.Broker.Complete(agent, wrong));
        Assert.Equal("SERVER01", f.Broker.Connection(agent)!.ComputerName);
        Assert.Equal(command.Id, (await f.Broker.PollAsync(agent, "SERVER01", default))!.Id);
        f.Broker.Complete(agent, Reply(command)); await waiting;
    }
    [Theory]
    [InlineData("")]
    [InlineData("DOMAIN\\SERVER01")]
    [InlineData("server.example.com")]
    [InlineData("SERVER NAME")]
    public async Task InvalidReportedComputerNamesAreRejected(string computer)
    {
        using var f = new BrokerFixture();
        await Assert.ThrowsAsync<ArgumentException>(() => f.Broker.PollAsync(Agent(), computer, default));
    }
    [Fact]
    public async Task StaleObservedTargetCannotQueueACommandForTheWrongComputer()
    {
        using var f = new BrokerFixture(); var agent = Agent(); using var stop = new CancellationTokenSource();
        var polling = f.Broker.PollAsync(agent, "SERVER02", stop.Token);
        stop.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => polling);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Broker.ExecuteAsync(agent, "SERVER01", "Admin", new(), _ => Task.CompletedTask, default));
    }
    private static LocalGroupsAgent Agent()=>new(){ServerKey="A",CertificateSha256=new('A',64),ConnectionBinding=new('B',64)};
    private static AgentResult Reply(AgentCommand command)=>new(){Id=command.Id,ProtocolVersion=1,ComputerName=command.ComputerName,Success=true,Message="Loaded.",Groups=[]};
    private sealed class Clock:TimeProvider { public DateTimeOffset Now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Now; }
    private sealed class BrokerFixture:IDisposable
    {
        public string Directory{get;}=Path.Combine(Path.GetTempPath(),"MAUI-LG-"+Guid.NewGuid().ToString("N"));
        public Clock Clock{get;}=new();public LocalGroupsBroker Broker{get;}
        public BrokerFixture()=>Broker=new(Clock,new LocalGroupsLedger(Directory),NullLogger<LocalGroupsBroker>.Instance);
        public void Dispose()=>System.IO.Directory.Delete(Directory,true);
    }
}
