using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Controllers;
using System.Reflection;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Features.Editors.Hosts;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Tests.MoveIt;

// Mock native transports: verify save behavior without pretending to exercise live MOVEit COM.
public sealed class HostSharedCrudTests
{
    [Theory]
    [InlineData(false, false, 401)]
    [InlineData(true, false, 403)]
    [InlineData(true, true, 200)]
    public async Task HostBrowseRequiresLiveNativeAdminAndUsesRouteIdentity(bool authenticated, bool admin, int status)
    {
        var calls = 0;
        var session = Stub<IMoveItSessionService>((_, _) => authenticated
            ? new MoveItSessionState { Username = "Marc", Info = new MoveItInfo { IsAdmin = admin } } : null);
        var browse = Stub<IAutomationHostBrowseService>((method, args) =>
        {
            calls++;
            Assert.Equal(nameof(IAutomationHostBrowseService.BrowseAsync), method.Name);
            Assert.Equal("42", args[0]); Assert.Equal("/folder", args[1]);
            Assert.Equal("", args[5]); // Inspection must not invent a Task context.
            return Task.FromResult(new AutomationHostBrowseResult("42", "Saved Host", "FTP", "/folder", "/",
                new[] { new AutomationHostBrowseItem("file.txt", "/folder/file.txt", false, Size: "1024", LastModified: "2026-10-19") }));
        });
        using var provider = new ServiceCollection().AddSingleton(browse).BuildServiceProvider();
        var controller = new HostEditorController(null!, null!, null!, null!, null!, null!, session,
            new EditorOptions<MetadataUiOptions>(new()), NullLogger<HostEditorController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = provider } } };
        var result = await controller.Browse("42", new AutomationHostBrowseRequest { HostId = "different-host", Directory = "/folder" }, default);
        if (status != 200) Assert.Equal(status, Assert.IsAssignableFrom<ObjectResult>(result).StatusCode);
        else
        {
            var listing = Assert.IsType<AutomationHostBrowseResult>(Assert.IsType<JsonResult>(result).Value);
            Assert.Equal("1024", listing.Items[0].Size);
            Assert.Equal("2026-10-19", listing.Items[0].LastModified);
        }
        Assert.Equal(status == 200 ? 1 : 0, calls);
        Assert.True(typeof(HostEditorController).GetMethod(nameof(HostEditorController.Browse))!
            .IsDefined(typeof(ValidateAntiForgeryTokenAttribute)));
    }

    [Theory]
    [InlineData(false, false, false, false, true)]
    [InlineData(true, false, false, false, true)]
    [InlineData(true, true, false, false, true)]
    [InlineData(false, false, false, true, true)]
    [InlineData(false, false, false, false, false)]
    public async Task EditorUndoStartsEmptyRegardlessOfStoredGridHistory(bool history, bool local, bool expected, bool readOnly, bool configurationEditable)
    {
        var host = new HostEditorShellViewModel().Host with { Id = local ? "0" : "42", CanEditDelete = true, CanEditConfiguration = configurationEditable };
        var editor = Stub<IHostEditorService>((_, _) => Task.FromResult(host));
        var session = Stub<IMoveItSessionService>((_, _) => new MoveItSessionState { Username = "Marc", EnvironmentKey = "test" });
        var metadata = Stub<IMetadataRepository>((_, _) => Task.FromResult(new ObjectMetadataSnapshot(null, new Dictionary<string, string>(), null, "", null, "")));
        var undo = Stub<IHostUndoService>((method, args) =>
        {
            Assert.Equal(nameof(IHostUndoService.GetAvailableHostIdsAsync), method.Name);
            Assert.Equal("Marc", args[0]);
            return Task.FromResult<IReadOnlySet<string>>(history ? new HashSet<string> { host.Id } : new HashSet<string>());
        });
        var services = new ServiceCollection();
        services.AddSingleton(Stub<IMoveItObjectAccessService>((method, _) => method.Name == nameof(IMoveItObjectAccessService.GetObjectAsync)
            ? Task.FromResult(new MoveItObjectAccess(!readOnly, !readOnly, false)) : throw new NotSupportedException(method.Name)));
        services.AddSingleton<IOptionsSnapshot<UiOptions>>(new EditorOptions<UiOptions>(new()));
        services.AddSingleton(Stub<IUserPreferencesService>((_, _) => Task.FromResult(new HostEditorPreferences(false))));
        using var provider = services.BuildServiceProvider();
        var controller = new HostEditorController(editor, null!, null!, metadata, undo, null!, session,
            new EditorOptions<MetadataUiOptions>(new()), NullLogger<HostEditorController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = provider } } };
        var result = Assert.IsType<JsonResult>(await controller.Shell(host.Id, default));
        var shell = Assert.IsType<HostEditorShellViewModel>(result.Value);
        Assert.Equal(expected, shell.CanUndo);
        Assert.Equal(readOnly || !configurationEditable, shell.ReadOnly);
        if (shell.ReadOnly) { Assert.False(shell.Host.CanEditMoveItFields); Assert.False(shell.Host.CanEditConfiguration); Assert.False(shell.CheckoutObjectsForDurationOfEdit); }
    }
    private sealed class EditorOptions<T>(T value) : IOptionsSnapshot<T> where T : class
    {
        public T Value => value;
        public T Get(string? name) => value;
    }

    [Fact]
    public async Task LocalFileSystemCapturesRoundTripThroughTheSharedCheckedOutRestSave()
    {
        var f = new Fixture("FileSystem");
        var patches = new[]
        {
            new JsonObject { ["DefRetryCount"] = "5", ["DefRetryTimeoutSecs"] = "45", ["UseNotif"] = true, ["DefRescanSecs"] = "1" },
            new JsonObject { ["RenameAfterUpload"] = true, ["TempUploadName"] = "CTMP[Rnd:4]", ["DefPreserveFileTimestamp"] = true },
            new JsonObject { ["DefMxFi"] = "100", ["DefMxBy"] = "1024000" },
            new JsonObject { ["DelOldStateDays"] = "1", ["UseDefStateCaching"] = false, ["StateCacheTime"] = "10", ["StateCacheUnit"] = "Minutes", ["AdjDST"] = false }
        };
        foreach (var patch in patches)
        {
            var result = await f.Coordinator.SaveDefinitionAsync("0", new("(Default)", "Desc", patch), "tester", f.Live);
            Assert.True(result.Changed); Assert.Equal("FileSystem", result.TypeCode);
        }
        Assert.Equal("0", f.Submitted!["ID"]!.GetValue<string>());
        Assert.Equal("(Default)", f.Submitted["Name"]!.GetValue<string>());
        Assert.Equal("Desc", f.Saved["Desc"]!.GetValue<string>());
        foreach (var (key, expected) in new[] { ("DefRetryCount", 5L), ("DefRetryTimeoutSecs", 45L), ("DefRescanSecs", 1L),
            ("DefMxFi", 100L), ("DefMxBy", 1024000L), ("DelOldStateDays", 1L) })
            Assert.Equal(expected, f.Saved[key]!.GetValue<long>());
        foreach (var key in new[] { "UseNotif", "RenameAfterUpload", "DefPreserveFileTimestamp" }) Assert.Equal(1, f.Saved[key]!.GetValue<int>());
        Assert.Equal(0, f.Saved["AdjDST"]!.GetValue<int>()); Assert.Equal(0, f.Saved["UseDefStateCaching"]!.GetValue<int>());
        Assert.Equal(10, f.Saved["StateCacheTime"]!.GetValue<int>()); Assert.Equal("Minutes", f.Saved["StateCacheUnit"]!.GetValue<string>());
        Assert.Equal("CTMP[Rnd:4]", f.Saved["TempUploadName"]!.GetValue<string>());
        Assert.Equal("Keep", f.Saved["NativeOnly"]!["Value"]!.GetValue<string>());
        Assert.False(f.Saved["OverridesNoPerm"]!.GetValue<bool>()); Assert.Empty(f.Saved["Group"]!.AsArray());
        Assert.Equal(4, f.Checkouts); Assert.Equal(4, f.Writes); Assert.Equal(4, f.Releases); Assert.Equal(4, f.Undo);
        Assert.False((await f.Coordinator.SaveDefinitionAsync("0", new("(Default)", "Desc", patches[^1]), "tester", f.Live with
            { Hosts = new[] { new AutomationHost { Id = "0", Name = "(Default)", Description = "Desc", TypeCode = "FileSystem", CanEditDelete = true } } })).Changed);
        Assert.Equal(4, f.Writes);
    }

    [Theory]
    [InlineData("Host", "server")]
    [InlineData("DefPassword", "secret")]
    [InlineData("ID2", "42")]
    [InlineData("DefConnTimeoutSecs", "30")]
    [InlineData("DefRetryCount", "-1")]
    [InlineData("UseNotif", "invalid")]
    public async Task LocalFileSystemRejectsUncapturedOrInvalidSettingsBeforeCheckout(string key, string value)
    {
        var f = new Fixture("FileSystem");
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Coordinator.SaveDefinitionAsync("0",
            new("(Default)", "Old", new JsonObject { [key] = value }), "tester", f.Live));
        Assert.Equal(0, f.Writes); Assert.Equal(0, f.Checkouts); Assert.Equal(0, f.Undo);
    }

    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task LocalFileSystemCannotRenameThroughEitherSaveEntryPoint(bool informationOnly)
    {
        var f = new Fixture("FileSystem");
        await Assert.ThrowsAsync<InvalidOperationException>(() => informationOnly
            ? f.Coordinator.UpdateBasicDetailsAsync("0", "Renamed", "Desc")
            : f.Coordinator.SaveDefinitionAsync("0", new("Renamed", "Desc", new JsonObject()), "tester", f.Live));
        Assert.Equal(0, f.Checkouts); Assert.Equal(0, f.Writes);
    }

    [Fact]
    public async Task LocalFileSystemDescriptionUsesTheSameInformationRestSave()
    {
        var f = new Fixture("FileSystem");
        await f.Coordinator.UpdateBasicDetailsAsync("0", "(Default)", "Desc");
        Assert.Equal("Desc", f.Saved["Desc"]!.GetValue<string>());
        Assert.Equal(1, f.Checkouts); Assert.Equal(1, f.Writes); Assert.Equal(1, f.Releases);
    }

    [Fact]
    public async Task LocalFileSystemPermissionAndRevisionChecksStillPreventWrites()
    {
        var denied = new Fixture("FileSystem", false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => denied.Coordinator.SaveDefinitionAsync("0",
            new("(Default)", "Desc", new JsonObject { ["UseNotif"] = true }), "tester", denied.Live));
        Assert.Equal(0, denied.Checkouts); Assert.Equal(0, denied.Writes);
        var f = new Fixture("FileSystem") { NativeXml = "<FileSystem ID='0' DefRetryCount='3' />" };
        var revision = ObjectEditVersion.Create("Host", "0", ObjectEditVersion.Definition(f.Saved, f.NativeXml));
        f.NativeXml = "<FileSystem ID='0' DefRetryCount='5' />";
        using var edit = ObjectEditVersion.Require("Host", "0", revision);
        await Assert.ThrowsAsync<ObjectEditConflictException>(() => f.Coordinator.SaveDefinitionAsync("0",
            new("(Default)", "Desc", new JsonObject()), "tester", f.Live));
        Assert.Equal(0, f.Writes); Assert.Equal(1, f.Releases);
    }

    [Fact]
    public async Task LocalFileSystemReadbackFailureRetainsTheSharedPartialSaveClassification()
    {
        var f = new Fixture("FileSystem") { IgnoreDescription = true };
        var error = await Assert.ThrowsAsync<HostPartialSaveException>(() => f.Coordinator.SaveDefinitionAsync("0",
            new("(Default)", "Desc", new JsonObject()), "tester", f.Live));
        Assert.Equal("0", error.HostId); Assert.Equal("readback", error.VerificationStage);
        Assert.Equal(1, f.Writes); Assert.Equal(1, f.Releases);
    }

    [Theory]
    [InlineData("Share", HttpStatusCode.BadRequest)]
    [InlineData("siLock", HttpStatusCode.Forbidden)]
    [InlineData("SSHFTP", HttpStatusCode.UnprocessableEntity)]
    public async Task DefinitivePutRejectionsReleaseCheckoutAndKeepTheDraftRetryableWithoutLeakingSecrets(string type, HttpStatusCode status)
    {
        var fixture = new Fixture(type) { WriteFailure = new MoveItApiException("Native error REPLACEMENT_SECRET", status) };
        var error = await Assert.ThrowsAsync<MoveItApiException>(() => fixture.Coordinator.SaveDefinitionAsync("42",
            new HostDefinitionSaveRequest("Renamed", "Old", new JsonObject()), "tester", fixture.Live));
        Assert.Equal(status, error.StatusCode); Assert.DoesNotContain("REPLACEMENT_SECRET", error.ToString());
        Assert.Equal("Host", fixture.Saved["Name"]!.ToString());
        Assert.Equal(1, fixture.Writes); Assert.Equal(1, fixture.Releases);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout, false)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    [InlineData(HttpStatusCode.UnprocessableEntity, true)]
    public async Task LostAcknowledgementsOrFailedCleanupStillRequireReconciliation(HttpStatusCode status, bool releaseFailure)
    {
        var fixture = new Fixture("SSHFTP") { WriteFailure = new MoveItApiException("REPLACEMENT_SECRET", status), FailRelease = releaseFailure };
        var error = await Assert.ThrowsAsync<HostPartialSaveException>(() => fixture.Coordinator.SaveDefinitionAsync("42",
            new HostDefinitionSaveRequest("Renamed", "Old", new JsonObject()), "tester", fixture.Live));
        Assert.DoesNotContain("REPLACEMENT_SECRET", error.ToString());
        Assert.Equal(1, fixture.Writes); Assert.Equal(1, fixture.Releases);
    }

    [Theory]
    [InlineData("Share")]
    [InlineData("siLock")]
    public async Task OlderRestProfilesTreatFailedPutAcknowledgementsAsUncertain(string type)
    {
        var fixture = new Fixture(type) { FailWrite = true };
        var error = await Assert.ThrowsAsync<HostPartialSaveException>(() => fixture.Coordinator.SaveDefinitionAsync("42",
            new HostDefinitionSaveRequest("Renamed", "Old", new JsonObject()), "tester", fixture.Live));
        Assert.Equal("42", error.HostId);
        Assert.DoesNotContain("REPLACEMENT_SECRET", error.ToString());
        Assert.Equal(1, fixture.Writes);
        Assert.Equal(1, fixture.Releases);
    }

    [Fact]
    public async Task FtpDirectoryModeChangesUseTheExistingCheckoutAndVerificationLifecycle()
    {
        var fixture = new Fixture("FTP");
        fixture.Saved["CustomDir"] = 1;
        foreach (var key in HostFtpDirectoryParsing.Columns) fixture.Saved[key] = 1;
        await fixture.Coordinator.SaveDefinitionAsync("42", new("Host", "Old", new JsonObject
        { [HostFtpDirectoryParsing.ModeKey] = "script", ["DirScriptID"] = "100401", ["Secure"] = "on-ccc" }), "tester", fixture.Live);
        Assert.Equal("100401", fixture.Submitted!["DirScriptID"]!.GetValue<string>());
        Assert.Equal("on-ccc", fixture.Submitted["Secure"]!.GetValue<string>());
        Assert.False(fixture.Submitted.ContainsKey("CustomDir"));
        Assert.False(fixture.Submitted.ContainsKey(HostFtpDirectoryParsing.ModeKey));
        foreach (var key in HostFtpDirectoryParsing.Columns) Assert.False(fixture.Submitted.ContainsKey(key));
        Assert.Equal(1, fixture.Checkouts); Assert.Equal(1, fixture.Writes); Assert.Equal(1, fixture.Releases);
        await fixture.Coordinator.SaveDefinitionAsync("42", new("Host", "Old", new JsonObject
        { [HostFtpDirectoryParsing.ModeKey] = "automatic" }), "tester", fixture.Live);
        Assert.False(fixture.Submitted!.ContainsKey("DirScriptID"));
        Assert.Equal(2, fixture.Checkouts); Assert.Equal(2, fixture.Writes); Assert.Equal(2, fixture.Releases);
        var unavailable = new Fixture("FTP");
        await Assert.ThrowsAsync<InvalidOperationException>(() => unavailable.Coordinator.SaveDefinitionAsync("42", new("Host", "Old", new JsonObject
        { [HostFtpDirectoryParsing.ModeKey] = "script", ["DirScriptID"] = "999" }), "tester", unavailable.Live));
        Assert.Equal(0, unavailable.Checkouts); Assert.Equal(0, unavailable.Writes);
    }

    [Theory]
    [InlineData("Share", false)]
    [InlineData("Share", true)]
    [InlineData("siLock", false)]
    [InlineData("siLock", true)]
    [InlineData("SSHFTP", false)]
    [InlineData("SSHFTP", true)]
    public async Task CheckoutIdentityMismatchCannotWriteAndStillReleases(string type, bool wrongType)
    {
        var fixture = new Fixture(type) { CheckoutId = wrongType ? null : "99", CheckoutType = wrongType ? "SMTP" : null };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Coordinator.SaveDefinitionAsync("42",
            new HostDefinitionSaveRequest("Renamed", "Old", new JsonObject()), "tester", fixture.Live));
        Assert.Equal(1, fixture.Checkouts);
        Assert.Equal(1, fixture.Releases);
        Assert.Equal(0, fixture.Writes);
    }

    [Theory]
    [InlineData("Share")]
    [InlineData("siLock")]
    [InlineData("SSHFTP")]
    public async Task InitialHostIdentityMismatchCannotCaptureUndoOrWrite(string type)
    {
        var fixture = new Fixture(type);
        fixture.Saved["ID"] = "99";
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Coordinator.SaveDefinitionAsync("42",
            new HostDefinitionSaveRequest("Renamed", "Old", new JsonObject()), "tester", fixture.Live));
        Assert.Equal(0, fixture.Undo);
        Assert.Equal(0, fixture.Checkouts);
        Assert.Equal(0, fixture.Writes);
    }

    [Theory]
    [InlineData("Share")]
    [InlineData("siLock")]
    [InlineData("SSHFTP")]
    public async Task WrongReadbackIdentityIsAPartialSave(string type)
    {
        var fixture = new Fixture(type) { CorruptReadbackId = true };
        var error = await Assert.ThrowsAsync<HostPartialSaveException>(() => fixture.Coordinator.SaveDefinitionAsync("42",
            new HostDefinitionSaveRequest("Renamed", "Old", new JsonObject()), "tester", fixture.Live));
        Assert.Equal("42", error.HostId);
        Assert.Equal(1, fixture.Writes);
        Assert.Equal(1, fixture.Releases);
    }

    [Theory]
    [InlineData("Share")]
    [InlineData("siLock")]
    [InlineData("SSHFTP")]
    public async Task CancelledPutRemainsUncertainAndUsesIndependentCheckoutCleanup(string type)
    {
        using var requestCancellation = new CancellationTokenSource();
        var fixture = new Fixture(type) { CancelWrite = true, CancelSource = requestCancellation };
        await Assert.ThrowsAsync<HostPartialSaveException>(() => fixture.Coordinator.SaveDefinitionAsync("42",
            new HostDefinitionSaveRequest("Renamed", "Old", new JsonObject()), "tester", fixture.Live, requestCancellation.Token));
        Assert.True(requestCancellation.IsCancellationRequested);
        Assert.Equal(1, fixture.Writes);
        Assert.Equal(1, fixture.Releases);
    }

    [Theory]
    [InlineData("Share", "UNC")]
    [InlineData("siLock", "Host")]
    public async Task MetadataOnlyNoopDoesNotRequireCompletingAnExistingHost(string type, string incompleteField)
    {
        var fixture = new Fixture(type);
        fixture.Saved[incompleteField] = "";
        var result = await fixture.Coordinator.SaveDefinitionAsync("42",
            new HostDefinitionSaveRequest("Host", "Old", new JsonObject()), "tester", fixture.Live);
        Assert.False(result.Changed);
        Assert.Equal(0, fixture.Checkouts);
        Assert.Equal(0, fixture.Writes);
    }

    [Theory]
    [InlineData("Share")]
    [InlineData("siLock")]
    [InlineData("FTP")]
    [InlineData("SSHFTP")]
    [InlineData("POP3")]
    [InlineData("S3")]
    [InlineData("AzureBlob")]
    [InlineData("GoogleStorage")]
    [InlineData("SharePoint")]
    public async Task AllStateCacheProfilesUseSharedCheckoutSaveAndReadback(string type)
    {
        var fixture = new Fixture(type);
        fixture.Saved["UseDefStateCaching"] = 1;
        foreach (var time in new[] { 1, 0, -1 })
        {
            var configuration = new JsonObject { ["UseDefStateCaching"] = false, ["StateCacheTime"] = time };
            if (time > 0) configuration["StateCacheUnit"] = "Minutes";
            var result = await fixture.Coordinator.SaveDefinitionAsync("42", new HostDefinitionSaveRequest("Host", "Old", configuration), "tester", fixture.Live);
            Assert.True(result.Changed);
            Assert.Equal(0, fixture.Submitted!["UseDefStateCaching"]!.GetValue<int>());
            Assert.Equal(time, fixture.Submitted!["StateCacheTime"]!.GetValue<int>());
            if (time > 0) Assert.Equal("Minutes", fixture.Submitted!["StateCacheUnit"]!.GetValue<string>());
            else Assert.Null(fixture.Submitted!["StateCacheUnit"]);
        }
        Assert.True((await fixture.Coordinator.SaveDefinitionAsync("42",
            new HostDefinitionSaveRequest("Host", "Old", new JsonObject { ["UseDefStateCaching"] = true }), "tester", fixture.Live)).Changed);
        Assert.Equal(1, fixture.Submitted!["UseDefStateCaching"]!.GetValue<int>());
        Assert.Null(fixture.Submitted!["StateCacheTime"]);
        Assert.Null(fixture.Submitted!["StateCacheUnit"]);
        Assert.Equal("Keep", fixture.Saved["NativeOnly"]!["Value"]!.GetValue<string>());
        Assert.Equal("Finance", fixture.Saved["Group"]![0]!.GetValue<string>());
        Assert.Equal(4, fixture.Writes);
        Assert.Equal(4, fixture.Undo);
        Assert.Equal(4, fixture.Checkouts);
        Assert.Equal(4, fixture.Releases);
        var unchanged = await fixture.Coordinator.SaveDefinitionAsync("42", new HostDefinitionSaveRequest("Host", "Old",
            new JsonObject { ["UseDefStateCaching"] = true }), "tester", fixture.Live);
        Assert.False(unchanged.Changed);
        Assert.Equal(4, fixture.Writes);
    }

    [Fact]
    public async Task InvalidStateCacheCannotCheckoutOrWrite()
    {
        var fixture = new Fixture("AzureBlob");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Coordinator.SaveDefinitionAsync("42",
            new HostDefinitionSaveRequest("Host", "Old", new JsonObject { ["UseDefStateCaching"] = false, ["StateCacheTime"] = 1, ["StateCacheUnit"] = "Days" }), "tester", fixture.Live));
        Assert.Equal(0, fixture.Checkouts);
        Assert.Equal(0, fixture.Undo);
        Assert.Equal(0, fixture.Writes);
    }

    [Fact]
    public void SharedHostEditorAndUndoServicesResolveWithoutADependencyCycle()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.Configure<HostBehaviorOptions>(_ => { });
        services.AddSingleton(Stub<IMoveItRestClient>((method, _) => throw new NotSupportedException(method.Name)));
        services.AddSingleton(Stub<IMoveItComClient>((method, _) => throw new NotSupportedException(method.Name)));
        services.AddSingleton(Stub<IMoveItSessionService>((method, _) => throw new NotSupportedException(method.Name)));
        services.AddSingleton(Stub<IMetadataRepository>((method, _) => throw new NotSupportedException(method.Name)));
        services.AddSingleton(Stub<IConfigurationUndoWorkflow>((method, _) => throw new NotSupportedException(method.Name)));
        services.AddScoped<IHostDefinitionSaveService, HostDefinitionSaveService>();
        services.AddScoped<IHostCrudCoordinator, HostCrudCoordinator>();
        services.AddScoped<IAutomationHostService, AutomationHostService>();
        services.AddScoped<IHostEditorSaveService, HostEditorSaveService>();
        services.AddScoped<IHostUndoService, HostUndoService>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();
        Assert.IsType<HostEditorSaveService>(scope.ServiceProvider.GetRequiredService<IHostEditorSaveService>());
        Assert.IsType<HostUndoService>(scope.ServiceProvider.GetRequiredService<IHostUndoService>());
    }

    [Theory]
    [InlineData("Share")]
    [InlineData("siLock")]
    [InlineData("FTP")]
    [InlineData("SSHFTP")]
    [InlineData("S3")]
    [InlineData("AzureBlob")]
    [InlineData("GoogleStorage")]
    public async Task SharedEditorSavePreservesNativeFieldsAndSupportsRenameAndClear(string type)
    {
        var fixture = new Fixture(type);
        var result = await fixture.Coordinator.SaveDefinitionAsync(" 42 ",
            new HostDefinitionSaveRequest("Renamed", "", new JsonObject { ["DefUsername"] = "newuser" }),
            "tester", fixture.Live);
        Assert.True(result.Changed);
        Assert.Equal("Renamed", result.HostName);
        Assert.Equal(type, result.TypeCode);
        Assert.Equal("Renamed", fixture.Saved["Name"]!.GetValue<string>());
        Assert.Equal("", fixture.Saved["Desc"]!.GetValue<string>());
        Assert.Equal("newuser", fixture.Saved["DefUsername"]!.GetValue<string>());
        Assert.Equal("STORED_SECRET", fixture.Saved["DefPassword"]!.GetValue<string>());
        Assert.Equal("Keep", fixture.Saved["NativeOnly"]!["Value"]!.GetValue<string>());
        Assert.Equal("Finance", fixture.Saved["Group"]![0]!.GetValue<string>());
        Assert.Equal(1, fixture.Undo);
        Assert.Equal(1, fixture.Checkouts);
        Assert.Equal(1, fixture.Writes);
        Assert.Equal(1, fixture.Releases);
    }

    [Fact]
    public async Task UnchangedSaveDoesNotCaptureUndoOrCheckout()
    {
        var fixture = new Fixture("Share");
        var result = await fixture.Coordinator.SaveDefinitionAsync("42",
            new HostDefinitionSaveRequest("Host", "Old", new JsonObject { ["DefUsername"] = "saveduser" }),
            "tester", fixture.Live);
        Assert.False(result.Changed);
        Assert.Equal(0, fixture.Undo);
        Assert.Equal(0, fixture.Checkouts);
        Assert.Equal(0, fixture.Writes);
    }

    [Fact]
    public async Task DeniedEditDoesNotWriteOrCaptureUndo()
    {
        var fixture = new Fixture("FTP", canEdit: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Coordinator.SaveDefinitionAsync("42",
            new HostDefinitionSaveRequest("Renamed", "Old", new JsonObject()), "tester", fixture.Live));
        Assert.Equal(0, fixture.Undo);
        Assert.Equal(0, fixture.Checkouts);
        Assert.Equal(0, fixture.Writes);
    }

    [Theory]
    [InlineData("Share")]
    [InlineData("siLock")]
    [InlineData("SSHFTP")]
    public async Task ReleaseFailureAfterSuccessfulPutIsAPartialSaveWithoutRetry(string type)
    {
        var fixture = new Fixture(type) { FailRelease = true };
        await Assert.ThrowsAsync<HostPartialSaveException>(() => fixture.Coordinator.SaveDefinitionAsync("42",
            new HostDefinitionSaveRequest("Renamed", "Old", new JsonObject()), "tester", fixture.Live));
        Assert.Equal(1, fixture.Writes);
        Assert.Equal(1, fixture.Releases);
    }

    [Theory]
    [InlineData("Share")]
    [InlineData("siLock")]
    [InlineData("SSHFTP")]
    public async Task ReadbackMismatchAfterPutIsAPartialSaveAndStillReleasesCheckout(string type)
    {
        var fixture = new Fixture(type) { IgnoreDescription = true };
        await Assert.ThrowsAsync<HostPartialSaveException>(() => fixture.Coordinator.SaveDefinitionAsync("42",
            new HostDefinitionSaveRequest("Host", "New", new JsonObject()), "tester", fixture.Live));
        Assert.Equal(1, fixture.Writes);
        Assert.Equal(1, fixture.Releases);
    }

    [Fact]
    public async Task UnknownProtocolFieldIsRejectedBeforeCheckout()
    {
        var fixture = new Fixture("Share");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Coordinator.SaveDefinitionAsync("42",
            new HostDefinitionSaveRequest("Host", "Old", new JsonObject { ["UnverifiedSetting"] = "x" }),
            "tester", fixture.Live));
        Assert.Equal(0, fixture.Undo);
        Assert.Equal(0, fixture.Writes);
    }

    [Fact]
    public async Task OptionalEditorAdapterUsesSharedHostFacade()
    {
        var calls = 0;
        var hosts = Stub<IAutomationHostService>((method, args) =>
        {
            Assert.Equal(nameof(IAutomationHostService.SaveDefinitionAsync), method.Name);
            calls++;
            var request = (HostDefinitionSaveRequest)args[1]!;
            Assert.Equal("Renamed", request.Name);
            Assert.Equal("", request.Description);
            Assert.Equal("", request.Configuration["DefUsername"]!.GetValue<string>());
            return Task.FromResult(new HostDefinitionSaveResult(true, "Renamed", "Share"));
        });
        var result = await new HostEditorSaveService(hosts).SaveMoveItAsync("42", new HostEditorSaveRequest
        {
            Name = "Renamed", Description = "", Configuration = new JsonObject { ["DefUsername"] = "" }
        }, "tester");
        Assert.True(result.Changed);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task SharedCoordinatorRetainsComForImportAndRestoreOperations()
    {
        var calls = new List<string>();
        var com = Stub<IMoveItComClient>((method, args) =>
        {
            calls.Add(method.Name);
            if (method.Name == nameof(IMoveItComClient.CreateConfiguredHostAsync))
                return Task.FromResult(new MoveItCreatedHost("42", "Host", "FTP/FTPS", "FTP"));
            Assert.Equal("42", (string)args[0]!);
            return method.Name switch
            {
                nameof(IMoveItComClient.UpdateConfiguredHostAsync) or nameof(IMoveItComClient.GetHostConfigurationAsync)
                    or nameof(IMoveItComClient.RestoreHostConfigurationAsync)
                    => Task.FromResult(new MoveItHostConfiguration("42", "Host", "FTP", "<FTP ID=\"42\" />")),
                _ => Task.CompletedTask
            };
        });
        var rest = Stub<IMoveItRestClient>((method, _) => throw new NotSupportedException(method.Name));
        var saves = Stub<IHostDefinitionSaveService>((method, _) => throw new NotSupportedException(method.Name));
        var crud = new HostCrudCoordinator(rest, com, saves);
        var fields = new Dictionary<string, string> { ["NAME"] = "Host", ["HOST_TYPE"] = "FTP", ["HOST"] = "example.com" };
        await crud.CreateConfiguredAsync(HostBasicDefinitionProfile.Parse(fields));
        await crud.UpdateConfiguredAsync(" 42 ", HostBasicDefinitionProfile.ParseUpdate(fields));
        await crud.GetNativeConfigurationAsync(" 42 ");
        await crud.RestoreNativeConfigurationAsync(" 42 ", "<FTP ID=\"42\" />");
        await crud.DeleteAsync(" 42 ");
        Assert.Equal(new[] { "CreateConfiguredHostAsync", "UpdateConfiguredHostAsync",
            "GetHostConfigurationAsync", "RestoreHostConfigurationAsync", "DeleteHostAsync" }, calls);
    }

    [Theory]
    [InlineData("www.someurl.com")]
    [InlineData("https://storage.example.test:9443")]
    public async Task S3AlternativeProviderSavesThroughTheSharedTypedRestLifecycle(string endpoint)
    {
        var fixture = new Fixture("S3");
        await fixture.Coordinator.SaveDefinitionAsync("42", new HostDefinitionSaveRequest("Host", "Old",
            new JsonObject { ["S3AlternativeEndpoint"] = endpoint, ["IsNonAmazonEndPoint"] = true }), "tester", fixture.Live);
        Assert.Equal(1, fixture.Submitted!["IsNonAmazonEndPoint"]!.GetValue<int>());
        Assert.Equal(endpoint, fixture.Saved["S3AlternativeEndpoint"]!.GetValue<string>());
        Assert.Equal("bucket", fixture.Saved["DefBucket"]!.GetValue<string>());
        Assert.Equal("saveduser", fixture.Saved["DefUsername"]!.GetValue<string>());
        Assert.Null(fixture.Submitted["DefPassword"]);
        Assert.Equal("STORED_SECRET", fixture.Saved["DefPassword"]!.GetValue<string>());
        Assert.Equal("Keep", fixture.Saved["NativeOnly"]!["Value"]!.GetValue<string>());
        Assert.Equal(1, fixture.Writes);
        Assert.Equal(1, fixture.Checkouts);
        Assert.Equal(1, fixture.Releases);
    }

    [Fact]
    public async Task S3DefaultProviderPreservesInactiveUrl()
    {
        var fixture = new Fixture("S3");
        fixture.Saved["IsNonAmazonEndPoint"] = 1;
        fixture.Saved["S3AlternativeEndpoint"] = "www.someurl.com";
        await fixture.Coordinator.SaveDefinitionAsync("42", new HostDefinitionSaveRequest("Host", "Old",
            new JsonObject { ["IsNonAmazonEndPoint"] = false }), "tester", fixture.Live);
        Assert.Equal(0, fixture.Saved["IsNonAmazonEndPoint"]!.GetValue<int>());
        Assert.Equal("www.someurl.com", fixture.Saved["S3AlternativeEndpoint"]!.GetValue<string>());
        Assert.Equal(1, fixture.Writes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task S3AlternativeProviderWithoutUrlCannotReachPut(string endpoint)
    {
        var fixture = new Fixture("S3");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Coordinator.SaveDefinitionAsync("42",
            new HostDefinitionSaveRequest("Host", "Old", new JsonObject
            { ["IsNonAmazonEndPoint"] = true, ["S3AlternativeEndpoint"] = endpoint }), "tester", fixture.Live));
        Assert.Contains("Alternative Provider URL", error.Message);
        Assert.Equal(0, fixture.Writes);
    }

    [Theory]
    [InlineData("POP3")]
    [InlineData("SMTP")]
    [InlineData("S3")]
    [InlineData("AzureBlob")]
    [InlineData("GoogleStorage")]
    [InlineData("Share")]
    [InlineData("siLock")]
    [InlineData("SSHFTP")]
    [InlineData("FTP")]
    public async Task CapturedPasswordReplacementKeepsWhitespaceAndPreservesOtherSecrets(string type)
    {
        var fixture = new Fixture(type);
        fixture.Saved["ProxyPassword"] = "OLD_PROXY_CIPHER";
        await fixture.Coordinator.SaveDefinitionAsync("42", new HostDefinitionSaveRequest("Host", "Old",
            new JsonObject { ["DefPassword"] = "  replacement  " }), "tester", fixture.Live);
        Assert.Equal("  replacement  ", fixture.Submitted!["DefPassword"]!.GetValue<string>());
        if (type is "S3" or "AzureBlob" or "GoogleStorage" or "SSHFTP" or "FTP") Assert.Null(fixture.Submitted["ProxyPassword"]);
        Assert.Equal("OLD_PROXY_CIPHER", fixture.Saved["ProxyPassword"]!.GetValue<string>());
        Assert.Equal("SERVER_CIPHER", fixture.Saved["DefPassword"]!.GetValue<string>());
        Assert.Equal(1, fixture.Writes);
    }

    [Theory]
    [InlineData("POP3")]
    [InlineData("SMTP")]
    [InlineData("GoogleStorage")]
    public async Task CapturedOAuthModeAndSecretUseTheNativeNullableContract(string type)
    {
        var fixture = new Fixture(type);
        fixture.Saved["UseOAuth"] = 1;
        fixture.Saved["OAuthClientID"] = "client";
        fixture.Saved["ProjectID"] = "project";
        fixture.Saved["OAuthScope"] = "scope";
        fixture.Saved["OAuthTokenURL"] = "https://example.test/token";
        fixture.Saved["OAuthClientSecret"] = "ORIGINAL_CIPHER";
        await fixture.Coordinator.SaveDefinitionAsync("42", new HostDefinitionSaveRequest("Host", "Old",
            new JsonObject { ["OAuthClientSecret"] = "NEW_OAUTH_SECRET" }), "tester", fixture.Live);
        Assert.Null(fixture.Submitted!["DefPassword"]);
        Assert.Equal("NEW_OAUTH_SECRET", fixture.Submitted["OAuthClientSecret"]!.GetValue<string>());
        Assert.Equal("SERVER_CIPHER", fixture.Saved["OAuthClientSecret"]!.GetValue<string>());
        Assert.Equal("STORED_SECRET", fixture.Saved["DefPassword"]!.GetValue<string>());
    }

    [Fact]
    public async Task SftpClientKeyAndFingerprintShareTheOneNativeHostPut()
    {
        var fixture = new Fixture("SSHFTP");
        await fixture.Coordinator.SaveDefinitionAsync("42", new HostDefinitionSaveRequest("Host", "Old", new JsonObject
        { ["DefClientKeyID"] = "123", ["SSHFingerprint"] = "RSA-SHA2-256: TEST", ["IgnoreCertProbs"] = false }), "tester", fixture.Live);
        Assert.Equal("123", fixture.Submitted!["DefClientKeyID"]!.GetValue<string>());
        Assert.Equal("RSA-SHA2-256: TEST", fixture.Submitted["SSHFingerprint"]!.GetValue<string>());
        Assert.Equal(0, fixture.Submitted["IgnoreCertProbs"]!.GetValue<int>());
        Assert.Equal(1, fixture.Writes);
        Assert.Equal(1, fixture.Checkouts);
    }

    [Fact]
    public async Task UnavailableSftpClientKeyIsRejectedBeforeCheckout()
    {
        var fixture = new Fixture("SSHFTP");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Coordinator.SaveDefinitionAsync("42",
            new HostDefinitionSaveRequest("Host", "Old", new JsonObject { ["DefClientKeyID"] = "999" }), "tester", fixture.Live));
        Assert.Equal(0, fixture.Checkouts);
        Assert.Equal(0, fixture.Writes);
    }

    [Fact]
    public async Task UncertainNativeWriteDoesNotEchoAReplacementSecretOrRetry()
    {
        var fixture = new Fixture("SSHFTP") { FailWrite = true };
        var error = await Assert.ThrowsAsync<HostPartialSaveException>(() => fixture.Coordinator.SaveDefinitionAsync("42",
            new HostDefinitionSaveRequest("Host", "Old", new JsonObject { ["DefPassword"] = "REPLACEMENT_SECRET" }), "tester", fixture.Live));
        Assert.DoesNotContain("REPLACEMENT_SECRET", error.ToString());
        Assert.Equal(1, fixture.Writes);
        Assert.Equal(1, fixture.Releases);
    }

    [Fact]
    public async Task SharePointModeSecretProxyAndLibraryUseOneNativePut()
    {
        var fixture = new Fixture("SharePoint");
        await fixture.Coordinator.SaveDefinitionAsync("42", new HostDefinitionSaveRequest("Host", "Old", new JsonObject
        {
            ["ClientSecret"] = "  replacement  ", ["RealmID"] = "realm", ["UsingAppAuthentication"] = "1",
            ["ProxyType"] = "HTTPS", ["ProxyHost"] = "proxy.example.test", ["ProxyPort"] = 8080,
            ["ProxyIgnoreCertErrors"] = true, ["DefDocumentLibrary"] = "Library",
            ["DefDocumentLibraryGuid"] = "11111111-2222-3333-4444-555555555555"
        }), "tester", fixture.Live);
        Assert.Equal(1, fixture.Submitted!["UsingAppAuthentication"]!.GetValue<int>());
        Assert.Equal("  replacement  ", fixture.Submitted["ClientSecret"]!.GetValue<string>());
        Assert.Null(fixture.Submitted["ProxyPassword"]);
        Assert.Equal("SERVER_CIPHER", fixture.Saved["ClientSecret"]!.GetValue<string>());
        Assert.Equal(1, fixture.Writes); Assert.Equal(1, fixture.Undo); Assert.Equal(1, fixture.Releases);
        Assert.Equal("Keep", fixture.Saved["NativeOnly"]!["Value"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("FTP")]
    [InlineData("SharePoint")]
    [InlineData("siLock")]
    public async Task CertificateReferencesAreValidatedAndSavedTogether(string type)
    {
        var fixture = new Fixture(type);
        var patch = new JsonObject { ["DefClientCertSHA1"] = "AA BB", ["DefClientCertStore"] = "My", ["DefClientCertSubject"] = "Test Certificate" };
        await fixture.Coordinator.SaveDefinitionAsync("42", new HostDefinitionSaveRequest("Host", "Old", patch), "tester", fixture.Live);
        Assert.Equal("AA BB", fixture.Submitted!["DefClientCertSHA1"]!.GetValue<string>());
        Assert.Equal(1, fixture.Writes);
        var bad = new Fixture(type);
        patch["DefClientCertStore"] = "Other";
        await Assert.ThrowsAsync<InvalidOperationException>(() => bad.Coordinator.SaveDefinitionAsync("42", new HostDefinitionSaveRequest("Host", "Old", patch), "tester", bad.Live));
        Assert.Equal(0, bad.Checkouts); Assert.Equal(0, bad.Writes);
    }

    [Fact]
    public async Task FtpCapturedTlsAndDirectorySettingsPreserveCredentials()
    {
        var fixture = new Fixture("FTP");
        await fixture.Coordinator.SaveDefinitionAsync("42", new HostDefinitionSaveRequest("Host", "Old", new JsonObject
        { ["Secure"] = "implicit", ["Port"] = 990, ["CustomDirColDate"] = 1, ["CustomDirColFilename"] = 2, [HostFtpDirectoryParsing.ModeKey] = "custom",
          ["CustomDirSkipTop"] = 1, ["CustomDirSkipBottom"] = 1, ["AddComSignon"] = "first\nsecond" }), "tester", fixture.Live);
        Assert.Equal("implicit", fixture.Submitted!["Secure"]!.GetValue<string>());
        Assert.Equal(990L, fixture.Submitted["Port"]!.GetValue<long>());
        Assert.Equal("first\nsecond", fixture.Submitted["AddComSignon"]!.GetValue<string>());
        Assert.Null(fixture.Submitted["DefPassword"]);
        Assert.Equal("STORED_SECRET", fixture.Saved["DefPassword"]!.GetValue<string>());
    }

    [Fact]
    public async Task UncRunAsModeOmitsBothCredentialsAndEditorCommandFromNativePut()
    {
        var fixture = new Fixture("Share");
        await fixture.Coordinator.SaveDefinitionAsync("42", new("Host", "Old", new JsonObject
        { [HostAuthenticationSettings.UncModeKey] = "run-as" }), "tester", fixture.Live);
        Assert.False(fixture.Submitted!.ContainsKey("DefUsername"));
        Assert.False(fixture.Submitted.ContainsKey("DefPassword"));
        Assert.False(fixture.Submitted.ContainsKey(HostAuthenticationSettings.UncModeKey));
        Assert.Equal("run-as", HostAuthenticationSettings.UncMode(fixture.Saved));
        Assert.Equal(1, fixture.Writes);
    }

    [Fact]
    public async Task UncSpecifiedCredentialsCanReplacePasswordOrPreserveItWhenBlank()
    {
        var fixture = new Fixture("Share");
        await fixture.Coordinator.SaveDefinitionAsync("42", new("Host", "Old", new JsonObject
        { [HostAuthenticationSettings.UncModeKey] = "credentials", ["DefUsername"] = @"domain\user", ["DefPassword"] = "  literal\\x41  " }), "tester", fixture.Live);
        Assert.Equal(@"domain\user", fixture.Submitted!["DefUsername"]!.GetValue<string>());
        Assert.Equal("  literal\\x41  ", fixture.Submitted["DefPassword"]!.GetValue<string>());
        Assert.Equal("SERVER_CIPHER", fixture.Saved["DefPassword"]!.GetValue<string>());
        await fixture.Coordinator.SaveDefinitionAsync("42", new("Host", "Changed", new JsonObject
        { ["DefPassword"] = "" }), "tester", fixture.Live);
        Assert.Null(fixture.Submitted!["DefPassword"]);
        Assert.Equal("SERVER_CIPHER", fixture.Saved["DefPassword"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("D:")]
    [InlineData("Z:")]
    public async Task UncDriveLetterUsesCapturedNativeValues(string drive)
    {
        var fixture = new Fixture("Share");
        await fixture.Coordinator.SaveDefinitionAsync("42", new("Host", "Changed", new JsonObject
        { ["DriveLetter"] = drive }), "tester", fixture.Live);
        Assert.Equal(drive, fixture.Submitted!["DriveLetter"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("run-as", "user", "")]
    [InlineData("run-as", "", "secret")]
    [InlineData("credentials", "", "secret")]
    public async Task InvalidUncCredentialCombinationsDoNotReachMoveIt(string mode, string username, string password)
    {
        var fixture = new Fixture("Share");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Coordinator.SaveDefinitionAsync("42", new("Host", "Old", new JsonObject
        { [HostAuthenticationSettings.UncModeKey] = mode, ["DefUsername"] = username, ["DefPassword"] = password }), "tester", fixture.Live));
        Assert.Equal(0, fixture.Checkouts); Assert.Equal(0, fixture.Writes);
    }

    [Theory]
    [InlineData("tls-c")]
    [InlineData("tls-c-ccc")]
    [InlineData("on-ccc")]
    [InlineData("implicit")]
    [InlineData("implicit-ccc")]
    public async Task FtpTlsModesAndAccountUseCapturedNativeFields(string secure)
    {
        var fixture = new Fixture("FTP");
        await fixture.Coordinator.SaveDefinitionAsync("42", new("Host", "Old", new JsonObject
        { ["Secure"] = secure, ["DefAccount"] = "[P:FTPAccount]" }), "tester", fixture.Live);
        Assert.Equal(secure, fixture.Submitted!["Secure"]!.GetValue<string>());
        Assert.Equal("[P:FTPAccount]", fixture.Submitted["DefAccount"]!.GetValue<string>());
        Assert.Null(fixture.Submitted["DefPassword"]);
    }

    [Theory]
    [InlineData("POP3")]
    [InlineData("SMTP")]
    public async Task Office365HelperComposesOnlyNativeOAuthFieldsAndEnablesTls(string type)
    {
        var fixture = new Fixture(type);
        fixture.Saved["Secure"] = 0;
        await fixture.Coordinator.SaveDefinitionAsync("42", new("Host", "Old", new JsonObject
        {
            ["UseOAuth"] = true, [HostMailOAuthSettings.ProviderKey] = "microsoft365", [HostMailOAuthSettings.TenantKey] = "tenantid1",
            ["DefUsername"] = "username1", ["OAuthClientID"] = "clientid1", ["OAuthClientSecret"] = "  secret1  "
        }), "tester", fixture.Live);
        Assert.Equal(HostMailOAuthSettings.Scope, fixture.Submitted!["OAuthScope"]!.GetValue<string>());
        Assert.Equal("https://login.microsoftonline.com/tenantid1/oauth2/v2.0/token", fixture.Submitted["OAuthTokenURL"]!.GetValue<string>());
        Assert.Equal(1, fixture.Submitted["Secure"]!.GetValue<int>());
        Assert.Equal("  secret1  ", fixture.Submitted["OAuthClientSecret"]!.GetValue<string>());
        Assert.Null(fixture.Submitted["DefPassword"]);
        Assert.False(fixture.Submitted.ContainsKey(HostMailOAuthSettings.ProviderKey));
        Assert.False(fixture.Submitted.ContainsKey(HostMailOAuthSettings.TenantKey));
        Assert.Equal("microsoft365", HostMailOAuthSettings.Provider(fixture.Saved));
        Assert.Equal("tenantid1", HostMailOAuthSettings.Tenant(fixture.Saved));
    }

    [Theory]
    [InlineData("POP3")]
    [InlineData("SMTP")]
    public async Task CustomMailOAuthPreservesExplicitScopeAndEndpoint(string type)
    {
        var fixture = new Fixture(type);
        await fixture.Coordinator.SaveDefinitionAsync("42", new("Host", "Old", new JsonObject
        {
            ["UseOAuth"] = true, [HostMailOAuthSettings.ProviderKey] = "custom", ["OAuthClientID"] = "client",
            ["OAuthScope"] = "scope", ["OAuthTokenURL"] = "https://identity.example.test/token"
        }), "tester", fixture.Live);
        Assert.Equal("scope", fixture.Submitted!["OAuthScope"]!.GetValue<string>());
        Assert.Equal("https://identity.example.test/token", fixture.Submitted["OAuthTokenURL"]!.GetValue<string>());
        Assert.Equal("custom", HostMailOAuthSettings.Provider(fixture.Saved));
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("../other")]
    [InlineData("tenant?extra=value")]
    public async Task InvalidOffice365TenantDoesNotReachMoveIt(string tenant)
    {
        var fixture = new Fixture("SMTP");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Coordinator.SaveDefinitionAsync("42", new("Host", "Old", new JsonObject
        { ["UseOAuth"] = true, [HostMailOAuthSettings.ProviderKey] = "microsoft365", [HostMailOAuthSettings.TenantKey] = tenant }), "tester", fixture.Live));
        Assert.Equal(0, fixture.Checkouts); Assert.Equal(0, fixture.Writes);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SharePointDefaultSiteCanBeBlankInBothSupportedAuthenticationModes(int mode)
    {
        var fixture = new Fixture("SharePoint");
        fixture.Saved["UsingAppAuthentication"] = mode;
        fixture.Saved["RealmID"] = "realm";
        await fixture.Coordinator.SaveDefinitionAsync("42", new("Host", "Old", new JsonObject
        { ["Site"] = "", ["ClientID"] = "changed-client" }), "tester", fixture.Live);
        Assert.Equal("", fixture.Submitted!["Site"]!.GetValue<string>());
        Assert.Equal("changed-client", fixture.Submitted["ClientID"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("Share", false)]
    [InlineData("Share", true)]
    [InlineData("FTP", false)]
    [InlineData("FTP", true)]
    public async Task StaleHostEditorCannotOverwriteConfigurationOrSuppressedCredentials(string type, bool nativeCredentialChanged)
    {
        var fixture = new Fixture(type);
        var original = fixture.Saved.DeepClone();
        var xml = $"<{type} ID=\"42\" Name=\"Host\" DefPassword=\"old\" />";
        fixture.NativeXml = xml;
        var version = ObjectEditVersion.Create("Host", "42", ObjectEditVersion.Definition(original, xml));
        if (nativeCredentialChanged) fixture.NativeXml = xml.Replace("DefPassword=\"old\"", "DefPassword=\"new\"");
        else fixture.Saved["Host"] = "changed-by-another-editor.example";
        using var scope = ObjectEditVersion.Require("Host", "42", version);
        await Assert.ThrowsAsync<ObjectEditConflictException>(() => fixture.Coordinator.SaveDefinitionAsync("42",
            new HostDefinitionSaveRequest("Renamed", "Old", new JsonObject()), "tester", fixture.Live));
        Assert.Equal(0, fixture.Writes);
        Assert.Equal(1, fixture.Checkouts);
        Assert.Equal(1, fixture.Releases);
    }

    public static IEnumerable<object[]> FlatCases => HostV0CreationTests.Types.SelectMany(values =>
        new[] { new object[] { values[0], false }, new object[] { values[0], true } });

    [Theory] [MemberData(nameof(FlatCases))]
    public async Task FlatDefinitionsKeepFullEditorRevisionAndVerifySavedFieldsForEveryType(string type, bool flatCheckout)
    {
        var fixture = new Fixture(type) { FlatRead = true, FlatCheckout = flatCheckout };
        var xml = $"<{type} ID=\"42\" Name=\"Host\" />";
        using var edit = ObjectEditVersion.Require("Host", "42", ObjectEditVersion.Create("Host", "42", ObjectEditVersion.Definition(fixture.Saved, xml)));
        var result = await fixture.Coordinator.SaveDefinitionAsync("42", new HostDefinitionSaveRequest("Renamed", "Updated", new JsonObject()), "tester", fixture.Live);
        Assert.True(result.Changed); Assert.Equal(type, result.TypeCode);
        Assert.Equal("Renamed", fixture.Saved["Name"]!.ToString()); Assert.Equal("Updated", fixture.Saved["Desc"]!.ToString());
        Assert.Equal(3, fixture.InventoryReads); Assert.Equal(1, fixture.Writes); Assert.Equal(1, fixture.Checkouts); Assert.Equal(1, fixture.Releases);
        Assert.Null(fixture.Submitted!["Type"]); // Inventory type must never alter the original GET/checkout projection.
    }

    [Theory] [InlineData("missing")] [InlineData("ambiguous")] [InlineData("wrong-type")] [InlineData("missing-type")]
    public async Task FlatReadbackCannotSucceedWithUnverifiableInventory(string failure)
    {
        var fixture = new Fixture("Share") { FlatRead = true };
        fixture.InventoryReader = attempt => attempt < 3
            ? [new JsonObject { ["Share"] = fixture.Saved.DeepClone() }]
            : failure switch
            {
                "missing" => [],
                "ambiguous" => [new JsonObject { ["Share"] = fixture.Saved.DeepClone() }, new JsonObject { ["Share"] = fixture.Saved.DeepClone() }],
                "wrong-type" => [new JsonObject { ["SMTP"] = fixture.Saved.DeepClone() }],
                _ => [fixture.Saved.DeepClone().AsObject()]
            };
        var error = await Assert.ThrowsAsync<HostPartialSaveException>(() => fixture.Coordinator.SaveDefinitionAsync("42",
            new HostDefinitionSaveRequest("Renamed", "Updated", new JsonObject()), "tester", fixture.Live));
        Assert.Equal("readback", error.VerificationStage); Assert.NotEmpty(error.VerificationReason);
        Assert.Equal(1, fixture.Writes); Assert.Equal(1, fixture.Releases);
    }

    [Theory] [InlineData("PUT")] [InlineData("readback")] [InlineData("checkout-release")]
    public async Task SaveDiagnosticsKeepTheFailureStageAndNeverNativeSecrets(string stage)
    {
        var fixture = new Fixture("FTP") { FailWrite = stage == "PUT", IgnoreDescription = stage == "readback", FailRelease = stage == "checkout-release" };
        var error = await Assert.ThrowsAsync<HostPartialSaveException>(() => fixture.Coordinator.SaveDefinitionAsync("42",
            new HostDefinitionSaveRequest("Renamed", "Updated", new JsonObject()), "tester", fixture.Live));
        Assert.Equal(stage, error.VerificationStage); Assert.Contains("Save stage: " + stage, error.Message);
        Assert.DoesNotContain("REPLACEMENT_SECRET", error.ToString()); Assert.Equal(1, fixture.Writes); Assert.Equal(1, fixture.Releases);
    }

    [Fact]
    public async Task FlatEditorRevisionStillRejectsChangedSuppressedCredentialsBeforePut()
    {
        var fixture = new Fixture("FTP") { FlatRead = true, NativeXml = "<FTP ID=\"42\" DefPassword=\"OLD\" />" };
        var version = ObjectEditVersion.Create("Host", "42", ObjectEditVersion.Definition(fixture.Saved, fixture.NativeXml!));
        fixture.NativeXml = "<FTP ID=\"42\" DefPassword=\"NEW\" />";
        using var edit = ObjectEditVersion.Require("Host", "42", version);
        await Assert.ThrowsAsync<ObjectEditConflictException>(() => fixture.Coordinator.SaveDefinitionAsync("42",
            new HostDefinitionSaveRequest("Renamed", "Updated", new JsonObject()), "tester", fixture.Live));
        Assert.Equal(0, fixture.Writes); Assert.Equal(1, fixture.Releases);
    }

    private sealed class Fixture
    {
        public HostCrudCoordinator Coordinator { get; }
        public HostCacheSnapshot Live { get; }
        public JsonObject Saved { get; private set; }
        public JsonObject? Submitted { get; private set; }
        public bool FlatRead { get; init; }
        public bool FlatCheckout { get; init; }
        public int InventoryReads { get; private set; }
        public Func<int, IReadOnlyList<JsonObject>>? InventoryReader { get; set; }
        public int Undo { get; private set; }
        public int Checkouts { get; private set; }
        public int Writes { get; private set; }
        public int Releases { get; private set; }
        public bool FailRelease { get; init; }
        public bool IgnoreDescription { get; init; }
        public bool FailWrite { get; init; }
        public Exception? WriteFailure { get; init; }
        public bool CancelWrite { get; init; }
        public CancellationTokenSource? CancelSource { get; init; }
        public string? CheckoutId { get; init; }
        public string? CheckoutType { get; init; }
        public bool CorruptReadbackId { get; init; }
        public string? NativeXml { get; set; }

        public Fixture(string type, bool canEdit = true)
        {
            var id = type == "FileSystem" ? "0" : "42";
            var name = type == "FileSystem" ? "(Default)" : "Host";
            Saved = new JsonObject
            {
                ["ID"] = id, ["Name"] = name, ["Desc"] = "Old", ["Host"] = "example.com", ["Port"] = "22",
                ["UNC"] = @"\\server\share", ["DefUsername"] = "saveduser", ["DefPassword"] = "STORED_SECRET",
                ["DefBucket"] = "bucket", ["DefContainer"] = "container", ["DefDLInteg"] = "never",
                ["Secure"] = type == "FTP" ? "on" : "1", ["ProxyType"] = "None",
                ["UsingAppAuthentication"] = 2, ["TenantName"] = "tenant", ["Site"] = "site", ["SiteUrlPrefix"] = "/sites/",
                ["ClientID"] = "client", ["TenantID"] = "tenant-id", ["ClientSecret"] = "STORED_CLIENT_SECRET",
                ["DefClientCertSHA1"] = "OLD", ["DefClientCertStore"] = "My", ["DefClientCertSubject"] = "Old Certificate",
                ["NativeOnly"] = new JsonObject { ["Value"] = "Keep" }, ["Group"] = new JsonArray("Finance")
            };
            if (type == "FileSystem") Saved = JsonNode.Parse("""
                {"Name":"(Default)","ID":"0","Desc":"Old","DefRetryCount":3,"DefRetryTimeoutSecs":30,
                 "UsedByCount":0,"OverridesTotal":0,"OverridesNoPerm":false,"DelOldStateDays":0,"UseDefStateCaching":1,
                 "UseNotif":0,"DefRescanSecs":0,"RenameAfterUpload":0,"TempUploadName":"CTMP[Rnd:4]",
                 "DefMxFi":0,"DefMxBy":0,"AdjDST":1,"DefPreserveFileTimestamp":0,"Group":[],"NativeOnly":{"Value":"Keep"}}
                """)!.AsObject();
            Live = new HostCacheSnapshot(new[] { new AutomationHost
            { Id = id, Name = name, Description = "Old", TypeCode = type, CanEditDelete = canEdit } }, DateTimeOffset.UtcNow);
            var rest = Stub<IMoveItRestClient>((method, args) => method.Name switch
            {
                nameof(IMoveItRestClient.GetHostAsync) => Task.FromResult(FlatRead ? Saved.DeepClone().AsObject() : Wrap()),
                nameof(IMoveItRestClient.GetHostsAsync) => Inventory(),
                nameof(IMoveItRestClient.GetCustomScriptsAsync) => Task.FromResult((IReadOnlyList<JsonObject>)Array.Empty<JsonObject>()),
                nameof(IMoveItRestClient.GetStandardScriptsAsync) => Task.FromResult((IReadOnlyList<JsonObject>)new[] { new JsonObject { ["ID"] = "100401", ["Name"] = "Directory Parser" } }),
                nameof(IMoveItRestClient.HostNameExistsAsync) => Task.FromResult(false),
                nameof(IMoveItRestClient.GetSshKeysAsync) => Task.FromResult((IReadOnlyList<JsonObject>)new[] { new JsonObject { ["ID"] = "123", ["Name"] = "Test Key" } }),
                nameof(IMoveItRestClient.GetHostTlsCertificatesV0Async) => Task.FromResult(new JsonObject { ["Certs"] = new JsonObject { ["Cert"] = new JsonArray(new JsonObject
                    { ["Store"] = "My", ["IssuedTo"] = "Test Certificate", ["SHA1Thumbprint"] = "AA BB", ["xIsExpired"] = false }) } }),
                nameof(IMoveItRestClient.CheckoutHostV0Async) => Checkout(),
                nameof(IMoveItRestClient.UpdateHostV0Async) => Put((JsonObject)args[1]!),
                nameof(IMoveItRestClient.ReleaseHostCheckoutV0Async) => Release((CancellationToken)args[1]!),
                _ => throw new NotSupportedException(method.Name)
            });
            var com = Stub<IMoveItComClient>((method, _) => method.Name == nameof(IMoveItComClient.GetHostConfigurationAsync)
                ? Task.FromResult(new MoveItHostConfiguration(id, name, type, NativeXml ?? $"<{type} ID=\"{id}\" Name=\"{name}\" />"))
                : throw new NotSupportedException(method.Name));
            var undo = Stub<IConfigurationUndoWorkflow>((method, args) => method.Name == nameof(IConfigurationUndoWorkflow.CaptureAsync)
                ? Capture((Func<CancellationToken, Task<UndoConfigurationSource>>)args[2]!)
                : throw new NotSupportedException(method.Name));
            Coordinator = new HostCrudCoordinator(rest, com,
                new HostDefinitionSaveService(rest, com, undo, NullLogger<HostDefinitionSaveService>.Instance));

            JsonObject Wrap() => new() { [type] = Saved.DeepClone() };
            Task<IReadOnlyList<JsonObject>> Inventory()
            {
                InventoryReads++;
                return Task.FromResult(InventoryReader?.Invoke(InventoryReads) ?? new[] { Wrap() });
            }
            Task<JsonObject> Checkout()
            {
                Checkouts++;
                var host = Saved.DeepClone().AsObject();
                if (CheckoutId is not null) host["ID"] = CheckoutId;
                return Task.FromResult(FlatCheckout ? host : new JsonObject { [CheckoutType ?? type] = host });
            }
            Task<JsonObject> Put(JsonObject payload)
            {
                Writes++;
                if (CancelWrite)
                {
                    CancelSource?.Cancel();
                    return Task.FromCanceled<JsonObject>(new CancellationToken(true));
                }
                if (WriteFailure is not null) return Task.FromException<JsonObject>(WriteFailure);
                if (FailWrite) return Task.FromException<JsonObject>(new InvalidOperationException("Native error REPLACEMENT_SECRET"));
                Submitted = HostDefinitionJson.UnwrapHost(payload).Host.DeepClone().AsObject();
                var previous = Saved;
                Saved = HostDefinitionJson.UnwrapHost(payload).Host.DeepClone().AsObject();
                foreach (var secret in new[] { "DefPassword", "OAuthClientSecret", "ProxyPassword", "ClientSecret" })
                    if (Saved.ContainsKey(secret) && Saved[secret] is null && previous.ContainsKey(secret))
                        Saved[secret] = previous[secret]?.DeepClone();
                    else if (Saved[secret] is not null && (secret == "DefPassword" && type is "Share" or "siLock"
                        || HostCapturedProfiles.TryGet(type, out var profileFields) && profileFields.Any(field => field.Key == secret && field.Sensitive)))
                        Saved[secret] = "SERVER_CIPHER";
                if (IgnoreDescription) Saved["Desc"] = "Old";
                if (CorruptReadbackId) Saved["ID"] = "99";
                return Task.FromResult(new JsonObject { ["status"] = "success", ["id"] = id, ["errorcode"] = 0 });
            }
            Task Release(CancellationToken cancellationToken)
            {
                Assert.False(cancellationToken.IsCancellationRequested);
                Releases++;
                return FailRelease ? Task.FromException(new InvalidOperationException("Release failed")) : Task.CompletedTask;
            }
            async Task<ConfigurationUndoSnapshot> Capture(Func<CancellationToken, Task<UndoConfigurationSource>> capture)
            {
                Undo++;
                var source = await capture(CancellationToken.None);
                Assert.Equal(id, source.Id);
                return new ConfigurationUndoSnapshot("editor-save-snapshot", "Host", id, name, "Marc", DateTimeOffset.UtcNow, source.Xml);
            }
        }
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
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler!(method!, args ?? Array.Empty<object?>());
    }
}
