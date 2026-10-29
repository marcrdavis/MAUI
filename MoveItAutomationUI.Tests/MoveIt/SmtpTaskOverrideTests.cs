using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Tasks.AdvancedTasks;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;

namespace MoveItAutomationUI.Tests.MoveIt;

/// <summary>Captured native SMTP Destination overrides, shared by Traditional and Advanced Tasks.</summary>
public sealed class SmtpTaskOverrideTests
{
    private static TraditionalTaskDefinitionComposer Composer => new(new TraditionalTaskHostProfileCatalog());

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void CapturedOAuthOverridesComposeAndVerifyForBothTaskTypes(bool advanced, bool microsoft365)
    {
        var task = TaskDefinition();
        var patch = Patch(microsoft365);
        if (advanced)
        {
            task["TT"] = "COS";
            AdvancedTaskDefinition.Apply(task,
                JsonNode.Parse("""[{"key":"loop","kind":"For","steps":[{"key":"smtp","kind":"Destination"}]}]""")!.AsArray(),
                new JsonArray(new JsonObject { ["clientKey"] = "smtp", ["kind"] = "Destination", ["patch"] = patch }), null, Composer);
        }
        else Apply(task, patch);
        var actual = advanced ? task["steps"]![0]!["For"]!["steps"]![0]!["Destination"]!.AsObject() : Destination(task);
        Assert.Equal(0, actual["UseDefUser"]!.GetValue<int>());
        Assert.Equal(1, actual["UseOAuth"]!.GetValue<int>());
        Assert.Equal("oauth-user", actual["Username"]!.GetValue<string>());
        Assert.Equal("clientid", actual["OAuthClientID"]!.GetValue<string>());
        Assert.Equal(patch["OAuthScope"]!.GetValue<string>(), actual["OAuthScope"]!.GetValue<string>());
        Assert.Equal(patch["OAuthTokenURL"]!.GetValue<string>(), actual["OAuthTokenURL"]!.GetValue<string>());
        Assert.Equal("  literal\\secret  ", actual["OAuthClientSecret"]!.GetValue<string>());
        Assert.False(actual.ContainsKey("Password"));
        Assert.Equal("keep", task["NativeTaskField"]!.GetValue<string>());
        // The readback may suppress/encrypt the secret. Non-secret fields still must match.
        patch["ID"] = actual["ID"]!.DeepClone();
        var readback = actual.DeepClone().AsObject(); readback["OAuthClientSecret"] = null;
        Composer.VerifyEditorSteps(new JsonArray(new JsonObject { ["kind"] = "Destination", ["id"] = actual["ID"]!.GetValue<string>(), ["patch"] = patch.DeepClone() }),
            new JsonArray(new JsonObject { ["Destination"] = readback.DeepClone() }));
        readback["OAuthClientID"] = "unexpected";
        Assert.Throws<InvalidOperationException>(() => Composer.VerifyEditorSteps(
            new JsonArray(new JsonObject { ["kind"] = "Destination", ["id"] = actual["ID"]!.GetValue<string>(), ["patch"] = patch.DeepClone() }),
            new JsonArray(new JsonObject { ["Destination"] = readback.DeepClone() })));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void ExistingSameModeOverridePreservesSuppressedSecretAndUnrelatedFields(bool oauth)
    {
        var patch = oauth ? Patch(true) : Legacy(); var task = TaskDefinition(); Apply(task, patch);
        var saved = Destination(task); saved["NativeStepField"] = "keep";
        saved[oauth ? "OAuthClientSecret" : "Password"] = "SERVER_CIPHER";
        patch.Remove(oauth ? "OAuthClientSecret" : "Password"); patch["Subject"] = "Changed";
        Apply(task, patch);
        Assert.Equal("SERVER_CIPHER", Destination(task)[oauth ? "OAuthClientSecret" : "Password"]!.GetValue<string>());
        Assert.Equal("Changed", Destination(task)["Subject"]!.GetValue<string>());
        Assert.Equal("keep", Destination(task)["NativeStepField"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("new")] [InlineData("host")] [InlineData("inherit")]
    [InlineData("mode")] [InlineData("clone")]
    public void NewCredentialContextRequiresAnExplicitSecret(string context)
    {
        var task = TaskDefinition(); var patch = Patch(true);
        if (context != "new") Apply(task, context == "mode" ? Legacy() : patch);
        patch.Remove("OAuthClientSecret");
        if (context == "host") patch["HostID"] = "another-smtp";
        if (context == "inherit") Destination(task)["UseDefUser"] = 1;
        var item = new JsonObject { ["kind"] = "Destination", ["id"] = "12", ["patch"] = patch, ["clone"] = context == "clone" };
        var error = Assert.Throws<InvalidOperationException>(() => Composer.ApplyEditorSteps(task, new JsonArray(item)));
        Assert.Contains("Client Secret must be set", error.Message);
    }

    [Fact]
    public void OAuthToLegacyRequiresPasswordAndInheritingNeedsNoTaskCredentials()
    {
        var task = TaskDefinition(); Apply(task, Patch(true));
        var legacy = Legacy(); legacy.Remove("Password");
        Assert.Throws<InvalidOperationException>(() => Apply(task, legacy));
        legacy["Password"] = "legacy-replacement"; Apply(task, legacy);
        Assert.Equal(0, Destination(task)["UseOAuth"]!.GetValue<int>());
        Assert.Equal("legacy-replacement", Destination(task)["Password"]!.GetValue<string>());
        Apply(task, new JsonObject { ["ID"] = "12", ["HostID"] = "smtp-host", ["Type"] = "SMTP", ["UseDefUser"] = 1, ["AddressTo"] = "test@example.test" });
        Assert.Equal(1, Destination(task)["UseDefUser"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("Username")] [InlineData("OAuthClientID")] [InlineData("OAuthScope")]
    [InlineData("OAuthTokenURL")] [InlineData("OAuthClientSecret")] [InlineData("AddressTo")]
    public void EmptyRequiredOverrideFieldsAreRejected(string key)
    {
        var patch = Patch(true); patch[key] = "";
        Assert.Throws<InvalidOperationException>(() => Apply(TaskDefinition(), patch));
    }

    private static JsonObject TaskDefinition() => JsonNode.Parse("""{"ID":"100","Name":"SMTP test","TT":"","NextEID":13,"NativeTaskField":"keep","steps":[]}""")!.AsObject();
    private static JsonObject Destination(JsonObject task) => task["steps"]![0]!["Destination"]!.AsObject();
    private static void Apply(JsonObject task, JsonObject patch) => Composer.ApplyEditorSteps(task,
        new JsonArray(new JsonObject { ["kind"] = "Destination", ["id"] = "12", ["patch"] = patch.DeepClone() }));
    private static JsonObject Patch(bool microsoft365) => new()
    {
        ["ID"] = "12", ["HostID"] = "smtp-host", ["Type"] = "SMTP", ["UseDefUser"] = 0,
        ["AddressTo"] = "test@example.test", ["Subject"] = "Subject", ["Message"] = "Message", ["IsHTML"] = 0,
        ["Zip"] = 1, ["UseOrigName"] = 1, ["FileName"] = "[OrigName]", ["UseOAuth"] = 1,
        ["Username"] = "oauth-user", ["OAuthClientID"] = "clientid", ["OAuthClientSecret"] = "  literal\\secret  ",
        ["OAuthScope"] = microsoft365 ? "https://outlook.office365.com/.default" : "https://scope.example.test",
        ["OAuthTokenURL"] = microsoft365 ? "https://login.microsoftonline.com/tenantid/oauth2/v2.0/token" : "https://identity.example.test/token",
        ["UseDefRetryCount"] = 1, ["UseDefRetryTimeoutSecs"] = 1, ["UseDefConnTimeoutSecs"] = 1, ["UseDefDataTimeoutSecs"] = 1
    };
    private static JsonObject Legacy()
    {
        var patch = Patch(true); patch["UseOAuth"] = 0; patch["Username"] = "legacy-user"; patch["Password"] = "legacy-password";
        foreach (var key in new[] { "OAuthClientID", "OAuthClientSecret", "OAuthScope", "OAuthTokenURL" }) patch.Remove(key);
        return patch;
    }
}
