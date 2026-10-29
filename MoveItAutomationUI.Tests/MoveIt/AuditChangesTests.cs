using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Features.AuditChanges;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class AuditChangesTests
{
    private static JsonObject Capture()
    {
        using var stream = typeof(AuditChangesTests).Assembly.GetManifestResourceStream("MoveItAutomationUI.Tests.MoveIt.Fixtures.AuditChangeCapture.json")!;
        using var reader = new StreamReader(stream); return JsonNode.Parse(reader.ReadToEnd())!.AsObject();
    }

    [Fact]
    public void SuppliedNativeCaptureShowsThreeRemovedAndOneAddedLines()
    {
        var record = Capture()["Record"]!;
        var result = AuditChangeComparer.Compare(new(record["PreChange"]!.GetValue<string>(), record["PostChange"]!.GetValue<string>()));
        Assert.True(result.Added == 1 && result.Removed == 3);
        Assert.Contains(result.Lines, line => line.Kind == "removed" && line.Text.Contains("<Group>GroupA</Group>"));
        Assert.Contains(result.Lines, line => line.Kind == "removed" && line.Text.Contains("FileMask=\"*.*\""));
        Assert.Contains(result.Lines, line => line.Kind == "added" && line.Text.Contains("FileMask=\"*.csv\""));
        Assert.False(result.Grouped); Assert.False(result.PlainText);
        Assert.All(result.Lines, line => {
            Assert.Equal(line.Kind != "added", line.Before.HasValue);
            Assert.Equal(line.Kind != "removed", line.After.HasValue);
        });
    }

    [Fact]
    public void AttributeOrderAndStructuralIndentationDoNotCreateFalseChanges()
    {
        var result = AuditChangeComparer.Compare(new("<Task B=\"2\" A=\"1\"> <Info><Notes>one</Notes></Info> </Task>",
            "<Task A=\"1\" B=\"2\">\n  <Info>\n<Notes>one</Notes>\n</Info>\n</Task>"));
        Assert.True(result.Added == 0 && result.Removed == 0);
        Assert.Contains(result.Lines, line => line.Text.Contains("A=\"1\""));
    }

    [Fact]
    public void LeafWhitespaceChildOrderAndProtectedKeywordsRemainSignificant()
    {
        var result = AuditChangeComparer.Compare(new("<Task><Notes> </Notes><Source ID=\"1\"/><Source ID=\"2\"/><Password>ENCRYPTED_OLD</Password></Task>",
            "<Task><Notes>  </Notes><Source ID=\"2\"/><Source ID=\"1\"/><Password>ENCRYPTED_NEW</Password></Task>"));
        Assert.True(result.Added > 0 && result.Removed > 0);
        Assert.Contains(result.Lines, line => line.Kind == "removed" && line.Text.Contains("<Notes> </Notes>"));
        Assert.Contains(result.Lines, line => line.Kind == "added" && line.Text.Contains("ENCRYPTED_NEW"));
    }

    [Theory]
    [InlineData("", "<Task />", "added")]
    [InlineData("<Task />", "", "removed")]
    [InlineData("", "", "none")]
    public void CreatesDeletesAndEmptySnapshotsAreSupported(string before, string after, string expected)
    {
        var result = AuditChangeComparer.Compare(new(before, after));
        if (expected == "none") Assert.Empty(result.Lines);
        else Assert.All(result.Lines, line => Assert.Equal(expected, line.Kind));
    }

    [Fact]
    public void DtdAndMalformedXmlArePlainTextAndNeverResolveExternalEntities()
    {
        var input = "<!DOCTYPE Task [<!ENTITY external SYSTEM 'file:///must-not-be-read'>]><Task>&external;</Task>";
        var result = AuditChangeComparer.Compare(new(input, "<Task A='1'>"));
        Assert.True(result.PlainText);
        Assert.Contains(result.Lines, line => line.Text == input);
        Assert.Contains(result.Lines, line => line.Text == "<Task A='1'>");
    }

    [Fact]
    public void LargeChangesUseBoundedGroupingAndPreserveEveryLine()
    {
        var before = string.Join('\n', Enumerable.Range(0, 2500).Select(index => "old " + index));
        var after = string.Join('\n', Enumerable.Range(0, 2500).Select(index => "new " + index));
        var result = AuditChangeComparer.Compare(new(before, after));
        Assert.True(result.Grouped && result.PlainText);
        Assert.True(result.Added == 2500 && result.Removed == 2500);
        Assert.Equal(before, string.Join('\n', result.Lines.Where(line => line.Before.HasValue).Select(line => line.Text)));
        Assert.Equal(after, string.Join('\n', result.Lines.Where(line => line.After.HasValue).Select(line => line.Text)));
    }

    [Fact]
    public void OversizedOrOvernestedSnapshotsAreRejectedAndCancellationIsHonored()
    {
        Assert.Throws<InvalidOperationException>(() => AuditChangeComparer.Compare(new(new string('x', 1_048_577), "")));
        Assert.Throws<InvalidOperationException>(() => AuditChangeComparer.Compare(new(string.Concat(Enumerable.Repeat("<T>", 70)) + string.Concat(Enumerable.Repeat("</T>", 70)), "")));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => AuditChangeComparer.Compare(new("same", "same"), cancel.Token));
    }

    [Theory]
    [InlineData("2026.0", "1", true)]
    [InlineData("18.0.1", "true", true)]
    [InlineData("2027.0", "0", false)]
    [InlineData("2026.0", "false", false)]
    [InlineData("2026.0", "null", false)]
    [InlineData("2026.0", "\"1\"", true)]
    [InlineData("2025.1", "1", false)]
    [InlineData("", "1", false)]
    public async Task NativeRowFlagAndVersionControlAvailabilityWithoutComparisonRequests(string version, string flag, bool expected)
    {
        var calls = new List<string>();
        var rest = Stub<IMoveItRestClient>((method, _) => {
            calls.Add(method.Name);
            return method.Name == nameof(IMoveItRestClient.GetAuditReportAsync)
                ? Task.FromResult<IReadOnlyList<JsonObject>>(new[] { JsonNode.Parse("{\"LogID\":481,\"TargetType\":\"task\",\"TargetID\":806566301,\"TargetName\":\"adv1\",\"HasAuditDiff\":" + flag + "}")!.AsObject() })
                : throw new NotSupportedException(method.Name);
        });
        var service = new AutomationReportService(rest, Sessions(State(version)));
        var batch = await service.GetAuditAsync(null, null, 100, null, 100);
        var row = Assert.Single(batch.Items); Assert.Equal(expected, row.HasAuditDiff);
        Assert.Equal(new[] { nameof(IMoveItRestClient.GetAuditReportAsync) }, calls);
        var controller = new RecordsController(service, new Snapshot<UiOptions>(new()), new Snapshot<GridBehaviorOptions>(new()),
            new Snapshot<ReportBehaviorOptions>(new()), NullLogger<RecordsController>.Instance);
        var grid = (GridViewModel)typeof(RecordsController).GetMethod("BuildAuditGrid", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(controller, new object[] { batch.Items, 1, false, 100 })!;
        Assert.Equal(expected ? "1" : "0", Assert.Single(grid.Rows).Values["HasAuditDiff"]);
        Assert.DoesNotContain(grid.Columns, column => column.Key == "HasAuditDiff");
    }

    [Theory]
    [InlineData("2026.0", true)]
    [InlineData("18.0.1", true)]
    [InlineData("2025.1", false)]
    [InlineData("", false)]
    public async Task RestUsesCapturedGetAndGatesOlderServersBeforeAnyRequest(string version, bool allowed)
    {
        using var handler = new NativeHandler(Capture().ToJsonString());
        using var factory = new Factory(handler);
        var rest = new MoveItRestClient(factory, Sessions(State(version)), ConfigurationStartupState.Capture(new ConfigurationBuilder().Build()), null!, NullLogger<MoveItRestClient>.Instance);
        if (allowed)
        {
            var result = await rest.GetAuditChangesAsync(481);
            Assert.Equal(Capture()["Record"]!["PreChange"]!.GetValue<string>(), result.PreChange);
            Assert.Equal(new[] { "GET /api/v0/reports/audit/diff/481" }, handler.Requests);
        }
        else { await Assert.ThrowsAsync<InvalidOperationException>(() => rest.GetAuditChangesAsync(481)); Assert.Empty(handler.Requests); }
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => rest.GetAuditChangesAsync(0));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Record\":{}}")]
    [InlineData("{\"Record\":{\"PreChange\":12}}")]
    public async Task InvalidNativeResponseDoesNotMasqueradeAsNoDifferences(string body)
    {
        using var handler = new NativeHandler(body); using var factory = new Factory(handler);
        var rest = new MoveItRestClient(factory, Sessions(State("2026.0")), ConfigurationStartupState.Capture(new ConfigurationBuilder().Build()), null!, NullLogger<MoveItRestClient>.Instance);
        await Assert.ThrowsAsync<MoveItApiException>(() => rest.GetAuditChangesAsync(481));
    }

    [Theory]
    [InlineData(403, "permission")]
    [InlineData(404, "no longer available")]
    [InlineData(500, "could not be loaded")]
    public async Task ControllerKeepsErrorsSafeAndLogsNoSnapshotContent(int nativeStatus, string message)
    {
        var logger = new SafeLogger();
        var rest = Stub<IMoveItRestClient>((_, _) => throw new MoveItApiException("PRIVATE_CONFIG_MUST_NOT_BE_LOGGED", (HttpStatusCode)nativeStatus));
        var controller = new AuditChangesController(rest, Sessions(State("2026.0")), logger);
        var result = Assert.IsAssignableFrom<ObjectResult>(await controller.Details(481, default));
        Assert.Equal(nativeStatus is 403 or 404 ? nativeStatus : 502, result.StatusCode);
        Assert.Contains(message, System.Text.Json.JsonSerializer.Serialize(result.Value));
        Assert.DoesNotContain("PRIVATE_CONFIG", string.Join('\n', logger.Messages));
        Assert.True(logger.Messages.Count == 1);
        Assert.NotNull(typeof(AuditChangesController).GetCustomAttribute<AuthorizeAttribute>());
        var action = typeof(AuditChangesController).GetMethod(nameof(AuditChangesController.Details))!;
        Assert.NotNull(action.GetCustomAttribute<HttpGetAttribute>());
        Assert.True(action.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore);
    }

    [Fact]
    public async Task ControllerRejectsInvalidIdsOldVersionsAndExpiredSessionsWithoutReadingNativeData()
    {
        var rest = Stub<IMoveItRestClient>((_, _) => throw new InvalidOperationException("Must not read"));
        var old = new AuditChangesController(rest, Sessions(State("2025.1")), NullLogger<AuditChangesController>.Instance);
        Assert.IsType<BadRequestObjectResult>(await old.Details(481, default));
        var current = new AuditChangesController(rest, Sessions(State("2026.0")), NullLogger<AuditChangesController>.Instance);
        Assert.IsType<BadRequestObjectResult>(await current.Details(-1, default));
        var expired = new AuditChangesController(rest, Sessions(null), NullLogger<AuditChangesController>.Instance);
        Assert.IsType<UnauthorizedObjectResult>(await expired.Details(481, default));
    }

    [Fact]
    public async Task ControllerReturnsMergedLinesWithoutAWriteOrCheckout()
    {
        var calls = new List<string>();
        var capture = Capture()["Record"]!;
        var rest = Stub<IMoveItRestClient>((method, _) => {
            calls.Add(method.Name);
            return method.Name == nameof(IMoveItRestClient.GetAuditChangesAsync)
                ? Task.FromResult(new AuditChangeRecord(capture["PreChange"]!.GetValue<string>(), capture["PostChange"]!.GetValue<string>()))
                : throw new NotSupportedException(method.Name);
        });
        var controller = new AuditChangesController(rest, Sessions(State("2026.0")), NullLogger<AuditChangesController>.Instance);
        var result = Assert.IsType<JsonResult>(await controller.Details(481, default));
        var comparison = Assert.IsType<AuditChangeComparison>(result.Value);
        Assert.True(comparison.Added == 1 && comparison.Removed == 3);
        Assert.Equal(new[] { nameof(IMoveItRestClient.GetAuditChangesAsync) }, calls);
    }

    private static MoveItSessionState State(string version) => new() { WebAdminUrl = "https://audit.test", AccessToken = "test-token",
        AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddHours(1), Info = new MoveItInfo { CentralVersion = version } };
    private static IMoveItSessionService Sessions(MoveItSessionState? state) => Stub<IMoveItSessionService>((method, _) => method.Name == nameof(IMoveItSessionService.Get) ? state : throw new NotSupportedException(method.Name));
    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, HostSharedCrudTests.CrudProxy>(); ((HostSharedCrudTests.CrudProxy)(object)proxy).Handler = handler; return proxy;
    }
    private sealed class NativeHandler(string response) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Null(request.Content); Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Requests.Add(request.Method + " " + request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response) });
        }
    }
    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory, IDisposable
    {
        private readonly List<HttpClient> _clients = [];
        public HttpClient CreateClient(string name) { var client = new HttpClient(handler, false); _clients.Add(client); return client; }
        public void Dispose() { foreach (var client in _clients) client.Dispose(); }
    }
    private sealed class Snapshot<T>(T value) : IOptionsSnapshot<T> where T : class
    { public T Value => value; public T Get(string? name) => value; }
    private sealed class SafeLogger : ILogger<AuditChangesController>
    {
        public List<string> Messages { get; } = [];
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}
