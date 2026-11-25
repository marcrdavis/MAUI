using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.Reporting;
using MoveItAutomationUI.Services.Security;
using MoveItAutomationUI.Tests.MoveIt;

namespace MoveItAutomationUI.Tests.Reporting;

public sealed class ReportDeliveryNoneTests
{
    [Fact]
    public void NoneDiscardsScheduleDetailsWithoutValidatingHiddenControls()
    {
        var calculator = new ReportScheduleCalculator();
        var schedule = calculator.ValidateAndNormalize(new() {
            DeliveryMode = "none", TimeOfDay = "invalid", TimeZoneId = "invalid",
            Frequency = "invalid", Dates = ["invalid"], DaysOfWeek = [-1], DaysOfMonth = [50]
        }, "invalid");
        Assert.Equal("None", schedule.DeliveryMode);
        Assert.Empty(schedule.Frequency);
        Assert.Empty(schedule.Dates);
        Assert.Empty(schedule.DaysOfWeek);
        Assert.Empty(schedule.DaysOfMonth);
        Assert.Null(calculator.GetNextRunUtc(schedule, DateTimeOffset.UtcNow));
        Assert.Equal("None", calculator.Describe(schedule));
    }

    [Fact]
    public async Task NoneSavesWithoutRecipientsSmtpOrCredentialsAndIsNeverDue()
    {
        await using var f = await Fixture.StartAsync();
        var result = await f.Reports.CreateAsync(Request("None"));
        Assert.True(result.Saved);
        Assert.False(result.Sent);
        Assert.Empty(result.Warning);
        var report = await f.Db.OutboundReports.SingleAsync();
        Assert.Empty(report.Recipients);
        Assert.Null(report.NextRunUtc);
        var savedSchedule = JsonSerializer.Deserialize<ReportScheduleDefinition>(report.ScheduleJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("UTC", savedSchedule.TimeZoneId);
        Assert.Empty(await f.Repository.GetDueAsync(DateTimeOffset.UtcNow.AddYears(1)));
        await f.Reports.SetEnabledAsync(report.Id, false, "tester", false);
        await f.Reports.SetEnabledAsync(report.Id, true, "tester", false);
        Assert.True(report.Enabled);
        Assert.Null(report.NextRunUtc);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Reports.RunSavedNowAsync(report.Id, "tester", false));
        Assert.Equal(0, f.EmailCalls);
    }

    [Fact]
    public async Task SwitchingDeliveryClearsScheduleAndRestoringEmailStillValidatesIt()
    {
        await using var f = await Fixture.StartAsync();
        f.CredentialsConfigured = true;
        var saved = await f.Reports.CreateAsync(Request("Recurring"));
        f.CredentialsConfigured = false;
        var request = new UpdateOutboundReportRequest {
            Id = saved.Id!, Title = "Edited", Enabled = true,
            Recipients = "not an address", Schedule = new() { DeliveryMode = "None", TimeOfDay = "invalid" }
        };
        var updated = await f.Reports.UpdateAsync(request, "tester", false);
        Assert.Equal("None", updated.Schedule.DeliveryMode);
        Assert.Empty(updated.Recipients);
        Assert.Null(updated.NextRunUtc);
        Assert.Equal("UTC", updated.Schedule.TimeZoneId);
        request.Schedule = Request("Recurring").Schedule;
        request.Schedule.TimeZoneId = ""; // Edit sends the saved context, not a timezone selector.
        request.Recipients = "team@example.test";
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Reports.UpdateAsync(request, "tester", false));
        f.CredentialsConfigured = true;
        request.Recipients = "";
        await Assert.ThrowsAsync<ArgumentException>(() => f.Reports.UpdateAsync(request, "tester", false));
        request.Recipients = "team@example.test";
        updated = await f.Reports.UpdateAsync(request, "tester", false);
        Assert.NotNull(updated.NextRunUtc);
        Assert.Equal(new[] { "team@example.test" }, updated.Recipients);
    }

    [Theory]
    [InlineData("None")]
    [InlineData("Recurring")]
    [InlineData("Once")]
    [InlineData("Now")]
    public async Task PreparedCreateLinkIsInactiveUntilSaveAndIndependentOfDelivery(string mode)
    {
        await using var f = await Fixture.StartAsync();
        f.CredentialsConfigured = true;
        var prepared = f.Sharing.PrepareCreateLink("tester", "TEST");
        var guid = Guid.Parse(prepared.Path!.Split('/')[3]);
        Assert.Null(await f.Sharing.CsvAsync(guid, true, default));
        var request = Request(mode);
        request.ApiAccess = new() { Enabled = true, LinkDraft = prepared.LinkDraft };
        var result = await f.Sharing.CreateAsync(request, "tester", "TEST", default);
        Assert.True(result.Saved);
        Assert.Equal(mode == "Now", result.Sent);
        Assert.Equal(prepared.Path, (await f.Sharing.GetAsync(result.Id!, "tester", false, default)).Path);
        var csv = await f.Sharing.CsvAsync(guid, false, default);
        Assert.NotNull(csv);
        Assert.Equal(1, csv.RowCount);
        var report = await f.Db.OutboundReports.SingleAsync();
        Assert.Equal(mode is "Now" or "None", report.NextRunUtc is null);
        Assert.Equal(mode == "Now" ? "None" : mode,
            JsonSerializer.Deserialize<ReportScheduleDefinition>(report.ScheduleJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.DeliveryMode);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Sharing.CreateAsync(request, "tester", "TEST", default));
        Assert.Equal(1, await f.Db.OutboundReports.CountAsync());
    }

    [Fact]
    public async Task CreateDraftRejectsTamperingOtherUsersEnvironmentsAndFailedSaves()
    {
        await using var f = await Fixture.StartAsync();
        var prepared = f.Sharing.PrepareCreateLink("tester", "TEST");
        var request = Request("None");
        request.ApiAccess = new() { Enabled = true, LinkDraft = prepared.LinkDraft };
        await Assert.ThrowsAsync<ArgumentException>(() => f.Sharing.CreateAsync(request, "other", "TEST", default));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Sharing.CreateAsync(request, "tester", "OTHER", default));
        request.ApiAccess.LinkDraft = "tampered";
        await Assert.ThrowsAsync<ArgumentException>(() => f.Sharing.CreateAsync(request, "tester", "TEST", default));
        request.ApiAccess.LinkDraft = prepared.LinkDraft;
        request.Title = "";
        await Assert.ThrowsAsync<ArgumentException>(() => f.Sharing.CreateAsync(request, "tester", "TEST", default));
        Assert.Empty(await f.Db.OutboundReports.ToArrayAsync());
        Assert.Empty(await f.Db.OutboundReportApiShares.ToArrayAsync());
        request.Title = "Retry";
        await f.Sharing.CreateAsync(request, "tester", "TEST", default);
        Assert.Equal(prepared.Path, (await f.Sharing.GetAsync((await f.Db.OutboundReports.SingleAsync()).Id, "tester", false, default)).Path);
    }

    [Fact]
    public async Task SendNowEmailFailureRetainsSavedApiReportWithAnExplicitWarning()
    {
        await using var f = await Fixture.StartAsync();
        var prepared = f.Sharing.PrepareCreateLink("tester", "TEST");
        var request = Request("Now");
        request.ApiAccess = new() { Enabled = true, LinkDraft = prepared.LinkDraft };
        f.EmailFailure = new InvalidOperationException("SMTP unavailable");
        var result = await f.Sharing.CreateAsync(request, "tester", "TEST", default);
        Assert.True(result.Saved);
        Assert.False(result.Sent);
        Assert.Contains("email delivery failed", result.Warning);
        Assert.Equal(prepared.Path, (await f.Sharing.GetAsync(result.Id!, "tester", false, default)).Path);
        Assert.Null((await f.Db.OutboundReports.SingleAsync()).NextRunUtc);
    }

    [Fact]
    public async Task NoneWithoutRecipientsRoundTripsThroughReportExportAndImport()
    {
        await using var f = await Fixture.StartAsync();
        var result = await f.Reports.CreateAsync(Request("None"));
        var portability = new OutboundReportPortabilityService(f.Db, new ReportScheduleCalculator(), f.Security);
        var package = await portability.ExportAsync([result.Id!], "tester", false);
        using var stream = new MemoryStream(package.Content);
        var file = new FormFile(stream, 0, stream.Length, "file", package.FileName);
        var preview = await portability.PreflightImportAsync(file, "other");
        Assert.Equal(0, preview.ErrorCount);
        stream.Position = 0;
        await portability.ImportAsync(file, "other");
        var imported = await f.Db.OutboundReports.SingleAsync(r => r.OwnerUsername == "other");
        Assert.Empty(imported.Recipients);
        Assert.False(imported.Enabled);
        Assert.Null(imported.NextRunUtc);
        Assert.Empty(await f.Db.OutboundReportApiShares.ToArrayAsync());
    }

    private static CreateOutboundReportRequest Request(string mode) => new() {
        GridKey = "tasks", Title = "Operations failures", Recipients = mode == "None" ? "" : "team@example.test",
        ViewState = new() { Columns = [new() { Key = "Name", Label = "Task Name" }] },
        SourceContext = new() { Path = "/Tasks" }, Snapshot = new(),
        Schedule = new() { DeliveryMode = mode, Frequency = "Daily", TimeOfDay = "08:00", TimeZoneId = "UTC",
            Dates = [DateTime.UtcNow.AddDays(2).ToString("yyyy-MM-dd")] }
    };

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ReportApiRuntime _runtime = new();
        public MetadataDbContext Db { get; }
        public OutboundReportRepository Repository { get; }
        public OutboundReportService Reports { get; }
        public ReportApiSharingService Sharing { get; }
        public IMauiSecurityService Security { get; }
        public bool CredentialsConfigured;
        public int EmailCalls;
        public Exception? EmailFailure;

        private Fixture(SqliteConnection connection)
        {
            _connection = connection;
            Db = new(new DbContextOptionsBuilder<MetadataDbContext>().UseSqlite(connection).Options);
            var session = new MoveItSessionState { Username = "tester", EnvironmentKey = "TEST",
                Info = new() { ServerTimezoneInfo = new() { TimeZoneName = "UTC" } } };
            var sessions = Stub<IMoveItSessionService>((method, _) => method.Name switch {
                "Get" => session, "PushBackgroundSession" => new Lease(), _ => throw new NotSupportedException(method.Name)
            });
            Security = Stub<IMauiSecurityService>((method, _) => method.Name == "IsAdmin" ? false : throw new NotSupportedException(method.Name));
            Repository = new(Db, sessions);
            var data = new OutboundReportData([new() { Key = "Name", Label = "Task Name" }], [new() { Values = new() { ["Name"] = "Example" } }]);
            var source = Stub<IOutboundReportDataSource>((method, _) => method.Name switch {
                "NormalizeSnapshot" => data, "LoadAsync" => Task.FromResult(data), _ => throw new NotSupportedException(method.Name)
            });
            var email = Stub<IOutboundReportEmailService>((method, _) => {
                EmailCalls++;
                if (EmailFailure is not null) throw EmailFailure;
                return method.Name == "GetConfigurationStatus" ? new OutboundEmailConfigurationStatus(true, "") : Task.CompletedTask;
            });
            var credentials = Stub<IDataAccessCredentialService>((method, _) => method.Name switch {
                "IsEnvironmentConfigured" => CredentialsConfigured,
                "GetConfiguredForEnvironment" => (IReadOnlyList<DataAccessCredential>)[new("node", "TEST", "Test", "data", "password")],
                _ => throw new NotSupportedException(method.Name)
            });
            var rest = Stub<IMoveItRestClient>((method, _) => method.Name switch {
                "AuthenticateAsync" => Task.FromResult(session), "GetInfoAsync" => Task.FromResult(new MoveItInfo { IsAdmin = true }),
                _ => throw new NotSupportedException(method.Name)
            });
            Reports = new(Repository, source, email, new ReportScheduleCalculator(), credentials, sessions, rest, Security, NullLogger<OutboundReportService>.Instance);
            Sharing = new(Db, new EphemeralDataProtectionProvider(), Reports, Repository, Security, _runtime, NullLogger<ReportApiSharingService>.Instance);
        }
        public static async Task<Fixture> StartAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var fixture = new Fixture(connection);
            await fixture.Db.Database.EnsureCreatedAsync();
            return fixture;
        }
        public async ValueTask DisposeAsync() { _runtime.Dispose(); await Db.DisposeAsync(); await _connection.DisposeAsync(); }
        private sealed class Lease : IDisposable { public void Dispose() { } }
    }
    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, HostSharedCrudTests.CrudProxy>();
        ((HostSharedCrudTests.CrudProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}
