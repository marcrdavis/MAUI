using System.Xml.Linq;
using MoveItAutomationUI.Services.MoveIt.Hosts;
using Xunit;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class HostBasicDefinitionProfileTests
{
    [Theory]
    [InlineData("FTP", "FTP", 21)]
    [InlineData("FTPS", "FTP", 21)]
    [InlineData("SFTP", "SSHFTP", 22)]
    [InlineData("SSHFTP", "SSHFTP", 22)]
    public void Parse_NormalizesConnectionTypes(string rawType, string expectedType, int expectedPort)
    {
        var input = Values("NAME", "Vendor");
        input["HOST_TYPE"] = rawType;
        input["HOST"] = "example.com";
        input["PASSWORD"] = "dummy-test-secret";
        if (rawType == "FTPS") input["SECURE"] = "tls-p";
        var result = HostBasicDefinitionProfile.Parse(input, "DEV_");
        Assert.Equal("DEV_Vendor", result.Name);
        Assert.Equal(expectedType, result.HostType);
        Assert.Equal(expectedPort, result.Port);
    }

    [Fact]
    public void Parse_SftpRequiresPasswordOrClientKey()
    {
        var input = Values("NAME", "Vendor");
        input["HOST_TYPE"] = "SFTP";
        input["HOST"] = "example.com";
        Assert.Throws<ArgumentException>(() => HostBasicDefinitionProfile.Parse(input));
        input["SSHKEY"] = "Partner Key";
        var parsed = HostBasicDefinitionProfile.Parse(input);
        Assert.Equal("Partner Key", parsed.SshKeyName);
    }

    [Fact]
    public void Parse_UncRequiresServerAndShare()
    {
        var input = Values("NAME", "Internal");
        input["HOST_TYPE"] = "UNC";
        input["UNC"] = @"\\server";
        Assert.Throws<ArgumentException>(() => HostBasicDefinitionProfile.Parse(input));
        input["UNC"] = @"\\server\share";
        Assert.Equal("Share", HostBasicDefinitionProfile.Parse(input).HostType);
    }

    [Fact]
    public void Compose_UsesEncryptedCredential_NotPlaintext()
    {
        var input = Values("NAME", "Vendor");
        input["HOST_TYPE"] = "SFTP";
        input["HOST"] = "example.com";
        input["PASSWORD"] = "dummy-test-secret";
        var definition = HostBasicDefinitionProfile.Parse(input);
        var xml = HostBasicDefinitionProfile.Compose("<SSHFTP ID=\"1\" Name=\"Vendor\" />", definition, "COM_ENCRYPTED", "0");
        var root = XElement.Parse(xml);
        Assert.Equal("COM_ENCRYPTED", (string?)root.Attribute("DefPassword"));
        Assert.Equal("example.com", (string?)root.Attribute("Host"));
        Assert.DoesNotContain("dummy-test-secret", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_PreservesNativeSkeletonFields()
    {
        var input = Values("NAME", "Partner");
        input["HOST_TYPE"] = "FTP"; input["HOST"] = "ftp.example.com"; input["SECURE"] = "tls-p";
        var def = HostBasicDefinitionProfile.Parse(input);
        var xml = HostBasicDefinitionProfile.Compose("<FTP ID=\"44\" Name=\"Partner\" DefPassive=\"1\" />", def, string.Empty, "0");
        var root = XElement.Parse(xml);
        Assert.Equal("1", (string?)root.Attribute("DefPassive"));
        Assert.Equal("tls-p", (string?)root.Attribute("Secure"));
    }

    [Fact]
    public void CsvPassword_PreservesSignificantWhitespaceAndLiteralEscapes()
    {
        var csv = "NAME,HOST_TYPE,HOST,PASSWORD\nTest,SFTP,example.com,  literal\\x41!  ";
        var file = MoveItAutomationUI.Services.MoveIt.TaskSpreadsheetImportFileReader.Read(
            System.Text.Encoding.UTF8.GetBytes(csv), "test.csv", preservePasswordCells: true);
        var definition = HostBasicDefinitionProfile.Parse(file.Rows[0].Values);
        Assert.Equal("  literal\\x41!  ", definition.Password);
    }


    [Fact]
    public void ReadHostFile_AcceptsCanonicalHeaders()
    {
        var csv = "NAME,HOST_TYPE,HOST,RESOURCE_GROUP,IGNORE_CERT_PROBS,PASSWORD\nVendor,SFTP,example.com,Production,0,test-secret";
        var file = HostSpreadsheetImportService.ReadHostFile(
            System.Text.Encoding.UTF8.GetBytes(csv), "hosts.csv");
        Assert.Contains("HOST_TYPE", file.Headers);
        var row = Assert.Single(file.Rows);
        Assert.Equal("Production", row.Values["RESOURCE_GROUP"]);
        Assert.Equal("0", row.Values["IGNORE_CERT_PROBS"]);
    }

    [Fact]
    public void ReadHostFile_NormalizesLegacyHeadersToTaskImportConvention()
    {
        var csv = "Name,HostType,Host,ResourceGroup,IgnoreCertProbs,Password\nVendor,SFTP,example.com,Production,0,  retain\\x41  ";
        var file = HostSpreadsheetImportService.ReadHostFile(
            System.Text.Encoding.UTF8.GetBytes(csv), "hosts.csv");
        Assert.Contains("HOST_TYPE", file.Headers);
        Assert.Contains("RESOURCE_GROUP", file.Headers);
        Assert.Contains("IGNORE_CERT_PROBS", file.Headers);
        Assert.DoesNotContain("HOSTTYPE", file.Headers);
        var row = Assert.Single(file.Rows);
        Assert.Equal("SFTP", row.Values["HOST_TYPE"]);
        Assert.Equal("Production", row.Values["RESOURCE_GROUP"]);
        Assert.Equal("  retain\\x41  ", row.Values["PASSWORD"]);
    }

    [Fact]
    public void ReadHostFile_RejectsAliasAndCanonicalHeaderCollisions()
    {
        var csv = "NAME,HOSTTYPE,HOST_TYPE\nVendor,SFTP,SFTP";
        var error = Assert.Throws<InvalidOperationException>(() => HostSpreadsheetImportService.ReadHostFile(
            System.Text.Encoding.UTF8.GetBytes(csv), "hosts.csv"));
        Assert.Contains("Duplicate Host import columns", error.Message, StringComparison.Ordinal);
    }


    [Theory]
    [InlineData("MOVEit Transfer", "siLock", 443)]
    [InlineData("siLock", "siLock", 443)]
    [InlineData("POP3", "POP3", 110)]
    [InlineData("SMTP", "SMTP", 25)]
    public void Phase2Step1_Parse_NormalizesNativeTypesAndPorts(string spreadsheetType, string expectedType, int defaultPort)
    {
        var input = Values("NAME", "TEST");
        input["HOST_TYPE"] = spreadsheetType;
        input["HOST"] = "mail.example.com";
        input["USERNAME"] = "serviceuser";
        input["PASSWORD"] = "example secret";
        var def = HostBasicDefinitionProfile.Parse(input);
        Assert.Equal(expectedType, def.HostType);
        Assert.Equal(defaultPort, def.Port);
        Assert.Equal("mail.example.com", def.Host);
    }

    [Theory]
    [InlineData("siLock", "1")]
    [InlineData("MOVEIT TRANSFER", "0")]
    public void Phase2Step1_MoveItTransferSecureYOrNMapsToNativeBoolean(string spreadsheetType, string expected)
    {
        var input = Values("NAME", "TRANSFER");
        input["HOST_TYPE"] = spreadsheetType;
        input["HOST"] = "transfer.example.com";
        input["SECURE"] = expected == "1" ? "y" : "N";
        input["IGNORE_CERT_PROBS"] = "N";
        input["SCRIPT_PATH"] = "siLockNet";
        var def = HostBasicDefinitionProfile.Parse(input);
        Assert.Equal(expected, def.Secure);
        const string skeleton = "<siLock ID=\"15\" Name=\"TRANSFER\" Host=\"\" Port=\"443\" Secure=\"1\" IgnoreCertProbs=\"0\" ScriptPath=\"siLockNet\" />";
        var xml = System.Xml.Linq.XElement.Parse(
            HostBasicDefinitionProfile.Compose(skeleton, def, "ENCRYPTED", ""));
        Assert.Equal(expected, (string?)xml.Attribute("Secure"));
        Assert.Equal("siLockNet", (string?)xml.Attribute("ScriptPath"));
        Assert.Equal("ENCRYPTED", (string?)xml.Attribute("DefPassword"));
    }

    [Fact]
    public void Phase2Step1_Pop3CreatePreservesNativeDefaultsAndEncryptsCredential()
    {
        var input = Values("NAME", "INBOX");
        input["HOST_TYPE"] = "POP3";
        input["HOST"] = "pop.example.com";
        input["PORT"] = "110";
        input["USERNAME"] = "mailuser";
        input["PASSWORD"] = "plaintext";
        var def = HostBasicDefinitionProfile.Parse(input);
        const string skeleton = "<POP3 ID=\"17\" Name=\"INBOX\" Host=\"\" Port=\"110\" DefRetryCount=\"0\"/>";
        var xml = System.Xml.Linq.XElement.Parse(HostBasicDefinitionProfile.Compose(skeleton, def, "CIPHER", ""));
        Assert.Equal("POP3", xml.Name.LocalName);
        Assert.Equal("pop.example.com", (string?)xml.Attribute("Host"));
        Assert.Equal("CIPHER", (string?)xml.Attribute("DefPassword"));
        Assert.Equal("0", (string?)xml.Attribute("DefRetryCount"));
        Assert.DoesNotContain("plaintext", xml.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Phase2Step1_SmtpCreateAndUpdatePreserveExistingSettings()
    {
        var input = Values("NAME", "MAILOUT");
        input["HOST_TYPE"] = "SMTP";
        input["HOST"] = "smtp.example.com";
        input["USERNAME"] = "mailuser";
        input["PASSWORD"] = "secret";
        input["FROM_ADDRESS"] = "notifications@example.com";
        var def = HostBasicDefinitionProfile.Parse(input);
        const string skeleton = "<SMTP ID=\"18\" Name=\"MAILOUT\" Host=\"\" Port=\"25\" DefAddressFrom=\"\" DefRetryCount=\"3\" />";
        var root = System.Xml.Linq.XElement.Parse(HostBasicDefinitionProfile.Compose(skeleton, def, "CIPHER", ""));
        Assert.Equal("SMTP", root.Name.LocalName);
        Assert.Equal("notifications@example.com", (string?)root.Attribute("DefAddressFrom"));
        Assert.Equal("3", (string?)root.Attribute("DefRetryCount"));
        var update = HostBasicDefinitionProfile.ParseUpdate(new Dictionary<string, string>
        {
            ["NAME"] = "MAILOUT", ["HOST_TYPE"] = "SMTP",
            ["FROM_ADDRESS"] = "no-reply@example.com", ["PASSWORD"] = ""
        });
        var updated = System.Xml.Linq.XElement.Parse(HostBasicDefinitionProfile.ComposeUpdate(root.ToString(), update, "", ""));
        Assert.Equal("no-reply@example.com", (string?)updated.Attribute("DefAddressFrom"));
        Assert.Equal("CIPHER", (string?)updated.Attribute("DefPassword"));
        Assert.Equal("18", (string?)updated.Attribute("ID"));
        Assert.Equal("3", (string?)updated.Attribute("DefRetryCount"));
    }

    [Theory]
    [InlineData("POP3", "SECURE", "Y")]
    [InlineData("SMTP", "SECURE", "N")]
    [InlineData("POP3", "SCRIPT_PATH", "siLockNet")]
    [InlineData("SMTP", "SCRIPT_PATH", "siLockNet")]
    [InlineData("POP3", "FROM_ADDRESS", "a@example.com")]
    [InlineData("siLock", "FROM_ADDRESS", "a@example.com")]
    public void Phase2Step1_UnsupportedProtocolFieldRejectsCreateAndUpdate(string type, string field, string value)
    {
        var input = Values("NAME", "TEST");
        input["HOST_TYPE"] = type;
        input["HOST"] = "example.com";
        input[field] = value;
        var createError = Assert.Throws<ArgumentException>(() => HostBasicDefinitionProfile.Parse(input));
        var updateError = Assert.Throws<ArgumentException>(() => HostBasicDefinitionProfile.ParseUpdate(input));
        Assert.Equal(field, createError.ParamName);
        Assert.Equal(field, updateError.ParamName);
    }

    [Theory]
    [InlineData("siLock", "Y", "1")]
    [InlineData("siLock", "N", "0")]
    public void Phase2Step1_TransferCertificateChoiceNormalizesOnUpdate(string type, string choice, string native)
    {
        var fields = Values("NAME", "TRANSFER");
        fields["HOST_TYPE"] = type;
        fields["IGNORE_CERT_PROBS"] = choice;
        var update = HostBasicDefinitionProfile.ParseUpdate(fields);
        var xml = System.Xml.Linq.XElement.Parse(HostBasicDefinitionProfile.ComposeUpdate(
            "<siLock ID=\"81\" Name=\"TRANSFER\" IgnoreCertProbs=\"0\" />", update, "", ""));
        Assert.Equal(native, (string?)xml.Attribute("IgnoreCertProbs"));
    }


    [Theory]
    [InlineData("Amazon S3", "S3", "BUCKET", "partner-inbound")]
    [InlineData("AWS S3", "S3", "BUCKET", "partner-inbound")]
    [InlineData("Azure Blob", "AzureBlob", "CONTAINER", "daily-extracts")]
    [InlineData("Google Cloud Storage", "GoogleStorage", "BUCKET", "partner-inbound")]
    [InlineData("GCS", "GoogleStorage", "BUCKET", "partner-inbound")]
    public void Phase2Step2_ParseCloudHost_UsesNativeTypeAndLocation(
        string type, string nativeType, string locationColumn, string location)
    {
        var values = Values("NAME", "CLOUD");
        values["HOST_TYPE"] = type;
        values["USERNAME"] = "access-key-id";
        values["PASSWORD"] = "raw-secret";
        values[locationColumn] = location;
        var definition = HostBasicDefinitionProfile.Parse(values);
        Assert.Equal(nativeType, definition.HostType);
        Assert.Equal(0, definition.Port); // No Host/Port fields in cloud native skeletons.
        Assert.Equal(string.Empty, definition.Host);
        Assert.Equal(locationColumn == "BUCKET" ? location : "", definition.Bucket);
        Assert.Equal(locationColumn == "CONTAINER" ? location : "", definition.Container);
        Assert.Equal("raw-secret", definition.Password);
    }

    [Theory]
    [InlineData("S3", "Y", "1", "ifsure")]
    [InlineData("Amazon S3", "N", "0", "always")]
    [InlineData("Azure Blob", "Y", "1", "never")]
    [InlineData("AzureBlob", "N", "0", "ignore")]
    public void Phase2Step2_CreateCloudHost_ComposesNativeSecureAndIntegrity(
        string type, string secure, string nativeSecure, string integrity)
    {
        var values = Values("NAME", "CLOUD");
        values["HOST_TYPE"] = type;
        values["USERNAME"] = "access";
        values["PASSWORD"] = "plaintext-key";
        values["SECURE"] = secure;
        values["DATA_INTEGRITY"] = integrity;
        if (type.Contains("Azure", StringComparison.OrdinalIgnoreCase))
            values["CONTAINER"] = "mycontainer";
        else values["BUCKET"] = "mybucket";

        var definition = HostBasicDefinitionProfile.Parse(values);
        var nativeXml = definition.HostType == "S3"
            ? "<S3 ID=\"12\" Name=\"CLOUD\" Secure=\"1\" DefBucket=\"\" DefRetryCount=\"0\" />"
            : "<AzureBlob ID=\"12\" Name=\"CLOUD\" Secure=\"1\" DefContainer=\"\" DefRetryCount=\"0\" />";
        var saved = XElement.Parse(HostBasicDefinitionProfile.Compose(nativeXml, definition, "ENCRYPTED_KEY", ""));
        Assert.Equal(nativeSecure, (string?)saved.Attribute("Secure"));
        Assert.Equal(integrity, (string?)saved.Attribute("DefDLInteg"));
        Assert.Equal("ENCRYPTED_KEY", (string?)saved.Attribute("DefPassword"));
        Assert.Equal("access", (string?)saved.Attribute("DefUsername"));
        Assert.Equal("0", (string?)saved.Attribute("DefRetryCount"));
        Assert.Null(saved.Attribute("Host"));
        Assert.Null(saved.Attribute("Port"));
        Assert.Equal(definition.HostType == "S3" ? "mybucket" : "mycontainer",
            (string?)saved.Attribute(definition.HostType == "S3" ? "DefBucket" : "DefContainer"));
        Assert.DoesNotContain("plaintext-key", saved.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Phase2Step2_GoogleStorageHmacUsesExistingNativeModeAndEncryptedSecret()
    {
        var values = Values("NAME", "GCS");
        values["HOST_TYPE"] = "Google Storage";
        values["BUCKET"] = "my-bucket";
        values["USERNAME"] = "HMAC_ID";
        values["PASSWORD"] = "google-hmac-secret";
        var definition = HostBasicDefinitionProfile.Parse(values);
        const string skeleton = "<GoogleStorage ID=\"101\" Name=\"GCS\" DefBucket=\"\" UseOAuth=\"0\" DefRetryCount=\"0\" Secure=\"1\" />";
        var root = XElement.Parse(HostBasicDefinitionProfile.Compose(skeleton, definition, "COM_SECRET", ""));
        Assert.Equal("0", (string?)root.Attribute("UseOAuth"));
        Assert.Equal("my-bucket", (string?)root.Attribute("DefBucket"));
        Assert.Equal("HMAC_ID", (string?)root.Attribute("DefUsername"));
        Assert.Equal("COM_SECRET", (string?)root.Attribute("DefPassword"));
        Assert.Equal("1", (string?)root.Attribute("Secure"));
    }

    [Theory]
    [InlineData("S3", "BUCKET", "new-bucket")]
    [InlineData("AzureBlob", "CONTAINER", "newcontainer")]
    [InlineData("GoogleStorage", "BUCKET", "new-bucket")]
    public void Phase2Step2_CloudUpdatePreservesIdentitySecretAndNativeDefaults(
        string nativeType, string changedColumn, string changedValue)
    {
        var original = $"<{nativeType} ID=\"555\" Name=\"CLOUD\" DefBucket=\"old-bucket\" DefContainer=\"oldcontainer\" UseOAuth=\"0\" DefUsername=\"olduser\" DefPassword=\"SAVED_CIPHER\" DefRetryCount=\"7\" Secure=\"1\" Extension=\"keep\" />";
        var values = Values("NAME", "CLOUD");
        values["HOST_TYPE"] = nativeType;
        values[changedColumn] = changedValue;
        values["PASSWORD"] = "";
        values["USERNAME"] = "";
        var update = HostBasicDefinitionProfile.ParseUpdate(values);
        var saved = XElement.Parse(HostBasicDefinitionProfile.ComposeUpdate(original, update, "", ""));
        Assert.Equal("555", (string?)saved.Attribute("ID"));
        Assert.Equal("SAVED_CIPHER", (string?)saved.Attribute("DefPassword"));
        Assert.Equal("olduser", (string?)saved.Attribute("DefUsername"));
        Assert.Equal("7", (string?)saved.Attribute("DefRetryCount"));
        Assert.Equal("keep", (string?)saved.Attribute("Extension"));
        Assert.Equal(changedValue, (string?)saved.Attribute(changedColumn == "BUCKET" ? "DefBucket" : "DefContainer"));
        Assert.Equal("1", (string?)saved.Attribute("Secure"));
    }

    [Fact]
    public void Phase2Step2_GoogleOAuthSavedHostRejectsHmacCredentialUpdates()
    {
        var values = Values("NAME", "GOOGLE");
        values["HOST_TYPE"] = "GCS";
        values["USERNAME"] = "new_hmac_access_key";
        var patch = HostBasicDefinitionProfile.ParseUpdate(values);
        Assert.Throws<InvalidOperationException>(() => HostBasicDefinitionProfile.ComposeUpdate(
            "<GoogleStorage ID=\"12\" Name=\"GOOGLE\" UseOAuth=\"1\" DefUsername=\"oauth\" />", patch, "", ""));

        values.Remove("USERNAME");
        values["BUCKET"] = "new-bucket";
        patch = HostBasicDefinitionProfile.ParseUpdate(values);
        var updated = XElement.Parse(HostBasicDefinitionProfile.ComposeUpdate(
            "<GoogleStorage ID=\"12\" Name=\"GOOGLE\" UseOAuth=\"1\" DefUsername=\"oauth\" />", patch, "", ""));
        Assert.Equal("new-bucket", (string?)updated.Attribute("DefBucket"));
        Assert.Equal("oauth", (string?)updated.Attribute("DefUsername"));
    }

    [Theory]
    [InlineData("S3", "CONTAINER", "not-container")]
    [InlineData("AzureBlob", "BUCKET", "not-bucket")]
    [InlineData("GoogleStorage", "CONTAINER", "not-container")]
    [InlineData("S3", "HOST", "s3.amazonaws.com")]
    [InlineData("AzureBlob", "PORT", "443")]
    [InlineData("GoogleStorage", "SECURE", "Y")]
    [InlineData("GoogleStorage", "DATA_INTEGRITY", "never")]
    [InlineData("AzureBlob", "DATA_INTEGRITY", "ifsure")]
    [InlineData("S3", "DATA_INTEGRITY", "incorrect")]
    [InlineData("S3", "IGNORE_CERT_PROBS", "Y")]
    [InlineData("S3", "SSHKEY", "partner")]
    public void Phase2Step2_CloudUnsupportedFieldsFailBothValidationPaths(
        string type, string column, string value)
    {
        var values = Values("NAME", "CLOUD");
        values["HOST_TYPE"] = type;
        values["USERNAME"] = "user";
        values["PASSWORD"] = "pw";
        values[type == "AzureBlob" ? "CONTAINER" : "BUCKET"] = "default-location";
        values[column] = value;
        var created = Assert.Throws<ArgumentException>(() => HostBasicDefinitionProfile.Parse(values));
        var updated = Assert.Throws<ArgumentException>(() => HostBasicDefinitionProfile.ParseUpdate(values));
        Assert.Equal(column, created.ParamName);
        Assert.Equal(column, updated.ParamName);
    }

    [Theory]
    [InlineData("S3", "BUCKET")]
    [InlineData("AzureBlob", "CONTAINER")]
    [InlineData("GoogleStorage", "BUCKET")]
    public void Phase2Step2_CloudCreateRequiresLocationAndCredentials(string type, string column)
    {
        var values = Values("NAME", "CLOUD");
        values["HOST_TYPE"] = type;
        values["USERNAME"] = "id";
        values["PASSWORD"] = "secret";
        var missingLocation = Assert.Throws<ArgumentException>(() => HostBasicDefinitionProfile.Parse(values));
        Assert.Equal(column, missingLocation.ParamName);
        values[column] = "my-location";
        values.Remove("USERNAME");
        Assert.Equal("USERNAME", Assert.Throws<ArgumentException>(() => HostBasicDefinitionProfile.Parse(values)).ParamName);
        values["USERNAME"] = "id";
        values.Remove("PASSWORD");
        Assert.Equal("PASSWORD", Assert.Throws<ArgumentException>(() => HostBasicDefinitionProfile.Parse(values)).ParamName);
    }


    private static Dictionary<string, string> SharePointCertificateFields()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["NAME"] = "TEAM_SITE", ["HOST_TYPE"] = "SharePoint",
            ["TENANT_NAME"] = "contoso",
            ["SITE"] = "Operations",
            ["SITE_URL_PREFIX"] = "/sites/",
            ["CLIENT_ID"] = "8b16c6b1-3f07-40dc-862c-4b4615762f05",
            ["TENANT_ID"] = "f8b3432b-9c13-4e55-af6b-3935fdc5631e",
            ["DOCUMENT_LIBRARY"] = "Documents",
            ["CERT_STORE"] = "My",
            ["CERT_SUBJECT"] = "CN=MAUI SharePoint Test",
            ["CERT_SHA1"] = "0123456789ABCDEF0123456789ABCDEF01234567"
        };
    }

    [Theory]
    [InlineData("SharePoint")]
    [InlineData("SharePoint Online")]
    public void Phase2Step3_SharePointCreateUsesNativeCertificateProfile(string requestedType)
    {
        var data = SharePointCertificateFields();
        data["HOST_TYPE"] = requestedType;
        var definition = HostBasicDefinitionProfile.Parse(data);
        Assert.Equal("SharePoint", definition.HostType);
        Assert.Equal(0, definition.Port);
        Assert.Equal(string.Empty, definition.Host);
        const string skeleton = "<SharePoint ID=\"42\" Name=\"TEAM_SITE\" TenantName=\"\" Site=\"\" SiteUrlPrefix=\"/sites/\" UsingAppAuthentication=\"2\" ClientID=\"\" TenantID=\"\" DefDocumentLibrary=\"\" DefDocumentLibraryGuid=\"\" DefClientCertStore=\"\" DefClientCertSubject=\"\" DefClientCertSHA1=\"\" ProxyType=\"None\" Secure=\"1\" DefRetryCount=\"0\"><Desc></Desc></SharePoint>";
        var root = XElement.Parse(HostBasicDefinitionProfile.Compose(skeleton, definition, string.Empty, string.Empty));
        Assert.Equal("2", (string?)root.Attribute("UsingAppAuthentication"));
        Assert.Equal("contoso", (string?)root.Attribute("TenantName"));
        Assert.Equal("Operations", (string?)root.Attribute("Site"));
        Assert.Equal("/sites/", (string?)root.Attribute("SiteUrlPrefix"));
        Assert.Equal("Documents", (string?)root.Attribute("DefDocumentLibrary"));
        Assert.Equal("My", (string?)root.Attribute("DefClientCertStore"));
        Assert.Equal("CN=MAUI SharePoint Test", (string?)root.Attribute("DefClientCertSubject"));
        Assert.Equal("0123456789ABCDEF0123456789ABCDEF01234567", (string?)root.Attribute("DefClientCertSHA1"));
        Assert.Equal("0", (string?)root.Attribute("DefRetryCount"));
        Assert.Null(root.Attribute("DefUsername"));
        Assert.Null(root.Attribute("DefPassword"));
        Assert.Null(root.Attribute("Host"));
        Assert.Null(root.Attribute("Port"));
    }

    [Fact]
    public void Phase2Step3_SharePointSparseUpdatePreservesHostIdentityCertificateAndDefaults()
    {
        var current = "<SharePoint ID=\"17\" Name=\"TEAM_SITE\" TenantName=\"contoso\" Site=\"Operations\" SiteUrlPrefix=\"/sites/\" UsingAppAuthentication=\"2\" ClientID=\"app1\" TenantID=\"tenant1\" DefDocumentLibrary=\"Old Documents\" DefClientCertStore=\"My\" DefClientCertSubject=\"CN=Existing\" DefClientCertSHA1=\"0123456789ABCDEF0123456789ABCDEF01234567\" DefRetryCount=\"9\" ProxyType=\"None\" Extra=\"keep\" />";
        var patch = HostBasicDefinitionProfile.ParseUpdate(new Dictionary<string, string>
        {
            ["NAME"] = "TEAM_SITE", ["HOST_TYPE"] = "SharePoint", ["DOCUMENT_LIBRARY"] = "New Documents",
            ["CERT_STORE"] = "", ["CERT_SHA1"] = "", ["TENANT_ID"] = ""
        });
        var root = XElement.Parse(HostBasicDefinitionProfile.ComposeUpdate(current, patch, "", ""));
        Assert.Equal("17", (string?)root.Attribute("ID"));
        Assert.Equal("New Documents", (string?)root.Attribute("DefDocumentLibrary"));
        Assert.Equal("My", (string?)root.Attribute("DefClientCertStore"));
        Assert.Equal("0123456789ABCDEF0123456789ABCDEF01234567", (string?)root.Attribute("DefClientCertSHA1"));
        Assert.Equal("tenant1", (string?)root.Attribute("TenantID"));
        Assert.Equal("9", (string?)root.Attribute("DefRetryCount"));
        Assert.Equal("keep", (string?)root.Attribute("Extra"));
    }

    [Fact]
    public void Phase2Step3_SharePointRefusesUnverifiedAuthenticationModeOnUpdate()
    {
        var patch = HostBasicDefinitionProfile.ParseUpdate(new Dictionary<string, string>
        {
            ["NAME"] = "TEAM_SITE", ["HOST_TYPE"] = "SharePoint", ["SITE"] = "newsite"
        });
        Assert.Throws<InvalidOperationException>(() => HostBasicDefinitionProfile.ComposeUpdate(
            "<SharePoint ID=\"17\" Name=\"TEAM_SITE\" UsingAppAuthentication=\"1\" />", patch, "", ""));
    }

    [Theory]
    [InlineData("HOST", "contoso.sharepoint.com")]
    [InlineData("PORT", "443")]
    [InlineData("USERNAME", "user")]
    [InlineData("PASSWORD", "a-secret")]
    [InlineData("SECURE", "Y")]
    [InlineData("SCRIPT_PATH", "path")]
    [InlineData("BUCKET", "x")]
    [InlineData("DOCUMENT_LIBRARY_GUID", "not-guid")]
    [InlineData("CERT_SHA1", "not-a-thumbprint")]
    [InlineData("SITE_URL_PREFIX", "https://contoso.sharepoint.com/sites/")]
    public void Phase2Step3_SharePointRejectsUnsupportedOrMalformedColumns(string column, string input)
    {
        var data = SharePointCertificateFields();
        data[column] = input;
        Assert.Equal(column, Assert.Throws<ArgumentException>(() => HostBasicDefinitionProfile.Parse(data)).ParamName);
        Assert.Equal(column, Assert.Throws<ArgumentException>(() => HostBasicDefinitionProfile.ParseUpdate(data)).ParamName);
    }

    [Theory]
    [InlineData("TENANT_NAME")]
    [InlineData("SITE")]
    [InlineData("CLIENT_ID")]
    [InlineData("TENANT_ID")]
    [InlineData("DOCUMENT_LIBRARY")]
    [InlineData("CERT_STORE")]
    [InlineData("CERT_SUBJECT")]
    [InlineData("CERT_SHA1")]
    public void Phase2Step3_SharePointCreateRequiresVerifiedCertificateFields(string required)
    {
        var data = SharePointCertificateFields();
        data.Remove(required);
        var error = Assert.Throws<ArgumentException>(() => HostBasicDefinitionProfile.Parse(data));
        Assert.Equal(required, error.ParamName);
    }

    [Fact]
    public void Phase2Step3_SharePointColumnsAreNotAcceptedForOtherHosts()
    {
        var data = Values("NAME", "S3_HOST");
        data["HOST_TYPE"] = "S3"; data["BUCKET"] = "bucket";
        data["USERNAME"] = "access"; data["PASSWORD"] = "secret";
        data["TENANT_NAME"] = "contoso";
        Assert.Equal("TENANT_NAME", Assert.Throws<ArgumentException>(() => HostBasicDefinitionProfile.Parse(data)).ParamName);
        Assert.Equal("TENANT_NAME", Assert.Throws<ArgumentException>(() => HostBasicDefinitionProfile.ParseUpdate(data)).ParamName);
    }

    private static Dictionary<string, string> Values(string key, string value) => new(StringComparer.OrdinalIgnoreCase) { [key] = value };
}
