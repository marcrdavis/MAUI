// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Holds the process-lifetime upgrade state determined at startup and updated during an upgrade.

using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.Upgrade;

public sealed class MauiUpgradeState
{
    private readonly object _sync = new();

    public string InstalledVersion { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string TargetVersion { get; private set; } = string.Empty;
    public int CurrentSchemaVersion { get; private set; }
    public int TargetSchemaVersion { get; private set; } = 1;
    public int CurrentConfigurationVersion { get; private set; } = 1;
    public int TargetConfigurationVersion { get; private set; } = 1;
    public bool IsPending { get; private set; }
    public bool PendingRestart { get; private set; }
    public string? StartupError { get; private set; }
    public SemaphoreSlim ExecutionGate { get; } = new(1, 1);

    // Purpose: Initializes in-memory upgrade state from the validated release manifest and installed versions.
    public void Initialize(
        string installedVersion,
        MauiUpgradeManifest manifest,
        int currentSchemaVersion,
        int currentConfigurationVersion,
        string? startupError = null)
    {
        lock (_sync)
        {
            InstalledVersion = installedVersion?.Trim() ?? string.Empty;
            Description = manifest.Description.Trim();
            TargetVersion = manifest.TargetVersion.Trim();
            CurrentSchemaVersion = currentSchemaVersion;
            TargetSchemaVersion = manifest.RequiredSchemaVersion;
            CurrentConfigurationVersion = currentConfigurationVersion;
            TargetConfigurationVersion = manifest.RequiredConfigurationVersion;
            StartupError = startupError;
            PendingRestart = false;
            IsPending = startupError is not null
                || !string.Equals(InstalledVersion, TargetVersion, StringComparison.OrdinalIgnoreCase)
                || CurrentSchemaVersion != TargetSchemaVersion
                || CurrentConfigurationVersion != TargetConfigurationVersion;
        }
    }

    // Purpose: Updates committed schema/configuration progress while an upgrade is executing.
    public void UpdateProgress(int schemaVersion, int configurationVersion)
    {
        lock (_sync)
        {
            CurrentSchemaVersion = schemaVersion;
            CurrentConfigurationVersion = configurationVersion;
        }
    }

    // Purpose: Marks a successfully completed upgrade as current without requiring an application restart.
    public void MarkCompleted(int schemaVersion, int configurationVersion)
    {
        lock (_sync)
        {
            CurrentSchemaVersion = schemaVersion;
            CurrentConfigurationVersion = configurationVersion;
            InstalledVersion = TargetVersion;
            StartupError = null;
            PendingRestart = false;
            IsPending = false;
        }
    }

    // Purpose: Marks a successfully completed upgrade as awaiting a controlled application restart.
    public void MarkCompletedPendingRestart(int schemaVersion, int configurationVersion)
    {
        lock (_sync)
        {
            CurrentSchemaVersion = schemaVersion;
            CurrentConfigurationVersion = configurationVersion;
            InstalledVersion = TargetVersion;
            StartupError = null;
            PendingRestart = true;
            IsPending = true;
        }
    }

    // Purpose: Builds the Upgrade page view model from the current in-memory state.
    public MauiUpgradeViewModel ToViewModel()
    {
        lock (_sync)
        {
            return new MauiUpgradeViewModel
            {
                InstalledVersion = InstalledVersion,
                Description = Description,
                TargetVersion = TargetVersion,
                CurrentSchemaVersion = CurrentSchemaVersion,
                TargetSchemaVersion = TargetSchemaVersion,
                CurrentConfigurationVersion = CurrentConfigurationVersion,
                TargetConfigurationVersion = TargetConfigurationVersion,
                PendingRestart = PendingRestart,
                StartupError = StartupError
            };
        }
    }
}
