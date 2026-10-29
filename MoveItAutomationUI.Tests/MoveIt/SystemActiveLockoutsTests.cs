using System.Net;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.SystemSettings;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class SystemActiveLockoutsTests
{
    [Fact]
    public async Task ReadUsesBothCapturedV1ListsAndKeepsNativeTimestamp()
    {
        using var native = new SystemSettingsTests.NativeHandler();
        var result = await SystemSettingsTests.Service(native).ReadActiveLockoutsAsync(default);
        Assert.Equal("192.168.198.1", result.Addresses[0].Value); Assert.Equal("miuser", result.Users[0].Value);
        Assert.Equal("2026-10-22 08:25:49", result.Users[0].Stamp); Assert.NotEmpty(result.Users[0].EditVersion);
        Assert.Contains("GET /api/v1/lockouts/ip", native.Calls); Assert.Contains("GET /api/v1/lockouts/user", native.Calls);
        Assert.True(native.Calls.Count == 2); Assert.True(native.Writes == 0 && native.LockoutDeletes == 0);
    }

    [Theory]
    [InlineData("ip")]
    [InlineData("user")]
    public async Task SingleAndMultipleUnlocksSendBareArraysAndReadBackWithoutCheckout(string kind)
    {
        using var native = new SystemSettingsTests.NativeHandler(); var field = kind == "ip" ? "IP" : "User";
        var extra = kind == "ip" ? "192.168.198.2" : @"DOMAIN\User Two";
        native.Lockouts[kind]["items"]!.AsArray().Add(new JsonObject { [field] = extra, ["Stamp"] = "2026-10-22 09:01:02" });
        var service = SystemSettingsTests.Service(native); var opened = await service.ReadActiveLockoutsAsync(default);
        var selected = (kind == "ip" ? opened.Addresses : opened.Users).Select(item => new SystemLockoutSelection(item.Value, item.EditVersion)).ToArray();
        native.Calls.Clear();
        var result = await service.UnlockAsync(kind, selected, default);
        Assert.Equal(2, result.Unlocked); Assert.Equal(0, result.AlreadyUnlocked); Assert.Empty(result.Items);
        Assert.True(native.LockoutDeletes == 1 && native.LastUnlockBody!.Count == 2);
        Assert.Equal(extra, native.LastUnlockBody![1]!.GetValue<string>());
        Assert.Equal(new[] { "GET /api/v1/lockouts/" + kind, "DELETE /api/v1/lockouts/" + kind, "GET /api/v1/lockouts/" + kind }, native.Calls);
        Assert.False(native.CheckedOut);
    }

    [Fact]
    public async Task EmptyListsAreNotInterpretedAsMissingData()
    {
        using var native = new SystemSettingsTests.NativeHandler();
        foreach (var list in native.Lockouts.Values) list["items"]!.AsArray().Clear();
        var result = await SystemSettingsTests.Service(native).ReadActiveLockoutsAsync(default);
        Assert.Empty(result.Users); Assert.Empty(result.Addresses); Assert.True(native.LockoutDeletes == 0);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"items\":null}")]
    [InlineData("{\"items\":[{}]}")]
    [InlineData("{\"items\":[{\"IP\":\"192.168.198.1\"}]}")]
    [InlineData("{\"items\":[{\"IP\":\"192.168.198.1\",\"Stamp\":3}]}")]
    [InlineData("{\"items\":[{\"IP\":\"192.168.198.1\",\"Stamp\":\"date\"},{\"IP\":\"192.168.198.1\",\"Stamp\":\"date\"}]}")]
    public async Task MissingMalformedOrDuplicateRowsAreRejectedWithoutMutation(string json)
    {
        using var native = new SystemSettingsTests.NativeHandler(); native.Lockouts["ip"] = JsonNode.Parse(json)!.AsObject();
        await Assert.ThrowsAsync<InvalidOperationException>(() => SystemSettingsTests.Service(native).ReadActiveLockoutsAsync(default));
        Assert.True(native.LockoutDeletes == 0 && native.Writes == 0);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("../../globals")]
    [InlineData("IP")]
    public async Task KindIsAllowlistedBeforeNativeCalls(string kind)
    {
        using var native = new SystemSettingsTests.NativeHandler();
        await Assert.ThrowsAsync<ArgumentException>(() => SystemSettingsTests.Service(native).UnlockAsync(kind, [new("value", "version")], default));
        Assert.Empty(native.Calls);
    }

    [Fact]
    public async Task MissingAndDuplicateSelectionsDoNotCallNativeEndpoints()
    {
        using var native = new SystemSettingsTests.NativeHandler(); var service = SystemSettingsTests.Service(native);
        foreach (var values in new IReadOnlyList<SystemLockoutSelection>[] { [], [new("", "version")], [new("user", "")], [new("user", "version"), new("user", "version")] })
            await Assert.ThrowsAsync<ArgumentException>(() => service.UnlockAsync("user", values, default));
        Assert.Empty(native.Calls);
    }

    [Fact]
    public async Task NewLockoutForSameUserRequiresReviewBeforeUnlocking()
    {
        using var native = new SystemSettingsTests.NativeHandler(); var service = SystemSettingsTests.Service(native);
        var opened = await service.ReadActiveLockoutsAsync(default); var user = opened.Users[0];
        native.Lockouts["user"]["items"]![0]!["Stamp"] = "2026-10-22 10:00:00";
        await Assert.ThrowsAsync<SystemActiveLockoutConflictException>(() => service.UnlockAsync("user", [new(user.Value, user.EditVersion)], default));
        Assert.True(native.LockoutDeletes == 0); Assert.False(native.CheckedOut);
    }

    [Fact]
    public async Task AlreadyExpiredSelectionReturnsSuccessWithoutDeletingAnything()
    {
        using var native = new SystemSettingsTests.NativeHandler(); var service = SystemSettingsTests.Service(native);
        var opened = await service.ReadActiveLockoutsAsync(default); var user = opened.Users[0]; native.Lockouts["user"]["items"]!.AsArray().Clear();
        var result = await service.UnlockAsync("user", [new(user.Value, user.EditVersion)], default);
        Assert.Equal(0, result.Unlocked); Assert.Equal(1, result.AlreadyUnlocked); Assert.True(native.LockoutDeletes == 0);
    }

    [Fact]
    public async Task UnlockLeavesUnselectedRowsAndOtherKindUnchanged()
    {
        using var native = new SystemSettingsTests.NativeHandler(); var service = SystemSettingsTests.Service(native);
        native.Lockouts["user"]["items"]!.AsArray().Add(new JsonObject { ["User"] = "other", ["Stamp"] = "2026-10-22 09:00:00" });
        var opened = await service.ReadActiveLockoutsAsync(default); var user = opened.Users[0];
        var result = await service.UnlockAsync("user", [new(user.Value, user.EditVersion)], default);
        Assert.Equal("other", result.Items[0].Value); Assert.True(result.Items.Count == 1);
        Assert.True(native.Lockouts["ip"]["items"]!.AsArray().Count == 1);
    }

    [Theory]
    [InlineData("network")]
    [InlineData("cancel")]
    [InlineData("unchanged")]
    [InlineData("readback-network")]
    [InlineData("malformed-readback")]
    public async Task AmbiguousDeleteOrReadbackIsNotReplayed(string fault)
    {
        using var native = new SystemSettingsTests.NativeHandler(); var service = SystemSettingsTests.Service(native);
        var user = (await service.ReadActiveLockoutsAsync(default)).Users[0]; native.UnlockFault = fault;
        await Assert.ThrowsAsync<SystemUnlockUncertainException>(() => service.UnlockAsync("user", [new(user.Value, user.EditVersion)], default));
        Assert.True(native.LockoutDeletes == 1); Assert.DoesNotContain(native.Calls, call => call.Contains("checkout") || call.Contains("globals"));
    }

    [Fact]
    public async Task DefinitiveNativeRejectionIsNotReplayed()
    {
        using var native = new SystemSettingsTests.NativeHandler(); var service = SystemSettingsTests.Service(native);
        var user = (await service.ReadActiveLockoutsAsync(default)).Users[0]; native.UnlockFault = "validation";
        var error = await Assert.ThrowsAsync<MoveItApiException>(() => service.UnlockAsync("user", [new(user.Value, user.EditVersion)], default));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, error.StatusCode); Assert.True(native.LockoutDeletes == 1);
        Assert.True(native.Lockouts["user"]["items"]!.AsArray().Count == 1);
    }

    [Fact]
    public async Task NativeAdministratorIsRequiredForReadAndUnlock()
    {
        using var native = new SystemSettingsTests.NativeHandler(); var service = SystemSettingsTests.Service(native, false);
        await Assert.ThrowsAsync<MoveItApiException>(() => service.ReadActiveLockoutsAsync(default));
        await Assert.ThrowsAsync<MoveItApiException>(() => service.UnlockAsync("user", [new("miuser", "version")], default));
        Assert.Empty(native.Calls);
    }
}
