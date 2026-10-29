using MoveItAutomationUI.Models;
using System.Globalization;
using System.Text.Json.Nodes;

namespace MoveItAutomationUI.Services.MoveIt.Hosts;

public sealed record HostProfileChoice(string Value, string Label, bool DefaultEnabled = false);
public sealed record HostProfileField(string Key, string Label, string Section, string Kind = "Text",
    bool Editable = true, IReadOnlyList<HostProfileChoice>? Choices = null,
    string ConditionKey = "", string ConditionValue = "", MoveItFeature? RequiredFeature = null, string HelpText = "")
{
    public bool Sensitive => Kind.StartsWith("Secret", StringComparison.Ordinal);
}

// One field/ownership catalog for the supplied v0 Host PUT captures and their editor controls.
// Resource Groups and server counters remain preserve-only; state-cache ownership is shared.
public static class HostCapturedProfiles
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<HostProfileField>> Profiles = Build();
    public static bool TryGet(string type, out IReadOnlyList<HostProfileField> fields)
        => Profiles.TryGetValue(type, out fields!);
    public static bool TryGetSupported(string type, MoveItInfo? info, out IReadOnlyList<HostProfileField> fields)
    {
        if (!TryGet(type, out fields) || type.Equals("OneDrive", StringComparison.OrdinalIgnoreCase) && !MoveItFeatureSupport.Supports(info, MoveItFeature.OneDriveHosts))
        { fields = Array.Empty<HostProfileField>(); return false; }
        fields = fields.Where(field => field.RequiredFeature is null || MoveItFeatureSupport.Supports(info, field.RequiredFeature.Value))
            .Select(field => field.ConditionKey == "AuthMethod" && !MoveItFeatureSupport.Supports(info, MoveItFeature.AmazonS3IamAuthentication)
                ? field with { ConditionKey = "", ConditionValue = "" } : field).ToArray();
        return true;
    }
    public static string ProfileId(string type) => type.ToLowerInvariant() + "-host-v2";

    private static IReadOnlyDictionary<string, IReadOnlyList<HostProfileField>> Build()
    {
        var profiles = new Dictionary<string, IReadOnlyList<HostProfileField>>(StringComparer.OrdinalIgnoreCase);
        // Built-in Host 0 has its own captured contract: no credentials, connection or alternate Host.
        profiles["FileSystem"] = new HostProfileField[]
        {
            new("DefRetryCount", "Retry Count (0 = no retries)", "general", "Number"),
            new("DefRetryTimeoutSecs", "Retry Timeout (seconds)", "general", "Number"),
            new("DefRescanSecs", "Transfer Rescan Time (seconds; 0 = disabled)", "general", "Number"),
            new("UseNotif", "Use File Notifications", "general", "Boolean"),
            new("RenameAfterUpload", "Upload as Temp File then Rename", "uploads", "Boolean"),
            new("TempUploadName", "Temporary Upload Filename Pattern", "uploads"),
            new("DefPreserveFileTimestamp", "Preserve File Timestamp on Upload", "uploads", "Boolean"),
            new("DefMxFi", "File Count Download Limit (0 = unlimited)", "limits", "Number"),
            new("DefMxBy", "File Size Download Limit (bytes; 0 = unlimited)", "limits", "Number"),
            new("DelOldStateDays", "Delete State for Host Path Entries After (days; 0 = never)", "advanced", "Number"),
            new("AdjDST", "Account for Daylight Saving Time", "advanced", "Boolean")
        }.Concat(HostStateCacheSettings.Fields).ToArray();
        foreach (var type in new[] { "POP3", "SMTP", "S3", "AzureBlob", "GoogleStorage", "SSHFTP", "FTP", "SharePoint", "OneDrive" })
        {
            var fields = new List<HostProfileField>();
            void Add(string key, string label, string section, string kind = "Text", bool editable = true,
                IReadOnlyList<HostProfileChoice>? choices = null, string conditionKey = "", string conditionValue = "")
                => fields.Add(new(key, label, section, kind, editable, choices, conditionKey, conditionValue));
            void Number(string key, string label, string section) => Add(key, label, section, "Number");
            void Flag(string key, string label, string section) => Add(key, label, section, "Boolean");
            var mail = type is "POP3" or "SMTP";
            var google = type == "GoogleStorage";
            var oneDrive = type == "OneDrive";
            var cloud = type is "S3" or "AzureBlob" or "GoogleStorage" or "OneDrive";
            var sftp = type == "SSHFTP";
            var ftp = type == "FTP";
            var sharePoint = type == "SharePoint";
            if (mail || sftp || ftp) { Add("Host", "Hostname / IP Address", "general"); Number("Port", "Port", "general"); }
            if (type == "SMTP") Add("DefAddressFrom", "Default From Address", "general");
            if (type == "AzureBlob") Add("DefContainer", "Default Container", "general");
            if (type is "S3" or "GoogleStorage") Add("DefBucket", "Default Bucket", "general");
            if (type == "S3")
            {
                fields.Add(new("AuthMethod", "Authentication", "authentication", "Select", Choices: Choices(("0", "Access Keys"), ("1", "AWS Config File"), ("2", "EC2 Instance Role (IMDS)")), RequiredFeature: MoveItFeature.AmazonS3IamAuthentication));
                fields.Add(new(HostCloudAuthenticationSettings.S3ProfileKey, "Profile Name", "authentication", ConditionKey: "AuthMethod", ConditionValue: "1", RequiredFeature: MoveItFeature.AmazonS3IamAuthentication, HelpText: "Leave blank to use the default profile in MOVEit's configured AWS configuration file."));
                Add("IsNonAmazonEndPoint", "Use Alternative S3 Provider", "general", "Boolean", conditionKey: "AuthMethod", conditionValue: "0");
                // Native S3 accepts scheme-less endpoints; retain the captured text contract.
                Add("S3AlternativeEndpoint", "Alternative Provider URL", "general",
                    conditionKey: "IsNonAmazonEndPoint", conditionValue: "true");
            }
            if (sharePoint)
            {
                Add("TenantName", "Tenant Name", "general");
                Add("SiteUrlPrefix", "Site URL Prefix", "general");
                Add("Site", "Site", "general");
                Add("DefDocumentLibrary", "Default Document Library", "general", "DocumentLibrary");
                Add("DefDocumentLibraryGuid", "Document Library GUID", "general");
                Add("UsingAppAuthentication", "Authentication", "authentication", "Select", choices: Choices(("1", "App Access"), ("2", "Certificate Access")));
                Add("ClientID", "Application / Client ID", "authentication");
                Add("ClientSecret", "Client Secret", "authentication", "Secret", conditionKey: "UsingAppAuthentication", conditionValue: "1");
                Add("RealmID", "Realm ID", "authentication", conditionKey: "UsingAppAuthentication", conditionValue: "1");
                Add("TenantID", "Tenant ID", "authentication", conditionKey: "UsingAppAuthentication", conditionValue: "2");
            }
            if (mail || google) Flag("UseOAuth", "Use OAuth", "authentication");
            if (oneDrive)
            {
                Add(HostCloudAuthenticationSettings.OneDriveModeKey, "Authentication", "authentication", "Select", choices: Choices(("certificate", "Certificate Authentication"), ("client-secret", "Client Secret Authentication")));
                Add("TenantID", "Tenant ID", "authentication");
                Add("DefUsername", "Username", "authentication");
                Add("OAuthClientID", "Client ID", "authentication");
                Add("OAuthClientSecret", "Client Secret", "authentication", "Secret", conditionKey: HostCloudAuthenticationSettings.OneDriveModeKey, conditionValue: "client-secret");
            }
            if (!sharePoint && !oneDrive)
            {
            Add("DefUsername", type == "AzureBlob" ? "Storage Account" : cloud ? "Access Key ID" : "Username", "authentication",
                conditionKey: google ? "UseOAuth" : type == "S3" ? "AuthMethod" : "", conditionValue: google ? "false" : type == "S3" ? "0" : "");
            Add("DefPassword", type == "AzureBlob" ? "Storage Account Key" : cloud ? "Secret Access Key" : "Password", "authentication", "Secret",
                conditionKey: mail || google ? "UseOAuth" : type == "S3" ? "AuthMethod" : "", conditionValue: mail || google ? "false" : type == "S3" ? "0" : "");
            }
            if (mail || google)
            {
                if (mail)
                {
                    Add(HostMailOAuthSettings.ProviderKey, "OAuth Provider", "authentication", "Select",
                        choices: Choices(("microsoft365", "Microsoft Office 365"), ("custom", "Custom")), conditionKey: "UseOAuth", conditionValue: "true");
                    Add(HostMailOAuthSettings.TenantKey, "Tenant ID", "authentication", conditionKey: HostMailOAuthSettings.ProviderKey, conditionValue: "microsoft365");
                }
                if (google) Add("ProjectID", "OAuth Project ID", "authentication", conditionKey: "UseOAuth", conditionValue: "true");
                Add("OAuthClientID", "OAuth Client ID", "authentication", conditionKey: "UseOAuth", conditionValue: "true");
                Add("OAuthClientSecret", google ? "OAuth Private Key" : "OAuth Client Secret", "authentication", google ? "SecretMultiline" : "Secret",
                    conditionKey: "UseOAuth", conditionValue: "true");
                if (mail)
                {
                    Add("OAuthScope", "OAuth Scope", "authentication", conditionKey: "UseOAuth", conditionValue: "true");
                    Add("OAuthTokenURL", "OAuth Token URL", "authentication", conditionKey: "UseOAuth", conditionValue: "true");
                }
            }
            if (sftp)
            {
                Add("DefClientKeyID", "SSH Client Key", "authentication", "KeySelect");
                Add("SSHFingerprint", "SSH Host Key Fingerprint", "authentication", "HostFingerprint");
                Flag("IgnoreCertProbs", "Ignore SSH Host Key Problems", "authentication");
                Add("EncList", "Encryption Algorithms", "ssh-algorithms", "AlgorithmList");
                Add("KexAlgs", "Key Exchange Algorithms", "ssh-algorithms", "AlgorithmList");
            }
            else if (ftp)
            {
                Add("DefAccount", "Account", "authentication");
                Add("Secure", "Secure Connection / TLS Mode", "security", "Select", choices: Choices(("none", "None"), ("on", "Explicit (Ctrl/Data)"), ("on-ccc", "Explicit (Ctrl/Data) with CCC"), ("tls-c", "Explicit (Ctrl Only)"), ("tls-c-ccc", "Explicit (Ctrl Only) with CCC"), ("implicit", "Implicit (Ctrl/Data)"), ("implicit-ccc", "Implicit (Ctrl/Data) with CCC")));
                Flag("IgnoreCertProbs", "Ignore TLS Certificate Errors", "security");
            }
            else
            {
                // Google Secure was captured as a retained native flag; its editor has no TLS switch.
                if (!google && !oneDrive) Flag("Secure", "Secure Connection with TLS", "security");
                if (mail) Flag("IgnoreCertProbs", "Ignore Certificate Problems", "security");
            }
            if (ftp || sharePoint || oneDrive)
            {
                var section = ftp ? "security" : "authentication";
                // FTP client certificates stay configurable in every TLS mode. SharePoint
                // certificates belong specifically to its Certificate Access authentication mode.
                var conditionKey = oneDrive ? HostCloudAuthenticationSettings.OneDriveModeKey : sharePoint ? "UsingAppAuthentication" : "";
                var conditionValue = oneDrive ? "certificate" : sharePoint ? "2" : "";
                Add("DefClientCertSHA1", "Client Certificate", section, "Certificate", conditionKey: conditionKey, conditionValue: conditionValue);
                Add("DefClientCertStore", "Certificate Store", section, "CertificatePart", conditionKey: conditionKey, conditionValue: conditionValue);
                Add("DefClientCertSubject", "Certificate Subject", section, "CertificatePart", conditionKey: conditionKey, conditionValue: conditionValue);
            }
            if (cloud || sftp || ftp || sharePoint)
            {
                var proxyTypes = ftp ? Choices(("None", "None"), ("SOCKS5", "SOCKS5")) : sftp ? Choices(("None", "None"), ("SOCKS4", "SOCKS4"), ("SOCKS5", "SOCKS5"), ("WEBStandard", "Web Standard"))
                    : Choices(("None", "None"), ("HTTP", "HTTP"), ("HTTPS", "HTTPS"));
                Add("ProxyType", "Proxy Type", "proxy", "Select", choices: proxyTypes);
                Add("ProxyHost", "Proxy Hostname / IP Address", "proxy", conditionKey: "ProxyType", conditionValue: "active");
                Add("ProxyPort", "Proxy Port", "proxy", "Number", conditionKey: "ProxyType", conditionValue: "active");
                Add("ProxyUsername", "Proxy Username", "proxy", conditionKey: "ProxyType", conditionValue: "active");
                Add("ProxyPassword", "Proxy Password", "proxy", "Secret", conditionKey: "ProxyType", conditionValue: "active");
                if (sharePoint) Add("ProxyIgnoreCertErrors", "Ignore Proxy Certificate Errors", "proxy", "Boolean", conditionKey: "ProxyType", conditionValue: "HTTPS");
                Number("DefMxFi", "File Count Download Limit (0 = unlimited)", "limits");
                Number("DefMxBy", "File Size Download Limit (bytes; 0 = unlimited)", "limits");
                Number("DefRescanSecs", "Transfer Rescan Time (seconds; 0 = disabled)", "timeouts");
            }
            Number("DefConnTimeoutSecs", "Connect Timeout (seconds)", "timeouts");
            if (!sftp) Number("DefDataTimeoutSecs", "Data Timeout (seconds)", "timeouts");
            Number("DefRetryCount", "Retry Count (0 = no retries)", "timeouts");
            Number("DefRetryTimeoutSecs", "Retry Timeout (seconds)", "timeouts");
            if (type is "S3" or "AzureBlob")
                Add("DefDLInteg", "Data Integrity Checking", "data-integrity", "Select", choices: type == "S3"
                    ? Choices(("never", "Never"), ("ifsure", "If Supported"), ("ignore", "Always, Ignore Failures"), ("always", "Always"))
                    : Choices(("never", "Never"), ("ignore", "Always, Ignore Failures"), ("always", "Always")));
            if (mail || sftp || ftp) Add("ID2", "Alternate Host", "advanced", "HostSelect");
            if (type != "SMTP")
            {
                Number("DelOldStateDays", "Delete State After (days; 0 = never)", "advanced");
                fields.AddRange(HostStateCacheSettings.Fields);
            }
            if (cloud || sftp || ftp || sharePoint) Flag("AdjDST", "Account for Daylight Saving Time", "advanced");
            if (sftp)
            {
                Flag("RenameAfterUpload", "Upload as Temp File then Rename", "uploads");
                Add("TempUploadName", "Temporary Upload Filename Pattern", "uploads");
                Flag("DefSetFileAttr", "Set File Permissions After Upload", "uploads");
                Add("DefDestFileAttr", "Destination File Permissions (octal)", "uploads");
                Flag("DefPreserveFileTimestamp", "Preserve File Timestamp on Upload", "uploads");
                Flag("DefBlindDL", "Blind Downloads", "transfers");
                Flag("DefRsm", "Resume Transfers", "transfers");
                Add("CmpL", "Disable Compression", "transfers", "Select", choices: Choices(("0", "Yes"), ("6", "No")));
                Add("DefMD5Get", "MD5 Download Verification", "data-integrity", "Select", choices: Choices(("Never", "Never"), ("IfPresent", "If Present"), ("Always", "Always")));
                Add("DefMD5File", "MD5 Filename", "data-integrity");
                Flag("OptimizeSSHBuf", "Optimize SSH Buffer", "advanced");
            }
            if (ftp)
            {
                Flag("RenameAfterUpload", "Upload as Temp File then Rename", "uploads");
                Add("TempUploadName", "Temporary Upload Filename Pattern", "uploads");
                Add("DefAB", "Transfer Type", "transfers", "Select", choices: Choices(("binary", "Binary"), ("ascii", "ASCII")));
                foreach (var entry in new[] { ("DefPassive", "Passive Transfer Mode"), ("DefBlindDL", "Blind Downloads"), ("DefBlindUL", "Blind Uploads"), ("DefRsm", "Resume Partial Transfers"), ("DefReuseSSL", "Reuse TLS Session for Data Connections"), ("DefXS", "Use XSHA1 Command, if Available") }) Flag(entry.Item1, entry.Item2, "transfers");
                Add("DefMD5Get", "Use MD5", "data-integrity", "Select", choices: Choices(("Never", "Never"), ("IfPresent", "If Present"), ("Always", "Always")));
                Add("DefMD5File", "MD5 Filename", "data-integrity");
                Add("AddComSignon", "Commands Upon Sign-on", "additional-commands", "Multiline");
                Add("AddCom", "Commands Before Transfer", "additional-commands", "Multiline");
                Add("AddComAX", "Commands After Transfer", "additional-commands", "Multiline");
                Flag("NATUseServerIPForData", "Ignore PASV IP in Passive Mode", "advanced");
                Add("NATClientIP", "NAT Client IP", "advanced");
                Add(HostFtpDirectoryParsing.ModeKey, "Directory List Parsing", "directory-parsing", "DirectoryParsing",
                    choices: Choices(("automatic", "Use built-in automatic directory list parsing"), ("custom", "Use custom directory list parsing parameters"), ("script", "Use directory parsing script")));
                foreach (var entry in new[] { ("CustomDirSkipTop", "Top"), ("CustomDirSkipBottom", "Bottom"), ("CustomDirColFilename", "File name"), ("CustomDirColDate", "Date") })
                    Add(entry.Item1, entry.Item2, "directory-parsing", "Number", conditionKey: HostFtpDirectoryParsing.ModeKey, conditionValue: "custom");
                Add("DirScriptID", "Directory Parsing Script", "directory-parsing", "ScriptSelect", conditionKey: HostFtpDirectoryParsing.ModeKey, conditionValue: "script");
            }
            profiles[type] = fields;
        }
        return profiles;
    }

    private static IReadOnlyList<HostProfileChoice> Choices(params (string Value, string Label)[] values)
        => values.Select(x => new HostProfileChoice(x.Value, x.Label)).ToArray();

    public static bool IsActive(HostProfileField field, JsonObject host)
    {
        if (field.Key == "S3AlternativeEndpoint" && HostCloudAuthenticationSettings.S3Method(host) != 0) return false;
        if (field.ConditionKey.Length == 0) return true;
        var value = field.ConditionKey == HostFtpDirectoryParsing.ModeKey ? HostFtpDirectoryParsing.Mode(host)
            : HostCloudAuthenticationSettings.Value(host, field.ConditionKey);
        if (field.ConditionValue == "active") return value.Length > 0 && !value.Equals("None", StringComparison.OrdinalIgnoreCase);
        if (field.ConditionValue is not ("true" or "false")) return value.Equals(field.ConditionValue, StringComparison.OrdinalIgnoreCase);
        var enabled = value.Equals("1", StringComparison.OrdinalIgnoreCase) || value.Equals("true", StringComparison.OrdinalIgnoreCase);
        return enabled == (field.ConditionValue == "true");
    }

    public static void Apply(JsonObject host, JsonObject configuration, string type)
    {
        if (!TryGet(type, out var fields)) throw new InvalidOperationException("This Host profile is not supported.");
        var definitions = fields.ToDictionary(x => x.Key, StringComparer.Ordinal);
        foreach (var pair in configuration)
            if (!definitions.TryGetValue(pair.Key, out var field) || !field.Editable)
                throw new InvalidOperationException("The requested setting is not writable for this Host type.");
        // Mode and proxy type must be applied first so credential guardrails use the effective mode.
        HostCloudAuthenticationSettings.ApplyModes(host, configuration, type);
        HostStateCacheSettings.Apply(host, configuration);
        if (type == "FTP") HostFtpDirectoryParsing.Apply(host, configuration);
        foreach (var pair in configuration.Where(pair => !HostStateCacheSettings.IsField(pair.Key) && !HostCloudAuthenticationSettings.IsCommand(pair.Key)
            && !(type is "POP3" or "SMTP" && HostMailOAuthSettings.IsField(pair.Key))
            && !(type == "FTP" && HostFtpDirectoryParsing.IsField(pair.Key))).OrderBy(x => x.Key is "UseOAuth" or "ProxyType" or "UsingAppAuthentication" or "Secure" or "IsNonAmazonEndPoint" ? 0 : 1))
        {
            var field = definitions[pair.Key];
            var value = HostDefinitionJson.ReadNode(pair.Value);
            if (field.Sensitive)
            {
                if (pair.Value is null || value.Length == 0) continue; // Null/blank explicitly preserves a secret.
                if (pair.Value is not JsonValue json || !json.TryGetValue<string>(out _))
                    throw new InvalidOperationException("A replacement secret must be text.");
                if (!IsActive(field, host)) throw new InvalidOperationException("The replacement credential is not used by the selected authentication/proxy mode.");
                Set(host, pair.Key, JsonValue.Create(value)); // The captured REST contract expects plaintext; MOVEit encrypts persistence.
            }
            else if (!IsActive(field, host))
                throw new InvalidOperationException("The setting does not apply to the selected authentication/proxy mode.");
            else if (field.Kind == "Boolean")
            {
                var text = value.Trim();
                if (text is "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase)) Set(host, pair.Key, JsonValue.Create(1));
                else if (text is "0" || text.Equals("false", StringComparison.OrdinalIgnoreCase)) Set(host, pair.Key, JsonValue.Create(0));
                else throw new InvalidOperationException("Boolean Host fields must be Yes or No.");
            }
            else if (field.Kind == "Number")
            {
                if (!long.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 0
                    || ((pair.Key is "Port" or "ProxyPort") && (number < 1 || number > 65535)))
                    throw new InvalidOperationException("A numeric Host setting is outside its permitted range.");
                Set(host, pair.Key, JsonValue.Create(number));
            }
            else
            {
                var text = value.Trim();
                if (field.Kind == "AlgorithmList")
                {
                    var algorithms = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (algorithms.Any(item => item.Any(char.IsWhiteSpace))) throw new InvalidOperationException("SSH algorithms must be comma-separated names without whitespace.");
                    text = string.Join(",", algorithms.Distinct(StringComparer.Ordinal));
                }
                if (field.Choices is not null && field.Kind != "AlgorithmList")
                {
                    var choice = field.Choices.FirstOrDefault(x => x.Value.Equals(text, StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException("The selected Host setting is not supported by this profile.");
                    text = choice.Value;
                }
                if (!IsActive(field, host) && field.ConditionKey.Length > 0)
                    throw new InvalidOperationException("The setting does not apply to the selected authentication/proxy mode.");
                Set(host, pair.Key, pair.Key is "CmpL" or "UsingAppAuthentication" or "AuthMethod" ? JsonValue.Create(int.Parse(text, CultureInfo.InvariantCulture)) : JsonValue.Create(text));
            }
        }
        if (type is "POP3" or "SMTP") HostMailOAuthSettings.Apply(host, configuration);
    }

    private static void Set(JsonObject host, string name, JsonNode? value)
    {
        var keys = host.Select(pair => pair.Key).Where(key => key.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        var actualKey = keys.FirstOrDefault() ?? name;
        foreach (var duplicate in keys.Skip(1)) host.Remove(duplicate);
        host[actualKey] = value;
    }

    public static void PreserveUnsubmittedSecrets(JsonObject payload, JsonObject configuration, string type)
    {
        _ = TryGet(type, out var fields);
        foreach (var field in fields.Where(x => x.Sensitive))
        {
            if ((type == "OneDrive" || type == "S3") && (field.Key is "DefPassword" or "OAuthClientSecret") && !IsActive(field, payload))
            {
                foreach (var key in payload.Select(pair => pair.Key).Where(key => key.Equals(field.Key, StringComparison.OrdinalIgnoreCase)).ToArray()) payload.Remove(key);
                continue;
            }
            if (!configuration.TryGetPropertyValue(field.Key, out var value) || value is null || HostDefinitionJson.ReadNode(value).Length == 0)
                Set(payload, field.Key, null);
        }
    }
}
