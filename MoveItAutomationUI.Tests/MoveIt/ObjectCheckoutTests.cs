using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Tasks;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;
using Xunit;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class ObjectCheckoutTests
{
    [Theory]
    [InlineData(MoveItTaskKind.Traditional)]
    [InlineData(MoveItTaskKind.Synchronization)]
    [InlineData(MoveItTaskKind.Advanced)]
    public async Task SaveOwnsCheckoutUntilVerifiedReadback(MoveItTaskKind kind)
    {
        var events = new List<string>();
        var task = new JsonObject { ["ID"] = "1", ["TT"] = kind switch { MoveItTaskKind.Traditional => "Tr", MoveItTaskKind.Advanced => "COS", _ => "Sy" }, ["Name"] = "Old" };
        var rest = Stub<IMoveItRestClient>((method, args) =>
        {
            events.Add(method.Name);
            switch (method.Name)
            {
                case nameof(IMoveItRestClient.CheckoutTaskV0Async):
                case nameof(IMoveItRestClient.ReleaseTaskCheckoutV0Async): return Task.CompletedTask;
                case nameof(IMoveItRestClient.GetTaskAsync): return Task.FromResult(task.DeepClone().AsObject());
                case nameof(IMoveItRestClient.UpdateTaskAsync): task = ((JsonObject)args[1]!).DeepClone().AsObject(); return Task.CompletedTask;
                case nameof(IMoveItRestClient.UpdateTaskV0Async):
                    task = ((JsonObject)args[1]!).DeepClone().AsObject();
                    return Task.FromResult(new JsonObject { ["id"] = "1", ["status"] = "success", ["errorcode"] = 0 });
                default: throw new NotSupportedException(method.Name);
            }
        });
        await new TaskJsonPersistenceService(rest).UpdateFreshAsync("1", kind, value => { value["Name"] = "New"; return value; },
            saved => Assert.Equal("New", saved["Name"]!.ToString()), true);
        Assert.Equal(new[] { nameof(IMoveItRestClient.CheckoutTaskV0Async), nameof(IMoveItRestClient.GetTaskAsync),
            kind == MoveItTaskKind.Traditional ? nameof(IMoveItRestClient.UpdateTaskAsync) : nameof(IMoveItRestClient.UpdateTaskV0Async),
            nameof(IMoveItRestClient.GetTaskAsync), nameof(IMoveItRestClient.ReleaseTaskCheckoutV0Async) }, events);
    }

    [Theory]
    [InlineData(MoveItTaskKind.Traditional)]
    [InlineData(MoveItTaskKind.Synchronization)]
    [InlineData(MoveItTaskKind.Advanced)]
    public async Task ReadOnlyStatusChangesUnderCheckoutAndReleaseDoNotInvalidateEditorRevision(MoveItTaskKind kind)
    {
        var task = new JsonObject { ["ID"] = "1", ["TT"] = kind switch { MoveItTaskKind.Traditional => "", MoveItTaskKind.Advanced => "COS", _ => "Sy" }, ["Name"] = "Old", ["Active"] = 0 };
        var checkedOut = false; var writes = 0; var releases = 0;
        JsonObject Read()
        {
            var copy = task.DeepClone().AsObject();
            copy["xRun"] = !checkedOut; copy["xAlter"] = !checkedOut;
            copy["xAddEditDelete"] = !checkedOut; copy["Renamed"] = checkedOut;
            copy["Scheduled"] = checkedOut ? "DISABLED" : "UNSCHEDULED";
            return copy;
        }
        string Xml() => $"<Task ID=\"1\" Name=\"{task["Name"]}\" />";
        var rest = Stub<IMoveItRestClient>((method, args) => method.Name switch
        {
            nameof(IMoveItRestClient.CheckoutTaskV0Async) => Acquire(),
            nameof(IMoveItRestClient.ReleaseTaskCheckoutV0Async) => Release(),
            nameof(IMoveItRestClient.GetTaskAsync) => Task.FromResult(Read()),
            nameof(IMoveItRestClient.UpdateTaskAsync) => Write((JsonObject)args[1]!),
            nameof(IMoveItRestClient.UpdateTaskV0Async) => WriteV0((JsonObject)args[1]!),
            _ => throw new NotSupportedException(method.Name)
        });
        Task Acquire() { checkedOut = true; return Task.CompletedTask; }
        Task Release() { checkedOut = false; releases++; return Task.CompletedTask; }
        Task Write(JsonObject value) { task = value.DeepClone().AsObject(); writes++; return Task.CompletedTask; }
        async Task<JsonObject> WriteV0(JsonObject value) { await Write(value); return new() { ["id"]="1", ["status"]="success", ["errorcode"]=0 }; }
        var com = Stub<IMoveItComClient>((_, _) => Task.FromResult(new MoveItTaskConfiguration("1",task["Name"]!.ToString(),Xml())));
        var revision = ObjectEditVersion.Create("Task", "1", ObjectEditVersion.Definition(Read(), Xml()));
        var persistence = new TaskJsonPersistenceService(rest, com);
        foreach (var name in new[] { "First edit", "Second edit" })
        {
            using var scope = ObjectEditVersion.Require("Task", "1", revision);
            await persistence.UpdateFreshAsync("1", kind, current => { current["Name"] = name; return current; }, requireReadback:true);
            revision = ObjectEditVersion.SavedVersion!;
            Assert.False(string.IsNullOrEmpty(revision));
        }
        Assert.Equal(2, writes); Assert.Equal(2, releases); Assert.False(checkedOut);
    }

    [Fact]
    public void BranchAnnotationsAreExcludedWithoutIgnoringRealTaskFieldsOrParameterNames()
    {
        var task = JsonNode.Parse("""{"ID":"1","steps":[{"If":{"ID":"2","When":[{"__parentStepId":"2","__branchIndex":0,"Criteria":{"comp":{"a":"[TaskStatus]","b":"Success","test":"MASK"}},"steps":[]}]}}],"Parameters":{"__parentStepId":{"value":"real value"}}}""")!.AsObject();
        const string xml = "<Task ID=\"1\" />";
        var revision = ObjectEditVersion.Create("Task", "1", ObjectEditVersion.Definition(task, xml));
        task["steps"]![0]!["If"]!["When"]![0]!["__parentStepId"] = "";
        task["steps"]![0]!["If"]!["When"]![0]!["__branchIndex"] = 5;
        Assert.Equal(revision, ObjectEditVersion.Create("Task", "1", ObjectEditVersion.Definition(task, xml)));
        task["Parameters"]!["__parentStepId"]!["value"] = "changed value";
        Assert.NotEqual(revision, ObjectEditVersion.Create("Task", "1", ObjectEditVersion.Definition(task, xml)));
    }

    [Fact]
    public async Task FailedCheckoutNeverWritesOrReleasesAnotherOwnersCheckout()
    {
        var calls = 0;
        var rest = Stub<IMoveItRestClient>((method, _) =>
        {
            calls++;
            if (method.Name == nameof(IMoveItRestClient.CheckoutTaskV0Async)) throw new InvalidOperationException("Checked out by someone else");
            throw new Exception("Should not reach " + method.Name);
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => new TaskJsonPersistenceService(rest)
            .UpdateFreshAsync("1", MoveItTaskKind.Traditional, task => task));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleJsonOrSuppressedNativeCredentialChangesAreRejectedBeforePut(bool credentialChange)
    {
        var initial = new JsonObject { ["ID"] = "1", ["TT"] = "Tr", ["Name"] = "Old" };
        const string initialXml = "<Task ID=\"1\" Name=\"Old\"><Source DefPassword=\"encrypted-old\" /></Task>";
        var current = initial.DeepClone().AsObject();
        if (!credentialChange) current["Name"] = "Someone else's change";
        var released = 0;
        var rest = Stub<IMoveItRestClient>((method, _) => method.Name switch
        {
            nameof(IMoveItRestClient.CheckoutTaskV0Async) => Task.CompletedTask,
            nameof(IMoveItRestClient.ReleaseTaskCheckoutV0Async) => Release(),
            nameof(IMoveItRestClient.GetTaskAsync) => Task.FromResult(current),
            _ => throw new Exception("No PUT is allowed: " + method.Name)
        });
        Task Release() { released++; return Task.CompletedTask; }
        var com = Stub<IMoveItComClient>((_, _) => Task.FromResult(new MoveItTaskConfiguration("1", "Old",
            credentialChange ? initialXml.Replace("encrypted-old", "encrypted-new") : initialXml)));
        using var scope = ObjectEditVersion.Require("Task", "1", ObjectEditVersion.Create("Task", "1", ObjectEditVersion.Definition(initial, initialXml)));
        await Assert.ThrowsAsync<ObjectEditConflictException>(() => new TaskJsonPersistenceService(rest, com)
            .UpdateFreshAsync("1", MoveItTaskKind.Traditional, value => value));
        Assert.Equal(1, released);
    }

    [Fact]
    public async Task CancellationStillReleasesWithIndependentCleanupToken()
    {
        using var request = new CancellationTokenSource();
        var released = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ObjectCheckout.ExecuteAsync("Task", "1",
            _ => Task.CompletedTask, ct => { request.Cancel(); ct.ThrowIfCancellationRequested(); return Task.FromResult(true); },
            ct => { Assert.False(ct.IsCancellationRequested); released = true; return Task.CompletedTask; }, request.Token));
        Assert.True(released);
    }

    [Fact]
    public async Task ReleaseFailureCannotBeReportedAsSuccessfulSave()
    {
        await Assert.ThrowsAsync<ObjectCheckoutReleaseException>(() => ObjectCheckout.ExecuteAsync("Task", "1",
            _ => Task.CompletedTask, _ => Task.FromResult(true), _ => throw new HttpRequestException("Lost release"), CancellationToken.None));
    }

    // Exercise the real REST response parser, not a mocked release callback.
    [Theory]
    [InlineData(204, "")]
    [InlineData(200, "{\"status\":\"success\",\"errorcode\":0}")]
    [InlineData(200, "{\"status\":\"error\",\"errorcode\":4015}")]
    [InlineData(409, "{\"status\":\"error\",\"errorcode\":\"4015\"}")]
    [InlineData(400, "{\"errorCode\":4015}")]
    [InlineData(500, "{\"ErrorCode\":4015}")]
    public async Task RestReleaseAcceptsSuccessOrExplicitAlreadyUnlockedOnly(int status, string body)
    {
        using var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) };
        await ParseRelease(response);
    }

    [Theory]
    [InlineData(409, "{\"errorcode\":4010}")]
    [InlineData(200, "{\"status\":\"error\",\"errorcode\":4010}")]
    [InlineData(200, "{\"status\":\"error\"}")]
    [InlineData(409, "{\"id\":4015,\"message\":\"Task 4015 is locked\"}")]
    [InlineData(500, "upstream disconnected")]
    [InlineData(403, "{\"errorcode\":4015}")]
    public async Task RestReleasePreservesRealFailuresWithoutEchoingResponseSecrets(int status, string body)
    {
        using var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body) };
        var exception = await Assert.ThrowsAsync<MoveItApiException>(() => ParseRelease(response));
        Assert.Equal((HttpStatusCode)status, exception.StatusCode);
        Assert.DoesNotContain(body, exception.Message);
    }

    [Fact]
    public async Task AlreadyUnlockedDoesNotHideAnEditFailureOrWeakenAcquisition()
    {
        var editFailure = new InvalidOperationException("Readback mismatch");
        var caught = await Assert.ThrowsAsync<InvalidOperationException>(() => ObjectCheckout.ExecuteAsync("Task", "1",
            _ => Task.CompletedTask, _ => Task.FromException<bool>(editFailure), async ct =>
            {
                using var response = new HttpResponseMessage(HttpStatusCode.Conflict)
                    { Content = new StringContent("{\"errorcode\":4015}") };
                await ParseRelease(response);
            }, CancellationToken.None));
        Assert.Same(editFailure, caught);
        // The same native response is still an error outside the release endpoint.
        using var acquireResponse = new HttpResponseMessage(HttpStatusCode.Conflict)
            { Content = new StringContent("{\"errorcode\":4015}") };
        await Assert.ThrowsAsync<MoveItApiException>(() => ParseRelease(acquireResponse, false));
    }

    private static Task<string> ParseRelease(HttpResponseMessage response, bool release = true)
        => (Task<string>)typeof(MoveItRestClient).GetMethod("ParseTextResponseAsync", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [response, CancellationToken.None, release])!;

    [Fact]
    public async Task SameObjectRequestsAreSerializedEvenWhenNativeCheckoutIsReentrant()
    {
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = false;
        var first = ObjectCheckout.ExecuteAsync("Host", "same-session", _ => Task.CompletedTask,
            async _ => { firstEntered.SetResult(); await finishFirst.Task; return true; }, _ => Task.CompletedTask, CancellationToken.None);
        await firstEntered.Task;
        var second = ObjectCheckout.ExecuteAsync("Host", "same-session", _ => { secondEntered = true; return Task.CompletedTask; },
            _ => Task.FromResult(true), _ => Task.CompletedTask, CancellationToken.None);
        Assert.False(secondEntered);
        finishFirst.SetResult();
        await Task.WhenAll(first, second);
        Assert.True(secondEntered);
    }

    [Fact]
    public void NativeScheduleFollowupRejectsConcurrentScheduleAndCredentialChanges()
    {
        var task = new JsonObject { ["ID"] = "1", ["Name"] = "Task" };
        const string xml = "<Task ID=\"1\"><Schedules><Schedule ID=\"2\" /></Schedules><Source DefPassword=\"old\" /></Task>";
        var definition = ObjectEditVersion.Definition(task, xml);
        using var scope = ObjectEditVersion.Require("Task", "1", ObjectEditVersion.Create("Task", "1", definition));
        ObjectEditVersion.Verify("Task", "1", definition);
        ObjectEditVersion.Persisted("Task", "1", definition);
        Assert.Throws<ObjectEditConflictException>(() => ObjectEditVersion.VerifyNativeSchedules(xml.Replace("ID=\"2\"", "ID=\"3\"")));
        Assert.Throws<ObjectEditConflictException>(() => ObjectEditVersion.CompleteNativeSchedules("1", task, xml.Replace("DefPassword=\"old\"", "DefPassword=\"new\"")));
        ObjectEditVersion.CompleteNativeSchedules("1", task, xml.Replace("ID=\"2\"", "ID=\"3\""));
        Assert.NotNull(ObjectEditVersion.SavedVersion);
    }

    [Fact]
    public void RevisionIgnoresPropertyAndAttributeOrderingButPreservesContentAndObjectIdentity()
    {
        var first = ObjectEditVersion.Definition(JsonNode.Parse("{\"a\":1,\"b\":2}")!, "<Task ID=\"1\" Name=\"X\"><Info>note</Info></Task>");
        var reordered = ObjectEditVersion.Definition(JsonNode.Parse("{\"b\":2,\"a\":1}")!, "<Task Name=\"X\" ID=\"1\">\n <Info>note</Info>\n</Task>");
        Assert.Equal(ObjectEditVersion.Create("Task", "1", first), ObjectEditVersion.Create("Task", "1", reordered));
        Assert.NotEqual(ObjectEditVersion.Create("Task", "1", first), ObjectEditVersion.Create("Task", "2", first));
        Assert.Throws<ObjectEditConflictException>(() => ObjectEditVersion.Require("Task", "1", ""));
    }

    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, Proxy>(); ((Proxy)(object)proxy).Handler = handler; return proxy;
    }
    public class Proxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args ?? []);
    }
}
