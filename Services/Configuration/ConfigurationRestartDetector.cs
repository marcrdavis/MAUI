// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Captures startup-sensitive configuration and reports staged changes that require an application restart.

using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.Configuration;

public interface IConfigurationRestartDetector
{
    // Purpose: Returns staged configuration changes that require an application restart.
    IReadOnlyList<string> GetRestartReasons(MoveItOptions moveIt, MetadataDatabaseOptions database);

    // Purpose: Returns restart-sensitive changes represented by a staged appsettings document.
    IReadOnlyList<string> GetRestartReasons(JsonObject root);
}

public sealed class ConfigurationRestartDetector : IConfigurationRestartDetector
{
    private readonly ConfigurationStartupState _startupState;

    // Purpose: Initializes restart detection from the configuration captured at application startup.
    public ConfigurationRestartDetector(ConfigurationStartupState startupState)
    {
        _startupState = startupState;
    }

    // Purpose: Returns staged configuration changes that require an application restart.
    public IReadOnlyList<string> GetRestartReasons(MoveItOptions moveIt, MetadataDatabaseOptions database)
    {
        var reasons = new List<string>();
        if (!string.Equals(_startupState.DatabaseProvider, database.Provider.Value?.Trim(), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(_startupState.DatabaseConnectionString, database.ConnectionString.Value ?? string.Empty, StringComparison.Ordinal))
        {
            reasons.Add("Metadata database configuration changed.");
        }

        if (_startupState.RequestTimeoutSeconds != moveIt.RequestTimeoutSeconds.Value
            || _startupState.IdleSessionTimeoutMinutes != moveIt.IdleSessionTimeoutMinutes.Value
            || _startupState.TaskRunHistoryMaxCount != moveIt.TaskRunHistoryMaxCount.Value)
        {
            reasons.Add("MOVEit runtime/authentication settings changed.");
        }

        if (_startupState.AllowInvalidServerCertificates != moveIt.AllowInvalidServerCertificates.Value)
        {
            reasons.Add("Server certificate validation behavior changed.");
        }

        return reasons;
    }

    // Purpose: Applies the same restart policy to a staged appsettings document used by Setup/Upgrade workflows.
    public IReadOnlyList<string> GetRestartReasons(JsonObject root)
    {
        ArgumentNullException.ThrowIfNull(root);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(root.ToJsonString()));
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(stream)
            .Build();

        var moveIt = configuration.GetSection(MoveItOptions.SectionName).Get<MoveItOptions>() ?? new MoveItOptions();
        var database = configuration.GetSection(MetadataDatabaseOptions.SectionName).Get<MetadataDatabaseOptions>() ?? new MetadataDatabaseOptions();
        return GetRestartReasons(moveIt, database);
    }

}
