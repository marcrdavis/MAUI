using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.Security;

namespace MoveItAutomationUI.Tests.Security;

public sealed class MauiAdminAuthorizationHandlerTests
{
    [Fact]
    public async Task AdminRequirement_FailsWhenMoveItSessionIsNotLive()
    {
        var requirement = new MauiAdminRequirement();
        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            BuildPrincipal("admin"),
            resource: null);
        var handler = new MauiAdminAuthorizationHandler(
            new FakeSecurityService(isAdmin: true),
            new FakeSessionService(isAuthenticated: false));

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task AdminRequirement_SucceedsWhenRoleAndLiveSessionAreValid()
    {
        var requirement = new MauiAdminRequirement();
        var context = new AuthorizationHandlerContext(
            new[] { requirement },
            BuildPrincipal("admin"),
            resource: null);
        var handler = new MauiAdminAuthorizationHandler(
            new FakeSecurityService(isAdmin: true),
            new FakeSessionService(isAuthenticated: true));

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    private static ClaimsPrincipal BuildPrincipal(string username)
        => new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, username) }, "test"));

    [Theory]
    [InlineData(true, false, false, true, false)]
    [InlineData(true, true, false, true, true)]
    [InlineData(true, false, true, true, true)]
    [InlineData(true, true, true, true, true)]
    [InlineData(true, true, false, false, false)]
    [InlineData(true, false, true, false, false)]
    [InlineData(true, true, true, false, false)]
    [InlineData(false, false, true, true, false)]
    [InlineData(false, true, false, true, true)]
    public async Task TemplateAdministration_AllowsEitherAdminOnlyWithALiveSession(
        bool allowMoveItAdmin, bool mauiAdmin, bool moveItAdmin, bool live, bool expected)
    {
        var requirement = new MauiAdminRequirement(allowMoveItAdmin);
        var context = new AuthorizationHandlerContext(new[] { requirement }, BuildPrincipal("user"), resource: null);
        var handler = new MauiAdminAuthorizationHandler(new FakeSecurityService(mauiAdmin), new FakeSessionService(live, moveItAdmin));

        await handler.HandleAsync(context);

        Assert.Equal(expected, context.HasSucceeded);
    }

    [Fact]
    public async Task TemplateAdministration_DeniesAnonymousPrincipalEvenWithAdminSession()
    {
        var requirement = new MauiAdminRequirement(allowMoveItAdmin: true);
        var context = new AuthorizationHandlerContext(new[] { requirement }, new ClaimsPrincipal(new ClaimsIdentity()), resource: null);
        var handler = new MauiAdminAuthorizationHandler(new FakeSecurityService(true), new FakeSessionService(true, true));

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    private sealed class FakeSecurityService : IMauiSecurityService
    {
        private readonly bool _isAdmin;
        public FakeSecurityService(bool isAdmin) => _isAdmin = isAdmin;
        public IReadOnlyList<MauiSecurityUser> GetUsers() => Array.Empty<MauiSecurityUser>();
        public bool IsAdmin(string? username) => _isAdmin;
        public string GetRole(string? username) => _isAdmin ? MauiSecurityRoles.Admin : MauiSecurityRoles.User;
        public Task<MauiSecurityUser> RecordSuccessfulLoginAsync(string username, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveUsersAsync(IReadOnlyList<ConfigurationSecurityUserSaveRequest> requestedUsers, IReadOnlyCollection<string> originalUsernames, string modifiedBy, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeSessionService : IMoveItSessionService
    {
        private readonly bool _moveItAdmin;
        public FakeSessionService(bool isAuthenticated, bool moveItAdmin = false)
        { IsAuthenticated = isAuthenticated; _moveItAdmin = moveItAdmin; }
        public bool IsAuthenticated { get; }
        public MoveItSessionState? Get() => IsAuthenticated ? new() { Info = new() { IsAdmin = _moveItAdmin } } : null;
        public string Create(MoveItSessionState state) => throw new NotSupportedException();
        public void Update(MoveItSessionState state) => throw new NotSupportedException();
        public bool HasLiveBrowserSession(ClaimsPrincipal principal) => IsAuthenticated;
        public void UpdateTokens(string accessToken, string refreshToken, DateTimeOffset accessTokenExpiresUtc) => throw new NotSupportedException();
        public bool DismissSystemMessage() => false;
        public bool RecordUserActivity() => false;
        public void Clear() { }
        public IDisposable PushBackgroundSession(MoveItSessionState state) => throw new NotSupportedException();
    }
}
