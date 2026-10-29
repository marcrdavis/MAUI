// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Reads saved MOVEit server definitions from the reloaded configuration rather than the startup snapshot.
// This catalog is the single lookup boundary for login, REST authentication, unattended credentials,
// readiness, Host Health, and backups. Startup-only runtime settings remain unchanged.

using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.Configuration;

public interface IMoveItServerCatalog
{
    IReadOnlyList<MoveItServerDefinition> GetServers();
    MoveItServerDefinition? FindByKey(string? key);
    MoveItServerDefinition? FindForLogin(string? keyOrDisplayName);
}

public sealed class MoveItServerCatalog : IMoveItServerCatalog
{
    private readonly IConfiguration _configuration;

    public MoveItServerCatalog(IConfiguration configuration) => _configuration = configuration;

    // Bind a new server list from reloadable IConfiguration on each operation.
    // AppSettingsDocumentStore publishes reloads after in-process writes; never retain a startup list.
    public IReadOnlyList<MoveItServerDefinition> GetServers()
        => (_configuration.GetSection(MoveItOptions.SectionName).Get<MoveItOptions>() ?? new MoveItOptions())
            .Servers.ToArray();

    public MoveItServerDefinition? FindByKey(string? key)
    {
        var value = (key ?? string.Empty).Trim();
        if (value.Length == 0) return null;
        return GetServers().FirstOrDefault(server => string.Equals(server.Key, value, StringComparison.OrdinalIgnoreCase));
    }

    public MoveItServerDefinition? FindForLogin(string? keyOrDisplayName)
    {
        var value = (keyOrDisplayName ?? string.Empty).Trim();
        if (value.Length == 0) return null;
        var servers = GetServers();
        return servers.FirstOrDefault(server => string.Equals(server.Key, value, StringComparison.OrdinalIgnoreCase))
            ?? servers.FirstOrDefault(server => string.Equals(server.DisplayName, value, StringComparison.OrdinalIgnoreCase));
    }
}
