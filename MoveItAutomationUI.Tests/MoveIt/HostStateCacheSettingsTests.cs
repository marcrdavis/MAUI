using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class HostStateCacheSettingsTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void NonTimedModesRemoveUnitAndPreserveOtherState(int time)
    {
        var host = new JsonObject { ["UseDefStateCaching"] = 0, ["StateCacheTime"] = 10, ["StateCacheUnit"] = "Minutes", ["NativeOnly"] = "keep" };
        HostStateCacheSettings.Apply(host, new JsonObject { ["StateCacheTime"] = time });
        Assert.Equal(time, host["StateCacheTime"]!.GetValue<int>());
        Assert.Null(host["StateCacheUnit"]);
        Assert.Equal("keep", host["NativeOnly"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("Minutes")]
    [InlineData("Hours")]
    public void TimedModesUseNativeDurationAndUnit(string unit)
    {
        var host = new JsonObject { ["UseDefStateCaching"] = 1 };
        HostStateCacheSettings.Apply(host, new JsonObject { ["StateCacheUnit"] = unit, ["StateCacheTime"] = 1, ["UseDefStateCaching"] = false });
        Assert.Equal(0, host["UseDefStateCaching"]!.GetValue<int>());
        Assert.Equal(1, host["StateCacheTime"]!.GetValue<int>());
        Assert.Equal(unit, host["StateCacheUnit"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("-2", "Minutes")]
    [InlineData("1.5", "Minutes")]
    [InlineData("2147483648", "Minutes")]
    [InlineData("", "Minutes")]
    [InlineData("1", "")]
    [InlineData("1", "Days")]
    public void InvalidCacheSettingsAreRejectedBeforeMutation(string time, string unit)
    {
        var host = new JsonObject { ["UseDefStateCaching"] = 1, ["NativeOnly"] = "keep" };
        var before = host.ToJsonString();
        Assert.Throws<InvalidOperationException>(() => HostStateCacheSettings.Apply(host,
            new JsonObject { ["UseDefStateCaching"] = false, ["StateCacheTime"] = time, ["StateCacheUnit"] = unit }));
        Assert.Equal(before, host.ToJsonString());
    }

    [Fact]
    public void InheritanceRemovesExplicitChildrenAndKeepsNativeCasing()
    {
        var host = new JsonObject { ["usedefstatecaching"] = 0, ["statecachetime"] = 5, ["STATECACHEUNIT"] = "Hours", ["Opaque"] = "keep" };
        HostStateCacheSettings.Apply(host, new JsonObject { ["UseDefStateCaching"] = true });
        Assert.Equal(1, host["usedefstatecaching"]!.GetValue<int>());
        Assert.False(host.ContainsKey("statecachetime"));
        Assert.False(host.ContainsKey("STATECACHEUNIT"));
        Assert.False(host.ContainsKey("UseDefStateCaching"));
        Assert.Equal("keep", host["Opaque"]!.GetValue<string>());
    }

    [Fact]
    public void UnsubmittedStateCacheIsPreservedExactly()
    {
        var host = new JsonObject { ["UseDefStateCaching"] = 0, ["StateCacheTime"] = 7, ["StateCacheUnit"] = "NativeFutureUnit" };
        var before = host.ToJsonString();
        HostStateCacheSettings.Apply(host, new JsonObject { ["DelOldStateDays"] = 30 });
        Assert.Equal(before, host.ToJsonString());
    }

    [Fact]
    public void ReadbackVerifiesDerivedUnitWhenOnlyDurationWasSubmitted()
    {
        var expected = new JsonObject { ["UseDefStateCaching"] = 0, ["StateCacheTime"] = 5, ["StateCacheUnit"] = "Minutes" };
        var actual = (JsonObject)expected.DeepClone(); actual["StateCacheUnit"] = "Hours";
        Assert.Throws<InvalidOperationException>(() => HostStateCacheSettings.Verify(expected, actual,
            new JsonObject { ["StateCacheTime"] = 5 }));
    }
}
