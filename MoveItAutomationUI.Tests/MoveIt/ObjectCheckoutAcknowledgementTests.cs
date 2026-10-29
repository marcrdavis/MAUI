using System.Net;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class ObjectCheckoutAcknowledgementTests
{
    [Theory]
    [InlineData("This section is not checked out", false)]
    [InlineData("This object has not been checked out", false)]
    [InlineData("MOVEit REST request failed: This section is not checked out", false)]
    [InlineData("Already checked out", true)]
    [InlineData("This section has been checked out to alice since 2026-10-24 10:30:00", true)]
    public void UnlockedResponsesAreNotCheckoutConflicts(string message, bool locked)
        => Assert.Equal(locked, ObjectCheckoutRejectedException.DescribesLock(message));

    [Theory]
    [InlineData("Task")]
    [InlineData("Host")]
    [InlineData("Script")]
    [InlineData("DateList")]
    [InlineData("Globals")]
    [InlineData("Groups")]
    public async Task UnlockedHttpErrorsDoNotBecomeLockConflictsOrTriggerAnotherRequest(string type)
    {
        using var handler = new Handler("{\"errors\":[{\"message\":\"This section is not checked out\"}]}", HttpStatusCode.UnprocessableEntity);
        var error = await Assert.ThrowsAsync<MoveItApiException>(() => Acquire(Client(handler), type));
        Assert.False(ObjectCheckoutRejectedException.DescribesLock(error.Message));
        Assert.True(handler.Requests == 1);
    }

    [Theory]
    [InlineData("Task")]
    [InlineData("Host")]
    [InlineData("Script")]
    [InlineData("DateList")]
    [InlineData("Globals")]
    [InlineData("Groups")]
    public async Task NativeOwnerAndTimeSurviveAllCheckoutRejections(string type)
    {
        const string details = "This section has been checked out to alice since 2026-10-24 10:30:00";
        using var handler = new Handler("{\"status\":\"error\",\"errorcode\":4010,\"message\":\"" + details + "\",\"DefPassword\":\"private-password\"}");
        var error = await Assert.ThrowsAsync<ObjectCheckoutRejectedException>(() => Acquire(Client(handler), type));
        Assert.Equal(details.Replace("This section", "This object"), error.Message); Assert.DoesNotContain("private-password", error.Message);
        Assert.True(ObjectCheckoutRejectedException.DescribesLock(error.Message)); Assert.True(handler.Requests == 1);
    }
    [Theory]
    [InlineData("Task")]
    [InlineData("Host")]
    [InlineData("Script")]
    [InlineData("DateList")]
    [InlineData("Globals")]
    [InlineData("Groups")]
    public async Task HttpLockErrorsRetainNativeDetailsWithoutRawPayload(string type)
    {
        using var handler = new Handler("{\"errors\":[{\"message\":\"This section has been checked out to bob since 2026-10-24 11:00:00\"}],\"password\":\"private-password\"}", HttpStatusCode.UnprocessableEntity);
        var error = await Assert.ThrowsAsync<ObjectCheckoutRejectedException>(() => Acquire(Client(handler), type));
        Assert.Contains("bob since 2026-10-24 11:00:00", error.Message); Assert.DoesNotContain("private-password", error.Message); Assert.True(handler.Requests == 1);
        Assert.StartsWith("This object has been checked out to bob", error.Message); Assert.DoesNotContain("MOVEit REST request failed", error.Message);
    }
    [Theory]
    [InlineData("Task", "{\"status\":\"error\",\"errorcode\":4010}")]
    [InlineData("Host", "{\"Status\":\"ERROR\",\"ErrorCode\":\"4010\"}")]
    [InlineData("Script", "{\"errorcode\":4010}")]
    [InlineData("DateList", "{\"status\":\"error\"}")]
    public async Task SuccessfulHttpCannotConcealNativeCheckoutRejection(string type, string body)
    {
        using var handler = new Handler(body);
        var exception = await Assert.ThrowsAsync<ObjectCheckoutRejectedException>(() => Acquire(Client(handler), type));
        Assert.DoesNotContain("private-password", exception.Message);
        Assert.Equal(1, handler.Requests);
    }

    [Theory]
    [InlineData("Task", "")]
    [InlineData("Task", "{\"ID\":\"101\",\"Name\":\"Task\"}")]
    [InlineData("Task", "{\"status\":\"success\",\"errorcode\":0}")]
    [InlineData("Host", "{\"FTP\":{\"ID\":\"101\",\"DefPassword\":\"private-password\"}}")]
    [InlineData("Script", "{\"ID\":\"101\"}")]
    [InlineData("DateList", "{\"ID\":\"101\"}")]
    public async Task NativeDefinitionsAndExistingAcknowledgementShapesRemainAccepted(string type, string body)
    {
        using var handler = new Handler(body);
        await Acquire(Client(handler), type);
        Assert.Equal(1, handler.Requests);
    }

    private static async Task Acquire(MoveItRestClient client, string type)
    {
        if (type == "Task") await client.CheckoutTaskV0Async("101");
        else if (type == "Host") await client.CheckoutHostV0Async("101");
        else if (type == "Script") await client.CheckoutCustomScriptV0Async("101");
        else if (type == "Globals") await client.CheckoutGlobalsV0Async();
        else if (type == "Groups") await client.CheckoutResourceGroupsV0Async();
        else await client.CheckoutDateListV0Async("101");
    }
    private static MoveItRestClient Client(HttpMessageHandler handler)
    {
        var sessions = DispatchProxy.Create<IMoveItSessionService, HostSharedCrudTests.CrudProxy>();
        ((HostSharedCrudTests.CrudProxy)(object)sessions).Handler = (method, _) => method.Name == nameof(IMoveItSessionService.Get)
            ? new MoveItSessionState { WebAdminUrl = "https://checkout.test", AccessToken = "token", AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) }
            : throw new NotSupportedException(method.Name);
        return new(new Factory(handler), sessions, ConfigurationStartupState.Capture(new ConfigurationBuilder().Build()), null!, NullLogger<MoveItRestClient>.Instance);
    }
    private sealed class Handler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.EndsWith("/checkout", request.RequestUri!.AbsolutePath);
            Assert.Contains(request.Headers.Accept, header => header.MediaType == (request.RequestUri.AbsolutePath.Contains("/tasks/") ? "*/*" : "application/json"));
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, disposeHandler: false); }
}
