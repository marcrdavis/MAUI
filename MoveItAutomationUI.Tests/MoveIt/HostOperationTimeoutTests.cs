using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Hosts;
using MoveItAutomationUI.Services.MoveIt.Tasks;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class HostOperationTimeoutTests
{
    [Fact]
    public void NativeTimeoutsAndRetriesExtendTheDiagnosticBudgetWithinBounds()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), HostOperationTimeout.ForRequest(new JsonObject()));
        var host = new JsonObject { ["DefConnTimeoutSecs"] = 200, ["DefDataTimeoutSecs"] = 180,
            ["DefRetryCount"] = 1, ["DefRetryTimeoutSecs"] = 30 };
        Assert.Equal(TimeSpan.FromSeconds(820), HostOperationTimeout.ForRequest(host));
        host["DefConnTimeoutSecs"] = long.MaxValue;
        host["DefRetryCount"] = long.MaxValue;
        Assert.Equal(TimeSpan.FromMinutes(30), HostOperationTimeout.ForRequest(host));
        Assert.Equal(TimeSpan.FromSeconds(430), HostOperationTimeout.ForRequest(new JsonObject
            { ["logininfo"] = new JsonObject { ["conntimeoutsecs"] = "200", ["datatimeoutsecs"] = 200 } }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupBudgetTracksLimitsEnrichedAfterSparseCheckoutEvenWhenTheReadFails(bool fail)
    {
        HostOperationTimeout.Scope? captured = null;
        using var server = ObjectCheckout.ForServer("https://sparse-" + Guid.NewGuid().ToString("N") + ".test");
        var releases = 0;
        var rest = Stub<IMoveItRestClient>((method, _) => method.Name switch
        {
            nameof(IMoveItRestClient.CheckoutHostV0Async) => Task.FromResult(JsonNode.Parse("""{"FTP":{"ID":"42","Name":"Sparse"}}""")!.AsObject()),
            nameof(IMoveItRestClient.ReleaseHostCheckoutV0Async) => Release(),
            _ => throw new NotSupportedException(method.Name)
        });
        Task Release() { releases++; return Task.CompletedTask; }
        async Task<int> Read(JsonObject _, CancellationToken ct)
        {
            // The REST adapter receives effective fields after v1 enrichment.
            await Task.Yield();
            var duration = HostOperationTimeout.ForRequest(new JsonObject
                { ["logininfo"] = new JsonObject { ["conntimeoutsecs"] = 600, ["datatimeoutsecs"] = 300 } });
            Assert.Equal(TimeSpan.FromSeconds(930), duration);
            if (fail) throw new HttpRequestException("Simulated native failure after enrichment");
            return 1;
        }
        // Observe the same scope object from inside the diagnostic without retaining native data.
        Task<int> Capture(JsonObject host, CancellationToken ct)
        {
            captured = HostOperationTimeout.ActiveScope;
            return Read(host, ct);
        }
        if (fail) await Assert.ThrowsAsync<HttpRequestException>(() => HostBrowseCheckout.ReadAsync(rest, "42", Capture, default));
        else await HostBrowseCheckout.ReadAsync(rest, "42", Capture, default);
        Assert.NotNull(captured); Assert.Equal(TimeSpan.FromSeconds(930), captured.Timeout);
        Assert.True(releases == 1);
        Assert.Equal(HostOperationTimeout.Minimum, HostOperationTimeout.ForRequest(new JsonObject()));
    }

    [Theory]
    [InlineData(false, "pass")]
    [InlineData(true, "pass")]
    [InlineData(false, "timeout")]
    [InlineData(true, "timeout")]
    [InlineData(false, "network")]
    [InlineData(true, "network")]
    public async Task HostAndPreflightPathTestsReturnResultsAndReleaseTheirCheckout(bool path, string outcome)
    {
        using var f = new Fixture(outcome);
        using var server = ObjectCheckout.ForServer(f.Session.WebAdminUrl);
        var tests = new HostConnectionTestService(f.Rest, NullLogger<HostConnectionTestService>.Instance);
        var result = path ? await tests.TestHostPathAsync("42", "/", "*.txt") : await tests.TestHostAsync("42");
        Assert.Equal(outcome == "pass" ? HostConnectionTestService.StatusPassed : HostConnectionTestService.StatusFailed, result.Status);
        if (outcome == "timeout") Assert.Contains("timed out", result.Message);
        Assert.Equal(new[] { "checkout", "browse", "release" }, f.Handler.Lifecycle);
        Assert.True(f.Handler.Releases == 1);
        Assert.Equal(TimeSpan.FromMinutes(5), f.Handler.BrowseTimeout);
        Assert.Equal(HostOperationTimeout.Maximum, f.Handler.ReleaseTimeout);
        Assert.False(f.Handler.ReleaseWasCancelled);
    }

    [Fact]
    public async Task SavedRetrySettingsStillApplyWhenTheNativeBrowseEnvelopeOmitsThem()
    {
        using var f = new Fixture("pass");
        f.Handler.Host["DefConnTimeoutSecs"] = 200; f.Handler.Host["DefDataTimeoutSecs"] = 180;
        f.Handler.Host["DefRetryCount"] = 1; f.Handler.Host["DefRetryTimeoutSecs"] = 30;
        using var server = ObjectCheckout.ForServer(f.Session.WebAdminUrl);
        var result = await new HostConnectionTestService(f.Rest, NullLogger<HostConnectionTestService>.Instance).TestHostAsync("42");
        Assert.Equal(HostConnectionTestService.StatusPassed, result.Status);
        Assert.Equal(TimeSpan.FromSeconds(820), f.Handler.BrowseTimeout);
        Assert.Equal(TimeSpan.FromMinutes(5), HostOperationTimeout.ForRequest(new JsonObject()));
    }

    [Fact]
    public async Task HostFailureStaysInThePreflightResultInsteadOfEscapingAsAnHttpError()
    {
        using var f = new Fixture("timeout");
        using var server = ObjectCheckout.ForServer(f.Session.WebAdminUrl);
        var tests = new HostConnectionTestService(f.Rest, NullLogger<HostConnectionTestService>.Instance);
        var crud = Stub<ITaskCrudCoordinator>((method, _) => method.Name == nameof(ITaskCrudCoordinator.GetDefinitionAsync)
            ? Task.FromResult(JsonNode.Parse("""{"ID":"100","Name":"Slow task","steps":[{"Source":{"ID":"11","HostID":"42","Type":"FTP","Path":"/","FileMask":"*.*"}}]}""")!.AsObject())
            : throw new NotSupportedException(method.Name));
        var hosts = Stub<IAutomationHostService>((method, _) => method.Name == nameof(IAutomationHostService.GetHostsAsync)
            ? Task.FromResult<IReadOnlyList<AutomationHost>>(new[] { new AutomationHost { Id = "42", Name = "Slow Host", TypeCode = "FTP" } })
            : throw new NotSupportedException(method.Name));
        var preflight = new TaskPreflightService(f.Rest, crud, null!, hosts, tests, null!, null!, null!, null!);
        var result = await preflight.RunStageAsync("100", TaskPreflightStages.Hosts);
        Assert.Equal(TaskPreflightStatuses.Fail, result.Status);
        Assert.Contains(result.Checks, check => check.Status == TaskPreflightStatuses.Fail && check.Message.Contains("timed out"));
        Assert.True(f.Handler.Releases == 1);
        Assert.True(f.Handler.Lifecycle.Count(item => item == "browse") == 1); // Failed connection skips path browsing.
    }

    [Fact]
    public async Task CancelledHostReadStillAllowsCleanupBeyondTheOldFifteenSecondLimit()
    {
        // This deliberately crosses the old production cleanup deadline; no live server is used.
        using var f = new Fixture("cancel") { SlowRelease = true };
        using var server = ObjectCheckout.ForServer(f.Session.WebAdminUrl);
        var tests = new HostConnectionTestService(f.Rest, NullLogger<HostConnectionTestService>.Instance);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tests.TestHostAsync("42", f.Caller.Token));
        Assert.True(f.Handler.Releases == 1);
        Assert.False(f.Handler.ReleaseWasCancelled);
        Assert.Equal(new[] { "checkout", "browse", "release" }, f.Handler.Lifecycle);
    }

    [Fact]
    public async Task RejectedCheckoutDoesNotReleaseAnotherEditorsLock()
    {
        using var f = new Fixture("rejected");
        using var server = ObjectCheckout.ForServer(f.Session.WebAdminUrl);
        var result = await new HostConnectionTestService(f.Rest, NullLogger<HostConnectionTestService>.Instance).TestHostAsync("42");
        Assert.Equal(HostConnectionTestService.StatusFailed, result.Status);
        Assert.True(f.Handler.Releases == 0);
        Assert.Equal(new[] { "checkout" }, f.Handler.Lifecycle);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CheckoutTakenByAnotherEditorDuringTheTestIsNotReleased(bool path)
    {
        using var f = new Fixture("ownership-lost");
        using var server = ObjectCheckout.ForServer(f.Session.WebAdminUrl);
        var tests = new HostConnectionTestService(f.Rest, NullLogger<HostConnectionTestService>.Instance);
        var result = path ? await tests.TestHostPathAsync("42", "/") : await tests.TestHostAsync("42");
        Assert.Equal(HostConnectionTestService.StatusFailed, result.Status);
        Assert.Contains("another-user", result.Message);
        Assert.True(f.Handler.Releases == 0);
        Assert.Equal(new[] { "checkout", "browse" }, f.Handler.Lifecycle);
    }

    [Fact]
    public async Task RealReleaseFailureIsReportedWithoutRepeatingTheDiagnosticOrDeletingAgain()
    {
        using var f = new Fixture("release-failed");
        using var server = ObjectCheckout.ForServer(f.Session.WebAdminUrl);
        var result = await new HostConnectionTestService(f.Rest, NullLogger<HostConnectionTestService>.Instance).TestHostAsync("42");
        Assert.Equal(HostConnectionTestService.StatusFailed, result.Status);
        Assert.Contains("could not release", result.Message);
        Assert.True(f.Handler.Releases == 1);
        Assert.Equal(new[] { "checkout", "browse", "release" }, f.Handler.Lifecycle);
    }

    private sealed class Fixture : IDisposable
    {
        public readonly CancellationTokenSource Caller = new();
        public MoveItSessionState Session { get; } = new() { WebAdminUrl = "https://slow-host-" + Guid.NewGuid().ToString("N") + ".test",
            AccessToken = "token", AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddHours(1), Username = "tester" };
        public Handler Handler { get; }
        public MoveItRestClient Rest { get; }
        public bool SlowRelease { init => Handler.SlowRelease = value; }
        public Fixture(string outcome)
        {
            Handler = new Handler(outcome, Caller);
            var factory = new Factory(Handler); Handler.Factory = factory;
            var sessions = Stub<IMoveItSessionService>((method, _) => method.Name == nameof(IMoveItSessionService.Get) ? Session : throw new NotSupportedException(method.Name));
            Rest = new(factory, sessions, ConfigurationStartupState.Capture(new ConfigurationBuilder().Build()), null!, NullLogger<MoveItRestClient>.Instance);
        }
        public void Dispose() { Caller.Dispose(); Handler.Dispose(); }
    }

    private sealed class Handler(string outcome, CancellationTokenSource caller) : HttpMessageHandler
    {
        public Factory Factory = null!;
        public readonly List<string> Lifecycle = [];
        public int Releases;
        public bool SlowRelease, ReleaseWasCancelled;
        public TimeSpan BrowseTimeout, ReleaseTimeout;
        public JsonObject Host { get; } = JsonNode.Parse("""{"ID":"42","Name":"Slow Host","Host":"ftp.test","Port":21,"DefUsername":"user","DefConnTimeoutSecs":120,"DefDataTimeoutSecs":120,"Secure":"none","ProxyType":"None"}""")!.AsObject();
        private JsonObject Definition() => new() { ["FTP"] = Host.DeepClone() };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path.EndsWith("/checkout"))
            {
                Lifecycle.Add("checkout");
                if (outcome == "rejected") return Reply("""{"status":"error","errorcode":4010,"message":"This section has been checked out to another-user since 2026-10-27 09:00:00"}""");
                return Reply(Definition().ToJsonString());
            }
            if (path.EndsWith("/ftp/browse"))
            {
                Lifecycle.Add("browse"); BrowseTimeout = Factory.Clients[^1].Timeout;
                if (outcome == "timeout") throw new TaskCanceledException("Simulated HTTP timeout");
                if (outcome == "network") throw new HttpRequestException("Connection lost");
                if (outcome == "ownership-lost") return new(HttpStatusCode.Conflict)
                {
                    Content = new StringContent("""{"errors":[{"message":"This object has been checked out to another-user since 2026-10-27 09:00:00"}]}""", System.Text.Encoding.UTF8, "application/json")
                };
                if (outcome == "cancel") { caller.Cancel(); throw new OperationCanceledException(caller.Token); }
                return Reply("""{"CurrentDir":"/","Dirs":{"Dir":[]},"Files":{"File":[{"Name":"file.txt"}]}}""");
            }
            if (request.Method == HttpMethod.Delete && path.EndsWith("/checkout"))
            {
                Lifecycle.Add("release"); Releases++; ReleaseTimeout = Factory.Clients[^1].Timeout;
                ReleaseWasCancelled = ct.IsCancellationRequested;
                Assert.False(ReleaseWasCancelled);
                if (SlowRelease) await Task.Delay(TimeSpan.FromSeconds(16), ct);
                if (outcome == "release-failed") throw new HttpRequestException("MOVEit is unavailable during cleanup");
                return Reply("""{"status":"success","errorcode":0}""");
            }
            if (request.Method == HttpMethod.Get && path == "/api/v1/hosts/42") return Reply(Definition().ToJsonString());
            throw new InvalidOperationException("Unexpected REST request: " + request.Method + " " + path);
        }
        private static HttpResponseMessage Reply(string json) => new(HttpStatusCode.OK)
            { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
    }
    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public List<HttpClient> Clients { get; } = [];
        public HttpClient CreateClient(string name) { var client = new HttpClient(handler, disposeHandler: false); Clients.Add(client); return client; }
    }
    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, HostSharedCrudTests.CrudProxy>();
        ((HostSharedCrudTests.CrudProxy)(object)proxy).Handler = handler; return proxy;
    }
}
