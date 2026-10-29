using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Configuration;
using MoveItAutomationUI.Services.Configuration;

namespace MoveItAutomationUI.Tests.Configuration;

public sealed class AppSettingsRestrictedIisTests
{
    [Fact]
    public async Task SetupSeed_FromPrecreatedEmptyObject_UsesReleaseTemplate()
    {
        using var fixture = new Installation();
        File.WriteAllText(fixture.LivePath, "{}\n");
        File.WriteAllText(fixture.TemplatePath, """{"MoveIt":{"Servers":[]},"Ui":{"Version":"0.17.306","Theme":{"Value":"light"}}}""");

        var result = await fixture.Store.ReadSeedAsync();

        Assert.NotNull(result["MoveIt"]?["Servers"]);
        Assert.Equal("0.17.306", result["Ui"]?["Version"]?.GetValue<string>());
        Assert.Equal("light", result["Ui"]?["Theme"]?["Value"]?.GetValue<string>());
        Assert.Equal("{}\n", File.ReadAllText(fixture.LivePath));
    }

    [Fact]
    public async Task SetupSeed_PreservesNestedAdministratorOverridesAndUnknownProperties()
    {
        using var fixture = new Installation();
        File.WriteAllText(fixture.LivePath, """{"Ui":{"Theme":{"Value":"dark"},"Custom":{"Enabled":true}}}""");
        File.WriteAllText(fixture.TemplatePath, """{"Ui":{"Theme":{"Value":"light","Description":"Preserved"},"Version":"0.17.306"},"MoveIt":{"Servers":[]}}""");

        var result = await fixture.Store.ReadSeedAsync();

        Assert.Equal("dark", result["Ui"]?["Theme"]?["Value"]?.GetValue<string>());
        Assert.Equal("Preserved", result["Ui"]?["Theme"]?["Description"]?.GetValue<string>());
        Assert.True(result["Ui"]?["Custom"]?["Enabled"]?.GetValue<bool>());
        Assert.NotNull(result["MoveIt"]);
    }

    [Fact]
    public async Task SetupSeed_FailsClearlyWhenTheDeploymentDidNotCreateAppsettings()
    {
        using var fixture = new Installation();
        File.WriteAllText(fixture.TemplatePath, "{}");

        var error = await Assert.ThrowsAsync<AppSettingsDeploymentException>(() => fixture.Store.ReadSeedAsync());

        Assert.Contains("appsettings.json is missing", error.Message);
        Assert.False(File.Exists(fixture.LivePath));
    }

    [Fact]
    public async Task SetupSeed_RejectsZeroLengthPlaceholderRatherThanTreatingItAsJson()
    {
        using var fixture = new Installation();
        File.WriteAllText(fixture.LivePath, "");
        File.WriteAllText(fixture.TemplatePath, "{}");

        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Store.ReadSeedAsync());
        Assert.Equal(0L, new FileInfo(fixture.LivePath).Length);
    }

    [Fact]
    public async Task WriteLive_UsesExistingFileAndKeepsOnlyDataRecoveryFiles()
    {
        using var fixture = new Installation();
        File.WriteAllText(fixture.LivePath, """{"Ui":{"Version":"old"}}""");
        var root = JsonNode.Parse("""{"Ui":{"Version":"new"}}""")!.AsObject();

        await fixture.Store.WriteLiveAsync(root);

        Assert.Equal("new", fixture.Store.ReadLive()["Ui"]?["Version"]?.GetValue<string>());
        Assert.Equal("old", JsonNode.Parse(File.ReadAllText(fixture.RecoveryPath))?["Ui"]?["Version"]?.GetValue<string>());
        Assert.Equal("appsettings.json", Path.GetFileName(Assert.Single(Directory.GetFiles(fixture.Root))));
        Assert.False(File.Exists(fixture.LivePath + ".tmp"));
    }

    [Fact]
    public async Task WriteLive_ReloadsRuntimeConfigurationWithoutRestart()
    {
        using var fixture = new Installation();
        File.WriteAllText(fixture.LivePath, """{"Ui":{"Version":"old"}}""");
        fixture.Configuration.Reload();
        Assert.Equal("old", fixture.Configuration["Ui:Version"]);

        await fixture.Store.WriteLiveAsync(JsonNode.Parse("""{"Ui":{"Version":"new"}}""")!.AsObject());

        Assert.Equal("new", fixture.Configuration["Ui:Version"]);
        Assert.Equal("new", fixture.Store.ReadLive()["Ui"]?["Version"]?.GetValue<string>());
    }

    [Fact]
    public async Task WriteLive_BackupRestoreMode_LeavesRuntimeSnapshotPendingRestart()
    {
        using var fixture = new Installation();
        File.WriteAllText(fixture.LivePath, """{"Ui":{"Version":"active"}}""");
        fixture.Configuration.Reload();

        await fixture.Store.WriteLiveAsync(
            JsonNode.Parse("""{"Ui":{"Version":"restored"}}""")!.AsObject(),
            publishRuntime: false);

        Assert.Equal("restored", fixture.Store.ReadLive()["Ui"]?["Version"]?.GetValue<string>());
        Assert.Equal("active", fixture.Configuration["Ui:Version"]);
        fixture.Configuration.Reload();
        Assert.Equal("restored", fixture.Configuration["Ui:Version"]);
    }

    [Fact]
    public async Task WriteLive_WhenRuntimeReloadFails_RestoresPreviousDocumentAndRuntime()
    {
        using var fixture = new Installation();
        File.WriteAllText(fixture.LivePath, """{"Ui":{"Version":"original"}}""");
        fixture.Configuration.Reload();
        var failingConfiguration = new FailOneReloadConfiguration(fixture.Configuration);
        var store = new AppSettingsDocumentStore(new TestEnvironment(fixture.Root), failingConfiguration);

        var error = await Assert.ThrowsAsync<AppSettingsDeploymentException>(
            () => store.WriteLiveAsync(JsonNode.Parse("""{"Ui":{"Version":"candidate"}}""")!.AsObject()));

        Assert.Contains("previous configuration was restored", error.Message);
        Assert.Equal("original", store.ReadLive()["Ui"]?["Version"]?.GetValue<string>());
        Assert.Equal("original", fixture.Configuration["Ui:Version"]);
    }

    [Fact]
    public void UpgradeSafetyCopy_GoesUnderData_NotTheApplicationRoot()
    {
        using var fixture = new Installation();
        File.WriteAllText(fixture.LivePath, "{}");

        var backup = fixture.Store.CreateLiveBackup("restore-123");

        Assert.StartsWith(Path.Combine(fixture.Root, "Data", "ConfigurationRecovery") + Path.DirectorySeparatorChar, backup);
        Assert.True(File.Exists(backup));
        Assert.Equal("{}", File.ReadAllText(backup));
        Assert.Single(Directory.GetFiles(fixture.Root));
    }

    [Fact]
    public async Task InvalidExistingConfiguration_IsNeverOverwritten()
    {
        using var fixture = new Installation();
        File.WriteAllText(fixture.LivePath, "NOT JSON");

        await Assert.ThrowsAsync<AppSettingsDeploymentException>(
            () => fixture.Store.WriteLiveAsync(new JsonObject { ["Example"] = 1 }));
        Assert.Equal("NOT JSON", File.ReadAllText(fixture.LivePath));
    }

    private sealed class Installation : IDisposable
    {
        public Installation()
        {
            Root = Path.Combine(Path.GetTempPath(), "maui-restricted-iis-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Configuration = new ConfigurationBuilder()
                .SetBasePath(Root)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .Build();
            Store = new AppSettingsDocumentStore(new TestEnvironment(Root), Configuration);
        }

        public string Root { get; }
        public AppSettingsDocumentStore Store { get; }
        public IConfigurationRoot Configuration { get; }
        public string LivePath => Path.Combine(Root, "appsettings.json");
        public string TemplatePath => Path.Combine(Root, "appsettings.template.json");
        public string RecoveryPath => Path.Combine(Root, "Data", "ConfigurationRecovery", "appsettings.lastgood.json");
        public void Dispose() { try { Directory.Delete(Root, recursive: true); } catch { } }
    }

    // Inject a deterministic one-time reload failure to verify both persisted and runtime rollback.
    private sealed class FailOneReloadConfiguration : IConfigurationRoot
    {
        private readonly IConfigurationRoot _inner;
        private bool _fail = true;
        public FailOneReloadConfiguration(IConfigurationRoot inner) => _inner = inner;
        public IEnumerable<IConfigurationProvider> Providers => _inner.Providers;
        public string? this[string key] { get => _inner[key]; set => _inner[key] = value; }
        public IEnumerable<IConfigurationSection> GetChildren() => _inner.GetChildren();
        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => _inner.GetReloadToken();
        public IConfigurationSection GetSection(string key) => _inner.GetSection(key);
        public void Reload()
        {
            if (_fail) { _fail = false; throw new InvalidOperationException("Test-controlled reload failure."); }
            _inner.Reload();
        }
        public void Dispose() { /* Fixture owns the inner configuration. */ }
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public TestEnvironment(string root) { ContentRootPath = root; WebRootPath = Path.Combine(root, "wwwroot"); }
        public string ApplicationName { get; set; } = "MAUI.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
