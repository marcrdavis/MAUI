// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Verifies MOVEit server topology is live while other startup-sensitive settings remain restart-gated.

using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;

namespace MoveItAutomationUI.Tests.Configuration;

public sealed class MoveItServerHotReloadTests
{
    [Fact]
    public void LiveCatalog_AddEditRemove_AppliesAfterConfigurationReload()
    {
        var root = Path.Combine(Path.GetTempPath(), "maui-server-catalog-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "appsettings.json");
        try
        {
            WriteServers(file, Server("prod", "https://prod.example.invalid"));
            var configuration = new ConfigurationBuilder()
                .SetBasePath(root)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .Build();
            var catalog = new MoveItServerCatalog(configuration);

            Assert.Equal("https://prod.example.invalid", catalog.FindByKey("PROD")?.WebAdminUrl);
            Assert.Null(catalog.FindByKey("qa"));

            WriteServers(file,
                Server("prod", "https://prod2.example.invalid"),
                Server("qa", "https://qa.example.invalid"));
            configuration.Reload();

            Assert.Collection(
                catalog.GetServers().OrderBy(server => server.Key, StringComparer.OrdinalIgnoreCase),
                server => Assert.Equal("prod", server.Key),
                server => Assert.Equal("qa", server.Key));
            Assert.Equal("https://prod2.example.invalid", catalog.FindForLogin("PROD")?.WebAdminUrl);
            Assert.Equal("qa", catalog.FindForLogin("QA Friendly")?.Key);

            WriteServers(file, Server("qa", "https://qa.example.invalid"));
            configuration.Reload();
            Assert.Null(catalog.FindByKey("prod"));
            Assert.Single(catalog.GetServers());
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void RestartDetector_ChangingOnlyServers_DoesNotRequireRestart()
    {
        var baseOptions = new MoveItOptions { Servers = new List<MoveItServerDefinition> { Server("prod", "https://prod.example.invalid") } };
        var database = new MetadataDatabaseOptions();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MoveIt:RequestTimeoutSeconds:Value"] = baseOptions.RequestTimeoutSeconds.Value.ToString(),
                ["MoveIt:IdleSessionTimeoutMinutes:Value"] = baseOptions.IdleSessionTimeoutMinutes.Value.ToString(),
                ["MoveIt:TaskRunHistoryMaxCount:Value"] = baseOptions.TaskRunHistoryMaxCount.Value.ToString(),
                ["MoveIt:AllowInvalidServerCertificates:Value"] = "false",
                ["MetadataDatabase:Provider:Value"] = database.Provider.Value,
                ["MetadataDatabase:ConnectionString:Value"] = database.ConnectionString.Value
            }).Build();
        var detector = new ConfigurationRestartDetector(ConfigurationStartupState.Capture(configuration));

        baseOptions.Servers = new List<MoveItServerDefinition> { Server("qa", "https://qa.example.invalid") };
        Assert.Empty(detector.GetRestartReasons(baseOptions, database));

        baseOptions.AllowInvalidServerCertificates.Value = true;
        Assert.Contains(detector.GetRestartReasons(baseOptions, database), reason => reason.Contains("certificate", StringComparison.OrdinalIgnoreCase));
        baseOptions.AllowInvalidServerCertificates.Value = false;
        baseOptions.RequestTimeoutSeconds.Value++;
        Assert.Contains(detector.GetRestartReasons(baseOptions, database), reason => reason.Contains("runtime", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DataAccessCredentials_AreNotPreservedAcrossEndpointOrEnvironmentChanges()
    {
        var original = Server("prod", "https://prod.example.invalid");
        original.ServerHost = "prod-com";
        var edited = new ConfigurationServerSaveRequest
        {
            Key = "prod", EnvironmentKey = "prod", DisplayName = "Renamed Production",
            WebAdminUrl = "https://prod.example.invalid/", ServerHost = "PROD-COM"
        };
        Assert.True(ConfigurationSettingsMutator.SameConnectionIdentity(original, edited));
        edited.WebAdminUrl = "https://another.example.invalid";
        Assert.False(ConfigurationSettingsMutator.SameConnectionIdentity(original, edited));
        edited.WebAdminUrl = original.WebAdminUrl;
        edited.EnvironmentKey = "qa";
        Assert.False(ConfigurationSettingsMutator.SameConnectionIdentity(original, edited));
        edited.EnvironmentKey = "prod";
        edited.ServerHost = "new-com";
        Assert.False(ConfigurationSettingsMutator.SameConnectionIdentity(original, edited));
    }

    [Fact]
    public void DataAccessCredentials_EnvironmentKeyFallback_MatchesServerKey()
    {
        var original = Server("prod", "https://prod.example.invalid");
        original.EnvironmentKey = string.Empty; // persisted legacy server definitions can omit this field
        var edited = new ConfigurationServerSaveRequest
        {
            Key = "prod", EnvironmentKey = "", DisplayName = "Renamed Production",
            WebAdminUrl = "https://prod.example.invalid/", ServerHost = ""
        };

        Assert.True(ConfigurationSettingsMutator.SameConnectionIdentity(original, edited));
        edited.EnvironmentKey = " prod ";
        Assert.True(ConfigurationSettingsMutator.SameConnectionIdentity(original, edited));
        edited.EnvironmentKey = "qa";
        Assert.False(ConfigurationSettingsMutator.SameConnectionIdentity(original, edited));
    }

    private static MoveItServerDefinition Server(string key, string url) => new()
    {
        Key = key,
        EnvironmentKey = key,
        DisplayName = key.Equals("qa", StringComparison.OrdinalIgnoreCase) ? "QA Friendly" : "Production",
        WebAdminUrl = url
    };

    private static void WriteServers(string path, params MoveItServerDefinition[] servers)
        => File.WriteAllText(path, JsonSerializer.Serialize(new { MoveIt = new { Servers = servers } }));
}
