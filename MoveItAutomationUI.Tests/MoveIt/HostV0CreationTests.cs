using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Tests.MoveIt;

// Exact supplied POST captures and mocked native transports; no live MOVEit writes.
public sealed class HostV0CreationTests
{
    public static IEnumerable<object[]> Types => new[] { "Share", "siLock", "FTP", "SSHFTP", "POP3", "SMTP", "S3", "AzureBlob", "SharePoint", "GoogleStorage" }.Select(type => new object[] { type });
    private const string ResourceUnc = @"\\server\share";
    private static JsonObject Capture(string type)
    {
        using var stream = typeof(HostV0CreationTests).Assembly.GetManifestResourceStream("MoveItAutomationUI.Tests.MoveIt.Fixtures.HostV0CreationCaptures.json")!;
        var capture = new JsonObject { [type] = JsonNode.Parse(stream)![type]!.DeepClone() };
        // The supplied Share shell was blank; New Host now requires its connection.
        if (type == "Share") capture[type]!["UNC"] = ResourceUnc;
        if (type == "SSHFTP")
        {
            var algorithms = HostSshAlgorithmTests.Catalog();
            capture[type]!["EncList"] = HostSshAlgorithms.DefaultSelection(algorithms.Encryption);
            capture[type]!["KexAlgs"] = HostSshAlgorithms.DefaultSelection(algorithms.KeyExchange);
        }
        return capture;
    }

    [Theory] [MemberData(nameof(Types))]
    public async Task NewHostPostsExactCapturedDefaultsAndVerifiesIdentityWithoutCom(string type)
    {
        var fixture = new Fixture(type);
        var result = await fixture.Crud.CreateShellAsync(" " + fixture.Name + " ", " " + type.ToLowerInvariant() + " ", unc: " " + ResourceUnc + " ");
        Assert.Equal("374281533", result.Id); Assert.Equal(fixture.Name, result.Name); Assert.Equal(type, result.HostTypeCode);
        Assert.False(string.IsNullOrWhiteSpace(result.HostType));
        Assert.True(JsonNode.DeepEquals(Capture(type), fixture.Submitted));
        Assert.Equal(new[] { "POST", "GET:374281533" }, fixture.Calls); Assert.Equal(0, fixture.ComCalls);
        Assert.Equal(type == "SSHFTP" ? 2 : 0, fixture.Discoveries.Count);
        // A second explicit request starts from an independent seed, never from the first submitted object.
        fixture.Submitted![type]!["Port"] = 9999;
        await fixture.Create();
        Assert.True(JsonNode.DeepEquals(Capture(type), fixture.Submitted));
    }

    [Theory] [MemberData(nameof(Types))]
    public async Task HostCreationCarriesTheSelectedResourceGroupForEveryHostType(string type)
    {
        var fixture = new Fixture(type);
        await fixture.Crud.CreateShellAsync(fixture.Name, type, unc: ResourceUnc, resourceGroup: "Host Group");
        Assert.Equal("Host Group", fixture.Submitted![type]!["Group"]![0]!.ToString());
    }

    [Theory] [MemberData(nameof(Types))]
    public async Task ActualClientPostsTypedEnvelopeToV0WithBearerAndJsonWithoutMutation(string type)
    {
        var definition = Capture(type); var before = definition.DeepClone();
        using var handler = new CaptureHandler();
        var session = Stub<IMoveItSessionService>((method, _) => method.Name == nameof(IMoveItSessionService.Get)
            ? new MoveItSessionState { WebAdminUrl = "https://moveit.test", AccessToken = "test-token", AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) }
            : throw new NotSupportedException(method.Name));
        var client = new MoveItRestClient(new ClientFactory(handler), session,
            ConfigurationStartupState.Capture(new ConfigurationBuilder().Build()), null!, NullLogger<MoveItRestClient>.Instance);
        await client.CreateHostV0Async(definition);
        Assert.Equal(1, handler.Requests); Assert.Equal(HttpMethod.Post, handler.Method); Assert.Equal("/api/v0/hosts", handler.Path);
        Assert.Equal("Bearer test-token", handler.Authorization); Assert.Equal("application/json", handler.MediaType);
        Assert.True(JsonNode.DeepEquals(before, handler.Body)); Assert.True(JsonNode.DeepEquals(before, definition));
    }

    [Theory]
    [InlineData("empty")] [InlineData("missing-id")] [InlineData("zero-id")] [InlineData("negative-id")]
    [InlineData("status")] [InlineData("missing-code")] [InlineData("errorcode")]
    public async Task InvalidAcknowledgementNeverRetriesDeletesOrFallsBack(string failure)
    {
        var fixture = new Fixture("SSHFTP");
        switch (failure)
        {
            case "empty": fixture.Response = new JsonObject(); break;
            case "missing-id": fixture.Response.Remove("id"); break;
            case "zero-id": fixture.Response["id"] = "0"; break;
            case "negative-id": fixture.Response["id"] = "-1"; break;
            case "status": fixture.Response["status"] = "error"; break;
            case "missing-code": fixture.Response.Remove("errorcode"); break;
            case "errorcode": fixture.Response["errorcode"] = 53; break;
        }
        var error = await Assert.ThrowsAsync<HostV0CreationOutcomeException>(fixture.Create);
        Assert.Equal(failure is "empty" or "missing-id" or "zero-id" or "negative-id" ? null : "374281533", error.HostId);
        Assert.Equal(new[] { "POST" }, fixture.Calls); Assert.Equal(0, fixture.ComCalls);
        Assert.Equal("acknowledgement", error.VerificationStage); Assert.Equal("invalid-acknowledgement", error.VerificationReason);
    }

    [Theory]
    [InlineData("readback")] [InlineData("id")] [InlineData("name")] [InlineData("type")]
    public async Task FailedReadbackPreservesKnownIdAndNeverReplaysOrDeletes(string failure)
    {
        var fixture = new Fixture("Share");
        if (failure == "readback") fixture.ReadFailure = new HttpRequestException("Disconnected");
        else if (failure == "type") fixture.Persisted = Capture("SMTP");
        else fixture.Persisted["Share"]![failure == "id" ? "ID" : "Name"] = "Other";
        var error = await Assert.ThrowsAsync<HostV0CreationOutcomeException>(fixture.Create);
        Assert.Equal("374281533", error.HostId); Assert.Equal(fixture.Name, error.HostName);
        Assert.Equal(failure == "readback" ? UnavailableReadCalls() : new[] { "POST", "GET:374281533" }, fixture.Calls); Assert.Equal(0, fixture.ComCalls);
        Assert.Equal("readback", error.VerificationStage); Assert.False(string.IsNullOrWhiteSpace(error.VerificationReason));
    }

    [Fact]
    public async Task SftpDiscoveryFailureIsSafeBeforeCreationWithoutFallback()
    {
        var failure = new MoveItApiException("Unavailable", HttpStatusCode.Forbidden);
        var fixture = new Fixture("SSHFTP") { DiscoveryFailure = failure };
        Assert.Same(failure, await Assert.ThrowsAsync<MoveItApiException>(fixture.Create));
        Assert.Equal(0, fixture.Posts); Assert.Equal(0, fixture.ComCalls); Assert.Empty(fixture.Calls);
    }

    [Fact]
    public async Task NewSftpHostUsesConfiguredSelectionsAndOrderForBothLists()
    {
        var defaults = new SshAlgorithmOptions();
        defaults.Encryption.Value = "aes128-ctr,aes256-ctr";
        defaults.KeyExchange.Value = "ecdh-sha2-nistp521,curve25519-sha256";
        var fixture = new Fixture("SSHFTP", defaults);
        await fixture.Create();
        Assert.Equal(defaults.Encryption.Value, fixture.Submitted!["SSHFTP"]!["EncList"]!.GetValue<string>());
        Assert.Equal(defaults.KeyExchange.Value, fixture.Submitted["SSHFTP"]!["KexAlgs"]!.GetValue<string>());
        Assert.Equal(0, fixture.ComCalls);
    }

    [Fact]
    public async Task UnsupportedConfiguredDefaultsRejectSftpCreationBeforePost()
    {
        var defaults = new SshAlgorithmOptions(); defaults.Encryption.Value = "retired-cipher";
        var fixture = new Fixture("SSHFTP", defaults);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(fixture.Create);
        Assert.Contains("Settings", error.Message); Assert.Equal(0, fixture.Posts); Assert.Empty(fixture.Calls);
    }

    [Theory] [InlineData(0)] [InlineData(408)] [InlineData(500)]
    public async Task LostOrUncertainResponsesRemainUncertainWithoutFallback(int status)
    {
        var fixture = new Fixture("FTP") { PostFailure = status == 0 ? new HttpRequestException("Disconnected") : new MoveItApiException("Unknown", (HttpStatusCode)status) };
        var error = await Assert.ThrowsAsync<HostV0CreationOutcomeException>(fixture.Create);
        Assert.Null(error.HostId); Assert.Equal(new[] { "POST" }, fixture.Calls); Assert.Equal(0, fixture.ComCalls);
    }

    [Theory] [InlineData(400)] [InlineData(401)] [InlineData(403)] [InlineData(409)] [InlineData(422)]
    public async Task DefinitiveRejectionRemainsSafeWithoutFallback(int status)
    {
        var rejection = new MoveItApiException("Rejected", (HttpStatusCode)status);
        var fixture = new Fixture("S3") { PostFailure = rejection };
        Assert.Same(rejection, await Assert.ThrowsAsync<MoveItApiException>(fixture.Create));
        Assert.Equal(new[] { "POST" }, fixture.Calls); Assert.Equal(0, fixture.ComCalls);
    }

    [Fact]
    public async Task CancellationBeforePostDoesNotCreateAndAfterDispatchNeverReplays()
    {
        var fixture = new Fixture("AzureBlob"); using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Crud.CreateShellAsync(fixture.Name, "AzureBlob", canceled.Token));
        Assert.Empty(fixture.Calls);
        fixture.PostFailure = new OperationCanceledException();
        await Assert.ThrowsAsync<HostV0CreationOutcomeException>(fixture.Create);
        Assert.Equal(new[] { "POST" }, fixture.Calls); Assert.Equal(0, fixture.ComCalls);
    }

    [Fact]
    public async Task InvalidNameAndUnsupportedTypeFailBeforePost()
    {
        var fixture = new Fixture("SMTP");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Crud.CreateShellAsync(" ", "SMTP"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Crud.CreateShellAsync(new string('x', 255), "SMTP"));
        foreach (var type in new[] { "FileSystem", "AS2", "Unknown", "" })
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Crud.CreateShellAsync("Name", type));
        Assert.Empty(fixture.Calls); Assert.Equal(0, fixture.ComCalls);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("  ")] [InlineData("server\\share")]
    public async Task ShareRequiresUncBeforeAnyNativeWrite(string? unc)
    {
        var fixture = new Fixture("Share");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Crud.CreateShellAsync(fixture.Name, "Share", unc: unc));
        Assert.Contains("Resource UNC", error.Message);
        Assert.Empty(fixture.Calls); Assert.Equal(0, fixture.ComCalls);
    }

    [Theory]
    [InlineData("Share", @"\\SERVER\SHARE\", true)]
    [InlineData("Share", @"\\server\other", false)]
    [InlineData("FTP", @"\\server\share", false)]
    public async Task ShareConnectionUniquenessUsesFreshDefinitionsAndPreservesItsCreationValue(string existingType, string existingUnc, bool duplicate)
    {
        var calls = new List<string>();
        var crud = Stub<IHostCrudCoordinator>((method, args) =>
        {
            calls.Add(method.Name);
            return method.Name switch
            {
                nameof(IHostCrudCoordinator.NameExistsAsync) => Task.FromResult(false),
                nameof(IHostCrudCoordinator.GetDefinitionsAsync) => Task.FromResult<IReadOnlyList<JsonObject>>([
                    new JsonObject { [existingType] = new JsonObject { ["ID"] = "12", ["Name"] = "Other", ["UNC"] = existingUnc } }]),
                nameof(IHostCrudCoordinator.CreateShellAsync) => Created(args),
                _ => throw new NotSupportedException(method.Name)
            };
        });
        Task<MoveItCreatedHost> Created(object?[] args)
        {
            Assert.Equal(ResourceUnc, args[3]);
            return Task.FromResult(new MoveItCreatedHost("13", "New", "UNC Share", "Share"));
        }
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var session = Stub<IMoveItSessionService>((_, _) => new MoveItSessionState { ServerKey = "Test", Username = "tester" });
        var service = new AutomationHostService(null!, crud, session, cache, new Snapshot());
        if (duplicate)
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateHostAsync("New", "Share", unc: " " + ResourceUnc + " "));
            Assert.Contains("unique UNC path", error.Message);
            Assert.Equal(new[] { nameof(IHostCrudCoordinator.NameExistsAsync), nameof(IHostCrudCoordinator.GetDefinitionsAsync) }, calls);
        }
        else
        {
            Assert.Equal("13", (await service.CreateHostAsync("New", "Share", unc: " " + ResourceUnc + " ")).Id);
            Assert.Equal(new[] { nameof(IHostCrudCoordinator.NameExistsAsync), nameof(IHostCrudCoordinator.GetDefinitionsAsync), nameof(IHostCrudCoordinator.CreateShellAsync) }, calls);
        }
    }

    [Fact]
    public async Task ConfiguredSpreadsheetCreationStillUsesComAndTheOriginalDefinition()
    {
        var definition = new HostBasicDefinition("Import", "FTP", "server", 21, "user", "secret", "", "none", "", "Description", "Operations", false);
        var writes = 0;
        var com = Stub<IMoveItComClient>((method, args) =>
        {
            Assert.Equal(nameof(IMoveItComClient.CreateConfiguredHostAsync), method.Name); Assert.Same(definition, args[0]); writes++;
            return Task.FromResult(new MoveItCreatedHost("99", "Import", "FTP/FTPS", "FTP"));
        });
        var crud = new HostCrudCoordinator(Stub<IMoveItRestClient>((method, _) => throw new Exception("Spreadsheet creation must not change transport: " + method.Name)), com, null!);
        Assert.Equal("99", (await crud.CreateConfiguredAsync(definition)).Id); Assert.Equal(1, writes);
    }

    [Fact]
    public async Task UncertainNewHostInvalidatesInventoryAndDuplicateNameRejectsBeforePost()
    {
        var fixture = new Fixture("FTP") { ReadFailure = new HttpRequestException("Readback lost") }; var reads = 0; var duplicate = false;
        var crud = Stub<IHostCrudCoordinator>((method, _) => method.Name switch
        {
            nameof(IHostCrudCoordinator.NameExistsAsync) => Task.FromResult(duplicate),
            nameof(IHostCrudCoordinator.CreateShellAsync) => fixture.Create(),
            nameof(IHostCrudCoordinator.GetDefinitionsAsync) => Inventory(),
            _ => throw new NotSupportedException(method.Name)
        });
        Task<IReadOnlyList<JsonObject>> Inventory() { reads++; return Task.FromResult<IReadOnlyList<JsonObject>>([]); }
        var rest = Stub<IMoveItRestClient>((method, _) => method.Name == nameof(IMoveItRestClient.GetTaskHostReferenceDefinitionsAsync)
            ? Task.FromResult<IReadOnlyList<JsonObject>>([]) : throw new NotSupportedException(method.Name));
        var session = Stub<IMoveItSessionService>((_, _) => new MoveItSessionState { ServerKey = "Test", Username = "tester" });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new AutomationHostService(rest, crud, session, cache, new Snapshot());
        await service.GetHostSnapshotAsync(); var before = reads;
        await Assert.ThrowsAsync<HostV0CreationOutcomeException>(() => service.CreateHostAsync(fixture.Name, "FTP"));
        await service.GetHostSnapshotAsync(); Assert.Equal(before + 1, reads);
        duplicate = true;
        await Assert.ThrowsAsync<HostNameAlreadyExistsException>(() => service.CreateHostAsync(fixture.Name, "FTP"));
        Assert.Equal(UnavailableReadCalls(), fixture.Calls);
    }

    private static string[] UnavailableReadCalls() => new[] { "POST", "GET:374281533" }.Concat(Enumerable.Repeat("inventory", 4)).ToArray();

    [Theory] [MemberData(nameof(Types))]
    public async Task IncompleteIndividualProjectionReconcilesByReturnedIdInFreshInventory(string type)
    {
        foreach (var withoutType in new[] { true, false })
        {
            var fixture = new Fixture(type); var inventory = fixture.Persisted.DeepClone().AsObject();
            var body = fixture.Persisted[type]!.DeepClone().AsObject();
            if (withoutType) fixture.Persisted = body;
            else { body["Type"] = type; body.Remove("ID"); fixture.Persisted = body; }
            fixture.InventoryRead = _ => [inventory];
            var result = await fixture.Create();
            Assert.Equal("374281533", result.Id); Assert.Equal(type, result.HostTypeCode);
            Assert.Equal(new[] { "POST", "GET:374281533", "inventory" }, fixture.Calls);
            Assert.Equal(1, fixture.Posts); Assert.Equal(0, fixture.ComCalls);
        }
    }

    [Fact]
    public async Task NewMoveitHostCanVerifyFromInventoryWhenTheIndividualGetIsNotReady()
    {
        var fixture = new Fixture("siLock") { ReadFailure = new MoveItApiException("Not ready", HttpStatusCode.NotFound) };
        fixture.Response["id"] = 157331079;
        var body = fixture.Persisted["siLock"]!.DeepClone().AsObject(); body["ID"] = 157331079; body["Name"] = "NEWMOVEIT";
        fixture.InventoryRead = _ => [new JsonObject { ["SiLock"] = body.DeepClone() }];
        var result = await fixture.Crud.CreateShellAsync("NEWMOVEIT", "siLock");
        Assert.Equal("157331079", result.Id); Assert.Equal("NEWMOVEIT", result.Name); Assert.Equal("siLock", result.HostTypeCode);
        Assert.Equal(new[] { "POST", "GET:157331079", "inventory" }, fixture.Calls); Assert.Equal(1, fixture.Posts); Assert.Equal(0, fixture.ComCalls);
    }

    [Fact]
    public async Task DelayedVisibilityRepeatsOnlyReadsAndKeepsTheOriginalCreatedId()
    {
        var fixture = new Fixture("FTP") { ReadFailure = new MoveItApiException("Not visible yet", HttpStatusCode.NotFound) };
        fixture.InventoryRead = count => count < 3 ? [] : [fixture.Persisted.DeepClone().AsObject()];
        Assert.Equal("374281533", (await fixture.Create()).Id);
        Assert.Equal(3, fixture.InventoryReads); Assert.Equal(1, fixture.Posts); Assert.Equal(0, fixture.ComCalls);
    }

    [Fact]
    public async Task ANameMatchWithAnotherIdCannotBeMistakenForTheCreatedHost()
    {
        var fixture = new Fixture("SMTP") { ReadFailure = new MoveItApiException("Not visible", HttpStatusCode.NotFound) };
        var other = fixture.Persisted.DeepClone().AsObject(); other["SMTP"]!["ID"] = "99";
        fixture.InventoryRead = _ => [other];
        var error = await Assert.ThrowsAsync<HostV0CreationOutcomeException>(fixture.Create);
        Assert.Equal("374281533", error.HostId); Assert.Equal("created-id-not-visible", error.VerificationReason);
        Assert.Equal(4, fixture.InventoryReads); Assert.Equal(1, fixture.Posts); Assert.Equal(0, fixture.ComCalls);
    }

    [Theory] [InlineData("name-mismatch")] [InlineData("type-mismatch")] [InlineData("ambiguous-inventory-id")]
    public async Task ConflictingInventoryRemainsUnverifiedWithASpecificReason(string failure)
    {
        var fixture = new Fixture("FTP") { ReadFailure = new HttpRequestException("Disconnected") };
        var body = fixture.Persisted["FTP"]!.DeepClone().AsObject();
        if (failure == "name-mismatch") body["Name"] = "Different";
        var entry = new JsonObject { [failure == "type-mismatch" ? "SMTP" : "FTP"] = body };
        fixture.InventoryRead = _ => failure == "ambiguous-inventory-id" ? [entry, entry.DeepClone().AsObject()] : [entry];
        var error = await Assert.ThrowsAsync<HostV0CreationOutcomeException>(fixture.Create);
        Assert.Equal("readback", error.VerificationStage); Assert.Equal(failure, error.VerificationReason);
        Assert.Equal(1, fixture.InventoryReads); Assert.Equal(1, fixture.Posts); Assert.Equal(0, fixture.ComCalls);
    }

    [Theory] [InlineData(401)] [InlineData(403)]
    public async Task ReadbackAuthorizationFailureDoesNotPollOrBypassPermissions(int status)
    {
        var fixture = new Fixture("Share") { ReadFailure = new MoveItApiException("SENSITIVE", (HttpStatusCode)status) };
        var error = await Assert.ThrowsAsync<HostV0CreationOutcomeException>(fixture.Create);
        Assert.Equal("HTTP-" + status, error.VerificationReason); Assert.DoesNotContain("SENSITIVE", error.Message);
        Assert.Equal(0, fixture.InventoryReads); Assert.Equal(1, fixture.Posts); Assert.Equal(0, fixture.ComCalls);
    }

    [Fact]
    public async Task CanceledVisibilityChecksKeepTheCreatedIdAndNeverReplayPost()
    {
        using var canceled = new CancellationTokenSource();
        var fixture = new Fixture("S3") { ReadFailure = new MoveItApiException("Not ready", HttpStatusCode.NotFound) };
        fixture.InventoryRead = _ => { canceled.Cancel(); return []; };
        var error = await Assert.ThrowsAsync<HostV0CreationOutcomeException>(() => fixture.Crud.CreateShellAsync(fixture.Name, "S3", canceled.Token));
        Assert.Equal("374281533", error.HostId); Assert.Equal("canceled", error.VerificationReason);
        Assert.Equal(1, fixture.InventoryReads); Assert.Equal(1, fixture.Posts); Assert.Equal(0, fixture.ComCalls);
    }

    [Theory] [MemberData(nameof(Types))]
    public async Task RealRestClientReconcilesCreationThroughItsExistingHostInventoryParser(string type)
    {
        foreach (var missing in new[] { true, false })
        {
            using var handler = new CreationRoundTripHandler(missing);
            var session = Stub<IMoveItSessionService>((method, _) => method.Name == nameof(IMoveItSessionService.Get)
                ? new MoveItSessionState { WebAdminUrl = "https://moveit.test", AccessToken = "test-token", AccessTokenExpiresUtc = DateTimeOffset.UtcNow.AddHours(1) }
                : throw new NotSupportedException(method.Name));
            var client = new MoveItRestClient(new ClientFactory(handler), session,
                ConfigurationStartupState.Capture(new ConfigurationBuilder().Build()), null!, NullLogger<MoveItRestClient>.Instance);
            var com = Stub<IMoveItComClient>((method, _) => throw new Exception("Unexpected COM: " + method.Name));
            var crud = new HostCrudCoordinator(client, com, null!);
            var result = await crud.CreateShellAsync("NEW_HOST", type, unc: ResourceUnc);
            Assert.Equal("157331079", result.Id); Assert.Equal("NEW_HOST", result.Name); Assert.Equal(type, result.HostTypeCode);
            Assert.Equal(new[] { "POST /api/v0/hosts", "GET /api/v1/hosts/157331079", "GET /api/v1/hosts" }, handler.Calls.Where(call => !call.Contains("/api/v0/ssh", StringComparison.Ordinal)));
        }
    }

    private sealed class CreationRoundTripHandler(bool missing) : HttpMessageHandler
    {
        public List<string> Calls { get; } = [];
        private JsonObject? _created;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath; Calls.Add(request.Method + " " + path);
            if (request.Method == HttpMethod.Get && path == "/api/v0/ssh/encryptionlist")
                return Reply(HttpStatusCode.OK, HostSshAlgorithmTests.Capture("Encryption").ToJsonString());
            if (request.Method == HttpMethod.Get && path == "/api/v0/sshdefaultkexalgs")
                return Reply(HttpStatusCode.OK, HostSshAlgorithmTests.Capture("KeyExchange").ToJsonString());
            if (request.Method == HttpMethod.Post && path == "/api/v0/hosts")
            {
                _created = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
                var body = _created.First().Value!.AsObject(); body["ID"] = 157331079;
                return Reply(HttpStatusCode.OK, "{\"id\":157331079,\"status\":\"success\",\"errorcode\":0,\"refreshRequired\":true}");
            }
            if (request.Method == HttpMethod.Get && path == "/api/v1/hosts/157331079")
                return missing ? Reply(HttpStatusCode.NotFound, "{\"message\":\"Not yet visible\"}")
                    : Reply(HttpStatusCode.OK, _created!.First().Value!.ToJsonString()); // Flat projection has no protocol discriminator.
            if (request.Method == HttpMethod.Get && path == "/api/v1/hosts")
                return Reply(HttpStatusCode.OK, new JsonArray(_created!.DeepClone()).ToJsonString());
            throw new InvalidOperationException("Unexpected HTTP call: " + request.Method + " " + path);
        }
        private static HttpResponseMessage Reply(HttpStatusCode code, string json) => new(code)
        { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
    }

    private sealed class Snapshot : IOptionsSnapshot<HostBehaviorOptions>
    { public HostBehaviorOptions Value { get; } = new(); public HostBehaviorOptions Get(string? name) => Value; }
    private sealed class Fixture
    {
        public IHostCrudCoordinator Crud { get; }
        public string Name { get; }
        public JsonObject Response { get; set; } = JsonNode.Parse("""{"id":"374281533","status":"success","errorcode":0,"refreshRequired":true}""")!.AsObject();
        public JsonObject Persisted { get; set; }
        public JsonObject? Submitted { get; private set; }
        public List<string> Calls { get; } = [];
        public int ComCalls { get; private set; }
        public Exception? PostFailure { get; set; }
        public Exception? ReadFailure { get; set; }
        public Func<int, IReadOnlyList<JsonObject>>? InventoryRead { get; set; }
        public int InventoryReads { get; private set; }
        public int Posts { get; private set; }
        public Exception? DiscoveryFailure { get; set; }
        public List<string> Discoveries { get; } = [];
        private readonly string _type;
        public Task<MoveItCreatedHost> Create() => Crud.CreateShellAsync(Name, _type, unc: ResourceUnc);
        public Fixture(string type, SshAlgorithmOptions? defaults = null)
        {
            _type = type; Persisted = Capture(type); Name = Persisted[type]!["Name"]!.ToString(); Persisted[type]!["ID"] = "374281533";
            var rest = Stub<IMoveItRestClient>((method, args) => method.Name switch
            {
                nameof(IMoveItRestClient.GetSshEncryptionListV0Async) => Discover("Encryption"),
                nameof(IMoveItRestClient.GetSshDefaultKexAlgorithmsV0Async) => Discover("KeyExchange"),
                nameof(IMoveItRestClient.CreateHostV0Async) => Post(args),
                nameof(IMoveItRestClient.GetHostAsync) => Read(args),
                nameof(IMoveItRestClient.GetHostsAsync) => Inventory(),
                _ => throw new NotSupportedException("Unexpected operation: " + method.Name)
            });
            Task<JsonObject> Discover(string kind)
            {
                Discoveries.Add(kind);
                return DiscoveryFailure is null ? Task.FromResult(HostSshAlgorithmTests.Capture(kind)) : Task.FromException<JsonObject>(DiscoveryFailure);
            }
            Task<JsonObject> Post(object?[] args)
            {
                Calls.Add("POST"); Posts++; Submitted = ((JsonObject)args[0]!).DeepClone().AsObject();
                return PostFailure is null ? Task.FromResult(Response.DeepClone().AsObject()) : Task.FromException<JsonObject>(PostFailure);
            }
            Task<JsonObject> Read(object?[] args)
            {
                Calls.Add("GET:" + args[0]); return ReadFailure is null ? Task.FromResult(Persisted.DeepClone().AsObject()) : Task.FromException<JsonObject>(ReadFailure);
            }
            Task<IReadOnlyList<JsonObject>> Inventory()
            {
                Calls.Add("inventory"); InventoryReads++;
                return Task.FromResult(InventoryRead?.Invoke(InventoryReads) ?? Array.Empty<JsonObject>());
            }
            var com = Stub<IMoveItComClient>((method, _) => { ComCalls++; throw new Exception("COM must not be used: " + method.Name); });
            Crud = new HostCrudCoordinator(rest, com, null!, defaults is null ? null : new AlgorithmSnapshot(defaults));
        }
    }
    private sealed class AlgorithmSnapshot(SshAlgorithmOptions value) : IOptionsSnapshot<SshAlgorithmOptions>
    { public SshAlgorithmOptions Value => value; public SshAlgorithmOptions Get(string? name) => value; }
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
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"374281533\",\"status\":\"success\",\"errorcode\":0,\"refreshRequired\":true}") };
        }
    }
    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, disposeHandler: false); }
    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    { var value = DispatchProxy.Create<T, Proxy>(); ((Proxy)(object)value).Handler = handler; return value; }
    public class Proxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args ?? []);
    }
}
