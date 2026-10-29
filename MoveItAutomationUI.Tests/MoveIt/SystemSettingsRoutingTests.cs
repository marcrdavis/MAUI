using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Features.Editors;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt.SystemSettings;

namespace MoveItAutomationUI.Tests.MoveIt;

// Real MVC, DI, authentication, antiforgery and JSON serialization. Only the MOVEit
// HTTP peer is simulated. In particular, editor options stay unbound, as in Program.cs.
public sealed class SystemSettingsRoutingTests
{
    private const string Setting = "Editors:UseMauiSystemSettingsEditor:Value";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("")]
    [InlineData("/maui")]
    public async Task SavedPreferenceEnablesOpeningAndSavingWithoutRestartOrOptionsBinding(string pathBase)
    {
        await using var host = await Fixture.StartAsync(pathBase);
        using var client = host.Client();
        var token = await Token(client, pathBase);
        var change = new SystemSettingsSaveRequest(new SystemLoggingSettings
        {
            DebugLevel = 60, LogToDisk = true, LogFile = "MOVEitC.log", MaxLogFileSize = 10485760,
            LogToTaskLog = true, TaskLogDir = "", AS2LogDirectory = "", NumTaskLogsToKeep = 5, AuditXferRetries = true
        }, "revision");
        foreach (var action in new[] { "Shell", "Save", "Lockout", "ActiveLockouts", "Unlock" })
        {
            using var disabled = await Post(client, pathBase, action, token, action == "Save" ? change : action == "Unlock" ? new SystemUnlockRequest("ip", [new("192.168.198.1", "version")]) : new { });
            Assert.Equal(HttpStatusCode.NotFound, disabled.StatusCode);
        }
        Assert.Empty(host.Native.Calls);

        // Save Configuration publishes into this same live IConfiguration root.
        host.Configuration[Setting] = "true";
        using (var scope = host.App.Services.CreateScope())
            Assert.False(scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<MauiEditorOptions>>().Value.UseMauiSystemSettingsEditor.Value);
        using var open = await Post(client, pathBase, "Shell", token, new { });
        Assert.Equal(HttpStatusCode.OK, open.StatusCode);
        var opened = JsonSerializer.Deserialize<EditorResponse>(await open.Content.ReadAsStringAsync(), WebJson)!;
        Assert.True(opened.Success);
        Assert.Equal(60, opened.State.Logging.DebugLevel);
        Assert.Equal("MOVEitC.log", opened.State.Logging.LogFile);
        Assert.Equal("", opened.State.Logging.AS2LogDirectory);
        Assert.NotEmpty(opened.State.EditVersion);
        Assert.Equal(new[] { "POST /api/v0/globals/checkout", "DELETE /api/v0/globals/checkout" }, host.Native.Calls);

        var logging = opened.State.Logging with { DebugLevel = 70, AS2LogDirectory = @"C:\Logs\ASx", MaxLogFileSize = 2147483648L };
        using var saved = await Post(client, pathBase, "Save", token, new SystemSettingsSaveRequest(logging, opened.State.EditVersion));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var result = JsonSerializer.Deserialize<EditorResponse>(await saved.Content.ReadAsStringAsync(), WebJson)!;
        Assert.True(result.Success);
        Assert.Equal(logging, result.State.Logging);
        Assert.NotEqual(opened.State.EditVersion, result.State.EditVersion);
        Assert.True(host.Native.Writes == 1);
        Assert.False(host.Native.CheckedOut);
        Assert.Equal(@"C:\Logs\ASx", host.Native.Data["Globals"]!["AS2LogDirectory"]!.GetValue<string>());

        // The same running host must honor a later disable for both read and write.
        host.Configuration[Setting] = "false";
        var calls = host.Native.Calls.Count;
        foreach (var action in new[] { "Shell", "Save", "Lockout", "ActiveLockouts", "Unlock" })
        {
            using var disabled = await Post(client, pathBase, action, token,
                action == "Save" ? new SystemSettingsSaveRequest(logging, result.State.EditVersion) : action == "Unlock" ? new SystemUnlockRequest("ip", [new("192.168.198.1", "version")]) : new { });
            Assert.Equal(HttpStatusCode.NotFound, disabled.StatusCode);
        }
        Assert.True(calls == host.Native.Calls.Count);
    }

    [Fact]
    public async Task HttpEndpointsRetainAdminAndAntiforgeryGuards()
    {
        await using var host = await Fixture.StartAsync("");
        using var client = host.Client();
        host.Configuration[Setting] = "true";
        var token = await Token(client, "");
        foreach (var action in new[] { "Shell", "Save", "Lockout", "ActiveLockouts", "Unlock" })
        {
            using var noToken = await Post(client, "", action, "", new { });
            Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.GetAsync("/SystemSettingsEditor/" + action)).StatusCode);
        }
        Assert.Empty(host.Native.Calls);
        host.MauiAdmin = false;
        using var denied = await Post(client, "", "Shell", token, new { });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Empty(host.Native.Calls);
        host.MauiAdmin = true; host.MoveItAdmin = false;
        using var nativeDenied = await Post(client, "", "Shell", token, new { });
        Assert.Equal(HttpStatusCode.Forbidden, nativeDenied.StatusCode);
        Assert.Empty(host.Native.Calls);
    }

    [Fact]
    public async Task AllSectionsAndTrustedIpsRoundTripThroughHttpAndNativeContracts()
    {
        await using var host = await Fixture.StartAsync("/maui"); using var client = host.Client();
        host.Configuration[Setting] = "true"; var token = await Token(client, "/maui");
        using var openedResponse = await Post(client, "/maui", "Shell", token, new { });
        var opened = JsonSerializer.Deserialize<EditorResponse>(await openedResponse.Content.ReadAsStringAsync(), WebJson)!.State;
        using var policyResponse = await Post(client, "/maui", "Lockout", token, new { });
        Assert.Equal(HttpStatusCode.OK, policyResponse.StatusCode);
        using var policyJson = JsonDocument.Parse(await policyResponse.Content.ReadAsStringAsync());
        var lockout = policyJson.RootElement.GetProperty("state").Deserialize<SystemLockoutState>(WebJson)!;
        Assert.Equal(new[] { "1.1.1.1" }, lockout.TrustedIPs);
        var request = new SystemSettingsSaveRequest(null, opened.EditVersion,
            EventLog: opened.EventLog! with { EventLogName = "MOVEit", EventLogLevel = 40, EventLogAuditLevel = 30 },
            Tasks: opened.Tasks! with { AS2PollIntervalSecs = 5 }, StateFile: new() { DefStateCacheTime = 5, DefStateCacheUnit = "Hours" },
            Tamper: new() { Enabled = false }, Lockout: lockout.Policy with { UserLockoutEnabled = true },
            LockoutVersion: lockout.EditVersion, TrustedIPs: ["2.2.2.2"], TrustedVersion: lockout.TrustedEditVersion);
        using var saved = await Post(client, "/maui", "Save", token, request);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var state = JsonSerializer.Deserialize<EditorResponse>(await saved.Content.ReadAsStringAsync(), WebJson)!.State;
        Assert.Equal(request.Tasks, state.Tasks); Assert.Equal(request.EventLog, state.EventLog);
        Assert.Equal(request.StateFile, state.StateFile); Assert.Equal(request.Tamper, state.Tamper);
        Assert.Equal(request.Lockout, state.Lockout!.Policy); Assert.Equal(new[] { "2.2.2.2" }, state.Lockout.TrustedIPs);
        Assert.True(host.Native.Writes == 1 && host.Native.PolicyWrites == 1 && host.Native.TrustedWrites == 2);
        Assert.Equal(new[] { "POST", "POST", "DELETE" }, host.Native.PolicyRequests.Select(item => item.Method));
        Assert.False(host.Native.CheckedOut);
        // A stale trusted revision rejects the complete request before another Globals write.
        using var stale = await Post(client, "/maui", "Save", token, request);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode); Assert.True(host.Native.Writes == 1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/maui")]
    public async Task ActiveLockoutsAndUnlockUseProtectedRoutesAndBareNativeDeleteWithoutCheckout(string pathBase)
    {
        await using var host = await Fixture.StartAsync(pathBase); using var client = host.Client();
        host.Configuration[Setting] = "true"; var token = await Token(client, pathBase);
        using var response = await Post(client, pathBase, "ActiveLockouts", token, new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var state = json.RootElement.GetProperty("state").Deserialize<SystemActiveLockouts>(WebJson)!;
        var item = state.Addresses[0];
        using var saved = await Post(client, pathBase, "Unlock", token, new SystemUnlockRequest("ip", [new(item.Value, item.EditVersion)]));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        using var savedJson = JsonDocument.Parse(await saved.Content.ReadAsStringAsync());
        var result = savedJson.RootElement.GetProperty("state").Deserialize<SystemUnlockResult>(WebJson)!;
        Assert.Equal(1, result.Unlocked); Assert.Empty(result.Items);
        Assert.True(host.Native.LockoutDeletes == 1);
        Assert.Equal("[\"192.168.198.1\"]", host.Native.LastUnlockBody!.ToJsonString());
        Assert.DoesNotContain(host.Native.Calls, call => call.Contains("checkout") || call.Contains("globals"));
        var user = state.Users[0]; host.Native.UnlockFault = "network";
        using var uncertain = await Post(client, pathBase, "Unlock", token, new SystemUnlockRequest("user", [new(user.Value, user.EditVersion)]));
        Assert.Equal(HttpStatusCode.Conflict, uncertain.StatusCode);
        using var error = JsonDocument.Parse(await uncertain.Content.ReadAsStringAsync());
        Assert.True(error.RootElement.GetProperty("refreshLockouts").GetBoolean());
        Assert.True(host.Native.LockoutDeletes == 2);
        host.MauiAdmin = false;
        using var forbidden = await Post(client, pathBase, "ActiveLockouts", token, new { });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var forbiddenUnlock = await Post(client, pathBase, "Unlock", token, new SystemUnlockRequest("ip", [new(item.Value, item.EditVersion)]));
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenUnlock.StatusCode);
    }

    private static async Task<string> Token(HttpClient client, string pathBase)
    {
        using var response = await client.GetAsync(pathBase + "/token");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("token").GetString()!;
    }
    private static async Task<HttpResponseMessage> Post(HttpClient client, string pathBase, string action, string token, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, pathBase + "/SystemSettingsEditor/" + action)
        { Content = JsonContent.Create(body, options: WebJson) };
        if (token.Length > 0) request.Headers.Add("RequestVerificationToken", token);
        return await client.SendAsync(request);
    }
    private sealed record EditorResponse(bool Success, SystemSettingsState State);
    private sealed class Fixture : IAsyncDisposable
    {
        public WebApplication App { get; private set; } = null!;
        public IConfiguration Configuration => App.Configuration;
        public SystemSettingsTests.NativeHandler Native { get; } = new();
        public bool MauiAdmin { get; set; } = true;
        public bool MoveItAdmin { get; set; } = true;
        private string _address = "";
        public HttpClient Client() => new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(_address) };
        public static async Task<Fixture> StartAsync(string pathBase)
        {
            var fixture = new Fixture();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            { ApplicationName = typeof(SystemSettingsEditorController).Assembly.GetName().Name, EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration[Setting] = "false";
            builder.Services.AddSingleton(fixture);
            builder.Services.AddControllersWithViews().AddApplicationPart(typeof(SystemSettingsEditorController).Assembly);
            builder.Services.AddScoped<ISystemSettingsService>(_ => SystemSettingsTests.Service(fixture.Native, fixture.MoveItAdmin));
            builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Test", _ => { });
            builder.Services.AddAuthorization(options => options.AddPolicy(MauiSecurityPolicies.Admin,
                policy => policy.RequireAuthenticatedUser().RequireRole(MauiSecurityRoles.Admin)));
            builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");
            // Deliberately do not Configure<MauiEditorOptions>: production reads this
            // preference directly, and an unbound snapshot is the reported regression.
            fixture.App = builder.Build();
            if (pathBase.Length > 0) fixture.App.UsePathBase(pathBase);
            fixture.App.UseRouting(); fixture.App.UseAuthentication(); fixture.App.UseAuthorization();
            fixture.App.MapGet("/token", (HttpContext context, IAntiforgery antiforgery) => Results.Json(new { token = antiforgery.GetAndStoreTokens(context).RequestToken }));
            fixture.App.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
            await fixture.App.StartAsync();
            fixture._address = fixture.App.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            return fixture;
        }
        public async ValueTask DisposeAsync() { await App.DisposeAsync(); Native.Dispose(); }
    }
    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, Fixture fixture)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var claims = new List<Claim> { new(ClaimTypes.Name, "Test") };
            if (fixture.MauiAdmin) claims.Add(new(ClaimTypes.Role, MauiSecurityRoles.Admin));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")), "Test")));
        }
    }
}
