using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.Reporting;

namespace MoveItAutomationUI.Tests.Reporting;

public sealed class OutboundReportSourcePolicyTests
{
    [Theory]
    [InlineData("logs", false, false)]
    [InlineData("LOGS", false, false)]
    [InlineData(" logs ", false, false)]
    [InlineData("logs", true, true)]
    [InlineData("tasks", false, true)]
    [InlineData("audit", false, true)]
    public void IsAllowed_EnforcesAdminOnlyReportSources(string gridKey, bool isAdmin, bool expected)
        => Assert.Equal(expected, OutboundReportSourcePolicy.IsAllowed(gridKey, isAdmin));

    [Fact]
    public void EnsureAllowed_RejectsLogsForNormalUser()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => OutboundReportSourcePolicy.EnsureAllowed("logs", isAdmin: false));
        Assert.Contains("Administrator", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureAllowed_AllowsLogsForAdmin()
        => OutboundReportSourcePolicy.EnsureAllowed("logs", isAdmin: true);

    [Theory]
    [InlineData("tasks", "JSON:Active=1", true)]
    [InlineData("TASKS", "json:Name contains invoice", true)]
    [InlineData("hosts", "JsOn:Port>=22", true)]
    [InlineData(" hosts ", "JSON:Port=22", true)]
    [InlineData("tasks", "Active=1", false)]
    [InlineData("tasks", " JSON:Active=1", false)]
    [InlineData("hosts", "example.com", false)]
    [InlineData("scripts", "JSON:Value=1", false)]
    [InlineData("tasks", "", false)]
    [InlineData("tasks", null, false)]
    public void ScheduledJsonSearchesAreRejectedOnlyForJsonEnabledGrids(string grid, string? search, bool rejected)
    {
        if (rejected)
        {
            var error = Assert.Throws<InvalidOperationException>(() => OutboundReportSourcePolicy.EnsureScheduledSearchSupported(grid, search));
            Assert.Contains("Clear the JSON search", error.Message);
        }
        else OutboundReportSourcePolicy.EnsureScheduledSearchSupported(grid, search);
    }
}

public sealed class OutboundReportServiceAuthorizationTests
{
    [Fact]
    public async Task CreateAsync_RejectsLogsReportForNormalUserBeforeAnyDataAccess()
    {
        var service = new OutboundReportService(
            repository: null!,
            dataSource: null!,
            email: null!,
            scheduleCalculator: null!,
            dataAccessCredentials: null!,
            sessionService: new FakeSessionService(),
            restClient: null!,
            security: new FakeSecurityService(isAdmin: false),
            logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<OutboundReportService>.Instance);

        var request = new MoveItAutomationUI.Models.CreateOutboundReportRequest
        {
            GridKey = "logs",
            Title = "Restricted",
            Recipients = "test@example.com"
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync(request));
        Assert.Contains("Administrator", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScheduledJsonSearchIsRejectedBeforeDataAccessOrPersistence()
    {
        var request = JsonRequest(OutboundReportDeliveryModes.Recurring);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => JsonService().CreateAsync(request));
        Assert.Contains("JSON searches can only be sent now", error.Message);
    }

    [Fact]
    public async Task JsonSendNowStillReachesNormalSmtpReadinessCheck()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => JsonService().CreateAsync(JsonRequest(OutboundReportDeliveryModes.Now)));
        Assert.Contains("outbound email", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("JSON searches", error.Message);
    }

    [Fact]
    public async Task UpdatingSavedViewCannotPersistUnsupportedJsonSearch()
    {
        var repository = DispatchProxy.Create<IOutboundReportRepository, JsonReportRepositoryProxy>();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => JsonService(repository).UpdateViewAsync(
            new UpdateOutboundReportViewRequest { Id = "one", GridKey = "tasks", ViewState = new() { SearchText = "JSON:Active=1" } },
            "normal-user", false));
        Assert.Contains("JSON searches can only be sent now", error.Message);
        Assert.Equal(0, ((JsonReportRepositoryProxy)(object)repository).Writes);
    }

    [Fact]
    public async Task PreviouslySavedJsonSearchFailsExplicitlyBeforeLoadingOrdinaryGridRows()
    {
        var source = new OutboundReportDataSource(null!, new FakeSecurityService(false));
        var report = new OutboundReportEntity { GridKey = "hosts", OwnerUsername = "normal-user",
            ViewStateJson = JsonSerializer.Serialize(new ReportGridViewState { SearchText = "json:Port=22" }) };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => source.LoadAsync(report));
        Assert.Contains("JSON searches can only be sent now", error.Message);
    }

    private static CreateOutboundReportRequest JsonRequest(string mode) => new()
    {
        GridKey = "tasks", Title = "JSON report", Recipients = "test@example.com",
        ViewState = new() { SearchText = "JSON:Active=1" }, Snapshot = new(),
        Schedule = new() { DeliveryMode = mode, Frequency = OutboundReportFrequencies.Daily, TimeOfDay = "08:00", TimeZoneId = "UTC" }
    };

    private static OutboundReportService JsonService(IOutboundReportRepository? repository = null) => new(
        repository: repository!, dataSource: null!,
        email: new OutboundReportEmailService(new ConfigurationBuilder().Build(), null!),
        scheduleCalculator: new ReportScheduleCalculator(), dataAccessCredentials: null!,
        sessionService: new FakeSessionService(), restClient: null!, security: new FakeSecurityService(false),
        logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<OutboundReportService>.Instance);

    public class JsonReportRepositoryProxy : DispatchProxy
    {
        public int Writes { get; private set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name == "GetAsync") return Task.FromResult<OutboundReportEntity?>(new() { Id = "one", GridKey = "tasks" });
            Writes++;
            throw new NotSupportedException(method?.Name);
        }
    }

    private sealed class FakeSessionService : MoveItAutomationUI.Services.MoveIt.IMoveItSessionService
    {
        private readonly MoveItAutomationUI.Services.MoveIt.MoveItSessionState _state = new()
        {
            ServerKey = "NODE41",
            EnvironmentKey = "TEST",
            Username = "normal-user",
            AccessToken = "token",
            LastUserActivityUtc = DateTimeOffset.UtcNow
        };
        public bool IsAuthenticated => true;
        public MoveItAutomationUI.Services.MoveIt.MoveItSessionState? Get() => _state;
        public string Create(MoveItAutomationUI.Services.MoveIt.MoveItSessionState state) => throw new NotSupportedException();
        public void Update(MoveItAutomationUI.Services.MoveIt.MoveItSessionState state) => throw new NotSupportedException();
        public bool HasLiveBrowserSession(System.Security.Claims.ClaimsPrincipal principal) => true;
        public void UpdateTokens(string accessToken, string refreshToken, DateTimeOffset accessTokenExpiresUtc) => throw new NotSupportedException();
        public bool DismissSystemMessage() => true;
        public bool RecordUserActivity() => true;
        public void Clear() { }
        public IDisposable PushBackgroundSession(MoveItAutomationUI.Services.MoveIt.MoveItSessionState state) => throw new NotSupportedException();
    }

    private sealed class FakeSecurityService : MoveItAutomationUI.Services.Security.IMauiSecurityService
    {
        private readonly bool _isAdmin;
        public FakeSecurityService(bool isAdmin) => _isAdmin = isAdmin;
        public IReadOnlyList<MoveItAutomationUI.Models.MauiSecurityUser> GetUsers() => Array.Empty<MoveItAutomationUI.Models.MauiSecurityUser>();
        public bool IsAdmin(string? username) => _isAdmin;
        public string GetRole(string? username) => _isAdmin ? MoveItAutomationUI.Models.MauiSecurityRoles.Admin : MoveItAutomationUI.Models.MauiSecurityRoles.User;
        public Task<MoveItAutomationUI.Models.MauiSecurityUser> RecordSuccessfulLoginAsync(string username, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveUsersAsync(IReadOnlyList<MoveItAutomationUI.Models.ConfigurationSecurityUserSaveRequest> requestedUsers, IReadOnlyCollection<string> originalUsernames, string modifiedBy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
