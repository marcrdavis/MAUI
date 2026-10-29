using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.MoveIt;

// The native Web Admin capture has an empty POST request, followed by a JSON
// acknowledgement. Exercise the production sender; do not confuse those two bodies.
public sealed class TamperResetContractTests
{
    [Fact]
    public async Task NativeResetSendsNoPayloadAndAcceptsCapturedAcknowledgement()
    {
        using var handler = new ResetHandler(HttpStatusCode.OK, """{"id":"-1","status":"success","errorcode":0}""");
        await Client(handler).ResetTamperDetectionAsync();
        Assert.True(handler.Calls == 1);
    }

    [Theory]
    [InlineData("""{"status":"error","errorcode":1,"message":"Reset denied."}""", "Reset denied.")]
    [InlineData("""{"status":"success","errorcode":1}""", "could not reset tamper detection")]
    [InlineData("""{"status":"error","errorcode":0}""", "could not reset tamper detection")]
    [InlineData("""{"status":"success"}""", "could not reset tamper detection")]
    [InlineData("{}", "could not reset tamper detection")]
    public async Task IncompleteOrFailedAcknowledgementIsNotSuccess(string response, string expectedMessage)
    {
        using var handler = new ResetHandler(HttpStatusCode.OK, response);
        var error = await Assert.ThrowsAsync<MoveItApiException>(() => Client(handler).ResetTamperDetectionAsync());
        Assert.Contains(expectedMessage, error.Message);
        Assert.True(handler.Calls == 1);
    }

    [Fact]
    public async Task DatabaseRejectionRemainsAnErrorWithoutReplayingTheReset()
    {
        const string response = """{"status":422,"errors":[{"message":"Data too long for column 'IPAddress' at row 1"}]}""";
        using var handler = new ResetHandler(HttpStatusCode.UnprocessableEntity, response);
        var error = await Assert.ThrowsAsync<MoveItApiException>(() => Client(handler).ResetTamperDetectionAsync());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, error.StatusCode);
        Assert.Contains("IPAddress", error.Message);
        Assert.True(handler.Calls == 1);
    }

    [Fact]
    public void ResetRetainsAdminAntiforgeryAndPostRequirements()
    {
        var action = typeof(MoveItServerController).GetMethod(nameof(MoveItServerController.ResetTamperDetection))!;
        Assert.Contains(action.GetCustomAttributes<AuthorizeAttribute>(), attribute => attribute.Policy == MauiSecurityPolicies.Admin);
        Assert.NotNull(action.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    private static MoveItRestClient Client(HttpMessageHandler handler)
    {
        var session = DispatchProxy.Create<IMoveItSessionService, SessionProxy>();
        return new MoveItRestClient(new ClientFactory(handler), session,
            ConfigurationStartupState.Capture(new ConfigurationBuilder().Build()), null!, NullLogger<MoveItRestClient>.Instance);
    }

    private sealed class ResetHandler(HttpStatusCode status, string response) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://moveit.test/api/v0/actions/resethashing", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer test-token", request.Headers.Authorization!.ToString());
            Assert.Null(request.Content); // No JSON envelope, empty object or literal JSON null.
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    public class SessionProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => method?.Name == nameof(IMoveItSessionService.Get)
                ? new MoveItSessionState
                {
                    WebAdminUrl = "https://moveit.test", AccessToken = "test-token",
                    AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
                }
                : throw new NotSupportedException(method?.Name);
    }
}
