// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Determines whether the deployed MAUI package requires an authenticated administrator upgrade.

using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.Metadata;

namespace MoveItAutomationUI.Services.Upgrade;

public sealed class MauiUpgradeStartupService
{
    private readonly IConfiguration _configuration;
    private readonly IMauiUpgradeManifestProvider _manifestProvider;
    private readonly MauiUpgradeState _state;
    private readonly MauiDatabaseSchemaManager _schemaManager;
    private readonly MauiSetupState _setupState;
    private readonly ILogger<MauiUpgradeStartupService> _logger;

    // Purpose: Initializes MAUI upgrade startup service with its required dependencies.
    public MauiUpgradeStartupService(
        IConfiguration configuration,
        IMauiUpgradeManifestProvider manifestProvider,
        MauiUpgradeState state,
        MauiDatabaseSchemaManager schemaManager,
        MauiSetupState setupState,
        ILogger<MauiUpgradeStartupService> logger)
    {
        _configuration = configuration;
        _manifestProvider = manifestProvider;
        _state = state;
        _schemaManager = schemaManager;
        _setupState = setupState;
        _logger = logger;
    }

    // Purpose: Inspects installed application, configuration, and schema versions for pending upgrades.
    public async Task InitializeAsync(CancellationToken cancellationToken = default, bool afterInitialSetup = false)
    {
        var manifest = _manifestProvider.Manifest;
        var installedVersion = _configuration[$"{UiOptions.SectionName}:Version"]?.Trim() ?? string.Empty;
        var configurationVersion = _configuration.GetValue<int?>("UpgradeState:ConfigurationVersion")
            ?? MauiDatabaseSchemaManager.InitialSchemaVersion;

        // First-run setup owns an unconfigured installation. The database provider/connection may still
        // be the template defaults, so upgrade detection deliberately does not touch the database yet.
        if (!_setupState.IsConfiguredAtStartup && !afterInitialSetup)
        {
            _state.Initialize(installedVersion, manifest, manifest.RequiredSchemaVersion, configurationVersion);
            return;
        }

        string? startupError = null;
        int schemaVersion;
        try
        {
            var inspection = await _schemaManager.InspectAsync(manifest, cancellationToken);
            schemaVersion = inspection.Version;
        }
        catch (Exception ex)
        {
            schemaVersion = 0;
            startupError = $"Unable to inspect the MAUI metadata database: {ex.Message}";
            _logger.LogError(ex, "Unable to inspect MAUI database state during upgrade detection.");
        }

        if (startupError is null && MauiVersionRules.IsOlder(installedVersion, manifest.MinimumSupportedVersion))
        {
            startupError = $"MAUI version {installedVersion} is older than the direct-upgrade compatibility floor for this package ({manifest.MinimumSupportedVersion}). Use a MAUI package that supports this installed version or restore a supported application/configuration/database baseline.";
        }
        if (startupError is null && schemaVersion > manifest.RequiredSchemaVersion)
        {
            startupError = $"The MAUI database schema is version {schemaVersion}, but this package supports schema version {manifest.RequiredSchemaVersion}. Deploy a newer MAUI package.";
        }
        if (startupError is null && configurationVersion > manifest.RequiredConfigurationVersion)
        {
            startupError = $"The MAUI configuration is version {configurationVersion}, but this package supports configuration version {manifest.RequiredConfigurationVersion}. Deploy a newer MAUI package.";
        }
        if (startupError is null && MauiVersionRules.IsNewer(installedVersion, manifest.TargetVersion))
        {
            startupError = $"appsettings.json reports MAUI version {installedVersion}, which is newer than the deployed package {manifest.TargetVersion}. Deploy the matching or newer package.";
        }

        _state.Initialize(installedVersion, manifest, schemaVersion, configurationVersion, startupError);
        _logger.LogInformation(
            "MAUI upgrade state: installed {InstalledVersion}, package {TargetVersion}, schema {SchemaVersion}/{TargetSchemaVersion}, configuration {ConfigurationVersion}/{TargetConfigurationVersion}, pending {Pending}.",
            installedVersion,
            manifest.TargetVersion,
            schemaVersion,
            manifest.RequiredSchemaVersion,
            configurationVersion,
            manifest.RequiredConfigurationVersion,
            _state.IsPending);
    }

}
