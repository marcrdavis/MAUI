// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Isolated MOVEit Host read contract used by the MAUI Host Editor.

using System.Globalization;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Features.Editors.Hosts;

public interface IHostEditorService
{
    Task<HostEditorHost> GetHostAsync(string hostId, CancellationToken cancellationToken = default);
}

public sealed class HostEditorService : IHostEditorService
{
    private readonly IAutomationHostService _hostService;
    private readonly IHostConnectionTestService _hostConnectionTestService;
    private readonly IAutomationScriptService _scripts;
    private readonly IMoveItObjectAccessService? _access;
    private readonly IMoveItSessionService? _sessions;

    private sealed record FieldDefinition(string Key, string Label, string Type = "Text", bool Sensitive = false, bool Editable = false,
        IReadOnlyList<HostEditorFieldOption>? Options = null, string ConditionKey = "", string ConditionValue = "");

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> SectionProfiles =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["siLock"] = new[] { "general", "authentication", "security", "limits", "timeouts", "advanced" },
            // UNC follows MOVEit's native layout. Preserve File Timestamp belongs in Advanced for this profile.
            ["Share"] = new[] { "general", "authentication", "limits", "timeouts", "advanced" }
        };

    private static readonly IReadOnlyDictionary<string, string> SectionLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["general"] = "General",
            ["authentication"] = "Authentication",
            ["proxy"] = "Proxy",
            ["security"] = "Security",
            ["uploads"] = "Uploads",
            ["limits"] = "Limits",
            ["timeouts"] = "Timeouts",
            ["transfers"] = "Transfers",
            ["data-integrity"] = "Data Integrity",
            ["ssh-algorithms"] = "SSH Algorithms",
            ["additional-commands"] = "Additional Commands",
            ["directory-parsing"] = "Directory Parsing",
            ["advanced"] = "Advanced"
        };

    // Generic read-only catalog for Host profiles whose write contract is not yet enabled.
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<FieldDefinition>> FieldsBySection =
        new Dictionary<string, IReadOnlyList<FieldDefinition>>(StringComparer.OrdinalIgnoreCase)
        {
            ["general"] = new FieldDefinition[]
            {
                new("Host", "Host or IP Address"), new("Port", "Port", "Number"), new("UNC", "Resource UNC"),
                new("DriveLetter", "Drive Letter"), new("DefBucket", "Default Bucket"), new("DefContainer", "Default Container"),
                new("Site", "Site"), new("SiteUrlPrefix", "Site URL Prefix"), new("DefDocumentLibrary", "Default Document Library"),
                new("ScriptPath", "Default Path"), new("Secure", "Secure Connection / TLS Mode"), new("UseNotif", "Use File Notifications", "Boolean"),
                new("DefAddressFrom", "Default From Address")
            },
            ["authentication"] = new FieldDefinition[]
            {
                new("DefUsername", "Username / Account ID"), new("DefPassword", "Password / Secret", "Secret", true),
                new("UseOAuth", "Use OAuth", "Boolean"), new("OAuthClientID", "OAuth Client ID"), new("OAuthScope", "OAuth Scope"),
                new("OAuthTokenURL", "OAuth Token URL"), new("TenantID", "Tenant ID"), new("TenantName", "Tenant Name"),
                new("ClientID", "Application / Client ID"), new("UsingAppAuthentication", "Application Authentication Mode")
            },
            ["proxy"] = new FieldDefinition[] { new("ProxyType", "Proxy Type") },
            ["security"] = new FieldDefinition[]
            {
                new("IgnoreCertProbs", "Ignore Certificate Problems", "Boolean"), new("DefClientCertSubject", "Client Certificate Subject"),
                new("DefClientCertStore", "Client Certificate Store"), new("DefClientCertSHA1", "Client Certificate Thumbprint"),
                new("SSHFingerprint", "SSH Fingerprint")
            },
            ["uploads"] = new FieldDefinition[]
            {
                new("DefPreserveFileTimestamp", "Preserve File Timestamp on Upload", "Boolean"), new("DefBlindUL", "Blind Upload", "Boolean"),
                new("RenameAfterUpload", "Rename After Upload", "Boolean"), new("TempUploadName", "Temporary Upload Name"),
                new("DefSetFileAttr", "Set File Attributes", "Boolean"), new("DefDestFileAttr", "Destination File Attributes")
            },
            ["limits"] = new FieldDefinition[] { new("DefMxFi", "File Count Download Limit", "Number"), new("DefMxBy", "File Size Download Limit", "Number") },
            ["timeouts"] = new FieldDefinition[]
            {
                new("DefConnTimeoutSecs", "Connect Timeout (seconds)", "Number"), new("DefDataTimeoutSecs", "Data Timeout (seconds)", "Number"),
                new("DefRetryCount", "Retry Count", "Number"), new("DefRetryTimeoutSecs", "Retry Timeout (seconds)", "Number"),
                new("DefRescanSecs", "Transfer Rescan Time (seconds)", "Number")
            },
            ["transfers"] = new FieldDefinition[]
            {
                new("DefAB", "Transfer Type"), new("DefPassive", "Passive Mode", "Boolean"), new("DefBlindDL", "Blind Download", "Boolean"),
                new("DefRsm", "Resume Transfers", "Boolean"), new("DefReuseSSL", "Reuse TLS Session", "Boolean"), new("DefXS", "XSHA1", "Boolean"),
                new("NATUseServerIPForData", "Use Server IP for Data Connection", "Boolean"), new("NATClientIP", "NAT Client IP")
            },
            ["data-integrity"] = new FieldDefinition[]
            {
                new("DefDLInteg", "Data Integrity Checking"), new("DefMD5Get", "MD5 Download Verification"), new("DefMD5File", "MD5 Filename")
            },
            ["ssh-algorithms"] = new FieldDefinition[]
            {
                new("EncList", "Encryption Algorithms", "Multiline"), new("KexAlgs", "Key Exchange Algorithms", "Multiline"),
                new("OptimizeSSHBuf", "Optimize SSH Buffer", "Boolean")
            },
            ["additional-commands"] = new FieldDefinition[]
            {
                new("AddCom", "Additional Commands"), new("AddComAX", "Additional Commands After Transfer"), new("AddComSignon", "Sign-on Commands")
            },
            ["advanced"] = new FieldDefinition[]
            {
                new("DelOldStateDays", "Delete State After (days)", "Number"),
                new("AdjDST", "Account for Daylight Saving Time", "Boolean"), new("DefWinCopyFileAPI", "Use Windows CopyFile API", "Boolean"),
                new("DefSort", "Default Sort"), new("DotNET", ".NET Mode", "Boolean")
            }
        };

    // Captured UNC Share profile, including native credential omission for Run-As authentication.
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<FieldDefinition>> ShareFieldsBySection =
        new Dictionary<string, IReadOnlyList<FieldDefinition>>(StringComparer.OrdinalIgnoreCase)
        {
            ["general"] = new FieldDefinition[]
            {
                new("UNC", "Resource UNC", "Text", false, true),
                new("DriveLetter", "Drive Letter", "Select", false, true,
                    new[] { new HostEditorFieldOption("", "None") }.Concat(Enumerable.Range('D', 'Z' - 'D' + 1)
                        .Select(letter => new HostEditorFieldOption($"{(char)letter}:", $"{(char)letter}:"))).ToArray()),
                new("UseNotif", "Use File Notifications", "Boolean", false, true)
            },
            ["authentication"] = new FieldDefinition[]
            {
                new(HostAuthenticationSettings.UncModeKey, "Authentication", "Select", false, true, new HostEditorFieldOption[]
                {
                    new("run-as", "Use MOVEit Automation Run-As credentials"), new("credentials", "Use these credentials")
                }),
                new("DefUsername", "Username", "Text", false, true, ConditionKey: HostAuthenticationSettings.UncModeKey, ConditionValue: "credentials"),
                new("DefPassword", "Password", "Secret", true, true, ConditionKey: HostAuthenticationSettings.UncModeKey, ConditionValue: "credentials")
            },
            ["limits"] = new FieldDefinition[]
            {
                new("DefMxFi", "File Count Download Limit (0 = unlimited)", "Number", false, true),
                new("DefMxBy", "File Size Download Limit (bytes; 0 = unlimited)", "Number", false, true)
            },
            ["timeouts"] = new FieldDefinition[]
            {
                new("DefRescanSecs", "Transfer Rescan Time (seconds; 0 = disabled)", "Number", false, true),
                new("DefRetryCount", "Retry Count (0 = no retries)", "Number", false, true),
                new("DefRetryTimeoutSecs", "Retry Timeout (seconds)", "Number", false, true)
            },
            ["advanced"] = new FieldDefinition[]
            {
                new("DefWinCopyFileAPI", "Use Windows CopyFile API", "Boolean", false, true),
                new("ID2", "Alternate Host", "HostSelect", false, true),
                new("DelOldStateDays", "Delete State for Host Path Entries After (days; 0 = never)", "Number", false, true),
                new("AdjDST", "Account for Daylight Saving Time", "Boolean", false, true),
                new("DefPreserveFileTimestamp", "Preserve File Timestamp on Upload", "Boolean", false, true)
            }
        };

    private static readonly IReadOnlyList<HostEditorFieldOption> MoveItSortOptions = new HostEditorFieldOption[]
    {
        new("Filename,A", "Filename — Ascending"),
        new("Filename,D", "Filename — Descending"),
        new("Date,A", "Date — Oldest First"),
        new("Date,D", "Date — Newest First"),
        new("Size,A", "Size — Smallest First"),
        new("Size,D", "Size — Largest First")
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<FieldDefinition>> MoveItTransferFieldsBySection =
        new Dictionary<string, IReadOnlyList<FieldDefinition>>(StringComparer.OrdinalIgnoreCase)
        {
            ["general"] = new FieldDefinition[]
            {
                new("Host", "Hostname / IP Address", "Text", false, true),
                new("Port", "Port", "Number", false, true),
                new("ScriptPath", "IIS Virtual Directory", "Text", false, true),
                new("DefSort", "Sort Files By", "Select", false, true, MoveItSortOptions),
                new("UseNotif", "Use File Notifications", "Boolean", false, true)
            },
            ["authentication"] = new FieldDefinition[]
            {
                new("DefUsername", "Username", "Text", false, true),
                new("DefPassword", "Password", "Secret", true, true)
            },
            ["security"] = new FieldDefinition[]
            {
                new("Secure", "Secure Connection with TLS", "Boolean", false, true),
                new("IgnoreCertProbs", "Ignore TLS Certificate Errors", "Boolean", false, true),
                new("DefClientCertSHA1", "Client Certificate", "Certificate", false, true),
                new("DefClientCertStore", "Certificate Store", "CertificatePart", false, true),
                new("DefClientCertSubject", "Certificate Subject", "CertificatePart", false, true)
            },
            ["limits"] = new FieldDefinition[]
            {
                new("DefMxFi", "File Count Download Limit (0 = unlimited)", "Number", false, true),
                new("DefMxBy", "File Size Download Limit (bytes; 0 = unlimited)", "Number", false, true)
            },
            ["timeouts"] = new FieldDefinition[]
            {
                new("DefConnTimeoutSecs", "Connect Timeout (seconds)", "Number", false, true),
                new("DefDataTimeoutSecs", "Data Timeout (seconds)", "Number", false, true),
                new("DefRetryCount", "Retry Count (0 = no retries)", "Number", false, true),
                new("DefRetryTimeoutSecs", "Retry Timeout (seconds)", "Number", false, true)
            },
            ["advanced"] = new FieldDefinition[]
            {
                new("ID2", "Alternate Host", "HostSelect", false, true),
                new("DelOldStateDays", "Delete State for Host Path Entries After (days; 0 = never)", "Number", false, true),
                new("DotNET", "MOVEit Transfer .NET/API Mode", "Boolean", false, false),
            }
        };

    public HostEditorService(IAutomationHostService hostService, IHostConnectionTestService hostConnectionTestService, IAutomationScriptService scripts, IMoveItObjectAccessService? access = null, IMoveItSessionService? sessions = null)
    {
        _hostService = hostService;
        _hostConnectionTestService = hostConnectionTestService;
        _scripts = scripts;
        _access = access;
        _sessions = sessions;
    }

    public async Task<HostEditorHost> GetHostAsync(string hostId, CancellationToken cancellationToken = default)
    {
        var normalizedHostId = (hostId ?? string.Empty).Trim();
        if (normalizedHostId.Length == 0) throw new ArgumentException("Host ID is required.", nameof(hostId));

        var commonTask = _hostService.GetHostAsync(normalizedHostId, cancellationToken);
        var rawTask = _hostService.GetHostDefinitionAsync(normalizedHostId, cancellationToken);
        await Task.WhenAll(commonTask, rawTask);

        var common = await commonTask;
        var (typeCode, raw) = UnwrapHost(await rawTask);
        if (string.IsNullOrWhiteSpace(typeCode)) typeCode = common.TypeCode;

        // Inventory flags can be absent or stale; never enter native snapshot reads
        // until effective edit access is confirmed for the authenticated user.
        var access = _access is null ? null : await _access.GetObjectAsync("Host", normalizedHostId, cancellationToken);
        var canEditMoveItFields = access?.CanEdit
            ?? ObjectCheckout.CanEditCheckedOut("Host", normalizedHostId, common.CanEditDelete);
        var hasWritableProfile = HostCapturedProfiles.TryGetSupported(typeCode, _sessions?.Get()?.Info, out _)
            || typeCode.Equals("Share", StringComparison.OrdinalIgnoreCase)
            || typeCode.Equals("siLock", StringComparison.OrdinalIgnoreCase);
        var canEditConfiguration = canEditMoveItFields && hasWritableProfile;
        var native = hasWritableProfile && canEditMoveItFields
            ? await _hostService.GetHostConfigurationSnapshotAsync(normalizedHostId, cancellationToken) : null;
        var writeProfile = !canEditConfiguration ? string.Empty
            : HostCapturedProfiles.TryGet(typeCode, out _) ? HostCapturedProfiles.ProfileId(typeCode)
            : typeCode.Equals("Share", StringComparison.OrdinalIgnoreCase) ? "unc-share-v2" : "moveit-transfer-v2";
        var readOnlyReason = canEditConfiguration
            ? string.Empty
            : hasWritableProfile && !canEditMoveItFields
                ? "This Host is read-only for your MOVEit account."
                : "This Host type is read-only in the current MAUI Host Editor.";

        IReadOnlyList<HostEditorFieldOption> alternateHostOptions = Array.Empty<HostEditorFieldOption>();
        if (typeCode.Equals("Share", StringComparison.OrdinalIgnoreCase)
            || typeCode.Equals("siLock", StringComparison.OrdinalIgnoreCase)
            || typeCode.Equals("FTP", StringComparison.OrdinalIgnoreCase)
            || typeCode.Equals("SSHFTP", StringComparison.OrdinalIgnoreCase)
            || typeCode.Equals("POP3", StringComparison.OrdinalIgnoreCase)
            || typeCode.Equals("SMTP", StringComparison.OrdinalIgnoreCase))
        {
            var hosts = await _hostService.GetHostsAsync(cancellationToken);
            var alternateType = typeCode;
            alternateHostOptions = new[] { new HostEditorFieldOption(string.Empty, "None") }
                .Concat(hosts
                    .Where(item => item.TypeCode.Equals(alternateType, StringComparison.OrdinalIgnoreCase)
                        && !item.Id.Equals(normalizedHostId, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(item => new HostEditorFieldOption(item.Id, item.Name)))
                .ToList();
        }

        IReadOnlyList<HostEditorFieldOption> clientKeys = Array.Empty<HostEditorFieldOption>();
        if (typeCode.Equals("SSHFTP", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                clientKeys = new[] { new HostEditorFieldOption("0", "None") }
                    .Concat((await _hostService.GetSshClientKeysAsync(cancellationToken)).Select(key => new HostEditorFieldOption(key.Id, key.Name))).ToArray();
            }
            catch (MoveItApiException)
            {
                // A restricted key inventory must not prevent editing unrelated Host properties.
                clientKeys = new[] { new HostEditorFieldOption("0", "None") };
            }
        }
        HostSshAlgorithmCatalog? algorithms = null;
        if (typeCode.Equals("SSHFTP", StringComparison.OrdinalIgnoreCase))
        {
            try { algorithms = await _hostService.GetSshAlgorithmsAsync(cancellationToken); }
            catch (MoveItAuthenticationException) { throw; }
            catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or HttpRequestException)
            {
                // Unavailable discovery must not fabricate choices or block unrelated edits.
            }
        }
        IReadOnlyList<HostEditorFieldOption>? parsingScripts = null;
        if (typeCode.Equals("FTP", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                parsingScripts = new[] { new HostEditorFieldOption("", "Select parsing Script…") }
                    .Concat((await _scripts.GetScriptsAsync(cancellationToken)).OrderBy(script => script.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(script => new HostEditorFieldOption(script.Id, script.Name))).ToArray();
            }
            catch (MoveItAuthenticationException) { throw; }
            catch (Exception ex) when (ex is MoveItApiException or HttpRequestException)
            {
                // Retain an existing reference and allow unrelated Host edits if inventory is unavailable.
            }
        }
        var captured = HostCapturedProfiles.TryGetSupported(typeCode, _sessions?.Get()?.Info, out var capturedFields);
        var sectionKeys = captured
            ? new[] { "general", "authentication", "proxy", "security", "uploads", "limits", "timeouts", "transfers", "data-integrity", "ssh-algorithms", "additional-commands", "directory-parsing", "advanced" }
                .Where(section => capturedFields.Any(field => field.Section == section)).ToArray()
            : SectionProfiles.TryGetValue(typeCode, out var configuredSections)
            ? configuredSections
            : new[] { "general", "authentication", "timeouts", "advanced" };
        var sections = sectionKeys
            .Select(sectionKey => new HostEditorSection(
                sectionKey,
                SectionLabels.TryGetValue(sectionKey, out var label) ? label : sectionKey,
                captured ? BuildCapturedFields(raw, capturedFields, sectionKey, canEditConfiguration, alternateHostOptions, clientKeys, algorithms, parsingScripts)
                    : BuildFields(raw, typeCode, sectionKey, canEditConfiguration, alternateHostOptions)))
            .ToList();

        return new HostEditorHost(
            common.Id,
            GetNode(raw, "Name") is null ? common.Name : ReadNode(GetNode(raw, "Name")),
            GetNode(raw, "Desc") is null ? common.Description : ReadNode(GetNode(raw, "Desc")),
            common.Type,
            typeCode,
            common.Destination,
            common.Secure,
            common.AssignedCount,
            access?.CanDelete ?? ObjectCheckout.CanEditCheckedOut("Host", normalizedHostId, common.CanEditDelete),
            canEditMoveItFields,
            canEditConfiguration,
            writeProfile,
            readOnlyReason,
            _hostConnectionTestService.SupportsHostType(typeCode),
            sections,
            common.AssignedTasks.Select(task => new HostEditorTaskReference(task.Id, task.Name)).ToList(),
            ObjectEditVersion.Create("Host", normalizedHostId, native is not null ? ObjectEditVersion.Definition(raw, native.Xml)
                : ObjectEditVersion.Named(ReadNode(GetNode(raw, "Name")), ReadNode(GetNode(raw, "Desc")))),
            GetNode(raw, "Group") is null ? common.Group : ResourceGroupLookupService.FormatAssignments(GetNode(raw, "Group")));
    }

    private static IReadOnlyList<HostEditorField> BuildCapturedFields(JsonObject raw,
        IReadOnlyList<HostProfileField> fields, string section, bool canEdit,
        IReadOnlyList<HostEditorFieldOption> alternateHosts, IReadOnlyList<HostEditorFieldOption> clientKeys,
        HostSshAlgorithmCatalog? algorithms = null, IReadOnlyList<HostEditorFieldOption>? parsingScripts = null)
        => fields.Where(field => field.Section == section).Select(field =>
        {
            var node = GetNode(raw, field.Key);
            var value = field.Sensitive ? "Stored value is suppressed" : FormatValue(node, field.Kind);
            if (field.Key == HostMailOAuthSettings.ProviderKey) value = HostMailOAuthSettings.Provider(raw);
            if (field.Key == HostMailOAuthSettings.TenantKey) value = HostMailOAuthSettings.Tenant(raw);
            if (field.Key == HostFtpDirectoryParsing.ModeKey) value = HostFtpDirectoryParsing.Mode(raw);
            if (HostFtpDirectoryParsing.Columns.Contains(field.Key) && node is null) value = "0";
            if (field.Key == "UseDefStateCaching" && node is null) value = "Yes";
            IReadOnlyList<HostEditorFieldOption>? choices = field.Choices?.Select(choice => new HostEditorFieldOption(choice.Value, choice.Label, choice.DefaultEnabled)).ToArray();
            var editable = canEdit && field.Editable;
            if (field.Key is HostCloudAuthenticationSettings.OneDriveModeKey or HostCloudAuthenticationSettings.S3ProfileKey or "AuthMethod") value = HostCloudAuthenticationSettings.Value(raw, field.Key);
            if (field.Key == "DefUsername" && field.ConditionKey == "AuthMethod" && HostCloudAuthenticationSettings.S3Method(raw) != 0) value = "";
            var help = field.HelpText;
            if (field.Kind == "AlgorithmList")
            {
                var algorithmChoices = field.Key == "EncList" ? algorithms?.Encryption : algorithms?.KeyExchange;
                choices = algorithmChoices?.Select(choice => new HostEditorFieldOption(choice.Value, choice.Label, choice.DefaultEnabled)).ToArray();
                editable &= algorithms is not null;
                if (algorithms is null) help = "Unable to retrieve supported SSH algorithms from MOVEit. Saved values are preserved. Reload the Host to try again.";
            }
            if (field.Kind is "HostSelect" or "KeySelect" or "ScriptSelect")
            {
                var current = ReadNode(node).Trim();
                var options = (field.Kind == "ScriptSelect" ? parsingScripts ?? new[] { new HostEditorFieldOption("", "Select parsing Script…") }
                    : field.Kind == "KeySelect" ? clientKeys : alternateHosts).ToList();
                if (current.Length > 0 && !options.Any(option => option.Value == current))
                    options.Add(new HostEditorFieldOption(current, $"{(field.Kind == "ScriptSelect" ? "Script" : field.Kind == "KeySelect" ? "Key" : "Host")} {current} (current reference)"));
                choices = options;
                if (field.Kind == "KeySelect" && value.Length == 0) value = "0";
                if (field.Kind == "ScriptSelect" && parsingScripts is null)
                {
                    editable = false;
                    help = "Unable to load Scripts from Resources. The current reference is preserved. Reload the Host to try again.";
                }
            }
            return new HostEditorField(field.Key, field.Label, value, field.Kind, field.Sensitive,
                editable, choices, field.ConditionKey, field.ConditionValue, help);
        }).ToArray();

    private static IReadOnlyList<HostEditorField> BuildFields(
        JsonObject raw,
        string typeCode,
        string sectionKey,
        bool canEditConfiguration,
        IReadOnlyList<HostEditorFieldOption> alternateHostOptions)
    {
        var catalog = typeCode.Equals("Share", StringComparison.OrdinalIgnoreCase) ? ShareFieldsBySection
            : typeCode.Equals("siLock", StringComparison.OrdinalIgnoreCase) ? MoveItTransferFieldsBySection
            : FieldsBySection;
        if (!catalog.TryGetValue(sectionKey, out var definitions)) return Array.Empty<HostEditorField>();

        var result = definitions
            .Select(definition =>
            {
                var node = GetNode(raw, definition.Key);
                var hasValue = node is not null && ReadNode(node).Length > 0;
                var value = definition.Sensitive
                    ? (hasValue ? "Configured (suppressed)" : "Stored value is not exposed by MOVEit")
                    : FormatValue(node, definition.Type);
                if (definition.Key == HostAuthenticationSettings.UncModeKey) value = HostAuthenticationSettings.UncMode(raw);
                IReadOnlyList<HostEditorFieldOption>? options = definition.Options;
                if (definition.Type.Equals("HostSelect", StringComparison.OrdinalIgnoreCase))
                {
                    var currentId = ReadNode(node).Trim();
                    var choices = alternateHostOptions.ToList();
                    if (currentId.Length > 0 && !choices.Any(option => option.Value.Equals(currentId, StringComparison.OrdinalIgnoreCase)))
                        choices.Add(new HostEditorFieldOption(currentId, $"Host {currentId} (current reference)"));
                    options = choices;
                }
                var editable = canEditConfiguration && definition.Editable;
                return new { definition, node, value, options, editable };
            })
            .Where(item => typeCode.Equals("Share", StringComparison.OrdinalIgnoreCase)
                || typeCode.Equals("siLock", StringComparison.OrdinalIgnoreCase)
                || item.node is not null
                || item.definition.Sensitive
                || item.definition.Key.Equals("ID2", StringComparison.OrdinalIgnoreCase))
            .Select(item => new HostEditorField(
                item.definition.Key,
                item.definition.Label,
                item.value,
                item.definition.Type,
                item.definition.Sensitive,
                item.editable,
                item.options, item.definition.ConditionKey, item.definition.ConditionValue))
            .ToList();
        if (sectionKey == "advanced" && (typeCode is "Share" or "siLock" || GetNode(raw, "UseDefStateCaching") is not null))
            result.AddRange(BuildCapturedFields(raw, HostStateCacheSettings.Fields, sectionKey,
                canEditConfiguration && (typeCode is "Share" or "siLock"), Array.Empty<HostEditorFieldOption>(), Array.Empty<HostEditorFieldOption>()));
        return result;
    }

    private static string FormatValue(JsonNode? node, string type)
    {
        var value = ReadNode(node).Trim();
        if (value.Length == 0) return string.Empty;
        if (type.Equals("Boolean", StringComparison.OrdinalIgnoreCase))
            return IsTrue(value) ? "Yes" : "No";
        return value;
    }

    private static bool IsTrue(string value)
        => value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase);

    internal static (string TypeCode, JsonObject Host) UnwrapHost(JsonObject wrapper) => HostDefinitionJson.UnwrapHost(wrapper);
    internal static JsonNode? GetNode(JsonObject? obj, string name) => HostDefinitionJson.GetNode(obj, name);
    internal static string ReadNode(JsonNode? node) => HostDefinitionJson.ReadNode(node);
}
