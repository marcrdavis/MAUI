using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Tasks;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class TaskFilenameSearchTests
{
    [Fact]
    public async Task OneReadExtractsOnlyStoredFilenamesAcrossTraditionalAdvancedAndSync()
    {
        using var native = new NativeHandler
        {
            Body = """
            {"items":[
              {"ID":"1","Name":"Not searchable here","NextActions":{"Source":{"FileMask":"not-a-step.txt"}},"steps":[
                {"Source":{"FileMask":"*.txt","Path":"secret-folder","Password":"secret-password","ExFile":"excluded.zip"}},
                {"Destination":{"FileName":"[OrigName].pgp","UseOrigName":0}},
                {"Process":{"Message":"secret-script-body"}},
                {"Source":{"FileMask":"*.TXT","Disabled":1}}]},
              {"ID":"2","steps":[{"For":{"steps":[{"If":{"When":[
                {"Criteria":{"comp":{"b":"not-a-filename"}},"steps":[{"Destination":{"FileName":"nested.csv"}}]},
                {"steps":[{"For":{"steps":[{"Source":{"FileMask":"[yyyy]*.xml"}}]}}]}
              ],"Otherwise":{"steps":[{"Destination":{"FileName":"otherwise.dat"}}]}}}]}}]},
              {"ID":"3","TT":"Sy","steps":[{"Source":{"FileMask":"a*.csv"}},{"Source":{"FileMask":"b*.xml"}}]},
              {"ID":"4","steps":[]}, {"ID":"5"}
            ]}
            """
        };
        var service = Service(native);
        Assert.Empty(native.Requests);
        var items = await service.ReadAsync();
        Assert.True(items.Count == 5);
        Assert.Equal("*.txt\n[OrigName].pgp", items[0].Text);
        Assert.Contains("nested.csv", items[1].Text);
        Assert.Contains("[yyyy]*.xml", items[1].Text);
        Assert.Contains("otherwise.dat", items[1].Text);
        Assert.Equal("a*.csv\nb*.xml", items[2].Text);
        Assert.Empty(items[3].Text); Assert.Empty(items[4].Text);
        var json = JsonSerializer.Serialize(items);
        foreach (var secret in new[] { "secret-folder", "secret-password", "secret-script-body", "excluded.zip", "not-a-step.txt", "not-a-filename", "Not searchable here" })
            Assert.DoesNotContain(secret, json);
        Assert.True(native.Requests.Count == 1);
        Assert.Equal("GET /api/v1/tasks", native.Requests[0].Path);
        Assert.Contains("steps", native.Requests[0].Query);
        Assert.DoesNotContain("checkout", native.Requests[0].Path);
    }

    [Fact]
    public async Task SingleStepAndSingleConditionProjectionsAreSupported()
    {
        using var native = new NativeHandler { Body = """{"items":[{"ID":"1","steps":{"If":{"When":{"steps":{"Source":{"FileMask":"one.txt"}}}}}}]}""" };
        var items = await Service(native).ReadAsync();
        Assert.Equal("one.txt", items[0].Text);
    }

    [Fact]
    public async Task OptionalNullContainersAndUnknownStepTypesDoNotHideKnownFilenames()
    {
        using var native = new NativeHandler { Body = """
            {"items":[{"ID":"1","steps":[
              {"FutureStep":{"FileName":"not-a-known-filename"}},
              {"For":null},{"If":null},{"If":{"When":null,"Otherwise":null}},
              {"Source":{"FileMask":"known.txt"}}
            ]}]}
            """ };
        var items = await Service(native).ReadAsync();
        Assert.Equal("known.txt", items[0].Text);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"items\":null}")]
    [InlineData("{\"items\":[null]}")]
    [InlineData("{\"items\":[{\"steps\":[]}]}")]
    [InlineData("{\"items\":[{\"ID\":\"1\"},{\"ID\":\"1\"}]}")]
    [InlineData("{\"items\":[{\"ID\":\"1\",\"steps\":[null]}]}")]
    [InlineData("{\"items\":[{\"ID\":\"1\",\"steps\":[{\"Source\":\"invalid\"}]}]}")]
    [InlineData("{\"items\":[{\"ID\":\"1\",\"steps\":[{\"Destination\":{\"FileName\":123}}]}]}")]
    [InlineData("{\"items\":[{\"ID\":\"1\",\"steps\":[{\"If\":{\"When\":[null]}}]}]}")]
    [InlineData("""{"items":[{"ID":"1","steps":[{"Source":{"FileMask":"partial.txt"}},{"For":"invalid"}]}]}""")]
    [InlineData("""{"items":[{"ID":"1","steps":[{"Source":{"FileMask":"partial.txt"}},{"If":123}]}]}""")]
    [InlineData("""{"items":[{"ID":"1","steps":[{"Source":{"FileMask":"partial.txt"}},{"If":{"Otherwise":[]}}]}]}""")]
    public async Task IncompleteProjectionDoesNotReturnPartialResults(string body)
    {
        using var native = new NativeHandler { Body = body };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(native).ReadAsync());
    }

    [Fact]
    public async Task ExpiredSessionNeverStartsNativeRead()
    {
        using var native = new NativeHandler();
        await Assert.ThrowsAsync<MoveItAuthenticationException>(() => Service(native, authenticated: false).ReadAsync());
        Assert.Empty(native.Requests);
    }

    [Fact]
    public async Task NonAdminUsesOwnNativeVisibilityAndNoSharedServerCache()
    {
        using var native = new NativeHandler { Body = """{"items":[{"ID":"readable","steps":[{"Source":{"FileMask":"visible.txt"}}]}]}""" };
        var service = Service(native);
        Assert.Equal("visible.txt", (await service.ReadAsync())[0].Text);
        native.Body = """{"items":[]}""";
        Assert.Empty(await service.ReadAsync());
        Assert.True(native.Requests.Count == 2);
        Assert.All(native.Requests, item => Assert.Equal("Bearer user-token", item.Authorization));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task NativeReadFailuresAreNotEmptySuccessfulIndexes(HttpStatusCode status)
    {
        using var native = new NativeHandler { Status = status };
        await Assert.ThrowsAsync<MoveItApiException>(() => Service(native).ReadAsync());
        Assert.True(native.Requests.Count == 1);
    }

    [Fact]
    public async Task CancellationIsNotConvertedIntoEmptyResults()
    {
        using var native = new NativeHandler(); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(native).ReadAsync(cancel.Token));
    }

    [Fact]
    public async Task UpstreamTimeoutReturnsSafeErrorButRequestCancellationPropagates()
    {
        using var native = new NativeHandler { Failure = new TaskCanceledException("native timeout details") };
        var controller = new TaskFilenameSearchController(Service(native), NullLogger<TaskFilenameSearchController>.Instance);
        var failure = Assert.IsType<ObjectResult>(await controller.Index(default));
        Assert.Equal(500, failure.StatusCode);
        var json = JsonSerializer.Serialize(failure.Value);
        Assert.Contains("Task filenames could not be loaded", json);
        Assert.DoesNotContain("native timeout details", json);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => controller.Index(cancelled.Token));
    }

    [Fact]
    public async Task ControllerReturnsCompactWebJsonAndKeepsFailureDetailsSafe()
    {
        using var native = new NativeHandler();
        var controller = new TaskFilenameSearchController(Service(native), NullLogger<TaskFilenameSearchController>.Instance);
        var result = Assert.IsType<JsonResult>(await controller.Index(default));
        var json = JsonSerializer.Serialize(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"success\":true", json); Assert.Contains("\"taskId\":\"1\"", json); Assert.Contains("\"text\":\"*.txt\"", json);
        native.Body = """{"items":[{"ID":"1","steps":[null]}]}""";
        var failure = Assert.IsType<ObjectResult>(await controller.Index(default));
        Assert.Equal(500, failure.StatusCode);
        Assert.Contains("Retry filename search", JsonSerializer.Serialize(failure.Value));
        Assert.DoesNotContain("steps", JsonSerializer.Serialize(failure.Value));
        var expired = new TaskFilenameSearchController(Service(native, authenticated: false), NullLogger<TaskFilenameSearchController>.Instance);
        Assert.IsType<UnauthorizedObjectResult>(await expired.Index(default));
    }

    [Fact]
    public void EndpointRequiresAuthenticationAndDisablesHttpCaching()
    {
        Assert.NotNull(typeof(TaskFilenameSearchController).GetCustomAttribute<AuthorizeAttribute>());
        var cache = typeof(TaskFilenameSearchController).GetCustomAttribute<ResponseCacheAttribute>()!;
        Assert.True(cache.NoStore); Assert.Equal(ResponseCacheLocation.None, cache.Location);
        Assert.NotNull(typeof(TaskFilenameSearchController).GetMethod("Index")!.GetCustomAttribute<HttpGetAttribute>());
    }

    internal static TaskFilenameSearchService Service(NativeHandler handler, bool authenticated = true)
    {
        var sessions = DispatchProxy.Create<IMoveItSessionService, SessionProxy>();
        ((SessionProxy)(object)sessions).Authenticated = authenticated;
        var rest = new MoveItRestClient(new Factory(handler), sessions,
            ConfigurationStartupState.Capture(new ConfigurationBuilder().Build()), null!, NullLogger<MoveItRestClient>.Instance);
        return new(rest, sessions);
    }
    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, false); }
    public class SessionProxy : DispatchProxy
    {
        public bool Authenticated { get; set; } = true;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            nameof(IMoveItSessionService.Get) => Authenticated ? new MoveItSessionState
            { ServerKey = "one", Username = "reader", WebAdminUrl = "https://moveit.test", AccessToken = "user-token", AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddHours(1), Info = new MoveItInfo { IsAdmin = false } } : null,
            _ => throw new NotSupportedException(method?.Name)
        };
    }
    internal sealed class NativeHandler : HttpMessageHandler
    {
        public string Body { get; set; } = """{"items":[{"ID":"1","steps":[{"Source":{"FileMask":"*.txt"}}]}]}""";
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public Exception? Failure { get; set; }
        public List<(string Path, string Query, string Authorization)> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (Failure is { } failure) throw failure;
            Requests.Add((request.Method + " " + request.RequestUri!.AbsolutePath, Uri.UnescapeDataString(request.RequestUri.Query), request.Headers.Authorization!.ToString()));
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") });
        }
    }
}
