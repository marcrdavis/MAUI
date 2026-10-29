// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Executes one controlled, cumulative MAUI upgrade using declarative instructions from upgrade.json.

using System.Text.Json.Nodes;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Backup;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.Metadata;

namespace MoveItAutomationUI.Services.Upgrade;

public interface IMauiUpgradeService
{
    // Purpose: Returns the current upgrade state formatted for the Upgrade page.
    MauiUpgradeViewModel GetViewModel();
    Task<MauiUpgradeExecutionResult> ExecuteAsync(
        string performedBy,
        Func<MauiUpgradeProgress, Task> progress,
        CancellationToken cancellationToken = default);
}

public sealed class MauiUpgradeService : IMauiUpgradeService
{
    private readonly IMauiUpgradeManifestProvider _manifestProvider;
    private readonly MauiUpgradeState _state;
    private readonly MauiDatabaseSchemaManager _schemaManager;
    private readonly AppSettingsWriteCoordinator _writeCoordinator;
    private readonly IAppSettingsDocumentStore _appSettings;
    private readonly IMauiMetadataDatabaseBackupService _metadataDatabaseBackup;
    private readonly IConfigurationRestartDetector _restartDetector;
    private readonly ILogger<MauiUpgradeService> _logger;

    // Purpose: Initializes MAUI upgrade service with its required dependencies.
    public MauiUpgradeService(
        IMauiUpgradeManifestProvider manifestProvider,
        MauiUpgradeState state,
        MauiDatabaseSchemaManager schemaManager,
        AppSettingsWriteCoordinator writeCoordinator,
        IAppSettingsDocumentStore appSettings,
        IMauiMetadataDatabaseBackupService metadataDatabaseBackup,
        IConfigurationRestartDetector restartDetector,
        ILogger<MauiUpgradeService> logger)
    {
        _manifestProvider = manifestProvider;
        _state = state;
        _schemaManager = schemaManager;
        _writeCoordinator = writeCoordinator;
        _appSettings = appSettings;
        _metadataDatabaseBackup = metadataDatabaseBackup;
        _restartDetector = restartDetector;
        _logger = logger;
    }

    // Purpose: Returns the current upgrade state formatted for the Upgrade page.
    public MauiUpgradeViewModel GetViewModel() => _state.ToViewModel();

    // Purpose: Executes the pending sequential configuration and schema upgrade.
    public async Task<MauiUpgradeExecutionResult> ExecuteAsync(
        string performedBy,
        Func<MauiUpgradeProgress, Task> progress,
        CancellationToken cancellationToken = default)
    {
        if (!_state.IsPending) throw new InvalidOperationException("No MAUI upgrade is pending.");
        if (_state.PendingRestart) throw new InvalidOperationException("The MAUI upgrade has already completed. Restart the application before continuing.");
        if (!string.IsNullOrWhiteSpace(_state.StartupError)) throw new InvalidOperationException(_state.StartupError);
        performedBy = performedBy?.Trim() ?? string.Empty;
        if (performedBy.Length == 0) throw new InvalidOperationException("The MAUI administrator identity could not be determined.");

        if (!await _state.ExecutionGate.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("A MAUI upgrade is already running.");

        var writeGateHeld = false;
        try
        {
            await _writeCoordinator.Gate.WaitAsync(cancellationToken);
            writeGateHeld = true;
            var manifest = _manifestProvider.Manifest;
            var fromVersion = _state.InstalledVersion;
            ValidateMinimumSupportedVersion(fromVersion, manifest.MinimumSupportedVersion);
            await ReportAsync(progress, "prepare", "Preparing upgrade...");

            var sourceRoot = await _appSettings.ReadLiveAsync(cancellationToken);

            var stagedRoot = sourceRoot.DeepClone().AsObject();
            var stagedConfigurationVersion = _state.CurrentConfigurationVersion;
            while (stagedConfigurationVersion < manifest.RequiredConfigurationVersion)
            {
                var migration = FindConfigurationMigration(manifest, stagedConfigurationVersion);
                ApplyConfigurationMigration(stagedRoot, migration);
                stagedConfigurationVersion = migration.ToVersion;
            }

            // Validate all configuration instructions before any persistent change is made. Final version
            // markers are staged now but written only after every database migration succeeds.
            SetConfigurationVersion(stagedRoot, manifest.RequiredConfigurationVersion);
            SetApplicationVersion(stagedRoot, manifest.TargetVersion);

            await ReportAsync(progress, "backup", "Backing up configuration...");
            var backupPath = _appSettings.CreateLiveBackup(manifest.TargetVersion);

            var schemaVersion = _state.CurrentSchemaVersion;
            if (schemaVersion > manifest.RequiredSchemaVersion)
                throw new InvalidOperationException($"The MAUI database schema is version {schemaVersion}, which is newer than this package supports ({manifest.RequiredSchemaVersion}).");

            var inspection = await _schemaManager.InspectAsync(manifest, cancellationToken);
            schemaVersion = inspection.Version;
            _state.UpdateProgress(schemaVersion, _state.CurrentConfigurationVersion);

            string databaseBackupPath = string.Empty;
            if (schemaVersion < manifest.RequiredSchemaVersion)
            {
                if (_metadataDatabaseBackup.IsSqlite)
                {
                    await ReportAsync(progress, "backup", "Backing up SQLite metadata database...");
                    databaseBackupPath = CreatePreUpgradeDatabaseBackupPath(manifest.TargetVersion);
                    await _metadataDatabaseBackup.BackupSqliteAsync(databaseBackupPath, cancellationToken);
                    _logger.LogInformation(
                        "Created pre-upgrade SQLite metadata database snapshot {DatabaseBackupPath} before schema migration {CurrentSchemaVersion}->{TargetSchemaVersion}.",
                        databaseBackupPath,
                        schemaVersion,
                        manifest.RequiredSchemaVersion);
                }
                else
                {
                    _logger.LogInformation(
                        "Automatic pre-upgrade metadata database snapshot skipped because provider {DatabaseProvider} is configured. SQL Server backups remain administrator-managed.",
                        _metadataDatabaseBackup.Provider);
                }
            }

            while (schemaVersion < manifest.RequiredSchemaVersion)
            {
                var migration = FindSchemaMigration(manifest, schemaVersion);
                var description = string.IsNullOrWhiteSpace(migration.Description)
                    ? $"Updating database schema {migration.FromVersion} → {migration.ToVersion}..."
                    : $"Updating database schema {migration.FromVersion} → {migration.ToVersion}: {migration.Description}";
                await ReportAsync(progress, "schema", description);
                await _schemaManager.ApplyMigrationAsync(migration, manifest.RequiredSchemaVersion, cancellationToken);
                schemaVersion = migration.ToVersion;
                _state.UpdateProgress(schemaVersion, _state.CurrentConfigurationVersion);
            }

            if (_state.CurrentConfigurationVersion < manifest.RequiredConfigurationVersion)
                await ReportAsync(progress, "configuration", "Updating application configuration...");
            else
                await ReportAsync(progress, "configuration", "Validating application configuration...");

            // Use the same startup-sensitive policy as Configuration > Settings. Version markers,
            // schema-only changes, and reload-safe configuration migrations do not require a restart
            // unless the release manifest explicitly requests one.
            var restartReasons = _restartDetector.GetRestartReasons(stagedRoot).ToList();
            if (manifest.RestartRequired)
                restartReasons.Add("The release manifest requires an application restart.");

            await ReportAsync(progress, "finalize", "Finalizing upgrade...");
            await _appSettings.WriteLiveAsync(stagedRoot, cancellationToken);

            _state.UpdateProgress(schemaVersion, manifest.RequiredConfigurationVersion);
            if (restartReasons.Count > 0)
                _state.MarkCompletedPendingRestart(schemaVersion, manifest.RequiredConfigurationVersion);
            else
                _state.MarkCompleted(schemaVersion, manifest.RequiredConfigurationVersion);

            _logger.LogInformation(
                "MAUI upgrade completed by {Username}: {FromVersion} -> {ToVersion}, schema {SchemaVersion}, configuration {ConfigurationVersion}, restart required {RestartRequired}. Reasons: {RestartReasons}",
                performedBy,
                fromVersion,
                manifest.TargetVersion,
                schemaVersion,
                manifest.RequiredConfigurationVersion,
                restartReasons.Count > 0,
                restartReasons.Count == 0 ? "None" : string.Join(" | ", restartReasons));

            await ReportAsync(progress, "complete", restartReasons.Count > 0
                ? "Upgrade complete. Select Restart MAUI to continue."
                : "Upgrade complete.");
            return new MauiUpgradeExecutionResult
            {
                FromVersion = fromVersion,
                ToVersion = manifest.TargetVersion,
                SchemaVersion = schemaVersion,
                ConfigurationVersion = manifest.RequiredConfigurationVersion,
                BackupFile = GetInstallationRelativePath(backupPath),
                DatabaseBackupFile = string.IsNullOrWhiteSpace(databaseBackupPath)
                    ? string.Empty
                    : GetInstallationRelativePath(databaseBackupPath),
                RestartRequired = restartReasons.Count > 0,
                RestartReasons = restartReasons
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MAUI upgrade failed while upgrading from {InstalledVersion} to {TargetVersion}.", _state.InstalledVersion, _state.TargetVersion);
            throw;
        }
        finally
        {
            if (writeGateHeld) _writeCoordinator.Gate.Release();
            _state.ExecutionGate.Release();
        }
    }

    // Purpose: Finds schema migration.
    private static MauiSchemaMigrationDefinition FindSchemaMigration(MauiUpgradeManifest manifest, int fromVersion)
    {
        return manifest.SchemaMigrations.SingleOrDefault(item => item.FromVersion == fromVersion && item.ToVersion == fromVersion + 1)
            ?? throw new InvalidOperationException($"upgrade.json does not contain the required schema migration {fromVersion}->{fromVersion + 1}.");
    }

    // Purpose: Finds configuration migration.
    private static MauiConfigurationMigrationDefinition FindConfigurationMigration(MauiUpgradeManifest manifest, int fromVersion)
    {
        return manifest.ConfigurationMigrations.SingleOrDefault(item => item.FromVersion == fromVersion && item.ToVersion == fromVersion + 1)
            ?? throw new InvalidOperationException($"upgrade.json does not contain the required configuration migration {fromVersion}->{fromVersion + 1}.");
    }

    // Purpose: Applies configuration migration.
    private static void ApplyConfigurationMigration(JsonObject root, MauiConfigurationMigrationDefinition migration)
    {
        foreach (var operation in migration.Operations)
        {
            var kind = operation.Operation?.Trim() ?? string.Empty;
            if (kind.Equals("AddIfMissing", StringComparison.OrdinalIgnoreCase))
            {
                AddIfMissing(root, operation.Path, operation.Value);
            }
            else if (kind.Equals("AddArrayItemIfMissing", StringComparison.OrdinalIgnoreCase))
            {
                AddArrayItemIfMissing(root, operation.Path, operation.Value);
            }
            else if (kind.Equals("RemoveArrayItemIfPresent", StringComparison.OrdinalIgnoreCase))
            {
                RemoveArrayItemIfPresent(root, operation.Path, operation.Value);
            }
            else if (kind.Equals("ReplaceArrayItemIfPresent", StringComparison.OrdinalIgnoreCase))
            {
                ReplaceArrayItemIfPresent(root, operation.Path, operation.Value, operation.NewValue);
            }
            else if (kind.Equals("SetValue", StringComparison.OrdinalIgnoreCase))
            {
                SetPath(root, operation.Path, operation.Value?.DeepClone(), createParents: true);
            }
            else if (kind.Equals("Remove", StringComparison.OrdinalIgnoreCase))
            {
                RemovePath(root, operation.Path);
            }
            else if (kind.Equals("Rename", StringComparison.OrdinalIgnoreCase) || kind.Equals("Move", StringComparison.OrdinalIgnoreCase))
            {
                MovePath(root, operation.Path, operation.NewPath);
            }
            else
            {
                throw new InvalidOperationException($"Unsupported configuration operation '{operation.Operation}'.");
            }
        }
    }

    // Purpose: Adds if missing.
    private static void AddIfMissing(JsonObject root, string path, JsonNode? value)
    {
        if (TryGetPath(root, path, out _)) return;
        SetPath(root, path, value?.DeepClone(), createParents: true);
    }

    // Purpose: Appends a value to an existing configuration array only when an equivalent item is not already present.
    private static void AddArrayItemIfMissing(JsonObject root, string path, JsonNode? value)
    {
        if (value is null)
            throw new InvalidOperationException($"Configuration array operation for '{path}' requires Value.");

        var array = GetRequiredArray(root, path);
        if (array.Any(item => JsonNode.DeepEquals(item, value))) return;
        array.Add(value.DeepClone());
    }

    // Purpose: Removes every equivalent value from an existing configuration array when present.
    private static void RemoveArrayItemIfPresent(JsonObject root, string path, JsonNode? value)
    {
        if (value is null)
            throw new InvalidOperationException($"Configuration array operation for '{path}' requires Value.");

        var array = GetRequiredArray(root, path);
        for (var index = array.Count - 1; index >= 0; index--)
        {
            if (JsonNode.DeepEquals(array[index], value)) array.RemoveAt(index);
        }
    }

    // Purpose: Replaces an existing configuration array value in place without creating duplicates.
    private static void ReplaceArrayItemIfPresent(JsonObject root, string path, JsonNode? value, JsonNode? newValue)
    {
        if (value is null)
            throw new InvalidOperationException($"Configuration array operation for '{path}' requires Value.");
        if (newValue is null)
            throw new InvalidOperationException($"Configuration array operation for '{path}' requires NewValue.");
        if (JsonNode.DeepEquals(value, newValue)) return;

        var array = GetRequiredArray(root, path);
        var matches = Enumerable.Range(0, array.Count)
            .Where(index => JsonNode.DeepEquals(array[index], value))
            .ToArray();
        if (matches.Length == 0) return;

        var replacementAlreadyExists = array.Any(item => JsonNode.DeepEquals(item, newValue));
        if (!replacementAlreadyExists)
        {
            array[matches[0]] = newValue.DeepClone();
            matches = matches.Skip(1).ToArray();
        }

        foreach (var index in matches.OrderByDescending(index => index))
        {
            array.RemoveAt(index);
        }
    }

    // Purpose: Returns an existing configuration array or fails without manufacturing structure.
    private static JsonArray GetRequiredArray(JsonObject root, string path)
    {
        if (!TryGetPath(root, path, out var target))
            throw new InvalidOperationException($"Configuration array path '{path}' does not exist.");
        if (target is not JsonArray array)
            throw new InvalidOperationException($"Configuration path '{path}' is not an array.");
        return array;
    }

    // Purpose: Moves a configuration value between manifest-defined JSON paths.
    private static void MovePath(JsonObject root, string sourcePath, string destinationPath)
    {
        if (TryGetPath(root, destinationPath, out _))
        {
            RemovePath(root, sourcePath);
            return;
        }
        if (!TryGetPath(root, sourcePath, out var value))
            throw new InvalidOperationException($"Configuration migration source '{sourcePath}' does not exist.");

        SetPath(root, destinationPath, value?.DeepClone(), createParents: true);
        RemovePath(root, sourcePath);
    }

    // Purpose: Attempts to retrieve path.
    private static bool TryGetPath(JsonObject root, string path, out JsonNode? value)
    {
        var parts = SplitPath(path);
        JsonNode? current = root;
        foreach (var part in parts)
        {
            if (current is not JsonObject currentObject || !currentObject.TryGetPropertyValue(part, out current))
            {
                value = null;
                return false;
            }
        }
        value = current;
        return true;
    }

    // Purpose: Sets path.
    private static void SetPath(JsonObject root, string path, JsonNode? value, bool createParents)
    {
        var parts = SplitPath(path);
        JsonObject current = root;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            var part = parts[index];
            if (current[part] is JsonObject child)
            {
                current = child;
                continue;
            }
            if (!createParents)
                throw new InvalidOperationException($"Configuration path '{string.Join(".", parts.Take(index + 1))}' does not exist.");
            if (current[part] is not null)
                throw new InvalidOperationException($"Configuration path '{string.Join(".", parts.Take(index + 1))}' is not an object.");
            child = new JsonObject();
            current[part] = child;
            current = child;
        }
        current[parts[^1]] = value;
    }

    // Purpose: Removes path.
    private static void RemovePath(JsonObject root, string path)
    {
        var parts = SplitPath(path);
        JsonObject current = root;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            if (current[parts[index]] is not JsonObject child) return;
            current = child;
        }
        current.Remove(parts[^1]);
    }

    // Purpose: Splits and validates a dot-delimited configuration path.
    private static string[] SplitPath(string? path)
    {
        var parts = (path ?? string.Empty).Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new InvalidOperationException("Configuration migration Path is required.");
        return parts;
    }

    // Purpose: Sets configuration version.
    private static void SetConfigurationVersion(JsonObject root, int version)
    {
        var state = root["UpgradeState"] as JsonObject;
        if (state is null)
        {
            state = new JsonObject();
            root["UpgradeState"] = state;
        }
        state["ConfigurationVersion"] = version;
        state["LastUpgradeUtc"] = DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
    }

    // Purpose: Sets application version.
    private static void SetApplicationVersion(JsonObject root, string version)
    {
        var ui = AppSettingsJson.RequiredSection(root, UiOptions.SectionName);
        ui["Version"] = version;
    }

    // Purpose: Validates minimum supported version.
    private static void ValidateMinimumSupportedVersion(string installedVersion, string minimumSupportedVersion)
    {
        if (string.IsNullOrWhiteSpace(minimumSupportedVersion)) return;
        if (!MauiVersionRules.TryParse(installedVersion, out var installed) || !MauiVersionRules.TryParse(minimumSupportedVersion, out var minimum))
            throw new InvalidOperationException($"Unable to validate upgrade compatibility from MAUI version '{installedVersion}' to this release.");
        if (installed < minimum)
            throw new InvalidOperationException($"MAUI version {installedVersion} is older than the direct-upgrade compatibility floor for this package ({minimumSupportedVersion}). Use a MAUI package that supports this installed version or restore a supported application/configuration/database baseline.");
    }

    // Purpose: Creates a unique destination for the automatic pre-upgrade SQLite recovery snapshot.
    private string CreatePreUpgradeDatabaseBackupPath(string targetVersion)
    {
        var safeVersion = string.Concat((targetVersion ?? string.Empty)
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_' ? ch : '_'));
        if (safeVersion.Length == 0) safeVersion = "upgrade";

        var root = Path.Combine(
            Path.GetDirectoryName(_appSettings.LivePath) ?? string.Empty,
            "Data",
            "Backups",
            "PreUpgrade");
        Directory.CreateDirectory(root);

        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        var folder = Path.Combine(root, $"{stamp}_{safeVersion}");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "maui.db");
    }

    // Purpose: Returns an installation-relative path suitable for displaying outside the server log.
    private string GetInstallationRelativePath(string path)
    {
        var root = Path.GetDirectoryName(_appSettings.LivePath) ?? string.Empty;
        return Path.GetRelativePath(root, path)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }

    // Purpose: Reports one Upgrade progress stage to the browser callback.
    private static Task ReportAsync(Func<MauiUpgradeProgress, Task> progress, string stage, string message) =>
        progress(new MauiUpgradeProgress { Stage = stage, Message = message });
}

