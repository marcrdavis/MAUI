namespace MoveItAutomationUI.Tests.UI;

public sealed class MauiTextChangesTests
{
    [Fact]
    public void TaskNotifications_FooterSaysCloseWithoutChangingItsAction()
    {
        var view = ReadSource("Views/Tasks/Index.cshtml");
        Assert.Contains("data-task-notification-cancel>Close</button>", view);
        Assert.DoesNotContain("data-task-notification-cancel>Cancel</button>", view);

        // Verify the runtime reset used before every opening does not override the Razor label.
        var script = ReadSource("wwwroot/js/tasks.js");
        var resetStart = script.IndexOf("function resetNotificationDialog() {", StringComparison.Ordinal);
        Assert.True(resetStart >= 0, "Notification reset handler was not found.");
        var resetEnd = script.IndexOf("async function openTaskNotifications(", resetStart, StringComparison.Ordinal);
        Assert.True(resetEnd > resetStart, "Notification reset handler was not delimited correctly.");
        var reset = script[resetStart..resetEnd];
        Assert.Contains("notificationCancel.textContent = 'Close';", reset);
        Assert.DoesNotContain("notificationCancel.textContent = 'Cancel';", reset);
        Assert.Contains("[notificationCancel, notificationClose].forEach(button => button?.addEventListener('click', () => { if (!notificationBusy) notificationDialog?.close(); }));", script);
    }

    [Fact]
    public void FactoryReset_AdvancedWarningUsesRevisedText()
    {
        var view = ReadSource("Views/Configuration/Settings.cshtml");
        Assert.Contains(
            "Resets the <code>appsettings.json</code> and removes the installation's <code>Data</code> folder contents, including any local SQLite database",
            view);
        Assert.DoesNotContain("Clears <code>appsettings.json</code> to <code>{}</code>", view);
        Assert.Contains("MAUI will sign you out, stop its application host, and complete deletion before the next startup. This cannot be undone.", view);
    }

    private static string ReadSource(string relativePath)
    {
        foreach (var origin in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var dir = new DirectoryInfo(origin); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "MoveItAutomationUI.csproj")))
                    return File.ReadAllText(Path.Combine(dir.FullName, relativePath));
            }
        }

        throw new DirectoryNotFoundException("MAUI source root not found.");
    }
}
