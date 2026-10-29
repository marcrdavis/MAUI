using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Services.Logging;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.Logging;

public sealed class BrowserNotificationTests
{
    [Theory]
    [InlineData("error", LogLevel.Error)]
    [InlineData("warning", LogLevel.Warning)]
    [InlineData("default", LogLevel.Information)]
    [InlineData("success", LogLevel.Information)]
    public void RecordUsesServerIdentityAndExistingLogChannel(string kind, LogLevel expected)
    {
        var channel = new MauiApplicationLogChannel();
        var controller = Create(channel, new Session());
        var result = controller.Record(new("Operation finished", kind, "/maui/Hosts?password=do-not-log#fragment", "page"));
        Assert.IsType<NoContentResult>(result);
        Assert.True(channel.Reader.TryRead(out var entry));
        Assert.NotNull(entry);
        Assert.Equal(expected, entry.Level);
        Assert.Equal("tester", entry.Username);
        Assert.Equal("QA", entry.EnvironmentKey);
        Assert.Equal("/maui/Hosts", entry.RequestPath);
        Assert.Equal("Operation finished", entry.Message);
        Assert.Contains("ClientReported", entry.PropertiesJson);
        Assert.False(channel.Reader.TryRead(out _));
    }

    [Theory]
    [InlineData("password=secret", "password=[redacted]")]
    [InlineData("{\"OAuthClientSecret\":\"secret with spaces\"}", "{\"OAuthClientSecret\":[redacted]}")]
    [InlineData("access_token=abc.def", "access_token=[redacted]")]
    [InlineData("Bearer abc.def", "Bearer [redacted]")]
    [InlineData("Authorization: Bearer abc.def", "Authorization: [redacted]")]
    [InlineData("Authorization=Basic abcdef==", "Authorization=[redacted]")]
    [InlineData("Line one\nLine two", "Line one Line two")]
    public void MessageSanitizationRedactsCredentialsAndControlCharacters(string input, string expected)
        => Assert.Equal(expected, BrowserNotificationsController.Sanitize(input));

    [Fact]
    public void RecordRejectsExpiredSessionAndEmptyMessages()
    {
        var channel = new MauiApplicationLogChannel();
        Assert.IsType<UnauthorizedResult>(Create(channel, new Session(false)).Record(new("Message", "error", "/Hosts", "modal")));
        Assert.IsType<BadRequestResult>(Create(channel, new Session()).Record(new(" ", "error", "/Hosts", "modal")));
        Assert.False(channel.Reader.TryRead(out _));
        Assert.True(BrowserNotificationsController.Sanitize(new string('x', 5000)).Length <= 4000);
    }

    [Fact]
    public void EndpointRequiresAuthenticationAndAntiforgery()
    {
        Assert.NotNull(typeof(BrowserNotificationsController).GetCustomAttribute<AuthorizeAttribute>());
        var action = typeof(BrowserNotificationsController).GetMethod(nameof(BrowserNotificationsController.Record))!;
        Assert.NotNull(action.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.NotNull(action.GetCustomAttribute<RequestSizeLimitAttribute>());
    }

    private static BrowserNotificationsController Create(MauiApplicationLogChannel channel, Session session)
        => new(channel, session) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    private sealed class Session(bool live = true) : IMoveItSessionService
    {
        public bool IsAuthenticated => live;
        public MoveItSessionState? Get() => live ? new() { Username = "tester", EnvironmentKey = "QA", ServerKey = "node" } : null;
        public string Create(MoveItSessionState state) => throw new NotSupportedException();
        public void Update(MoveItSessionState state) => throw new NotSupportedException();
        public bool HasLiveBrowserSession(ClaimsPrincipal principal) => live;
        public void UpdateTokens(string accessToken, string refreshToken, DateTimeOffset accessTokenExpiresUtc) => throw new NotSupportedException();
        public bool DismissSystemMessage() => false;
        public bool RecordUserActivity() => false;
        public void Clear() { }
        public IDisposable PushBackgroundSession(MoveItSessionState state) => throw new NotSupportedException();
    }
}
