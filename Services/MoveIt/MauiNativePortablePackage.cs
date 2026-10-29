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
/// Owns MAUI's Task/Host native-portability container. The native MOVEit export is intentionally retained
/// as an opaque XML document so MOVEit remains responsible for credentials, dependency encryption, and
/// configuration compatibility. MAUI stores only its own metadata beside that native payload.
/// </summary>
internal static class MauiNativePortablePackage
{
    public const string FormatVersion = "2.0";
    public const long MaxPackageBytes = 50L * 1024L * 1024L;
    private const long MaxExpandedEntryBytes = 100L * 1024L * 1024L;
    private const string ManifestEntryName = "manifest.json";
    private const string PayloadEntryName = "payload.xml";
    private const string MetadataEntryName = "maui-metadata.xml";

    internal sealed record SelectedItem(string Type, string SourceId, string SourceName);
    internal sealed record Manifest(
        string Format,
        string FormatVersion,
        string PackageKind,
        string PackageType,
        string PayloadFile,
        string MetadataFile,
        DateTimeOffset ExportedUtc,
        int ItemCount,
        int MetadataSchemaVersion,
        string ProtectionMode,
        bool IncludesKeys,
        int NativeEncryptionSchemeVersion,
        IReadOnlyList<SelectedItem> SelectedItems);
    internal sealed record MetadataItem(string ObjectType, string SourceId, string SourceName, PortableMetadataPackage Metadata);
    internal sealed record Package(Manifest Manifest, string NativeXml, IReadOnlyList<MetadataItem> MetadataItems);

    public static byte[] Build(
        string packageType,
        string protectionMode,
        bool includesKeys,
        DateTimeOffset exportedUtc,
        int nativeEncryptionSchemeVersion,
        IReadOnlyList<SelectedItem> selectedItems,
        string nativeXml,
        IReadOnlyList<XElement> metadataObjects)
    {
        var normalizedType = NormalizePackageType(packageType);
        var normalizedProtection = NormalizeProtectionMode(protectionMode);
        var normalizedSelectedItems = NormalizeSelectedItems(selectedItems, normalizedType);
        if (string.IsNullOrWhiteSpace(nativeXml)) throw new InvalidOperationException("MOVEit returned an empty native export.");

        // Validate the native XML and selected-object boundary before placing it in the archive.
        // DTD/entity processing is never allowed. Host packages are deliberately Host-only on import:
        // MOVEit requires the Tasks collection to exist, but it must remain empty.
        var nativeDocument = LoadXml(nativeXml, "native MOVEit export");
        ValidateNativePayload(normalizedType, normalizedSelectedItems, nativeDocument);

        var manifest = new Manifest(
            "MAUI.PortablePackage",
            FormatVersion,
            "NativeMoveIt",
            normalizedType,
            PayloadEntryName,
            MetadataEntryName,
            exportedUtc,
            normalizedSelectedItems.Count,
            MauiMetadataPortabilityService.SchemaVersion,
            normalizedProtection,
            includesKeys,
            nativeEncryptionSchemeVersion,
            normalizedSelectedItems);

        var metadataRoot = new XElement("MauiMetadataBundle",
            new XAttribute("SchemaVersion", MauiMetadataPortabilityService.SchemaVersion),
            metadataObjects.Select(element => new XElement(element)));
        var metadataDocument = new XDocument(new XDeclaration("1.0", "utf-8", null), metadataRoot)
            .ToString(SaveOptions.DisableFormatting);

        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteTextEntry(archive, ManifestEntryName, JsonSerializer.Serialize(manifest, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));
            WriteTextEntry(archive, PayloadEntryName, nativeXml);
            WriteTextEntry(archive, MetadataEntryName, metadataDocument);
        }

        var bytes = output.ToArray();
        if (bytes.LongLength > MaxPackageBytes)
            throw new InvalidOperationException("The MAUI portability package exceeds the 50 MB package limit. Export fewer objects or exclude referenced keys/certificates.");
        return bytes;
    }

    public static Package Parse(byte[] bytes, string expectedPackageType, IMauiMetadataPortabilityService metadataPortability)
    {
        ArgumentNullException.ThrowIfNull(metadataPortability);
        if (bytes is null || bytes.Length == 0) throw new InvalidOperationException("Select a non-empty MAUI portability package.");
        if (bytes.LongLength > MaxPackageBytes) throw new InvalidOperationException("The MAUI portability package exceeds the 50 MB import limit.");

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            if (archive.Entries.Count != 3)
                throw new InvalidOperationException("The MAUI portability package contains unsupported files.");
            var manifestEntry = GetRequiredEntry(archive, ManifestEntryName);
            var payloadEntry = GetRequiredEntry(archive, PayloadEntryName);
            var metadataEntry = GetRequiredEntry(archive, MetadataEntryName);

            var manifestText = ReadTextEntry(manifestEntry);
            var manifest = JsonSerializer.Deserialize<Manifest>(manifestText, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new InvalidOperationException("The MAUI portability package manifest is invalid.");

            if (!string.Equals(manifest.Format, "MAUI.PortablePackage", StringComparison.Ordinal))
                throw new InvalidOperationException("The selected file is not a MAUI portability package.");
            if (!string.Equals(manifest.FormatVersion, FormatVersion, StringComparison.Ordinal))
                throw new InvalidOperationException($"This MAUI portability package version is not supported. Expected version {FormatVersion}.");
            if (!string.Equals(manifest.PackageKind, "NativeMoveIt", StringComparison.Ordinal))
                throw new InvalidOperationException("The selected MAUI package is not a native MOVEit portability package.");
            if (!string.Equals(manifest.PayloadFile, PayloadEntryName, StringComparison.Ordinal)
                || !string.Equals(manifest.MetadataFile, MetadataEntryName, StringComparison.Ordinal))
                throw new InvalidOperationException("The MAUI portability package manifest references unsupported package files.");
            if (manifest.ItemCount < 1)
                throw new InvalidOperationException("The MAUI portability package manifest contains an invalid item count.");
            if (manifest.MetadataSchemaVersion != MauiMetadataPortabilityService.SchemaVersion)
                throw new InvalidOperationException($"This package uses an unsupported MAUI metadata schema. Expected version {MauiMetadataPortabilityService.SchemaVersion}.");
            var actualType = NormalizePackageType(manifest.PackageType);
            var expectedType = NormalizePackageType(expectedPackageType);
            if (!actualType.Equals(expectedType, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"This package contains {actualType} objects, but this import expects {expectedType} objects.");
            var protection = NormalizeProtectionMode(manifest.ProtectionMode);
            var normalizedSelectedItems = NormalizeSelectedItems(manifest.SelectedItems, actualType);
            if (normalizedSelectedItems.Count != manifest.ItemCount)
                throw new InvalidOperationException("The MAUI portability package item count does not match its manifest.");

            var nativeXml = ReadTextEntry(payloadEntry);
            var nativeDocument = LoadXml(nativeXml, "native MOVEit export");
            ValidateNativePayload(actualType, normalizedSelectedItems, nativeDocument);

            var metadataDocument = LoadXml(ReadTextEntry(metadataEntry), "MAUI metadata bundle");
            var metadataRoot = metadataDocument.Root;
            if (metadataRoot is null || !metadataRoot.Name.LocalName.Equals("MauiMetadataBundle", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The MAUI metadata bundle is invalid.");
            if (!int.TryParse((string?)metadataRoot.Attribute("SchemaVersion"), out var schemaVersion)
                || schemaVersion != MauiMetadataPortabilityService.SchemaVersion)
                throw new InvalidOperationException($"This package uses an unsupported MAUI metadata schema. Expected version {MauiMetadataPortabilityService.SchemaVersion}.");

            var metadataItems = new List<MetadataItem>();
            var metadataKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allowedMetadataKeys = GetNativeMetadataKeys(nativeDocument);
            foreach (var objectElement in metadataRoot.Elements())
            {
                if (!objectElement.Name.LocalName.Equals("Object", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The MAUI metadata bundle contains an unsupported element.");
                var objectType = NormalizeObjectType((string?)objectElement.Attribute("ObjectType"));
                var sourceId = ((string?)objectElement.Attribute("SourceId") ?? string.Empty).Trim();
                if (sourceId.Length == 0) throw new InvalidOperationException("The MAUI metadata bundle contains an object without a SourceId.");
                var sourceName = NormalizeRequired((string?)objectElement.Attribute("SourceName"), "Metadata object name");
                var metadataKey = BuildMetadataKey(objectType, sourceId);
                if (!metadataKeys.Add(metadataKey))
                    throw new InvalidOperationException($"The MAUI metadata bundle contains duplicate metadata for {objectType} ID '{sourceId}'.");
                if (!allowedMetadataKeys.Contains(metadataKey))
                    throw new InvalidOperationException($"The MAUI metadata bundle references {objectType} ID '{sourceId}', which is not present in the native MOVEit payload.");
                var package = metadataPortability.ParseFromEnvelope(objectElement, objectType);
                metadataItems.Add(new MetadataItem(objectType, sourceId, sourceName, package));
            }

            // Current MAUI native exports deliberately write one metadata envelope for every native object that
            // MAUI understands, even when that object's configured metadata field list is empty. Treat missing
            // envelopes as package corruption/inconsistency rather than silently importing the native object and
            // losing its MAUI metadata. This does not reject any package produced by the current 2.0 exporter.
            var missingMetadataCount = allowedMetadataKeys.Count(key => !metadataKeys.Contains(key));
            if (missingMetadataCount > 0)
                throw new InvalidOperationException(
                    $"The MAUI metadata bundle is incomplete for {missingMetadataCount} native object{(missingMetadataCount == 1 ? string.Empty : "s")}. Re-export the package before importing it.");

            return new Package(manifest with
            {
                PackageType = actualType,
                ProtectionMode = protection,
                SelectedItems = normalizedSelectedItems
            }, nativeXml, metadataItems);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException)
        {
            throw new InvalidOperationException("The selected file is not a valid MAUI portability package.", ex);
        }
    }

    public static XElement BuildMetadataObject(string objectType, string sourceId, string sourceName, XElement metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        var normalizedSourceId = (sourceId ?? string.Empty).Trim();
        if (normalizedSourceId.Length == 0) throw new InvalidOperationException("Metadata SourceId is required.");
        return new XElement("Object",
            new XAttribute("ObjectType", NormalizeObjectType(objectType)),
            new XAttribute("SourceId", normalizedSourceId),
            new XAttribute("SourceName", NormalizeRequired(sourceName, "Metadata object name")),
            new XAttribute("MetadataSchemaVersion", MauiMetadataPortabilityService.SchemaVersion),
            new XElement(metadata));
    }

    public static string BuildFileName(string packageType, IReadOnlyList<SelectedItem> selectedItems)
    {
        var type = NormalizePackageType(packageType);
        if (selectedItems.Count == 1)
            return PortabilitySupport.BuildSafeFileName(selectedItems[0].SourceName, selectedItems[0].SourceId, type);
        var plural = type.EndsWith('s') ? type : type + "s";
        return $"{plural}_{selectedItems.Count}_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}.maui";
    }


    private static IReadOnlyList<SelectedItem> NormalizeSelectedItems(IReadOnlyList<SelectedItem>? selectedItems, string packageType)
    {
        if (selectedItems is null || selectedItems.Count == 0)
            throw new InvalidOperationException("The MAUI portability package manifest does not contain selected objects.");

        var expectedType = packageType.Equals("Task", StringComparison.OrdinalIgnoreCase) ? "Task" : "Host";
        var normalized = new List<SelectedItem>(selectedItems.Count);
        for (var index = 0; index < selectedItems.Count; index++)
        {
            var item = selectedItems[index];
            if (item is null)
                throw new InvalidOperationException($"The MAUI portability package manifest contains an invalid selected object at position {index + 1}.");

            var type = NormalizeObjectType(item.Type);
            if (!type.Equals(expectedType, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"The MAUI {packageType} package manifest contains an unexpected selected object type.");

            var sourceId = (item.SourceId ?? string.Empty).Trim();
            if (sourceId.Length == 0)
                throw new InvalidOperationException("The MAUI portability package manifest contains a selected object without an ID.");

            normalized.Add(new SelectedItem(expectedType, sourceId, NormalizeRequired(item.SourceName, "Selected item name")));
        }

        return normalized;
    }

    private static void ValidateNativePayload(string packageType, IReadOnlyList<SelectedItem> selectedItems, XDocument document)
    {
        var root = document.Root;
        if (root is null || !root.Name.LocalName.Equals("Exported", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The native MOVEit export has an unexpected root element.");

        var tasks = root.Elements().FirstOrDefault(element => element.Name.LocalName.Equals("Tasks", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The native MOVEit export is missing the required Tasks collection.");
        var hosts = root.Elements().FirstOrDefault(element => element.Name.LocalName.Equals("Hosts", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The native MOVEit export is missing the required Hosts collection.");

        if (packageType.Equals("Host", StringComparison.OrdinalIgnoreCase))
        {
            // MOVEit's validated Host-import contract requires an explicit paired element, not a self-closing
            // <Tasks/> equivalent. Preserve that exact native shape because payload.xml is otherwise opaque.
            if (tasks.IsEmpty || tasks.HasAttributes || tasks.Nodes().Any())
                throw new InvalidOperationException("A MAUI Host portability package must contain an exact empty <Tasks></Tasks> collection.");

            var rootElements = root.Elements().ToList();
            var tasksIndex = rootElements.IndexOf(tasks);
            var hostsIndex = rootElements.IndexOf(hosts);
            if (tasksIndex < 0 || hostsIndex < 0 || tasksIndex >= hostsIndex)
                throw new InvalidOperationException("A MAUI Host portability package must contain the empty Tasks collection before Hosts.");
        }

        var nativeIds = (packageType.Equals("Task", StringComparison.OrdinalIgnoreCase) ? tasks : hosts)
            .Elements()
            .Select(element => ((string?)element.Attribute("ID") ?? string.Empty).Trim())
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var item in selectedItems)
        {
            if (!nativeIds.Contains(item.SourceId))
                throw new InvalidOperationException($"Selected {packageType} ID '{item.SourceId}' is not present in the native MOVEit export.");
        }

        var duplicateSelectedIds = selectedItems
            .GroupBy(item => (item.SourceId ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Key.Length > 0 && group.Count() > 1);
        if (duplicateSelectedIds is not null)
            throw new InvalidOperationException($"The MAUI portability package manifest contains duplicate selected ID '{duplicateSelectedIds.Key}'.");
    }


    private static HashSet<string> GetNativeMetadataKeys(XDocument document)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var root = document.Root ?? throw new InvalidOperationException("The native MOVEit export has an unexpected root element.");

        AddNativeKeys(keys, root, "Tasks", "Task", "Task", null);
        var hosts = root.Elements().FirstOrDefault(element => element.Name.LocalName.Equals("Hosts", StringComparison.OrdinalIgnoreCase));
        if (hosts is not null)
        {
            foreach (var element in hosts.Elements())
            {
                var id = ((string?)element.Attribute("ID") ?? string.Empty).Trim();
                if (id.Length > 0) keys.Add(BuildMetadataKey("Host", id));
            }
        }
        AddNativeKeys(keys, root, "Scripts", "Script", "Script", null);
        AddNativeKeys(keys, root, "DateLists", "DateList", "DateList", null);
        AddNativeKeys(keys, root, "SSHClientKeys", "SSHClientKey", "KeyCertificate", "ssh");
        AddNativeKeys(keys, root, "SSLClientCerts", "SSLClientCert", "KeyCertificate", "tls");
        AddNativeKeys(keys, root, "PGPKeys", "PGPKey", "KeyCertificate", "pgp");
        return keys;
    }

    private static void AddNativeKeys(HashSet<string> keys, XElement root, string containerName, string elementName, string metadataType, string? idPrefix)
    {
        var container = root.Elements().FirstOrDefault(element => element.Name.LocalName.Equals(containerName, StringComparison.OrdinalIgnoreCase));
        if (container is null) return;
        foreach (var element in container.Elements().Where(element => element.Name.LocalName.Equals(elementName, StringComparison.OrdinalIgnoreCase)))
        {
            var id = ((string?)element.Attribute("ID") ?? string.Empty).Trim();
            if (id.Length == 0) continue;
            var metadataId = string.IsNullOrWhiteSpace(idPrefix) ? id : $"{idPrefix}|{id}";
            keys.Add(BuildMetadataKey(metadataType, metadataId));
        }
    }

    private static string BuildMetadataKey(string objectType, string sourceId)
        => $"{NormalizeObjectType(objectType)}|{(sourceId ?? string.Empty).Trim()}";

    private static ZipArchiveEntry GetRequiredEntry(ZipArchive archive, string name)
    {
        var matches = archive.Entries.Where(entry => entry.FullName.Equals(name, StringComparison.Ordinal)).ToList();
        if (matches.Count != 1)
            throw new InvalidOperationException($"The MAUI portability package must contain exactly one '{name}' file using the canonical package filename.");
        return matches[0];
    }

    private static void WriteTextEntry(ZipArchive archive, string name, string value)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: false);
        writer.Write(value ?? string.Empty);
    }

    private static string ReadTextEntry(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxExpandedEntryBytes)
            throw new InvalidOperationException($"Package entry '{entry.FullName}' exceeds the supported expanded size.");
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var value = reader.ReadToEnd();
        if (Encoding.UTF8.GetByteCount(value) > MaxExpandedEntryBytes)
            throw new InvalidOperationException($"Package entry '{entry.FullName}' exceeds the supported expanded size.");
        return value;
    }

    private static XDocument LoadXml(string xml, string label)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxExpandedEntryBytes
            });
            return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException)
        {
            throw new InvalidOperationException($"The {label} contains invalid XML.", ex);
        }
    }

    private static string NormalizePackageType(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Equals("Task", StringComparison.OrdinalIgnoreCase)) return "Task";
        if (normalized.Equals("Host", StringComparison.OrdinalIgnoreCase)) return "Host";
        throw new InvalidOperationException("MAUI native portability currently supports Task and Host packages only.");
    }

    private static string NormalizeProtectionMode(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Equals("server-key", StringComparison.OrdinalIgnoreCase) || normalized.Equals("serverkey", StringComparison.OrdinalIgnoreCase)) return "server-key";
        if (normalized.Equals("passphrase", StringComparison.OrdinalIgnoreCase) || normalized.Equals("portable", StringComparison.OrdinalIgnoreCase)) return "passphrase";
        throw new InvalidOperationException("The MAUI package protection mode is not supported.");
    }

    private static string NormalizeObjectType(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) throw new InvalidOperationException("MAUI metadata ObjectType is required.");
        return normalized;
    }

    private static string NormalizeRequired(string? value, string label)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) throw new InvalidOperationException($"{label} is required.");
        return normalized;
    }
}
