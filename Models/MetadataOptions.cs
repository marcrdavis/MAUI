// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

public sealed class MetadataDatabaseOptions
{
    public const string SectionName = "MetadataDatabase";

    public SettingDefinition<string> Provider { get; set; } = new()
    {
        Name = "Database Provider",
        Value = "SQLite",
        Description = "Database provider used for UI-only metadata and hierarchy information.",
        Type = "List"
    };

    public SettingDefinition<string> ConnectionString { get; set; } = new()
    {
        Name = "Connection String",
        Value = @"Data Source=Data\maui.db",
        Description = "Connection string for the selected MAUI metadata database provider. SQLite relative paths are resolved from the MAUI application folder; absolute paths are also supported. SQL authentication credentials, when used, are stored in this value.",
        Type = "String"
    };
}

public sealed class MetadataUiOptions
{
    public const string SectionName = "Metadata";

    public ObjectMetadataDefinition Tasks { get; set; } = new();
    public ObjectMetadataDefinition Hosts { get; set; } = new();
    public ObjectMetadataDefinition Scripts { get; set; } = new();
    public ObjectMetadataDefinition DateLists { get; set; } = new();
    public ObjectMetadataDefinition GlobalParameters { get; set; } = new();
    public ObjectMetadataDefinition KeysAndCertificates { get; set; } = new();
}

public sealed class ObjectMetadataDefinition
{
    public List<MetadataFieldDefinition> Fields { get; set; } = new();
}

public sealed class MetadataFieldDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = "String";
    public List<string> PossibleValues { get; set; } = new();
    public bool Required { get; set; }
}
