using System.IO.Compression;
using System.Xml.Linq;
using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Tests;

public sealed class HostSpreadsheetUpdateProfileTests
{
    [Fact]
    public void ShippedCsvAndExcelTemplatesUseIdenticalHeadersAndYesNoSamples()
    {
        // Read exactly the files downloaded by the Import Hosts modal. This
        // catches an out-of-sync binary XLSX that parser-only tests cannot see.
        var templates = TemplateDirectory();
        var csv = HostSpreadsheetImportService.ReadHostFile(
            File.ReadAllBytes(Path.Combine(templates!, "MAUI_HostImportTemplate.csv")), "MAUI_HostImportTemplate.csv");
        var xlsx = HostSpreadsheetImportService.ReadHostFile(
            File.ReadAllBytes(Path.Combine(templates!, "MAUI_HostImportTemplate.xlsx")), "MAUI_HostImportTemplate.xlsx");
        Assert.Equal(string.Join("|", csv.Headers), string.Join("|", xlsx.Headers));
        Assert.NotEmpty(csv.Rows);
        Assert.NotEmpty(xlsx.Rows);
        Assert.Equal("N", csv.Rows[0].Values["IGNORE_CERT_PROBS"]);
        Assert.Equal("N", xlsx.Rows[0].Values["IGNORE_CERT_PROBS"]);
        Assert.Contains("BUCKET", csv.Headers);
        Assert.Contains("CONTAINER", csv.Headers);
        Assert.Contains("DATA_INTEGRITY", csv.Headers);
        foreach (var field in new[] { "TENANT_NAME", "SITE", "SITE_URL_PREFIX", "CLIENT_ID", "TENANT_ID",
            "DOCUMENT_LIBRARY", "DOCUMENT_LIBRARY_GUID", "CERT_STORE", "CERT_SUBJECT", "CERT_SHA1" })
            Assert.Contains(field, csv.Headers);
        Assert.Equal(28, csv.Headers.Count);
        Assert.Equal(28, xlsx.Headers.Count);
        // The Examples and Instructions worksheets must never be read as import rows.
        Assert.Single(xlsx.Rows);
        Assert.Single(csv.Rows);
    }

    [Fact]
    public void ShippedExcelExamplesAllUseSupportedImportProfiles()
    {
        var bytes = File.ReadAllBytes(Path.Combine(TemplateDirectory(), "MAUI_HostImportTemplate.xlsx"));
        // Simulate copying the Examples rows to the input tab without modifying the shipped file.
        // Only reorder sheets in this disposable copy; the production reader follows workbook relationships.
        using var memory = new MemoryStream();
        memory.Write(bytes);
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/workbook.xml")!;
            XDocument workbook;
            using (var stream = entry.Open()) workbook = XDocument.Load(stream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var sheets = workbook.Root!.Element(ns + "sheets")!;
            Assert.Equal(new[] { "Host Import", "Examples", "Instructions" },
                sheets.Elements(ns + "sheet").Select(item => (string?)item.Attribute("name")).ToArray());
            var examples = sheets.Elements(ns + "sheet").First(item => (string?)item.Attribute("name") == "Examples");
            examples.Remove();
            sheets.AddFirst(examples);
            entry.Delete();
            using var output = archive.CreateEntry("xl/workbook.xml").Open();
            workbook.Save(output);
        }
        var file = HostSpreadsheetImportService.ReadHostFile(memory.ToArray(), "examples.xlsx");
        Assert.True(file.Rows.Count == 13, "Expected one example for each documented profile/variant.");
        foreach (var row in file.Rows)
        {
            var profile = HostBasicDefinitionProfile.Parse(row.Values);
            Assert.StartsWith("Example_", profile.Name);
            Assert.False(profile.IgnoreCertProblems);
        }
        var explicitTls = file.Rows.First(item => item.Values["NAME"] == "Example_FTPS_Explicit");
        var implicitTls = file.Rows.First(item => item.Values["NAME"] == "Example_FTPS_Implicit");
        Assert.Equal("tls-p", explicitTls.Values["SECURE"]);
        Assert.Equal("21", explicitTls.Values["PORT"]);
        Assert.Equal("implicit", implicitTls.Values["SECURE"]);
        Assert.Equal("990", implicitTls.Values["PORT"]);
        var key = file.Rows.First(item => item.Values["NAME"] == "Example_SFTP_Key");
        Assert.Equal(string.Empty, key.Values["PASSWORD"]);
        Assert.NotEmpty(key.Values["SSHKEY"]);
    }

    private static string TemplateDirectory()
    {
        DirectoryInfo? directory = new(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Templates", "HostImport");
            if (File.Exists(Path.Combine(candidate, "MAUI_HostImportTemplate.csv"))
                && File.Exists(Path.Combine(candidate, "MAUI_HostImportTemplate.xlsx"))) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("The shipped Host import templates were not found.");
    }

    [Theory]
    [InlineData("SFTP", "SSHFTP")]
    [InlineData("UNC", "Share")]
    [InlineData("FTP", "FTP")]
    [InlineData("FTPS", "FTP")]
    public void IgnoreCertProbsNIsValidForCreateAndUpdate(string spreadsheetType, string nativeType)
    {
        // Mirrors the shipped Host CSV/XLSX template: the optional column is
        // populated with "N" for every Host type, including SFTP and UNC.
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["NAME"] = "Vendor_SFTP",
            ["HOST_TYPE"] = spreadsheetType,
            ["HOST"] = spreadsheetType == "UNC" ? "" : "sftp.example.com",
            ["PORT"] = spreadsheetType == "UNC" ? "" : "22",
            ["USERNAME"] = "transferuser",
            ["PASSWORD"] = "password",
            ["SSHKEY"] = "",
            ["SECURE"] = spreadsheetType == "FTPS" ? "tls-p" : "",
            ["UNC"] = spreadsheetType == "UNC" ? @"\\server\share" : "",
            ["DESCRIPTION"] = "Vendor inbound",
            ["IGNORE_CERT_PROBS"] = "N"
        };

        var create = HostBasicDefinitionProfile.Parse(values);
        Assert.Equal(nativeType, create.HostType);
        Assert.False(create.IgnoreCertProblems);
        var update = HostBasicDefinitionProfile.ParseUpdate(values);
        Assert.Equal(nativeType, update.HostType);
        var nativeXml = nativeType == "Share"
            ? "<Share ID=\"11\" Name=\"Vendor_SFTP\" UNC=\"\\\\server\\old\" DefPassword=\"SAVED\" />"
            : $"<{nativeType} ID=\"11\" Name=\"Vendor_SFTP\" Host=\"sftp.example.com\" Port=\"22\" IgnoreCertProbs=\"0\" DefPassword=\"SAVED\" />";
        var updatedXml = HostBasicDefinitionProfile.ComposeUpdate(nativeXml, update, "NEW_CIPHER", "");
        var updated = XElement.Parse(updatedXml);
        Assert.Equal("11", (string?)updated.Attribute("ID"));
        if (nativeType != "FTP")
            Assert.Equal("0", (string?)updated.Attribute("IgnoreCertProbs") ?? "0");
    }

    [Theory]
    [InlineData("SFTP", "Y")]
    [InlineData("UNC", "Y")]
    [InlineData("SFTP", "1")]
    [InlineData("FTP", "0")]
    [InlineData("FTP", "2")]
    [InlineData("SFTP", "2")]
    public void UnsupportedIgnoreCertProbsValuesAreRejectedByBothPaths(string type, string value)
    {
        var values = new Dictionary<string, string>
        {
            ["NAME"] = "TEST",
            ["HOST_TYPE"] = type,
            ["HOST"] = "host.example.com",
            ["PASSWORD"] = "password",
            ["UNC"] = @"\\server\share",
            ["IGNORE_CERT_PROBS"] = value
        };
        Assert.Throws<ArgumentException>(() => HostBasicDefinitionProfile.Parse(values));
        Assert.Throws<ArgumentException>(() => HostBasicDefinitionProfile.ParseUpdate(values));
    }

    [Theory]
    [InlineData("FTP", "Y", "1")]
    [InlineData("FTPS", "y", "1")]
    [InlineData("FTP", "N", "0")]
    [InlineData("FTPS", "n", "0")]
    public void FtpYesNoWritesNativeIgnoreCertAttribute(string type, string selection, string native)
    {
        var fields = new Dictionary<string, string>
        {
            ["NAME"] = "FTP_TEST", ["HOST_TYPE"] = type,
            ["HOST"] = "ftp.example.com", ["PASSWORD"] = "password",
            ["SECURE"] = type == "FTPS" ? "tls-p" : "none",
            ["IGNORE_CERT_PROBS"] = selection
        };
        var definition = HostBasicDefinitionProfile.Parse(fields);
        Assert.Equal(native == "1", definition.IgnoreCertProblems);

        var patch = HostBasicDefinitionProfile.ParseUpdate(fields);
        // Spreadsheet normalization must occur before composing native Host XML.
        Assert.Equal(native, patch.Fields["IGNORE_CERT_PROBS"]);
        const string existing = "<FTP ID=\"44\" Name=\"FTP_TEST\" Host=\"old.example.com\" IgnoreCertProbs=\"0\" />";
        var updated = XElement.Parse(HostBasicDefinitionProfile.ComposeUpdate(existing, patch, "", ""));
        Assert.Equal(native, (string?)updated.Attribute("IgnoreCertProbs"));
    }

    [Fact]
    public void BlankBinaryCellPreservesExistingNativeSettingDuringUpdate()
    {
        var fields = new Dictionary<string, string>
        {
            ["NAME"] = "FTP_TEST", ["HOST_TYPE"] = "FTP",
            ["IGNORE_CERT_PROBS"] = ""
        };
        var patch = HostBasicDefinitionProfile.ParseUpdate(fields);
        const string existing = "<FTP ID=\"55\" Name=\"FTP_TEST\" IgnoreCertProbs=\"1\" />";
        var updated = XElement.Parse(HostBasicDefinitionProfile.ComposeUpdate(existing, patch, "", ""));
        Assert.Equal("1", (string?)updated.Attribute("IgnoreCertProbs"));
    }

    [Theory]
    [InlineData("FTP", "1")]
    [InlineData("FTP", "0")]
    [InlineData("SFTP", "1")]
    [InlineData("UNC", "0")]
    public void NumericBinarySpreadsheetValuesAreRejectedUnderCanonicalYesNoContract(string type, string value)
    {
        var values = new Dictionary<string, string>
        {
            ["NAME"] = "HOST", ["HOST_TYPE"] = type,
            ["HOST"] = type == "UNC" ? "" : "host.example.com",
            ["PASSWORD"] = "password",
            ["UNC"] = type == "UNC" ? @"\\server\share" : "",
            ["IGNORE_CERT_PROBS"] = value
        };
        var createException = Assert.Throws<ArgumentException>(() => HostBasicDefinitionProfile.Parse(values));
        var updateException = Assert.Throws<ArgumentException>(() => HostBasicDefinitionProfile.ParseUpdate(values));
        Assert.Equal("IGNORE_CERT_PROBS", createException.ParamName);
        Assert.Equal("IGNORE_CERT_PROBS", updateException.ParamName);
    }

    [Fact]
    public void UpdateReusesExistingNativeDescriptionRepresentation()
    {
        const string existing = "<SSHFTP ID=\"19\" Name=\"DESC_TEST\" Host=\"host.example.com\" Desc=\"Old\" IgnoreCertProbs=\"0\" />";
        var fields = new Dictionary<string, string>
        {
            ["NAME"] = "DESC_TEST", ["HOST_TYPE"] = "SFTP",
            ["IGNORE_CERT_PROBS"] = "N", ["DESCRIPTION"] = "New description"
        };
        var xml = HostBasicDefinitionProfile.ComposeUpdate(existing,
            HostBasicDefinitionProfile.ParseUpdate(fields), "", "");
        var host = XElement.Parse(xml);
        Assert.Equal("New description", (string?)host.Attribute("Desc"));
        Assert.Empty(host.Elements());
        Assert.Equal("0", (string?)host.Attribute("IgnoreCertProbs"));
    }

    [Fact]
    public void BlankSftpFieldsPreserveExistingCredentialKeyAndOtherSettings()
    {
        const string existing = "<SSHFTP ID=\"123\" Name=\"SFTP_TEST\" Host=\"old.example.com\" Port=\"22\" DefUsername=\"olduser\" DefPassword=\"ENCRYPTED_ORIGINAL\" DefClientKeyID=\"456\" RetryCount=\"7\"><Desc>Original notes</Desc><Advanced>Keep me</Advanced></SSHFTP>";
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["NAME"] = "SFTP_TEST",
            ["HOST_TYPE"] = "SFTP",
            ["HOST"] = "new.example.com",
            ["PORT"] = "",
            ["USERNAME"] = "",
            ["PASSWORD"] = "",
            ["SSHKEY"] = "",
            ["DESCRIPTION"] = ""
        };
        var patch = HostBasicDefinitionProfile.ParseUpdate(fields);
        var xml = HostBasicDefinitionProfile.ComposeUpdate(existing, patch, string.Empty, string.Empty);
        var host = XElement.Parse(xml);
        Assert.Equal("123", (string?)host.Attribute("ID"));
        Assert.Equal("new.example.com", (string?)host.Attribute("Host"));
        Assert.Equal("22", (string?)host.Attribute("Port"));
        Assert.Equal("olduser", (string?)host.Attribute("DefUsername"));
        Assert.Equal("ENCRYPTED_ORIGINAL", (string?)host.Attribute("DefPassword"));
        Assert.Equal("456", (string?)host.Attribute("DefClientKeyID"));
        Assert.Equal("7", (string?)host.Attribute("RetryCount"));
        Assert.Equal("Original notes", (string?)host.Element("Desc"));
        Assert.Equal("Keep me", (string?)host.Element("Advanced"));
    }

    [Fact]
    public void ExplicitFtpFieldsUpdateWithoutReplacingOtherNativeSettings()
    {
        const string existing = "<FTP ID=\"42\" Name=\"FTP_TEST\" Host=\"old.example.com\" Port=\"21\" Secure=\"none\" IgnoreCertProbs=\"1\" DefPassword=\"OLD_CIPHER\" CustomSetting=\"keep\" />";
        var fields = new Dictionary<string, string>
        {
            ["NAME"] = "FTP_TEST", ["HOST_TYPE"] = "FTPS", ["HOST"] = "ftp.example.com",
            ["PORT"] = "990", ["SECURE"] = "implicit", ["IGNORE_CERT_PROBS"] = "N",
            ["PASSWORD"] = "new-password"
        };
        var patch = HostBasicDefinitionProfile.ParseUpdate(fields);
        var xml = HostBasicDefinitionProfile.ComposeUpdate(existing, patch, "ENCRYPTED_NEW", string.Empty);
        var host = XElement.Parse(xml);
        Assert.Equal("42", (string?)host.Attribute("ID"));
        Assert.Equal("FTP", host.Name.LocalName);
        Assert.Equal("ftp.example.com", (string?)host.Attribute("Host"));
        Assert.Equal("990", (string?)host.Attribute("Port"));
        Assert.Equal("implicit", (string?)host.Attribute("Secure"));
        Assert.Equal("0", (string?)host.Attribute("IgnoreCertProbs"));
        Assert.Equal("ENCRYPTED_NEW", (string?)host.Attribute("DefPassword"));
        Assert.Equal("keep", (string?)host.Attribute("CustomSetting"));
        Assert.DoesNotContain("new-password", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void UncUpdatePreservesExistingAttributesAndHostIdentity()
    {
        const string existing = "<Share ID=\"8\" Name=\"UNC_TEST\" UNC=\"\\\\old\\share\" DefPassword=\"ORIGINAL\" Keep=\"yes\" />";
        var fields = new Dictionary<string, string>
        {
            ["NAME"] = "UNC_TEST", ["HOST_TYPE"] = "UNC", ["UNC"] = @"\\new\share",
            ["PASSWORD"] = ""
        };
        var xml = HostBasicDefinitionProfile.ComposeUpdate(existing,
            HostBasicDefinitionProfile.ParseUpdate(fields), string.Empty, string.Empty);
        var host = XElement.Parse(xml);
        Assert.Equal(@"\\new\share", (string?)host.Attribute("UNC"));
        Assert.Equal("ORIGINAL", (string?)host.Attribute("DefPassword"));
        Assert.Equal("yes", (string?)host.Attribute("Keep"));
    }

    [Fact]
    public void IncompatibleHostTypeOrNameIsRejectedBeforeWriting()
    {
        const string existing = "<SSHFTP ID=\"12\" Name=\"SFTP_TEST\" />";
        var wrongType = HostBasicDefinitionProfile.ParseUpdate(new Dictionary<string, string>
        { ["NAME"] = "SFTP_TEST", ["HOST_TYPE"] = "FTP" });
        Assert.Throws<InvalidOperationException>(() =>
            HostBasicDefinitionProfile.ComposeUpdate(existing, wrongType, string.Empty, string.Empty));
        var wrongName = HostBasicDefinitionProfile.ParseUpdate(new Dictionary<string, string>
        { ["NAME"] = "OTHER", ["HOST_TYPE"] = "SFTP" });
        Assert.Throws<InvalidOperationException>(() =>
            HostBasicDefinitionProfile.ComposeUpdate(existing, wrongName, string.Empty, string.Empty));
    }

    [Theory]
    [InlineData("FTP", "PORT", "65536")]
    [InlineData("FTP", "SECURE", "invalid-mode")]
    [InlineData("SSHFTP", "SSHKEY", "")]
    [InlineData("UNC", "PORT", "22")]
    public void InvalidSparseFieldsAreRejectedWhereApplicable(string type, string column, string value)
    {
        var fields = new Dictionary<string, string> { ["NAME"] = "TEST", ["HOST_TYPE"] = type, [column] = value };
        if (type == "SSHFTP" && column == "SSHKEY" && value == string.Empty)
        {
            // Blank SSH key is valid when updating: retain the existing key.
            Assert.Equal("SSHFTP", HostBasicDefinitionProfile.ParseUpdate(fields).HostType);
            return;
        }
        Assert.Throws<ArgumentException>(() => HostBasicDefinitionProfile.ParseUpdate(fields));
    }
}
