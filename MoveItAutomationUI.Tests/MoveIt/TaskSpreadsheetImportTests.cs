using System.Text;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;
using Xunit;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class TaskSpreadsheetImportTests
{
    [Theory]
    [InlineData("Client Daily File", "PROD_", "PROD_CLIENT_DAILY_FILE")]
    [InlineData("Client Daily File", "team-", "team-CLIENT_DAILY_FILE")]
    [InlineData("Client Daily File", "PROD", "PRODCLIENT_DAILY_FILE")]
    [InlineData("Client Daily File", "", "CLIENT_DAILY_FILE")]
    [InlineData("Client Daily File", null, "CLIENT_DAILY_FILE")]
    [InlineData("Client Daily File", " None ", "NoneCLIENT_DAILY_FILE")]
    [InlineData("", "PROD_", "")]
    public void ComposeTaskName_PrependsLiteralOptionalPrefix(string name, string? prefix, string expected)
    {
        Assert.Equal(expected, TaskSpreadsheetImportService.ComposeTaskName(name, prefix));
    }

    [Fact]
    public void ComposeTaskName_EnforcesSamePrefixLimitAsHostsImport()
    {
        Assert.Equal(new string('p', 60) + "TASK", TaskSpreadsheetImportService.ComposeTaskName("Task", new string('p', 60)));
        Assert.Throws<ArgumentException>(() => TaskSpreadsheetImportService.ComposeTaskName("Task", new string('p', 61)));
        Assert.Equal(string.Empty, new TaskSpreadsheetAnalyzeForm().TaskNamePrefix);
    }

    [Theory]
    [InlineData("Client Daily File", "CLIENT_DAILY_FILE")]
    [InlineData("  qa-client__file ", "QA_CLIENT_FILE")]
    [InlineData("A/B-C", "A_B_C")]
    public void NormalizeTaskName_UsesCanonicalUnderscoreFormat(string input, string expected)
    {
        Assert.Equal(expected, TaskSpreadsheetImportService.NormalizeTaskName(input));
    }


    [Fact]
    public void ImportRowLimit_DefaultsTo500Rows()
    {
        var ui = new UiOptions();
        Assert.Equal(500, ui.ImportRowLimit.Value);
    }

    [Fact]
    public void AnalyzeForm_DefaultsImportedTasksToDisabled()
    {
        var form = new TaskSpreadsheetAnalyzeForm();

        Assert.True(form.LeaveTasksDisabled);
    }

    [Fact]
    public void XlsxReader_ReportsMalformedWorkbookAsValidationError()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            TaskSpreadsheetImportFileReader.Read(Encoding.UTF8.GetBytes("not an xlsx archive"), "tasks.xlsx"));

        Assert.Contains("valid or readable Excel workbook", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Traditional", true)]
    [InlineData("Advanced", false)]
    [InlineData("Synchronization", false)]
    [InlineData("", false)]
    public void ExistingTaskUpdate_AllowsOnlyTraditionalTasks(string taskType, bool expected)
    {
        var task = new AutomationTask { Type = taskType };

        Assert.Equal(expected, TaskSpreadsheetImportService.IsTraditionalTask(task));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("COS", false)]
    [InlineData("Sy", false)]
    public void ExistingTaskDefinitionUpdate_RejectsKnownNonTraditionalTypes(string tt, bool expected)
    {
        var task = new JsonObject { ["TT"] = tt };

        Assert.Equal(expected, TaskSpreadsheetImportService.IsTraditionalTaskDefinition(task));
    }

    [Fact]
    public void RequiredSpreadsheetHeaders_RequireResourceGroup()
    {
        var headers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TASK_NAME", "SOURCE_FILE", "AUTOMATION_SOURCE_HOST", "SOURCE_PATH",
            "AUTOMATION_DESTINATION_HOST", "DEST_PATH", "DELETE_SOURCE", "USE_DEST_FILENAME",
            "DEST_OVERWRITE", "ARCHIVE", "COMPRESSION", "SCHED_REPEAT",
            "SCHED_ONLY_UNTIL_SUCCESS", "SCHED_FAIL_IF_NO_FILES", "SCHED_POLL_WITH_NOTIF"
        };
        var issues = new List<TaskSpreadsheetImportIssue>();

        TaskSpreadsheetImportService.ValidateRequiredHeaders(headers, issues);

        Assert.Contains(issues, issue => issue.Column == "RESOURCE_GROUP");
        headers.Add("RESOURCE_GROUP");
        issues.Clear();
        TaskSpreadsheetImportService.ValidateRequiredHeaders(headers, issues);
        Assert.Empty(issues);
    }

    [Fact]
    public void ResourceGroupPayload_BlankIsEmptyArray_NotAnEmptyGroupName()
    {
        Assert.Empty(TaskSpreadsheetImportService.BuildResourceGroupPayload(null));
        Assert.Empty(TaskSpreadsheetImportService.BuildResourceGroupPayload("  "));
    }

    [Fact]
    public void ResourceGroupPayload_ExplicitNameIsRetained()
    {
        var payload = TaskSpreadsheetImportService.BuildResourceGroupPayload("  Data Integration  ");
        Assert.Equal("Data Integration", payload[0]!.GetValue<string>());
    }

    [Fact]
    public void PrepareUpdatePayload_PreservesUnknownMoveItPropertiesAndReplacesSpreadsheetOwnedFields()
    {
        var current = JsonNode.Parse("""
            {
              "ID": "123",
              "Name": "OLD",
              "Active": 1,
              "Group": ["Existing Group"],
              "NextEID": 17,
              "VersionSpecificSetting": "keep-me",
              "Info": { "Description": "old" },
              "Schedules": { "Schedule": [] },
              "steps": [{ "Source": { "HostID": 1 } }],
              "NextActions": { "NextAction": [{ "Type": "Email" }] }
            }
            """)!.AsObject();
        var analyzed = JsonNode.Parse("""
            {
              "Name": "NEW",
              "Info": { "Description": "new" },
              "Schedules": { "Schedule": [{ "RunEvenIfNotif": 0 }] },
              "steps": [{ "Source": { "HostID": 2 } }]
            }
            """)!.AsObject();

        var payload = TaskSpreadsheetImportService.PrepareUpdatePayload(current, analyzed, leaveDisabled: true);

        Assert.Equal("123", payload["ID"]!.GetValue<string>());
        Assert.Equal("NEW", payload["Name"]!.GetValue<string>());
        Assert.Equal("keep-me", payload["VersionSpecificSetting"]!.GetValue<string>());
        Assert.Equal(17, payload["NextEID"]!.GetValue<int>());
        Assert.Equal("Existing Group", payload["Group"]!.AsArray()[0]!.GetValue<string>());
        Assert.Equal(0, payload["Active"]!.GetValue<int>());
        Assert.Null(payload["NextActions"]);
        var updatedSource = payload["steps"]!.AsArray()[0]!.AsObject()["Source"]!.AsObject();
        Assert.Equal(2, updatedSource["HostID"]!.GetValue<int>());
    }

    [Fact]
    public void PgpTaskParameter_UsesNativeMoveItKeyId()
    {
        var key = new AutomationKeyCertificate { Id = "0xCA1705D5", Name = "Friendly PGP Name" };

        Assert.Equal("0xCA1705D5", TaskSpreadsheetImportService.GetPgpTaskKeyToken(key));
    }

    [Fact]
    public void CsvReader_AcceptsQuotedNewlinesAndNormalizesHeaders()
    {
        var csv = "task name,description,SCHED_EVERY_MIN\r\nTest Task,\"line 1\r\nline 2\",1:30\r\n";
        var result = TaskSpreadsheetImportFileReader.Read(Encoding.UTF8.GetBytes(csv), "tasks.csv");

        Assert.Collection(result.Headers,
            header => Assert.Equal("TASK_NAME", header),
            header => Assert.Equal("DESCRIPTION", header),
            header => Assert.Equal("SCHED_EVERY_MIN", header));
        Assert.Single(result.Rows);
        Assert.Equal("line 1\r\nline 2", result.Rows[0].Values["DESCRIPTION"]);
        Assert.Equal("90", result.Rows[0].Values["SCHED_EVERY_MIN"]);
    }

    [Fact]
    public void CsvReader_SkipsBlankRows()
    {
        var csv = "TASK_NAME,RESOURCE_GROUP\r\n\r\nTaskA,GroupA\r\n,,\r\n";
        var result = TaskSpreadsheetImportFileReader.Read(Encoding.UTF8.GetBytes(csv), "tasks.csv");

        Assert.Single(result.Rows);
        Assert.True(result.SkippedBlankRows >= 1);
    }

    [Fact]
    public void CsvReader_RejectsDuplicateNormalizedHeaders()
    {
        var csv = "Task Name,TASK-NAME\r\nA,B\r\n";
        var ex = Assert.Throws<InvalidOperationException>(() => TaskSpreadsheetImportFileReader.Read(Encoding.UTF8.GetBytes(csv), "tasks.csv"));
        Assert.Contains("duplicate column", ex.Message, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public void CsvReader_RejectsUnterminatedQuotedField()
    {
        var csv = "TASK_NAME,DESCRIPTION\r\nTaskA,\"unterminated\r\n";
        var ex = Assert.Throws<InvalidOperationException>(() => TaskSpreadsheetImportFileReader.Read(Encoding.UTF8.GetBytes(csv), "tasks.csv"));
        Assert.Contains("unterminated quoted field", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CsvReader_RejectsUnsupportedExtension()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => TaskSpreadsheetImportFileReader.Read(Encoding.UTF8.GetBytes("A\r\nB"), "tasks.xls"));
        Assert.Contains(".csv or .xlsx", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScheduleBuilder_DoesNotConsumeUnsupportedLegacyScheduleWindowsColumn()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SCHED_WINDOWS"] = "08:00-10:00@15"
        };

        var result = TaskSpreadsheetScheduleBuilder.TryBuild(row);

        Assert.True(result.Success, result.Error);
        Assert.Null(result.Schedules);
    }

    [Fact]
    public void ScheduleBuilder_RejectsDowAndDomTogether()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SCHED_DOW"] = "MON",
            ["SCHED_DOM"] = "1",
            ["SCHED_REPEAT"] = "Y"
        };

        var result = TaskSpreadsheetScheduleBuilder.TryBuild(row);
        Assert.False(result.Success);
        Assert.Contains("only one", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScheduleBuilder_NormalizesDayAbbreviations()
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SCHED_DOW"] = "MON,TUE",
            ["SCHED_START"] = "08:00",
            ["SCHED_END"] = "10:00",
            ["SCHED_EVERY_MIN"] = "15",
            ["SCHED_REPEAT"] = "Y"
        };

        var result = TaskSpreadsheetScheduleBuilder.TryBuild(row);
        Assert.True(result.Success, result.Error);
        var json = result.Schedules?.ToJsonString() ?? string.Empty;
        Assert.Contains("Monday", json, StringComparison.Ordinal);
        Assert.Contains("Tuesday", json, StringComparison.Ordinal);
        Assert.Contains("\"EveryMinutes\":15", json, StringComparison.Ordinal);
    }
}

public sealed class TaskSpreadsheetHostLocationTests
{
    [Theory]
    [InlineData("/outbound/client", true, "", "/outbound/client", "/outbound/client")]
    [InlineData("bucket-a/outbound/client", false, "bucket-a", "/outbound/client", "bucket-a/outbound/client")]
    [InlineData("bucket-a", false, "bucket-a", "/", "bucket-a")]
    [InlineData(@"container-a\\folder\\child", false, "container-a", "/folder/child", "container-a/folder/child")]
    public void RootedLocationParser_PreservesDefaultVsExplicitRoot(
        string input,
        bool useDefaultRoot,
        string rootName,
        string taskPath,
        string displayPath)
    {
        var result = TaskSpreadsheetImportService.ParseRootedLocation(input);

        Assert.True(result.Success, result.Error);
        Assert.Equal(useDefaultRoot, result.UseDefaultRoot);
        Assert.Equal(rootName, result.RootName);
        Assert.Equal(taskPath, result.TaskPath);
        Assert.Equal(displayPath, result.DisplayPath);
    }

    [Fact]
    public void SharedHostCatalog_ContainsAllSpreadsheetFileTransferProfiles()
    {
        var catalog = new TraditionalTaskHostProfileCatalog();
        var expected = new[] { "Share", "siLock", "FTP", "SSHFTP", "S3", "AzureBlob", "SharePoint", "GoogleStorage" };

        foreach (var type in expected)
        {
            var profile = catalog.Find(type);
            Assert.NotNull(profile);
            Assert.True(profile!.SourceWriteSupported, $"{type} Source contract should be supported.");
            Assert.True(profile.DestinationWriteSupported, $"{type} Destination contract should be supported.");
        }
    }

    [Fact]
    public void SharedHostCatalog_AppliesExplicitCloudRootFields()
    {
        var catalog = new TraditionalTaskHostProfileCatalog();
        var profile = catalog.Require("S3", "Destination");
        var step = new System.Text.Json.Nodes.JsonObject();

        catalog.ApplyDestinationDefaults(step, profile);
        catalog.ApplyLocation(step, profile, "/outbound/client", "bucket-a");

        Assert.Equal("bucket-a", step["Bucket"]!.GetValue<string>());
        Assert.Equal(0, step["UseDefBucket"]!.GetValue<int>());
        Assert.Equal("/outbound/client", step["Path"]!.GetValue<string>());
    }

    [Fact]
    public void SharedHostCatalog_AppliesSharePointLibraryGuid()
    {
        var catalog = new TraditionalTaskHostProfileCatalog();
        var profile = catalog.Require("SharePoint", "Destination");
        var step = new System.Text.Json.Nodes.JsonObject();

        catalog.ApplyLocation(step, profile, "/EDI/Outbound", "Documents", "library-guid");

        Assert.Equal("Documents", step["DocumentLibrary"]!.GetValue<string>());
        Assert.Equal("library-guid", step["DocumentLibraryGuid"]!.GetValue<string>());
        Assert.Equal(0, step["UseDefDocumentLibrary"]!.GetValue<int>());
    }

    [Fact]
    public void SharedHostCatalog_UsesDefaultAzureContainerWhenRootIsOmitted()
    {
        var catalog = new TraditionalTaskHostProfileCatalog();
        var profile = catalog.Require("AzureBlob", "Source");
        var step = new System.Text.Json.Nodes.JsonObject();

        catalog.ApplyLocation(step, profile, "/daily/inbound");

        Assert.Equal(1, step["UseDefContainer"]!.GetValue<int>());
        Assert.Equal(string.Empty, step["Container"]!.GetValue<string>());
        Assert.Equal("/daily/inbound", step["Path"]!.GetValue<string>());
    }

    [Fact]
    public void SharedHostCatalog_UsesFolderNameForMoveItTransfer()
    {
        var catalog = new TraditionalTaskHostProfileCatalog();
        var profile = catalog.Require("siLock", "Source");
        var step = new System.Text.Json.Nodes.JsonObject();

        catalog.ApplyLocation(step, profile, "/Home/Inbound");

        Assert.Equal("/Home/Inbound", step["FolderName"]!.GetValue<string>());
        Assert.Equal(string.Empty, step["FolderID"]!.GetValue<string>());
        Assert.Null(step["Path"]);
    }
}

