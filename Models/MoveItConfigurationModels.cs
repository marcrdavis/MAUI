// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

public static class MoveItConfigurationExportModes
{
    public const string Passphrase = "Passphrase";
    public const string ServerKey = "ServerKey";
    public const string Sanitized = "Sanitized";
}

public static class MoveItConfigurationImportModes
{
    public const string Passphrase = "Passphrase";
    public const string ServerKey = "ServerKey";
}

public sealed class MoveItConfigurationExportRequest
{
    public string Mode { get; set; } = MoveItConfigurationExportModes.Passphrase;
    public string Passphrase { get; set; } = string.Empty;
    public string ConfirmPassphrase { get; set; } = string.Empty;
}
