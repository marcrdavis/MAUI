using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Features.Editors;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.Security;

namespace MoveItAutomationUI.Tests.MoveIt;

// Runs real MVC routing, DI, compiled Razor/layout and JSON serialization over HTTP.
// The old markup-only browser fixture could not detect the unbound-options entry-route regression.
public sealed class GroupsAndPermissionsRoutingTests
{
    [Theory]
    [InlineData("")]
    [InlineData("/maui")]
    public async Task SavedPreferenceSelectsGridAndDataWithoutAnyFallbackPage(string pathBase)
    {
        await using var host = await Fixture.StartAsync(pathBase);
        using var client = host.Client();
        // Disabled: normal left navigation launches the native editor, and direct MAUI endpoints are unavailable.
        var shellResponse = await client.GetAsync(pathBase + "/Resources/Scripts");
        var scripts = await shellResponse.Content.ReadAsStringAsync();
        Assert.True(shellResponse.IsSuccessStatusCode, scripts);
        var nativeLink = GroupLink(scripts);
        Assert.Contains("data-native-editor-route=\"#/resources/permissions\"", nativeLink);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(pathBase + "/Resources/GroupsAndPermissions")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(pathBase + "/Resources/ResourceGroupPermissions")).StatusCode);
        Assert.Equal(0, host.Reads);

        // Emulate the runtime configuration publication performed by Save Configuration, with the host still running.
        // Deliberately do NOT bind MauiEditorOptions: reproduces the original application DI setup.
        host.Configuration["Editors:UseMauiGroupsAndPermissionsEditor:Value"] = "true";
        using (var scope = host.App.Services.CreateScope())
            Assert.False(scope.ServiceProvider.GetRequiredService<IOptionsSnapshot<MauiEditorOptions>>().Value.UseMauiGroupsAndPermissionsEditor.Value);
        var response = await client.GetAsync(pathBase + "/Resources/GroupsAndPermissions");
        var html = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, html);
        Assert.Contains("data-permissions-grid", html);
        Assert.Contains("<h1>Groups and Permissions</h1>", html);
        Assert.DoesNotContain("Native MOVEit page", html);
        Assert.DoesNotContain("This function is managed in MOVEit", html);
        Assert.DoesNotContain("data-native-editor-route=\"#/resources/permissions\"", GroupLink(html));
        Assert.Contains("href=\"" + pathBase + "/Resources/GroupsAndPermissions\"", GroupLink(html));
        Assert.Contains("data-inventory-url=\"" + pathBase + "/Resources/ResourceGroupPermissions\"", html);
        Assert.Contains("src=\"" + pathBase + "/js/groups-and-permissions.js", html);
        Assert.Contains("data-editor-url=\"" + pathBase + "/Resources/ResourceGroupEditor\"", html);
        Assert.Contains("data-resolve-url=\"" + pathBase + "/Resources/ResolveResourceGroupMembers\"", html);
        Assert.Contains("data-save-url=\"" + pathBase + "/Resources/SaveResourceGroup\"", html);
        Assert.Contains("data-save-permissions-url=\"" + pathBase + "/Resources/SaveResourceGroupPermissions\"", html);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(pathBase + "/Resources/ResourceGroupEditor?name=Finance")).StatusCode);
        Assert.Contains("data-delete-preflight-url=\"" + pathBase + "/Resources/PreflightDeleteResourceGroup\"", html);
        Assert.Contains("data-delete-url=\"" + pathBase + "/Resources/DeleteResourceGroup\"", html);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(pathBase + "/Resources/PreflightDeleteResourceGroup?name=Finance")).StatusCode);
        host.MoveItAdmin = false;
        // MOVEit administrator status gates the complete page and every grid/editor/ACL endpoint.
        var nonAdminShell = await (await client.GetAsync(pathBase + "/Resources/Scripts")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("data-groups-and-permissions-native-route", nonAdminShell);
        foreach (var route in new[] { "GroupsAndPermissions", "ResourceGroupPermissions", "ResourceGroupEditor?name=Finance", "PreflightDeleteResourceGroup?name=Finance", "ResourceGroupUserGroups?resourceGroupName=Finance&type=local" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(pathBase + "/Resources/" + route)).StatusCode);
        var token = Regex.Match(scripts, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        foreach (var route in new[] { "SaveResourceGroup", "DeleteResourceGroup", "ResolveResourceGroupMembers", "AddResourceGroupUserGroups", "RemoveResourceGroupUserGroup", "SaveResourceGroupPermissions" })
        {
            using var denied = new HttpRequestMessage(HttpMethod.Post, pathBase + "/Resources/" + route) { Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json") };
            denied.Headers.Add("RequestVerificationToken", token);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(denied)).StatusCode);
        }
        host.MoveItAdmin = true;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(pathBase + "/Resources/ResourceGroupUserGroups?resourceGroupName=Finance&type=remote")).StatusCode);
        foreach (var route in new[] { "AddResourceGroupUserGroups", "RemoveResourceGroupUserGroup", "SaveResourceGroupPermissions" })
        {
            using var write = new HttpRequestMessage(HttpMethod.Post, pathBase + "/Resources/" + route)
                { Content = new StringContent("{\"resourceGroupName\":\"Finance\",\"userGroups\":[{\"name\":\"Operators\",\"type\":\"local\"}],\"userGroupName\":\"Operators\",\"aclId\":\"0\",\"editVersion\":\"version\"}", System.Text.Encoding.UTF8, "application/json") };
            write.Headers.Add("RequestVerificationToken", token);
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(write)).StatusCode);
            using var noToken = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(pathBase + "/Resources/" + route, noToken)).StatusCode);
        }
        Assert.Equal(3, host.AclWrites);
        using (var delete = new HttpRequestMessage(HttpMethod.Post, pathBase + "/Resources/DeleteResourceGroup")
            { Content = new StringContent("{\"name\":\"Finance\",\"editVersion\":\"version\"}", System.Text.Encoding.UTF8, "application/json") })
        {
            delete.Headers.Add("RequestVerificationToken", token);
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(delete)).StatusCode);
        }
        Assert.Equal(1, host.GroupDeletes);
        using (var body = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"))
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(pathBase + "/Resources/DeleteResourceGroup", body)).StatusCode);
        // Native configuration writes require the application's anti-forgery token, including at /maui.
        using (var body = new StringContent("{}", System.Text.Encoding.UTF8, "application/json"))
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(pathBase + "/Resources/SaveResourceGroup", body)).StatusCode);
        var dataResponse = await client.GetAsync(pathBase + "/Resources/ResourceGroupPermissions?forceRefresh=true");
        Assert.Equal(HttpStatusCode.OK, dataResponse.StatusCode);
        var json = JsonNode.Parse(await dataResponse.Content.ReadAsStringAsync())!;
        Assert.True(json["success"]!.GetValue<bool>());
        Assert.Equal("Finance", json["groups"]![0]!["name"]!.ToString());
        Assert.Equal("All", json["groups"]![0]!["permissions"]!["TaskRun"]!["state"]!.ToString());
        Assert.Equal(1, host.Reads); Assert.True(host.Forced);

        if (pathBase.Length > 0 && Environment.GetEnvironmentVariable("MAUI_GROUPS_PAGE_FIXTURE_PATH") is { Length: > 0 } fixturePath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(fixturePath))!);
            await File.WriteAllTextAsync(fixturePath, JsonSerializer.Serialize(new { html }));
        }

        host.FailRead = true;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(pathBase + "/Resources/ResourceGroupPermissions")).StatusCode);
        host.SessionAvailable = false;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(pathBase + "/Resources/ResourceGroupPermissions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(pathBase + "/Resources/ResourceGroupEditor")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(pathBase + "/Resources/PreflightDeleteResourceGroup?name=Finance")).StatusCode);
        using (var expired = new HttpRequestMessage(HttpMethod.Post, pathBase + "/Resources/SaveResourceGroupPermissions") { Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json") })
        {
            expired.Headers.Add("RequestVerificationToken", token);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(expired)).StatusCode);
        }
        host.SessionAvailable = true;
        host.Configuration["Editors:UseMauiGroupsAndPermissionsEditor:Value"] = "false";
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(pathBase + "/Resources/GroupsAndPermissions")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(pathBase + "/Resources/ResourceGroupPermissions")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(pathBase + "/Resources/ResourceGroupEditor")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(pathBase + "/Resources/PreflightDeleteResourceGroup?name=Finance")).StatusCode);
        using (var disabled = new HttpRequestMessage(HttpMethod.Post, pathBase + "/Resources/SaveResourceGroupPermissions") { Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json") })
        {
            disabled.Headers.Add("RequestVerificationToken", token);
            Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(disabled)).StatusCode);
        }
    }

    [Theory]
    [InlineData("")] [InlineData("/maui")]
    public async Task CreationGroupsAreRenderedOnlyForNativeNonAdmins(string pathBase)
    {
        await using var host = await Fixture.StartAsync(pathBase);
        using var client = host.Client();
        foreach (var admin in new[] { true, false })
        {
            host.MoveItAdmin = admin;
            if (!admin) Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(pathBase + "/Resources/GlobalParameterDetails?name=Visible")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(pathBase + "/Resources/GlobalParameters")).StatusCode);
            foreach (var page in new[] { "Scripts", "KeysAndCerts" })
            {
                var response = await client.GetAsync(pathBase + "/Resources/" + page);
                var html = await response.Content.ReadAsStringAsync();
                Assert.True(response.IsSuccessStatusCode, html);
                if (admin) Assert.DoesNotContain("data-object-create-group=", html);
                else Assert.Contains("data-object-create-group=", html);
            }
        }
    }

    private static string GroupLink(string html)
    {
        var match = Regex.Match(html, "<a\\b[^>]*href=\"[^\"]*/Resources/GroupsAndPermissions\"[^>]*>[\\s\\S]*?</a>");
        Assert.True(match.Success, "The rendered application navigation must contain the Groups and Permissions link.");
        return match.Value;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public WebApplication App { get; private set; } = null!;
        public IConfiguration Configuration => App.Configuration;
        public int Reads { get; private set; }
        public bool Forced { get; private set; }
        public int AclWrites { get; set; }
        public int GroupDeletes { get; set; }
        public bool FailRead { get; set; }
        public bool SessionAvailable { get; set; } = true;
        public bool MoveItAdmin { get; set; } = true;
        private string _address = string.Empty;
        public HttpClient Client() => new(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(_address) };

        public static async Task<Fixture> StartAsync(string pathBase)
        {
            var fixture = new Fixture();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            { ApplicationName = typeof(ResourcesController).Assembly.GetName().Name, EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Configuration["Editors:UseMauiGroupsAndPermissionsEditor:Value"] = "false";
            builder.Services.AddScoped<MoveItObjectPermissionFilter>();
            builder.Services.AddMemoryCache();
            builder.Services.AddScoped<IMoveItObjectAccessService>(provider => new MoveItObjectAccessService(
                Stub<IMoveItRestClient>((method, _) => method.Name == nameof(IMoveItRestClient.GetResourceGroupDefinitionsAsync)
                    ? Task.FromResult<IReadOnlyList<JsonObject>>([]) : throw new NotSupportedException(method.Name)),
                provider.GetRequiredService<IMoveItSessionService>(), provider.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>()));
            builder.Services.AddControllersWithViews(options => options.Filters.AddService<MoveItObjectPermissionFilter>()).AddApplicationPart(typeof(ResourcesController).Assembly).AddControllersAsServices();
            builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Test", _ => { });
            builder.Services.AddAuthorization();
            builder.Services.Configure<GridBehaviorOptions>(options => { options.DefaultRowSize.Value = 2; options.DefaultRowSize.PossibleValues = [2, 5, 10]; });
            builder.Services.AddSingleton(ConfigurationStartupState.Capture(builder.Configuration));
            var sessions = Stub<IMoveItSessionService>((method, _) => method.Name == nameof(IMoveItSessionService.Get)
                ? fixture.SessionAvailable ? new MoveItSessionState { Username = "Test", ServerKey = "test", Info = new MoveItInfo { IsAdmin = fixture.MoveItAdmin } } : null
                : throw new NotSupportedException(method.Name));
            builder.Services.AddSingleton(sessions);
            builder.Services.AddSingleton(Stub<IResourceGroupManagementService>((method, _) =>
            {
                if (method.Name == nameof(IResourceGroupManagementService.PrepareDeleteAsync))
                    return Task.FromResult(new ResourceGroupDeleteSetup("Finance", "Description", "version"));
                if (method.Name == nameof(IResourceGroupManagementService.DeleteAsync)) { fixture.GroupDeletes++; return Task.CompletedTask; }
                return method.Name == nameof(IResourceGroupManagementService.PrepareEditorAsync)
                    ? Task.FromResult(new ResourceGroupEditorSetup(new ResourceGroupEditorState("Finance", "Description", "version", [], []), []))
                    : throw new NotSupportedException(method.Name);
            }));
            builder.Services.AddSingleton(Stub<IMauiSecurityService>((method, _) => method.Name == nameof(IMauiSecurityService.IsAdmin) ? false : throw new NotSupportedException(method.Name)));
            builder.Services.AddSingleton(Stub<IUserPreferencesService>((method, _) => method.Name switch
            {
                nameof(IUserPreferencesService.GetDefaults) => new UserPreferencesSnapshot("light", "Dashboard", 2),
                nameof(IUserPreferencesService.GetCurrentAsync) => Task.FromResult(new UserPreferencesSnapshot("light", "Dashboard", 2)),
                nameof(IUserPreferencesService.GetCurrentGridLayoutAsync) => Task.FromResult<UserGridLayoutSnapshot?>(null),
                _ => throw new NotSupportedException(method.Name)
            }));
            builder.Services.AddSingleton(Stub<ISavedViewService>((method, _) => method.Name == nameof(ISavedViewService.GetCurrentAsync)
                ? Task.FromResult<IReadOnlyList<SavedViewSummary>>([]) : throw new NotSupportedException(method.Name)));
            builder.Services.AddSingleton(Stub<IResourceGroupPermissionsService>((method, args) =>
            {
                if (method.Name == nameof(IResourceGroupPermissionsService.PrepareUserGroupsAsync))
                    return Task.FromResult(new ResourceGroupUserGroupSetup("Finance", args[1]!.ToString()!, [new("Operators", "Description")], []));
                if (method.Name is nameof(IResourceGroupPermissionsService.AddUserGroupsAsync) or nameof(IResourceGroupPermissionsService.RemoveUserGroupAsync))
                {
                    fixture.AclWrites++;
                    return Task.FromResult(new ResourceGroupUserGroupMutation("Finance", [new("Operators", "added", "Confirmed")]));
                }
                if (method.Name == nameof(IResourceGroupPermissionsService.SavePermissionsAsync))
                {
                    fixture.AclWrites++;
                    return Task.FromResult(new ResourceGroupPermissionsSaveResult("Finance", [new("0", "Operators", "saved", "Permissions saved.")]));
                }
                if (method.Name != nameof(IResourceGroupPermissionsService.GetSnapshotAsync)) throw new NotSupportedException(method.Name);
                fixture.Reads++; fixture.Forced = (bool)args[0]!;
                if (fixture.FailRead) return Task.FromException<ResourceGroupPermissionsSnapshot>(new InvalidOperationException("Permissions unavailable"));
                var permissions = new JsonObject(ResourceGroupPermissionsService.PermissionKeys.Select(key => KeyValuePair.Create<string, JsonNode?>(key, JsonValue.Create(1))));
                var snapshot = ResourceGroupPermissionsService.BuildSnapshot([new JsonObject { ["Name"] = "Finance" }],
                    [new JsonObject { ["ResourceGroupName"] = "Finance", ["UserGroupName"] = "Operators", ["ACLID"] = "0", ["Permissions"] = permissions }], DateTimeOffset.UtcNow);
                return Task.FromResult(snapshot);
            }));
            builder.Services.AddTransient(provider => new ResourcesController(
                scriptService: null!, dateListService: null!, globalParameterService: null!, keyCertificateService: null!,
                metadataRepository: null!, metadataValueValidation: null!, scriptUndoService: null!, recycleBinService: null!,
                resourcePortabilityService: null!, dateListUndoService: null!, sessionService: sessions,
                uiOptions: provider.GetRequiredService<IOptionsSnapshot<UiOptions>>(),
                gridBehaviorOptions: provider.GetRequiredService<IOptionsSnapshot<GridBehaviorOptions>>(),
                metadataOptions: provider.GetRequiredService<IOptionsSnapshot<MetadataUiOptions>>(),
                scriptBehaviorOptions: provider.GetRequiredService<IOptionsSnapshot<ScriptBehaviorOptions>>(),
                dateListBehaviorOptions: provider.GetRequiredService<IOptionsSnapshot<DateListBehaviorOptions>>(),
                globalParameterBehaviorOptions: provider.GetRequiredService<IOptionsSnapshot<GlobalParameterBehaviorOptions>>(),
                keyCertificateBehaviorOptions: provider.GetRequiredService<IOptionsSnapshot<KeyCertificateBehaviorOptions>>(),
                logger: provider.GetRequiredService<ILogger<ResourcesController>>(), scriptMetadataSaves: null!));
            fixture.App = builder.Build();
            fixture.App.UseDeveloperExceptionPage();
            if (pathBase.Length > 0) fixture.App.UsePathBase(pathBase);
            fixture.App.UseRouting(); fixture.App.UseAuthentication(); fixture.App.UseAuthorization();
            fixture.App.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
            await fixture.App.StartAsync();
            fixture._address = fixture.App.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            return fixture;
        }
        public async ValueTask DisposeAsync() => await App.DisposeAsync();
    }

    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
            => Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Test")], "Test")), "Test")));
    }
    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    { var proxy = DispatchProxy.Create<T, HostSharedCrudTests.CrudProxy>(); ((HostSharedCrudTests.CrudProxy)(object)proxy).Handler = handler; return proxy; }
}
