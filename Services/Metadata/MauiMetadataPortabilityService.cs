// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Xml.Linq;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.Metadata;

public interface IMauiMetadataPortabilityService
{
    // Purpose: Builds export element from the supplied data.
    Task<XElement> BuildExportElementAsync(string environmentKey, string objectType, string objectId, CancellationToken cancellationToken = default);
    // Purpose: Parses a portable MAUI package envelope into its validated model.
    PortableMetadataPackage ParseFromEnvelope(XElement exportRoot, string expectedObjectType);
    // Purpose: Validates imported MAUI metadata against the current resource metadata definition.
    Task<MetadataImportSummary> ValidateAsync(string environmentKey, PortableMetadataPackage package, string expectedObjectType, CancellationToken cancellationToken = default);
    // Purpose: Applies compatible to the current MAUI state.
    Task<int> ApplyCompatibleAsync(string environmentKey, string objectType, string objectId, PortableMetadataPackage package, string username, CancellationToken cancellationToken = default);
}

public sealed record PortableMetadataField(string Name, string Type, string Value);
public sealed record PortableMetadataPackage(int SchemaVersion, string ObjectType, IReadOnlyList<PortableMetadataField> Fields);
public sealed record MetadataImportSummary(
    int SchemaVersion,
    int ExportedFields,
    int CompatibleFields,
    IReadOnlyList<string> Issues,
    bool RequiresAcknowledgement);

/// <summary>
/// Owns the portable representation and compatibility rules for MAUI custom metadata.
/// Audit fields (Created/Modified By/On), folders, and other MAUI tracking data are intentionally excluded.
/// </summary>
public sealed class MauiMetadataPortabilityService : IMauiMetadataPortabilityService
{
    public const int SchemaVersion = 1;

    private readonly IMetadataRepository _metadata;
    private readonly MetadataUiOptions _options;
    private readonly ITagRepository _tags;

    // Purpose: Initializes MAUI metadata portability service with its required dependencies.
    public MauiMetadataPortabilityService(
        IMetadataRepository metadata,
        ITagRepository tags,
        IOptionsSnapshot<MetadataUiOptions> options)
    {
        _metadata = metadata;
        _tags = tags;
        _options = options.Value;
    }

    // Purpose: Builds export element from the supplied data.
    public async Task<XElement> BuildExportElementAsync(
        string environmentKey,
        string objectType,
        string objectId,
        CancellationToken cancellationToken = default)
    {
        var definition = GetDefinition(objectType);
        var configuredFields = GetConfiguredFields(definition);
        var snapshot = await _metadata.GetObjectMetadataAsync(environmentKey, objectType, objectId, cancellationToken);

        return new XElement("MauiMetadata",
            new XAttribute("ObjectType", NormalizeObjectType(objectType)),
            configuredFields.Select(field =>
            {
                snapshot.Values.TryGetValue(field.Name, out var value);
                return new XElement("Field",
                    new XAttribute("Name", field.Name),
                    new XAttribute("Type", NormalizeFieldType(field.Type)),
                    value ?? string.Empty);
            }));
    }

    // Purpose: Parses a portable MAUI package envelope into its validated model.
    public PortableMetadataPackage ParseFromEnvelope(XElement exportRoot, string expectedObjectType)
    {
        if (!int.TryParse((string?)exportRoot.Attribute("MetadataSchemaVersion"), out var schemaVersion) || schemaVersion != SchemaVersion)
        {
            throw new InvalidOperationException(
                $"This MAUI export metadata schema is not supported. Expected metadata schema version {SchemaVersion}.");
        }

        var metadataElements = exportRoot.Elements()
            .Where(element => element.Name.LocalName.Equals("MauiMetadata", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (metadataElements.Count != 1)
            throw new InvalidOperationException("The MAUI export must contain exactly one custom metadata block.");
        var metadataElement = metadataElements[0];
        if (metadataElement.Elements().Any(element => !element.Name.LocalName.Equals("Field", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The MAUI metadata block contains an unsupported element for this metadata schema version.");

        var objectType = NormalizeObjectType((string?)metadataElement.Attribute("ObjectType"));
        var expected = NormalizeObjectType(expectedObjectType);
        if (!objectType.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The MAUI metadata block is for object type '{objectType}', but this import expects '{expected}'.");
        }

        var fields = new List<PortableMetadataField>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fieldElement in metadataElement.Elements())
        {
            if (fieldElement.Elements().Any())
                throw new InvalidOperationException("MAUI metadata Field elements cannot contain nested elements.");
            var name = ((string?)fieldElement.Attribute("Name") ?? string.Empty).Trim();
            var type = NormalizeFieldType((string?)fieldElement.Attribute("Type"));
            if (name.Length == 0) throw new InvalidOperationException("The MAUI metadata block contains a field without a Name.");
            if (type.Length == 0) throw new InvalidOperationException($"MAUI metadata field '{name}' does not specify a Type.");
            if (!names.Add(name)) throw new InvalidOperationException($"The MAUI metadata block contains duplicate field '{name}'.");
            fields.Add(new PortableMetadataField(name, type, fieldElement.Value ?? string.Empty));
        }

        return new PortableMetadataPackage(schemaVersion, objectType, fields);
    }

    // Purpose: Validates imported MAUI metadata against the current resource metadata definition.
    public async Task<MetadataImportSummary> ValidateAsync(string environmentKey, PortableMetadataPackage package, string expectedObjectType, CancellationToken cancellationToken = default)
    {
        var expected = NormalizeObjectType(expectedObjectType);
        if (!package.ObjectType.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            return new MetadataImportSummary(package.SchemaVersion, package.Fields.Count, 0,
                new[] { $"Metadata object type '{package.ObjectType}' is not valid for {expected}." }, true);
        }

        var configured = GetConfiguredFields(GetDefinition(expected))
            .ToDictionary(field => field.Name, StringComparer.OrdinalIgnoreCase);
        var compatible = 0;
        var issues = new List<string>();

        foreach (var importedField in package.Fields)
        {
            if (!configured.TryGetValue(importedField.Name, out var currentField))
            {
                issues.Add($"Metadata field '{importedField.Name}' is not configured for {expected} in this MAUI instance.");
                continue;
            }

            var currentType = NormalizeFieldType(currentField.Type);
            if (!currentType.Equals(importedField.Type, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"Metadata field '{importedField.Name}' is type '{importedField.Type}' in the export but '{currentType}' in this MAUI instance.");
                continue;
            }

            if (currentField.Required && string.IsNullOrWhiteSpace(importedField.Value))
            {
                issues.Add($"Metadata field '{importedField.Name}' is required in this MAUI instance but the exported value is empty.");
                continue;
            }

            if (currentType.Equals("List", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(importedField.Value)
                && currentField.PossibleValues.Count > 0
                && !currentField.PossibleValues.Any(value => string.Equals(value, importedField.Value, StringComparison.OrdinalIgnoreCase)))
            {
                issues.Add($"Metadata field '{importedField.Name}' contains value '{importedField.Value}', which is not an allowed value in this MAUI instance.");
                continue;
            }

            if (currentType.Equals("Email", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(importedField.Value)
                && !MetadataValueValidation.TryNormalizeEmailList(importedField.Value, out _))
            {
                issues.Add($"Metadata field '{importedField.Name}' contains an invalid email address list.");
                continue;
            }

            if (currentType.Equals("Tags", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(importedField.Value))
            {
                try { _ = await _tags.NormalizeAssignmentAsync(environmentKey, importedField.Value, cancellationToken); }
                catch (ArgumentException ex)
                {
                    issues.Add($"Metadata field '{importedField.Name}' contains an invalid Tag assignment: {ex.Message}");
                    continue;
                }
            }

            compatible++;
        }

        return new MetadataImportSummary(package.SchemaVersion, package.Fields.Count, compatible, issues, issues.Count > 0);
    }

    // Purpose: Applies compatible to the current MAUI state.
    public async Task<int> ApplyCompatibleAsync(
        string environmentKey,
        string objectType,
        string objectId,
        PortableMetadataPackage package,
        string username,
        CancellationToken cancellationToken = default)
    {
        var summary = await ValidateAsync(environmentKey, package, objectType, cancellationToken);
        var configured = GetConfiguredFields(GetDefinition(objectType))
            .ToDictionary(field => field.Name, StringComparer.OrdinalIgnoreCase);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var importedField in package.Fields)
        {
            if (!configured.TryGetValue(importedField.Name, out var currentField)) continue;
            var currentType = NormalizeFieldType(currentField.Type);
            if (!currentType.Equals(importedField.Type, StringComparison.OrdinalIgnoreCase)) continue;
            if (currentField.Required && string.IsNullOrWhiteSpace(importedField.Value)) continue;
            if (currentType.Equals("List", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(importedField.Value)
                && currentField.PossibleValues.Count > 0
                && !currentField.PossibleValues.Any(value => string.Equals(value, importedField.Value, StringComparison.OrdinalIgnoreCase))) continue;
            if (currentType.Equals("Email", StringComparison.OrdinalIgnoreCase))
            {
                if (!MetadataValueValidation.TryNormalizeEmailList(importedField.Value, out var normalizedEmail)) continue;
                values[importedField.Name] = normalizedEmail;
                continue;
            }
            if (currentType.Equals("Tags", StringComparison.OrdinalIgnoreCase))
            {
                try { values[importedField.Name] = await _tags.NormalizeAssignmentAsync(environmentKey, importedField.Value, cancellationToken); }
                catch (ArgumentException) { continue; }
                continue;
            }
            values[importedField.Name] = importedField.Value ?? string.Empty;
        }

        if (values.Count > 0)
        {
            await _metadata.SaveObjectMetadataAsync(environmentKey, NormalizeObjectType(objectType), objectId, values, username, cancellationToken);
        }

        return summary.CompatibleFields;
    }

    // Purpose: Returns the configured metadata definition from the shared metadata resource catalog.
    private ObjectMetadataDefinition GetDefinition(string objectType)
        => MetadataResourceCatalog.GetByObjectType(NormalizeObjectType(objectType)).GetDefinition(_options);

    // Purpose: Returns configured fields.
    private static IReadOnlyList<MetadataFieldDefinition> GetConfiguredFields(ObjectMetadataDefinition definition)
    {
        var fields = definition.Fields
            .Where(field => !string.IsNullOrWhiteSpace(field.Name))
            .Select(field => new MetadataFieldDefinition
            {
                Name = field.Name.Trim(),
                Label = field.Label,
                Description = field.Description,
                Type = NormalizeFieldType(field.Type),
                PossibleValues = field.PossibleValues ?? new List<string>(),
                Required = field.Required
            })
            .ToList();

        var duplicate = fields.GroupBy(field => field.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"MAUI metadata configuration contains duplicate field '{duplicate.Key}'.");
        return fields;
    }

    // Purpose: Normalizes object type.
    private static string NormalizeObjectType(string? objectType)
    {
        var value = (objectType ?? string.Empty).Trim();
        if (value.Length == 0) throw new InvalidOperationException("MAUI metadata ObjectType is required.");
        return value;
    }

    // Purpose: Normalizes field type.
    private static string NormalizeFieldType(string? type) => (type ?? string.Empty).Trim();
}
