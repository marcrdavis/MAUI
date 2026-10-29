// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.Metadata;

/// <summary>
/// Shared helpers for projecting MAUI metadata fields and audit values into server-side grid definitions.
/// </summary>
public static class MetadataGridSupport
{
    public static readonly IReadOnlyList<string> AuditColumnKeys = new[]
    {
        "MetadataCreatedOn",
        "MetadataCreatedBy",
        "MetadataModifiedOn",
        "MetadataModifiedBy"
    };

    // Returns configured metadata fields with usable names in their configured order.
    public static IReadOnlyList<MetadataFieldDefinition> GetConfiguredFields(ObjectMetadataDefinition definition)
        => definition.Fields
            .Where(field => !string.IsNullOrWhiteSpace(field.Name))
            .ToList();

    // Builds a collision-safe grid key for a configured metadata field.
    public static string ColumnKey(string fieldName) => $"Metadata_{fieldName.Trim()}";

    // Returns configured metadata-field column keys without the standard audit columns.
    public static IEnumerable<string> GetFieldColumnKeys(ObjectMetadataDefinition definition)
        => GetConfiguredFields(definition).Select(field => ColumnKey(field.Name));

    // Returns all metadata and metadata-audit column keys for a grid's available-column list.
    public static IEnumerable<string> GetAvailableColumnKeys(ObjectMetadataDefinition definition)
        => GetFieldColumnKeys(definition).Concat(AuditColumnKeys);

    // Adds configured metadata columns and the standard metadata audit columns to a grid definition.
    public static void AddColumns(ICollection<GridColumn> columns, ObjectMetadataDefinition definition)
    {
        foreach (var field in GetConfiguredFields(definition))
        {
            columns.Add(new GridColumn
            {
                Key = ColumnKey(field.Name),
                Label = string.IsNullOrWhiteSpace(field.Label) ? field.Name : field.Label,
                Type = field.Type.Equals("Tags", StringComparison.OrdinalIgnoreCase) ? "tags" : "text"
            });
        }

        columns.Add(new GridColumn { Key = "MetadataCreatedOn", Label = "Created On", Type = "datetime" });
        columns.Add(new GridColumn { Key = "MetadataCreatedBy", Label = "Created By" });
        columns.Add(new GridColumn { Key = "MetadataModifiedOn", Label = "Modified On", Type = "datetime" });
        columns.Add(new GridColumn { Key = "MetadataModifiedBy", Label = "Modified By" });
    }

    // Adds configured metadata values and standard metadata audit values to display/raw grid dictionaries.
    public static void AddValues(
        IDictionary<string, string> values,
        IDictionary<string, string> rawValues,
        ObjectMetadataDefinition definition,
        ObjectMetadataSnapshot? metadata,
        Func<DateTimeOffset?, string> formatDate)
    {
        foreach (var field in GetConfiguredFields(definition))
        {
            var key = ColumnKey(field.Name);
            var value = metadata is not null && metadata.Values.TryGetValue(field.Name, out var metadataValue)
                ? metadataValue
                : string.Empty;
            values[key] = value;
            rawValues[key] = value;
        }

        values["MetadataCreatedOn"] = formatDate(metadata?.CreatedUtc);
        values["MetadataCreatedBy"] = metadata?.CreatedBy ?? string.Empty;
        values["MetadataModifiedOn"] = formatDate(metadata?.ModifiedUtc);
        values["MetadataModifiedBy"] = metadata?.ModifiedBy ?? string.Empty;

        rawValues["MetadataCreatedOn"] = metadata?.CreatedUtc?.ToString("O") ?? string.Empty;
        rawValues["MetadataCreatedBy"] = metadata?.CreatedBy ?? string.Empty;
        rawValues["MetadataModifiedOn"] = metadata?.ModifiedUtc?.ToString("O") ?? string.Empty;
        rawValues["MetadataModifiedBy"] = metadata?.ModifiedBy ?? string.Empty;
    }
}
