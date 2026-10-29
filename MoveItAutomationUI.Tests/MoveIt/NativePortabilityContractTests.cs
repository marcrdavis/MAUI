using System.Text.Json.Nodes;
using System.Xml.Linq;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class NativePortabilityContractTests
{
    [Theory]
    [InlineData("Task")] [InlineData("Host")]
    public void MauiPackageRetainsNativeResourceGroupsWithoutTranslation(string type)
    {
        var nativeXml = type == "Task"
            ? "<Exported><Tasks><Task ID=\"42\" Name=\"Task\"><Group>Group A</Group><Group>Group B</Group></Task></Tasks><Hosts></Hosts></Exported>"
            : "<Exported><Tasks></Tasks><Hosts><FTP ID=\"42\" Name=\"Host\"><Group>Group A</Group><Group>Group B</Group></FTP></Hosts></Exported>";
        var bytes = MauiNativePortablePackage.Build(type, "server-key", false, DateTimeOffset.UtcNow, 0,
            [new(type, "42", type)], nativeXml, []);
        using var stream = new MemoryStream(bytes);
        using var zip = new System.IO.Compression.ZipArchive(stream);
        using var reader = new StreamReader(zip.GetEntry("payload.xml")!.Open());
        Assert.Equal(nativeXml, reader.ReadToEnd());
        var imported = new JsonObject { [type] = new JsonObject { ["ID"] = "42", ["Group"] = new JsonArray("Group A", "Group B") } };
        var submitted = MoveItRestClient.BuildNativeImportRequest(imported, "");
        Assert.True(JsonNode.DeepEquals(imported[type]!["Group"], submitted["Imported"]![type]!["Group"]));
    }

    [Fact]
    public void BuildNativeExportRequest_MatchesCapturedMoveItShape()
    {
        var body = MoveItRestClient.BuildNativeExportRequest(
            new[] { new NativeConfigurationExportItem("899070619", "Task") },
            includeKeys: false,
            passphrase: "Portable-Test-Passphrase1!");

        var items = Assert.IsType<JsonArray>(body["Item"]);
        var item = Assert.IsType<JsonObject>(Assert.Single(items));
        Assert.Equal("899070619", item["ID"]!.GetValue<string>());
        Assert.Equal("Task", item["SectionType"]!.GetValue<string>());
        Assert.True(body["doNotExportKeys"]!.GetValue<bool>());
        Assert.Equal("Portable-Test-Passphrase1!", body["passphrase"]!.GetValue<string>());
    }

    [Fact]
    public void BuildNativeEncodeRequest_PreservesNativeXmlAndPassphrase()
    {
        const string xml = "<Exported><Tasks></Tasks><Hosts></Hosts></Exported>";

        var body = MoveItRestClient.BuildNativeEncodeRequest(xml, "Portable-Test-Passphrase1!");

        Assert.Equal(xml, body["fileContents"]!.GetValue<string>());
        Assert.Equal("Portable-Test-Passphrase1!", body["passphrase"]!.GetValue<string>());
    }

    [Fact]
    public void BuildNativeImportRequest_NestsPortablePassphraseInsideImportedAndPreservesPgpFields()
    {
        var imported = JsonNode.Parse("""
            {
              "Duplicated": [],
              "PGPKeys": {
                "PGPKey": [
                  {
                    "ID": "0x654DB456",
                    "Name": "Test PGP",
                    "Passphrase": "4gAAAA-PGP-ENCRYPTED-VALUE",
                    "KeyMaterial": "4gAAAA-PGP-KEY-MATERIAL"
                  }
                ]
              }
            }
            """)!.AsObject();
        var original = imported.ToJsonString();

        var body = MoveItRestClient.BuildNativeImportRequest(imported, "Portable-Test-Passphrase1!");

        Assert.Null(body["passphrase"]);
        var nested = Assert.IsType<JsonObject>(body["Imported"]);
        Assert.Equal("Portable-Test-Passphrase1!", nested["passphrase"]!.GetValue<string>());

        var pgpKeys = Assert.IsType<JsonObject>(nested["PGPKeys"]);
        var pgpArray = Assert.IsType<JsonArray>(pgpKeys["PGPKey"]);
        var pgp = Assert.IsType<JsonObject>(Assert.Single(pgpArray));
        Assert.Equal("4gAAAA-PGP-ENCRYPTED-VALUE", pgp["Passphrase"]!.GetValue<string>());
        Assert.Equal("4gAAAA-PGP-KEY-MATERIAL", pgp["KeyMaterial"]!.GetValue<string>());

        Assert.Equal(original, imported.ToJsonString());
    }

    [Fact]
    public void BuildHostOnlyNativeExport_RemovesCarrierAndPreservesExactEmptyTasksElement()
    {
        const string nativeXml = """
            <Exported>
              <Tasks Marker="carrier"><Task ID="500" Name="MAUI_Carrier" /></Tasks>
              <Hosts><Share ID="42" Name="ShareHost" /></Hosts>
            </Exported>
            """;
        var host = new AutomationHost { Id = "42", Name = "ShareHost", TypeCode = "Share" };

        var result = MauiNativePortabilityService.BuildHostOnlyNativeExport(nativeXml, new[] { host });

        Assert.Contains("<Tasks></Tasks>", result, StringComparison.Ordinal);
        Assert.DoesNotContain("MAUI_Carrier", result, StringComparison.Ordinal);
        Assert.DoesNotContain("Marker=\"carrier\"", result, StringComparison.Ordinal);
        Assert.True(result.IndexOf("<Tasks></Tasks>", StringComparison.Ordinal) < result.IndexOf("<Hosts>", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildHostCarrierTaskXml_UsesSourceForPop3AndDestinationForSmtp()
    {
        var hosts = new[]
        {
            new AutomationHost { Id = "11", Name = "MailIn", TypeCode = "POP3" },
            new AutomationHost { Id = "12", Name = "MailOut", TypeCode = "SMTP" }
        };

        var xml = MauiNativePortabilityService.BuildHostCarrierTaskXml("MAUI_HostExport_Test", hosts);
        var task = XDocument.Parse(xml).Root!;

        Assert.Equal("0", (string?)task.Attribute("Active"));
        Assert.Empty(task.Element("Schedules")!.Elements());

        var pop3 = Assert.Single(task.Elements("Source"));
        Assert.Equal("11", (string?)pop3.Attribute("HostID"));
        Assert.Equal("POP3", (string?)pop3.Attribute("Type"));

        var smtp = Assert.Single(task.Elements("Destination"));
        Assert.Equal("12", (string?)smtp.Attribute("HostID"));
        Assert.Equal("SMTP", (string?)smtp.Attribute("Type"));
    }

    [Fact]
    public void BuildHostPackage_RejectsSelfClosingTasksElement()
    {
        const string nativeXml = "<Exported><Tasks/><Hosts><Share ID=\"42\" Name=\"ShareHost\" /></Hosts></Exported>";
        var selected = new[]
        {
            new MauiNativePortablePackage.SelectedItem("Host", "42", "ShareHost")
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            MauiNativePortablePackage.Build(
                "Host",
                "server-key",
                includesKeys: false,
                DateTimeOffset.UtcNow,
                nativeEncryptionSchemeVersion: 0,
                selected,
                nativeXml,
                Array.Empty<XElement>()));

        Assert.Contains("<Tasks></Tasks>", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildHostPackage_AcceptsExplicitEmptyTasksElement()
    {
        const string nativeXml = "<Exported><Tasks></Tasks><Hosts><Share ID=\"42\" Name=\"ShareHost\" /></Hosts></Exported>";
        var selected = new[]
        {
            new MauiNativePortablePackage.SelectedItem("Host", "42", "ShareHost")
        };

        var bytes = MauiNativePortablePackage.Build(
            "Host",
            "server-key",
            includesKeys: false,
            DateTimeOffset.UtcNow,
            nativeEncryptionSchemeVersion: 0,
            selected,
            nativeXml,
            Array.Empty<XElement>());

        Assert.NotEmpty(bytes);
    }


    [Fact]
    public void ValidateConflictTargetIdentity_RejectsConflictThatDisappearedAfterAnalysis()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            MauiNativePortabilityService.ValidateConflictTargetIdentity(
                "PGP Key 'Test Key'",
                "pgp|0x654DB456",
                string.Empty,
                conflictStillExists: false,
                action: "overwrite"));

        Assert.Contains("changed or disappeared", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateConflictTargetIdentity_RejectsReplacementTargetAfterAnalysis()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            MauiNativePortabilityService.ValidateConflictTargetIdentity(
                "Host 'Partner SFTP'",
                "701133409",
                "812244001",
                conflictStillExists: true,
                action: "overwrite"));

        Assert.Contains("changed after the package was analyzed", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateConflictTargetIdentity_AllowsRenameWhenConflictStillExistsButTargetCannotBeResolved()
    {
        MauiNativePortabilityService.ValidateConflictTargetIdentity(
            "Task 'Daily Extract'",
            string.Empty,
            string.Empty,
            conflictStillExists: true,
            action: "rename");
    }

    [Fact]
    public void ApplyRenameResolution_MatchesCapturedRenameFields()
    {
        var node = JsonNode.Parse("""
            {
              "ID": "673236648",
              "Name": "Original",
              "displayName": "Original",
              "duplicate": ["id", "name"]
            }
            """)!.AsObject();

        MauiNativePortabilityService.ApplyRenameResolution(node, "Renamed");

        Assert.Equal("673236648", node["ID"]!.GetValue<string>());
        Assert.Equal("Renamed", node["Name"]!.GetValue<string>());
        Assert.Equal("Renamed", node["displayName"]!.GetValue<string>());
        Assert.Null(node["duplicate"]);
        Assert.True(node["Renamed"]!.GetValue<bool>());
        Assert.True(node["new"]!.GetValue<bool>());
    }
}
