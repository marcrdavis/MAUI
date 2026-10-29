using System.Reflection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Features.EnvironmentCopy;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class EnvironmentCopyJournalTests
{
    [Fact]
    public async Task OverwriteRecoveryIsProtectedAndSeparateFromTheRedactedOperationReport()
    {
        using var f=new Fixture();var plan=f.Plan();var source=new CopyObject("Host","1","Source","FTP");
        var item=new CopyPlanItem(source,true){Choice=new(source.Key,"overwrite","Destination","2"),DestinationXml="<FTP ID=\"2\" Name=\"Destination\" DefPassword=\"retained-protected-secret\"/>"};
        await f.Store.CaptureRecoveryAsync(plan,item,CancellationToken.None);
        var report=new CopyReport{OperationId=plan.Id,InitiatedBy="Admin",Source="DEV",Destination="PROD",CompletedUtc=DateTimeOffset.UtcNow,
            Items=[new(source.Key,"Host","1","Source","overwrite","2","Destination","Overwritten","Saved",[],true)]};
        await f.Store.SaveReportAsync(report);
        var files=Directory.GetFiles(Path.Combine(f.Directory,"Data","EnvironmentCopy"));
        foreach(var file in files)Assert.DoesNotContain("retained-protected-secret",await File.ReadAllTextAsync(file));
        var restored=await f.Store.ReadRecoveryAsync(plan.Id,source.Key,"Admin");Assert.Equal(item.DestinationXml,restored.Xml);Assert.Equal("2",restored.TargetId);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Store.ReadRecoveryAsync(plan.Id,source.Key,"Other admin"));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Store.ReadReportAsync(plan.Id,"Other admin"));
    }
    [Fact]
    public async Task StartupRetainsUncertainIdsWithoutReplayingWritesOrClaimingCheckoutOwnership()
    {
        using var f=new Fixture();var id=Guid.NewGuid().ToString("N");
        await f.Store.SaveReportAsync(new(){OperationId=id,InitiatedBy="Admin",Items=[new("Task|1","Task","1","Source","create","900","Copied","Not verified","Pending",[],false,"Protected transfer","Pending")]});
        await f.Store.StartupAsync(CancellationToken.None);
        var report=await f.Store.ReadReportAsync(id,"Admin");Assert.Equal("Interrupted",report.Status);Assert.NotNull(report.CompletedUtc);
        Assert.Equal("900",report.Items[0].DestinationId);Assert.Equal("Unconfirmed after restart",report.Items[0].CheckoutCleanup);
        Assert.False(f.Store.Queue.Reader.TryRead(out _));
    }
    [Fact]
    public async Task InterruptedGroupCreationRetainsPlannedNamesWithoutReplay()
    {
        using var f=new Fixture();var id=Guid.NewGuid().ToString("N");
        await f.Store.SaveReportAsync(new(){OperationId=id,InitiatedBy="Admin",GroupsPlanned=["Ops"],GroupCreationStatus="Not verified"});
        await f.Store.StartupAsync(CancellationToken.None);
        var report=await f.Store.ReadReportAsync(id,"Admin");
        Assert.Equal("Interrupted",report.Status);Assert.Equal(new[]{"Ops"},report.GroupsPlanned);Assert.Equal("Not verified",report.GroupCreationStatus);
        Assert.Empty(report.GroupsCreated);Assert.False(f.Store.Queue.Reader.TryRead(out _));
    }
    [Fact]
    public async Task AReviewedPlanIsQueuedOnceEvenIfStartIsSubmittedTwice()
    {
        using var f=new Fixture();var plan=f.Plan();var source=new CopyObject("DateList","1","Dates","Date List");
        plan.Items.Add(new(source,true){Choice=new(source.Key,"create","Dates")});plan.Review=new(plan.Id);plan.ReviewToken="reviewed";f.Store.Add(plan);
        var coordinator=new EnvironmentCopyCoordinator(null!,null!,EnvironmentCopyTests.Catalog(),f.Store,f.Options);
        await coordinator.StartAsync(new(plan.Id,"reviewed"),"Admin",CancellationToken.None);
        await coordinator.StartAsync(new(plan.Id,"reviewed"),"Admin",CancellationToken.None);
        Assert.True(f.Store.Queue.Reader.TryRead(out var queued));Assert.Same(plan,queued);Assert.False(f.Store.Queue.Reader.TryRead(out _));
    }
    [Fact]
    public async Task StaleReviewAndMissingOverwriteConfirmationCannotQueueWrites()
    {
        using var f=new Fixture();var plan=f.Plan();var source=new CopyObject("Script","1","Script","PS1");
        plan.Items.Add(new(source,true){Choice=new(source.Key,"overwrite","Target","2"),ImpactRevision="reviewed"});plan.DestinationObjects.Add(new("Script","2","Target","PS1"));
        plan.Review=new(plan.Id);plan.ReviewToken="reviewed";f.Store.Add(plan);var coordinator=new EnvironmentCopyCoordinator(null!,null!,EnvironmentCopyTests.Catalog(),f.Store,f.Options);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>coordinator.StartAsync(new(plan.Id,"stale",true),"Admin",CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>coordinator.StartAsync(new(plan.Id,"reviewed",false),"Admin",CancellationToken.None));
        Assert.False(f.Store.Queue.Reader.TryRead(out _));Assert.False(plan.Started);
    }
    [Fact]
    public async Task DiscardRemovesUnstartedPlansAndClearsRetainedKeyBytes()
    {
        using var f=new Fixture();var plan=f.Plan();var bytes=new byte[]{1,2,3};plan.Items.Add(new(new("KeyCertificate","tls|1","Cert","Tls"),true){KeyBytes=bytes,NativeXml="<Certificate/>"});f.Store.Add(plan);
        await f.Store.DiscardAsync(plan.Id,"Admin");Assert.Throws<InvalidOperationException>(()=>f.Store.Get(plan.Id,"Admin"));
        Assert.All(bytes,value=>Assert.Equal((byte)0,value));Assert.Empty(plan.Passphrase);Assert.Empty(plan.Items[0].NativeXml);
    }
    [Fact]
    public async Task DiscardNeverRemovesAnAlreadyStartedOperation()
    {
        using var f=new Fixture();var plan=f.Plan();plan.Started=true;f.Store.Add(plan);await f.Store.DiscardAsync(plan.Id,"Admin");Assert.Same(plan,f.Store.Get(plan.Id,"Admin"));
    }
    [Fact]
    public async Task RetentionAndInvalidIdsAreEnforcedBeforeReadingFiles()
    {
        using var f=new Fixture();var id=Guid.NewGuid().ToString("N");await f.Store.SaveReportAsync(new(){OperationId=id,InitiatedBy="Admin",CompletedUtc=DateTimeOffset.UtcNow});
        File.SetLastWriteTimeUtc(Path.Combine(f.Directory,"Data","EnvironmentCopy",id+".json"),DateTime.UtcNow.AddDays(-31));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Store.ReadReportAsync(id,"Admin"));
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Store.ReadReportAsync("../outside","Admin"));
    }

    [Fact]
    public async Task LivePhaseReadsAreOwnedAndDoNotRequireAJournalRewrite()
    {
        using var f=new Fixture();var plan=f.Plan();plan.Started=true;f.Store.Add(plan);
        var report=new CopyReport{OperationId=plan.Id,InitiatedBy="Admin",Status="Running",TotalObjects=2,Phase="Waiting to start"};
        await f.Store.SaveReportAsync(report);
        var item=new CopyPlanItem(new("Task","1","Daily","Traditional"),true){Choice=new("Task|1","create","Daily Copy")};
        f.Store.UpdateProgress(report,"Copying and verifying object",item);
        report.PhaseStartedUtc=DateTimeOffset.UtcNow.AddSeconds(-8);
        f.Store.UpdateProgress(report,"Copying and verifying object",item);
        plan.CancelRequested=true;
        var live=await f.Store.ReadReportAsync(plan.Id,"Admin");
        Assert.Equal("Copying and verifying object",live.Phase);Assert.Equal("Daily Copy",live.CurrentObjectName);
        Assert.Equal("Task|1",live.CurrentObjectKey);Assert.True(live.PhaseElapsedSeconds>=8);Assert.True(live.CancelRequested);
        Assert.Equal(2,live.TotalObjects);Assert.Equal(0,live.CompletedObjects);
        Assert.DoesNotContain("Copying and verifying object",await File.ReadAllTextAsync(Path.Combine(f.Directory,"Data","EnvironmentCopy",plan.Id+".json")));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Store.ReadReportAsync(plan.Id,"Another admin"));
    }
    [Fact]
    public async Task QueuedStopFinishesWithoutAccessingEnvironmentsAndClearsProtectedMaterial()
    {
        using var f=new Fixture();var plan=f.Plan();plan.Started=true;plan.CancelRequested=true;plan.Review=new(plan.Id,FolderMode:"new",FolderPath:"Do not create",CreateFolder:true);
        var bytes=new byte[]{9,8,7};var source=new CopyObject("Task","1","Daily","Traditional");
        plan.Items.Add(new(source,true){Choice=new(source.Key,"create","Copied"),KeyBytes=bytes});f.Store.Add(plan);
        await f.Store.SaveReportAsync(new(){OperationId=plan.Id,InitiatedBy="Admin",TotalObjects=1});
        var coordinator=new EnvironmentCopyCoordinator(null!,null!,EnvironmentCopyTests.Catalog(),f.Store,f.Options);
        await coordinator.ExecuteAsync(plan,CancellationToken.None);
        var report=await f.Store.ReadReportAsync(plan.Id,"Admin");
        Assert.Equal("Cancelled",report.Status);Assert.Equal("Cancelled",report.Phase);Assert.NotNull(report.CompletedUtc);
        Assert.Equal("Skipped",Assert.Single(report.Items).Status);Assert.Equal(0,report.CompletedObjects);
        Assert.All(bytes,value=>Assert.Equal((byte)0,value));Assert.Empty(plan.Passphrase);
        Assert.Throws<InvalidOperationException>(()=>f.Store.Get(plan.Id,"Admin"));
    }
    [Fact]
    public async Task FinalJournalFailureStillClearsProtectedMaterial()
    {
        using var f=new Fixture();var plan=f.Plan();plan.Started=true;
        var bytes=new byte[]{4,5};var source=new CopyObject("Task","1","Daily","Traditional");
        plan.Items.Add(new(source,true){Choice=new(source.Key,"create","Copied"),KeyBytes=bytes});f.Store.Add(plan);
        await f.Store.SaveReportAsync(new(){OperationId=plan.Id,InitiatedBy="Admin",TotalObjects=1});
        var reads=0;f.Options.OnRead=()=>{
            if(++reads!=2)return;
            var directory=Path.Combine(f.Directory,"Data","EnvironmentCopy");Directory.Delete(directory,true);File.WriteAllText(directory,"unwritable journal location");};
        var coordinator=new EnvironmentCopyCoordinator(null!,null!,EnvironmentCopyTests.Catalog(),f.Store,f.Options);
        await Assert.ThrowsAnyAsync<IOException>(()=>coordinator.ExecuteAsync(plan,CancellationToken.None));
        Assert.All(bytes,value=>Assert.Equal((byte)0,value));Assert.Empty(plan.Passphrase);
        Assert.Throws<InvalidOperationException>(()=>f.Store.Get(plan.Id,"Admin"));
    }
    [Fact]
    public async Task WorkerClearsProtectedMaterialWhenItsInitialJournalReadFails()
    {
        using var f=new Fixture();var plan=f.Plan();plan.Started=true;
        var bytes=new byte[]{1,2};plan.Items.Add(new(new("Task","1","Daily","Traditional"),true){KeyBytes=bytes});f.Store.Add(plan);
        using var services=new ServiceCollection().AddScoped<EnvironmentCopyCoordinator>(_=>new(null!,null!,EnvironmentCopyTests.Catalog(),f.Store,f.Options)).BuildServiceProvider();
        using var worker=new EnvironmentCopyWorker(f.Store,services.GetRequiredService<IServiceScopeFactory>(),NullLogger<EnvironmentCopyWorker>.Instance);
        await f.Store.Queue.Writer.WriteAsync(plan);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            for(var attempt=0;attempt<500&&plan.Passphrase.Length>0;attempt++)await Task.Delay(10);
            Assert.Empty(plan.Passphrase);Assert.All(bytes,value=>Assert.Equal((byte)0,value));
            Assert.Throws<InvalidOperationException>(()=>f.Store.Get(plan.Id,"Admin"));
        }
        finally { await worker.StopAsync(CancellationToken.None); }
    }
    [Theory]
    [InlineData("invalid-json")]
    [InlineData("null-report")]
    [InlineData("null-owner")]
    [InlineData("wrong-id")]
    [InlineData("null-items")]
    [InlineData("null-item")]
    [InlineData("null-skips")]
    [InlineData("null-skip-entry")]
    [InlineData("null-metadata")]
    [InlineData("null-metadata-entry")]
    public async Task OneCorruptJournalCannotPreventReconciliationOfOtherReports(string invalid)
    {
        using var f=new Fixture();var directory=Path.Combine(f.Directory,"Data","EnvironmentCopy");Directory.CreateDirectory(directory);
        var corruptId=new string('0',32);
        var json=System.Text.Json.JsonSerializer.SerializeToNode(new CopyReport{OperationId=corruptId,InitiatedBy="Admin",
            Items=[new("Task|1","Task","1","Daily","create","900","Copied","Not verified","Pending",[])]})!.AsObject();
        if(invalid=="null-owner")json["InitiatedBy"]=null;
        if(invalid=="wrong-id")json["OperationId"]=Guid.NewGuid().ToString("N");
        if(invalid=="null-items")json["Items"]=null;
        if(invalid=="null-item")json["Items"]=new System.Text.Json.Nodes.JsonArray((System.Text.Json.Nodes.JsonNode?)null);
        if(invalid=="null-skips")json["Items"]![0]!["Skipped"]=null;
        if(invalid=="null-skip-entry")json["Items"]![0]!["Skipped"]=new System.Text.Json.Nodes.JsonArray((System.Text.Json.Nodes.JsonNode?)null);
        if(invalid=="null-metadata")json["Items"]![0]!["MetadataNotes"]=null;
        if(invalid=="null-metadata-entry")json["Items"]![0]!["MetadataNotes"]=new System.Text.Json.Nodes.JsonArray((System.Text.Json.Nodes.JsonNode?)null);
        var corrupt=invalid=="invalid-json"?"{invalid":invalid=="null-report"?"null":json.ToJsonString();
        await File.WriteAllTextAsync(Path.Combine(directory,corruptId+".json"),corrupt);
        var id=Guid.NewGuid().ToString("N");await f.Store.SaveReportAsync(new(){OperationId=id,InitiatedBy="Admin",Phase="Copying",TotalObjects=3});
        await f.Store.StartupAsync(CancellationToken.None);
        var report=await f.Store.ReadReportAsync(id,"Admin");
        Assert.Equal("Interrupted",report.Status);Assert.Equal("Interrupted",report.Phase);Assert.NotNull(report.CompletedUtc);
        Assert.Equal(3,report.TotalObjects);Assert.False(f.Store.Queue.Reader.TryRead(out _));
        var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Store.ReadReportAsync(corruptId,"Admin"));
        Assert.Contains("incomplete or unreadable",error.Message);
        Assert.Equal(corrupt,await File.ReadAllTextAsync(Path.Combine(directory,corruptId+".json")));
    }

    [Theory]
    [InlineData(false,"Completed","metadata")]
    [InlineData(true,"Completed with warnings","warning")]
    public async Task RetainedReportsSeparateKnownMetadataDifferencesWithoutHidingWriteFailures(bool writeFailure,string status,string severity)
    {
        using var f=new Fixture();var id=Guid.NewGuid().ToString("N");
        var notices=new List<string>{"Metadata skipped: Metadata field 'Owner' is not configured.","Metadata 'Team' was empty; destination value retained."};
        if(writeFailure)notices.Add("Native configuration saved; a MAUI metadata/audit/folder write failed. Review local properties and folder placement.");
        await f.Store.SaveReportAsync(new(){OperationId=id,InitiatedBy="Admin",CompletedUtc=DateTimeOffset.UtcNow,Status="Completed with skips",Phase="Completed with skips",
            Items=[new("Task|1","Task","1","Source","create","2","Copied","Created","Saved",notices)]});
        var path=Path.Combine(f.Directory,"Data","EnvironmentCopy",id+".json");
        var legacy=System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        legacy["Items"]![0]!.AsObject().Remove("MetadataNotes");legacy["Items"]![0]!.AsObject().Remove("Severity");
        await File.WriteAllTextAsync(path,legacy.ToJsonString());
        var report=await f.Store.ReadReportAsync(id,"Admin");var item=Assert.Single(report.Items);
        Assert.Equal(status,report.Status);Assert.Equal(status,report.Phase);Assert.Equal(severity,item.Severity);
        Assert.True(item.MetadataNotes.Count==2);Assert.True(item.Skipped.Count==(writeFailure?1:0));
        Assert.Contains("not configured",item.MetadataNotes[0]);
        Assert.DoesNotContain("Metadata skipped:",item.MetadataNotes[0]);
        if(writeFailure)Assert.Contains("write failed",item.Skipped[0]);
        Assert.Equal(1,report.CompletedObjects);
    }
    [Fact]
    public async Task PeriodicRetentionRemovesExpiredReportAndRecoveryWithoutRestart()
    {
        using var f = new Fixture(); var plan = f.Plan();
        var source = new CopyObject("Host", "1", "Source", "FTP");
        await f.Store.CaptureRecoveryAsync(plan, new(source, true) { Choice = new(source.Key, "overwrite", "Target", "2"), DestinationXml = "<FTP/>" }, default);
        await f.Store.SaveReportAsync(new() { OperationId = plan.Id, InitiatedBy = "Admin", CompletedUtc = DateTimeOffset.UtcNow });
        var directory = Path.Combine(f.Directory, "Data", "EnvironmentCopy");
        foreach (var file in System.IO.Directory.GetFiles(directory)) File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-31));
        await f.Store.PruneFilesAsync(default);
        Assert.Empty(System.IO.Directory.GetFiles(directory));
    }
    [Fact]
    public async Task PeriodicRetentionKeepsActiveCopiesAndRecentReportRecovery()
    {
        using var f = new Fixture(); var plan = f.Plan(); plan.Started = true; f.Store.Add(plan);
        var directory = Path.Combine(f.Directory, "Data", "EnvironmentCopy");
        await f.Store.SaveReportAsync(new() { OperationId = plan.Id, InitiatedBy = "Admin", Status = "Running" });
        var report = Path.Combine(directory, plan.Id + ".json"); File.SetLastWriteTimeUtc(report, DateTime.UtcNow.AddDays(-31));
        var recovery = Path.Combine(directory, plan.Id + "-test.recovery"); await File.WriteAllTextAsync(recovery, "protected"); File.SetLastWriteTimeUtc(recovery, DateTime.UtcNow.AddDays(-31));
        await f.Store.PruneFilesAsync(default);
        Assert.True(File.Exists(report)); Assert.True(File.Exists(recovery));
        File.SetLastWriteTimeUtc(report, DateTime.UtcNow); f.Store.Forget(plan);
        await f.Store.PruneFilesAsync(default);
        Assert.True(File.Exists(report)); Assert.True(File.Exists(recovery));
        Assert.Equal("Running", System.Text.Json.JsonSerializer.Deserialize<CopyReport>(await File.ReadAllTextAsync(report))!.Status);
    }
    [Fact]
    public async Task PeriodicRetentionUsesLiveSettingAndKeepsUnrecognizedFiles()
    {
        using var f = new Fixture(); var id = Guid.NewGuid().ToString("N");
        await f.Store.SaveReportAsync(new() { OperationId = id, InitiatedBy = "Admin", CompletedUtc = DateTimeOffset.UtcNow });
        var directory = Path.Combine(f.Directory, "Data", "EnvironmentCopy"); var path = Path.Combine(directory, id + ".json");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-5)); await File.WriteAllTextAsync(Path.Combine(directory, "administrator-note.txt"), "keep");
        await f.Store.PruneFilesAsync(default); Assert.True(File.Exists(path));
        f.Options.CurrentValue.RecoveryRetentionDays.Value = 1;
        await f.Store.PruneFilesAsync(default); Assert.False(File.Exists(path)); Assert.True(File.Exists(Path.Combine(directory, "administrator-note.txt")));
    }

    private sealed class Fixture:IDisposable
    {
        public string Directory {get;}=Path.Combine(Path.GetTempPath(),"MAUI-Copy-Journal-"+Guid.NewGuid().ToString("N"));
        public Monitor Options {get;}=new();
        public EnvironmentCopyStore Store {get;}
        public Fixture()
        {
            var environment=DispatchProxy.Create<IWebHostEnvironment,EnvironmentStub>();((EnvironmentStub)(object)environment).Root=Directory;
            Store=new(environment,new EphemeralDataProtectionProvider(),Options);
        }
        public CopyPlan Plan()=>new(){Owner="Admin",Source="DEV",Destination="PROD",IncludeDependencies=true};
        public void Dispose(){if(System.IO.Directory.Exists(Directory))System.IO.Directory.Delete(Directory,true);}
    }
    public class EnvironmentStub:DispatchProxy
    {
        public string Root="";
        protected override object? Invoke(MethodInfo? method,object?[]? args)=>method!.Name=="get_ContentRootPath"?Root:throw new NotSupportedException(method.Name);
    }
    public sealed class Monitor:IOptionsMonitor<EnvironmentCopyOptions>
    {
        private readonly EnvironmentCopyOptions _value=new(){Enabled=new(){Value=true}};
        public Action? OnRead {get;set;}
        public EnvironmentCopyOptions CurrentValue {get{OnRead?.Invoke();return _value;}}
        public EnvironmentCopyOptions Get(string? name)=>CurrentValue;
        public IDisposable? OnChange(Action<EnvironmentCopyOptions,string?> listener)=>null;
    }
}
