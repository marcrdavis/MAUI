using System.Text.RegularExpressions;

namespace MoveItAutomationUI.Tests.Lifecycle;

public sealed class MauiApplicationRestartUiContractTests
{
    [Fact]
    public void RestartButtonsShareOneConfirmationAndPathBaseAwareEndpoints()
    {
        var view = Read("Views/Configuration/Settings.cshtml");
        var script = Read("wwwroot/js/configuration-application-restart.js");
        var restart = view.IndexOf("data-application-restart-section>", StringComparison.Ordinal);
        var reset = view.IndexOf("data-factory-reset-section>", StringComparison.Ordinal);
        Assert.True(restart >= 0 && reset > restart, "Restart card must precede Factory Reset.");
        Assert.Contains("configuration-operation-body", view[restart..reset]);
        Assert.Contains("configuration-operation-actions", view[restart..reset]);
        var restartBanner = view[view.IndexOf("data-restart-banner", StringComparison.Ordinal)..view.IndexOf("data-configuration-workspace", StringComparison.Ordinal)];
        Assert.DoesNotContain("Pending restart", restartBanner);
        Assert.Contains("data-application-restart-open", restartBanner);
        Assert.Collection(Regex.Matches(view, "data-application-restart-open").Cast<Match>(),
            match => Assert.NotEmpty(match.Value),
            match => Assert.NotEmpty(match.Value));
        Assert.Single(Regex.Matches(view, "data-application-restart-dialog").Cast<Match>());
        Assert.Contains("Url.Action(\"Restart\", \"MauiApplicationRestart\")", view);
        Assert.Contains("Url.Action(\"Status\", \"MauiApplicationRestart\")", view);
        Assert.Contains("MauiLifecyclePaths.BrowserUrl(Context.Request.PathBase, MauiLifecyclePaths.Login)", view);
        Assert.Contains("document.querySelectorAll('[data-application-restart-open]')", script);
        Assert.Contains("'RequestVerificationToken': token", script);
        Assert.Contains("current.instanceId !== result.instanceId", script);
        Assert.Contains("configuration-application-restart.js", view);
    }

    [Fact]
    public void BrowserIdleTimerUsesEffectiveStartupPolicy_NotSavedButInactiveSettings()
    {
        var layout = Read("Views/Shared/_Layout.cshtml");
        Assert.Contains("GetRequiredService<MoveItAutomationUI.Services.Configuration.ConfigurationStartupState>()", layout);
        Assert.Contains(".IdleSessionTimeoutMinutes;", layout);
        Assert.DoesNotContain("MoveItOptionsAccessor.Value.IdleSessionTimeoutMinutes.Value", layout);
    }

    [Fact]
    public void RestartPortPreservesRc2SpecificSettingsAndFactoryReset()
    {
        var view = Read("Views/Configuration/Settings.cshtml");
        foreach (var label in new[] { "Enable Quick Transfer", "Import Row Limit", "Use MAUI Task Editor", "Factory Reset" })
            Assert.Contains(label, view);
    }

    private static string Read(string path)
    {
        DirectoryInfo? folder = new(AppContext.BaseDirectory);
        for (var i = 0; folder is not null && i < 12; folder = folder.Parent, i++)
        {
            var source = Path.Combine(folder.FullName, path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(source)) return File.ReadAllText(source);
        }
        throw new FileNotFoundException("Source file not found", path);
    }
}
