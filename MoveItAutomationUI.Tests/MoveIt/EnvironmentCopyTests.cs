using System.Reflection;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using MoveItAutomationUI.Features.EnvironmentCopy;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.Configuration;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class EnvironmentCopyTests
{
    [Fact]
    public void StructuredRemappingPreservesMacrosCodeAndNestedFlow()
    {
        const string xml = """<Task ID="11" Name="Source" Active="1"><Info><Notes>HostID=2 and [TaskID]</Notes></Info><Source HostID="0" Type="FileSystem" FileMask="*.*"/><For ID="14"><Process ScriptID="3"/><Destination HostID="2" Type="SSHFTP" ClientKeyID="4"/><If ID="18"><When><RunTask TaskID="5"/></When></If></For><Schedules><Schedule><DateListRef ID="6" Run="1"/></Schedule></Schedules><Parameters><Parameter Name="PGPRecipientKey" value="0x77"/></Parameters><Group>Old</Group></Task>""";
        var mappings = new Dictionary<string,string> { ["Host|0"]="0",["Host|2"]="22",["Script|3"]="33",["Task|5"]="55",["DateList|6"]="66",["KeyCertificate|ssh|4"]="ssh|44",["KeyCertificate|pgp|0x77"]="pgp|0x88" };
        var result = XElement.Parse(EnvironmentCopyRules.Rewrite(xml,"111","Copied",mappings,["Destination"],true));
        Assert.Equal("0",(string?)result.Attribute("Active")); Assert.Equal("111",(string?)result.Attribute("ID"));
        Assert.Equal("HostID=2 and [TaskID]",result.Element("Info")!.Element("Notes")!.Value);
        Assert.Equal("*.*",(string?)result.Element("Source")!.Attribute("FileMask"));
        Assert.Equal("22",(string?)result.Descendants("Destination").Single().Attribute("HostID"));
        Assert.Equal("44",(string?)result.Descendants("Destination").Single().Attribute("ClientKeyID"));
        Assert.Equal("33",(string?)result.Descendants("Process").Single().Attribute("ScriptID"));
        Assert.Equal("55",(string?)result.Descendants("RunTask").Single().Attribute("TaskID"));
        Assert.Equal("66",(string?)result.Descendants("DateListRef").Single().Attribute("ID"));
        Assert.Equal("14",(string?)result.Element("For")!.Attribute("ID"));
        Assert.Equal("pgp|0x88".Split('|')[1],(string?)result.Descendants("Parameter").Single().Attribute("value"));
        Assert.Equal("Destination",result.Element("Group")!.Value);
        var references = EnvironmentCopyRules.References(xml);
        Assert.Contains(new CopySelection("Host","0"),references); Assert.Contains(new CopySelection("DateList","6"),references);
        Assert.DoesNotContain(new CopySelection("Task","11"),references);
    }
    [Theory]
    [InlineData("tasks","Host","Host")]
    [InlineData("tasks-hosts","Host","PHostS")]
    [InlineData("all","Script","PScriptS")]
    [InlineData("tasks-hosts","Script","Script")]
    [InlineData("tasks","Task","PTaskS")]
    public void NamingScopesApplyOnlyToEligibleNewNames(string scope,string type,string expected)
        => Assert.Equal(expected,EnvironmentCopyRules.NewName(type,type,"P","S",scope,true,new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
    [Fact]
    public void UniqueNamesReserveAgainstDestinationAndOtherRowsWithoutCaseSensitivity()
    {
        var names = new HashSet<string>(["DAILY","Daily (2)"],StringComparer.OrdinalIgnoreCase);
        var first = EnvironmentCopyRules.NewName("Daily","Task","","","tasks",true,names); names.Add(first);
        var second = EnvironmentCopyRules.NewName("Daily","Task","","","tasks",true,names);
        Assert.Equal("Daily (3)",first); Assert.Equal("Daily (4)",second);
        Assert.True(EnvironmentCopyRules.NewName(new string('a',255),"Task","","","tasks",true,new HashSet<string>([new string('a',255)])).Length <= 255);
    }
    [Fact]
    public void SharedDependenciesAreOrderedOnceAndCyclesBlockUntilExplicitReuse()
    {
        var graph = new Dictionary<string,IReadOnlyList<string>> { ["Task|1"]=["Host|1","Script|1"],["Task|2"]=["Host|1"],["Host|1"]=[],["Script|1"]=[] };
        var order = EnvironmentCopyRules.Order(graph).ToList();
        Assert.True(order.IndexOf("Host|1") < order.IndexOf("Task|1")); Assert.True(order.IndexOf("Host|1") < order.IndexOf("Task|2"));
        Assert.True(order.Count(x=>x=="Host|1")==1);
        graph["Task|1"]=["Task|2"]; graph["Task|2"]=["Task|1"];
        Assert.Throws<InvalidOperationException>(()=>EnvironmentCopyRules.Order(graph)); graph["Task|2"]=[];
        Assert.Contains("Task|1",EnvironmentCopyRules.Order(graph));
    }
    [Fact]
    public void AnIdCollisionNeverSelectsAnOverwriteAndReviewRequiresImpact()
    {
        var plan = Plan(); var source = new CopyObject("Task","1","Source","Traditional");
        var item = new CopyPlanItem(source,true) { Choice=new(source.Key,"create","Source"), NativeXml="<Task ID=\"1\" Name=\"Source\"/>" };plan.Items.Add(item);
        plan.DestinationObjects.Add(new("Task","1","Other","Traditional"));
        EnvironmentCopyCoordinator.Validate(plan);Assert.Empty(item.Validation);Assert.Equal("create",item.Choice.Action);
        item.Choice = new(source.Key,"overwrite","Other","1");EnvironmentCopyCoordinator.Validate(plan);
        Assert.Contains(item.Validation,x=>x.Contains("impact",StringComparison.OrdinalIgnoreCase));
        item.ImpactRevision="reviewed";EnvironmentCopyCoordinator.Validate(plan);Assert.Empty(item.Validation);
    }
    [Fact]
    public void BuiltInsAndDefaultFileSystemCanOnlyBeReused()
    {
        var plan=Plan();var builtin=new CopyObject("Script","100001","Built-in","VBScript",BuiltIn:true);
        var item=new CopyPlanItem(builtin,false){Choice=new(builtin.Key,"create",builtin.Name)};plan.Items.Add(item);
        EnvironmentCopyCoordinator.Validate(plan);Assert.NotEmpty(item.Validation);
        plan.DestinationObjects.Add(builtin);item.Choice=new(builtin.Key,"reuse",builtin.Name,builtin.Id);EnvironmentCopyCoordinator.Validate(plan);Assert.Empty(item.Validation);
        var local=new CopyObject("Host","0","(Default)","FileSystem");var host=new CopyPlanItem(local,false){Choice=new(local.Key,"create","New Local")};plan.Items.Add(host);
        EnvironmentCopyCoordinator.Validate(plan);Assert.NotEmpty(host.Validation);
    }
    [Theory]
    [InlineData("Script","100001",true)]
    [InlineData("Host","0",false)]
    public void ReuseOnlyResourcesCannotBeSelectedRenamedOrOverwritten(string type,string id,bool builtIn)
    {
        var resource = new CopyObject(type,id,"System object",type == "Script" ? "VBScript" : "FileSystem",BuiltIn:builtIn);
        Assert.True(resource.ReuseOnly);
        Assert.Throws<InvalidOperationException>(()=>EnvironmentCopyObjects.SelectRoots([new(type,id)],[resource]));
        var plan=Plan();plan.DestinationObjects.Add(resource);
        var item=new CopyPlanItem(resource,false){Choice=new(resource.Key,"create","Renamed")};plan.Items.Add(item);
        EnvironmentCopyCoordinator.Validate(plan);Assert.NotEmpty(item.Validation);
        item.Choice=new(resource.Key,"overwrite",resource.Name,resource.Id);item.ImpactRevision="reviewed";
        EnvironmentCopyCoordinator.Validate(plan);Assert.Contains(item.Validation,x=>x.Contains("only be reused"));
        item.Choice=new(resource.Key,"reuse",resource.Name,resource.Id);EnvironmentCopyCoordinator.Validate(plan);Assert.Empty(item.Validation);
        var view=System.Text.Json.JsonSerializer.SerializeToNode(EnvironmentCopyCoordinator.View(plan))!;
        var row=view["items"]![0]!;
        Assert.False(row["canCreate"]!.GetValue<bool>());Assert.False(row["canRename"]!.GetValue<bool>());Assert.False(row["canOverwrite"]!.GetValue<bool>());
    }
    [Fact]
    public void LocalFileSystemDependencyMapsOnlyToDestinationDefault()
    {
        var local=new CopyObject("Host","0","Local File System","FileSystem");
        Assert.True(EnvironmentCopyObjects.Compatible(local,local with { Name="Destination Local File System" }));
        Assert.False(EnvironmentCopyObjects.Compatible(local,local with { Id="20" }));
    }
    [Fact]
    public void SourceRootsKeepCustomObjectsAndDeduplicateExplicitSelections()
    {
        CopyObject[] inventory=[new("Script","7","Custom","PS1"),new("Task","0","Task","Traditional")];
        var roots=EnvironmentCopyObjects.SelectRoots([new("Script"," 7 "),new("Script","7"),new("Task","0")],inventory);
        Assert.True(roots.Length == 2);Assert.Contains(new CopySelection("Script","7"),roots);Assert.Contains(new CopySelection("Task","0"),roots);
        Assert.Throws<InvalidOperationException>(()=>EnvironmentCopyObjects.SelectRoots([new("Host","missing")],inventory));
    }
    [Fact]
    public async Task DependencyPreviewResolvesNestedSharedAndReuseOnlyReferencesWithoutMetadata()
    {
        CopyObject[] inventory=[new("Task","1","First","Traditional"),new("Task","2","Nested","Advanced"),new("Host","0","Local","FileSystem"),
            new("Host","20","Partner","SSHFTP"),new("Script","100001","Built-in","VBScript",BuiltIn:true),new("KeyCertificate","ssh|9","Key","Ssh")];
        var definitions=new Dictionary<string,string>{
            ["Task|1"]="<Task ID=\"1\"><Source HostID=\"0\"/><Process ScriptID=\"100001\"/><RunTask TaskID=\"2\"/></Task>",
            ["Task|2"]="<Task ID=\"2\"><Source HostID=\"20\"/><Process ScriptID=\"100001\"/></Task>",
            ["Host|0"]="<FileSystem ID=\"0\"/>",["Host|20"]="<SSHFTP ID=\"20\" DefClientKeyID=\"9\"/>",
            ["Script|100001"]="<Script ID=\"100001\"/>",["KeyCertificate|ssh|9"]="<KeyCertificate ID=\"ssh|9\"/>"};
        var reads=new List<string>();
        var graph=await EnvironmentCopyCoordinator.ReadSourceGraphAsync(inventory,[new("Task","1")],obj=>{
            reads.Add(obj.Key);return Task.FromResult(new CopyDefinition(new JsonObject(),definitions[obj.Key],"revision-"+obj.Key));},null,CancellationToken.None);
        Assert.True(graph.Count == 6);Assert.True(reads.Distinct().Count() == 6);
        Assert.Contains(graph,x=>!x.Selected && x.Source.Key == "Host|0" && x.Source.ReuseOnly);
        Assert.Contains(graph,x=>!x.Selected && x.Source.Key == "Script|100001" && x.Source.ReuseOnly);
        Assert.Contains(graph,x=>!x.Selected && x.Source.Key == "KeyCertificate|ssh|9");
        Assert.All(graph,item=>Assert.Null(item.Metadata));
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void GroupCreationPlanIncludesOnlyGroupsThatWillReceiveCopiedObjects(bool preserveOverwrite)
    {
        var plan=Plan();plan.Groups=["EXISTING"];
        void Item(string type,string id,string action,params string[] groups) => plan.Items.Add(new(new(type,id,id,type){AssignedGroups=groups},true){Choice=new(type+"|"+id,action,id)});
        Item("Task","1","create","Existing","Needed");
        Item("Host","2","create","needed");
        Item("Script","3","reuse","Do not create");
        Item("DateList","4","create","Date-only group");
        Item("Host","5","overwrite","Overwrite group");
        var review=new CopyReviewRequest(plan.Id,GroupMode:"create-source",PreserveDestinationGroups:preserveOverwrite);
        var groups=EnvironmentCopyCoordinator.MissingGroups(plan,review);
        Assert.Contains("Needed",groups);Assert.DoesNotContain(groups,name=>name is "Existing" or "Do not create" or "Date-only group");
        Assert.True(groups.Count(name=>name.Equals("needed",StringComparison.OrdinalIgnoreCase)) == 1);
        Assert.Equal(!preserveOverwrite,groups.Contains("Overwrite group"));
        Assert.Empty(EnvironmentCopyCoordinator.MissingGroups(plan,review with {GroupMode="source"}));
    }
    [Fact]
    public void DisablingDependenciesRequiresMappingsInsteadOfSilentlyOmittingThem()
    {
        var plan=Plan(false);var resource=new CopyObject("Script","1","Script","PS1");var item=new CopyPlanItem(resource,false){Choice=new(resource.Key,"create","New")};plan.Items.Add(item);
        EnvironmentCopyCoordinator.Validate(plan);Assert.Contains(item.Validation,x=>x.Contains("Include dependencies is off"));
        plan.DestinationObjects.Add(resource);item.Choice=new(resource.Key,"reuse","Script","1");EnvironmentCopyCoordinator.Validate(plan);Assert.Empty(item.Validation);
    }
    [Fact]
    public void KeyReuseRequiresVerifiedCryptographicIdentityRatherThanNameOrId()
    {
        var source=new CopyObject("KeyCertificate","tls|1","Certificate","Tls",Fingerprint:"AA BB");
        Assert.False(EnvironmentCopyObjects.Compatible(source,new("KeyCertificate","tls|1","Certificate","Tls",Fingerprint:"CC DD")));
        Assert.True(EnvironmentCopyObjects.Compatible(source,new("KeyCertificate","tls|7","Different label","Tls",Fingerprint:"aa:bb")));
        Assert.False(EnvironmentCopyObjects.Compatible(source with { Fingerprint="" },source with { Fingerprint="" }));
    }
    [Fact]
    public void ComparisonMarksSecretsAsNotComparedAndDoesNotEchoTheirValues()
    {
        var fields=EnvironmentCopyRules.RedactedProperties(JsonNode.Parse("""{"Name":"Host","DefPassword":"do-not-show","OAuthClientSecret":"private","Nested":{"AccessToken":"token"},"EditVersion":"opaque","Parameters":{"Parameter":[{"Name":"CustomerCredential","Secure":1,"value":"private-parameter","DisplayValue":"display-secret"}]}}""")!.AsObject());
        Assert.DoesNotContain(fields.Values,x=>x is "do-not-show" or "private" or "token" or "opaque" or "private-parameter" or "display-secret");
        Assert.Equal("Not compared (protected value)",fields["DefPassword"]);Assert.Equal("Host",fields["Name"]);
    }
    [Fact]
    public void ProtectedObjectIsolationRetainsNativeEnvelopeAndOnlyTheIntendedObject()
    {
        const string native="""<Exported Scheme="3"><Tasks><Task ID="1" Name="Task"/></Tasks><Hosts><FTP ID="2" Name="Host" DefPassword="protected-value"/></Hosts><Scripts><Script ID="3" Name="Script"/></Scripts><PGPKeys><PGPKey ID="0x99">protected-key</PGPKey></PGPKeys></Exported>""";
        var result=MauiNativePortabilityService.IsolateCopyObject(native,"Host","2");
        Assert.Contains("<Tasks></Tasks>",result);Assert.Contains("protected-value",result);Assert.DoesNotContain("protected-key",result);Assert.DoesNotContain("Name=\"Script\"",result);
        Assert.Equal("3",(string?)XDocument.Parse(result).Root!.Attribute("Scheme"));
        Assert.Throws<InvalidOperationException>(()=>MauiNativePortabilityService.IsolateCopyObject(native,"Host","missing"));
        var key=MauiNativePortabilityService.IsolateCopyObject(native,"PGPKey","0x99");Assert.Contains("protected-key",key);Assert.DoesNotContain("protected-value",key);
    }
    [Fact]
    public void RevisionIgnoresCheckoutAvailabilityButRejectsConfigurationChanges()
    {
        var before=EnvironmentCopyRules.RevisionXml("<Script ID=\"1\" Name=\"A\" xAddEditDelete=\"1\" UsedByCount=\"2\"><Source>one</Source></Script>");
        var after=EnvironmentCopyRules.RevisionXml("<Script Name=\"A\" ID=\"1\" xAddEditDelete=\"0\" UsedByCount=\"3\"><Source>one</Source></Script>");
        Assert.True(JsonNode.DeepEquals(before,after));
        Assert.False(JsonNode.DeepEquals(before,EnvironmentCopyRules.RevisionXml("<Script ID=\"1\" Name=\"A\"><Source>two</Source></Script>")));
    }
    [Fact]
    public void ReadbackNormalizesDefaultsGroupsAndProtectedValuesButRejectsLostFileMask()
    {
        var expected=JsonNode.Parse("""{"ID":"old","Name":"Task","Group":["A","B"],"Active":0,"steps":[{"Source":{"ID":"11","HostID":"2","FileMask":"*.*","Password":"protected"}}]}""")!.AsObject();
        var saved=JsonNode.Parse("""{"ID":"new","Name":"Task","Group":["B","A"],"Active":false,"steps":[{"Source":{"ID":"11","HostID":"2","FileMask":"*.*","Password":null}}]}""")!.AsObject();
        EnvironmentCopyVerification.Verify(expected,saved,"Task","new","Task");
        saved["steps"]![0]!["Source"]!["FileMask"]="";Assert.Throws<InvalidOperationException>(()=>EnvironmentCopyVerification.Verify(expected,saved,"Task","new","Task"));
    }
    [Theory]
    [InlineData("authenticate")] [InlineData("info")]
    public async Task CancelledConnectionDisposesItsTemporaryScope(string stage)
    {
        var disposed=new ScopeDisposalProbe();
        var rest=Proxy<IMoveItRestClient>((method,_)=>method.Name switch{
            nameof(IMoveItRestClient.AuthenticateAsync)=>stage=="authenticate"
                ? Task.FromException<MoveItSessionState>(new OperationCanceledException())
                : Task.FromResult(new MoveItSessionState{Username="svc-copy"}),
            nameof(IMoveItRestClient.GetInfoAsync)=>Task.FromException<MoveItInfo>(new OperationCanceledException()),
            _=>throw new NotSupportedException(method.Name)});
        var credentials=Proxy<IDataAccessCredentialService>((method,_)=>method.Name==nameof(IDataAccessCredentialService.GetConfiguredForEnvironment)
            ? new DataAccessCredential[]{new("node","DEV","Node","svc-copy","placeholder")} : throw new NotSupportedException(method.Name));
        using var services=new ServiceCollection().AddScoped<ScopeDisposalProbe>(_=>disposed)
            .AddScoped<IMoveItRestClient>(sp=>{sp.GetRequiredService<ScopeDisposalProbe>();return rest;}).BuildServiceProvider();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>CopyEnvironmentConnection.OpenAsync(services.GetRequiredService<IServiceScopeFactory>(),credentials,"DEV",CancellationToken.None));
        Assert.True(disposed.Disposed);
    }
    private sealed class ScopeDisposalProbe:IDisposable { public bool Disposed;public void Dispose()=>Disposed=true; }
    [Fact]
    public async Task ServerRequiresMauiPolicyAndMoveItAdminSeparately()
    {
        var authorization=typeof(EnvironmentCopyController).GetCustomAttribute<AuthorizeAttribute>();Assert.Equal(MauiSecurityPolicies.Admin,authorization!.Policy);
        var session=Proxy<IMoveItSessionService>((method,_)=>method.Name=="Get"?new MoveItSessionState{Info=new MoveItInfo{IsAdmin=false}}:throw new NotSupportedException());
        var controller=new EnvironmentCopyController(null!,null!,session,NullLogger<EnvironmentCopyController>.Instance);
        Assert.Equal(403,Assert.IsType<ObjectResult>(await controller.Catalog()).StatusCode);
        Assert.False(new EnvironmentCopyOptions().Enabled.Value);
    }

    [Theory]
    [InlineData("Task","Traditional",true)]
    [InlineData("Task","Traditional",false)]
    [InlineData("Host","FTP",true)]
    [InlineData("Host","SSHFTP",false)]
    [InlineData("Script","PS1",true)]
    [InlineData("DateList","Date List",false)]
    public void MatchingNamesDefaultToReuseForRootsAndDependencies(string type,string subtype,bool selected)
    {
        var source=new CopyObject(type,"1","Matching name",subtype);
        var target=new CopyObject(type,"20","MATCHING NAME",subtype);
        var choice=EnvironmentCopyObjects.DefaultChoice(source,[target],selected,false);
        Assert.Equal("reuse",choice.Action);Assert.Equal("20",choice.TargetId);Assert.Equal(target.Name,choice.Name);
    }
    [Fact]
    public void DefaultMatchingNeverGuessesFromOnlyAnIdIncompatibleTypeOrAmbiguousName()
    {
        var source=new CopyObject("Host","1","Partner","SSHFTP");
        foreach(var targets in new CopyObject[][] {
            [new("Host","1","Different","SSHFTP")], [new("Host","20","Partner","FTP")],
            [new("Host","20","Partner","SSHFTP"),new("Host","21","PARTNER","SSHFTP")]})
            Assert.Equal("",EnvironmentCopyObjects.DefaultChoice(source,targets,true,true).Action);
        Assert.Equal("create",EnvironmentCopyObjects.DefaultChoice(source,[],true,true).Action);
        Assert.Equal("",EnvironmentCopyObjects.DefaultChoice(source,[],false,false).Action);
        Assert.Equal("create",EnvironmentCopyObjects.DefaultChoice(source,[],false,true).Action);
    }
    [Fact]
    public void DefaultKeyMatchingStillRequiresFingerprintAndPrivateKeyCapability()
    {
        var source=new CopyObject("KeyCertificate","tls|1","Certificate","Tls",Fingerprint:"AA BB",PublicPrivate:"pair");
        Assert.Equal("",EnvironmentCopyObjects.DefaultChoice(source,[source with { Id="tls|2",Fingerprint="CC DD" }],true,true).Action);
        Assert.Equal("",EnvironmentCopyObjects.DefaultChoice(source,[source with { Id="tls|2",PublicPrivate="public" }],true,true).Action);
        var choice=EnvironmentCopyObjects.DefaultChoice(source,[source with { Id="tls|2",Name="Other label",Fingerprint="aa:bb" }],true,true);
        Assert.Equal("reuse",choice.Action);Assert.Equal("tls|2",choice.TargetId);
    }
    [Fact]
    public async Task CopyInventoryReadsCompleteMembershipsThroughSharedRestWithoutCheckout()
    {
        var reads=0;var now=DateTimeOffset.UtcNow;
        var rest=Proxy<IMoveItRestClient>((method,_)=>{
            if(method.Name!=nameof(IMoveItRestClient.GetResourceGroupDefinitionsAsync))throw new NotSupportedException(method.Name);
            reads++;return Task.FromResult<IReadOnlyList<JsonObject>>([JsonNode.Parse("""{"Name":"Finance","Members":[{"t":"task","value":"1"}]}""")!.AsObject()]);});
        using var services=new ServiceCollection()
            .AddSingleton(rest)
            .AddSingleton(Proxy<IAutomationTaskService>((method,_)=>method.Name==nameof(IAutomationTaskService.GetTaskSnapshotAsync)
                ? Task.FromResult(new TaskCacheSnapshot([new(){Id="1",Name="Daily",Group="stale display"}],now)) : throw new NotSupportedException(method.Name)))
            .AddSingleton(Proxy<IAutomationHostService>((method,_)=>method.Name==nameof(IAutomationHostService.GetHostSnapshotAsync)
                ? Task.FromResult(new HostCacheSnapshot([],now)) : throw new NotSupportedException(method.Name)))
            .AddSingleton(Proxy<IAutomationScriptService>((method,_)=>method.Name==nameof(IAutomationScriptService.GetScriptSnapshotAsync)
                ? Task.FromResult(new ScriptCacheSnapshot([],now)) : throw new NotSupportedException(method.Name)))
            .AddSingleton(Proxy<IAutomationDateListService>((method,_)=>method.Name==nameof(IAutomationDateListService.GetSnapshotAsync)
                ? Task.FromResult(new DateListCacheSnapshot([],now)) : throw new NotSupportedException(method.Name)))
            .AddSingleton(Proxy<IAutomationKeyCertificateService>((method,_)=>method.Name==nameof(IAutomationKeyCertificateService.GetSnapshotAsync)
                ? Task.FromResult(new KeyCertificateCacheSnapshot([],now)) : throw new NotSupportedException(method.Name)))
            .BuildServiceProvider();
        var item=Assert.Single(await EnvironmentCopyObjects.InventoryAsync(services,CancellationToken.None));
        Assert.Equal(1,reads);Assert.Equal(new[]{"Finance"},item.AssignedGroups);Assert.Equal("Finance",item.Groups);
    }
    [Fact]
    public void MembershipMappingUsesExactNativeTypeAndIdAndPreservesNamesContainingCommas()
    {
        CopyObject[] objects=[new("Task","1","Task","Traditional",Groups:"stale"),new("Host","1","Host","SSHFTP"),
            new("Script","1","Script","PS1"),new("KeyCertificate","ssh|1","SSH","Ssh"),
            new("KeyCertificate","tls|AA BB","TLS","Tls"),new("KeyCertificate","pgp|0x77","PGP","Pgp"),new("DateList","1","Dates","Date List")];
        JsonObject[] definitions=[
            JsonNode.Parse("""{"Name":"Finance, East","Members":[{"t":"task","value":"1"},{"t":"task","value":"1"},{"t":"host","value":"1"},{"t":"ssl","value":"AA BB"},{"t":"script","value":"missing"}]}""")!.AsObject(),
            JsonNode.Parse("""{"Name":"Operations","Members":[{"t":"TASK","value":1},{"t":"script","value":"1"},{"t":"ssh","value":"1"},{"t":"pgp","value":"0x77"}]}""")!.AsObject(),
            new(){["Name"]="Empty",["Members"]=new JsonArray()}];
        var inventory=EnvironmentCopyObjects.WithAssignments(objects,definitions);
        Assert.Equal(new[]{"Finance, East","Operations"},inventory[0].AssignedGroups);
        Assert.Equal(new[]{"Finance, East"},inventory[1].AssignedGroups);
        Assert.Equal(new[]{"Operations"},inventory[2].AssignedGroups);
        Assert.Equal(new[]{"Operations"},inventory[3].AssignedGroups);
        Assert.Equal(new[]{"Finance, East"},inventory[4].AssignedGroups);
        Assert.Equal(new[]{"Operations"},inventory[5].AssignedGroups);
        Assert.Empty(inventory[6].AssignedGroups);
        Assert.Equal("stale",objects[0].Groups); // Mapping cannot mutate the cached object inventory.
        Assert.Empty(EnvironmentCopyObjects.WithAssignments(objects,[])[0].AssignedGroups);
        Assert.Throws<InvalidOperationException>(()=>EnvironmentCopyObjects.WithAssignments(objects,[new(){["Name"]="Invalid",["Members"]="not an array"}]));
    }
    [Theory]
    [InlineData("create-source",false,true,"Finance, East|OPERATIONS")]
    [InlineData("source",false,true,"OPERATIONS")]
    [InlineData("one",false,true,"Common")]
    [InlineData("none",false,true,"")]
    [InlineData("create-source",true,true,"Destination only")]
    [InlineData("create-source",true,false,"Finance, East|OPERATIONS")]
    public async Task ReviewPlacementAndMissingGroupsUseMembershipsEvenWhenNativeXmlOmitsGroups(string mode,bool overwrite,bool preserve,string expected)
    {
        var inventory=EnvironmentCopyObjects.WithAssignments([new("Task","1","Daily","Traditional")],
            [JsonNode.Parse("""{"Name":"Finance, East","Members":[{"t":"task","value":"1"}]}""")!.AsObject(),
             JsonNode.Parse("""{"Name":"Operations","Members":[{"t":"task","value":"1"}]}""")!.AsObject()]);
        var graph=await EnvironmentCopyCoordinator.ReadSourceGraphAsync(inventory,[new("Task","1")],
            _=>Task.FromResult(new CopyDefinition(new JsonObject(),"<Task ID=\"1\" Name=\"Daily\"/>","revision")),null,CancellationToken.None);
        var plan=Plan();plan.Items.AddRange(graph);plan.Groups=["OPERATIONS","Common"];
        var item=graph[0];item.Choice=new(item.Source.Key,overwrite?"overwrite":"create","Copied",overwrite?"20":"");
        item.DestinationXml="<Task ID=\"20\" Name=\"Copied\"/>";
        item.DestinationGroupsBefore=["Destination only"];
        plan.Review=new(plan.Id,GroupMode:mode,Group:"Common",PreserveDestinationGroups:preserve);
        var view=System.Text.Json.JsonSerializer.SerializeToNode(EnvironmentCopyCoordinator.View(plan))!;
        var placement=view["placement"]![0]!;
        Assert.Equal(expected, string.Join("|",placement["AfterGroups"]!.AsArray().Select(value=>value!.ToString())));
        Assert.Equal("Finance, East|Operations",string.Join("|",placement["SourceGroups"]!.AsArray().Select(value=>value!.ToString())));
        Assert.Equal(overwrite&&preserve,placement["DestinationGroupsPreserved"]!.GetValue<bool>());
        Assert.Equal(overwrite?"Destination only":"",string.Join("|",placement["BeforeGroups"]!.AsArray().Select(value=>value!.ToString())));
        var missing=EnvironmentCopyCoordinator.MissingGroups(plan,plan.Review);
        if(mode=="create-source"&&!(overwrite&&preserve)) Assert.Equal(new[]{"Finance, East"},missing);else Assert.Empty(missing);
    }
    [Fact]
    public void ResourceGroupChangesRequireNewReviewInsteadOfApplyingStaleAssignments()
    {
        EnvironmentCopyCoordinator.RequireUnchangedGroups(["Finance","Operations"],["operations","FINANCE"],"changed");
        Assert.Throws<InvalidOperationException>(()=>EnvironmentCopyCoordinator.RequireUnchangedGroups(["Finance"],[],"source changed"));
        Assert.Throws<InvalidOperationException>(()=>EnvironmentCopyCoordinator.RequireUnchangedGroups(["Finance"],["Finance","Another"],"destination changed"));
    }


    [Theory]
    [InlineData("Task","Traditional")]
    [InlineData("Host","FTP")]
    [InlineData("Script","PS1")]
    public void ObjectGroupArrayOverridesIncompleteInverseInventoryWithoutSplittingNames(string type,string subtype)
    {
        var obj=new CopyObject(type,"1","Object",subtype){AssignedGroups=["Inventory only"]};
        var definition=JsonNode.Parse("""{"Group":["Finance, East","Operations","operations","",null]}""")!.AsObject();
        var resolved=EnvironmentCopyObjects.WithDefinitionAssignments(obj,definition);
        Assert.Equal(new[]{"Finance, East","Operations"},resolved.AssignedGroups);
        Assert.Equal(new[]{"Inventory only"},obj.AssignedGroups);
        Assert.Empty(EnvironmentCopyObjects.WithDefinitionAssignments(resolved,new(){["Group"]=new JsonArray()}).AssignedGroups);
        Assert.Equal(resolved.AssignedGroups,EnvironmentCopyObjects.WithDefinitionAssignments(resolved,new()).AssignedGroups);
        Assert.Throws<InvalidOperationException>(()=>EnvironmentCopyObjects.WithDefinitionAssignments(obj,new(){["Group"]=42}));
    }
    [Fact]
    public async Task TaskGroupShownByInformationSurvivesSparseMemberInventoryIntoMissingGroupPlan()
    {
        // Information reads Task.Group; neither export XML nor the inverse membership projection contains it.
        var inventory=EnvironmentCopyObjects.WithAssignments([new("Task","1","Daily","Traditional",Groups:"Finance")],
            [new(){["Name"]="Finance",["Members"]=new JsonArray()}]);
        Assert.Equal("Finance",inventory[0].Groups);Assert.Empty(inventory[0].AssignedGroups);
        var definition=JsonNode.Parse("""{"ID":"1","Name":"Daily","Group":["Finance"]}""")!.AsObject();
        var graph=await EnvironmentCopyCoordinator.ReadSourceGraphAsync(inventory,[new("Task","1")],
            _=>Task.FromResult(new CopyDefinition(definition,"<Task ID=\"1\" Name=\"Daily\"/>","same")),null,CancellationToken.None);
        var plan=Plan();plan.Items.AddRange(graph);plan.Groups=["Other"];
        graph[0].Choice=new(graph[0].Source.Key,"create","Copied");
        plan.Review=new(plan.Id,GroupMode:"create-source");
        Assert.Equal(new[]{"Finance"},graph[0].Source.AssignedGroups);
        Assert.Equal(new[]{"Finance"},EnvironmentCopyCoordinator.MissingGroups(plan,plan.Review));
        var placement=Assert.Single(EnvironmentCopyCoordinator.Placements(plan));
        Assert.Equal(new[]{"Finance"},placement.AfterGroups);Assert.Equal(new[]{"Finance"},placement.SourceGroups);
        var fresh=EnvironmentCopyObjects.WithDefinitionAssignments(inventory[0],definition);
        EnvironmentCopyCoordinator.RequireUnchangedGroups(graph[0].Source.AssignedGroups,fresh.AssignedGroups,"changed");
        Assert.Throws<InvalidOperationException>(()=>EnvironmentCopyCoordinator.RequireUnchangedGroups(graph[0].Source.AssignedGroups,
            EnvironmentCopyObjects.WithDefinitionAssignments(inventory[0],new(){["Group"]=new JsonArray()}).AssignedGroups,"changed"));
    }
    [Fact]
    public async Task PlacementPreviewUsesSharedRulesWithoutConnectionsAndInvalidatesTheOldReviewToken()
    {
        var settings=new EnvironmentCopyOptions();settings.Enabled.Value=true;
        var options=Proxy<IOptionsMonitor<EnvironmentCopyOptions>>((method,_)=>method.Name=="get_CurrentValue"?settings:throw new NotSupportedException(method.Name));
        var environment=Proxy<IWebHostEnvironment>((method,_)=>method.Name=="get_ContentRootPath"?Path.GetTempPath():throw new NotSupportedException(method.Name));
        var store=new EnvironmentCopyStore(environment,new EphemeralDataProtectionProvider(),options);
        var plan=Plan();plan.Groups=["Existing"];plan.Review=new(plan.Id);plan.ReviewToken="old-token";
        var source=new CopyObject("Task","1","Daily","Traditional"){AssignedGroups=["Finance"]};
        plan.Items.Add(new(source,true){Choice=new(source.Key,"create","Copied")});store.Add(plan);
        // Null authentication dependencies make any unintended environment access fail this test.
        var coordinator=new EnvironmentCopyCoordinator(null!,null!,Catalog(),store,options);
        var preview=System.Text.Json.JsonSerializer.SerializeToNode(await coordinator.PlacementAsync(
            new(plan.Id,GroupMode:"create-source"),"admin",CancellationToken.None))!;
        Assert.Equal("Finance",preview["groupsToCreate"]![0]!.ToString());
        Assert.Equal("Finance",preview["placement"]![0]!["AfterGroups"]![0]!.ToString());
        Assert.Equal("",plan.ReviewToken);Assert.Equal("none",plan.Review.GroupMode);Assert.False(plan.Started);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>coordinator.PlacementAsync(new(plan.Id),"another-user",CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(()=>coordinator.PlacementAsync(new(plan.Id,GroupMode:"unsupported"),"admin",CancellationToken.None));
        plan.Started=true;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>coordinator.PlacementAsync(new(plan.Id),"admin",CancellationToken.None));
    }


    [Fact]
    public void CompletionCountIncludesOnlyVerifiedCopiesAndReusedObjects()
    {
        var report=new CopyReport{TotalObjects=5,CompletedUtc=DateTimeOffset.UtcNow,Status="Stopped",Items=[
            new("Task|1","Task","1","A","create","11","A","Created","",[]),
            new("Host|2","Host","2","B","reuse","22","B","Reused","",[]),
            new("Script|3","Script","3","C","create","","C","Not verified","",[]),
            new("Script|4","Script","4","D","create","","D","Failed","",[]),
            new("Task|5","Task","5","E","create","","E","Skipped","",[])]};
        Assert.Equal(2,report.CompletedObjects);Assert.Equal(5,report.TotalObjects);
    }
    [Fact]
    public void MultipleMatchingKeyIdentitiesRequireAnExplicitResolution()
    {
        var source=new CopyObject("KeyCertificate","ssh|1","Original label","Ssh",Fingerprint:"AA BB");
        var choice=EnvironmentCopyObjects.DefaultChoice(source,[source with{Id="ssh|2",Name="Other label"},source with{Id="ssh|3",Name="Third label"}],true,true);
        Assert.Equal("",choice.Action);Assert.Equal("",choice.TargetId);
    }
    [Theory]
    [InlineData("before-read",0,0)]
    [InlineData("after-read",1,0)]
    [InlineData("after-first-folder",1,1)]
    public async Task StopPreventsTheNextFolderWrite(string stage,int expectedReads,int expectedWrites)
    {
        var plan=Plan();plan.Items.Add(new(new("Task","1","Daily","Traditional"),true){Choice=new("Task|1","create","Copied")});
        plan.Review=new(plan.Id,FolderMode:"new",FolderPath:"Parent/Child",CreateFolder:true);
        var stopped=stage=="before-read";var reads=0;var writes=0;
        var metadata=Proxy<IMetadataRepository>((method,_)=>{
            if(method.Name==nameof(IMetadataRepository.GetFoldersAsync)){reads++;if(stage=="after-read")stopped=true;return Task.FromResult<IReadOnlyList<MetadataFolderRecord>>([]);}
            if(method.Name==nameof(IMetadataRepository.CreateFolderAsync)){writes++;if(stage=="after-first-folder")stopped=true;return Task.FromResult(new MetadataFolderRecord(writes,null,"Parent"));}
            throw new NotSupportedException(method.Name);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>EnvironmentCopyCoordinator.ResolveFolderAsync(metadata,plan,CancellationToken.None,()=>stopped));
        Assert.Equal(expectedReads,reads);Assert.Equal(expectedWrites,writes);
    }

    [Theory]
    [InlineData("unknown-row")]
    [InlineData("invalid-name")]
    [InlineData("invalid-action")]
    public async Task RejectedResolutionLeavesAllChoicesAndTheReviewedPlanUnchanged(string invalid)
    {
        var (coordinator,store)=PlanningCoordinator();var plan=Plan();
        var first=new CopyPlanItem(new("Task","1","First","Traditional"),true){Choice=new("Task|1","reuse","Existing","10"),ImpactRevision="reviewed-impact"};
        var second=new CopyPlanItem(new("Task","2","Second","Traditional"),true){Choice=new("Task|2","create","Second")};
        plan.Items.AddRange([first,second]);plan.Review=new(plan.Id);plan.ReviewToken="reviewed-token";store.Add(plan);
        var original=first.Choice;var originalReview=plan.Review;
        var bad=new CopyChoice(invalid=="unknown-row"?"Task|unknown":"Task|2",invalid=="invalid-action"?"unsupported":"create",invalid=="invalid-name"?new string('x',256):"Second");
        await Assert.ThrowsAsync<ArgumentException>(()=>coordinator.ResolveAsync(new(plan.Id,[new("Task|1","create","Changed"),bad]),"admin",CancellationToken.None));
        Assert.Equal(original,first.Choice);Assert.Equal("reviewed-impact",first.ImpactRevision);
        Assert.Equal("Second",second.Choice.Name);Assert.Same(originalReview,plan.Review);Assert.Equal("reviewed-token",plan.ReviewToken);
        await coordinator.ResolveAsync(new(plan.Id,[new("Task|1","create","Changed"),new("Task|2","create","Second")]),"admin",CancellationToken.None);
        Assert.Equal("Changed",first.Choice.Name);Assert.Empty(first.ImpactRevision);Assert.Null(plan.Review);Assert.Empty(plan.ReviewToken);
    }
    [Theory]
    [InlineData("null-list")]
    [InlineData("null-row")]
    [InlineData("null-action")]
    [InlineData("null-name")]
    public async Task MalformedResolutionDoesNotChangeAnExistingReview(string invalid)
    {
        var (coordinator,store)=PlanningCoordinator();var plan=Plan();
        plan.Items.Add(new(new("Task","1","Daily","Traditional"),true){Choice=new("Task|1","create","Daily")});
        plan.Review=new(plan.Id);plan.ReviewToken="reviewed-token";store.Add(plan);
        IReadOnlyList<CopyChoice> choices=invalid switch {
            "null-list"=>null!,"null-row"=>[null!],"null-action"=>[new("Task|1",null!,"Daily")],_=>[new("Task|1","create",null!)]};
        await Assert.ThrowsAsync<ArgumentException>(()=>coordinator.ResolveAsync(new(plan.Id,choices),"admin",CancellationToken.None));
        Assert.Equal("Daily",plan.Items[0].Choice.Name);Assert.NotNull(plan.Review);Assert.Equal("reviewed-token",plan.ReviewToken);
    }
    [Theory]
    [InlineData("null-list")]
    [InlineData("null-row")]
    [InlineData("empty-id")]
    [InlineData("unknown-type")]
    [InlineData("empty-list")]
    [InlineData("too-many")]
    public async Task InvalidSelectionsAreRejectedBeforeOpeningDataAccessConnections(string invalid)
    {
        var (coordinator,_)=PlanningCoordinator();
        IReadOnlyList<CopySelection> selection=invalid switch {
            "null-list"=>null!,"null-row"=>[null!],"empty-id"=>[new("Task"," ")],"unknown-type"=>[new("unsupported","1")],
            "too-many"=>Enumerable.Range(1,501).Select(id=>new CopySelection("Task",id.ToString())).ToArray(),_=>[]};
        await Assert.ThrowsAsync<ArgumentException>(()=>coordinator.SelectionAsync(new("DEV",selection),CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(()=>coordinator.AnalyzeAsync(new("DEV","PROD",selection),"admin",CancellationToken.None));
    }
    [Fact]
    public async Task MalformedBoundRequestsReturnAnInlineMessageWithoutCallingTheCoordinator()
    {
        var (coordinator,store)=PlanningCoordinator();
        var session=Proxy<IMoveItSessionService>((method,_)=>method.Name=="Get"?new MoveItSessionState{Info=new MoveItInfo{IsAdmin=true}}:throw new NotSupportedException());
        var controller=new EnvironmentCopyController(coordinator,store,session,NullLogger<EnvironmentCopyController>.Instance);
        controller.ModelState.AddModelError("request","Invalid JSON");
        var response=Assert.IsType<BadRequestObjectResult>(await controller.Selection(null!,CancellationToken.None));
        Assert.Contains("missing or invalid",System.Text.Json.JsonSerializer.Serialize(response.Value));
    }
    [Fact]
    public async Task ComparisonCannotReadOrChangeAStartedPlan()
    {
        var (coordinator,store)=PlanningCoordinator();var plan=Plan();plan.Started=true;
        var item=new CopyPlanItem(new("Host","1","Partner","FTP"),true){ImpactRevision="reviewed-impact"};plan.Items.Add(item);store.Add(plan);
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>coordinator.DifferencesAsync(plan.Id,item.Source.Key,"2","admin",CancellationToken.None));
        Assert.Equal("This copy operation has already started.",error.Message);Assert.Equal("reviewed-impact",item.ImpactRevision);
    }
    [Theory]
    [InlineData("Created",false,false,"none")]
    [InlineData("Created",false,true,"metadata")]
    [InlineData("Overwritten",true,false,"warning")]
    [InlineData("Created",true,true,"warning")]
    [InlineData("Failed",false,true,"error")]
    [InlineData("Not verified",true,true,"error")]
    [InlineData("Skipped",false,false,"warning")]
    public void ResultSeverityDistinguishesInformationalMetadataFromWarningsAndErrors(string status,bool warning,bool metadata,string severity)
    {
        var item=new CopyResult("Task|1","Task","1","Source","create","2","Copied",status,"Saved",warning?["Folder assignment failed"]:[])
            {MetadataNotes=metadata?["Owner field is not configured"]:[]};
        Assert.Equal(severity,item.Severity);
        if(status is "Created" or "Overwritten")
        {
            var report=new CopyReport{Items=[item]};report.SetSuccessfulCompletionStatus();
            Assert.Equal(warning?"Completed with warnings":"Completed",report.Status);
        }
    }
    [Theory]
    [InlineData("none",0)]
    [InlineData("audit",1)]
    [InlineData("folder",1)]
    [InlineData("metadata",1)]
    [InlineData("all",1)]
    public async Task LocalFollowUpsSeparateCompatibilityNotesFromTheSpecificFailedWrite(string failure,int expectedWarnings)
    {
        var audit=0;var folders=0;var applied=0;
        var metadata=Proxy<IMetadataRepository>((method,_)=>{
            if(method.Name==nameof(IMetadataRepository.RecordObjectCreatedAsync)){audit++;if(failure is "audit" or "all")throw new IOException("audit failed");return Task.CompletedTask;}
            if(method.Name==nameof(IMetadataRepository.AssignObjectToFolderAsync)){folders++;if(failure is "folder" or "all")throw new IOException("folder failed");return Task.CompletedTask;}
            if(method.Name==nameof(IMetadataRepository.GetObjectMetadataAsync))return Task.FromResult(new ObjectMetadataSnapshot(null,new Dictionary<string,string>(),null,"",null,""));
            throw new NotSupportedException(method.Name);
        });
        var portability=Proxy<IMauiMetadataPortabilityService>((method,_)=>{
            if(method.Name==nameof(IMauiMetadataPortabilityService.ValidateAsync))return Task.FromResult(new MetadataImportSummary(1,2,1,["Owner field is not configured"],true));
            if(method.Name==nameof(IMauiMetadataPortabilityService.ApplyCompatibleAsync)){applied++;if(failure is "metadata" or "all")throw new IOException("metadata failed");return Task.FromResult(1);}
            throw new NotSupportedException(method.Name);
        });
        using var services=new ServiceCollection().AddSingleton(metadata).AddSingleton(portability).BuildServiceProvider();
        var plan=Plan();plan.Review=new(plan.Id);
        var item=new CopyPlanItem(new("Task","1","Daily","Traditional"),true){Choice=new("Task|1","create","Copied"),
            Metadata=new(1,"Task",[new("Owner","Text","Source owner"),new("Team","Text","Operations")])};
        item.MetadataNotes.Add("Owner field is not configured"); // Informational difference already found at Review.
        await EnvironmentCopyCoordinator.SaveLocalPropertiesAsync(services,plan,item,"2",null);
        Assert.True(audit==1);Assert.True(folders==(failure is "audit" or "all"?0:1));
        Assert.True(applied==(failure is "audit" or "folder" or "all"?0:1));Assert.True(item.Skipped.Count==expectedWarnings);
        Assert.Equal("Owner field is not configured",Assert.Single(item.MetadataNotes));
        Assert.DoesNotContain(item.Skipped,message=>message.Contains("not configured",StringComparison.Ordinal));
        if(failure=="audit")Assert.Contains("audit record",item.Skipped[0]);
        if(failure=="folder")Assert.Contains("folder assignment",item.Skipped[0]);
        if(failure=="metadata")Assert.Contains("metadata could not be saved",item.Skipped[0]);
    }
    private static (EnvironmentCopyCoordinator Coordinator,EnvironmentCopyStore Store) PlanningCoordinator()
    {
        var settings=new EnvironmentCopyOptions();settings.Enabled.Value=true;
        var options=Proxy<IOptionsMonitor<EnvironmentCopyOptions>>((method,_)=>method.Name=="get_CurrentValue"?settings:throw new NotSupportedException(method.Name));
        var environment=Proxy<IWebHostEnvironment>((method,_)=>method.Name=="get_ContentRootPath"?Path.GetTempPath():throw new NotSupportedException(method.Name));
        var store=new EnvironmentCopyStore(environment,new EphemeralDataProtectionProvider(),options);
        // Planning validation must not open either environment; missing connection dependencies fail the test if it does.
        return (new(null!,null!,Catalog(),store,options),store);
    }
    internal static IMoveItServerCatalog Catalog() => Proxy<IMoveItServerCatalog>((method,_) =>
        method.Name == nameof(IMoveItServerCatalog.GetServers)
            ? new MoveItServerDefinition[] { new() { Key="DEV" }, new() { Key="PROD" } }
            : throw new NotSupportedException(method.Name));

    [Theory]
    [InlineData(0,false)] [InlineData(1,false)] [InlineData(2,true)]
    public void CopyRequiresDistinctEnvironmentsAndRejectsStaleEnabledSettings(int count, bool expected)
    {
        var connections = Enumerable.Range(0,count).Select(i => new MoveItServerDefinition { Key="node"+i, EnvironmentKey="env"+i }).ToList();
        if (count > 0) connections.Add(new() { Key="ha-node", EnvironmentKey="ENV0" });
        Assert.Equal(expected, EnvironmentCopyAvailability.HasMultipleEnvironments(connections));
        var settings = new EnvironmentCopyOptions(); settings.Enabled.Value=true;
        var options = Proxy<IOptionsMonitor<EnvironmentCopyOptions>>((_,_)=>settings);
        var catalog = Proxy<IMoveItServerCatalog>((_,_)=>connections.ToArray());
        var coordinator = new EnvironmentCopyCoordinator(null!,null!,catalog,null!,options);
        Assert.Equal(expected,coordinator.Enabled);
        connections.Clear();
        Assert.False(coordinator.Enabled); // Availability follows connection changes without restart.
    }

    [Theory]
    [InlineData("",false)] [InlineData("dev",false)] [InlineData("PROD",true)]
    public void SettingsSaveForcesCopyOffUnlessTwoEnvironmentsRemain(string secondEnvironment,bool expected)
    {
        var store=Proxy<IAppSettingsDocumentStore>((method,_)=>method.Name==nameof(IAppSettingsDocumentStore.ReadTemplate)?new JsonObject():throw new NotSupportedException());
        var mutator=new ConfigurationSettingsMutator(null!,store);
        var request=new ConfigurationSettingsSaveRequest { CopyBetweenEnvironments=true,CopyRecoveryRetentionDays=17,
            Servers=[new(){Key="first",EnvironmentKey="DEV"}] };
        if(secondEnvironment.Length>0)request.Servers.Add(new(){Key="second",EnvironmentKey=secondEnvironment});
        var root=new JsonObject();mutator.Apply(root,request);
        Assert.Equal(expected,root["EnvironmentCopy"]!["Enabled"]!["Value"]!.GetValue<bool>());
        Assert.Equal(17,root["EnvironmentCopy"]!["RecoveryRetentionDays"]!["Value"]!.GetValue<int>());
    }

    private static CopyPlan Plan(bool dependencies=true)=>new(){Owner="admin",Source="DEV",Destination="PROD",IncludeDependencies=dependencies};
    private static T Proxy<T>(Func<MethodInfo,object?[],object?> callback) where T:class { var proxy=DispatchProxy.Create<T,Stub>();((Stub)(object)proxy).Callback=callback;return proxy; }
    public class Stub:DispatchProxy { public Func<MethodInfo,object?[],object?> Callback=null!;protected override object? Invoke(MethodInfo? method,object?[]? args)=>Callback(method!,args??[]); }
}
