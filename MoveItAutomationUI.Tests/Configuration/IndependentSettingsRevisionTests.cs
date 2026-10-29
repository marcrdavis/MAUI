// Regression coverage: independent credential operations must return the revision of
// the document they persisted, so the open Settings page can safely advance its token.

using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.Reporting;

namespace MoveItAutomationUI.Tests.Configuration;

public sealed class IndependentSettingsRevisionTests
{
    [Fact]
    public async Task DataAccessSaveAndClear_ReturnRevisionsOfPersistedDocuments()
    {
        var store = new MemoryStore("""{"MoveIt":{"Servers":[{"Key":"prod","DisplayName":"Production"}]}}""");
        var snapshots = new AppSettingsConfigurationSnapshotReader(store);
        var configuration = new ConfigurationBuilder().Build();
        using var services = new ServiceCollection().BuildServiceProvider();
        var service = new DataAccessCredentialService(
            configuration,
            new MoveItServerCatalog(configuration),
            store,
            new AppSettingsWriteCoordinator(),
            snapshots,
            new TestProtector(),
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DataAccessCredentialService>.Instance);

        var initialRevision = snapshots.Read().Revision;
        var savedRevision = await service.SaveAsync("prod", "robot", "secret");

        Assert.NotEqual(initialRevision, savedRevision);
        Assert.Equal(snapshots.Read().Revision, savedRevision);
        var savedServer = store.ReadLive()["MoveIt"]!["Servers"]![0]!;
        Assert.Equal("robot", savedServer["DataAccessUsername"]!.GetValue<string>());
        Assert.Equal("encrypted-test", savedServer["DataAccessProtectedPassword"]!.GetValue<string>());

        var clearedRevision = await service.ClearAsync("prod");

        Assert.NotEqual(savedRevision, clearedRevision);
        Assert.Equal(snapshots.Read().Revision, clearedRevision);
        var clearedServer = store.ReadLive()["MoveIt"]!["Servers"]![0]!;
        Assert.Null(clearedServer["DataAccessUsername"]);
        Assert.Null(clearedServer["DataAccessProtectedPassword"]);
    }

    [Fact]
    public async Task SmtpClear_ReturnsCurrentRevisionAndRetainsOtherSmtpSettings()
    {
        var store = new MemoryStore("""{"Smtp":{"Host":"smtp.example.invalid","ProtectedPassword":"previous"}}""");
        var snapshots = new AppSettingsConfigurationSnapshotReader(store);
        var service = new ReportConfigurationStore(
            store,
            new AppSettingsWriteCoordinator(),
            snapshots,
            NullLogger<ReportConfigurationStore>.Instance);
        var initialRevision = snapshots.Read().Revision;

        var newRevision = await service.ClearSmtpPasswordAsync();

        Assert.NotEqual(initialRevision, newRevision);
        Assert.Equal(snapshots.Read().Revision, newRevision);
        Assert.Equal(string.Empty, store.ReadLive()["Smtp"]!["ProtectedPassword"]!.GetValue<string>());
        Assert.Equal("smtp.example.invalid", store.ReadLive()["Smtp"]!["Host"]!.GetValue<string>());
    }

    private sealed class TestProtector : IDataAccessCredentialProtector
    {
        public string Protect(string clearText) => "encrypted-test";
        public string Unprotect(string protectedValue) => "secret";
    }

    private sealed class MemoryStore : IAppSettingsDocumentStore
    {
        private JsonObject _document;

        public MemoryStore(string json) => _document = JsonNode.Parse(json)!.AsObject();

        public string LivePath => string.Empty;
        public string TemplatePath => string.Empty;
        public bool LiveExists => true;
        public JsonObject ReadTemplate() => JsonNode.Parse("""{"Smtp":{}}""")!.AsObject();
        public JsonObject ReadLive() => _document.DeepClone().AsObject();
        public Task<JsonObject> ReadLiveAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(ReadLive());
        public Task<JsonObject> ReadSeedAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task WriteLiveAsync(JsonObject root, CancellationToken cancellationToken = default, bool publishRuntime = true)
        {
            _document = root.DeepClone().AsObject();
            return Task.CompletedTask;
        }
        public string CreateLiveBackup(string label) => throw new NotSupportedException();
    }
}
