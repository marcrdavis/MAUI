using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class HostFtpDirectoryParsingTests
{
    [Theory]
    [InlineData("automatic", "custom")]
    [InlineData("automatic", "script")]
    [InlineData("custom", "automatic")]
    [InlineData("custom", "script")]
    [InlineData("script", "automatic")]
    [InlineData("script", "custom")]
    public void ModeTransitionsUseOnlyTheCapturedNativeFields(string from, string to)
    {
        var host = new JsonObject { ["NativeOnly"] = "retain", ["DefPassword"] = "secret" };
        var initial = Patch(from);
        HostCapturedProfiles.Apply(host, initial, "FTP");
        var before = host.DeepClone().AsObject();
        var patch = Patch(to);
        HostCapturedProfiles.Apply(host, patch, "FTP");
        Assert.Equal(to, HostFtpDirectoryParsing.Mode(host));
        Assert.True(HostFtpDirectoryParsing.HasChanges(before, host, patch));
        Assert.False(host.ContainsKey(HostFtpDirectoryParsing.ModeKey));
        Assert.Equal("retain", host["NativeOnly"]!.GetValue<string>());
        Assert.Equal("secret", host["DefPassword"]!.GetValue<string>());
        Assert.Equal(to == "script", host.ContainsKey("DirScriptID"));
        Assert.Equal(to == "custom", host.ContainsKey("CustomDir"));
        foreach (var key in HostFtpDirectoryParsing.Columns) Assert.Equal(to == "custom", host.ContainsKey(key));
        HostFtpDirectoryParsing.Verify(host, host.DeepClone().AsObject(), patch);
        var incorrect = new JsonObject { ["DirScriptID"] = "999" };
        Assert.Throws<InvalidOperationException>(() => HostFtpDirectoryParsing.Verify(host, incorrect, patch));
    }

    [Fact]
    public void AutomaticAndCustomZeroDefaultsAllowNativeDefaultOmission()
    {
        var automatic = new JsonObject();
        HostFtpDirectoryParsing.Verify(automatic, new JsonObject { ["CustomDir"] = 0, ["DirScriptID"] = "" }, Patch("automatic"));
        var expected = new JsonObject();
        HostCapturedProfiles.Apply(expected, new JsonObject { [HostFtpDirectoryParsing.ModeKey] = "custom" }, "FTP");
        HostFtpDirectoryParsing.Verify(expected, new JsonObject { ["CustomDir"] = 1 }, Patch("custom"));
        Assert.False(HostFtpDirectoryParsing.HasChanges(expected, expected, Patch("custom")));
    }

    [Fact]
    public void InvalidAndConflictingParsingRequestsAreRejected()
    {
        foreach (var patch in new[] {
            new JsonObject { [HostFtpDirectoryParsing.ModeKey] = "other" },
            new JsonObject { [HostFtpDirectoryParsing.ModeKey] = "script", ["DirScriptID"] = "" },
            new JsonObject { [HostFtpDirectoryParsing.ModeKey] = "script", ["DirScriptID"] = "-1" },
            new JsonObject { [HostFtpDirectoryParsing.ModeKey] = "automatic", ["CustomDirSkipTop"] = 1 },
            new JsonObject { [HostFtpDirectoryParsing.ModeKey] = "custom", ["DirScriptID"] = "100401" },
            new JsonObject { [HostFtpDirectoryParsing.ModeKey] = "custom", ["CustomDirColDate"] = "1.5" }
        }) Assert.Throws<InvalidOperationException>(() => HostCapturedProfiles.Apply(new JsonObject(), patch, "FTP"));
        Assert.Throws<InvalidOperationException>(() => HostCapturedProfiles.Apply(new JsonObject(), Patch("custom"), "SSHFTP"));
    }

    [Theory]
    [InlineData("on-ccc")]
    [InlineData("on")]
    public void CccUsesTheCapturedSecureValue(string secure)
    {
        var host = new JsonObject { ["Secure"] = "on", ["NativeOnly"] = "retain" };
        HostCapturedProfiles.Apply(host, new JsonObject { ["Secure"] = secure }, "FTP");
        Assert.Equal(secure, host["Secure"]!.GetValue<string>());
        Assert.Equal("retain", host["NativeOnly"]!.GetValue<string>());
    }

    private static JsonObject Patch(string mode)
    {
        var patch = new JsonObject { [HostFtpDirectoryParsing.ModeKey] = mode };
        if (mode == "script") patch["DirScriptID"] = "100401";
        if (mode == "custom") foreach (var key in HostFtpDirectoryParsing.Columns) patch[key] = 1;
        return patch;
    }
}
