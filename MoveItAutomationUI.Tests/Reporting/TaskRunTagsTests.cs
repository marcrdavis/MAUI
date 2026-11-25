using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.Reporting;
using MoveItAutomationUI.Services.Security;
using MoveItAutomationUI.Tests.MoveIt;

namespace MoveItAutomationUI.Tests.Reporting;

public sealed class TaskRunTagsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TagsAreOptionalAndAvailableInLegacyGridSettingsAndConfiguration(bool alreadyAvailable)
    {
        using var f = new Fixture();
        var ui = f.Services.GetRequiredService<IOptionsSnapshot<UiOptions>>().Value;
        if (alreadyAvailable) ui.Grids["taskruns"].AvailableColumns.Add("Tags");
        var controller = f.Controller();
        var page = Assert.IsType<ReportPageViewModel>(Assert.IsType<ViewResult>(controller.TaskRun()).Model);
        Assert.Equal("tags", Assert.Single(page.Grid.Columns, column => column.Key == "Tags").Type);
        Assert.Single(page.Grid.Options.AvailableColumns, column => column == "Tags");
        Assert.DoesNotContain("Tags", page.Grid.Options.DefaultColumns);
        Assert.DoesNotContain("Tags", page.Grid.SearchColumns);
        Assert.Empty(f.MetadataCalls);

        var grids = new UiOptions();
        foreach (var key in ConfigurationSettingsCatalog.GridSectionKeys)
            grids.Grids[key] = new GridOptions { AvailableColumns = ["StartTime"], DefaultColumns = ["StartTime"] };
        var method = typeof(ConfigurationSettingsViewModelBuilder).GetMethod("BuildGridDefinitions", BindingFlags.NonPublic | BindingFlags.Static)!;
        var definitions = Assert.IsAssignableFrom<IReadOnlyList<ConfigurationGridDefinitionViewModel>>(
            method.Invoke(null, [grids, grids, new MetadataUiOptions(), new MetadataUiOptions(), new JsonObject()]));
        var taskRuns = Assert.Single(definitions, definition => definition.Key == "taskruns");
        Assert.Contains("Tags", taskRuns.AvailableColumns);
        Assert.Contains("Tags", taskRuns.SearchableColumns);
        Assert.DoesNotContain("Tags", taskRuns.DefaultColumns);
    }

    [Fact]
    public async Task UsesConfiguredTagsFieldAndOneEnvironmentScopedReadForDistinctTaskIds()
    {
        using var f = new Fixture();
        var batch = Batch(await f.Controller().TaskRunBatch());
        var rows = batch["rows"]!.AsArray();
        Assert.Equal(new[] { "Operations; Finance", "Operations; Finance", "Development", "", "" },
            rows.Select(row => row!["values"]!["Tags"]!.GetValue<string>()).ToArray());
        Assert.Equal("Operations; Finance", rows[0]!["rawValues"]!["Tags"]!.GetValue<string>());
        var call = Assert.Single(f.MetadataCalls);
        Assert.Equal("PROD", call.Environment);
        Assert.Equal("Task", call.ObjectType);
        Assert.Equal(new[] { "10", "20", "30" }, call.Ids);
        Assert.Equal(1, f.RunReads);
        Assert.Equal(1, f.InventoryReads);
    }

    [Fact]
    public async Task RefreshReloadsCurrentTagsWithoutChangingRunIdentityAndRespectsEnvironmentChanges()
    {
        using var f = new Fixture();
        var controller = f.Controller();
        var before = Batch(await controller.TaskRunBatch());
        f.Tags["PROD"]["10"] = "Updated";
        var refreshed = Batch(await controller.TaskRunBatch());
        Assert.Equal(before["rows"]![0]!["id"]!.ToString(), refreshed["rows"]![0]!["id"]!.ToString());
        Assert.Equal("Updated", refreshed["rows"]![0]!["values"]!["Tags"]!.ToString());
        f.Session.EnvironmentKey = "qa";
        var otherEnvironment = Batch(await controller.TaskRunBatch());
        Assert.Equal("QA", otherEnvironment["rows"]![0]!["values"]!["Tags"]!.ToString());
        Assert.Equal("QA", f.MetadataCalls[^1].Environment);
        Assert.Equal(3, f.MetadataCalls.Count);
    }

    [Theory]
    [InlineData("column", null)]
    [InlineData("column", "failed")]
    [InlineData("search", null)]
    [InlineData("scope", null)]
    public async Task SavedReportsUseTagsForFiltersAndCsvOutput(string mode, string? preset)
    {
        using var f = new Fixture();
        var view = View();
        switch (mode)
        {
            case "column": view.Filters["Tags"] = "operations"; break;
            case "search": view.SearchColumns.Add("Tags"); view.SearchText = "operations"; break;
            case "scope": view.ScopeFilters["Tags"] = "Operations; Finance"; break;
        }
        var result = await f.DataSource.LoadAsync(Report(view, preset));
        Assert.Equal(2, result.Rows.Count);
        Assert.All(result.Rows, row => Assert.Equal("Operations; Finance", row.Values["Tags"]));
        var csv = ReportCsvWriter.Write(result);
        Assert.StartsWith("Task Name,Tags\r\n", csv);
        Assert.Contains("Task 10,Operations; Finance", csv);
        Assert.DoesNotContain("<span", csv);
        Assert.Contains(csv, ReportCsvWriter.WriteEmail("Task Run", "", result));
        Assert.Single(f.MetadataCalls);
    }

    [Fact]
    public async Task SharedCsvApiGeneratesTheSavedTaskRunTagsColumnAndFilter()
    {
        using var f = new Fixture();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new MetadataDbContext(new DbContextOptionsBuilder<MetadataDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var view = View();
        view.Filters["Tags"] = "operations";
        var report = Report(view);
        report.Id = Guid.NewGuid().ToString("D");
        report.OwnerUsernameKey = "TESTER";
        report.Title = "Tagged failures";
        report.ScheduleJson = "{}";
        report.CreatedUtc = report.ModifiedUtc = DateTimeOffset.UtcNow;
        db.OutboundReports.Add(report);
        await db.SaveChangesAsync();
        var repository = Stub<IOutboundReportRepository>((method, _) => method.Name switch
        {
            nameof(IOutboundReportRepository.GetAsync) => Task.FromResult<OutboundReportEntity?>(report),
            nameof(IOutboundReportRepository.RecordExecutionAsync) => Task.CompletedTask,
            _ => throw new NotSupportedException(method.Name)
        });
        var reports = Stub<IOutboundReportService>((method, args) => method.Name switch
        {
            nameof(IOutboundReportService.UpdateAsync) => Task.FromResult<OutboundReportSummary>(null!),
            nameof(IOutboundReportService.GenerateAsync) => f.DataSource.LoadAsync((OutboundReportEntity)args[0]!, (CancellationToken)args[1]!),
            _ => throw new NotSupportedException(method.Name)
        });
        var security = Stub<IMauiSecurityService>((method, _) => method.Name == nameof(IMauiSecurityService.IsAdmin)
            ? true : throw new NotSupportedException(method.Name));
        using var runtime = new ReportApiRuntime();
        var sharing = new ReportApiSharingService(db, new EphemeralDataProtectionProvider(), reports, repository, security,
            runtime, NullLogger<ReportApiSharingService>.Instance);
        await sharing.UpdateAsync(new() { Id = report.Id, ApiAccess = new() { Enabled = true } }, "tester", false, default);
        var access = await sharing.GetAsync(report.Id, "tester", false, default);
        var controller = new ReportApiController(sharing, NullLogger<ReportApiController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.Request.Method = "GET";
        var result = Assert.IsType<FileContentResult>(await controller.Csv(access.Path!.Split('/')[3], default));
        Assert.Equal("text/csv; charset=utf-8", result.ContentType);
        var csv = Encoding.UTF8.GetString(result.FileContents);
        Assert.StartsWith("Task Name,Tags\r\n", csv);
        Assert.Contains("Task 10,Operations; Finance", csv);
        Assert.DoesNotContain("Development", csv);
        Assert.Single(f.MetadataCalls);
    }

    [Fact]
    public async Task FullReportEnrichesEveryBatchBeforeApplyingTheSavedTagsFilter()
    {
        using var f = new Fixture();
        f.Runs = count => count == 1
            ? Enumerable.Range(2, 101).Reverse().Select(id => Run(id, id % 2 == 0 ? "10" : "20")).ToArray()
            : [Run(2, "10"), Run(1, "30")];
        var view = View();
        view.Filters["Tags"] = "operations";
        var result = await f.DataSource.LoadAsync(Report(view));
        Assert.Equal(51, result.Rows.Count);
        Assert.Equal(2, f.MetadataCalls.Count);
        Assert.Equal(new[] { "10", "20" }, f.MetadataCalls[0].Ids);
        Assert.Equal(new[] { "10", "30" }, f.MetadataCalls[1].Ids);
    }

    [Fact]
    public async Task FailedMetadataReadOnALaterBatchRejectsTheWholeReport()
    {
        using var f = new Fixture();
        f.Runs = count => count == 1 ? Enumerable.Range(2, 101).Reverse().Select(id => Run(id, "10")).ToArray() : [Run(1, "10")];
        f.MetadataFailure = count => count == 2 ? new InvalidOperationException("Metadata offline.") : null;
        var view = View();
        view.Filters["Tags"] = "not:development";
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => f.DataSource.LoadAsync(Report(view)));
        Assert.Contains("Unable to load current task Tags metadata", error.Message);
        Assert.Equal(2, f.MetadataCalls.Count);
    }

    [Fact]
    public async Task UnconfiguredTagsDoNotReadOtherMetadataFields()
    {
        using var f = new Fixture(tagsConfigured: false);
        var batch = Batch(await f.Controller().TaskRunBatch());
        Assert.All(batch["rows"]!.AsArray(), row => Assert.Equal("", row!["values"]!["Tags"]!.ToString()));
        Assert.Empty(f.MetadataCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyBatchesAndMissingTaskIdsDoNotTriggerMetadataReads(bool includeRun)
    {
        using var f = new Fixture();
        f.Runs = _ => includeRun ? [Run(1, "")] : [];
        _ = Batch(await f.Controller().TaskRunBatch());
        Assert.Empty(f.MetadataCalls);
    }

    private static JsonNode Batch(IActionResult result)
        => JsonSerializer.SerializeToNode(Assert.IsType<JsonResult>(result).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static JsonObject Run(int id, string taskId) => new()
    {
        ["RunID"] = id.ToString(CultureInfo.InvariantCulture), ["TaskID"] = taskId,
        ["TaskName"] = "Task " + taskId, ["Status"] = "Failed"
    };

    private static ReportGridViewState View() => new()
    {
        Columns = [new() { Key = "TaskName", Label = "Task Name" }, new() { Key = "Tags", Label = "Tags", Type = "tags" }],
        FilterTypes = new() { ["Tags"] = "tags" }
    };

    private static OutboundReportEntity Report(ReportGridViewState view, string? preset = null)
    {
        var source = new ReportSourceContext { Path = "/Records/TaskRun" };
        if (preset is not null) source.Query["dashboardView"] = preset;
        return new() { GridKey = "taskruns", OwnerUsername = "tester", EnvironmentKey = "PROD",
            ViewStateJson = JsonSerializer.Serialize(view), SourceContextJson = JsonSerializer.Serialize(source) };
    }

    private sealed class Fixture : IDisposable
    {
        public ServiceProvider Services { get; }
        public MoveItSessionState Session { get; } = new() { ServerKey = "production-alias", EnvironmentKey = "prod", Username = "tester" };
        public Dictionary<string, Dictionary<string, string>> Tags { get; } = new()
        {
            ["PROD"] = new() { ["10"] = "Operations; Finance", ["20"] = "Development" },
            ["QA"] = new() { ["10"] = "QA" }
        };
        public List<(string Environment, string ObjectType, string[] Ids)> MetadataCalls { get; } = [];
        public Func<int, IReadOnlyList<JsonObject>> Runs { get; set; } = _ => [Run(5, "10"), Run(4, "10"), Run(3, "20"), Run(2, "30"), Run(1, "")];
        public Func<int, Exception?> MetadataFailure { get; set; } = _ => null;
        public int RunReads { get; private set; }
        public int InventoryReads { get; private set; }
        public OutboundReportDataSource DataSource => new(Services, Stub<IMauiSecurityService>((method, _) =>
            method.Name == nameof(IMauiSecurityService.IsAdmin) ? true : throw new NotSupportedException(method.Name)));

        public Fixture(bool tagsConfigured = true)
        {
            var sessions = Stub<IMoveItSessionService>((method, _) => method.Name == nameof(IMoveItSessionService.Get)
                ? Session : throw new NotSupportedException(method.Name));
            var rest = Stub<IMoveItRestClient>((method, _) => method.Name switch
            {
                nameof(IMoveItRestClient.GetTaskRunReportAsync) => Task.FromResult(Runs(++RunReads)),
                nameof(IMoveItRestClient.GetTasksAsync) => Inventory(),
                _ => throw new NotSupportedException(method.Name)
            });
            var metadata = Stub<IMetadataRepository>((method, args) =>
            {
                if (method.Name != nameof(IMetadataRepository.GetObjectMetadataBatchAsync)) throw new NotSupportedException(method.Name);
                ((CancellationToken)args[3]!).ThrowIfCancellationRequested();
                var environment = (string)args[0]!;
                var ids = ((IEnumerable<string>)args[2]!).ToArray();
                MetadataCalls.Add((environment, (string)args[1]!, ids));
                if (MetadataFailure(MetadataCalls.Count) is { } error) throw error;
                var values = new Dictionary<string, ObjectMetadataSnapshot>(StringComparer.OrdinalIgnoreCase);
                foreach (var id in ids)
                    if (Tags.TryGetValue(environment, out var byId) && byId.TryGetValue(id, out var tag))
                        values[id] = new(null, new Dictionary<string, string> { ["TeamTags"] = tag }, null, "", null, "");
                return Task.FromResult<IReadOnlyDictionary<string, ObjectMetadataSnapshot>>(values);
            });
            Services = new ServiceCollection().AddLogging()
                .Configure<ReportBehaviorOptions>(options => options.TaskRun.BatchResultLimit.Value = 100)
                .Configure<UiOptions>(options => options.Grids["taskruns"] = new GridOptions
                    { DefaultColumns = ["StartTime"], AvailableColumns = ["StartTime"], SearchColumns = ["TaskName"] })
                .Configure<MetadataUiOptions>(options => options.Tasks.Fields = [new()
                    { Name = "TeamTags", Label = "Team Tags", Type = tagsConfigured ? "tAgS" : "String" }])
                .AddSingleton(sessions).AddSingleton(metadata)
                .AddSingleton<IAutomationReportService>(new AutomationReportService(rest, sessions))
                .BuildServiceProvider();
        }

        private Task<IReadOnlyList<JsonObject>> Inventory()
        {
            InventoryReads++;
            return Task.FromResult<IReadOnlyList<JsonObject>>([new() { ["ID"] = "10" }, new() { ["ID"] = "20" }, new() { ["ID"] = "30" }]);
        }

        public RecordsController Controller()
        {
            var controller = ActivatorUtilities.CreateInstance<RecordsController>(Services);
            controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
            controller.Url = Stub<IUrlHelper>((method, _) => method.Name switch
            {
                nameof(IUrlHelper.Action) => "/Records/TaskRun",
                nameof(IUrlHelper.Content) => "/Records/TaskRun",
                nameof(IUrlHelper.IsLocalUrl) => true,
                _ => throw new NotSupportedException(method.Name)
            });
            return controller;
        }

        public void Dispose() => Services.Dispose();
    }

    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, HostSharedCrudTests.CrudProxy>();
        ((HostSharedCrudTests.CrudProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}
