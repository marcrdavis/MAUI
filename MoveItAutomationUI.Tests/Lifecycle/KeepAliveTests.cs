using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.Lifecycle;

namespace MoveItAutomationUI.Tests.Lifecycle;

public sealed class KeepAliveTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(20, 20)]
    [InlineData(0, 5)]
    [InlineData(21, 5)]
    public void DefaultsAndSafeIntervals(int input, int expected)
    {
        var options = new KeepAliveOptions();
        Assert.True(options.Enabled.Value);
        options.IntervalMinutes.Value = input;
        Assert.Equal(TimeSpan.FromMinutes(expected), options.GetInterval());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    [InlineData(-1)]
    public async Task SettingsSaveRejectsOutOfRangeIntervals(int interval)
    {
        var validator = new ConfigurationSettingsValidator(null!);
        var error = await Assert.ThrowsAsync<ArgumentException>(() => validator.ValidateAndNormalizeAsync(
            new ConfigurationSettingsSaveRequest { KeepAliveIntervalMinutes = interval }, new ConfigurationBuilder().Build()));
        Assert.Equal("Ping Interval must be between 1 and 20 minutes.", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/MAUI")]
    public void ObservedAddressRetainsApplicationPathButPinsActualLocalPort(string pathBase)
    {
        var target = new KeepAliveTarget();
        var context = Context(pathBase);
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("untrusted.example", 6553);
        context.Connection.LocalPort = 443;
        target.Observe(context);
        Assert.NotNull(target.Address);
        Assert.Equal(IPAddress.Loopback, target.Address!.LocalAddress);
        Assert.Equal(443, target.Address.LocalPort);
        Assert.Equal("https://untrusted.example" + pathBase + KeepAliveTarget.Path, target.Address.Url.AbsoluteUri);
        Assert.False(target.Accepts(null));
        Assert.False(target.Accepts("wrong"));
        Assert.True(target.Accepts(target.Token));
    }

    [Fact]
    public void MissingOrUnspecifiedLocalConnectionCannotBecomeTarget()
    {
        var target = new KeepAliveTarget();
        target.Observe(new DefaultHttpContext());
        var context = Context("");
        context.Connection.LocalIpAddress = IPAddress.Any;
        target.Observe(context);
        Assert.Null(target.Address);
    }

    [Fact]
    public void MappedIpv4ConnectionUsesAnIpv4SocketTarget()
    {
        var context = Context("");
        context.Connection.LocalIpAddress = IPAddress.Parse("::ffff:127.0.0.1");
        var target = new KeepAliveTarget();
        target.Observe(context);
        Assert.Equal(IPAddress.Loopback, target.Address!.LocalAddress);
    }

    [Theory]
    [InlineData(false, "GET", 404)]
    [InlineData(true, "POST", 404)]
    [InlineData(true, "GET", 204)]
    public async Task PrivateEndpointNeverReachesAuthenticationOrDependencies(bool validToken, string method, int status)
    {
        var target = new KeepAliveTarget();
        var context = Context("");
        context.Request.Path = KeepAliveTarget.Path;
        context.Request.Method = method;
        if (validToken) context.Request.Headers[KeepAliveTarget.Header] = target.Token;
        var nextCalled = false;
        var middleware = new KeepAliveMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        await middleware.InvokeAsync(context, target);
        Assert.Equal(status, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.False(nextCalled);
        Assert.Null(target.Address);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/MAUI")]
    public async Task RealHttpPingUsesLocalEndpointWithoutDnsAndPreservesPathBase(string pathBase)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<KeepAliveTarget>();
        await using var app = builder.Build();
        if (pathBase.Length > 0) app.UsePathBase(pathBase);
        app.UseMiddleware<KeepAliveMiddleware>();
        app.Run(context => { context.Response.StatusCode = 418; return Task.CompletedTask; });
        await app.StartAsync();
        try
        {
            var uri = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
            var target = app.Services.GetRequiredService<KeepAliveTarget>();
            var observed = Context(pathBase);
            observed.Connection.LocalPort = uri.Port;
            // This name must never be resolved: ConnectCallback pins the socket to the local listener.
            observed.Request.Host = new HostString("not-a-real-host.invalid", 9);
            target.Observe(observed);
            await new KeepAlivePinger().PingAsync(target.Address!, target.Token, CancellationToken.None);
            var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
                new KeepAlivePinger().PingAsync(target.Address!, "incorrect-token", CancellationToken.None));
            Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
        }
        finally { await app.StopAsync(); }
    }

    [Fact]
    public async Task LiveEnableDisableAndIntervalChangesWakeWorkerWithoutRestart()
    {
        var options = new OptionsMonitor();
        options.Value.Enabled.Value = false;
        var target = new KeepAliveTarget();
        target.Observe(Context(""));
        var pinger = new Pinger();
        using var worker = new KeepAliveService(options, target, pinger, NullLogger<KeepAliveService>.Instance);
        await worker.StartAsync(CancellationToken.None);
        Assert.Equal(0, pinger.Calls);
        options.Change(true, 5);
        await pinger.First.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, pinger.Calls);
        options.Change(false, 1);
        await worker.StopAsync(CancellationToken.None);
        Assert.Equal(1, pinger.Calls);
    }

    [Fact]
    public async Task ShutdownCancelsAnInFlightPing()
    {
        var options = new OptionsMonitor();
        var target = new KeepAliveTarget();
        target.Observe(Context(""));
        var pinger = new Pinger { WaitForCancellation = true };
        using var worker = new KeepAliveService(options, target, pinger, NullLogger<KeepAliveService>.Instance);
        await worker.StartAsync(CancellationToken.None);
        await pinger.First.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);
        Assert.True(pinger.Cancelled);
    }

    [Fact]
    public async Task FailedPingDoesNotStopWorkerAndAConfigurationChangeCanRetry()
    {
        var options = new OptionsMonitor();
        var target = new KeepAliveTarget();
        target.Observe(Context(""));
        var pinger = new Pinger { FailFirst = true };
        using var worker = new KeepAliveService(options, target, pinger, NullLogger<KeepAliveService>.Instance);
        await worker.StartAsync(CancellationToken.None);
        await pinger.First.Task.WaitAsync(TimeSpan.FromSeconds(5));
        options.Change(true, 5);
        await pinger.Second.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);
        Assert.Equal(2, pinger.Calls);
    }

    [Fact]
    public void SettingsSavePersistsKeepAliveWithoutChangingEditorPreferenceKey()
    {
        var store = System.Reflection.DispatchProxy.Create<IAppSettingsDocumentStore, MoveItAutomationUI.Tests.MoveIt.SystemSettingsTests.TemplateProxy>();
        var root = new JsonObject();
        new ConfigurationSettingsMutator(null!, store).Apply(root, new ConfigurationSettingsSaveRequest
        {
            KeepAliveEnabled = false, KeepAliveIntervalMinutes = 12, UseMauiSystemSettingsEditor = true
        });
        Assert.False(root["KeepAlive"]!["Enabled"]!["Value"]!.GetValue<bool>());
        Assert.Equal(12, root["KeepAlive"]!["IntervalMinutes"]!["Value"]!.GetValue<int>());
        Assert.True(root["Editors"]!["UseMauiSystemSettingsEditor"]!["Value"]!.GetValue<bool>());
    }

    private static DefaultHttpContext Context(string pathBase)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        context.Request.PathBase = pathBase;
        context.Connection.LocalIpAddress = IPAddress.Loopback;
        context.Connection.LocalPort = 8080;
        return context;
    }

    private sealed class Pinger : IKeepAlivePinger
    {
        public int Calls;
        public bool WaitForCancellation;
        public bool Cancelled;
        public bool FailFirst;
        public TaskCompletionSource First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Second { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task PingAsync(KeepAliveAddress address, string token, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref Calls);
            First.TrySetResult();
            if (call == 1 && FailFirst) throw new HttpRequestException("Simulated local connection failure.");
            if (call == 2) Second.TrySetResult();
            if (!WaitForCancellation) return;
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { Cancelled = true; throw; }
        }
    }

    private sealed class OptionsMonitor : IOptionsMonitor<KeepAliveOptions>
    {
        private event Action<KeepAliveOptions, string?>? Changed;
        public KeepAliveOptions Value { get; } = new();
        public KeepAliveOptions CurrentValue => Value;
        public KeepAliveOptions Get(string? name) => Value;
        public IDisposable OnChange(Action<KeepAliveOptions, string?> listener)
        {
            Changed += listener;
            return new Subscription(() => Changed -= listener);
        }
        public void Change(bool enabled, int interval)
        {
            Value.Enabled.Value = enabled;
            Value.IntervalMinutes.Value = interval;
            Changed?.Invoke(Value, null);
        }
        private sealed class Subscription(Action dispose) : IDisposable { public void Dispose() => dispose(); }
    }
}
