// Project: MAUI - MOVEit Automation UI
// Functional tests for coherent settings snapshots and optimistic concurrency revisions.

using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.Configuration;

namespace MoveItAutomationUI.Tests.Configuration;

public sealed class AppSettingsSnapshotTests
{
    [Fact]
    public void FromDocument_BindsServersAndSettingsFromTheSameSnapshot()
    {
        var reader = new AppSettingsConfigurationSnapshotReader(new UnusedDocumentStore());
        var original = JsonNode.Parse("""{"MoveIt":{"Servers":[{"Key":"prod","DisplayName":"Production","WebAdminUrl":"https://prod.example.invalid"}]},"Smtp":{"Host":"mail.example.invalid"}}""")!.AsObject();
        var snapshot = reader.FromDocument(original);

        Assert.Equal("prod", snapshot.Configuration["MoveIt:Servers:0:Key"]);
        Assert.Equal("mail.example.invalid", snapshot.Configuration["Smtp:Host"]);
        Assert.Equal(snapshot.Revision, reader.GetRevision(original));
    }

    [Fact]
    public void Revision_DetectsIndependentCredentialsAndConnectionChanges()
    {
        var reader = new AppSettingsConfigurationSnapshotReader(new UnusedDocumentStore());
        var root = JsonNode.Parse("""{"MoveIt":{"Servers":[{"Key":"prod","WebAdminUrl":"https://prod.example.invalid","DataAccessProtectedPassword":"old"}]}}""")!.AsObject();
        var firstRevision = reader.GetRevision(root);

        root["MoveIt"]!["Servers"]![0]!["DataAccessProtectedPassword"] = "new";
        var secondRevision = reader.GetRevision(root);
        Assert.NotEqual(firstRevision, secondRevision);

        root["MoveIt"]!["Servers"]![0]!["WebAdminUrl"] = "https://new-prod.example.invalid";
        Assert.NotEqual(secondRevision, reader.GetRevision(root));
    }

    [Fact]
    public void Revision_IsStableForUnchangedConfiguration()
    {
        var reader = new AppSettingsConfigurationSnapshotReader(new UnusedDocumentStore());
        var root = JsonNode.Parse("""{"MoveIt":{"Servers":[]},"Ui":{"Version":"0.17.317"}}""")!.AsObject();
        Assert.Equal(reader.GetRevision(root), reader.GetRevision(root.DeepClone().AsObject()));
    }

    private sealed class UnusedDocumentStore : IAppSettingsDocumentStore
    {
        public string LivePath => string.Empty;
        public string TemplatePath => string.Empty;
        public bool LiveExists => false;
        public JsonObject ReadTemplate() => throw new NotSupportedException();
        public JsonObject ReadLive() => throw new NotSupportedException();
        public Task<JsonObject> ReadLiveAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<JsonObject> ReadSeedAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task WriteLiveAsync(JsonObject root, CancellationToken cancellationToken = default, bool publishRuntime = true) => throw new NotSupportedException();
        public string CreateLiveBackup(string label) => throw new NotSupportedException();
    }
}
