// Project: MAUI - MOVEit Automation UI
// Regression test for login server list casting after the live-server catalog introduction.
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Tests.Configuration;

public sealed class LoginServerCollectionContractTests
{
    [Fact]
    public void LoginView_AcceptsCatalogReadOnlyListAndArrays()
    {
        // MoveItServerCatalog.GetServers() returns a MoveItServerDefinition[] exposed through
        // IReadOnlyList. Casting that array to List<T> returned null and hid valid servers.
        IReadOnlyList<MoveItServerDefinition> catalogServers = new[]
        {
            new MoveItServerDefinition
            {
                Key = "prod", DisplayName = "Production", WebAdminUrl = "https://prod.example.invalid"
            }
        };
        object viewBagValue = catalogServers;
        var viewServers = viewBagValue as IReadOnlyList<MoveItServerDefinition>
            ?? Array.Empty<MoveItServerDefinition>();
        Assert.Equal("prod", Assert.Single(viewServers).Key);
        Assert.False(viewBagValue is List<MoveItServerDefinition>);

        // This is the integration seam Razor must continue to use. MVC ViewBag is dynamic,
        // so the bad collection cast otherwise compiles without any warning.
        var razor = File.ReadAllText(Path.Combine(FindSourceRoot(), "Views", "Account", "Login.cshtml"));
        Assert.Contains("ViewBag.Servers as IReadOnlyList<MoveItServerDefinition>", razor);
        Assert.DoesNotContain("ViewBag.Servers as List<MoveItServerDefinition>", razor);
    }

    private static string FindSourceRoot()
    {
        foreach (var origin in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(origin); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "MoveItAutomationUI.csproj")))
                    return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException("The MAUI source root was not found for the login view contract test.");
    }
}
