// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Retained Reports distinguish removed connections from missing unattended credentials.

using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.Reporting;
using MoveItAutomationUI.Services.Security;
using MoveItAutomationUI.Tests.MoveIt;

namespace MoveItAutomationUI.Tests.Reporting;

public sealed class ReportEnvironmentReadinessTests
{
    [Fact]
    public async Task RemovingAndRestoringConnectionReclassifiesReadinessWithoutChangingReports()
    {
        var f = new Fixture();
        f.Servers.AddRange([Server("qa-node", "QA"), Server("prod-node", "PROD")]);
        f.Reports.AddRange([Report("QA"), Report("PROD") with { Enabled = false }]);
        f.Credentials.Add("QA");
        var before = await f.List();
        Assert.Equal(new[] { "PROD" }, Values(before, "missingDataAccessEnvironments"));
        Assert.Empty(Values(before, "unconfiguredEnvironments"));

        f.Servers.RemoveAt(1);
        f.CredentialChecks.Clear();
        var removed = await f.List();
        Assert.Empty(Values(removed, "missingDataAccessEnvironments"));
        Assert.Equal(new[] { "PROD" }, Values(removed, "unconfiguredEnvironments"));
        Assert.Equal(new[] { "QA" }, f.CredentialChecks);
        Assert.Equal(2, removed.GetProperty("reports").GetArrayLength());
        Assert.False(f.Reports[1].Enabled);
        Assert.Equal("PROD", f.Reports[1].EnvironmentKey);

        f.Servers.Add(Server("replacement-prod-node", "prod"));
        f.Credentials.Add("PROD");
        var restored = await f.List();
        Assert.Empty(Values(restored, "missingDataAccessEnvironments"));
        Assert.Empty(Values(restored, "unconfiguredEnvironments"));
    }

    [Fact]
    public async Task RemovingLastConnectionAndRestoringWithoutCredentialsReclassifiesReadiness()
    {
        var f = new Fixture();
        f.Servers.Add(Server("prod-node", "PROD"));
        f.Reports.Add(Report(" prod "));
        f.Credentials.Add("PROD");
        var ready = await f.List();
        Assert.Empty(Values(ready, "missingDataAccessEnvironments"));
        Assert.Empty(Values(ready, "unconfiguredEnvironments"));

        f.Servers.Clear();
        f.CredentialChecks.Clear();
        var removed = await f.List();
        Assert.Empty(Values(removed, "missingDataAccessEnvironments"));
        Assert.Equal(new[] { "PROD" }, Values(removed, "unconfiguredEnvironments"));
        Assert.Empty(f.CredentialChecks);
        Assert.Single(removed.GetProperty("reports").EnumerateArray());

        f.Servers.Add(Server("replacement-prod-node", "PROD"));
        f.Credentials.Clear();
        var restored = await f.List();
        Assert.Equal(new[] { "PROD" }, Values(restored, "missingDataAccessEnvironments"));
        Assert.Empty(Values(restored, "unconfiguredEnvironments"));
        Assert.Equal(new[] { "PROD" }, f.CredentialChecks);
    }

    [Fact]
    public async Task RemainingConnectionInSameEnvironmentIsStillConfigured()
    {
        var f = new Fixture();
        f.Servers.Add(Server("prod-secondary", " prod "));
        f.Reports.AddRange([Report("PROD"), Report("prod")]);
        var missing = await f.List();
        Assert.Equal(new[] { "PROD" }, Values(missing, "missingDataAccessEnvironments"));
        Assert.Empty(Values(missing, "unconfiguredEnvironments"));
        Assert.Equal(new[] { "PROD" }, f.CredentialChecks);
        f.Credentials.Add("PROD");
        var ready = await f.List();
        Assert.Empty(Values(ready, "missingDataAccessEnvironments"));
        Assert.Empty(Values(ready, "unconfiguredEnvironments"));
    }

    [Fact]
    public async Task RemovedAndMissingCredentialEnvironmentsRemainDistinctAndServerKeyFallbackWorks()
    {
        var f = new Fixture();
        f.Servers.Add(Server("qa", ""));
        f.Reports.AddRange([Report("PROD"), Report("QA"), Report("prod")]);
        var result = await f.List();
        Assert.Equal(new[] { "QA" }, Values(result, "missingDataAccessEnvironments"));
        Assert.Equal(new[] { "PROD" }, Values(result, "unconfiguredEnvironments"));
        Assert.Equal(new[] { "QA" }, f.CredentialChecks);
    }

    [Theory]
    [InlineData(true, "all", true)]
    [InlineData(true, "mine", false)]
    [InlineData(false, "all", false)]
    public async Task ReadinessUsesOnlyReportsInTheRequestedOwnerScope(bool admin, string scope, bool includeAll)
    {
        var f = new Fixture { Admin = admin };
        f.Servers.Add(Server("qa-node", "QA"));
        f.Credentials.Add("QA");
        f.Reports.AddRange([Report("QA"), Report("PROD") with { OwnerUsername = "another-user" }]);
        var result = await f.List(scope);
        Assert.Equal(includeAll, f.IncludedAllOwners);
        Assert.Equal(includeAll ? new[] { "PROD" } : Array.Empty<string>(), Values(result, "unconfiguredEnvironments"));
        Assert.Empty(Values(result, "missingDataAccessEnvironments"));
    }

    [Fact]
    public async Task EmptyReportListHasNoEnvironmentWarningsOrCredentialReads()
    {
        var f = new Fixture();
        var result = await f.List();
        Assert.Empty(Values(result, "missingDataAccessEnvironments"));
        Assert.Empty(Values(result, "unconfiguredEnvironments"));
        Assert.Empty(f.CredentialChecks);
    }

    private static string[] Values(JsonElement response, string name)
        => response.GetProperty("readiness").GetProperty(name).EnumerateArray().Select(value => value.GetString()!).ToArray();

    private static MoveItServerDefinition Server(string key, string environment) => new() { Key = key, EnvironmentKey = environment };
    private static OutboundReportSummary Report(string environment) => new(
        Guid.NewGuid().ToString("D"), environment, "tester", "tasks", "Tasks", "Test report", "",
        [], true, new() { DeliveryMode = "None" }, null, null, "", "", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class Fixture
    {
        public bool Admin = true;
        public bool IncludedAllOwners;
        public List<MoveItServerDefinition> Servers { get; } = [];
        public List<OutboundReportSummary> Reports { get; } = [];
        public HashSet<string> Credentials { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> CredentialChecks { get; } = [];
        private readonly ReportsController _controller;

        public Fixture()
        {
            var reports = Stub<IOutboundReportService>((method, args) => {
                if (method.Name != nameof(IOutboundReportService.ListAsync)) throw new NotSupportedException(method.Name);
                IncludedAllOwners = (bool)args[1]!;
                IReadOnlyList<OutboundReportSummary> rows = Reports.Where(report => IncludedAllOwners || report.OwnerUsername == (string)args[0]!).ToArray();
                return Task.FromResult(rows);
            });
            var email = Stub<IOutboundReportEmailService>((method, _) => method.Name == nameof(IOutboundReportEmailService.GetConfigurationStatus)
                ? new OutboundEmailConfigurationStatus(true, "") : throw new NotSupportedException(method.Name));
            var credentials = Stub<IDataAccessCredentialService>((method, args) => {
                if (method.Name != nameof(IDataAccessCredentialService.IsEnvironmentConfigured)) throw new NotSupportedException(method.Name);
                var environment = (string)args[0]!;
                CredentialChecks.Add(environment);
                return Credentials.Contains(environment);
            });
            var security = Stub<IMauiSecurityService>((method, _) => method.Name == nameof(IMauiSecurityService.IsAdmin)
                ? Admin : throw new NotSupportedException(method.Name));
            var catalog = Stub<IMoveItServerCatalog>((method, _) => method.Name == nameof(IMoveItServerCatalog.GetServers)
                ? (IReadOnlyList<MoveItServerDefinition>)Servers.ToArray() : throw new NotSupportedException(method.Name));
            _controller = new(reports, null!, email, credentials, security, Snapshot(new UiOptions()), Snapshot(new GridBehaviorOptions()), catalog) {
                ControllerContext = new() { HttpContext = new DefaultHttpContext {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "tester")], "Test"))
                } }
            };
        }
        public async Task<JsonElement> List(string scope = "all")
        {
            var result = Assert.IsType<JsonResult>(await _controller.List(scope, default));
            return JsonSerializer.SerializeToElement(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
    }

    private static IOptionsSnapshot<T> Snapshot<T>(T value) where T : class
        => Stub<IOptionsSnapshot<T>>((method, _) => method.Name == "get_Value" ? value : throw new NotSupportedException(method.Name));

    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, HostSharedCrudTests.CrudProxy>();
        ((HostSharedCrudTests.CrudProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}
