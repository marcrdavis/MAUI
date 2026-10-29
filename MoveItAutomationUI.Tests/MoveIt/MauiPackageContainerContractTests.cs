using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class MauiPackageContainerContractTests
{
    private static readonly string[] CanonicalEntries =
    {
        "manifest.json",
        "maui-metadata.xml",
        "payload.xml"
    };

    [Theory]
    [InlineData("Script")]
    [InlineData("DateList")]
    [InlineData("Report")]
    public void ObjectExportPackages_UseCanonicalMaui20ZipContainer(string packageType)
    {
        var item = MauiPortablePackage.BuildItem(
            "101",
            $"Sample {packageType}",
            packageType,
            new XElement($"{packageType}Configuration", new XAttribute("ID", "101"), new XAttribute("Name", $"Sample {packageType}")),
            new XElement("MauiMetadata", new XAttribute("ObjectType", packageType)));

        var bytes = MauiPortablePackage.Build(packageType, new[] { item }, DateTimeOffset.Parse("2026-10-10T12:00:00Z"));

        AssertCanonicalZip(bytes, expectedKind: "ObjectExport", expectedType: packageType);
    }

    [Theory]
    [InlineData("Task")]
    [InlineData("Host")]
    public void NativeTaskHostPackages_UseCanonicalMaui20ZipContainer(string packageType)
    {
        var nativeXml = packageType == "Task"
            ? "<Exported><Tasks><Task ID=\"101\" Name=\"Sample Task\" /></Tasks><Hosts></Hosts></Exported>"
            : "<Exported><Tasks></Tasks><Hosts><Share ID=\"101\" Name=\"Sample Host\" /></Hosts></Exported>";
        var sourceName = $"Sample {packageType}";
        var selected = new[] { new MauiNativePortablePackage.SelectedItem(packageType, "101", sourceName) };
        var metadata = new[]
        {
            MauiNativePortablePackage.BuildMetadataObject(
                packageType,
                "101",
                sourceName,
                new XElement("MauiMetadata", new XAttribute("ObjectType", packageType)))
        };

        var bytes = MauiNativePortablePackage.Build(
            packageType,
            "server-key",
            includesKeys: false,
            DateTimeOffset.Parse("2026-10-10T12:00:00Z"),
            nativeEncryptionSchemeVersion: 0,
            selected,
            nativeXml,
            metadata);

        AssertCanonicalZip(bytes, expectedKind: "NativeMoveIt", expectedType: packageType);
    }

    [Fact]
    public void ResourcePackage_RejectsPlainXmlMasqueradingAsMaui()
    {
        var bytes = Encoding.UTF8.GetBytes("<MauiPayload />");

        var exception = Assert.Throws<InvalidOperationException>(() => MauiPortablePackage.Parse(bytes, "Script", "Script"));

        Assert.Contains("not a valid MAUI Script export", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NativePackage_RejectsPlainXmlMasqueradingAsMaui()
    {
        var bytes = Encoding.UTF8.GetBytes("<Exported><Tasks></Tasks><Hosts></Hosts></Exported>");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MauiNativePortablePackage.Parse(bytes, "Task", new UnusedMetadataPortabilityService()));

        Assert.Contains("not a valid MAUI portability package", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertCanonicalZip(byte[] bytes, string expectedKind, string expectedType)
    {
        Assert.True(bytes.Length >= 4);
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal((byte)'K', bytes[1]);

        using var stream = new MemoryStream(bytes, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var entries = archive.Entries.Select(entry => entry.FullName).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        Assert.Equal(CanonicalEntries, entries);

        var manifestEntry = archive.GetEntry("manifest.json");
        Assert.NotNull(manifestEntry);
        using var manifestStream = manifestEntry!.Open();
        using var document = JsonDocument.Parse(manifestStream);
        var root = document.RootElement;
        Assert.Equal("MAUI.PortablePackage", root.GetProperty("format").GetString());
        Assert.Equal("2.0", root.GetProperty("formatVersion").GetString());
        Assert.Equal(expectedKind, root.GetProperty("packageKind").GetString());
        Assert.Equal(expectedType, root.GetProperty("packageType").GetString());
        Assert.Equal("payload.xml", root.GetProperty("payloadFile").GetString());
        Assert.Equal("maui-metadata.xml", root.GetProperty("metadataFile").GetString());
    }

    private sealed class UnusedMetadataPortabilityService : IMauiMetadataPortabilityService
    {
        public Task<XElement> BuildExportElementAsync(string environmentKey, string objectType, string objectId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public PortableMetadataPackage ParseFromEnvelope(XElement exportRoot, string expectedObjectType)
            => throw new NotSupportedException();

        public Task<MetadataImportSummary> ValidateAsync(string environmentKey, PortableMetadataPackage package, string expectedObjectType, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<int> ApplyCompatibleAsync(string environmentKey, string objectType, string objectId, PortableMetadataPackage package, string username, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
