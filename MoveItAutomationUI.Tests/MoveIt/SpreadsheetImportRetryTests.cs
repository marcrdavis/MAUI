using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class SpreadsheetImportRetryTests
{
    [Theory]
    [InlineData("Task", "GET")] [InlineData("Task", "POST")] [InlineData("Task", "PUT")]
    [InlineData("Host", "GET")] [InlineData("Host", "POST")] [InlineData("Host", "PUT")]
    public async Task ImportRequestRetries500OnceWithTheSameMethodPayloadAndCredentials(string kind, string method)
    {
        using var handler = new Handler((_, count, _) => Task.FromResult(Reply(
            count == 1 ? HttpStatusCode.InternalServerError : HttpStatusCode.OK,
            count == 1 ? """{"message":"temporary"}""" : Success)));
        var client = Client(handler);
        using (MoveItImportRowRetryScope.Enter())
            await InvokeAsync(client, kind, method);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(handler.Requests[0], handler.Requests[1]);
        Assert.Equal("Bearer test-token", handler.Requests[1].Authorization);
        Assert.Equal(new HttpMethod(method), handler.Requests[1].Method);
        Assert.False(MoveItImportRowRetryScope.IsEnabled);
    }

    [Fact]
    public async Task Repeated500SurfacesTheSecondFailureAndNeverMakesAThirdAttempt()
    {
        using var handler = new Handler((_, count, _) => Task.FromResult(Reply(
            HttpStatusCode.InternalServerError, count == 1 ? """{"message":"first failure"}""" : """{"message":"second failure"}""")));
        using var scope = MoveItImportRowRetryScope.Enter();
        var error = await Assert.ThrowsAsync<MoveItApiException>(() => Client(handler).GetTaskAsync("123"));
        Assert.Equal(HttpStatusCode.InternalServerError, error.StatusCode);
        Assert.Contains("second failure", error.Message);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Theory]
    [InlineData(400)] [InlineData(403)] [InlineData(404)] [InlineData(408)]
    [InlineData(409)] [InlineData(429)] [InlineData(502)] [InlineData(503)]
    public async Task OtherHttpFailuresAreNotRetried(int status)
    {
        using var handler = new Handler((_, _, _) => Task.FromResult(Reply((HttpStatusCode)status, """{"message":"failure"}""")));
        using var scope = MoveItImportRowRetryScope.Enter();
        var error = await Assert.ThrowsAsync<MoveItApiException>(() => Client(handler).GetTaskAsync("123"));
        Assert.Equal((HttpStatusCode)status, error.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task DifferentFailureOnRetryIsReportedWithoutAnotherAttempt()
    {
        using var handler = new Handler((_, count, _) => Task.FromResult(Reply(
            count == 1 ? HttpStatusCode.InternalServerError : HttpStatusCode.Forbidden,
            count == 1 ? """{"message":"temporary"}""" : """{"message":"permission denied"}""")));
        using var scope = MoveItImportRowRetryScope.Enter();
        var error = await Assert.ThrowsAsync<MoveItApiException>(() => Client(handler).GetTaskAsync("123"));
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Contains("permission denied", error.Message);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task OrdinaryRequestsOutsideImportRetainTheirExistingNo500RetryBehavior()
    {
        using var handler = new Handler((_, _, _) => Task.FromResult(Reply(HttpStatusCode.InternalServerError, "{}")));
        await Assert.ThrowsAsync<MoveItApiException>(() => Client(handler).GetTaskAsync("123"));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CancellationAfter500StopsBeforeTheRetryAndDisposesTheFirstResponse()
    {
        using var cancellation = new CancellationTokenSource();
        var content = new TrackedContent("{}");
        using var handler = new Handler((_, _, _) =>
        {
            cancellation.Cancel();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = content });
        });
        using var scope = MoveItImportRowRetryScope.Enter();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(handler).GetTaskAsync("123", cancellation.Token));
        Assert.Single(handler.Requests);
        Assert.True(content.Disposed);
    }

    [Fact]
    public async Task ATransportFailureWithoutAnHttp500ResponseIsNotRetried()
    {
        using var handler = new Handler((_, _, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("connection lost")));
        using var scope = MoveItImportRowRetryScope.Enter();
        await Assert.ThrowsAsync<HttpRequestException>(() => Client(handler).GetTaskAsync("123"));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task NestedImportScopesRestoreTheirPreviousStateAcrossAwaits()
    {
        Assert.False(MoveItImportRowRetryScope.IsEnabled);
        using (MoveItImportRowRetryScope.Enter())
        {
            await Task.Yield();
            Assert.True(MoveItImportRowRetryScope.IsEnabled);
            using (MoveItImportRowRetryScope.Enter())
            {
                await Task.Yield();
                Assert.True(MoveItImportRowRetryScope.IsEnabled);
            }
            Assert.True(MoveItImportRowRetryScope.IsEnabled);
        }
        Assert.False(MoveItImportRowRetryScope.IsEnabled);
    }

    [Fact]
    public async Task FailedReadbackRetriesTheReadWithoutRepeatingAnAcceptedCreate()
    {
        using var handler = new Handler((request, _, _) => Task.FromResult(Reply(
            request.Method == HttpMethod.Post ? HttpStatusCode.OK : HttpStatusCode.InternalServerError,
            request.Method == HttpMethod.Post ? Success : """{"message":"readback unavailable"}""")));
        var service = new TraditionalTaskPersistenceService(Client(handler), null!,
            new TraditionalTaskDefinitionComposer(new TraditionalTaskHostProfileCatalog()));
        using var scope = MoveItImportRowRetryScope.Enter();
        await Assert.ThrowsAsync<TraditionalTaskCreationUnverifiedException>(() => service.CreateResolvedDefinitionAsync(
            new JsonObject { ["Name"] = "Imported" }, requireReadback: true));
        Assert.Single(handler.Requests, request => request.Method == HttpMethod.Post);
        Assert.Equal(2, handler.Requests.Count(request => request.Method == HttpMethod.Get));
    }

    private const string Success = """{"ID":"123","Name":"Imported","TT":"T","id":"123","status":"success","errorcode":0}""";

    private static async Task InvokeAsync(MoveItRestClient client, string kind, string method)
    {
        var task = new JsonObject { ["Name"] = "Imported", ["ID"] = "123", ["TT"] = "T" };
        var host = new JsonObject { ["FTP"] = new JsonObject { ["Name"] = "Imported", ["ID"] = "123" } };
        if (kind == "Task")
        {
            if (method == "GET") await client.GetTaskAsync("123");
            else if (method == "POST") await client.CreateTaskDefinitionAsync(task);
            else await client.UpdateTaskAsync("123", task);
        }
        else
        {
            if (method == "GET") await client.GetHostAsync("123");
            else if (method == "POST") await client.CreateHostV0Async(host);
            else await client.UpdateHostV0Async("123", host);
        }
    }

    private static MoveItRestClient Client(HttpMessageHandler handler)
    {
        var sessions = DispatchProxy.Create<IMoveItSessionService, SessionProxy>();
        return new MoveItRestClient(new ClientFactory(handler), sessions,
            ConfigurationStartupState.Capture(new ConfigurationBuilder().Build()), null!, NullLogger<MoveItRestClient>.Instance);
    }

    public class SessionProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name == nameof(IMoveItSessionService.Get)
            ? new MoveItSessionState { WebAdminUrl = "https://moveit.test", AccessToken = "test-token", AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) }
            : throw new NotSupportedException(method.Name);
    }

    private sealed record RequestSnapshot(HttpMethod Method, string Path, string Body, string? Authorization);
    private sealed class Handler(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        internal List<RequestSnapshot> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new(request.Method, request.RequestUri!.AbsolutePath, body, request.Headers.Authorization?.ToString()));
            return await reply(request, Requests.Count, cancellationToken);
        }
    }
    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
    private sealed class TrackedContent(string value) : StringContent(value, Encoding.UTF8, "application/json")
    {
        internal bool Disposed { get; private set; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
    private static HttpResponseMessage Reply(HttpStatusCode status, string body) => new(status)
    { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
