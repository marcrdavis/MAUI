using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class ForcedCheckoutTransportTests
{
    [Theory]
    [InlineData("Task", "498499684", "/api/v0/tasks/498499684/checkout")]
    [InlineData("Host", "101", "/api/v0/hosts/101/checkout")]
    [InlineData("Host", "0", "/api/v0/hosts/0/checkout")]
    [InlineData("Script", "101", "/api/v0/customscripts/101/checkout")]
    [InlineData("DateList", "101", "/api/v0/datelists/101/checkout")]
    public async Task AdminOverrideUsesTheForceBodyAfterNormalCheckoutIsRejected(string type, string id, string path)
    {
        using var f = new Fixture(path, id, admin:true);
        await Assert.ThrowsAsync<ObjectCheckoutRejectedException>(() => f.Transport.AcquireAsync(type, id, f.Session, default));
        Assert.True(f.Handler.Bodies.Count == 1); Assert.False(f.Handler.Bodies[0]?["force"]?.GetValue<bool>() ?? false);
        await f.Transport.ForceAcquireAsync(type, id, f.Session, default);
        Assert.True(f.Handler.Bodies.Count == 2); var forced = f.Handler.Bodies[1]!;
        Assert.True(forced["force"]!.GetValue<bool>()); Assert.True(forced.Count == 1);
    }

    [Theory]
    [InlineData("MOVEit Settings", "/api/v0/globals/checkout")]
    [InlineData("Resource Groups", "/api/v0/resourcegroups/checkout")]
    public async Task DatasetOverrideUsesTheSameExplicitForceInput(string type, string path)
    {
        using var f = new Fixture(path, "-1", admin:true);
        var result = await f.Rest.ForceCheckoutObjectV0Async(type, "-1");
        Assert.Equal("-1", result["ID"]!.ToString());
        Assert.True(Assert.Single(f.Handler.Bodies)!["force"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("Task")]
    [InlineData("Host")]
    [InlineData("Script")]
    [InlineData("DateList")]
    [InlineData("MOVEit Settings")]
    [InlineData("Resource Groups")]
    public async Task NonAdminCannotDispatchAForcedRestRequest(string type)
    {
        using var f = new Fixture("not-called", "101", admin:false);
        await Assert.ThrowsAsync<MoveItApiException>(() => f.Rest.ForceCheckoutObjectV0Async(type, "101"));
        if (type is "Task" or "Host" or "Script" or "DateList")
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.Transport.ForceAcquireAsync(type, "101", f.Session, default));
        Assert.Empty(f.Handler.Bodies);
    }

    [Fact]
    public async Task RejectedForceIsNotRetriedAndRetainsTheNormalizedNativeMessage()
    {
        using var f = new Fixture("/api/v0/tasks/101/checkout", "101", admin:true); f.Handler.RejectForce = true;
        var error = await Assert.ThrowsAsync<ObjectCheckoutRejectedException>(() => f.Transport.ForceAcquireAsync("Task", "101", f.Session, default));
        Assert.Equal("This object has been checked out to miadmin since 2026-10-24 17:27:53", error.Message);
        Assert.Single(f.Handler.Bodies);
    }
    private sealed class Fixture : IDisposable
    {
        public MoveItSessionState Session { get; }
        public Handler Handler { get; }
        public MoveItRestClient Rest { get; }
        public ObjectEditLeaseTransport Transport { get; }
        private readonly ServiceProvider _services;
        public Fixture(string path, string id, bool admin)
        {
            Session = new() { WebAdminUrl="https://moveit.test", Username="miadmin", Info=new MoveItInfo { IsAdmin=admin }, AccessToken="token", AccessTokenExpiresUtc=DateTimeOffset.UtcNow.AddHours(1) };
            var sessions = Stub<IMoveItSessionService>((method, _) => method.Name switch {
                nameof(IMoveItSessionService.Get) => Session,
                nameof(IMoveItSessionService.PushBackgroundSession) => new Scope(),
                _ => throw new NotSupportedException(method.Name) });
            Handler = new(path,id);
            Rest = new(new Factory(Handler),sessions,ConfigurationStartupState.Capture(new ConfigurationBuilder().Build()),null!,NullLogger<MoveItRestClient>.Instance);
            var com = Stub<IMoveItComClient>((method,_) => throw new InvalidOperationException("REST override must not call COM: " + method.Name));
            _services = new ServiceCollection().AddSingleton(sessions).AddSingleton<IMoveItRestClient>(Rest).AddSingleton(com).BuildServiceProvider();
            Transport = new(_services.GetRequiredService<IServiceScopeFactory>());
        }
        public void Dispose() { _services.Dispose(); Handler.Dispose(); }
    }
    private sealed class Handler(string path, string id) : HttpMessageHandler
    {
        public List<JsonObject?> Bodies { get; } = [];
        public bool RejectForce;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Post, request.Method); Assert.Equal(path, request.RequestUri!.AbsolutePath);
            var body = request.Content is null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(ct))!.AsObject(); Bodies.Add(body);
            var force = body?["force"]?.GetValue<bool>() == true;
            if (!force || RejectForce) return new(HttpStatusCode.Conflict) { Content = new StringContent("{\"errors\":[{\"message\":\"This section has been checked out to miadmin since 2026-10-24 17:27:53\"}]}") };
            Assert.Equal("application/json",request.Content!.Headers.ContentType!.MediaType);
            return new(HttpStatusCode.OK) { Content = new StringContent(new JsonObject { ["ID"]=id,["Name"]="Current server object" }.ToJsonString()) };
        }
    }
    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory { public HttpClient CreateClient(string name) => new(handler,disposeHandler:false); }
    private sealed class Scope : IDisposable { public void Dispose() { } }
    private static T Stub<T>(Func<MethodInfo,object?[],object?> handler) where T:class
    {
        var value = DispatchProxy.Create<T,HostSharedCrudTests.CrudProxy>();((HostSharedCrudTests.CrudProxy)(object)value).Handler=handler;return value;
    }
}
