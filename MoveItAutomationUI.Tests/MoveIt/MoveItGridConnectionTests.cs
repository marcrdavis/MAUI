using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class MoveItGridConnectionTests
{
    [Theory]
    [InlineData("Tasks", "UpcomingRunsGrid")]
    [InlineData("Tasks", "InitialGrid")]
    [InlineData("Tasks", "RefreshGrid")]
    [InlineData("Hosts", "InitialGrid")]
    [InlineData("Hosts", "RefreshGrid")]
    [InlineData("Resources", "InitialScriptsGrid")]
    [InlineData("Resources", "RefreshScriptsGrid")]
    [InlineData("Resources", "InitialDateListsGrid")]
    [InlineData("Resources", "RefreshDateListsGrid")]
    [InlineData("Resources", "InitialGlobalParametersGrid")]
    [InlineData("Resources", "RefreshGlobalParametersGrid")]
    [InlineData("Resources", "InitialKeysAndCertificatesGrid")]
    [InlineData("Resources", "RefreshKeysAndCertificatesGrid")]
    [InlineData("Resources", "ResourceGroupPermissions")]
    [InlineData("Records", "TaskRunBatch")]
    [InlineData("Records", "FileActivityBatch")]
    [InlineData("Records", "AuditBatch")]
    public void OnlyExplicitReadOnlyGridRoutesParticipate(string controller, string action)
    {
        var context = Context(controller, action);
        context.Request.PathBase = "/MAUI";
        Assert.True(MoveItGridConnectionMiddleware.IsGridRead(context.Request));
        context.Request.Method = "POST";
        Assert.False(MoveItGridConnectionMiddleware.IsGridRead(context.Request));
    }

    [Theory]
    [InlineData("Tasks", "SaveDetails")]
    [InlineData("HostEditor", "Definition")]
    [InlineData("TraditionalTaskEditor", "Save")]
    [InlineData("ObjectEditLease", "Heartbeat")]
    [InlineData("Resources", "ResourceGroupEditor")]
    [InlineData("Reports", "Index")]
    [InlineData("Account", "Login")]
    public void EditorsWritesAndLocalReportsKeepTheirExistingBehavior(string controller, string action)
        => Assert.False(MoveItGridConnectionMiddleware.IsGridRead(Context(controller, action).Request));

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError, "unreachable")]
    [InlineData(HttpRequestError.ConnectionError, "unreachable")]
    [InlineData(HttpRequestError.SecureConnectionError, null)]
    [InlineData(HttpRequestError.HttpProtocolError, null)]
    [InlineData(HttpRequestError.InvalidResponse, null)]
    public void TransportEvidenceIsDistinctFromTlsAndProtocolErrors(HttpRequestError error, string? expected)
        => Assert.Equal(expected, MoveItGridConnectionObservation.Classify(new HttpRequestException(error, "Failure")));

    [Theory]
    [InlineData(SocketError.HostNotFound, "unreachable")]
    [InlineData(SocketError.ConnectionRefused, "unreachable")]
    [InlineData(SocketError.NetworkUnreachable, "unreachable")]
    [InlineData(SocketError.ConnectionReset, "unreachable")]
    [InlineData(SocketError.TimedOut, "timeout")]
    [InlineData(SocketError.AccessDenied, null)]
    public void WrappedSocketFailuresAreClassifiedWithoutParsingMessages(SocketError error, string? expected)
        => Assert.Equal(expected, MoveItGridConnectionObservation.Classify(
            new HttpRequestException("Failure", new SocketException((int)error))));

    [Fact]
    public void SocketTimeoutInsideConnectionErrorStillRequiresTwoTimeouts()
        => Assert.Equal("timeout", MoveItGridConnectionObservation.Classify(
            new HttpRequestException(HttpRequestError.ConnectionError, "timeout", new SocketException((int)SocketError.TimedOut))));

    [Fact]
    public async Task UnhandledDnsFailureBecomesJson503AndOneTransportHeader()
    {
        var result = await Run(async invoker => await Send(invoker),
            _ => throw new HttpRequestException(HttpRequestError.NameResolutionError, "private server name"));
        Assert.Equal(503, result.Response.StatusCode);
        Assert.Equal("unreachable", result.Response.Headers[MoveItGridConnectionMiddleware.HeaderName].ToString());
        result.Response.Body.Position = 0;
        var body = await new StreamReader(result.Response.Body).ReadToEndAsync();
        Assert.Contains("MAUI cannot connect to MOVEit.", body);
        Assert.DoesNotContain("private server name", body);
    }

    [Fact]
    public async Task ControllerHandledFailuresStillCarryTransportEvidence()
    {
        var result = await Run(async invoker =>
        {
            try { await Send(invoker); }
            catch (HttpRequestException) { }
        }, _ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused"));
        Assert.Equal("unreachable", result.Response.Headers[MoveItGridConnectionMiddleware.HeaderName].ToString());
    }

    [Fact]
    public async Task SeveralUpstreamTimeoutsInOneGridRequestProduceOneTimeoutOutcome()
    {
        var result = await Run(async invoker =>
        {
            foreach (var path in new[] { "tasks", "status" })
                try { await Send(invoker, path); } catch (OperationCanceledException) { }
        }, _ => throw new TaskCanceledException("timeout", new TimeoutException()));
        Assert.Equal("timeout", result.Response.Headers[MoveItGridConnectionMiddleware.HeaderName].ToString());
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(500)]
    public async Task HttpErrorsDoNotMeanUnreachable(int code)
    {
        var result = await Run(async invoker => await Send(invoker),
            _ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)code)));
        Assert.Equal("responded", result.Response.Headers[MoveItGridConnectionMiddleware.HeaderName].ToString());
        Assert.Null(MoveItGridConnectionObservation.Classify(new HttpRequestException("HTTP error", null, (HttpStatusCode)code)));
    }

    [Fact]
    public async Task CachedRequestCannotClaimRecovery()
    {
        var result = await Run(_ => Task.CompletedTask, _ => throw new InvalidOperationException("must not send"));
        Assert.Equal("none", result.Response.Headers[MoveItGridConnectionMiddleware.HeaderName].ToString());
    }

    [Fact]
    public async Task LiveReadAndSuccessfulRetryCanConfirmRecovery()
    {
        var calls = 0;
        var result = await Run(async invoker =>
        {
            try { await Send(invoker); } catch (HttpRequestException) { }
            await Send(invoker);
        }, _ => ++calls == 1 ? throw new HttpRequestException(HttpRequestError.ConnectionError, "refused")
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        Assert.Equal("live", result.Response.Headers[MoveItGridConnectionMiddleware.HeaderName].ToString());
    }

    [Fact]
    public async Task TokenRenewalAloneDoesNotConfirmGridRecovery()
    {
        var result = await Run(async invoker =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://moveit.test/api/v1/token");
            using var response = await invoker.SendAsync(request, CancellationToken.None);
        }, _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        Assert.Equal("none", result.Response.Headers[MoveItGridConnectionMiddleware.HeaderName].ToString());
    }

    [Fact]
    public async Task ActualHttpClientTimeoutIsObservedInsideItsLinkedSendToken()
    {
        var context = Context("Tasks", "RefreshGrid");
        var accessor = new HttpContextAccessor { HttpContext = context };
        using var handler = new MoveItGridConnectionHandler(accessor) { InnerHandler = new WaitForCancellation() };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(50) };
        await new MoveItGridConnectionMiddleware(async _ =>
        {
            using var response = await client.GetAsync("https://moveit.test/api/v1/tasks");
        }).InvokeAsync(context);
        Assert.Equal(503, context.Response.StatusCode);
        Assert.Equal("timeout", ((MoveItGridConnectionObservation)context.Items[MoveItGridConnectionMiddleware.ObservationKey]!).Outcome);
        Assert.False(context.RequestAborted.IsCancellationRequested);
    }

    [Theory]
    [InlineData("/api/v1/reports/taskruns")]
    [InlineData("/api/v1/reports/fileactivity")]
    [InlineData("/api/v1/reports/audit")]
    [InlineData("/api/v0/reports/fileactivity")]
    public async Task ReadOnlyReportPostsCanConfirmRecovery(string path)
    {
        var context = Context("Records", "TaskRunBatch");
        var result = await Run(async invoker =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://moveit.test" + path);
            using var response = await invoker.SendAsync(request, CancellationToken.None);
            await MoveItGridConnectionHandler.ReadBodyAsync(response, CancellationToken.None);
        }, _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }), context);
        Assert.Equal("live", result.Response.Headers[MoveItGridConnectionMiddleware.HeaderName].ToString());
    }

    [Fact]
    public async Task CancelledBodyReadDoesNotRaiseAnOutage()
    {
        using var cancellation = new CancellationTokenSource();
        var context = Context("Resources", "RefreshKeysAndCertificatesGrid");
        context.RequestAborted = cancellation.Token;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(async invoker =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://moveit.test/api/v1/sshkeys");
            using var response = await invoker.SendAsync(request, CancellationToken.None);
            cancellation.Cancel();
            await MoveItGridConnectionHandler.ReadBodyAsync(response, cancellation.Token);
        }, _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new CancelledContent() }), context));
        Assert.False(((MoveItGridConnectionObservation)context.Items[MoveItGridConnectionMiddleware.ObservationKey]!).HasTransportFailure);
    }

    [Fact]
    public async Task BrowserCancellationDoesNotRaiseTimeoutOrConsumeTheError()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var context = Context("Tasks", "RefreshGrid");
        context.RequestAborted = cancelled.Token;
        await Assert.ThrowsAsync<TaskCanceledException>(() => Run(async invoker => await Send(invoker),
            _ => throw new TaskCanceledException(), context));
        var observation = (MoveItGridConnectionObservation)context.Items[MoveItGridConnectionMiddleware.ObservationKey]!;
        Assert.Equal("none", observation.Outcome);
    }

    [Fact]
    public async Task CachedHandlerDoesNotLeakOutcomesBetweenRequests()
    {
        var accessor = new HttpContextAccessor();
        using var handler = new MoveItGridConnectionHandler(accessor) { InnerHandler = new Stub(_ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused")) };
        using var invoker = new HttpMessageInvoker(handler);
        var first = Context("Tasks", "RefreshGrid");
        accessor.HttpContext = first;
        await new MoveItGridConnectionMiddleware(async _ => await Send(invoker)).InvokeAsync(first);
        var second = Context("Hosts", "RefreshGrid");
        accessor.HttpContext = second;
        await new MoveItGridConnectionMiddleware(_ => Task.CompletedTask).InvokeAsync(second);
        Assert.Equal("unreachable", ((MoveItGridConnectionObservation)first.Items[MoveItGridConnectionMiddleware.ObservationKey]!).Outcome);
        Assert.Equal("none", ((MoveItGridConnectionObservation)second.Items[MoveItGridConnectionMiddleware.ObservationKey]!).Outcome);
    }

    private static DefaultHttpContext Context(string controller, string action)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.RouteValues["controller"] = controller;
        context.Request.RouteValues["action"] = action;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task Send(HttpMessageInvoker invoker, string path = "tasks")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://moveit.test/api/v1/{path}");
        using var response = await invoker.SendAsync(request, CancellationToken.None);
    }

    private static async Task<DefaultHttpContext> Run(Func<HttpMessageInvoker, Task> action,
        Func<HttpRequestMessage, Task<HttpResponseMessage>> send, DefaultHttpContext? context = null)
    {
        context ??= Context("Tasks", "RefreshGrid");
        var feature = new ResponseFeature { Body = context.Response.Body };
        context.Features.Set<IHttpResponseFeature>(feature);
        var accessor = new HttpContextAccessor { HttpContext = context };
        using var handler = new MoveItGridConnectionHandler(accessor) { InnerHandler = new Stub(send) };
        using var invoker = new HttpMessageInvoker(handler);
        await new MoveItGridConnectionMiddleware(_ => action(invoker)).InvokeAsync(context);
        await feature.FireStarting();
        return context;
    }

    private sealed class CancelledContent : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => Task.FromException(new TaskCanceledException());
    }

    private sealed class WaitForCancellation : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The linked send token should have been cancelled.");
        }
    }

    private sealed class Stub(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }

    // DefaultHttpContext does not run response-start callbacks; this feature exercises the
    // production OnStarting header contract without adding an integration-server dependency.
    private sealed class ResponseFeature : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> callbacks = new();
        public override void OnStarting(Func<object, Task> callback, object state) => callbacks.Add((callback, state));
        internal async Task FireStarting()
        {
            foreach (var entry in callbacks.AsEnumerable().Reverse()) await entry.Callback(entry.State);
        }
    }
}
