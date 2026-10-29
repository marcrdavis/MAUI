using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.SystemSettings;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class SystemSettingsPart2Tests
{
    [Fact]
    public async Task AllGlobalsSectionsSaveInOneEnvelopeAndPreserveUnownedValues()
    {
        using var native = new SystemSettingsTests.NativeHandler(); var service = SystemSettingsTests.Service(native);
        var opened = await service.ReadAsync(default);
        var update = new SystemSettingsUpdate(opened.EditVersion,
            EventLog: opened.EventLog! with { EventLogName = "MOVEit", EventLogLevel = 40, EventLogAuditLevel = 30 },
            Tasks: opened.Tasks! with { MaxSimulTasks = 25, AllowMultRuns = true, SuppressQueuedTasksAtStartup = true, PollIntervalSecs = 7, AS2PollIntervalSecs = 5 },
            StateFile: new() { DefStateCacheTime = 2, DefStateCacheUnit = "Minutes" }, Tamper: new() { Enabled = false });
        var saved = await service.SaveAsync(update, default);
        Assert.Equal(update.EventLog, saved.EventLog); Assert.Equal(update.Tasks, saved.Tasks);
        Assert.Equal(update.StateFile, saved.StateFile); Assert.Equal(update.Tamper, saved.Tamper);
        Assert.Equal(opened.Logging, saved.Logging); Assert.True(native.Writes == 1);
        Assert.True(native.PolicyWrites == 0 && native.TrustedWrites == 0);
        Assert.DoesNotContain(native.Calls, item => item.Contains("systemSettings"));
        var globals = native.Data["Globals"]!;
        Assert.Equal(1, globals["AllowMultRuns"]!.GetValue<int>());
        Assert.Equal(1, globals["SuppressHashing"]!.GetValue<int>());
        Assert.Equal(5, globals["AS2PollIntervalSecs"]!.GetValue<int>());
        Assert.Equal("IPWorks", globals["SFTPLib"]!.GetValue<string>());
        Assert.Equal("preserve", globals["FutureSetting"]!["values"]![1]!.GetValue<string>());
        Assert.False(native.CheckedOut);
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(0, null)]
    [InlineData(5, "Hours")]
    [InlineData(2, "Minutes")]
    public async Task StateCacheMatchesNativeSentinelAndTimedPatterns(int time, string? unit)
    {
        using var native = new SystemSettingsTests.NativeHandler();
        native.Data["Globals"]!["DefStateCacheTime"] = 2; native.Data["Globals"]!["DefStateCacheUnit"] = "Minutes";
        var service = SystemSettingsTests.Service(native); var opened = await service.ReadAsync(default);
        var state = new SystemStateFileSettings { DefStateCacheTime = time, DefStateCacheUnit = unit };
        Assert.Equal(state, (await service.SaveAsync(new(opened.EditVersion, StateFile: state), default)).StateFile);
        Assert.Equal(time > 0, native.Data["Globals"]!.AsObject().ContainsKey("DefStateCacheUnit"));
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    public async Task TamperCheckboxInvertsNativeSuppression(bool enabled, int suppression)
    {
        using var native = new SystemSettingsTests.NativeHandler(); var service = SystemSettingsTests.Service(native);
        var opened = await service.ReadAsync(default);
        await service.SaveAsync(new(opened.EditVersion, Tamper: new() { Enabled = enabled }), default);
        Assert.Equal(suppression, native.Data["Globals"]!["SuppressHashing"]!.GetValue<int>());
    }

    [Fact]
    public async Task MissingOptionalAs2PollingUsesNativeZeroDefaultAndWritesExplicitValue()
    {
        using var native = new SystemSettingsTests.NativeHandler(); var service = SystemSettingsTests.Service(native);
        var opened = await service.ReadAsync(default); Assert.Equal(0, opened.Tasks!.AS2PollIntervalSecs);
        await service.SaveAsync(new(opened.EditVersion, Tasks: opened.Tasks with { AS2PollIntervalSecs = 5 }), default);
        Assert.Equal(5, native.Data["Globals"]!["AS2PollIntervalSecs"]!.GetValue<int>());
        var json = JsonSerializer.SerializeToNode(opened.Tasks, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        Assert.Contains(json, pair => pair.Key == "as2PollIntervalSecs");
        Assert.DoesNotContain(json, pair => pair.Key == "aS2PollIntervalSecs");
    }

    [Theory]
    [InlineData("event")]
    [InlineData("task")]
    [InlineData("state")]
    [InlineData("lockout")]
    [InlineData("trusted")]
    public async Task InvalidSectionPreventsEveryMutationEvenWithValidGlobalsChange(string invalid)
    {
        using var native = new SystemSettingsTests.NativeHandler(); var service = SystemSettingsTests.Service(native);
        var opened = await service.ReadAsync(default); var lockout = await service.ReadLockoutAsync(default); native.Calls.Clear();
        var update = new SystemSettingsUpdate(opened.EditVersion, Tamper: new() { Enabled = false });
        update = invalid switch
        {
            "event" => update with { EventLog = opened.EventLog! with { EventLogAuditLevel = 20 } },
            "task" => update with { Tasks = opened.Tasks! with { AS2PollIntervalSecs = -1 } },
            "state" => update with { StateFile = new() { DefStateCacheTime = 0, DefStateCacheUnit = "Hours" } },
            "lockout" => update with { Lockout = lockout.Policy with { IPLockoutEnabled = true, IPLockoutTries = 0 } },
            _ => update with { TrustedIPs = ["1.1.1.1", "invalid"] }
        };
        await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(update, default));
        Assert.Empty(native.Calls); Assert.True(native.Writes == 0 && native.PolicyWrites == 0 && native.TrustedWrites == 0);
    }

    [Fact]
    public async Task PolicyReadUsesCapturedGetEndpointsAndRequiresNativeAdmin()
    {
        using var native = new SystemSettingsTests.NativeHandler();
        var state = await SystemSettingsTests.Service(native).ReadLockoutAsync(default);
        Assert.Equal(new[] { "GET /api/v0/systemSettings/iplockout", "GET /api/v0/systemSettings/trustedip" }, native.Calls);
        Assert.Equal(new[] { "1.1.1.1" }, state.TrustedIPs); Assert.NotEmpty(state.TrustedEditVersion);
        Assert.False(state.Policy.IPLockoutEnabled); Assert.True(state.Policy.IPAutoUnlockEnabled);
        native.Calls.Clear();
        var error = await Assert.ThrowsAsync<MoveItApiException>(() => SystemSettingsTests.Service(native, false).ReadLockoutAsync(default));
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode); Assert.Empty(native.Calls);
    }

    [Fact]
    public async Task PolicySaveAcceptsEmptyNativeResponseAndPreservesUnknownFields()
    {
        using var native = new SystemSettingsTests.NativeHandler(); var service = SystemSettingsTests.Service(native);
        var opened = await service.ReadAsync(default); var lockout = await service.ReadLockoutAsync(default);
        var desired = lockout.Policy with { IPLockoutEnabled = true, IPLockoutUnits = "days", UserAutoUnlockEnabled = false, UserUnlockUnits = "hours" };
        var saved = await service.SaveAsync(new(opened.EditVersion, Lockout: desired, LockoutVersion: lockout.EditVersion), default);
        Assert.Equal(desired, saved.Lockout!.Policy); Assert.True(native.PolicyWrites == 1 && native.Writes == 0 && native.TrustedWrites == 0);
        Assert.Equal("preserve", native.Policy["FuturePolicy"]!.GetValue<string>());
        Assert.True(native.Policy["IPLockoutEnabled"]!.GetValue<bool>());
        Assert.Equal(opened.Logging, saved.Logging); Assert.False(native.CheckedOut);
    }

    [Fact]
    public async Task TrustedIpSaveAddsBeforeRemovingUsingOneAddressPerCapturedCall()
    {
        using var native = new SystemSettingsTests.NativeHandler(); var service = SystemSettingsTests.Service(native);
        var opened = await service.ReadAsync(default); var lockout = await service.ReadLockoutAsync(default);
        var saved = await service.SaveAsync(new(opened.EditVersion, TrustedIPs: ["2.2.2.2", "2001:db8::1"], TrustedVersion: lockout.TrustedEditVersion), default);
        Assert.True(native.Writes == 0 && native.PolicyWrites == 0 && native.TrustedWrites == 3);
        Assert.Equal(new[] { "POST", "POST", "DELETE" }, native.PolicyRequests.Select(item => item.Method));
        Assert.Equal(new[] { "2.2.2.2", "2001:db8::1" }, saved.Lockout!.TrustedIPs);
        Assert.Equal("1.1.1.1", native.PolicyRequests.Last().Body["TrustedIP"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task EmptyTrustedListRemovesLastEntryWithoutPostingEmptyAddress()
    {
        using var native = new SystemSettingsTests.NativeHandler(); var service = SystemSettingsTests.Service(native);
        var opened = await service.ReadAsync(default); var lockout = await service.ReadLockoutAsync(default);
        var saved = await service.SaveAsync(new(opened.EditVersion, TrustedIPs: [], TrustedVersion: lockout.TrustedEditVersion), default);
        Assert.Empty(saved.Lockout!.TrustedIPs); Assert.True(native.TrustedWrites == 1);
        Assert.Equal("DELETE", native.PolicyRequests[0].Method);
    }

    [Theory]
    [InlineData("policy")]
    [InlineData("trusted")]
    public async Task StalePolicyOrTrustedListBlocksAllWritesIncludingGlobals(string changed)
    {
        using var native = new SystemSettingsTests.NativeHandler(); var service = SystemSettingsTests.Service(native);
        var opened = await service.ReadAsync(default); var lockout = await service.ReadLockoutAsync(default);
        if (changed == "policy") native.Policy["IPLockoutTries"] = 7; else native.Trusted.Add("3.3.3.3");
        var update = new SystemSettingsUpdate(opened.EditVersion, Tamper: new() { Enabled = false },
            Lockout: lockout.Policy with { UserLockoutEnabled = true }, LockoutVersion: lockout.EditVersion, TrustedIPs: [], TrustedVersion: lockout.TrustedEditVersion);
        await Assert.ThrowsAsync<ObjectEditConflictException>(() => service.SaveAsync(update, default));
        Assert.True(native.Writes == 0 && native.PolicyWrites == 0 && native.TrustedWrites == 0);
    }

    [Theory]
    [InlineData("validation")]
    [InlineData("network")]
    [InlineData("readback")]
    [InlineData("cancel")]
    public async Task NativePolicyFailureIsNotReplayedAndReportsPossiblePartialSave(string fault)
    {
        using var native = new SystemSettingsTests.NativeHandler(); var service = SystemSettingsTests.Service(native);
        var opened = await service.ReadAsync(default); var lockout = await service.ReadLockoutAsync(default); native.PolicyFault = fault;
        await Assert.ThrowsAsync<SystemSettingsPartialSaveException>(() => service.SaveAsync(new(opened.EditVersion,
            Tamper: new() { Enabled = false }, Lockout: lockout.Policy with { IPLockoutEnabled = true }, LockoutVersion: lockout.EditVersion), default));
        Assert.True(native.Writes == 1 && native.PolicyWrites == 1); Assert.False(native.CheckedOut);
    }

    [Fact]
    public async Task PartialTrustedChangesStopAtFailureWithoutReplayOrUndoingAcceptedChanges()
    {
        using var native = new SystemSettingsTests.NativeHandler(); var service = SystemSettingsTests.Service(native);
        var opened = await service.ReadAsync(default); var lockout = await service.ReadLockoutAsync(default); native.FailTrustedWrite = 2;
        await Assert.ThrowsAsync<SystemSettingsPartialSaveException>(() => service.SaveAsync(new(opened.EditVersion,
            TrustedIPs: ["2.2.2.2", "3.3.3.3"], TrustedVersion: lockout.TrustedEditVersion), default));
        Assert.True(native.TrustedWrites == 2); Assert.Equal(new[] { "1.1.1.1", "2.2.2.2" }, native.Trusted);
        Assert.DoesNotContain(native.PolicyRequests, item => item.Method == "DELETE"); Assert.False(native.CheckedOut);
    }

    [Fact]
    public async Task PolicyChangeAfterGlobalsCommitIsReportedAsPartialWithoutOverwritingIt()
    {
        using var native = new SystemSettingsTests.NativeHandler(); var service = SystemSettingsTests.Service(native);
        var opened = await service.ReadAsync(default); var lockout = await service.ReadLockoutAsync(default);
        native.AfterPut = () => native.Policy["IPLockoutTries"] = 9;
        await Assert.ThrowsAsync<SystemSettingsPartialSaveException>(() => service.SaveAsync(new(opened.EditVersion,
            Tamper: new() { Enabled = false }, Lockout: lockout.Policy with { UserLockoutEnabled = true }, LockoutVersion: lockout.EditVersion), default));
        Assert.True(native.Writes == 1 && native.PolicyWrites == 0);
        Assert.Equal(9, native.Policy["IPLockoutTries"]!.GetValue<int>()); Assert.False(native.CheckedOut);
    }

    [Fact]
    public async Task EquivalentTrustedIpOrderingDoesNotProduceFalseRevisionConflict()
    {
        using var native = new SystemSettingsTests.NativeHandler(); native.Trusted.Add("2001:db8::1");
        var service = SystemSettingsTests.Service(native); var opened = await service.ReadAsync(default); var lockout = await service.ReadLockoutAsync(default);
        native.Trusted.Reverse();
        var result = await service.SaveAsync(new(opened.EditVersion, TrustedIPs: ["1.1.1.1", "2001:db8:0:0:0:0:0:1"], TrustedVersion: lockout.TrustedEditVersion), default);
        Assert.NotNull(result.Lockout); Assert.True(native.TrustedWrites == 0);
    }

    [Fact]
    public void DuplicateEquivalentIpv6AndMalformedAddressesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => SystemTrustedIPs.Validate(["2001:db8::1", "2001:db8:0:0:0:0:0:1"]));
        foreach (var address in new[] { "", "1", "127.1", "invalid", "1.1.1.999", " 1.1.1.1" })
            Assert.Throws<ArgumentException>(() => SystemTrustedIPs.Validate([address]));
        Assert.Throws<InvalidOperationException>(() => SystemTrustedIPs.Read(new JsonObject()));
        Assert.Throws<InvalidOperationException>(() => SystemTrustedIPs.Read(JsonNode.Parse("{\"TrustedIP\":[{}]}")!.AsObject()));
    }

    [Fact]
    public void BrowserRequestRoundTripsNativeAcronymsAndRequiresCompletePolicyFields()
    {
        using var native = new SystemSettingsTests.NativeHandler();
        var policy = SystemLockoutPolicy.Read(native.Policy);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var request = new SystemSettingsSaveRequest(null, "global", Lockout: policy, LockoutVersion: "policy", TrustedIPs: ["1.1.1.1"], TrustedVersion: "trusted");
        var json = JsonSerializer.SerializeToNode(request, options)!;
        Assert.True(json["lockout"]!["ipAutoUnlockEnabled"]!.GetValue<bool>());
        Assert.Equal(policy, JsonSerializer.Deserialize<SystemSettingsSaveRequest>(json.ToJsonString(), options)!.Lockout);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SystemLockoutPolicy>("{}"));
    }
}
