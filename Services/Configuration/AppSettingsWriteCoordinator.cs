// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Coordinates appsettings.json writes across configuration, credentials, reporting, setup, and upgrade workflows.

namespace MoveItAutomationUI.Services.Configuration;

public sealed class AppSettingsWriteCoordinator
{
    public SemaphoreSlim Gate { get; } = new(1, 1);
}
