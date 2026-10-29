// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using MoveItAutomationUI.Services.Metadata;

namespace MoveItAutomationUI.Services.MoveIt;

/// <summary>
/// Defines the MAUI 2.0 ZIP container used by portable resource/report packages.
/// A package contains manifest.json, payload.xml, and maui-metadata.xml.
/// </summary>
internal static class MauiPortablePackage
{
    public const string FormatVersion = "2.0";
    public const int MaxItems = 500;
    public const long MaxPackageBytes = 50L * 1024L * 1024L;
    private const long MaxExpandedEntryBytes = 100L * 1024L * 1024L;
    private const long MaxCharactersInDocument = 50_000_000;
    private const string PackageFormat = "MAUI.PortablePackage";
    private const string PackageKind = "ObjectExport";
    private const string ManifestEntryName = "manifest.json";
    private const string PayloadEntryName = "payload.xml";
    private const string MetadataEntryName = "maui-metadata.xml";

    internal sealed record Item(string SourceId, string Name, string Type, XElement Configuration, XElement Envelope);
    internal sealed record Package(string ObjectType, DateTimeOffset? ExportedUtc, IReadOnlyList<Item> Items);
    private sealed record Manifest(
        string Format,
        string FormatVersion,
        string PackageKind,
        string PackageType,
        string PayloadFile,
        string MetadataFile,
        DateTimeOffset ExportedUtc,
        int ItemCount,
        int MetadataSchemaVersion);

    // Purpose: Builds one MAUI 2.0 ZIP package from one or more same-type resource items.
    public static byte[] Build(string objectType, IReadOnlyList<XElement> items, DateTimeOffset exportedUtc)
    {
        var normalizedType = NormalizeRequired(objectType, "ObjectType");
        if (items is null || items.Count == 0) throw new InvalidOperationException("A MAUI export package must contain at least one item.");
        if (items.Count > MaxItems) throw new InvalidOperationException($"A MAUI export package cannot contain more than {MaxItems} items.");

        var payloadItems = new List<XElement>(items.Count);
        var metadataItems = new List<XElement>(items.Count);
        foreach (var sourceItem in items)
        {
            var item = ValidateBuildItem(sourceItem);
            var metadataObjectType = ((string?)item.Metadata.Attribute("ObjectType") ?? string.Empty).Trim();
            if (metadataObjectType.Length > 0 && !metadataObjectType.Equals(normalizedType, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Item '{item.Name}' contains MAUI metadata for object type '{metadataObjectType}', but the package type is '{normalizedType}'.");
            payloadItems.Add(new XElement("Item",
                new XAttribute("SourceId", item.SourceId),
                new XAttribute("Name", item.Name),
                new XAttribute("Type", item.Type),
                new XElement("Configuration", new XElement(item.Configuration))));
            metadataItems.Add(new XElement("Object",
                new XAttribute("ObjectType", normalizedType),
                new XAttribute("SourceId", item.SourceId),
                new XAttribute("SourceName", item.Name),
                new XAttribute("Type", item.Type),
                new XAttribute("MetadataSchemaVersion", MauiMetadataPortabilityService.SchemaVersion),
                new XElement(item.Metadata)));
        }

        var payloadDocument = new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement("MauiPayload",
                new XAttribute("ObjectType", normalizedType),
                new XAttribute("ExportedUtc", exportedUtc.ToString("O")),
                new XElement("Items", payloadItems)));
        var metadataDocument = new XDocument(new XDeclaration("1.0", "utf-8", null),
            new XElement("MauiMetadataBundle",
                new XAttribute("SchemaVersion", MauiMetadataPortabilityService.SchemaVersion),
                metadataItems));
        var manifest = new Manifest(
            PackageFormat,
            FormatVersion,
            PackageKind,
            normalizedType,
            PayloadEntryName,
            MetadataEntryName,
            exportedUtc,
            items.Count,
            MauiMetadataPortabilityService.SchemaVersion);

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteTextEntry(archive, ManifestEntryName, JsonSerializer.Serialize(manifest, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));
            WriteTextEntry(archive, PayloadEntryName, payloadDocument.ToString(SaveOptions.DisableFormatting));
            WriteTextEntry(archive, MetadataEntryName, metadataDocument.ToString(SaveOptions.DisableFormatting));
        }

        var bytes = output.ToArray();
        if (bytes.LongLength > MaxPackageBytes)
            throw new InvalidOperationException("The MAUI export package exceeds the 50 MB portability limit. Export fewer items.");
        return bytes;
    }

    // Purpose: Creates one portable package item around a native/resource configuration and its MAUI metadata.
    public static XElement BuildItem(string sourceId, string name, string type, XElement configuration, XElement metadata)
    {
        var normalizedName = NormalizeRequired(name, "Name");
        if (configuration is null) throw new InvalidOperationException("A MAUI export item must contain a configuration.");
        if (metadata is null) throw new InvalidOperationException("A MAUI export item must contain MAUI metadata.");

        return new XElement("Item",
            new XAttribute("SourceId", (sourceId ?? string.Empty).Trim()),
            new XAttribute("Name", normalizedName),
            new XAttribute("Type", (type ?? string.Empty).Trim()),
            new XAttribute("MetadataSchemaVersion", MauiMetadataPortabilityService.SchemaVersion),
            new XElement("Configuration", new XElement(configuration)),
            new XElement(metadata));
    }

    // Purpose: Parses and strictly validates one MAUI 2.0 ZIP package.
    public static Package Parse(byte[] bytes, string expectedObjectType, string friendlyLabel)
    {
        if (bytes is null || bytes.Length == 0) throw new InvalidOperationException($"Select a non-empty MAUI {friendlyLabel} export file.");
        if (bytes.LongLength > MaxPackageBytes) throw new InvalidOperationException($"The MAUI {friendlyLabel} export exceeds the 50 MB portability limit.");

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            if (archive.Entries.Count != 3) throw new InvalidOperationException("The MAUI export package contains unsupported files.");

            var manifestEntry = GetRequiredEntry(archive, ManifestEntryName);
            var payloadEntry = GetRequiredEntry(archive, PayloadEntryName);
            var metadataEntry = GetRequiredEntry(archive, MetadataEntryName);

            var manifest = JsonSerializer.Deserialize<Manifest>(ReadTextEntry(manifestEntry), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidOperationException("The MAUI export package manifest is invalid.");

            if (!string.Equals(manifest.Format, PackageFormat, StringComparison.Ordinal))
                throw new InvalidOperationException($"The selected file is not a MAUI {friendlyLabel} export.");
            if (!string.Equals(manifest.FormatVersion, FormatVersion, StringComparison.Ordinal))
                throw new InvalidOperationException($"This MAUI {friendlyLabel} export format is not supported. Expected format version {FormatVersion}.");
            if (!string.Equals(manifest.PackageKind, PackageKind, StringComparison.Ordinal))
                throw new InvalidOperationException($"The selected MAUI package is not a {friendlyLabel} export package.");
            if (!string.Equals(manifest.PayloadFile, PayloadEntryName, StringComparison.Ordinal)
                || !string.Equals(manifest.MetadataFile, MetadataEntryName, StringComparison.Ordinal))
                throw new InvalidOperationException("The MAUI export package manifest references unsupported package files.");
            if (manifest.ItemCount < 1 || manifest.ItemCount > MaxItems)
                throw new InvalidOperationException($"The MAUI export package contains an invalid item count. The supported maximum is {MaxItems}.");
            if (manifest.MetadataSchemaVersion != MauiMetadataPortabilityService.SchemaVersion)
                throw new InvalidOperationException($"This MAUI export metadata schema is not supported. Expected metadata schema version {MauiMetadataPortabilityService.SchemaVersion}.");

            var expected = NormalizeRequired(expectedObjectType, "ObjectType");
            var actual = NormalizeRequired(manifest.PackageType, "PackageType");
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"The selected MAUI package contains {actual} items, but this import expects {expected} items.");

            var payloadDocument = LoadXml(ReadTextEntry(payloadEntry), "MAUI package payload");
            var metadataDocument = LoadXml(ReadTextEntry(metadataEntry), "MAUI metadata bundle");
            var payloadRoot = payloadDocument.Root;
            var metadataRoot = metadataDocument.Root;
            if (payloadRoot is null || !payloadRoot.Name.LocalName.Equals("MauiPayload", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The MAUI export payload is invalid.");
            if (metadataRoot is null || !metadataRoot.Name.LocalName.Equals("MauiMetadataBundle", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The MAUI metadata bundle is invalid.");

            var payloadType = NormalizeRequired((string?)payloadRoot.Attribute("ObjectType"), "Payload ObjectType");
            if (!payloadType.Equals(actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The MAUI package manifest and payload object types do not match.");
            if (!int.TryParse((string?)metadataRoot.Attribute("SchemaVersion"), out var metadataVersion)
                || metadataVersion != MauiMetadataPortabilityService.SchemaVersion)
                throw new InvalidOperationException($"This MAUI export metadata schema is not supported. Expected metadata schema version {MauiMetadataPortabilityService.SchemaVersion}.");

            var payloadItems = GetOnlyItems(payloadRoot, "payload");
            var metadataItems = metadataRoot.Elements().ToList();
            if (metadataItems.Any(element => !element.Name.LocalName.Equals("Object", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("The MAUI metadata bundle contains an unsupported element.");
            if (payloadItems.Count != manifest.ItemCount || metadataItems.Count != manifest.ItemCount)
                throw new InvalidOperationException("The MAUI package item count does not match its manifest.");

            var metadataByKey = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var metadataItem in metadataItems)
            {
                var metadataObjectType = NormalizeRequired((string?)metadataItem.Attribute("ObjectType"), "Metadata ObjectType");
                if (!metadataObjectType.Equals(actual, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The MAUI package manifest and metadata object types do not match.");
                var metadataSourceId = ((string?)metadataItem.Attribute("SourceId") ?? string.Empty).Trim();
                var metadataName = NormalizeRequired((string?)metadataItem.Attribute("SourceName"), "Metadata Item Name");
                var metadataKey = BuildItemKey(metadataSourceId, metadataName);
                if (!metadataByKey.TryAdd(metadataKey, metadataItem))
                    throw new InvalidOperationException($"The MAUI metadata bundle contains duplicate metadata for '{metadataName}'.");
            }

            var parsedItems = new List<Item>(payloadItems.Count);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sourceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var payloadItem in payloadItems)
            {
                var name = NormalizeRequired((string?)payloadItem.Attribute("Name"), "Item Name");
                if (!names.Add(name)) throw new InvalidOperationException($"The MAUI export package contains duplicate item name '{name}'.");
                var sourceId = ((string?)payloadItem.Attribute("SourceId") ?? string.Empty).Trim();
                if (sourceId.Length > 0 && !sourceIds.Add(sourceId)) throw new InvalidOperationException($"The MAUI export package contains duplicate SourceId '{sourceId}'.");
                var type = ((string?)payloadItem.Attribute("Type") ?? string.Empty).Trim();

                if (payloadItem.Elements().Any(element => !element.Name.LocalName.Equals("Configuration", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"Item '{name}' contains an unsupported payload element.");
                var configurations = payloadItem.Elements().Where(element => element.Name.LocalName.Equals("Configuration", StringComparison.OrdinalIgnoreCase)).ToList();
                if (configurations.Count != 1) throw new InvalidOperationException($"Item '{name}' must contain exactly one Configuration block.");
                var nativeConfigurations = configurations[0].Elements().ToList();
                if (nativeConfigurations.Count != 1) throw new InvalidOperationException($"Item '{name}' must contain exactly one configuration object.");

                var metadataKey = BuildItemKey(sourceId, name);
                if (!metadataByKey.TryGetValue(metadataKey, out var metadataItem))
                    throw new InvalidOperationException($"Item '{name}' does not have a matching MAUI metadata entry.");
                var metadataSourceId = ((string?)metadataItem.Attribute("SourceId") ?? string.Empty).Trim();
                var metadataName = NormalizeRequired((string?)metadataItem.Attribute("SourceName"), "Metadata Item Name");
                var metadataTypeCode = ((string?)metadataItem.Attribute("Type") ?? string.Empty).Trim();
                if (!sourceId.Equals(metadataSourceId, StringComparison.OrdinalIgnoreCase)
                    || !name.Equals(metadataName, StringComparison.OrdinalIgnoreCase)
                    || !type.Equals(metadataTypeCode, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Item '{name}' does not match its MAUI metadata identity.");
                if (!int.TryParse((string?)metadataItem.Attribute("MetadataSchemaVersion"), out var itemMetadataVersion)
                    || itemMetadataVersion != MauiMetadataPortabilityService.SchemaVersion)
                    throw new InvalidOperationException($"Item '{name}' does not use the supported MAUI metadata schema version.");
                if (metadataItem.Elements().Any(element => !element.Name.LocalName.Equals("MauiMetadata", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"Item '{name}' contains an unsupported metadata element.");
                var metadataBlocks = metadataItem.Elements().Where(element => element.Name.LocalName.Equals("MauiMetadata", StringComparison.OrdinalIgnoreCase)).ToList();
                if (metadataBlocks.Count != 1)
                    throw new InvalidOperationException($"Item '{name}' must contain exactly one MAUI metadata block.");
                var innerMetadataType = ((string?)metadataBlocks[0].Attribute("ObjectType") ?? string.Empty).Trim();
                if (innerMetadataType.Length > 0 && !innerMetadataType.Equals(actual, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Item '{name}' contains MAUI metadata for object type '{innerMetadataType}', but the package type is '{actual}'.");

                var envelope = new XElement("Item",
                    new XAttribute("SourceId", sourceId),
                    new XAttribute("Name", name),
                    new XAttribute("Type", type),
                    new XAttribute("MetadataSchemaVersion", MauiMetadataPortabilityService.SchemaVersion),
                    new XElement("Configuration", new XElement(nativeConfigurations[0])),
                    new XElement(metadataBlocks[0]));
                parsedItems.Add(new Item(sourceId, name, type, new XElement(nativeConfigurations[0]), envelope));
            }

            if (metadataByKey.Count != parsedItems.Count)
                throw new InvalidOperationException("The MAUI metadata bundle contains an item that is not present in the package payload.");

            DateTimeOffset? exportedUtc = manifest.ExportedUtc;
            if (DateTimeOffset.TryParse((string?)payloadRoot.Attribute("ExportedUtc"), out var payloadExportedUtc)
                && payloadExportedUtc != manifest.ExportedUtc)
                throw new InvalidOperationException("The MAUI package manifest and payload export timestamps do not match.");

            return new Package(actual, exportedUtc, parsedItems);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or XmlException)
        {
            throw new InvalidOperationException($"The selected file is not a valid MAUI {friendlyLabel} export.", ex);
        }
    }

    // Purpose: Builds a stable filename for a multi-item package.
    public static string BuildBulkFileName(string objectType, int count)
    {
        var label = NormalizeRequired(objectType, "ObjectType") switch
        {
            "DateList" => "DateLists",
            var value => value.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? value : value + "s"
        };
        return $"{label}_{count}_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.maui";
    }

    private static (string SourceId, string Name, string Type, XElement Configuration, XElement Metadata) ValidateBuildItem(XElement sourceItem)
    {
        if (sourceItem is null || !sourceItem.Name.LocalName.Equals("Item", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A MAUI export package contains an invalid item.");
        var sourceId = ((string?)sourceItem.Attribute("SourceId") ?? string.Empty).Trim();
        var name = NormalizeRequired((string?)sourceItem.Attribute("Name"), "Item Name");
        var type = ((string?)sourceItem.Attribute("Type") ?? string.Empty).Trim();
        if (!int.TryParse((string?)sourceItem.Attribute("MetadataSchemaVersion"), out var schemaVersion)
            || schemaVersion != MauiMetadataPortabilityService.SchemaVersion)
            throw new InvalidOperationException($"Item '{name}' does not use the supported MAUI metadata schema version.");
        if (sourceItem.Elements().Any(element => !element.Name.LocalName.Equals("Configuration", StringComparison.OrdinalIgnoreCase)
            && !element.Name.LocalName.Equals("MauiMetadata", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Item '{name}' contains an unsupported package element.");
        var configurations = sourceItem.Elements().Where(element => element.Name.LocalName.Equals("Configuration", StringComparison.OrdinalIgnoreCase)).ToList();
        var metadataBlocks = sourceItem.Elements().Where(element => element.Name.LocalName.Equals("MauiMetadata", StringComparison.OrdinalIgnoreCase)).ToList();
        if (configurations.Count != 1 || configurations[0].Elements().Count() != 1)
            throw new InvalidOperationException($"Item '{name}' must contain exactly one configuration object.");
        if (metadataBlocks.Count != 1)
            throw new InvalidOperationException($"Item '{name}' must contain exactly one MAUI metadata block.");
        return (sourceId, name, type, new XElement(configurations[0].Elements().Single()), new XElement(metadataBlocks[0]));
    }

    private static List<XElement> GetOnlyItems(XElement root, string label)
    {
        var itemContainers = root.Elements().Where(element => element.Name.LocalName.Equals("Items", StringComparison.OrdinalIgnoreCase)).ToList();
        if (itemContainers.Count != 1 || root.Elements().Any(element => !element.Name.LocalName.Equals("Items", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"The MAUI package {label} must contain exactly one Items collection.");
        var items = itemContainers[0].Elements().ToList();
        if (items.Count == 0) throw new InvalidOperationException($"The MAUI package {label} does not contain any items.");
        if (items.Count > MaxItems) throw new InvalidOperationException($"The MAUI package {label} contains more than the supported maximum of {MaxItems} items.");
        if (items.Any(element => !element.Name.LocalName.Equals("Item", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"The MAUI package {label} Items collection contains an unsupported element.");
        return items;
    }

    private static string BuildItemKey(string sourceId, string name)
    {
        var normalizedId = (sourceId ?? string.Empty).Trim();
        return normalizedId.Length > 0
            ? $"id:{normalizedId}"
            : $"name:{NormalizeRequired(name, "Item Name")}";
    }

    private static XDocument LoadXml(string xml, string label)
    {
        try
        {
            using var stringReader = new StringReader(xml);
            using var reader = XmlReader.Create(stringReader, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxCharactersInDocument
            });
            return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException)
        {
            throw new InvalidOperationException($"The {label} contains invalid XML.", ex);
        }
    }

    private static ZipArchiveEntry GetRequiredEntry(ZipArchive archive, string name)
    {
        var matches = archive.Entries.Where(entry => string.Equals(entry.FullName, name, StringComparison.Ordinal)).ToList();
        if (matches.Count != 1) throw new InvalidOperationException($"The MAUI export package must contain exactly one '{name}' file using the canonical package filename.");
        return matches[0];
    }

    private static void WriteTextEntry(ZipArchive archive, string name, string value)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(value ?? string.Empty);
    }

    private static string ReadTextEntry(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxExpandedEntryBytes)
            throw new InvalidOperationException($"The MAUI export package entry '{entry.FullName}' is too large.");
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var value = reader.ReadToEnd();
        if (Encoding.UTF8.GetByteCount(value) > MaxExpandedEntryBytes)
            throw new InvalidOperationException($"The MAUI export package entry '{entry.FullName}' is too large.");
        return value;
    }

    private static string NormalizeRequired(string? value, string label)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) throw new InvalidOperationException($"MAUI package {label} is required.");
        return normalized;
    }
}
