using System.Globalization;
using System.Xml.Linq;

namespace MoveItAutomationUI.Services.MoveIt.Hosts;

// Authoritative shared Host connection profile used by CSV/XLSX import and Host CRUD.
// Phase 2 Step 2 adds verified S3, Azure Blob and Google Storage (HMAC) fields.
public sealed record HostBasicDefinition(
    string Name, string HostType, string Host, int Port, string Username,
    string Password, string SshKeyName, string Secure, string Unc,
    string Description, string ResourceGroup, bool IgnoreCertProblems,
    string ScriptPath = "", string FromAddress = "",
    string Bucket = "", string Container = "", string DataIntegrity = "",
    IReadOnlyDictionary<string, string>? SharePointFields = null);

// Update fields are an explicit sparse patch. Empty spreadsheet cells preserve the saved Host
// configuration, including passwords and client keys. The profile applies only supported fields.
public sealed record HostBasicUpdate(string Name, string HostType, IReadOnlyDictionary<string, string> Fields);

public static class HostBasicDefinitionProfile
{
    private static readonly HashSet<string> CloudTypes = new(StringComparer.OrdinalIgnoreCase)
    { "S3", "AzureBlob", "GoogleStorage" };

    private static readonly HashSet<string> FtpSecureValues = new(StringComparer.OrdinalIgnoreCase)
    { "on", "1", "ssl", "tls-p", "on-ccc", "tls-p-ccc", "off", "none", "0", "tls", "tls-c", "tls-c-ccc", "implicit", "implicit-ccc" };

    // Keep the CSV/XLSX contract consistent with Task Import: binary spreadsheet
    // choices are Y or N. Only the native MOVEit Host XML uses 1 or 0.
    // Blank is allowed: create defaults to N, update preserves the saved value.
    private static string NormalizeIgnoreCertProblems(string value, string hostType)
    {
        var input = (value ?? string.Empty).Trim();
        if (input.Length == 0) return string.Empty;
        if (input.Equals("N", StringComparison.OrdinalIgnoreCase)) return "0";
        if (!input.Equals("Y", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("IGNORE_CERT_PROBS must be Y or N.", "IGNORE_CERT_PROBS");
        if (hostType is not ("FTP" or "siLock"))
            throw new ArgumentException("IGNORE_CERT_PROBS=Y is supported only for FTP/FTPS and MOVEit Transfer Hosts.", "IGNORE_CERT_PROBS");
        return "1";
    }

    // Unlike FTP's protocol-mode SECURE column, MOVEit Transfer uses a native
    // binary Secure flag. Do not silently reinterpret Y/N as an FTP TLS mode.
    private static string NormalizeSecureFlag(string value, string type)
    {
        var input = (value ?? string.Empty).Trim();
        if (type is "siLock" or "S3" or "AzureBlob")
        {
            if (input.Length == 0) return string.Empty;
            if (input.Equals("Y", StringComparison.OrdinalIgnoreCase)) return "1";
            if (input.Equals("N", StringComparison.OrdinalIgnoreCase)) return "0";
            throw new ArgumentException("SECURE must be Y or N for MOVEit Transfer, S3 or Azure Blob Hosts.", "SECURE");
        }
        if (type == "FTP") return input;
        if (input.Length > 0)
            throw new ArgumentException("SECURE is not supported by this basic Host import profile for the selected Host type.", "SECURE");
        return string.Empty;
    }

    private static string NormalizeType(string type) => type switch
    {
        "FTP" or "FTPS" => "FTP",
        "SFTP" or "SSHFTP" or "SSH/SFTP" => "SSHFTP",
        "UNC" or "SHARE" or "FILESHARE" or "FILE SHARE" => "Share",
        "MOVEIT TRANSFER" or "MOVEIT_TRANSFER" or "MOVEITTRANSFER" or "SILOCK" => "siLock",
        "POP3" => "POP3",
        "SMTP" => "SMTP",
        "S3" or "AWS S3" or "AMAZON S3" => "S3",
        "AZUREBLOB" or "AZURE BLOB" or "AZURE BLOB STORAGE" => "AzureBlob",
        "GOOGLESTORAGE" or "GOOGLE STORAGE" or "GOOGLE CLOUD STORAGE" or "GCS" => "GoogleStorage",
        "SHAREPOINT" or "SHAREPOINT ONLINE" => "SharePoint",
        _ => throw new ArgumentException(
            "HOST_TYPE must be FTP, FTPS, SFTP, UNC, MOVEIT TRANSFER, POP3, SMTP, S3, AZURE BLOB, GOOGLE STORAGE, or SHAREPOINT.", "HOST_TYPE")
    };

    private static void ValidateFtpsMode(string requestedType, string secure)
    {
        if (requestedType == "FTPS" && (secure.Equals("none", StringComparison.OrdinalIgnoreCase)
            || secure.Equals("off", StringComparison.OrdinalIgnoreCase) || secure == "0"))
            throw new ArgumentException("FTPS requires a TLS mode. Use HOST_TYPE=FTP to explicitly configure plaintext FTP.", "SECURE");
    }

    private static int DefaultPort(string type) => type switch
    {
        "FTP" => 21,
        "SSHFTP" => 22,
        "siLock" => 443,
        "POP3" => 110,
        "SMTP" => 25,
        _ => 0
    };

    private static void ValidateProtocolExtras(string type, string scriptPath, string fromAddress)
    {
        if (scriptPath.Length > 0 && type != "siLock")
            throw new ArgumentException("SCRIPT_PATH applies only to MOVEit Transfer Hosts.", "SCRIPT_PATH");
        if (fromAddress.Length > 0 && type != "SMTP")
            throw new ArgumentException("FROM_ADDRESS applies only to SMTP Hosts.", "FROM_ADDRESS");
        if (fromAddress.Contains('\r') || fromAddress.Contains('\n'))
            throw new ArgumentException("FROM_ADDRESS cannot contain line breaks.", "FROM_ADDRESS");
    }

    // Align exactly with the S3/Azure writable values in the shared HostDefinitionSaveService.
    // Google Storage has no writable host-level integrity rule.
    private static string ValidateCloudFields(
        string type, string bucket, string container, string integrity, string username,
        string password, string host, string port, bool creating)
    {
        if (!CloudTypes.Contains(type))
        {
            if (bucket.Length > 0) throw new ArgumentException("BUCKET is supported only for S3 and Google Storage.", "BUCKET");
            if (container.Length > 0) throw new ArgumentException("CONTAINER is supported only for Azure Blob.", "CONTAINER");
            if (integrity.Length > 0) throw new ArgumentException("DATA_INTEGRITY is supported only for S3 and Azure Blob.", "DATA_INTEGRITY");
            return string.Empty;
        }
        if (host.Length > 0) throw new ArgumentException("HOST is not used for cloud Hosts. Configure the bucket or container instead.", "HOST");
        if (port.Length > 0) throw new ArgumentException("PORT is not configurable through this cloud Host profile.", "PORT");
        if (type == "AzureBlob")
        {
            if (bucket.Length > 0) throw new ArgumentException("BUCKET is not supported for Azure Blob; use CONTAINER.", "BUCKET");
            if (creating && container.Length == 0) throw new ArgumentException("CONTAINER is required for Azure Blob Hosts.", "CONTAINER");
        }
        else
        {
            if (container.Length > 0) throw new ArgumentException("CONTAINER is supported only for Azure Blob.", "CONTAINER");
            if (creating && bucket.Length == 0) throw new ArgumentException("BUCKET is required for S3 and Google Storage Hosts.", "BUCKET");
        }
        var normalizedIntegrity = integrity.ToLowerInvariant();
        if (normalizedIntegrity.Length > 0)
        {
            var supported = type switch
            {
                "S3" => normalizedIntegrity is "never" or "ifsure" or "ignore" or "always",
                "AzureBlob" => normalizedIntegrity is "never" or "ignore" or "always",
                _ => false
            };
            if (!supported)
                throw new ArgumentException("DATA_INTEGRITY is unsupported for this Host type (S3: never/ifsure/ignore/always; Azure: never/ignore/always).", "DATA_INTEGRITY");
        }
        if (creating && username.Length == 0)
            throw new ArgumentException("USERNAME (access key ID/storage account) is required for cloud Host creation.", "USERNAME");
        if (creating && password.Length == 0)
            throw new ArgumentException("PASSWORD (secret key/account key) is required for cloud Host creation.", "PASSWORD");
        return normalizedIntegrity;
    }

    // Captured native SharePoint Host skeleton and HostConnectionTestService only verify
    // UsingAppAuthentication=2 (application + client certificate). Preserve this mode;
    // neither username/password login nor client-secret auth is part of this profile.
    private static readonly IReadOnlyDictionary<string, string> SharePointAttributes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["TENANT_NAME"] = "TenantName",
            ["SITE"] = "Site",
            ["SITE_URL_PREFIX"] = "SiteUrlPrefix",
            ["CLIENT_ID"] = "ClientID",
            ["TENANT_ID"] = "TenantID",
            ["DOCUMENT_LIBRARY"] = "DefDocumentLibrary",
            ["DOCUMENT_LIBRARY_GUID"] = "DefDocumentLibraryGuid",
            ["CERT_STORE"] = "DefClientCertStore",
            ["CERT_SUBJECT"] = "DefClientCertSubject",
            ["CERT_SHA1"] = "DefClientCertSHA1"
        };

    private static IReadOnlyDictionary<string, string> ValidateSharePointFields(
        IReadOnlyDictionary<string, string> values, string type, bool creating)
    {
        string Get(string key) => values.TryGetValue(key, out var item) ? (item ?? string.Empty).Trim() : string.Empty;
        var provided = SharePointAttributes.Keys
            .Where(key => Get(key).Length > 0)
            .ToDictionary(key => key, Get, StringComparer.OrdinalIgnoreCase);
        if (type != "SharePoint")
        {
            if (provided.Count > 0)
                throw new ArgumentException("SharePoint columns are supported only for SharePoint Hosts.", provided.Keys.First());
            return provided;
        }
        foreach (var column in new[] { "HOST", "PORT", "USERNAME", "PASSWORD", "UNC", "SSHKEY",
                    "SECURE", "IGNORE_CERT_PROBS", "SCRIPT_PATH", "FROM_ADDRESS", "BUCKET", "CONTAINER", "DATA_INTEGRITY" })
        {
            if (column == "IGNORE_CERT_PROBS" && Get(column).Equals("N", StringComparison.OrdinalIgnoreCase)) continue;
            if (Get(column).Length > 0)
                throw new ArgumentException("This field is not supported by the SharePoint application/certificate profile.", column);
        }

        var sha = Get("CERT_SHA1");
        if (sha.Length > 0 && (sha.Length != 40 || !sha.All(Uri.IsHexDigit)))
            throw new ArgumentException("CERT_SHA1 must be a 40-character hexadecimal certificate thumbprint.", "CERT_SHA1");
        var libraryGuid = Get("DOCUMENT_LIBRARY_GUID");
        if (libraryGuid.Length > 0 && !Guid.TryParse(libraryGuid, out _))
            throw new ArgumentException("DOCUMENT_LIBRARY_GUID must be a valid GUID.", "DOCUMENT_LIBRARY_GUID");
        var prefix = Get("SITE_URL_PREFIX");
        if (prefix.Length > 0 && (!prefix.StartsWith("/", StringComparison.Ordinal)
            || prefix.Contains('\r') || prefix.Contains('\n') || prefix.Contains('?') || prefix.Contains('#')))
            throw new ArgumentException("SITE_URL_PREFIX must be a path beginning with / (for example /sites/).", "SITE_URL_PREFIX");
        foreach (var pair in provided)
            if (pair.Value.Contains('\r') || pair.Value.Contains('\n'))
                throw new ArgumentException("SharePoint fields cannot contain line breaks.", pair.Key);

        if (creating)
        {
            foreach (var column in new[] { "TENANT_NAME", "SITE", "CLIENT_ID", "TENANT_ID",
                        "DOCUMENT_LIBRARY", "CERT_STORE", "CERT_SUBJECT", "CERT_SHA1" })
                if (Get(column).Length == 0)
                    throw new ArgumentException("This SharePoint application/certificate setting is required for Host creation.", column);
        }
        return provided;
    }

    public static HostBasicDefinition Parse(IReadOnlyDictionary<string, string> values, string prefix = "")
    {
        string Get(string key) => values.TryGetValue(key, out var item)
            ? (key.Equals("PASSWORD", StringComparison.OrdinalIgnoreCase) ? item ?? string.Empty : (item ?? string.Empty).Trim())
            : string.Empty;
        var name = (prefix + Get("NAME")).Trim();
        var requestedType = Get("HOST_TYPE").ToUpperInvariant();
        var type = NormalizeType(requestedType);
        if (name.Length == 0 || name.Length > 254) throw new ArgumentException("Name is required and cannot exceed 254 characters.", "NAME");
        if (prefix.Length > 60) throw new ArgumentException("Host Name Prefix cannot exceed 60 characters.", "NAME");
        var host = Get("HOST");
        var unc = Get("UNC");
        var port = DefaultPort(type);
        if (Get("PORT").Length > 0 && type != "Share" && type != "SharePoint" && !CloudTypes.Contains(type))
        {
            if (!int.TryParse(Get("PORT"), NumberStyles.None, CultureInfo.InvariantCulture, out port) || port < 1 || port > 65535)
                throw new ArgumentException("Port must be between 1 and 65535.", "PORT");
        }
        if (type == "Share" && (!unc.StartsWith(@"\\", StringComparison.Ordinal) || unc.Substring(2).Split('\\', StringSplitOptions.RemoveEmptyEntries).Length < 2))
            throw new ArgumentException("UNC must begin with \\\\ and specify a server and share.", "UNC");
        if (type != "Share" && type != "SharePoint" && !CloudTypes.Contains(type) && host.Length == 0)
            throw new ArgumentException("Host is required for this Host type.", "HOST");
        var secure = NormalizeSecureFlag(Get("SECURE"), type);
        if (requestedType == "FTPS" && secure.Length == 0)
            throw new ArgumentException("FTPS requires an explicit Secure TLS mode (for example, tls-p or implicit).", "SECURE");
        ValidateFtpsMode(requestedType, secure);
        if (type == "FTP" && secure.Length == 0) secure = "none";
        if (type == "FTP" && !FtpSecureValues.Contains(secure))
            throw new ArgumentException("Secure specifies an unsupported FTP TLS mode.", "SECURE");
        var ignoreCertProblems = NormalizeIgnoreCertProblems(Get("IGNORE_CERT_PROBS"), type) == "1";
        var sshKey = Get("SSHKEY");
        if (type == "SSHFTP" && sshKey.Length == 0 && Get("PASSWORD").Length == 0)
            throw new ArgumentException("SFTP requires Password or SSHKey.", "PASSWORD");
        if (type != "SSHFTP" && sshKey.Length > 0)
            throw new ArgumentException("SSHKey applies only to SFTP Hosts.", "SSHKEY");
        if (type != "Share" && unc.Length > 0)
            throw new ArgumentException("UNC applies only to UNC Share Hosts.", "UNC");
        var scriptPath = Get("SCRIPT_PATH");
        var fromAddress = Get("FROM_ADDRESS");
        ValidateProtocolExtras(type, scriptPath, fromAddress);
        var sharePoint = ValidateSharePointFields(values, type, creating: true);
        var bucket = Get("BUCKET");
        var container = Get("CONTAINER");
        var integrity = ValidateCloudFields(type, bucket, container, Get("DATA_INTEGRITY"),
            Get("USERNAME"), Get("PASSWORD"), host, Get("PORT"), creating: true);
        return new HostBasicDefinition(name, type, host, port, Get("USERNAME"), Get("PASSWORD"),
            sshKey, secure, unc, Get("DESCRIPTION"), Get("RESOURCE_GROUP"), ignoreCertProblems,
            scriptPath, fromAddress, bucket, container, integrity, sharePoint);
    }


    public static HostBasicUpdate ParseUpdate(IReadOnlyDictionary<string, string> values, string prefix = "")
    {
        static string Get(IReadOnlyDictionary<string, string> fields, string key)
            => fields.TryGetValue(key, out var value) ? (value ?? string.Empty).Trim() : string.Empty;
        var name = (prefix + Get(values, "NAME")).Trim();
        if (name.Length == 0 || name.Length > 254) throw new ArgumentException("Name is required and cannot exceed 254 characters.", "NAME");
        if (prefix.Length > 60) throw new ArgumentException("Host Name Prefix cannot exceed 60 characters.", "NAME");
        var requestedType = Get(values, "HOST_TYPE").ToUpperInvariant();
        var type = NormalizeType(requestedType);
        var port = Get(values, "PORT");
        if (port.Length > 0 && (type is "Share" or "SharePoint" || !int.TryParse(port, NumberStyles.None,
                CultureInfo.InvariantCulture, out var parsedPort) || parsedPort < 1 || parsedPort > 65535))
            throw new ArgumentException("Port must be between 1 and 65535 for non-UNC Hosts.", "PORT");
        var host = Get(values, "HOST");
        if (type == "Share" && host.Length > 0)
            throw new ArgumentException("HOST cannot be updated for UNC Share Hosts.", "HOST");
        var unc = Get(values, "UNC");
        if (unc.Length > 0 && (type != "Share" || !unc.StartsWith(@"\\", StringComparison.Ordinal)
                || unc.Substring(2).Split('\\', StringSplitOptions.RemoveEmptyEntries).Length < 2))
            throw new ArgumentException("UNC must specify an existing Share Host and a \\\\server\\share path.", "UNC");
        var secure = NormalizeSecureFlag(Get(values, "SECURE"), type);
        if (type == "FTP" && secure.Length > 0 && !FtpSecureValues.Contains(secure))
            throw new ArgumentException("SECURE must specify a supported FTP/FTPS TLS mode.", "SECURE");
        ValidateFtpsMode(requestedType, secure);
        var normalizedIgnoreCertProblems = NormalizeIgnoreCertProblems(Get(values, "IGNORE_CERT_PROBS"), type);
        if (Get(values, "SSHKEY").Length > 0 && type != "SSHFTP")
            throw new ArgumentException("SSHKEY applies only to SFTP Hosts.", "SSHKEY");
        if (type != "Share" && unc.Length > 0)
            throw new ArgumentException("UNC applies only to UNC Share Hosts.", "UNC");
        ValidateProtocolExtras(type, Get(values, "SCRIPT_PATH"), Get(values, "FROM_ADDRESS"));
        _ = ValidateSharePointFields(values, type, creating: false);
        var integrity = ValidateCloudFields(type, Get(values, "BUCKET"), Get(values, "CONTAINER"),
            Get(values, "DATA_INTEGRITY"), Get(values, "USERNAME"), Get(values, "PASSWORD"),
            host, port, creating: false);
        // Retain original password whitespace; it may be significant to MOVEit authentication.
        var patch = values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        if (normalizedIgnoreCertProblems.Length > 0)
            patch["IGNORE_CERT_PROBS"] = normalizedIgnoreCertProblems;
        if (type is ("siLock" or "S3" or "AzureBlob") && secure.Length > 0)
            patch["SECURE"] = secure;
        if (integrity.Length > 0)
            patch["DATA_INTEGRITY"] = integrity;
        return new HostBasicUpdate(name, type, patch);
    }

    // Apply a sparse Host update to a checked-out native definition. The COM caller supplies
    // encrypted credentials/client-key IDs and never persists the plaintext spreadsheet secret.
    public static string ComposeUpdate(string currentXml, HostBasicUpdate update, string encryptedPassword, string sshKeyId)
    {
        var root = XElement.Parse(currentXml, LoadOptions.PreserveWhitespace);
        if (!root.Name.LocalName.Equals(update.HostType, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Host type changed; analyze the spreadsheet again.");
        if (update.HostType == "SharePoint"
            && ((string?)root.Attribute("UsingAppAuthentication") ?? string.Empty).Trim() != "2")
            throw new InvalidOperationException("SharePoint Host updates require the verified application/certificate authentication mode (2).");
        if (update.HostType == "GoogleStorage"
            && (((string?)root.Attribute("UseOAuth") ?? string.Empty).Trim() is "1" or "true")
            && ((update.Fields.TryGetValue("USERNAME", out var oauthUser) && !string.IsNullOrWhiteSpace(oauthUser))
                || (update.Fields.TryGetValue("PASSWORD", out var oauthSecret) && !string.IsNullOrEmpty(oauthSecret))))
            throw new InvalidOperationException("Google Storage OAuth credentials cannot be modified by the HMAC spreadsheet profile.");
        var savedName = ((string?)root.Attribute("Name") ?? string.Empty).Trim();
        if (!savedName.Equals(update.Name, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Host name changed; analyze the spreadsheet again.");
        string Value(string name) => update.Fields.TryGetValue(name, out var value) ? (value ?? string.Empty).Trim() : string.Empty;
        void ApplyAttribute(string column, string attribute)
        {
            var value = Value(column);
            if (value.Length > 0) root.SetAttributeValue(attribute, value);
        }
        if (update.HostType == "Share") ApplyAttribute("UNC", "UNC");
        else if (!CloudTypes.Contains(update.HostType) && update.HostType != "SharePoint")
        {
            ApplyAttribute("HOST", "Host");
            ApplyAttribute("PORT", "Port");
        }
        if (update.HostType is "FTP" or "siLock" or "S3" or "AzureBlob")
        {
            ApplyAttribute("SECURE", "Secure");
            if (update.HostType is "FTP" or "siLock")
                ApplyAttribute("IGNORE_CERT_PROBS", "IgnoreCertProbs");
        }
        ApplyAttribute("USERNAME", "DefUsername");
        if (update.Fields.TryGetValue("PASSWORD", out var rawPassword) && !string.IsNullOrEmpty(rawPassword))
            root.SetAttributeValue("DefPassword", encryptedPassword);
        if (update.HostType == "SSHFTP" && Value("SSHKEY").Length > 0)
            root.SetAttributeValue("DefClientKeyID", sshKeyId);
        if (update.HostType == "siLock") ApplyAttribute("SCRIPT_PATH", "ScriptPath");
        if (update.HostType == "SMTP") ApplyAttribute("FROM_ADDRESS", "DefAddressFrom");
        if (update.HostType is "S3" or "GoogleStorage") ApplyAttribute("BUCKET", "DefBucket");
        if (update.HostType == "AzureBlob") ApplyAttribute("CONTAINER", "DefContainer");
        if (update.HostType is "S3" or "AzureBlob") ApplyAttribute("DATA_INTEGRITY", "DefDLInteg");
        if (update.HostType == "SharePoint")
            foreach (var field in SharePointAttributes)
                ApplyAttribute(field.Key, field.Value);
        var description = Value("DESCRIPTION");
        if (description.Length > 0)
        {
            // Native Hosts may represent Description/Desc as either an attribute
            // or an element. Update the existing representation without creating
            // a second, conflicting field in the checked-out definition.
            var attribute = root.Attributes().FirstOrDefault(x =>
                x.Name.LocalName.Equals("Desc", StringComparison.OrdinalIgnoreCase)
                || x.Name.LocalName.Equals("Description", StringComparison.OrdinalIgnoreCase));
            if (attribute is not null) attribute.Value = description;
            else
            {
                var element = root.Elements().FirstOrDefault(x =>
                    x.Name.LocalName.Equals("Desc", StringComparison.OrdinalIgnoreCase)
                    || x.Name.LocalName.Equals("Description", StringComparison.OrdinalIgnoreCase));
                if (element is not null) element.Value = description;
                else root.SetAttributeValue("Desc", description);
            }
        }
        return root.ToString(SaveOptions.DisableFormatting);
    }

    // Apply the connection fields to the already-established MAUI native Host skeleton;
    // never manufacture a second set of protocol defaults.
    public static string Compose(string skeleton, HostBasicDefinition definition, string encryptedPassword, string sshKeyId)
    {
        var root = XElement.Parse(skeleton, LoadOptions.PreserveWhitespace);
        if (!root.Name.LocalName.Equals(definition.HostType, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Host type does not match the native Host definition.");
        if (definition.HostType == "Share") root.SetAttributeValue("UNC", definition.Unc);
        else if (!CloudTypes.Contains(definition.HostType) && definition.HostType != "SharePoint")
        {
            root.SetAttributeValue("Host", definition.Host);
            root.SetAttributeValue("Port", definition.Port.ToString(CultureInfo.InvariantCulture));
        }
        if (definition.HostType == "FTP") root.SetAttributeValue("Secure", definition.Secure);
        if (definition.HostType is ("siLock" or "S3" or "AzureBlob") && definition.Secure.Length > 0)
            root.SetAttributeValue("Secure", definition.Secure);
        if (definition.HostType is "FTP" or "siLock")
            root.SetAttributeValue("IgnoreCertProbs", definition.IgnoreCertProblems ? "1" : "0");
        if (definition.HostType == "SSHFTP") root.SetAttributeValue("DefClientKeyID", sshKeyId);
        if (definition.HostType == "siLock" && definition.ScriptPath.Length > 0)
            root.SetAttributeValue("ScriptPath", definition.ScriptPath);
        if (definition.HostType == "SMTP" && definition.FromAddress.Length > 0)
            root.SetAttributeValue("DefAddressFrom", definition.FromAddress);
        if (definition.HostType is "S3" or "GoogleStorage") root.SetAttributeValue("DefBucket", definition.Bucket);
        if (definition.HostType == "AzureBlob") root.SetAttributeValue("DefContainer", definition.Container);
        if (definition.DataIntegrity.Length > 0) root.SetAttributeValue("DefDLInteg", definition.DataIntegrity);
        if (definition.HostType == "SharePoint")
        {
            foreach (var field in definition.SharePointFields ?? new Dictionary<string, string>())
                root.SetAttributeValue(SharePointAttributes[field.Key], field.Value);
        }
        else
        {
            root.SetAttributeValue("DefUsername", definition.Username);
            root.SetAttributeValue("DefPassword", encryptedPassword);
        }
        var desc = root.Elements().FirstOrDefault(x => x.Name.LocalName.Equals("Desc", StringComparison.OrdinalIgnoreCase));
        if (desc is null) root.Add(new XElement("Desc", definition.Description));
        else desc.Value = definition.Description;
        return root.ToString(SaveOptions.DisableFormatting);
    }
}

