// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Declarative application, configuration, and database upgrade models.

using System.Text.Json.Nodes;

namespace MoveItAutomationUI.Models;

public sealed class MauiUpgradeManifest
{
    public int ManifestVersion { get; set; } = 1;
    public string Description { get; set; } = string.Empty;
    public string TargetVersion { get; set; } = string.Empty;
    public string MinimumSupportedVersion { get; set; } = string.Empty;
    public int RequiredSchemaVersion { get; set; } = 1;
    public int RequiredConfigurationVersion { get; set; } = 1;
    // Optional release-level requirement; older manifests default to no forced restart.
    public bool RestartRequired { get; set; } = false;
    public List<MauiSchemaMigrationDefinition> SchemaMigrations { get; set; } = new();
    public List<MauiConfigurationMigrationDefinition> ConfigurationMigrations { get; set; } = new();
}

public sealed class MauiSchemaMigrationDefinition
{
    public int FromVersion { get; set; }
    public int ToVersion { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<MauiSchemaOperationDefinition> Operations { get; set; } = new();
}

public sealed class MauiSchemaOperationDefinition
{
    public string Operation { get; set; } = string.Empty;
    public string Table { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NewName { get; set; } = string.Empty;
    public MauiSchemaColumnDefinition? Column { get; set; }
    public List<MauiSchemaColumnDefinition> Columns { get; set; } = new();
    public MauiSchemaPrimaryKeyDefinition? PrimaryKey { get; set; }
    public List<MauiSchemaForeignKeyDefinition> ForeignKeys { get; set; } = new();
    public List<string> IndexColumns { get; set; } = new();
    public bool Unique { get; set; }
    public string SqliteSql { get; set; } = string.Empty;
    public string SqlServerSql { get; set; } = string.Empty;
}

public sealed class MauiSchemaPrimaryKeyDefinition
{
    public string Name { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = new();
}

public sealed class MauiSchemaForeignKeyDefinition
{
    public string Name { get; set; } = string.Empty;
    public List<string> Columns { get; set; } = new();
    public string PrincipalTable { get; set; } = string.Empty;
    public List<string> PrincipalColumns { get; set; } = new();
    public string OnDelete { get; set; } = "NoAction";
}

public sealed class MauiSchemaColumnDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "String";
    public int? Length { get; set; }
    public bool Nullable { get; set; } = true;
    public bool PrimaryKey { get; set; }
    public bool AutoIncrement { get; set; }
    public JsonNode? DefaultValue { get; set; }
}

public sealed class MauiConfigurationMigrationDefinition
{
    public int FromVersion { get; set; }
    public int ToVersion { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<MauiConfigurationOperationDefinition> Operations { get; set; } = new();
}

public sealed class MauiConfigurationOperationDefinition
{
    public string Operation { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string NewPath { get; set; } = string.Empty;
    public JsonNode? Value { get; set; }
    public JsonNode? NewValue { get; set; }
}

public sealed class MauiUpgradeViewModel
{
    public string InstalledVersion { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string TargetVersion { get; init; } = string.Empty;
    public int CurrentSchemaVersion { get; init; }
    public int TargetSchemaVersion { get; init; }
    public int CurrentConfigurationVersion { get; init; }
    public int TargetConfigurationVersion { get; init; }
    public bool PendingRestart { get; init; }
    public string? StartupError { get; init; }
}

public sealed class MauiUpgradeProgress
{
    public string Stage { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed class MauiUpgradeExecutionResult
{
    public string FromVersion { get; init; } = string.Empty;
    public string ToVersion { get; init; } = string.Empty;
    public int SchemaVersion { get; init; }
    public int ConfigurationVersion { get; init; }
    public string BackupFile { get; init; } = string.Empty;
    public string DatabaseBackupFile { get; init; } = string.Empty;
    public bool RestartRequired { get; init; }
    public IReadOnlyList<string> RestartReasons { get; init; } = Array.Empty<string>();
}

