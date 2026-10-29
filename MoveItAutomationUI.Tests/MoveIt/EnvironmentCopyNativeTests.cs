using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Features.EnvironmentCopy;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Tasks;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class EnvironmentCopyNativeTests
{
    [Fact]
    public async Task NewTaskUsesProtectedImportAndRecordsAssignedIdBeforeVerification()
    {
        var f = new Fixture(); var known = new List<string>();
        var result = await f.Service.CopyAsync(f.Request(),id=>{known.Add(id);return Task.CompletedTask;});
        Assert.Equal("900",result.Id);Assert.Equal(new[]{"900"},known);Assert.Equal(1,f.Imports);Assert.Equal(0,f.Acquires);Assert.Equal(0,f.Releases);
        Assert.Equal("Portable-copy-passphrase1!",f.Passphrase);
        Assert.Equal("0",f.Imported!["Tasks"]!["Task"]![0]!["Active"]!.ToString());
        Assert.True(f.Imported["Tasks"]!["Task"]![0]!["new"]!.GetValue<bool>());
        Assert.Equal("protected-host-secret",f.Imported["Tasks"]!["Task"]![0]!["steps"]![0]!["Source"]!["Password"]!.ToString());
    }
    [Fact]
    public async Task ChangedOverwriteRevisionIsDetectedUnderCheckoutAndReleasesOnlyItsOwnCheckout()
    {
        var f=new Fixture{Existing=true,ChangeOnCheckout=true};var before=f.Xml;
        var request=f.Request("10") with { ExpectedRevision=ObjectEditVersion.Create("EnvironmentCopy","Task|10",EnvironmentCopyRules.RevisionXml(before)) };
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.CopyAsync(request,_=>Task.CompletedTask));
        Assert.Equal(1,f.Acquires);Assert.Equal(1,f.Releases);Assert.Equal(0,f.Imports);
    }
    [Fact]
    public async Task RejectedCheckoutIsNeverReleasedOrUsedForImport()
    {
        var f=new Fixture{Existing=true,RejectCheckout=true};
        await Assert.ThrowsAsync<ObjectCheckoutRejectedException>(()=>f.Service.CopyAsync(f.Request("10"),_=>Task.CompletedTask));
        Assert.Equal(1,f.Acquires);Assert.Equal(0,f.Releases);Assert.Equal(0,f.Imports);
    }
    [Fact]
    public async Task AnOpenEditorReservationBlocksCopyUsingTheSameServerScope()
    {
        var f=new Fixture{Existing=true};
        using var server=ObjectCheckout.ForServer(f.State.WebAdminUrl);
        using var reserved=await ObjectCheckout.ReserveAsync("Task","10","other-editor",CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.CopyAsync(f.Request("10"),_=>Task.CompletedTask));
        Assert.Equal(0,f.Acquires);Assert.Equal(0,f.Releases);Assert.Equal(0,f.Imports);
    }
    [Fact]
    public async Task LostNativeImportResponseIsNeverReplayed()
    {
        var f=new Fixture{LoseResponse=true};
        await Assert.ThrowsAsync<NativePortabilityImportOutcomeUnknownException>(()=>f.Service.CopyAsync(f.Request(),_=>Task.CompletedTask));
        Assert.Equal(1,f.Imports);Assert.True(f.Created);
    }
    [Fact]
    public async Task ANewNameConflictDoesNotBecomeAnOverwrite()
    {
        var f=new Fixture{Existing=true};
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Service.CopyAsync(f.Request(),_=>Task.CompletedTask));
        Assert.Equal(0,f.Imports);Assert.Equal(0,f.Acquires);
    }
    private sealed class Fixture
    {
        public bool Existing,Created,ChangeOnCheckout,RejectCheckout,LoseResponse;
        public int Acquires,Releases,Imports;
        public string Passphrase="";
        public JsonObject? Imported;
        public MoveItSessionState State=new(){ServerKey="COPY-"+Guid.NewGuid().ToString("N"),EnvironmentKey="DEST",WebAdminUrl="https://"+Guid.NewGuid().ToString("N")+".test",AccessToken="test",Info=new MoveItInfo{IsAdmin=true}};
        public string Xml=>"<Task ID=\"10\" Name=\"Copied\" Active=\"0\"><Info><Notes>"+(ChangeOnCheckout&&Acquires>0?"changed":"old")+"</Notes></Info></Task>";
        public MauiNativePortabilityService Service {get;}
        public NativeEnvironmentCopyRequest Request(string target="")=>new("Task","1","<Exported><Tasks><Task ID=\""+(target.Length>0?target:"1")+"\" Name=\"Copied\" Active=\"0\"/></Tasks><Hosts></Hosts></Exported>","Portable-copy-passphrase1!","Copied",target,"",[],false);
        public Fixture()
        {
            var rest=Proxy<IMoveItRestClient>((method,args)=>
            {
                switch(method.Name)
                {
                    case nameof(IMoveItRestClient.CheckoutTaskV0Async):Acquires++;if(RejectCheckout)throw new ObjectCheckoutRejectedException("Already checked out");return Task.CompletedTask;
                    case nameof(IMoveItRestClient.ReleaseTaskCheckoutV0Async):Releases++;return Task.CompletedTask;
                    case nameof(IMoveItRestClient.EncodeNativeImportAsync):return Task.FromResult(JsonNode.Parse("""{"Tasks":{"Task":[{"ID":"1","Name":"Copied","Active":0,"Group":[],"steps":[{"Source":{"ID":"11","HostID":"22","FileMask":"*.*","Password":"protected-host-secret"}}]}]},"Hosts":{"hosts":[]},"Duplicated":[]}""")!.AsObject());
                    case nameof(IMoveItRestClient.ImportNativeConfigurationAsync):Imports++;Imported=(JsonObject)((JsonObject)args[0]!).DeepClone();Passphrase=(string)args[1]!;Created=true;if(LoseResponse)throw new HttpRequestException("Lost response");return Task.CompletedTask;
                    default:throw new NotSupportedException(method.Name);
                }
            });
            var crud=Proxy<ITaskCrudCoordinator>((method,_)=>method.Name switch
            {
                nameof(ITaskCrudCoordinator.GetNativeConfigurationAsync)=>Task.FromResult(new MoveItTaskConfiguration("10","Copied",Xml)),
                nameof(ITaskCrudCoordinator.GetDefinitionAsync)=>Task.FromResult(JsonNode.Parse("""{"ID":"900","Name":"Copied","Active":false,"Group":[],"steps":[{"Source":{"ID":"11","HostID":"22","FileMask":"*.*","Password":null}}]}""")!.AsObject()),
                _=>throw new NotSupportedException(method.Name)
            });
            var tasks=Proxy<IAutomationTaskService>((method,_)=>method.Name==nameof(IAutomationTaskService.GetTaskSnapshotAsync)
                ?Task.FromResult(new TaskCacheSnapshot(Existing||Created?[new AutomationTask{Id=Created?"900":"10",Name="Copied",Type="Traditional"}]:[],DateTimeOffset.UtcNow)):throw new NotSupportedException(method.Name));
            var hosts=Proxy<IAutomationHostService>((method,_)=>Task.FromResult(new HostCacheSnapshot([],DateTimeOffset.UtcNow)));
            var scripts=Proxy<IAutomationScriptService>((method,_)=>Task.FromResult(new ScriptCacheSnapshot([],DateTimeOffset.UtcNow)));
            var dates=Proxy<IAutomationDateListService>((method,_)=>Task.FromResult(new DateListCacheSnapshot([],DateTimeOffset.UtcNow)));
            var keys=Proxy<IAutomationKeyCertificateService>((method,_)=>Task.FromResult(new KeyCertificateCacheSnapshot([],DateTimeOffset.UtcNow)));
            var session=Proxy<IMoveItSessionService>((method,_)=>State);
            Service=new(rest,crud,tasks,hosts,scripts,dates,keys,null!,null!,null!,null!,null!,null!,session,NullLogger<MauiNativePortabilityService>.Instance);
        }
    }
    private static T Proxy<T>(Func<MethodInfo,object?[],object?> callback) where T:class{var proxy=DispatchProxy.Create<T,Stub>();((Stub)(object)proxy).Callback=callback;return proxy;}
    public class Stub:DispatchProxy{public Func<MethodInfo,object?[],object?> Callback=null!;protected override object? Invoke(MethodInfo? method,object?[]? args)=>Callback(method!,args??[]);}
}
