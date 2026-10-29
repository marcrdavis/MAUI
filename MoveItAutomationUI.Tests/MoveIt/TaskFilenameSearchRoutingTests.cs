using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Services.MoveIt.Tasks;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class TaskFilenameSearchRoutingTests
{
    [Theory]
    [InlineData("")]
    [InlineData("/maui")]
    public async Task NonAdminReadUsesRealMvcRouteAndNativeProjectionWithoutCheckout(string pathBase)
    {
        await using var host = await Fixture.StartAsync(pathBase);
        using var client = new HttpClient { BaseAddress = host.Address };
        using var anonymous = await client.GetAsync(pathBase + "/TaskFilenameSearch");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Empty(host.Native.Requests);
        client.DefaultRequestHeaders.Add("X-Test-Auth", "reader");
        using var response = await client.GetAsync(pathBase + "/TaskFilenameSearch");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore == true);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        var item = json.RootElement.GetProperty("items")[0];
        Assert.Equal("1", item.GetProperty("taskId").GetString());
        Assert.Equal("*.txt", item.GetProperty("text").GetString());
        Assert.True(host.Native.Requests.Count == 1);
        Assert.Equal("GET /api/v1/tasks", host.Native.Requests[0].Path);
        Assert.Equal("?fields=ID,steps", host.Native.Requests[0].Query);
        Assert.Equal("Bearer user-token", host.Native.Requests[0].Authorization);
        host.Native.Body = "{}";
        using var incomplete = await client.GetAsync(pathBase + "/TaskFilenameSearch");
        Assert.Equal(HttpStatusCode.InternalServerError, incomplete.StatusCode);
        using var error = JsonDocument.Parse(await incomplete.Content.ReadAsStringAsync());
        Assert.False(error.RootElement.TryGetProperty("items", out _));
        Assert.Contains("Retry filename search", error.RootElement.GetProperty("message").GetString());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required WebApplication App { get; init; }
        public required Uri Address { get; init; }
        public required TaskFilenameSearchTests.NativeHandler Native { get; init; }
        public static async Task<Fixture> StartAsync(string pathBase)
        {
            var native = new TaskFilenameSearchTests.NativeHandler();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, Authentication>("test", _ => { });
            builder.Services.AddAuthorization();
            builder.Services.AddControllers().AddApplicationPart(typeof(TaskFilenameSearchController).Assembly);
            builder.Services.AddScoped<ITaskFilenameSearchService>(_ => TaskFilenameSearchTests.Service(native));
            var app = builder.Build();
            if (pathBase.Length > 0) app.UsePathBase(pathBase);
            app.UseAuthentication(); app.UseAuthorization();
            app.MapControllerRoute("default", "{controller=TaskFilenameSearch}/{action=Index}/{id?}");
            await app.StartAsync();
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            return new Fixture { App = app, Address = new Uri(address), Native = native };
        }
        public async ValueTask DisposeAsync() { await App.DisposeAsync(); Native.Dispose(); }
    }
    private sealed class Authentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Test-Auth")) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "reader")], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
