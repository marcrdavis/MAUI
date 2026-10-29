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
