// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Loads and validates the cumulative upgrade manifest shipped with each MAUI release.

using System.Text.Json;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;

namespace MoveItAutomationUI.Services.Upgrade;

public interface IMauiUpgradeManifestProvider
{
    MauiUpgradeManifest Manifest { get; }
    string ManifestPath { get; }
}

public sealed class MauiUpgradeManifestProvider : IMauiUpgradeManifestProvider
{
    public MauiUpgradeManifest Manifest { get; }
    public string ManifestPath { get; }

    // Purpose: Initializes MAUI upgrade manifest provider with its required dependencies.
    public MauiUpgradeManifestProvider(
        IWebHostEnvironment environment,
        IAppSettingsDocumentStore appSettings)
    {
        ManifestPath = Path.Combine(environment.ContentRootPath, "upgrade.json");
        if (!File.Exists(ManifestPath))
        {
            throw new InvalidOperationException($"MAUI upgrade manifest was not found at '{ManifestPath}'.");
        }

        try
        {
            var json = File.ReadAllText(ManifestPath);
            Manifest = JsonSerializer.Deserialize<MauiUpgradeManifest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidOperationException("upgrade.json did not contain a valid MAUI upgrade manifest.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("upgrade.json is not valid JSON.", ex);
        }

        Validate(Manifest);
        ValidateReleaseContract(Manifest, appSettings.ReadTemplate());
    }

    // Purpose: Validates the release upgrade manifest and its sequential migration definitions.
    private static void Validate(MauiUpgradeManifest manifest)
    {
        if (manifest.ManifestVersion != 1)
            throw new InvalidOperationException($"Unsupported upgrade manifest version {manifest.ManifestVersion}. This build supports manifest version 1.");
        if (string.IsNullOrWhiteSpace(manifest.Description))
            throw new InvalidOperationException("upgrade.json Description is required.");
        if (string.IsNullOrWhiteSpace(manifest.TargetVersion))
            throw new InvalidOperationException("upgrade.json TargetVersion is required.");
        if (!MauiVersionRules.TryParse(manifest.TargetVersion, out var targetVersion))
            throw new InvalidOperationException($"upgrade.json TargetVersion '{manifest.TargetVersion}' is not a valid MAUI version.");
        if (!string.IsNullOrWhiteSpace(manifest.MinimumSupportedVersion))
        {
            if (!MauiVersionRules.TryParse(manifest.MinimumSupportedVersion, out var minimumSupportedVersion))
                throw new InvalidOperationException($"upgrade.json MinimumSupportedVersion '{manifest.MinimumSupportedVersion}' is not a valid MAUI version.");
            if (minimumSupportedVersion > targetVersion)
                throw new InvalidOperationException($"upgrade.json MinimumSupportedVersion '{manifest.MinimumSupportedVersion}' cannot be newer than TargetVersion '{manifest.TargetVersion}'.");
        }
        if (manifest.RequiredSchemaVersion < 1)
            throw new InvalidOperationException("upgrade.json RequiredSchemaVersion must be at least 1.");
        if (manifest.RequiredConfigurationVersion < 1)
            throw new InvalidOperationException("upgrade.json RequiredConfigurationVersion must be at least 1.");

        ValidateSchemaMigrations(manifest);
        ValidateConfigurationMigrations(manifest);
    }

    // Purpose: Validates schema migrations.
    private static void ValidateSchemaMigrations(MauiUpgradeManifest manifest)
    {
        var ordered = manifest.SchemaMigrations.OrderBy(item => item.FromVersion).ToArray();
        var seen = new HashSet<int>();
        foreach (var migration in ordered)
        {
            if (migration.FromVersion < 0 || migration.ToVersion != migration.FromVersion + 1)
                throw new InvalidOperationException($"Schema migration {migration.FromVersion}->{migration.ToVersion} must advance exactly one version.");
            if (!seen.Add(migration.FromVersion))
                throw new InvalidOperationException($"upgrade.json contains more than one schema migration beginning at version {migration.FromVersion}.");
            if (migration.ToVersion > manifest.RequiredSchemaVersion)
                throw new InvalidOperationException($"Schema migration {migration.FromVersion}->{migration.ToVersion} exceeds RequiredSchemaVersion {manifest.RequiredSchemaVersion}.");
            foreach (var operation in migration.Operations)
            {
                ValidateSchemaOperation(migration, operation);
            }
        }

        // The cumulative manifest owns the complete physical schema lifecycle, including initial
        // provisioning. Version 0 represents an empty database, so every release must retain a
        // complete sequential path from 0 through RequiredSchemaVersion.
        for (var version = 0; version < manifest.RequiredSchemaVersion; version++)
        {
            if (!seen.Contains(version))
                throw new InvalidOperationException($"upgrade.json is missing the sequential schema migration {version}->{version + 1}.");
        }

        var initialMigration = ordered.Single(item => item.FromVersion == 0 && item.ToVersion == 1);
        if (!initialMigration.Operations.Any(operation =>
                operation.Operation.Equals("CreateTable", StringComparison.OrdinalIgnoreCase)
                && operation.Table.Equals("MauiSchemaVersion", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Schema migration 0->1 must create the MauiSchemaVersion table.");
        }
    }

    // Purpose: Validates configuration migrations.
    private static void ValidateConfigurationMigrations(MauiUpgradeManifest manifest)
    {
        var ordered = manifest.ConfigurationMigrations.OrderBy(item => item.FromVersion).ToArray();
        var seen = new HashSet<int>();
        foreach (var migration in ordered)
        {
            if (migration.FromVersion < 1 || migration.ToVersion != migration.FromVersion + 1)
                throw new InvalidOperationException($"Configuration migration {migration.FromVersion}->{migration.ToVersion} must advance exactly one version.");
            if (!seen.Add(migration.FromVersion))
                throw new InvalidOperationException($"upgrade.json contains more than one configuration migration beginning at version {migration.FromVersion}.");
            if (migration.ToVersion > manifest.RequiredConfigurationVersion)
                throw new InvalidOperationException($"Configuration migration {migration.FromVersion}->{migration.ToVersion} exceeds RequiredConfigurationVersion {manifest.RequiredConfigurationVersion}.");
            foreach (var operation in migration.Operations)
            {
                ValidateConfigurationOperation(migration, operation);
            }
        }

        if (manifest.RequiredConfigurationVersion > 1)
        {
            for (var version = 1; version < manifest.RequiredConfigurationVersion; version++)
            {
                if (!seen.Contains(version))
                    throw new InvalidOperationException($"upgrade.json is missing the sequential configuration migration {version}->{version + 1}.");
            }
        }
    }
    // Purpose: Validates schema operation.
    private static void ValidateSchemaOperation(MauiSchemaMigrationDefinition migration, MauiSchemaOperationDefinition operation)
    {
        var kind = operation.Operation?.Trim() ?? string.Empty;
        var allowed = new[] { "CreateTable", "AddColumn", "RenameColumn", "DropColumn", "CreateIndex", "DropIndex", "ExecuteSql" };
        if (!allowed.Contains(kind, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Schema migration {migration.FromVersion}->{migration.ToVersion} contains unsupported operation '{operation.Operation}'.");

        if (kind.Equals("ExecuteSql", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(operation.SqliteSql))
                throw new InvalidOperationException($"ExecuteSql in schema migration {migration.FromVersion}->{migration.ToVersion} requires SqliteSql.");
            if (string.IsNullOrWhiteSpace(operation.SqlServerSql))
                throw new InvalidOperationException($"ExecuteSql in schema migration {migration.FromVersion}->{migration.ToVersion} requires SqlServerSql.");
            return;
        }

        if (string.IsNullOrWhiteSpace(operation.Table))
            throw new InvalidOperationException($"Schema operation '{kind}' in migration {migration.FromVersion}->{migration.ToVersion} requires Table.");
        if (kind.Equals("CreateTable", StringComparison.OrdinalIgnoreCase))
        {
            if (operation.Columns.Count == 0)
                throw new InvalidOperationException($"CreateTable in schema migration {migration.FromVersion}->{migration.ToVersion} requires Columns.");

            var columnNames = operation.Columns.Select(column => column.Name?.Trim() ?? string.Empty).ToArray();
            if (columnNames.Any(string.IsNullOrWhiteSpace) || columnNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != columnNames.Length)
                throw new InvalidOperationException($"CreateTable '{operation.Table}' contains blank or duplicate column names.");

            var columnPrimaryKeys = operation.Columns.Where(column => column.PrimaryKey).ToArray();
            if (columnPrimaryKeys.Length > 1)
                throw new InvalidOperationException($"CreateTable '{operation.Table}' can contain only one column-level PrimaryKey.");
            foreach (var column in operation.Columns.Where(column => column.AutoIncrement))
            {
                if (!column.PrimaryKey)
                    throw new InvalidOperationException($"CreateTable '{operation.Table}' column '{column.Name}' uses AutoIncrement without PrimaryKey.");
                if (!string.Equals(column.Type, "Integer", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(column.Type, "Long", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"CreateTable '{operation.Table}' auto-increment column '{column.Name}' must be Integer or Long.");
            }

            if (operation.PrimaryKey is not null)
            {
                if (operation.PrimaryKey.Columns.Count == 0)
                    throw new InvalidOperationException($"CreateTable '{operation.Table}' has an empty PrimaryKey definition.");
                if (operation.Columns.Any(column => column.PrimaryKey))
                    throw new InvalidOperationException($"CreateTable '{operation.Table}' cannot combine a table PrimaryKey with column-level PrimaryKey flags.");
                foreach (var column in operation.PrimaryKey.Columns)
                {
                    if (!columnNames.Contains(column, StringComparer.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"CreateTable '{operation.Table}' primary key references unknown column '{column}'.");
                }
            }

            foreach (var foreignKey in operation.ForeignKeys)
            {
                if (foreignKey.Columns.Count == 0 || foreignKey.Columns.Count != foreignKey.PrincipalColumns.Count)
                    throw new InvalidOperationException($"CreateTable '{operation.Table}' contains an invalid foreign-key column mapping.");
                if (string.IsNullOrWhiteSpace(foreignKey.PrincipalTable))
                    throw new InvalidOperationException($"CreateTable '{operation.Table}' contains a foreign key without PrincipalTable.");
                foreach (var column in foreignKey.Columns)
                {
                    if (!columnNames.Contains(column, StringComparer.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"CreateTable '{operation.Table}' foreign key references unknown local column '{column}'.");
                }
                var deleteBehavior = foreignKey.OnDelete?.Trim() ?? string.Empty;
                var allowedDeleteBehaviors = new[] { "", "NoAction", "Restrict", "Cascade", "SetNull" };
                if (!allowedDeleteBehaviors.Contains(deleteBehavior, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"CreateTable '{operation.Table}' uses unsupported foreign-key delete behavior '{foreignKey.OnDelete}'.");
            }
        }
        if (kind.Equals("AddColumn", StringComparison.OrdinalIgnoreCase) && operation.Column is null)
            throw new InvalidOperationException($"AddColumn in schema migration {migration.FromVersion}->{migration.ToVersion} requires Column.");
        if ((kind.Equals("RenameColumn", StringComparison.OrdinalIgnoreCase)
             || kind.Equals("DropColumn", StringComparison.OrdinalIgnoreCase)
             || kind.Equals("CreateIndex", StringComparison.OrdinalIgnoreCase)
             || kind.Equals("DropIndex", StringComparison.OrdinalIgnoreCase))
            && string.IsNullOrWhiteSpace(operation.Name))
            throw new InvalidOperationException($"Schema operation '{kind}' in migration {migration.FromVersion}->{migration.ToVersion} requires Name.");
        if (kind.Equals("RenameColumn", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(operation.NewName))
            throw new InvalidOperationException($"RenameColumn in schema migration {migration.FromVersion}->{migration.ToVersion} requires NewName.");
        if (kind.Equals("CreateIndex", StringComparison.OrdinalIgnoreCase) && operation.IndexColumns.Count == 0)
            throw new InvalidOperationException($"CreateIndex in schema migration {migration.FromVersion}->{migration.ToVersion} requires IndexColumns.");
    }

    // Purpose: Validates configuration operation.
    private static void ValidateConfigurationOperation(MauiConfigurationMigrationDefinition migration, MauiConfigurationOperationDefinition operation)
    {
        var kind = operation.Operation?.Trim() ?? string.Empty;
        var allowed = new[]
        {
            "AddIfMissing",
            "AddArrayItemIfMissing",
            "RemoveArrayItemIfPresent",
            "ReplaceArrayItemIfPresent",
            "SetValue",
            "Remove",
            "Rename",
            "Move"
        };
        if (!allowed.Contains(kind, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Configuration migration {migration.FromVersion}->{migration.ToVersion} contains unsupported operation '{operation.Operation}'.");
        if (string.IsNullOrWhiteSpace(operation.Path))
            throw new InvalidOperationException($"Configuration operation '{kind}' in migration {migration.FromVersion}->{migration.ToVersion} requires Path.");
        if ((kind.Equals("Rename", StringComparison.OrdinalIgnoreCase) || kind.Equals("Move", StringComparison.OrdinalIgnoreCase))
            && string.IsNullOrWhiteSpace(operation.NewPath))
            throw new InvalidOperationException($"Configuration operation '{kind}' in migration {migration.FromVersion}->{migration.ToVersion} requires NewPath.");
        if ((kind.Equals("AddArrayItemIfMissing", StringComparison.OrdinalIgnoreCase)
             || kind.Equals("RemoveArrayItemIfPresent", StringComparison.OrdinalIgnoreCase)
             || kind.Equals("ReplaceArrayItemIfPresent", StringComparison.OrdinalIgnoreCase))
            && operation.Value is null)
            throw new InvalidOperationException($"Configuration operation '{kind}' in migration {migration.FromVersion}->{migration.ToVersion} requires Value.");
        if (kind.Equals("ReplaceArrayItemIfPresent", StringComparison.OrdinalIgnoreCase) && operation.NewValue is null)
            throw new InvalidOperationException($"Configuration operation '{kind}' in migration {migration.FromVersion}->{migration.ToVersion} requires NewValue.");
    }

    // Purpose: Verifies that the shipped template and upgrade manifest describe the same release.
    private static void ValidateReleaseContract(MauiUpgradeManifest manifest, JsonObject template)
    {
        var templateVersion = template[UiOptions.SectionName]?["Version"]?.GetValue<string>()?.Trim() ?? string.Empty;
        if (!templateVersion.Equals(manifest.TargetVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Release package mismatch: appsettings.template.json Ui.Version is '{templateVersion}', but upgrade.json TargetVersion is '{manifest.TargetVersion}'. Deploy matching files from one complete publish output; preserve appsettings.json and Data.");
        }

        var configurationVersionNode = template["UpgradeState"]?["ConfigurationVersion"]
            ?? throw new InvalidOperationException("appsettings.template.json UpgradeState.ConfigurationVersion is required.");

        int templateConfigurationVersion;
        try
        {
            templateConfigurationVersion = configurationVersionNode.GetValue<int>();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            throw new InvalidOperationException("appsettings.template.json UpgradeState.ConfigurationVersion must be an integer.", ex);
        }

        if (templateConfigurationVersion != manifest.RequiredConfigurationVersion)
        {
            throw new InvalidOperationException(
                $"Release package mismatch: appsettings.template.json configuration version is {templateConfigurationVersion}, but upgrade.json requires {manifest.RequiredConfigurationVersion}. Deploy matching files from one complete publish output; preserve appsettings.json and Data.");
        }
    }

}
