// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

// Session-level information returned by the native MOVEit Web Admin /api/v0/info endpoint.
// Analytics identifiers are intentionally not retained because MAUI does not need them for
// authorization, diagnostics, licensing, or time-zone handling.
public sealed class MoveItInfo
{
    public string CentralHost { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public string Domain { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public string AppBuildNumber { get; set; } = string.Empty;
    public string AppBuildTimestamp { get; set; } = string.Empty;
    public string CentralVersion { get; set; } = string.Empty;
    public string CentralApiVersion { get; set; } = string.Empty;
    public bool LicenseInformationAvailable { get; set; }
    public MoveItLicenseInfo License { get; set; } = new();
    public MoveItServerTimezoneInfo ServerTimezoneInfo { get; set; } = new();
    public int? ServerCertificateDaysValid { get; set; }
    public int? MaxInactiveSessionInterval { get; set; }
    public bool SftpFipsModeEnabled { get; set; }
    public bool CustomScriptingDisabled { get; set; }
}

public sealed class MoveItLicenseInfo
{
    public string Version { get; set; } = string.Empty;
    public bool ResilEnabled { get; set; }
    public string ResilRole { get; set; } = string.Empty;
    public int? MaxHosts { get; set; }
    public int? MaxTasks { get; set; }
    public bool As2Licensed { get; set; }
    public bool BackupHostsLicensed { get; set; }
    public bool AccountManagementLicensed { get; set; }
    public bool AdvancedTasksLicensed { get; set; }
    public bool CustomScriptsLicensed { get; set; }
    public bool ApiLicensed { get; set; }
    public bool PgpLicensed { get; set; }
}

public sealed class MoveItServerTimezoneInfo
{
    public string TimeZoneName { get; set; } = string.Empty;
    public string TimeZoneBias { get; set; } = string.Empty;
    public string DaylightBias { get; set; } = string.Empty;
}
