using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Hosts;
using MoveItAutomationUI.Services.MoveIt.Tasks;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class ObjectJsonSearchTests
{
    [Theory]
    [InlineData("Task", """{"ID":"1","TT":"COS","steps":[{"If":{"When":[{"steps":[{"RunTask":{"DoIfFailure":1}}]}]}}]}""")]
    [InlineData("Host", """{"SSHFTP":{"ID":"1","Host":"sftp.example.com","Port":22}}""")]
    [InlineData("Host", """{"ID":"1","Port":21}""")]
    public async Task ReadsThroughSharedObjectLibrariesWithoutEditOrCheckout(string type, string json)
    {
        var (service, task, host, _) = Fixture(json);
        var document = await service.ReadAsync(type, "1");
        Assert.Equal("1", document.Id);
        Assert.NotEmpty(document.Values);
        Assert.Single(type == "Task" ? task.Methods : host.Methods);
        Assert.Equal("GetDefinitionAsync", (type == "Task" ? task.Methods : host.Methods)[0]);
        Assert.Empty(type == "Task" ? host.Methods : task.Methods);
        Assert.Contains(document.Values, value => value.Path[^1] == (type == "Task" ? "DoIfFailure" : "Port"));
    }

    [Fact]
    public void ProjectionOmitsCredentialsSecureParametersAndUnsafeNumbersWithoutMutatingDefinition()
    {
        var definition = JsonNode.Parse("""
            {"ID":"1","Name":"visible","Password":"secret-one","ClientSecret":"secret-two","PrivateKey":"secret-three",
             "AccessToken":"secret-four","Account":"secret-five","Null":null,"Unsafe":9007199254740992,
             "Parameters":[{"Secure":1,"value":"secret-six","DisplayValue":"secret-seven","Name":"secure-name"},
               {"Secure":0,"value":"visible-param"}],"steps":[{"Source":{"FileMask":"*.txt"}}],"Flag":true}
            """)!.AsObject();
        var original = definition.ToJsonString();
        var values = ObjectJsonSearchService.Project(definition);
        var serialized = JsonSerializer.Serialize(values);
        foreach (var secret in new[] { "secret-one", "secret-two", "secret-three", "secret-four", "secret-five", "secret-six", "secret-seven" })
            Assert.DoesNotContain(secret, serialized);
        Assert.DoesNotContain(values, value => value.Path[^1] is "Password" or "ClientSecret" or "PrivateKey" or "AccessToken" or "Account" or "Unsafe");
        Assert.Contains(values, value => value.Value?.ToString() == "visible-param");
        Assert.Contains(values, value => value.Path[^1] == "Null" && value.Value is null);
        Assert.Contains(values, value => value.Path.SequenceEqual(new[] { "steps", "0", "Source", "FileMask" }));
        Assert.Equal(original, definition.ToJsonString());
    }

    [Theory]
    [InlineData("Task", """{"ID":"2"}""")]
    [InlineData("Host", """{"FTP":{"ID":"2"}}""")]
    [InlineData("Host", """{}""")]
    public async Task RejectsDifferentOrMissingObjectIdentity(string type, string json)
    {
        var (service, _, _, _) = Fixture(json);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReadAsync(type, "1"));
    }

    [Fact]
    public async Task RequiresNativeSessionAndPropagatesCancellation()
    {
        var (service, task, host, session) = Fixture("""{"ID":"1"}""");
        session.Authenticated = false;
        await Assert.ThrowsAsync<MoveItAuthenticationException>(() => service.ReadAsync("Task", "1"));
        Assert.Empty(task.Methods); Assert.Empty(host.Methods);
        session.Authenticated = true;
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ReadAsync("Task", "1", cancellation.Token));
    }

    [Theory]
    [InlineData("Resource", "1")]
    [InlineData("Task", "../1")]
    [InlineData("Host", "")]
    public async Task InvalidSelectorsDoNotReadDefinitions(string type, string id)
    {
        var (service, task, host, _) = Fixture("""{"ID":"1"}""");
        var controller = Controller(service);
        Assert.IsType<BadRequestObjectResult>(await controller.Index(type, id, default));
        Assert.Empty(task.Methods); Assert.Empty(host.Methods);
    }

    [Fact]
    public async Task ControllerKeepsErrorsSafeAndReturnsWebJson()
    {
        var (service, task, _, session) = Fixture("""{"ID":"1","Active":1}""");
        var controller = Controller(service);
        var success = Assert.IsType<JsonResult>(await controller.Index("Task", "1", default));
        var json = JsonSerializer.Serialize(success.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"document\":{\"id\":\"1\",\"values\":", json);
        task.Failure = new InvalidOperationException("native response with sensitive values");
        var failure = Assert.IsType<ObjectResult>(await controller.Index("Task", "1", default));
        Assert.Equal(502, failure.StatusCode);
        Assert.DoesNotContain("sensitive values", JsonSerializer.Serialize(failure.Value));
        task.Failure = null; session.Authenticated = false;
        Assert.IsType<UnauthorizedObjectResult>(await controller.Index("Task", "1", default));
        session.Authenticated = true;
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => controller.Index("Task", "1", cancelled.Token));
        Assert.NotNull(typeof(ObjectJsonSearchController).GetCustomAttribute<AuthorizeAttribute>());
        var cache = typeof(ObjectJsonSearchController).GetCustomAttribute<ResponseCacheAttribute>()!;
        Assert.True(cache.NoStore); Assert.Equal(ResponseCacheLocation.None, cache.Location);
        Assert.NotNull(typeof(ObjectJsonSearchController).GetMethod("Index")!.GetCustomAttribute<HttpGetAttribute>());
    }

    [Fact]
    public void OversizedProjectionAndCancelledTraversalDoNotReturnPartialResults()
    {
        Assert.Throws<InvalidOperationException>(() => ObjectJsonSearchService.Project(new JsonObject { ["Name"] = new string('x', TaskJsonValues.MaximumDefinitionBytes) }));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => ObjectJsonSearchService.Project(new JsonObject(), cancelled.Token));
    }

    private static ObjectJsonSearchController Controller(IObjectJsonSearchService service)
        => new(service, NullLogger<ObjectJsonSearchController>.Instance);

    private static (ObjectJsonSearchService Service, DefinitionProxy Task, DefinitionProxy Host, TaskFilenameSearchTests.SessionProxy Session) Fixture(string json)
    {
        var tasks = DispatchProxy.Create<ITaskCrudCoordinator, DefinitionProxy>();
        var hosts = DispatchProxy.Create<IHostCrudCoordinator, DefinitionProxy>();
        var session = DispatchProxy.Create<IMoveItSessionService, TaskFilenameSearchTests.SessionProxy>();
        var task = (DefinitionProxy)(object)tasks; var host = (DefinitionProxy)(object)hosts;
        task.Definition = JsonNode.Parse(json)!.AsObject(); host.Definition = task.Definition;
        return (new(tasks, hosts, session), task, host, (TaskFilenameSearchTests.SessionProxy)(object)session);
    }

    public class DefinitionProxy : DispatchProxy
    {
        public JsonObject Definition { get; set; } = new();
        public List<string> Methods { get; } = [];
        public Exception? Failure { get; set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (args?[^1] is CancellationToken cancellation) cancellation.ThrowIfCancellationRequested();
            if (Failure is { } failure) throw failure;
            Methods.Add(method!.Name);
            if (method.Name != "GetDefinitionAsync") throw new NotSupportedException(method.Name);
            Assert.Equal("1", args![0]);
            return Task.FromResult(Definition);
        }
    }
}
