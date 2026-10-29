using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Tasks;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;

namespace MoveItAutomationUI.Tests.MoveIt;

// Captured creation contracts, mocked HTTP/COM only; no live MOVEit writes.
public sealed class TaskV0CreationTests
{
    private static JsonObject Capture(MoveItTaskKind kind)
    {
        var task = JsonNode.Parse("""{"Task":{"Name":"NewAdvTask","ID":"","AR":0,"Active":0,"Info":{"Description":"","Notes":""},"CacheNames":"random","UseDefStateCaching":1,"NextEID":11,"Watched":0,"InvalidOverride":0,"Group":[],"TT":"COS"}}""")!["Task"]!.AsObject();
        if (kind == MoveItTaskKind.Synchronization)
        {
            task["Name"] = "SyncTaskTEST"; task["TT"] = "Sy"; task["SameSecs"] = -1;
        }
        return task;
    }

    [Theory]
    [InlineData(MoveItTaskKind.Advanced)] [InlineData(MoveItTaskKind.Synchronization)]
    public async Task SharedCreationPostsTheCapturedDisabledEmptySeedAndVerifiesReadback(MoveItTaskKind kind)
    {
        var fixture = new Fixture(kind);
        var result = await fixture.Crud.CreateEmptyAsync(" " + fixture.Name + " ", kind.ToString());
        Assert.Equal("574205225", result.Id); Assert.Equal(fixture.Name, result.Name);
        Assert.Equal(kind == MoveItTaskKind.Advanced ? "Advanced" : "Synchronization", result.TaskType);
        Assert.True(JsonNode.DeepEquals(Capture(kind), fixture.Submitted));
        Assert.Equal(new[] { "POST", "GET:574205225" }, fixture.Calls);
        Assert.Equal(0, fixture.ComCalls);
    }

    [Theory]
    [InlineData(MoveItTaskKind.Advanced)] [InlineData(MoveItTaskKind.Synchronization)]
    public async Task ActualRestClientUsesPostV0TaskEnvelopeBearerAndJsonWithoutMutatingTheSeed(MoveItTaskKind kind)
    {
        var definition = Capture(kind); var before = definition.DeepClone();
        using var handler = new CaptureHandler();
        var session = Stub<IMoveItSessionService>((method, _) => method.Name == nameof(IMoveItSessionService.Get)
            ? new MoveItSessionState { WebAdminUrl = "https://moveit.test", AccessToken = "test-token", AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) }
            : throw new NotSupportedException(method.Name));
        var client = new MoveItRestClient(new ClientFactory(handler), session,
            ConfigurationStartupState.Capture(new ConfigurationBuilder().Build()), null!, NullLogger<MoveItRestClient>.Instance);
        await client.CreateTaskV0Async(definition);
        Assert.Equal(1, handler.Requests);
        Assert.Equal(HttpMethod.Post, handler.Method); Assert.Equal("/api/v0/tasks", handler.Path);
        Assert.Equal("Bearer test-token", handler.Authorization); Assert.Equal("application/json", handler.MediaType);
        Assert.True(JsonNode.DeepEquals(new JsonObject { ["Task"] = before.DeepClone() }, handler.Body));
        Assert.True(JsonNode.DeepEquals(before, definition));
    }

    [Theory]
    [InlineData("missing-id")] [InlineData("empty")] [InlineData("error")]
    [InlineData("errorcode")] [InlineData("inline-id")] [InlineData("inline-type")]
    [InlineData("inline-enabled")] [InlineData("inline-name")]
    public async Task InvalidAcknowledgementsNeverRetryCreateOrFallbackToCom(string failure)
    {
        var fixture = new Fixture(MoveItTaskKind.Advanced);
        switch (failure)
        {
            case "missing-id": fixture.Response.Remove("id"); break;
            case "empty": fixture.Response = new JsonObject(); break;
            case "error": fixture.Response["status"] = "error"; fixture.Response["errorcode"] = 53; break;
            case "errorcode": fixture.Response["errorcode"] = 1; break;
            case "inline-id": fixture.Response["object"]!["ID"] = "other"; break;
            case "inline-type": fixture.Response["object"]!["TT"] = "Sy"; break;
            case "inline-enabled": fixture.Response["object"]!["Active"] = 1; break;
            case "inline-name": fixture.Response["object"]!["Name"] = "Other Task"; break;
        }
        var error = await Assert.ThrowsAsync<TaskV0CreationOutcomeException>(() => fixture.Create());
        Assert.Equal(failure is "missing-id" or "empty" ? null : "574205225", error.TaskId);
        Assert.Equal(new[] { "POST" }, fixture.Calls); Assert.Equal(0, fixture.ComCalls);
    }

    [Theory]
    [InlineData("readback")] [InlineData("id")] [InlineData("name")] [InlineData("type")]
    [InlineData("enabled")] [InlineData("steps")] [InlineData("schedule")] [InlineData("sync-tolerance")]
    public async Task FailedSavedStateVerificationKeepsKnownIdAndNeverDeletesOrRepeats(string failure)
    {
        var fixture = new Fixture(MoveItTaskKind.Synchronization);
        switch (failure)
        {
            case "readback": fixture.ReadFailure = new HttpRequestException("Read failed"); break;
            case "id": fixture.Persisted["ID"] = "other"; break;
            case "name": fixture.Persisted["Name"] = "Other Task"; break;
            case "type": fixture.Persisted["TT"] = "COS"; break;
            case "enabled": fixture.Persisted["Active"] = true; break;
            case "steps": fixture.Persisted["steps"]!.AsArray().Add(new JsonObject { ["Source"] = new JsonObject() }); break;
            case "schedule": fixture.Persisted["Schedules"] = new JsonObject { ["Schedule"] = new JsonArray(new JsonObject()) }; break;
            case "sync-tolerance": fixture.Persisted["SameSecs"] = 0; break;
        }
        var error = await Assert.ThrowsAsync<TaskV0CreationOutcomeException>(() => fixture.Create());
        Assert.Equal("574205225", error.TaskId); Assert.Equal(fixture.Name, error.TaskName);
        Assert.Equal(new[] { "POST", "GET:574205225" }, fixture.Calls); Assert.Equal(0, fixture.ComCalls);
    }

    [Theory]
    [InlineData(0)] [InlineData(408)] [InlineData(500)]
    public async Task LostOrIndeterminatePostResponsesAreNotReplayed(int status)
    {
        var fixture = new Fixture(MoveItTaskKind.Advanced)
        {
            PostFailure = status == 0 ? new HttpRequestException("Disconnected") : new MoveItApiException("Unknown", (HttpStatusCode)status)
        };
        var error = await Assert.ThrowsAsync<TaskV0CreationOutcomeException>(() => fixture.Create());
        Assert.Null(error.TaskId); Assert.Equal(new[] { "POST" }, fixture.Calls); Assert.Equal(0, fixture.ComCalls);
    }

    [Theory]
    [InlineData(400)] [InlineData(403)] [InlineData(409)]
    public async Task DefiniteHttpRejectionsRemainSafeRejections(int status)
    {
        var fixture = new Fixture(MoveItTaskKind.Advanced) { PostFailure = new MoveItApiException("Rejected", (HttpStatusCode)status) };
        var error = await Assert.ThrowsAsync<MoveItApiException>(() => fixture.Create());
        Assert.Equal((HttpStatusCode)status, error.StatusCode); Assert.Equal(new[] { "POST" }, fixture.Calls);
    }

    [Fact]
    public async Task CancellationBeforeDispatchDoesNotPostAndCancellationAfterDispatchIsUncertain()
    {
        var fixture = new Fixture(MoveItTaskKind.Advanced);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Crud.CreateEmptyAsync(fixture.Name, "Advanced", cancellationToken: cancellation.Token));
        Assert.Empty(fixture.Calls);
        fixture.PostFailure = new OperationCanceledException();
        await Assert.ThrowsAsync<TaskV0CreationOutcomeException>(() => fixture.Create());
        Assert.Equal(new[] { "POST" }, fixture.Calls);
    }

    [Fact]
    public async Task AckOnlyResponseStillRequiresReadbackAndAcceptsFalseBooleanState()
    {
        var fixture = new Fixture(MoveItTaskKind.Synchronization); fixture.Response.Remove("object"); fixture.Persisted["Active"] = false;
        await fixture.Create(); Assert.Equal(new[] { "POST", "GET:574205225" }, fixture.Calls);
    }

    [Fact]
    public async Task InvalidInputFailsBeforeAnyPost()
    {
        var fixture = new Fixture(MoveItTaskKind.Advanced);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Crud.CreateEmptyAsync("", "Advanced"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Crud.CreateEmptyAsync(new string('x', 255), "Advanced"));
        Assert.Empty(fixture.Calls);
        Assert.Throws<ArgumentException>(() => new RestV0TaskCrudStrategy(MoveItTaskKind.Traditional, fixture.Rest));
    }

    [Theory]
    [InlineData(MoveItTaskKind.Advanced)] [InlineData(MoveItTaskKind.Synchronization)]
    public async Task NewAdvancedAndSyncTasksCarryTheChosenResourceGroup(MoveItTaskKind kind)
    {
        var fixture = new Fixture(kind);
        await fixture.Crud.CreateEmptyAsync(fixture.Name, kind.ToString(), "Tasks Group");
        Assert.Equal("Tasks Group", fixture.Submitted!["Group"]![0]!.ToString());
        Assert.Equal(new[] { "POST", "GET:574205225" }, fixture.Calls);
    }

    private sealed class Fixture
    {
        public ITaskCrudCoordinator Crud { get; }
        public IMoveItRestClient Rest { get; }
        public string Name { get; }
        public JsonObject Response { get; set; }
        public JsonObject Persisted { get; }
        public JsonObject? Submitted { get; private set; }
        public List<string> Calls { get; } = [];
        public int ComCalls { get; private set; }
        public Exception? PostFailure { get; set; }
        public Exception? ReadFailure { get; set; }
        private readonly MoveItTaskKind _kind;
        public Task<TaskCreationResult> Create() => Crud.CreateEmptyAsync(Name, _kind.ToString());
        public Fixture(MoveItTaskKind kind)
        {
            _kind = kind; Persisted = Capture(kind); Name = Persisted["Name"]!.ToString();
            Persisted["ID"] = "574205225"; Persisted["steps"] = new JsonArray();
            Response = new JsonObject { ["id"] = "574205225", ["status"] = "success", ["object"] = Persisted.DeepClone(), ["errorcode"] = 0, ["refreshRequired"] = false };
            Rest = Stub<IMoveItRestClient>((method, args) => method.Name switch
            {
                nameof(IMoveItRestClient.CreateTaskV0Async) => Post(args),
                nameof(IMoveItRestClient.GetTaskAsync) => Read(args),
                _ => throw new NotSupportedException("Unexpected REST operation: " + method.Name)
            });
            Task<JsonObject> Post(object?[] args)
            {
                Calls.Add("POST"); Submitted = ((JsonObject)args[0]!).DeepClone().AsObject();
                return PostFailure is null ? Task.FromResult(Response.DeepClone().AsObject()) : Task.FromException<JsonObject>(PostFailure);
            }
            Task<JsonObject> Read(object?[] args)
            {
                Calls.Add("GET:" + args[0]);
                return ReadFailure is null ? Task.FromResult(Persisted.DeepClone().AsObject()) : Task.FromException<JsonObject>(ReadFailure);
            }
            var com = Stub<IMoveItComClient>((method, _) => { ComCalls++; throw new InvalidOperationException("COM must not be used: " + method.Name); });
            var traditional = new TraditionalTaskPersistenceService(Rest, com, new TraditionalTaskDefinitionComposer(new TraditionalTaskHostProfileCatalog()));
            Crud = new TaskCrudCoordinator([new TraditionalTaskCrudStrategy(traditional), new RestV0TaskCrudStrategy(MoveItTaskKind.Advanced, Rest),
                new RestV0TaskCrudStrategy(MoveItTaskKind.Synchronization, Rest)], traditional, Rest, com);
        }
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? Path { get; private set; }
        public string? Authorization { get; private set; }
        public string? MediaType { get; private set; }
        public JsonNode? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++; Method = request.Method; Path = request.RequestUri!.AbsolutePath;
            Authorization = request.Headers.Authorization?.ToString(); MediaType = request.Content?.Headers.ContentType?.MediaType;
            Body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"574205225\",\"status\":\"success\",\"errorcode\":0}") };
        }
    }
    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var result = DispatchProxy.Create<T, Proxy>(); ((Proxy)(object)result).Handler = handler; return result;
    }
    public class Proxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args ?? []);
    }
}
