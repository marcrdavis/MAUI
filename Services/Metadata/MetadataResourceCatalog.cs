// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Single source of truth for MAUI metadata-capable resource types.

using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.Metadata;

internal sealed record MetadataResourceDescriptor(
    string ConfigurationKey,
    string Label,
    string ObjectType,
    string GridKey,
    IReadOnlyList<string> AllowedFieldTypes,
    Func<MetadataUiOptions, ObjectMetadataDefinition> DefinitionSelector,
    IReadOnlyList<string> Aliases)
{
    public ObjectMetadataDefinition GetDefinition(MetadataUiOptions options) => DefinitionSelector(options);
}

internal static class MetadataResourceCatalog
{
    private static readonly string[] StandardFieldTypes =
    {
        "String", "MultilineString", "Email", "Tags", "List", "Boolean", "Numeric", "Date"
    };

    private static readonly string[] GlobalParameterFieldTypes =
    {
        "String", "MultilineString", "Email", "Tags", "Numeric", "Date"
    };

    private static readonly string[] KeyCertificateFieldTypes =
    {
        "String", "MultilineString", "Email", "Tags"
    };

    public static readonly IReadOnlyList<MetadataResourceDescriptor> Resources = new[]
    {
        new MetadataResourceDescriptor(
            "Tasks", "Tasks", "Task", "tasks", StandardFieldTypes,
            options => options.Tasks,
            new[] { "Task", "Tasks" }),
        new MetadataResourceDescriptor(
            "Hosts", "Hosts", "Host", "hosts", StandardFieldTypes,
            options => options.Hosts,
            new[] { "Host", "Hosts" }),
        new MetadataResourceDescriptor(
            "Scripts", "Scripts", "Script", "scripts", StandardFieldTypes,
            options => options.Scripts,
            new[] { "Script", "Scripts" }),
        new MetadataResourceDescriptor(
            "DateLists", "Date Lists", "DateList", "datelists", StandardFieldTypes,
            options => options.DateLists,
            new[] { "DateList", "DateLists", "Date List", "Date Lists" }),
        new MetadataResourceDescriptor(
            "GlobalParameters", "Global Parameters", "GlobalParameter", "globalparameters", GlobalParameterFieldTypes,
            options => options.GlobalParameters,
            new[] { "GlobalParameter", "GlobalParameters", "Global Parameter", "Global Parameters" }),
        new MetadataResourceDescriptor(
            "KeysAndCertificates", "Keys & Certificates", "KeyCertificate", "keysandcertificates", KeyCertificateFieldTypes,
            options => options.KeysAndCertificates,
            new[] { "KeyCertificate", "KeyCertificates", "KeysAndCertificates", "Keys & Certificates", "Keys and Certificates" })
    };

    private static readonly IReadOnlyDictionary<string, MetadataResourceDescriptor> ByConfigurationKey = Resources
        .ToDictionary(item => item.ConfigurationKey, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, MetadataResourceDescriptor> ByGridKey = Resources
        .ToDictionary(item => item.GridKey, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, MetadataResourceDescriptor> ByObjectType = Resources
        .ToDictionary(item => item.ObjectType, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, MetadataResourceDescriptor> ByAlias = Resources
        .SelectMany(resource => resource.Aliases.Append(resource.ConfigurationKey).Append(resource.Label).Append(resource.ObjectType)
            .Select(alias => new KeyValuePair<string, MetadataResourceDescriptor>(alias, resource)))
        .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase);

    public static MetadataResourceDescriptor GetByConfigurationKey(string key)
        => ByConfigurationKey.TryGetValue((key ?? string.Empty).Trim(), out var resource)
            ? resource
            : throw new InvalidOperationException($"Metadata resource '{key}' is not supported.");

    public static bool TryGetByGridKey(string key, out MetadataResourceDescriptor resource)
    {
        if (ByGridKey.TryGetValue((key ?? string.Empty).Trim(), out var match))
        {
            resource = match;
            return true;
        }
        resource = null!;
        return false;
    }

    public static MetadataResourceDescriptor GetByObjectType(string objectType)
        => ByObjectType.TryGetValue((objectType ?? string.Empty).Trim(), out var resource)
            ? resource
            : throw new InvalidOperationException($"MAUI custom metadata is not configured for object type '{objectType}'.");

    public static MetadataResourceDescriptor Resolve(string value)
        => ByAlias.TryGetValue((value ?? string.Empty).Trim(), out var resource)
            ? resource
            : throw new InvalidOperationException(
                $"Metadata object type '{value}' is not supported. Supported types are {string.Join(", ", Resources.Select(item => item.Label))}.");
}
