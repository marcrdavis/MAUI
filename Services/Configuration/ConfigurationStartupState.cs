// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Captures effective startup-sensitive settings; first-run Setup can activate this snapshot once without recycling IIS.

using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.Configuration;

public sealed class ConfigurationStartupState
{
    private sealed record Snapshot(
        string DatabaseProvider,
        string DatabaseConnectionString,
        int RequestTimeoutSeconds,
        int IdleSessionTimeoutMinutes,
        int TaskRunHistoryMaxCount,
        bool AllowInvalidServerCertificates);

    private Snapshot _effective;

    private ConfigurationStartupState(Snapshot initial) => _effective = initial;

    public string DatabaseProvider => Volatile.Read(ref _effective).DatabaseProvider;
    public string DatabaseConnectionString => Volatile.Read(ref _effective).DatabaseConnectionString;
    public int RequestTimeoutSeconds => Volatile.Read(ref _effective).RequestTimeoutSeconds;
    public int IdleSessionTimeoutMinutes => Volatile.Read(ref _effective).IdleSessionTimeoutMinutes;
    public int TaskRunHistoryMaxCount => Volatile.Read(ref _effective).TaskRunHistoryMaxCount;
    public bool AllowInvalidServerCertificates => Volatile.Read(ref _effective).AllowInvalidServerCertificates;

    public static ConfigurationStartupState Capture(IConfiguration configuration)
        => new(ReadSnapshot(configuration));

    // Only Initial Setup calls this after successful persistence/configuration reload. Ordinary
    // Settings saves MUST NOT update this snapshot because DB/TLS/runtime options still require restart.
    public void ActivateAfterInitialSetup(IConfiguration configuration)
        => Volatile.Write(ref _effective, ReadSnapshot(configuration));

    private static Snapshot ReadSnapshot(IConfiguration configuration)
    {
        var moveIt = configuration.GetSection(MoveItOptions.SectionName).Get<MoveItOptions>() ?? new MoveItOptions();
        var database = configuration.GetSection(MetadataDatabaseOptions.SectionName).Get<MetadataDatabaseOptions>() ?? new MetadataDatabaseOptions();
        return new Snapshot(
            database.Provider.Value?.Trim() ?? string.Empty,
            database.ConnectionString.Value ?? string.Empty,
            moveIt.RequestTimeoutSeconds.Value,
            moveIt.IdleSessionTimeoutMinutes.Value,
            moveIt.TaskRunHistoryMaxCount.Value,
            moveIt.AllowInvalidServerCertificates.Value);
    }
}
