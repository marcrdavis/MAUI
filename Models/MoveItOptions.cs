// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Text.Json.Serialization;

namespace MoveItAutomationUI.Models;

public sealed class MoveItOptions
{
    public const string SectionName = "MoveIt";

    public SettingDefinition<int> RequestTimeoutSeconds { get; set; } = new()
    {
        Name = "REST Request Timeout Seconds",
        Value = 60,
        Description = "Maximum time allowed for ordinary MOVEit REST requests. Host tests and Browse allow at least 5 minutes, extended by native timeout/retry settings up to 30 minutes.",
        Type = "Numeric"
    };

    public SettingDefinition<bool> AllowInvalidServerCertificates { get; set; } = new()
    {
        Name = "Allow Invalid Server Certificates",
        Value = false,
        Description = "Development-only option that allows REST connections to Web Admin servers using an untrusted TLS certificate.",
        Type = "Boolean",
        PossibleValues = new List<bool> { false, true }
    };

    public static readonly IReadOnlyList<int> SupportedIdleSessionTimeoutMinutes = new[] { 15, 30, 60, 120, 240, 480 };

    public SettingDefinition<int> IdleSessionTimeoutMinutes { get; set; } = new()
    {
        Name = "Idle Session Timeout",
        Value = 30,
        Description = "How long MAUI keeps a signed-in session active without user activity.",
        Type = "List"
    };

    public SettingDefinition<int> TaskRunHistoryMaxCount { get; set; } = new()
    {
        Name = "Task Run History Maximum",
        Value = 10000,
        Description = "Maximum Task Run records requested when determining each Task's latest status.",
        Type = "Numeric"
    };

    public List<MoveItServerDefinition> Servers { get; set; } = new();
}

public sealed class MoveItServerDefinition
{
    public string Key { get; set; } = string.Empty;

    // Groups servers that share the same underlying MOVEit Automation configuration, such as HA/failover nodes.
    // Separate environments such as PROD and QA must use different values. When omitted, Key is used as a safe default.
    public string EnvironmentKey { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
    public string WebAdminUrl { get; set; } = string.Empty;

    // Allows an optional Windows COM API host override; when it is blank, the host from WebAdminUrl is used.
    public string ServerHost { get; set; } = string.Empty;

    // Optional unattended credential used by MAUI background data-access operations such as
    // scheduled reports, Host Health System Jobs, and automatic configuration backups. The password is DPAPI protected
    // and is never returned to the browser. These fields are omitted when not configured so
    // initial setup remains focused on connection topology only.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DataAccessUsername { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DataAccessProtectedPassword { get; set; }
}


public static class MoveItEnvironmentKey
{
    public const int MaxLength = 64;

    // Resolves the stable metadata scope for one configured server. A missing EnvironmentKey falls back to the server Key.
    public static string Resolve(MoveItServerDefinition server)
    {
        ArgumentNullException.ThrowIfNull(server);
        return Normalize(string.IsNullOrWhiteSpace(server.EnvironmentKey) ? server.Key : server.EnvironmentKey);
    }

    // Normalizes environment keys so metadata scoping is consistent across providers and configured failover nodes.
    public static string Normalize(string? environmentKey)
    {
        var value = (environmentKey ?? string.Empty).Trim();
        if (value.Length == 0) throw new ArgumentException("EnvironmentKey is required.", nameof(environmentKey));
        if (value.Length > MaxLength) throw new ArgumentException($"EnvironmentKey cannot exceed {MaxLength} characters.", nameof(environmentKey));
        return value.ToUpperInvariant();
    }
}
