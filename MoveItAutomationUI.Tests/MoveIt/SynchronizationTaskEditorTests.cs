// Captured/native-contract regression tests. No live MOVEit API is contacted.
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Tasks;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class SynchronizationTaskEditorTests
{
    [Theory]
    [InlineData("", "Traditional")]
    [InlineData("COS", "Advanced")]
    [InlineData("Sy", "Synchronization")]
    public async Task ReadOnlyEditorLoadsRestSchedulesWithoutNativeSnapshotOrEditRevision(string code, string expectedType)
    {
        var task = Definition("Share"); task["TT"] = code;
        task["Schedules"] = JsonNode.Parse("""{"Schedule":[{"Frequency":{"Interval":[{"StartTime":"01:00","EndTime":"01:00","EveryMinutes":0}]},"Days":{"DayOfWeek":["Monday"]}}]}""");
        var rest = Rest(() => task, (_, _) => throw new Exception("Read-only must not save"));
        var crud = Stub<ITaskCrudCoordinator>((method, _) => method.Name == nameof(ITaskCrudCoordinator.GetDefinitionAsync)
            ? Task.FromResult(task) : throw new Exception("Read-only must not call " + method.Name));
        var access = Stub<IMoveItObjectAccessService>((method, _) => method.Name switch
        {
            nameof(IMoveItObjectAccessService.GetObjectAsync) => Task.FromResult(new MoveItObjectAccess(false, false, false)),
            nameof(IMoveItObjectAccessService.GetCreationAsync) => Task.FromResult(new MoveItCreationAccess(false, new Dictionary<string, string[]>())),
            _ => throw new NotSupportedException(method.Name)
        });
        var result = await new TraditionalTaskEditorService(rest, crud, access).GetDefinitionAsync("100");
        Assert.Equal(expectedType, result.Task.Type);
        Assert.False(result.Task.CanAlter);
        Assert.False(result.Task.CanRun);
        Assert.False(result.Task.CanDelete);
        Assert.Single(result.Task.Schedules);
        Assert.Empty(result.EditVersion);
    }

    [Theory]
    [InlineData(null, "random")]
    [InlineData("", "random")]
    [InlineData("orig", "orig")]
    public async Task UnspecifiedCacheNamesSaveAndVerifyThroughTheSharedSyncPath(string? current, string expected)
    {
        var task = Definition("Share");
        if (current is null) task.Remove("CacheNames"); else task["CacheNames"] = current;
        var rest = Rest(() => task, (_, proposed) =>
        {
            Assert.Equal(expected, proposed["CacheNames"]!.GetValue<string>());
            task = proposed.DeepClone().AsObject();
            return Task.FromResult(Acknowledgement());
        });
        var request = Request(task); request["task"]!["cacheNames"] = "";
        await PatchService(rest).ApplyEditorPatchAsync("100", request);
        Assert.Equal(expected, task["CacheNames"]!.GetValue<string>());
        Assert.Equal("", request["task"]!["cacheNames"]!.GetValue<string>());
    }

    [Fact]
    public async Task SharedEditorPatchUsesV0AndPreservesSyncAndUnknownFields()
    {
        var task = Definition("Share");
        var original = task.DeepClone().AsObject();
        var writes = 0;
        var reads = 0;
        var rest = Rest(() => { reads++; return task; }, (_, proposed) =>
        {
            writes++; task = proposed.DeepClone().AsObject();
            return Task.FromResult(Acknowledgement());
        });
        var patch = Request(original);
        patch["steps"]![0]!["patch"]!["Path"] = "/edited";
        await PatchService(rest).ApplyEditorPatchAsync("100", patch);
        Assert.Equal(1, writes);
        Assert.Equal(3, reads);
        Assert.Equal(-1, task["SameSecs"]!.GetValue<int>());
        Assert.Equal("Sy", task["TT"]!.GetValue<string>());
        Assert.Equal("retain", task["NativeOnly"]!.GetValue<string>());
        Assert.Equal("keep", task["steps"]![0]!["WrapperField"]!.GetValue<string>());
        Assert.Equal("/edited", task["steps"]![0]!["Source"]!["Path"]!.GetValue<string>());
        Assert.Equal("/a", original["steps"]![0]!["Source"]!["Path"]!.GetValue<string>());
        for (var index = 0; index < 2; index++)
            foreach (var field in new[] { "SyF", "SyT", "SyDel", "UseLCaseComp", "PreserveFileTimestamp", "NativeSource" })
                Assert.True(JsonNode.DeepEquals(original["steps"]![index]!["Source"]![field], task["steps"]![index]!["Source"]![field]), field);
    }

    [Fact]
    public async Task CapturedSwapUsesSharedV0SaveAndRetainsFolderIdsWithoutResettingListings()
    {
        var task = Definition("Share"); var writes = 0; var clears = 0;
        var original = task.DeepClone().AsObject();
        var rest = Rest(() => task, (_, proposed) => { writes++; task = proposed.DeepClone().AsObject(); return Task.FromResult(Acknowledgement()); },
            () => { clears++; throw new InvalidOperationException("Swap must not clear listings"); });
        await Coordinator(rest).SwapSynchronizationFoldersAsync("100");
        Assert.Equal(1, writes); Assert.Equal(0, clears);
        Assert.Equal("12", task["steps"]![0]!["Source"]!["ID"]!.GetValue<string>());
        Assert.Equal("11", task["steps"]![1]!["Source"]!["ID"]!.GetValue<string>());
        Assert.Equal("/b", task["steps"]![0]!["Source"]!["Path"]!.GetValue<string>());
        Assert.Equal(1, task["steps"]![0]!["Source"]!["SyF"]!.GetValue<int>());
        Assert.Equal(0, task["steps"]![1]!["Source"]!["SyF"]!.GetValue<int>());
        Assert.Equal(1, task["steps"]![0]!["Source"]!["SyDel"]!.GetValue<int>());
        Assert.Equal(0, task["steps"]![1]!["Source"]!["SyDel"]!.GetValue<int>());
        Assert.Equal("retain", task["NativeOnly"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(original["Schedules"], task["Schedules"]));
        Assert.Equal(original["NextEID"]!.ToString(), task["NextEID"]!.ToString());
    }

    [Fact]
    public async Task ClearDisablesAndVerifiesTheTaskBeforeSendingCapturedDelete()
    {
        var task = Definition("Share"); task["Active"] = 1;
        var order = new List<string>();
        var rest = Rest(() => task, (_, proposed) => { task = proposed.DeepClone().AsObject(); order.Add("PUT:" + task["Active"]); return Task.FromResult(Acknowledgement()); },
            () => { Assert.Equal(0, task["Active"]!.GetValue<int>()); order.Add("DELETE"); return Task.FromResult(JsonNode.Parse("""{"id":"-1","status":"success","errorcode":0}""")!.AsObject()); });
        await Coordinator(rest).ClearSynchronizationListingsAsync("100");
        Assert.Equal(new[] { "PUT:0", "DELETE" }, order);
        Assert.Equal(0, task["Active"]!.GetValue<int>());
    }

    [Fact]
    public async Task ConfirmedPolicySaveActuallyClearsNativeListingsOnce()
    {
        var task = Definition("Share"); var writes = 0; var clears = 0;
        var rest = Rest(() => task, (_, proposed) => { writes++; task = proposed.DeepClone().AsObject(); return Task.FromResult(Acknowledgement()); },
            () => { clears++; return Task.FromResult(JsonNode.Parse("""{"id":"-1","status":"success","errorcode":0}""")!.AsObject()); });
        var request = Request(task); request["syncSettings"] = new JsonObject { ["direction"] = "bidirectional" }; request["syncChangesConfirmed"] = true;
        await PatchService(rest).ApplyEditorPatchAsync("100", request);
        Assert.Equal(1, writes); Assert.Equal(1, clears); Assert.Equal(0, task["Active"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("{\"id\":\"100\",\"status\":\"success\",\"errorcode\":0}")]
    [InlineData("{\"id\":\"-1\",\"status\":\"error\",\"errorcode\":5}")]
    [InlineData("{}")]
    public async Task AListingClearRequiresTheCapturedAcknowledgementAndNeverRetries(string response)
    {
        var task = Definition("Share"); var clears = 0;
        var rest = Rest(() => task, clear: () => { clears++; return Task.FromResult(JsonNode.Parse(response)!.AsObject()); });
        await Assert.ThrowsAsync<SynchronizationActionPartialSaveException>(() => Coordinator(rest).ClearSynchronizationListingsAsync("100"));
        Assert.Equal(1, clears);
    }

    [Fact]
    public async Task LostResetResponseBecomesAnUncertainOutcomeWithNoRetry()
    {
        var task = Definition("Share"); var clears = 0;
        var rest = Rest(() => task, clear: () => { clears++; throw new HttpRequestException("Connection lost"); });
        await Assert.ThrowsAsync<SynchronizationActionPartialSaveException>(() => Coordinator(rest).ClearSynchronizationListingsAsync("100"));
        Assert.Equal(1, clears);
    }

    [Theory]
    [InlineData("Tr")]
    [InlineData("COS")]
    public async Task SyncActionsRejectOtherTaskKindsBeforeAnyMutation(string type)
    {
        var task = Definition("Share"); task["TT"] = type;
        var clears = 0;
        var rest = Rest(() => task, clear: () => { clears++; throw new InvalidOperationException("Unexpected DELETE"); });
        await Assert.ThrowsAsync<InvalidOperationException>(() => Coordinator(rest).ClearSynchronizationListingsAsync("100"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Coordinator(rest).SwapSynchronizationFoldersAsync("100"));
        Assert.Equal(0, clears);
    }

    [Fact]
    public async Task ReadOnlySyncTasksCannotClearOrSwap()
    {
        var task = Definition("Share"); task["xAlter"] = 0;
        var rest = Rest(() => task);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Coordinator(rest).ClearSynchronizationListingsAsync("100"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Coordinator(rest).SwapSynchronizationFoldersAsync("100"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Coordinator(rest).SaveSynchronizationPreviewAsync("100"));
    }

    [Fact]
    public async Task CapturedPreviewSingletonUsesNativeActionsAndInitialRunState()
    {
        var preview = await Coordinator(Rest(() => Definition("Share"))).GetSynchronizationPreviewAsync("100");
        Assert.True(preview.CanChangeInitialRunOptions); Assert.Single(preview.Entries);
        Assert.Equal("reagentold.xml", preview.Entries[0].FolderA!.Path);
        Assert.Equal("1108", preview.Entries[0].FolderA!.Size);
        Assert.Equal("2024-01-24 19:48:32", preview.Entries[0].FolderA!.Timestamp);
        Assert.Null(preview.Entries[0].FolderB); Assert.Equal("Copy A → B", preview.Entries[0].Action);
    }

    [Fact]
    public void PreviewRetainsUnknownStatusesAndFiltersOnlyRecognizedNoActionEntries()
    {
        var preview = SynchronizationPreview.Parse(JsonNode.Parse("""{"SavedDirs":{"s1":1,"s2":1},"files":{"file":[{"s1":{"Path":"same.txt","Stat":"none"}},{"s2":{"Path":"back.txt","Stat":"copyfrom"}},{"s1":{"Path":"native.txt","Stat":"native-status"}}]}}""")!.AsObject());
        Assert.False(preview.CanChangeInitialRunOptions); Assert.Equal(3, preview.TotalEntries);
        Assert.True(preview.Entries[0].NoAction); Assert.Equal("Copy B → A", preview.Entries[1].Action);
        Assert.Equal("A: native-status", preview.Entries[2].Action); Assert.False(preview.Entries[2].NoAction);
    }

    [Fact]
    public void LargePreviewsReportTheKnownTotalAndLimitDisplayedRows()
    {
        var files = new JsonArray(Enumerable.Range(0, 1005).Select(index => (JsonNode?)new JsonObject { ["s1"] = new JsonObject { ["Path"] = index + ".txt", ["Stat"] = "copyfrom" } }).ToArray());
        var result = SynchronizationPreview.Parse(new JsonObject { ["SavedDirs"] = new JsonObject { ["s1"] = 0, ["s2"] = 0 }, ["files"] = new JsonObject { ["file"] = files } });
        Assert.Equal(1005, result.TotalEntries); Assert.Equal(1000, result.Entries.Count); Assert.True(result.Truncated);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"SavedDirs\":{\"s1\":-1,\"s2\":0}}")]
    [InlineData("{\"SavedDirs\":{\"s1\":0,\"s2\":0},\"files\":{\"file\":[null]}}")]
    public void MalformedPreviewDoesNotMasqueradeAsAnEmptyComparison(string json)
        => Assert.Throws<InvalidOperationException>(() => SynchronizationPreview.Parse(JsonNode.Parse(json)!.AsObject()));

    [Fact]
    public async Task PreviewOkUsesTheSharedSaveAndPreservesDisabledState()
    {
        var task = Definition("Share"); var writes = 0;
        var rest = Rest(() => task, (_, proposed) => { writes++; task = proposed.DeepClone().AsObject(); return Task.FromResult(Acknowledgement()); });
        await Coordinator(rest).SaveSynchronizationPreviewAsync("100");
        Assert.Equal(1, writes); Assert.Equal(0, task["Active"]!.GetValue<int>());
    }

    [Fact]
    public void CapturedV0TransportKeepsTheDetachedTaskEnvelope()
    {
        var task = Definition("Share");
        var body = MoveItRestClient.BuildTaskV0Body(task);
        Assert.Single(body);
        Assert.True(JsonNode.DeepEquals(task, body["Task"]));
        body["Task"]!["Name"] = "detached";
        Assert.Equal("Sync test", task["Name"]!.GetValue<string>());
        task["TT"] = "Tr";
        Assert.Throws<InvalidOperationException>(() => MoveItRestClient.BuildTaskV0Body(task));
    }

    [Fact]
    public async Task SyncUsesCommonSchedulesParametersAndNextActionWriters()
    {
        var task = Definition("Share");
        var rest = Rest(() => task, (_, proposed) => { task = proposed.DeepClone().AsObject(); return Task.FromResult(Acknowledgement()); });
        var request = Request(task);
        request["parameters"] = JsonNode.Parse("""{"replace":true,"Parameter":[{"name":"FolderName","value":"daily","secure":false}]}""");
        request["schedules"] = JsonNode.Parse("""
            [{"index":0,"serverIndex":-1,"id":"","multi":0,"daysOfWeek":["Monday","Tuesday","Wednesday"],
              "daysOfMonth":[],"dateLists":[],"onlyUntilFirstSuccess":true,"failIfNoSuccessInSchedule":true,
              "runEvenIfNotifications":true,"intervals":[{"serverIndex":-1,"id":"","startTime":"00:00","endTime":"23:59","everyMinutes":15}]}]
            """);
        request["nextActions"] = JsonNode.Parse("""
            [{"index":0,"serverIndex":-1,"id":"","type":"Email","doAfter":"Task","doIfSuccess":true,"doIfFailure":false,
              "doIfNoAction":false,"hostId":"smtp","addressTo":"operator@example.test","subject":"Success","message":"Sync succeeded","isHtml":false}]
            """);
        await PatchService(rest).ApplyEditorPatchAsync("100", request);
        Assert.Single(task["Schedules"]!["Schedule"]!.AsArray());
        Assert.Single(task["NextActions"]!["NextAction"]!.AsArray());
        Assert.Contains("daily", task["Parameters"]!.ToJsonString(), StringComparison.Ordinal);
        Assert.Equal("smtp", task["NextActions"]!["NextAction"]![0]!["HostID"]!.GetValue<string>());
        Assert.Equal(15, task["Schedules"]!["Schedule"]![0]!["Frequency"]!["Interval"]![0]!["EveryMinutes"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("FileSystem")]
    [InlineData("Share")]
    [InlineData("FTP")]
    [InlineData("SSHFTP")]
    [InlineData("S3")]
    [InlineData("AzureBlob")]
    [InlineData("SharePoint")]
    [InlineData("GoogleStorage")]
    public void ExistingSyncFoldersShareValidatedSourceProfiles(string type)
    {
        var task = Definition(type);
        var patch = Request(task)["steps"]!.AsArray();
        patch[1]!["patch"]!["Path"] = "/updated-b";
        patch[1]!["patch"]!["UseDefPreserveFileTimestamp"] = 0;
        patch[1]!["patch"]!["PreserveFileTimestamp"] = 1;
        SynchronizationTaskDefinitionComposer.ApplyFolders(task, patch);
        SynchronizationTaskDefinitionComposer.VerifyFolders(patch, task["steps"]);
        Assert.Equal("/updated-b", task["steps"]![1]!["Source"]!["Path"]!.GetValue<string>());
        Assert.Equal(0, task["steps"]![1]!["Source"]!["SyF"]!.GetValue<int>());
        Assert.Equal(1, task["steps"]![1]!["Source"]!["SyT"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("SyF")]
    [InlineData("SyT")]
    [InlineData("SyDel")]
    [InlineData("UseLCaseComp")]
    [InlineData("NewFilesOnly")]
    [InlineData("UseWinCopyFileAPI")]
    public void UnownedOrTraditionalOnlyFieldsAreRejected(string field)
    {
        var task = Definition("Share");
        var folders = Request(task)["steps"]!.AsArray();
        folders[0]!["patch"]![field] = 0;
        Assert.Throws<InvalidOperationException>(() => SynchronizationTaskDefinitionComposer.ApplyFolders(task, folders));
    }

    [Fact]
    public void SyncCannotReorderOrWriteOtherStepKinds()
    {
        var task = Definition("Share");
        var folders = Request(task)["steps"]!.AsArray();
        (folders[0]!["id"], folders[1]!["id"]) = (JsonValue.Create("12"), JsonValue.Create("11"));
        Assert.Throws<InvalidOperationException>(() => SynchronizationTaskDefinitionComposer.ApplyFolders(task, folders));
        folders = Request(task)["steps"]!.AsArray(); folders[0]!["patch"]!["ID"] = "99";
        Assert.Throws<InvalidOperationException>(() => SynchronizationTaskDefinitionComposer.ApplyFolders(task, folders));
        folders = Request(task)["steps"]!.AsArray(); folders[1]!["kind"] = "Destination";
        Assert.Throws<InvalidOperationException>(() => SynchronizationTaskDefinitionComposer.ApplyFolders(task, folders));
    }

    [Theory]
    [InlineData("unidirectional", 0, true)]
    [InlineData("unidirectional", 1, false)]
    [InlineData("unidirectional", 2, true)]
    [InlineData("bidirectional", 0, false)]
    [InlineData("bidirectional", 1, true)]
    public async Task SharedSaveAppliesConfirmedSyncPoliciesAndDisablesTask(string direction, int deletes, bool caseSensitive)
    {
        var task = Definition("Share"); var writes = 0;
        var rest = Rest(() => task, (_, proposed) => { writes++; task = proposed.DeepClone().AsObject(); return Task.FromResult(Acknowledgement()); });
        var request = Request(task);
        request["task"]!["enabled"] = true;
        request["syncChangesConfirmed"] = true;
        request["syncSettings"] = new JsonObject { ["direction"] = direction, ["deletePolicy"] = deletes, ["caseSensitive"] = caseSensitive };
        await PatchService(rest).ApplyEditorPatchAsync("100", request);
        Assert.Equal(1, writes); Assert.Equal(0, task["Active"]!.GetValue<int>());
        for (var index = 0; index < 2; index++)
        {
            var source = task["steps"]![index]!["Source"]!;
            Assert.Equal(index == 0 || direction == "bidirectional" ? 1 : 0, source["SyF"]!.GetValue<int>());
            Assert.Equal(index == 1 || direction == "bidirectional" ? 1 : 0, source["SyT"]!.GetValue<int>());
            Assert.Equal(index == 0 || direction == "bidirectional" ? deletes : 0, source["SyDel"]!.GetValue<int>());
            Assert.Equal(caseSensitive ? 0 : 1, source["UseLCaseComp"]!.GetValue<int>());
        }
    }

    [Fact]
    public void DirectionChangesApplyTheTaskDeletePolicyToBothActiveFolders()
    {
        var task = Definition("Share");
        Assert.True(SynchronizationTaskDefinitionComposer.ApplySettings(task, new JsonObject { ["direction"] = "bidirectional" }));
        Assert.Equal(1, task["steps"]![1]!["Source"]!["SyDel"]!.GetValue<int>());
        Assert.True(SynchronizationTaskDefinitionComposer.ApplySettings(task, new JsonObject { ["direction"] = "unidirectional" }));
        Assert.Equal(0, task["steps"]![1]!["Source"]!["SyDel"]!.GetValue<int>());
    }

    [Fact]
    public void ComparisonChangesPreserveAsymmetricDeletePolicies()
    {
        var task = Definition("Share");
        Assert.True(SynchronizationTaskDefinitionComposer.ApplySettings(task, new JsonObject { ["caseSensitive"] = false }));
        Assert.Equal(1, task["steps"]![0]!["Source"]!["SyDel"]!.GetValue<int>());
        Assert.Equal(0, task["steps"]![1]!["Source"]!["SyDel"]!.GetValue<int>());
    }

    [Fact]
    public async Task PolicyChangesWithoutConfirmationDoNotPut()
    {
        var task = Definition("Share"); var writes = 0;
        var rest = Rest(() => task, (_, proposed) => { writes++; return Task.FromResult(Acknowledgement()); });
        var request = Request(task); request["syncSettings"] = new JsonObject { ["direction"] = "bidirectional" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => PatchService(rest).ApplyEditorPatchAsync("100", request));
        Assert.Equal(0, writes); Assert.Equal(0, task["steps"]![0]!["Source"]!["SyT"]!.GetValue<int>());
    }

    [Theory]
    [InlineData(-3)]
    [InlineData(-2)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(43200)]
    [InlineData(2147483647)]
    public void InitialRunOptionsPreserveUnrelatedNativePolicies(int seconds)
    {
        var task = Definition("Share"); var folders = task["steps"]!.DeepClone();
        Assert.False(SynchronizationTaskDefinitionComposer.ApplySettings(task, new JsonObject { ["sameSeconds"] = seconds }));
        Assert.Equal(seconds, task["SameSecs"]!.GetValue<int>());
        Assert.True(JsonNode.DeepEquals(folders, task["steps"]));
    }

    [Theory]
    [InlineData("{\"direction\":\"wrong\"}")]
    [InlineData("{\"direction\":\"bidirectional\",\"deletePolicy\":2}")]
    [InlineData("{\"deletePolicy\":-1}")]
    [InlineData("{\"deletePolicy\":1.5}")]
    [InlineData("{\"caseSensitive\":1}")]
    [InlineData("{\"sameSeconds\":-4}")]
    [InlineData("{\"sameSeconds\":2147483648}")]
    [InlineData("{\"other\":1}")]
    public void InvalidPoliciesAreRejected(string json)
        => Assert.Throws<InvalidOperationException>(() => SynchronizationTaskDefinitionComposer.ApplySettings(Definition("Share"), JsonNode.Parse(json)!.AsObject()));

    [Theory]
    [InlineData("FileSystem")]
    [InlineData("Share")]
    [InlineData("FTP")]
    [InlineData("SSHFTP")]
    [InlineData("S3")]
    [InlineData("AzureBlob")]
    [InlineData("SharePoint")]
    [InlineData("GoogleStorage")]
    public void HostChangesRetainIdentityAndDropPreviousProtocolSecrets(string type)
    {
        var task = Definition("FTP");
        task["steps"]![0]!["Source"]!["Password"] = "previous-secret";
        task["steps"]![0]!["Source"]!["ClientSecret"] = "previous-client-secret";
        var folders = Request(Definition(type))["steps"]!.AsArray();
        folders[0]!["patch"]!["HostID"] = type == "FileSystem" ? "0" : "new-host";
        folders[1] = Request(task)["steps"]![1]!.DeepClone();
        SynchronizationTaskDefinitionComposer.ApplyFolders(task, folders);
        var source = task["steps"]![0]!["Source"]!.AsObject();
        Assert.Equal("11", source["ID"]!.GetValue<string>());
        Assert.Equal(type, source["Type"]!.GetValue<string>());
        Assert.DoesNotContain("previous-secret", source.ToJsonString());
        Assert.DoesNotContain("previous-client-secret", source.ToJsonString());
        Assert.Equal(1, source["SyF"]!.GetValue<int>()); Assert.Equal(0, source["SyT"]!.GetValue<int>());
    }

    [Fact]
    public async Task LocalBrowserUsesMoveItServerPathsWithoutCheckingOutHostZero()
    {
        JsonObject? sent = null;
        var rest = Stub<IMoveItRestClient>((method, args) => method.Name == nameof(IMoveItRestClient.BrowseFileSystemHostV0Async)
            ? Capture((JsonObject)args[0]!) : throw new NotSupportedException(method.Name));
        Task<JsonObject> Capture(JsonObject request)
        {
            sent = request;
            return Task.FromResult(JsonNode.Parse("""{"CurrentDir":"C:\\sync","Dirs":{"Dir":[{"DirName":"C:\\sync\\child"}]}}""")!.AsObject());
        }
        var service = new TraditionalTaskEditorFolderBrowseService(rest, Microsoft.Extensions.Logging.Abstractions.NullLogger<TraditionalTaskEditorFolderBrowseService>.Instance);
        var result = await service.BrowseAsync("100", "0", "Source", @"C:\sync");
        Assert.Equal(@"C:\sync", sent!["dirname"]!.GetValue<string>());
        Assert.Equal(@"C:\sync", result.CurrentPath);
        Assert.Equal(@"C:\", result.ParentPath);
        Assert.Single(result.Items);
        Assert.Equal(@"C:\sync\child", result.Items[0].Path);
    }

    [Fact]
    public void ExistingBidirectionalTasksRetainBothFromAndToRoles()
    {
        var task = Definition("Share");
        foreach (var wrapper in task["steps"]!.AsArray()) { wrapper!["Source"]!["SyF"] = 1; wrapper!["Source"]!["SyT"] = 1; }
        var before = task.DeepClone().AsObject();
        var folders = Request(task)["steps"]!.AsArray(); folders[1]!["patch"]!["Path"] = "/bidirectional-b";
        SynchronizationTaskDefinitionComposer.ApplyFolders(task, folders);
        SynchronizationTaskDefinitionComposer.VerifyNativeState(before, task);
        Assert.All(task["steps"]!.AsArray(), wrapper =>
        {
            Assert.Equal(1, wrapper!["Source"]!["SyF"]!.GetValue<int>());
            Assert.Equal(1, wrapper!["Source"]!["SyT"]!.GetValue<int>());
        });
    }

    [Fact]
    public void NewFoldersReceiveUniqueNativeIdsAndSyncRoles()
    {
        var task = Definition("Share"); task["steps"] = new JsonArray(); task["NextEID"] = 11;
        var folders = Request(Definition("Share"))["steps"]!.AsArray();
        foreach (var item in folders.OfType<JsonObject>()) { item["id"] = ""; item["patch"]!.AsObject().Remove("ID"); }
        SynchronizationTaskDefinitionComposer.ApplyFolders(task, folders);
        Assert.Equal("11", task["steps"]![0]!["Source"]!["ID"]!.GetValue<string>());
        Assert.Equal("12", task["steps"]![1]!["Source"]!["ID"]!.GetValue<string>());
        Assert.Equal(13, task["NextEID"]!.GetValue<int>());
        Assert.Equal(1, task["steps"]![0]!["Source"]!["SyF"]!.GetValue<int>());
        Assert.Equal(1, task["steps"]![1]!["Source"]!["SyT"]!.GetValue<int>());
    }

    [Fact]
    public async Task IndeterminatePutOrLostReadbackBlocksAnAutomaticRetry()
    {
        var task = Definition("Share"); var writes = 0;
        var rest = Rest(() => task, (_, _) => { writes++; throw new HttpRequestException("Connection lost"); });
        await Assert.ThrowsAsync<TraditionalTaskPartialSaveException>(() => new TaskJsonPersistenceService(rest)
            .UpdateFreshAsync("100", MoveItTaskKind.Synchronization, value => value, requireReadback: true));
        Assert.Equal(1, writes);
        rest = Rest(() => task, (_, _) => Task.FromResult(new JsonObject { ["id"] = "wrong", ["status"] = "success", ["errorcode"] = 0 }));
        await Assert.ThrowsAsync<TraditionalTaskPartialSaveException>(() => new TaskJsonPersistenceService(rest)
            .UpdateFreshAsync("100", MoveItTaskKind.Synchronization, value => value));
    }

    [Fact]
    public async Task SyncTypeAndIdentityAreCheckedBeforeAnyWrite()
    {
        var task = Definition("Share"); var writes = 0;
        var rest = Rest(() => task, (_, _) => { writes++; return Task.FromResult(Acknowledgement()); });
        await Assert.ThrowsAsync<InvalidOperationException>(() => new TaskJsonPersistenceService(rest)
            .UpdateFreshAsync("100", MoveItTaskKind.Traditional, value => value));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new TaskJsonPersistenceService(rest)
            .UpdateFreshAsync("100", MoveItTaskKind.Synchronization, value => { value["ID"] = "other"; return value; }));
        Assert.Equal(0, writes);
    }

    [Theory]
    [InlineData("", null, "UNSCHEDULED", true)]
    [InlineData("COS", null, "DISABLED", true)]
    [InlineData("", false, "ENABLED", true)]
    [InlineData("COS", false, "UNSCHEDULED", true)]
    [InlineData("", true, "INCOMPLETE", false)]
    [InlineData("COS", null, "INCOMPLETE", false)]
    public async Task EditorManualRunEligibilityMatchesExistingRunAction(string type, bool? runFlag, string scheduled, bool expected)
    {
        var task = Definition("Share"); task["TT"] = type; task["Scheduled"] = scheduled;
        if (runFlag is not null) task["xRun"] = runFlag.Value;
        var rest = Rest(() => task);
        var model = await new TraditionalTaskEditorService(rest, Coordinator(rest)).GetDefinitionAsync("100");
        Assert.Equal(expected, model.Task.CanRun);
    }

    public static IEnumerable<object[]> MailOverrideModes()
    {
        foreach (var type in new[] { "POP3", "SMTP" })
        {
            yield return new object[] { type, "legacy", "", false };
            yield return new object[] { type, "microsoft365", "tenantid2", true };
            yield return new object[] { type, "custom", "", true };
            yield return new object[] { type, "customMicrosoft", "", true };
            yield return new object[] { type, "customMicrosoftQuery", "", true };
            yield return new object[] { type, "customMicrosoftHttp", "", true };
            yield return new object[] { type, "inherit", "hosttenant", false };
        }
    }

    [Theory]
    [MemberData(nameof(MailOverrideModes))]
    public async Task MailOverrideReadModelKeepsModeAndProviderAndSuppressesSecrets(string type, string mode, string tenant, bool secretConfigured)
    {
        var task = Definition("Share"); task["TT"] = "";
        var kind = type == "POP3" ? "Source" : "Destination";
        var mail = new JsonObject { ["ID"] = "12", ["Type"] = type, ["HostID"] = "host-" + type, ["UseDefUser"] = mode == "inherit" ? 1 : 0,
            ["UseOAuth"] = mode == "legacy" ? 0 : 1, ["Username"] = "override-user", ["AddressTo"] = "test@example.test",
            ["Password"] = "PRIVATE_PASSWORD", ["OAuthClientSecret"] = "PRIVATE_SECRET", ["OAuthClientID"] = "override-client",
            ["OAuthScope"] = mode is "microsoft365" or "customMicrosoftQuery" or "customMicrosoftHttp" ? "https://outlook.office365.com/.default" : "custom-scope",
            ["OAuthTokenURL"] = mode switch {
                "microsoft365" or "customMicrosoft" => "https://login.microsoftonline.com/tenantid2/oauth2/v2.0/token",
                "customMicrosoftQuery" => "https://login.microsoftonline.com/tenantid2/oauth2/v2.0/token?extra=value",
                "customMicrosoftHttp" => "http://login.microsoftonline.com/tenantid2/oauth2/v2.0/token",
                _ => "https://identity.example.test/token" } };
        task["steps"] = new JsonArray(new JsonObject { [kind] = mail });
        var rest = Rest(() => task, mailOAuth: true);
        var model = await new TraditionalTaskEditorService(rest, Coordinator(rest)).GetDefinitionAsync("100");
        var source = model.Task.Steps[0].Source;
        var destination = model.Task.Steps[0].Destination;
        var step = source is not null
            ? new { source.EmailUseOAuth, source.EmailOAuthTenantId, source.EmailOAuthClientSecretConfigured, source.PasswordConfigured, source.EmailOAuthClientId, source.EmailOAuthScope, source.EmailOAuthTokenUrl }
            : destination is not null
                ? new { destination.EmailUseOAuth, destination.EmailOAuthTenantId, destination.EmailOAuthClientSecretConfigured, destination.PasswordConfigured, destination.EmailOAuthClientId, destination.EmailOAuthScope, destination.EmailOAuthTokenUrl }
                : throw new InvalidOperationException("Mail fixture did not produce a Source or Destination.");
        Assert.Equal(mode != "legacy", step.EmailUseOAuth);
        Assert.Equal(tenant, step.EmailOAuthTenantId);
        Assert.Equal(secretConfigured, step.EmailOAuthClientSecretConfigured);
        Assert.Equal(mode == "legacy", step.PasswordConfigured);
        Assert.Equal(mode == "inherit" ? "host-client" : "override-client", step.EmailOAuthClientId);
        if (mode.StartsWith("custom", StringComparison.Ordinal))
        {
            Assert.Equal(mail["OAuthScope"]!.GetValue<string>(), step.EmailOAuthScope);
            Assert.Equal(mail["OAuthTokenURL"]!.GetValue<string>(), step.EmailOAuthTokenUrl);
        }
        Assert.DoesNotContain("PRIVATE_", System.Text.Json.JsonSerializer.Serialize(model));
    }

    [Fact]
    public async Task EmitsProductionEditorFixturesAndFiltersTheEightSupportedHostTypes()
    {
        var fixtures = new Dictionary<string, object>();
        foreach (var type in SynchronizationTaskDefinitionComposer.HostTypes)
        {
            var task = Definition(type);
            var rest = Rest(() => task);
            var model = await new TraditionalTaskEditorService(rest, Coordinator(rest)).GetDefinitionAsync("100");
            Assert.Equal("Synchronization", model.Task.Type);
            Assert.Equal(-1, model.Task.Synchronization!.SameSeconds);
            Assert.All(model.SourceHosts, host => Assert.Contains(host.TypeCode, SynchronizationTaskDefinitionComposer.HostTypes));
            Assert.Equal(8, model.HostCapabilities.Count);
            Assert.All(model.Task.Steps, step => Assert.Equal("Source", step.Kind));
            fixtures["Sync-" + type] = new { model.EditVersion, model.Task, model.HostCapabilities, model.SourceHosts, model.DestinationHosts,
                model.ScriptOptions, model.SmtpHosts, model.TaskOptions, model.DateListOptions, model.TlsCertificates, model.SshClientKeys,
                preferences = new { autoSave = false }, fields = Array.Empty<object>(), folders = Array.Empty<object>(), metadata = new { }, audit = new { } };
        }
        foreach (var type in new[] { "Traditional", "NewSync", "Sync-Bidirectional" })
        {
            var task = Definition("Share");
            if (type == "Traditional") task["TT"] = "Tr";
            else if (type == "NewSync") task["steps"] = new JsonArray();
            else foreach (var wrapper in task["steps"]!.AsArray()) { wrapper!["Source"]!["SyF"] = 1; wrapper!["Source"]!["SyT"] = 1; }
            var rest = Rest(() => task);
            var model = await new TraditionalTaskEditorService(rest, Coordinator(rest)).GetDefinitionAsync("100");
            fixtures[type] = new { model.EditVersion, model.Task, model.HostCapabilities, model.SourceHosts, model.DestinationHosts,
                model.ScriptOptions, model.SmtpHosts, model.TaskOptions, model.DateListOptions, model.TlsCertificates, model.SshClientKeys,
                preferences = new { autoSave = false }, fields = Array.Empty<object>(), folders = Array.Empty<object>(), metadata = new { }, audit = new { } };
        }
        foreach (var type in new[] { "Advanced", "NewAdvanced", "AdvancedBranches" })
        {
            var task = AdvancedTaskEditorTests.Capture(type=="AdvancedBranches"?4:0); task["ID"]="100"; task["Name"]="Advanced test";task["xAlter"]=1;
            if(type=="NewAdvanced")task["steps"]=new JsonArray();
            var rest=Rest(()=>task);
            var model=await new TraditionalTaskEditorService(rest,Coordinator(rest)).GetDefinitionAsync("100");
            Assert.Equal("Advanced",model.Task.Type);Assert.NotNull(model.Task.AdvancedFlow);
            fixtures[type]=new {model.EditVersion,model.Task,model.HostCapabilities,model.SourceHosts,model.DestinationHosts,model.ScriptOptions,model.SmtpHosts,model.TaskOptions,model.DateListOptions,model.TlsCertificates,model.SshClientKeys,
                preferences=new {autoSave=false},fields=Array.Empty<object>(),folders=Array.Empty<object>(),metadata=new {},audit=new {}};
        }
        var fixturePath = Environment.GetEnvironmentVariable("MAUI_SYNC_EDITOR_FIXTURE_PATH");
        if (!string.IsNullOrWhiteSpace(fixturePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(fixturePath))!);
            await File.WriteAllTextAsync(fixturePath, JsonSerializer.Serialize(fixtures, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        }
    }

    private static JsonObject Definition(string type)
    {
        var task = JsonNode.Parse("""
        {"ID":"100","TT":"Sy","Name":"Sync test","Active":0,"xAlter":1,"CacheNames":"random","UseDefStateCaching":1,
         "SameSecs":-1,"NextEID":13,"NativeOnly":"retain","Info":{"Description":"Sync folders","Notes":""},"Schedules":{"Schedule":[]},
         "NextActions":{"NextAction":[]},"steps":[]}
        """)!.AsObject();
        for (var index = 0; index < 2; index++)
            task["steps"]!.AsArray().Add(new JsonObject { ["WrapperField"] = "keep", ["Source"] = new JsonObject
            {
                ["ID"] = index == 0 ? "11" : "12", ["Type"] = type, ["HostID"] = type == "FileSystem" ? "0" : "host-" + type,
                ["Path"] = index == 0 ? "/a" : "/b", ["FileMask"] = "*.*", ["Disabled"] = 0, ["ExFo"] = "", ["ExFile"] = "",
                ["SyF"] = index == 0 ? 1 : 0, ["SyT"] = index == 0 ? 0 : 1, ["SyDel"] = index == 0 ? 1 : 0,
                ["UseLCaseComp"] = 0, ["UseDefPreserveFileTimestamp"] = index == 0 ? 1 : 0, ["PreserveFileTimestamp"] = index == 0 ? 0 : 1,
                ["UseDefRetryCount"] = 1, ["UseDefRetryTimeoutSecs"] = 1, ["UseDefRescanSecs"] = 1, ["UDMxFi"] = 1, ["UDMxBy"] = 1,
                ["UseDefUser"] = 1, ["UseDefClientKey"] = 1, ["UseDefConnTimeoutSecs"] = 1, ["UseDefDataTimeoutSecs"] = 1,
                ["UseDefClientCert"] = 1, ["UseDefWinCopyFileAPI"] = 1, ["UseDefBucket"] = 1, ["UseDefContainer"] = 1,
                ["UseDefDocumentLibrary"] = 1, ["NativeSource"] = "retain", ["Password"] = null
            }});
        return task;
    }

    private static JsonObject Request(JsonObject task) => new()
    {
        ["task"] = new JsonObject { ["name"] = "Sync edited", ["description"] = "Sync folders", ["operatorNotes"] = "", ["enabled"] = false,
            ["cacheNames"] = "random", ["useDefaultStateCaching"] = true, ["useGlobalTaskLogRetention"] = true, ["autoRerun"] = false },
        ["steps"] = new JsonArray(task["steps"]!.AsArray().Select((wrapper,index) => (JsonNode?)new JsonObject
        {
            ["kind"] = "Source", ["id"] = wrapper!["Source"]!["ID"]!.ToString(), ["index"] = index,
            ["patch"] = new JsonObject { ["ID"] = wrapper!["Source"]!["ID"]!.DeepClone(), ["HostID"] = wrapper!["Source"]!["HostID"]!.DeepClone(),
                ["Type"] = wrapper!["Source"]!["Type"]!.DeepClone(), ["Path"] = wrapper!["Source"]!["Path"]!.DeepClone(), ["FileMask"] = "*.*" }
        }).ToArray()), ["schedules"] = new JsonArray(), ["nextActions"] = new JsonArray()
    };

    private static JsonObject Acknowledgement() => new() { ["id"] = "100", ["status"] = "success", ["errorcode"] = 0, ["refreshRequired"] = false };
    private static IMoveItRestClient Rest(Func<JsonObject> read, Func<string, JsonObject, Task<JsonObject>>? write = null, Func<Task<JsonObject>>? clear = null, Func<Task<JsonObject>>? preview = null, bool mailOAuth = false)
        => Stub<IMoveItRestClient>((method, args) => method.Name switch
        {
            nameof(IMoveItRestClient.CheckoutTaskV0Async) or nameof(IMoveItRestClient.ReleaseTaskCheckoutV0Async) => Task.CompletedTask,
            nameof(IMoveItRestClient.GetTaskAsync) => Task.FromResult(read().DeepClone().AsObject()),
            nameof(IMoveItRestClient.UpdateTaskV0Async) => write is null ? throw new NotSupportedException("Unexpected PUT") : write((string)args[0]!, (JsonObject)args[1]!),
            nameof(IMoveItRestClient.ClearSynchronizationCompareV0Async) => clear is null ? Task.FromResult(new JsonObject { ["id"] = "-1", ["status"] = "success", ["errorcode"] = 0 }) : clear(),
            nameof(IMoveItRestClient.GetSynchronizationCompareV0Async) => preview is null ? Task.FromResult(JsonNode.Parse("""{"SavedDirs":{"s1":0,"s2":0},"files":{"file":{"s1":{"Path":"reagentold.xml","b":0,"Stat":"copyfrom","Stamp":"2024-01-24 19:48:32","Size":1108}}}}""")!.AsObject()) : preview(),
            nameof(IMoveItRestClient.UpdateTaskAsync) => throw new InvalidOperationException("Sync must never use v1 PUT"),
            nameof(IMoveItRestClient.TaskNameExistsForOtherTaskAsync) => Task.FromResult(false),
            nameof(IMoveItRestClient.GetHostsAsync) => Task.FromResult<IReadOnlyList<JsonObject>>(SynchronizationTaskDefinitionComposer.HostTypes
                .Concat(new[] { "POP3", "SMTP", "siLock" }).Select(type => new JsonObject { [type] = new JsonObject
                { ["ID"] = type == "FileSystem" ? "0" : "host-" + type, ["Name"] = type + " host", ["DefRetryCount"] = 3, ["DefRetryTimeoutSecs"] = 30,
                  ["DefRescanSecs"] = 4, ["DefMxFi"] = 5, ["DefMxBy"] = 1024, ["DefPreserveFileTimestamp"] = 1,
                  ["UseOAuth"] = (type is "SMTP" or "POP3") && mailOAuth ? 1 : 0,
                  ["OAuthClientID"] = (type is "SMTP" or "POP3") && mailOAuth ? "host-client" : "",
                  ["OAuthScope"] = (type is "SMTP" or "POP3") && mailOAuth ? "https://outlook.office365.com/.default" : "",
                  ["OAuthTokenURL"] = (type is "SMTP" or "POP3") && mailOAuth ? "https://login.microsoftonline.com/hosttenant/oauth2/v2.0/token" : "",
                  ["DefUsername"] = "user", ["DefBucket"] = "bucket", ["DefContainer"] = "container", ["DefDocumentLibrary"] = "Documents" } }).ToArray()),
            _ when method.ReturnType == typeof(Task<IReadOnlyList<JsonObject>>) => Task.FromResult<IReadOnlyList<JsonObject>>(Array.Empty<JsonObject>()),
            _ => throw new NotSupportedException(method.Name)
        });
    private static ITaskCrudCoordinator Coordinator(IMoveItRestClient rest)
    {
        var com = Stub<IMoveItComClient>((method, _) => method.Name == nameof(IMoveItComClient.GetTaskConfigurationAsync)
            ? Task.FromResult(new MoveItTaskConfiguration("100", "Sync test", "<Task ID=\"100\" TT=\"Sy\"><Schedules /></Task>")) : throw new NotSupportedException(method.Name));
        var persistence = new TraditionalTaskPersistenceService(rest, com, new TraditionalTaskDefinitionComposer(new TraditionalTaskHostProfileCatalog()));
        return new TaskCrudCoordinator(new ITaskCrudStrategy[] { new TraditionalTaskCrudStrategy(persistence),
            new NativeTaskCrudStrategy(MoveItTaskKind.Advanced, com), new NativeTaskCrudStrategy(MoveItTaskKind.Synchronization, com) }, persistence, rest, com);
    }
    private static TaskDefinitionPatchService PatchService(IMoveItRestClient rest)
        => new(rest, Coordinator(rest), new TraditionalTaskDefinitionComposer(new TraditionalTaskHostProfileCatalog()));
    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, HostSharedCrudTests.CrudProxy>();
        ((HostSharedCrudTests.CrudProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}
