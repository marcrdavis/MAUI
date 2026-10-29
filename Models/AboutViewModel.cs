// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

public sealed class AboutViewModel
{
    public string ApplicationName { get; init; } = "MAUI";
    public string ApplicationLogo { get; init; } = string.Empty;
    public string ApplicationVersion { get; init; } = string.Empty;
    public string ServerDisplayName { get; init; } = string.Empty;
    public string WebAdminUrl { get; init; } = string.Empty;
    public MoveItInfo? MoveIt { get; init; }
}
