// Source integration guards for RC2 Factory Reset UI, preserving existing Settings layout.
namespace MoveItAutomationUI.Tests.Lifecycle;

public sealed class MauiFactoryResetUiContractTests
{
    [Fact]
    public void FactoryReset_AdvancedCardUsesPaddedSettingsBodyAndInsetAction()
    {
        var view = ReadSource("Views/Configuration/Settings.cshtml");
        var style = ReadSource("wwwroot/css/site.css");
        var start = view.IndexOf("data-factory-reset-section>", StringComparison.Ordinal);
        Assert.True(start >= 0, "Factory Reset card is missing from Advanced settings.");
        var end = view.IndexOf("</section>", start, StringComparison.Ordinal);
        Assert.True(end > start, "Factory Reset must stay inside the Advanced section.");
        var card = view[start..end];
        Assert.Matches(@"(?s)<div class=""configuration-operation-body"">\s*<div class=""configuration-destructive-warning"">.*?</div>\s*<div class=""configuration-operation-actions"">", card);
        Assert.Contains("data-factory-reset-open", card);
        // Reuse the existing configuration panel body rather than introducing one-off spacing.
        Assert.Contains(".configuration-operation-body { display: flex; flex: 1 1 auto; flex-direction: column; gap: 16px; padding: 18px 20px 20px; }", style);
    }

    [Fact]
    public void FactoryReset_ModalKeepsTypedConfirmationAndAntiforgerySubmission()
    {
        var script = ReadSource("wwwroot/js/configuration-factory-reset.js");
        Assert.Contains("submit.disabled = submitting || input.value !== 'RESET';", script);
        Assert.Contains("if (submitting || input.value !== 'RESET') return;", script);
        Assert.Contains("workspace.dataset.factoryResetUrl", script);
        Assert.Contains("'RequestVerificationToken': antiforgery", script);
        Assert.Contains("credentials: 'same-origin'", script);
    }

    [Fact]
    public void FactoryResetStartupHookPrecedesRc2BackgroundJobsAndDataProtection()
    {
        var program = ReadSource("Program.cs");
        var recovery = program.IndexOf("MauiFactoryResetRecovery.CompletePendingReset(", StringComparison.Ordinal);
        var protection = program.IndexOf("MauiDataProtectionConfiguration.AddMauiDataProtection(", StringComparison.Ordinal);
        var quickTransfer = program.IndexOf("AddHostedService<QuickTransferCleanupWorker>", StringComparison.Ordinal);
        Assert.True(recovery >= 0 && recovery < protection && protection < quickTransfer,
            "Reset recovery must run before both Data Protection and RC2 Quick Transfer workers.");
        Assert.Contains("app.UseMiddleware<MauiFactoryResetAccessMiddleware>();", program);
        Assert.Contains("builder.Services.AddScoped<MauiFactoryResetService>();", program);
    }

    [Fact]
    public void FactoryResetPortPreservesRc2OnlySettings()
    {
        var view = ReadSource("Views/Configuration/Settings.cshtml");
        Assert.Contains("Enable Quick Transfer", view);
        Assert.Contains("Import Row Limit", view);
        Assert.Contains("Use MAUI Task Editor", view);
        Assert.Contains("data-factory-reset-url=", view);
        Assert.Contains("configuration-factory-reset.js", view);
    }

    private static string ReadSource(string relative)
    {
        foreach (var origin in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(origin); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "MoveItAutomationUI.csproj")))
                    return File.ReadAllText(Path.Combine(dir.FullName, relative));
            }
        }
        throw new DirectoryNotFoundException("MAUI source root not found.");
    }
}
