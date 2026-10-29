// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Tracks when an installation becomes configured, including after first-run Setup without a host restart.

using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.Configuration;

public sealed class MauiSetupState
{
    private int _isConfigured;

    // Kept for existing RC1 consumers. This is now the current process state, not just its startup value.
    public bool IsConfiguredAtStartup => Volatile.Read(ref _isConfigured) != 0;

    // Capture the initial state from the deployment's configuration, including manual installations.
    public static MauiSetupState Capture(IConfiguration configuration)
    {
        var moveIt = configuration.GetSection(MoveItOptions.SectionName).Get<MoveItOptions>() ?? new MoveItOptions();
        var hasConfiguredServer = moveIt.Servers.Any(server =>
            !string.IsNullOrWhiteSpace(server.Key)
            && !string.IsNullOrWhiteSpace(server.WebAdminUrl));
        var state = new MauiSetupState();
        if (hasConfiguredServer) state.MarkConfigured();
        return state;
    }

    // Publish the configured state LAST, after writing/reloading appsettings and activating database/upgrade state.
    public void MarkConfigured() => Interlocked.Exchange(ref _isConfigured, 1);
}
