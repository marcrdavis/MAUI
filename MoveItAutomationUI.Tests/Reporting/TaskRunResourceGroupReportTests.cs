using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.Reporting;
using MoveItAutomationUI.Services.Security;
using MoveItAutomationUI.Tests.MoveIt;

namespace MoveItAutomationUI.Tests.Reporting;

public sealed class TaskRunResourceGroupReportTests
{
    [Theory]
    [InlineData("column", null)]
    [InlineData("column", "failed")]
    [InlineData("scope", null)]
    [InlineData("search", null)]
    public async Task ReportUsingGroupFilterFailsWhenLookupFails(string filter, string? preset)
    {
        var view = ViewState();
        switch (filter)
        {
            case "column": view.Filters["group"] = "Operations"; break;
            case "scope": view.ScopeFilters["Group"] = "Operations"; break;
            case "search":
                view.SearchColumns.Add("Group");
                view.SearchText = "Operations";
                break;
        }

        using var services = Services(() => [Run(101)], _ => throw new HttpRequestException("Inventory offline."));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DataSource(services).LoadAsync(Report(view, preset)));
        Assert.Contains("Unable to load MOVEit Resource Group assignments", error.Message);
    }

    [Fact]
    public async Task FailedLookupOnLaterBatchRejectsTheWholeReport()
    {
        var view = ViewState();
        view.Filters["Group"] = "Operations";
        var fetched = 0;
        using var services = Services(
            () => ++fetched == 1 ? Enumerable.Range(1, 101).Reverse().Select(Run).ToArray() : [Run(1)],
            count => count == 1 ? [TaskWithGroup("Operations")] : throw new HttpRequestException("Inventory offline."));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DataSource(services).LoadAsync(Report(view)));
        Assert.Contains("Unable to load MOVEit Resource Group assignments", error.Message);
        Assert.Equal(2, fetched);
    }

    [Fact]
    public async Task ReportWithoutGroupFilterStillLoadsRunsWhenLookupFails()
    {
        using var services = Services(() => [Run(101)], _ => throw new HttpRequestException("Inventory offline."));
        var result = await DataSource(services).LoadAsync(Report(ViewState()));
        Assert.Single(result.Rows);
    }

    [Theory]
    [InlineData("Operations", true)]
    [InlineData("Development", false)]
    public async Task SuccessfulLookupCanReturnMatchingRowsOrAnEmptyResult(string group, bool matches)
    {
        var view = ViewState();
        view.Filters["Group"] = "Operations";
        using var services = Services(() => [Run(101)], _ => [TaskWithGroup(group)]);
        var result = await DataSource(services).LoadAsync(Report(view));
        if (matches)
            Assert.Equal("Operations", Assert.Single(result.Rows).Values["Group"]);
        else
            Assert.Empty(result.Rows);
    }

    private static ReportGridViewState ViewState()
        => new() { Columns = [new() { Key = "Group", Label = "Resource Group" }] };

    private static OutboundReportEntity Report(ReportGridViewState view, string? preset = null)
    {
        var source = new ReportSourceContext { Path = "/Records/TaskRun" };
        if (preset is not null) source.Query["dashboardView"] = preset;
        return new OutboundReportEntity
        {
            GridKey = "taskruns",
            OwnerUsername = "tester",
            SourceContextJson = JsonSerializer.Serialize(source),
            ViewStateJson = JsonSerializer.Serialize(view)
        };
    }

    private static JsonObject Run(int id) => new()
    {
        ["RunID"] = id.ToString(CultureInfo.InvariantCulture),
        ["TaskID"] = "10",
        ["Status"] = "Failed"
    };

    private static JsonObject TaskWithGroup(string group)
        => new() { ["ID"] = "10", ["Group"] = new JsonArray(JsonValue.Create(group)) };

    private static ServiceProvider Services(Func<IReadOnlyList<JsonObject>> runs, Func<int, IReadOnlyList<JsonObject>> inventory)
    {
        var lookups = 0;
        var rest = Stub<IMoveItRestClient>((method, _) => method.Name switch
        {
            nameof(IMoveItRestClient.GetTaskRunReportAsync) => Task.FromResult(runs()),
            nameof(IMoveItRestClient.GetTasksAsync) => Task.FromResult(inventory(++lookups)),
            _ => throw new NotSupportedException(method.Name)
        });
        var sessions = Stub<IMoveItSessionService>((method, _) => throw new NotSupportedException(method.Name));
        return new ServiceCollection()
            .AddLogging()
            .Configure<ReportBehaviorOptions>(options => options.TaskRun.BatchResultLimit.Value = 100)
            .AddSingleton<IAutomationReportService>(new AutomationReportService(rest, sessions))
            .BuildServiceProvider();
    }

    private static OutboundReportDataSource DataSource(IServiceProvider services)
        => new(services, Stub<IMauiSecurityService>((method, _) =>
            method.Name == nameof(IMauiSecurityService.IsAdmin) ? true : throw new NotSupportedException(method.Name)));

    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, HostSharedCrudTests.CrudProxy>();
        ((HostSharedCrudTests.CrudProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}
