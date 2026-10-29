// Project: MAUI - MOVEit Automation UI
// One coherent, installation-owned snapshot for editable Settings and MOVEit server definitions.
// Runtime IConfiguration / IOptions still support external overrides and startup-bound options.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace MoveItAutomationUI.Services.Configuration;

public sealed record AppSettingsConfigurationSnapshot(
    JsonObject Document,
    IConfigurationRoot Configuration,
    string Revision);

public interface IAppSettingsConfigurationSnapshotReader
{
    AppSettingsConfigurationSnapshot Read();
    AppSettingsConfigurationSnapshot FromDocument(JsonObject document);
    string GetRevision(JsonObject document);
}

public sealed class AppSettingsConfigurationSnapshotReader : IAppSettingsConfigurationSnapshotReader
{
    private readonly IAppSettingsDocumentStore _store;

    public AppSettingsConfigurationSnapshotReader(IAppSettingsDocumentStore store) => _store = store;

    public AppSettingsConfigurationSnapshot Read() => FromDocument(_store.ReadLive());

    public AppSettingsConfigurationSnapshot FromDocument(JsonObject document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var json = document.ToJsonString();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        return new AppSettingsConfigurationSnapshot(document, configuration, GetRevision(document));
    }

    // Hash the complete persisted settings (including independent credential/settings changes).
    // Only the digest is returned to the browser, never any configuration secrets.
    public string GetRevision(JsonObject document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(document.ToJsonString()));
        return Convert.ToHexString(bytes);
    }
}
