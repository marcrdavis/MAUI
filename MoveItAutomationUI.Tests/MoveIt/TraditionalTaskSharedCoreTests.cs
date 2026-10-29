using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;
using Xunit;

namespace MoveItAutomationUI.Tests.MoveIt;

/// <summary>
/// Phase 1 parity tests: the common composer must produce exactly the JSON that
/// RC1 previously produced by applying Host defaults and locations in each feature.
/// These are contract tests, not a substitute for native MOVEit readback testing.
/// </summary>
public sealed class TraditionalTaskSharedCoreTests
{
    private readonly TraditionalTaskHostProfileCatalog _profiles = new();

    private TraditionalTaskDefinitionComposer Composer => new(_profiles);

    [Theory]
    [InlineData(true, false)] [InlineData(false, false)]
    [InlineData(true, true)] [InlineData(false, true)]
    public void Pop3NewOverrideOrModeRequiresSecretAndSameModePreservesIt(bool oauth, bool inherited)
    {
        var original = new JsonObject { ["ID"] = "11", ["HostID"] = "mail", ["Type"] = "POP3",
            ["UseDefUser"] = inherited ? 1 : 0, ["UseOAuth"] = oauth ? 0 : 1, ["FileMask"] = "*.*" };
        var task = new JsonObject { ["steps"] = new JsonArray(new JsonObject { ["Source"] = original }) };
        var patch = new JsonObject { ["ID"] = "11", ["HostID"] = "mail", ["Type"] = "POP3", ["UseDefUser"] = 0,
            ["UseOAuth"] = oauth ? 1 : 0, ["Username"] = "mail-user", ["OAuthClientID"] = "client",
            ["OAuthScope"] = "custom-scope", ["OAuthTokenURL"] = "https://identity.example.test/token" };
        void Apply() => Composer.ApplyEditorSteps(task, new JsonArray(new JsonObject { ["kind"] = "Source", ["id"] = "11", ["patch"] = patch.DeepClone() }));
        Assert.Throws<InvalidOperationException>(Apply);
        var field = oauth ? "OAuthClientSecret" : "Password";
        patch[field] = "  literal-secret  "; Apply();
        task["steps"]![0]!["Source"]![field] = "SERVER_CIPHER";
        patch.Remove(field); patch["FileMask"] = "*.csv"; Apply();
        Assert.Equal("SERVER_CIPHER", task["steps"]![0]!["Source"]![field]!.GetValue<string>());
        Assert.Equal("*.csv", task["steps"]![0]!["Source"]!["FileMask"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("Source", 1)]
    [InlineData("Destination", 1)]
    [InlineData("Source", 2)]
    [InlineData("Destination", 2)]
    public void SharePointOverrides_AcceptAppAndCertificateAccess(string kind, int mode)
    {
        var task = new JsonObject { ["steps"] = new JsonArray() };
        var patch = SharePointAuthenticationPatch(mode);
        Composer.ApplyEditorSteps(task, new JsonArray(new JsonObject { ["kind"] = kind, ["patch"] = patch }));
        var saved = task["steps"]![0]![kind]!;
        Assert.Equal(mode, saved["UsingAppAuthentication"]!.GetValue<int>());
        Assert.Equal("app", saved["ClientID"]!.GetValue<string>());
        Assert.Null(saved["Username"]);
        Assert.Null(saved["Password"]);
    }

    [Theory]
    [InlineData("Source", "Username")]
    [InlineData("Source", "Password")]
    [InlineData("Destination", "Username")]
    [InlineData("Destination", "Password")]
    public void SharePointOverrides_RejectUsernameAndPasswordPatches(string kind, string field)
    {
        var task = new JsonObject { ["steps"] = new JsonArray() };
        var patch = SharePointAuthenticationPatch(1);
        patch[field] = "unsupported";
        Assert.Throws<InvalidOperationException>(() => Composer.ApplyEditorSteps(task,
            new JsonArray(new JsonObject { ["kind"] = kind, ["patch"] = patch })));
        Assert.Empty(task["steps"]!.AsArray());
    }

    [Theory]
    [InlineData("Source")]
    [InlineData("Destination")]
    public void SharePointOverrides_RejectUnsupportedAuthenticationMode(string kind)
    {
        var task = new JsonObject { ["steps"] = new JsonArray() };
        var error = Assert.Throws<InvalidOperationException>(() => Composer.ApplyEditorSteps(task,
            new JsonArray(new JsonObject { ["kind"] = kind, ["patch"] = SharePointAuthenticationPatch(0) })));
        Assert.Contains("authentication mode", error.Message);
        Assert.Empty(task["steps"]!.AsArray());
    }

    [Fact]
    public void SharePointEdit_PreservesUnsubmittedNativeProperties()
    {
        var task = JsonNode.Parse("""{"steps":[{"Source":{"ID":"7","HostID":"42","Type":"SharePoint","Username":"legacy","Password":"opaque","UsingAppAuthentication":0}}]}""")!.AsObject();
        Composer.ApplyEditorSteps(task, new JsonArray(new JsonObject
        {
            ["kind"] = "Source", ["id"] = "7", ["index"] = 0,
            ["patch"] = new JsonObject { ["HostID"] = "42", ["Type"] = "SharePoint", ["Path"] = "/updated", ["UseDefUser"] = 1 }
        }));
        var saved = task["steps"]![0]!["Source"]!;
        Assert.Equal("legacy", saved["Username"]!.GetValue<string>());
        Assert.Equal("opaque", saved["Password"]!.GetValue<string>());
        Assert.Equal("/updated", saved["Path"]!.GetValue<string>());
    }

    private static JsonObject SharePointAuthenticationPatch(int mode) => new()
    {
        ["Type"] = "SharePoint", ["HostID"] = "42", ["Path"] = "/folder",
        ["UseDefUser"] = 0, ["UsingAppAuthentication"] = mode, ["ClientID"] = "app",
        ["RealmID"] = "realm", ["ClientSecret"] = "secret", ["TenantID"] = "tenant",
        ["ClientCertSHA1"] = "sha1", ["ClientCertStore"] = "My", ["ClientCertSubject"] = "CN=Test"
    };

    [Fact]
    public void EditorStepComposer_CreatesVerifiedNewSourceAndDestinationSteps()
    {
        var task = new JsonObject { ["steps"] = new JsonArray() };
        var patch = JsonNode.Parse("""
            [
              {"kind":"Source","patch":{"Type":"Share","HostID":"17","Path":"Inbound","FileMask":"*.csv"}},
              {"kind":"Destination","patch":{"Type":"Share","HostID":"18","Path":"Outbound"}}
            ]
            """)!.AsArray();

        Composer.ApplyEditorSteps(task, patch);
        var steps = task["steps"]!.AsArray();
        Assert.Collection(steps,
            first => Assert.NotNull(first?["Source"]),
            second => Assert.NotNull(second?["Destination"]));
        Assert.Equal("Inbound", steps[0]!["Source"]!["Path"]!.ToString());
        Assert.Equal("17", steps[0]!["Source"]!["HostID"]!.ToString());
        Assert.Equal("Outbound", steps[1]!["Destination"]!["Path"]!.ToString());
        Assert.Equal("18", steps[1]!["Destination"]!["HostID"]!.ToString());
        // A new Step is assembled without mutating the editor's requested patch.
        Assert.Null(patch[0]!["patch"]!["UseDefUser"]);
    }

    [Fact]
    public void EditorStepComposer_PreservesOpaqueStepsAndUnknownProperties()
    {
        var task = JsonNode.Parse("""
            {"steps":[{"Source":{"ID":"123","Type":"AS2","HostID":"44","UnknownServerValue":"unchanged"}}],"VendorField":"retain"}
            """)!.AsObject();
        var requested = JsonNode.Parse("""
            [{"kind":"Source","id":"123","index":0,"patch":null}]
            """)!.AsArray();
        Composer.ApplyEditorSteps(task, requested);
        Assert.Equal("unchanged", task["steps"]![0]!["Source"]!["UnknownServerValue"]!.ToString());
        Assert.Equal("retain", task["VendorField"]!.ToString());
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" \t ", false)]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData(" \t ", true)]
    public void ExplicitBlankFilespecRejectsNewAndExistingSourceEdits(string? mask, bool existing)
    {
        var task = JsonNode.Parse("""{"steps":[{"Source":{"ID":"11","Type":"Share","HostID":"17","Path":"Inbound","FileMask":"*.csv"}}]}""")!.AsObject();
        if (!existing) task["steps"] = new JsonArray();
        var original = task.DeepClone();
        var request = new JsonArray(new JsonObject
        {
            ["kind"] = "Source", ["id"] = existing ? "11" : "",
            ["patch"] = new JsonObject { ["Type"] = "Share", ["HostID"] = "17", ["Path"] = "Inbound", ["FileMask"] = mask }
        });
        var error = Assert.Throws<InvalidOperationException>(() => Composer.ApplyEditorSteps(task, request));
        Assert.Contains("Source File(s) is required", error.Message);
        Assert.True(JsonNode.DeepEquals(original, task));
    }

    [Fact]
    public void OmittedFilespecPreservesTheAuthoritativeSourceValue()
    {
        var task = JsonNode.Parse("""{"steps":[{"Source":{"ID":"11","Type":"Share","HostID":"17","Path":"Inbound","FileMask":"[Parm:InputMask]"}}]}""")!.AsObject();
        Composer.ApplyEditorSteps(task, new JsonArray(new JsonObject
        {
            ["kind"] = "Source", ["id"] = "11",
            ["patch"] = new JsonObject { ["Type"] = "Share", ["HostID"] = "17", ["Path"] = "Changed" }
        }));
        Assert.Equal("[Parm:InputMask]", task["steps"]![0]!["Source"]!["FileMask"]!.ToString());
    }

    [Fact]
    public void EditorStepComposer_RejectsNewOpaqueStepsAndUnverifiedHostProfiles()
    {
        var task = new JsonObject { ["steps"] = new JsonArray() };
        var opaque = JsonNode.Parse("""[{"kind":"Source","patch":null}]""")!.AsArray();
        Assert.Throws<InvalidOperationException>(() => Composer.ApplyEditorSteps(task, opaque));
        var unsupported = JsonNode.Parse("""[{"kind":"Source","patch":{"Type":"AS2","HostID":"3"}}]""")!.AsArray();
        Assert.Throws<InvalidOperationException>(() => Composer.ApplyEditorSteps(task, unsupported));
        Assert.Empty(task["steps"]!.AsArray());
    }

    [Fact]
    public void DisabledSkeletonComposer_ProducesDetachedNativeDefinition()
    {
        var definition = Composer.BuildDisabledTaskDefinition("Demo", "Operations");
        Assert.Equal("Demo", definition["Name"]!.ToString());
        Assert.Equal(0, definition["Active"]!.GetValue<int>());
        Assert.Equal("Operations", definition["Group"]![0]!.ToString());
        Assert.Empty(definition["steps"]!.AsArray());
        Assert.Empty(definition["Schedules"]!["Schedule"]!.AsArray());
        Assert.Throws<ArgumentException>(() => Composer.BuildDisabledTaskDefinition("  "));
    }

    [Theory]
    [InlineData("Share")]
    [InlineData("siLock")]
    [InlineData("FTP")]
    [InlineData("SSHFTP")]
    [InlineData("S3")]
    [InlineData("AzureBlob")]
    [InlineData("SharePoint")]
    [InlineData("GoogleStorage")]
    public void ComposedSource_EqualsOriginalRc1DefaultsAndLocation(string type)
    {
        var profile = _profiles.Require(type, "Source");
        var fields = new JsonObject
        {
            ["HostID"] = "42", ["Type"] = type, ["FileMask"] = "*.csv",
            ["NewFilesOnly"] = 0, ["DeleteOrig"] = 0
        };
        var expected = (JsonObject)fields.DeepClone();
        _profiles.ApplySourceDefaults(expected, profile);
        _profiles.ApplyLocation(expected, profile, "/source");

        var actual = Composer.BuildStepBody(new TraditionalTaskStepDefinition(
            TraditionalTaskStepRole.Source, profile, fields, "/source"));

        Assert.Equal(expected.ToJsonString(), actual.ToJsonString());
        Assert.Null(fields["Path"]); // callers remain untouched
    }

    [Theory]
    [InlineData("Share")]
    [InlineData("siLock")]
    [InlineData("FTP")]
    [InlineData("SSHFTP")]
    [InlineData("S3")]
    [InlineData("AzureBlob")]
    [InlineData("SharePoint")]
    [InlineData("GoogleStorage")]
    public void ComposedDestination_EqualsOriginalRc1DefaultsAndLocation(string type)
    {
        var profile = _profiles.Require(type, "Destination");
        var fields = new JsonObject
        {
            ["HostID"] = 42L, ["Type"] = type, ["FileName"] = "[OrigName]",
            ["UseOrigName"] = 0, ["OverwriteOrig"] = 1
        };
        if (profile.DestinationSupportsForceDir) fields["ForceDir"] = 1;
        var expected = (JsonObject)fields.DeepClone();
        _profiles.ApplyDestinationDefaults(expected, profile);
        _profiles.ApplyLocation(expected, profile, "/dest");

        var actual = Composer.BuildStepBody(new TraditionalTaskStepDefinition(
            TraditionalTaskStepRole.Destination, profile, fields, "/dest"));

        Assert.Equal(expected.ToJsonString(), actual.ToJsonString());
        Assert.Equal(42L, actual["HostID"]!.GetValue<long>());
        Assert.Equal(0, actual["UseOrigName"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("S3", "Bucket", "UseDefBucket")]
    [InlineData("AzureBlob", "Container", "UseDefContainer")]
    [InlineData("GoogleStorage", "Bucket", "UseDefBucket")]
    public void ExplicitRoot_UsesHostRootMapping(string hostType, string rootField, string useDefaultField)
    {
        var profile = _profiles.Require(hostType, "Source");
        var actual = Composer.BuildStepBody(new TraditionalTaskStepDefinition(
            TraditionalTaskStepRole.Source, profile,
            new JsonObject { ["Type"] = hostType }, "/folder", "my-root"));
        Assert.Equal("my-root", actual[rootField]!.GetValue<string>());
        Assert.Equal(0, actual[useDefaultField]!.GetValue<int>());
    }

    [Fact]
    public void SharePointExplicitLibrary_RequiresGuid()
    {
        var profile = _profiles.Require("SharePoint", "Source");
        Assert.Throws<InvalidOperationException>(() => Composer.BuildStepBody(
            new TraditionalTaskStepDefinition(TraditionalTaskStepRole.Source, profile,
                new JsonObject { ["Type"] = "SharePoint" }, "/folder", "library")));
    }

    [Fact]
    public void SourceRejectsMismatchedType()
    {
        var profile = _profiles.Require("Share", "Source");
        Assert.Throws<InvalidOperationException>(() => Composer.BuildStepBody(
            new TraditionalTaskStepDefinition(TraditionalTaskStepRole.Source, profile,
                new JsonObject { ["Type"] = "FTP" }, "/folder")));
    }

    [Fact]
    public void QuickTransferProcess_UsesStringScriptIdAndNativeParameterWrapper()
    {
        var parameters = new JsonArray(new JsonObject
        {
            ["Name"] = "PGPRecipientKey", ["value"] = "0xABCD", ["Secure"] = 0
        });
        var actual = Composer.BuildWrappedProcess(new TraditionalTaskProcessDefinition(
            JsonValue.Create("123")!, parameters));
        var body = actual["Process"]!.AsObject();
        var prePhaseOneNativeShape = new JsonObject
        {
            ["Process"] = new JsonObject
            {
                ["Run"] = "PerFile", ["ScriptID"] = "123",
                ["Parameters"] = new JsonObject { ["Parameter"] = new JsonArray(new JsonObject
                {
                    ["Name"] = "PGPRecipientKey", ["value"] = "0xABCD", ["Secure"] = 0
                }) }
            }
        };
        Assert.Equal(prePhaseOneNativeShape.ToJsonString(), actual.ToJsonString());
        Assert.Equal("123", body["ScriptID"]!.GetValue<string>());
        Assert.Equal("PerFile", body["Run"]!.GetValue<string>());
        var parameter = body["Parameters"]!["Parameter"]!.AsArray()[0]!.AsObject();
        Assert.Equal("0xABCD", parameter["value"]!.GetValue<string>());
        Assert.Equal(0, parameter["Secure"]!.GetValue<int>());
    }

    [Fact]
    public void SpreadsheetDecryptProcess_PreservesEmptyParametersAndNumericScriptId()
    {
        var body = Composer.BuildProcessBody(new TraditionalTaskProcessDefinition(
            JsonValue.Create(123L)!, new JsonArray(), IncludeParametersWhenEmpty: true));
        var prePhaseOneNativeShape = new JsonObject
        {
            ["Run"] = "PerFile", ["ScriptID"] = 123L,
            ["Parameters"] = new JsonObject { ["Parameter"] = new JsonArray() }
        };
        Assert.Equal(prePhaseOneNativeShape.ToJsonString(), body.ToJsonString());
        Assert.Equal(123L, body["ScriptID"]!.GetValue<long>());
        Assert.Empty(body["Parameters"]!["Parameter"]!.AsArray());

        var quickDecrypt = Composer.BuildProcessBody(new TraditionalTaskProcessDefinition(JsonValue.Create("123")!));
        Assert.Null(quickDecrypt["Parameters"]);
    }

    [Fact]
    public void StepList_ClonesInputsAndRejectsInvalidWrappers()
    {
        var source = new JsonObject { ["Source"] = new JsonObject { ["HostID"] = 1 } };
        var steps = Composer.BuildSteps(new[] { source });
        source["Source"]!["HostID"] = 2;
        Assert.Equal(1, steps[0]!["Source"]!["HostID"]!.GetValue<int>());
        Assert.Throws<InvalidOperationException>(() => Composer.BuildSteps(new[] { new JsonObject { ["Invalid"] = 1 } }));
    }

    [Fact]
    public void SharedHostCapabilities_KeepEditorDirectionRulesWithoutEnablingRc1HostWrites()
    {
        _profiles.ValidateCapabilities();
        Assert.True(_profiles.CanEditorWrite("Source", "POP3"));
        Assert.False(_profiles.CanEditorWrite("Destination", "POP3"));
        Assert.True(_profiles.CanEditorWrite("Destination", "SMTP"));
        Assert.False(_profiles.CanEditorWrite("Source", "SMTP"));
        Assert.Null(_profiles.Find("POP3"));
        Assert.Null(_profiles.Find("SMTP"));
        Assert.False(_profiles.CanEditorWrite("Source", "AS2"));
        Assert.True(_profiles.IsEditorPatchFieldAllowed("Source", "SSHFTP", "ClientKeyID"));
        Assert.False(_profiles.IsEditorPatchFieldAllowed("Destination", "S3", "ForceDir"));
    }

    [Fact]
    public void LocalFileSystemUsesOneWritableBrowsableProfileAcrossTaskCallers()
    {
        Assert.True(_profiles.Require("FileSystem", "Source").SourceWriteSupported);
        Assert.True(_profiles.CanEditorWrite("Source", "FileSystem"));
        Assert.True(_profiles.CanEditorWrite("Destination", "FileSystem"));
        Assert.True(_profiles.CanEditorBrowse("Source", "FileSystem"));
        Assert.True(_profiles.CanEditorBrowse("Destination", "FileSystem"));
        Assert.False(_profiles.IsEditorPatchFieldAllowed("Source", "FileSystem", "UseDefUser"));
        Assert.False(_profiles.IsEditorPatchFieldAllowed("Destination", "FileSystem", "UseDefWinCopyFileAPI"));
    }

    [Fact]
    public void SharedRootComposer_PreservesQuickTransferAndSpreadsheetNativeShapes()
    {
        var steps = new JsonArray(new JsonObject { ["Source"] = new JsonObject { ["HostID"] = 2 } });
        var schedules = new JsonObject { ["Schedule"] = new JsonArray() };
        var quick = Composer.BuildRootDefinition(new TraditionalTaskRootDefinition(
            "Adhoc", false, "Temporary Task created by MAUI Quick Transfer.",
            "Created by MAUI Quick Transfer.", schedules, steps, NextActions: new JsonArray()));
        Assert.Null(quick["Group"]);
        Assert.Empty(quick["NextActions"]!["NextAction"]!.AsArray());
        Assert.Equal("Created by MAUI Quick Transfer.", quick["Info"]!["Notes"]!.GetValue<string>());

        var spreadsheet = Composer.BuildRootDefinition(new TraditionalTaskRootDefinition(
            "Imported", false, "Description", null, schedules, steps,
            ResourceGroups: new JsonArray("Operations")));
        Assert.Equal("Operations", spreadsheet["Group"]![0]!.GetValue<string>());
        Assert.Null(spreadsheet["NextActions"]);
        Assert.Null(spreadsheet["Info"]!["Notes"]);
        Assert.Equal(0, spreadsheet["Active"]!.GetValue<int>());
        // The returned native nodes are detached from caller-owned inputs.
        steps.Clear();
        Assert.Single(quick["steps"]!.AsArray());
        Assert.Single(spreadsheet["steps"]!.AsArray());
    }

    [Fact]
    public void SharedRootComposer_PreservesUnownedFieldsDuringEdits()
    {
        var existing = JsonNode.Parse("""
            {"ID":"40","CustomMOVEitField":{"keep":true},"Group":["Original"]}
            """)!.AsObject();
        Composer.ApplyRootDefinition(existing, new TraditionalTaskRootDefinition(
            "Updated", true, "New Description", null,
            new JsonObject { ["Schedule"] = new JsonArray() }, new JsonArray()));
        Assert.True(existing["CustomMOVEitField"]!["keep"]!.GetValue<bool>());
        Assert.Equal("Original", existing["Group"]![0]!.GetValue<string>());
        Assert.Equal("40", existing["ID"]!.GetValue<string>());
        Assert.Null(existing["NextActions"]);
    }
}

