using System.Text.Json;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Reporting;

namespace MoveItAutomationUI.Tests.Reporting;

public sealed class OutboundReportSourceRoutesTests
{
    [Theory]
    [InlineData("tasks", "/Tasks/Index", "/Tasks/Index")]
    [InlineData("tasks", "/MAUI/Tasks/Index", "/Tasks/Index")]
    [InlineData("tasks", "/MAUI/Tasks", "/Tasks/Index")]
    [InlineData("tasks", "/QA%20Application/Tasks/Index/", "/Tasks/Index")]
    [InlineData("hosts", "/dev/AutomationUI/Hosts/Index", "/Hosts/Index")]
    [InlineData("upcomingruns", "/Tasks/Tasks/UpcomingRuns", "/Tasks/UpcomingRuns")]
    [InlineData("taskruns", "/Records/Records/TaskRun", "/Records/TaskRun")]
    [InlineData("fileactivity", "/dev/QA/Records/FileActivity", "/Records/FileActivity")]
    [InlineData("audit", "/Records/Audit", "/Records/Audit")]
    [InlineData("scripts", "/dev/MAUI/Resources/Scripts", "/Resources/Scripts")]
    [InlineData("datelists", "/Resources/DateLists", "/Resources/DateLists")]
    [InlineData("globalparameters", "/dev/Resources/GlobalParameters", "/Resources/GlobalParameters")]
    [InlineData("keysandcertificates", "/app/Resources/KeysAndCerts", "/Resources/KeysAndCerts")]
    [InlineData("logs", "/MAUI/Configuration/Logs", "/Configuration/Logs")]
    public void Normalize_RemovesPreviousApplicationPath(string gridKey, string path, string expected)
    {
        var result = OutboundReportSourceRoutes.Normalize(gridKey, new ReportSourceContext
        {
            Path = path,
            Query = new Dictionary<string, string> { ["filter"] = "failed", ["folderId"] = "123" }
        });
        Assert.Equal(expected, result.Path);
        Assert.Equal("failed", result.Query["filter"]);
        Assert.Equal("123", result.Query["folderId"]);
    }

    [Theory]
    [InlineData("tasks", "https://example.com/Tasks/Index")]
    [InlineData("tasks", "//evil.example/Tasks/Index")]
    [InlineData("tasks", "/OldApp/Hosts/Index")]
    [InlineData("tasks", "/OldApp/NotTasks/Index")]
    [InlineData("taskruns", "/OldApp/Records/Audit")]
    [InlineData("hosts", "/OldApp/Hosts/Index?filter=all")]
    [InlineData("hosts", "/OldApp/../Hosts/Index")]
    public void Normalize_RejectsUnrelatedOrUnsafeRoutes(string gridKey, string path)
    {
        Assert.Throws<ArgumentException>(() => OutboundReportSourceRoutes.Normalize(gridKey,
            new ReportSourceContext { Path = path }));
    }

    [Fact]
    public void NormalizeJson_ImportsLegacyReportWithoutPersistingItsFormerPathBase()
    {
        const string json = "{\"path\":\"/old/dev/Records/TaskRun\",\"query\":{\"taskId\":\"101\"}}";
        var normalized = OutboundReportSourceRoutes.NormalizeJson(json, "taskruns");
        var context = JsonSerializer.Deserialize<ReportSourceContext>(normalized,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(context);
        Assert.Equal("/Records/TaskRun", context.Path);
        Assert.Equal("101", context.Query["taskId"]);
    }

    [Fact]
    public void Normalize_DoesNotMutateTheExistingSavedDefinition()
    {
        var oldContext = new ReportSourceContext { Path = "/DEV/MAUI/Tasks/Index" };
        var normalized = OutboundReportSourceRoutes.Normalize("tasks", oldContext);
        Assert.Equal("/Tasks/Index", normalized.Path);
        Assert.Equal("/DEV/MAUI/Tasks/Index", oldContext.Path);
    }
    [Fact]
    public void Normalize_DropsLegacyReturnNavigationButKeepsRecordFilterQuery()
    {
        var source = new ReportSourceContext
        {
            Path = "/DEV/MAUI/Records/TaskRun",
            Query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["taskId"] = "101",
                ["filter"] = "failed",
                ["returnUrl"] = "/DEV/MAUI/Tasks/Index",
                ["returnLabel"] = "Task"
            }
        };
        var result = OutboundReportSourceRoutes.Normalize("taskruns", source);
        Assert.Equal("/Records/TaskRun", result.Path);
        Assert.Equal("101", result.Query["taskId"]);
        Assert.Equal("failed", result.Query["filter"]);
        Assert.False(result.Query.ContainsKey("returnUrl"));
        Assert.False(result.Query.ContainsKey("returnLabel"));
        Assert.True(source.Query.ContainsKey("returnUrl"));
    }

    [Fact]
    public void NormalizeJson_DropsLegacyIisSpecificRecordReturnUrlDuringPortability()
    {
        const string json = "{\"path\":\"/DEV/MAUI/Records/TaskRun\",\"query\":{\"taskId\":\"101\",\"returnUrl\":\"/DEV/MAUI/Tasks/Index\",\"returnLabel\":\"Task\"}}";
        var normalized = OutboundReportSourceRoutes.NormalizeJson(json, "taskruns");
        var context = JsonSerializer.Deserialize<ReportSourceContext>(normalized,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(context);
        Assert.Equal("/Records/TaskRun", context.Path);
        Assert.Equal("101", context.Query["taskId"]);
        Assert.False(context.Query.ContainsKey("returnUrl"));
        Assert.False(context.Query.ContainsKey("returnLabel"));
    }

}
