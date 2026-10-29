// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Shared, pure native Traditional Task JSON / Step profile / schedule / verification operations.

using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Services.MoveIt.TraditionalTasks;

/// <summary>
/// Pure, shared native Task definition transformations. Does not perform REST/COM calls,
/// depend on editor UI types, or mutate unrelated MOVEit properties. The shared composer
/// owns new Step and Host-change entrypoints; the shared patch service orchestrates writes.
/// </summary>
internal static class TraditionalTaskDefinitionEditorOperations
{
    internal static void ApplyTaskFields(JsonObject task, JsonObject patch)
    {
        task["Name"] = GetString(patch, "name").Trim();
        task["Active"] = Flag(GetBool(patch, "enabled"));

        var info = GetObject(task, "Info");
        if (info is null) { info = new JsonObject(); task["Info"] = info; }
        info["Description"] = GetString(patch, "description");
        info["Notes"] = GetString(patch, "operatorNotes");

        task[ExistingPropertyName(task, "CacheNames")] = ResolveCacheNames(GetString(patch, "cacheNames"), GetString(task, "CacheNames"));
        var useDefaultStateCaching = GetBool(patch, "useDefaultStateCaching", true);
        task["UseDefStateCaching"] = Flag(useDefaultStateCaching);
        if (useDefaultStateCaching)
        {
            // Native MOVEit omits both explicit cache fields when the Task inherits the global setting.
            // More importantly, an empty StateCacheUnit is not a valid MicStateCacheUnitType and causes
            // the full Task PUT to fail even when the editor did not touch Advanced settings.
            RemovePropertyIgnoreCase(task, "StateCacheTime");
            RemovePropertyIgnoreCase(task, "StateCacheUnit");
        }
        else
        {
            var stateCacheTime = ReadRequiredWholeNumber(patch, "stateCacheTime", "State Cache Time");
            if (stateCacheTime < -1)
                throw new InvalidOperationException("State Cache Time must be -1 (Always), 0 (Until end of task run), or a positive duration.");
            task[ExistingPropertyName(task, "StateCacheTime")] = stateCacheTime;
            if (stateCacheTime > 0)
            {
                var unit = GetString(patch, "stateCacheUnit").Trim();
                if (!unit.Equals("Hours", StringComparison.OrdinalIgnoreCase) && !unit.Equals("Minutes", StringComparison.OrdinalIgnoreCase))
                    unit = "Minutes";
                task[ExistingPropertyName(task, "StateCacheUnit")] = unit.Equals("Hours", StringComparison.OrdinalIgnoreCase) ? "Hours" : "Minutes";
            }
            else
            {
                // MOVEit represents Always (-1) and Until end of task run (0) without a unit.
                RemovePropertyIgnoreCase(task, "StateCacheUnit");
            }
        }

        task["AR"] = Flag(GetBool(patch, "autoRerun"));
        if (GetBool(patch, "useGlobalTaskLogRetention", true))
        {
            RemovePropertyIgnoreCase(task, "NumTaskLogsToKeep");
        }
        else
        {
            var taskLogsToKeep = ReadRequiredWholeNumber(patch, "taskLogsToKeep", "Number of Task Logs Saved");
            if (taskLogsToKeep < 0) throw new InvalidOperationException("Number of Task Logs Saved must be zero or greater.");
            task[ExistingPropertyName(task, "NumTaskLogsToKeep")] = taskLogsToKeep;
        }
    }

    // CacheNames is an enum, not free text. Native Task shells/defaults use random;
    // an unspecified editor value must preserve a fresh explicit choice (notably orig).
    // Never write the empty UI sentinel, which MOVEit rejects as MicCacheNamesType.
    internal static string ResolveCacheNames(string requested, string current = "")
    {
        var value = FirstNonBlank(requested, current, "random").ToLowerInvariant();
        return value is "random" or "orig" ? value
            : throw new InvalidOperationException("Cache File Names must use random names or original names.");
    }

    internal static void ApplySteps(JsonObject task, JsonArray? requested)
    {
        if (requested is null) throw new InvalidOperationException("Task Steps are required.");
        var existing = GetStepObjects(GetNode(task, "steps"));
        var consumed = new HashSet<int>();
        var output = new JsonArray();

        foreach (var node in requested)
        {
            if (node is not JsonObject item) continue;
            var kind = NormalizeStepKind(GetString(item, "kind"));
            var id = GetString(item, "id").Trim();
            var patch = GetObject(item, "patch");
            if (patch is null)
            {
                var preserveIndex = FindStep(existing, consumed, kind, id, GetInt(item, "index", -1));
                if (preserveIndex >= 0)
                {
                    consumed.Add(preserveIndex);
                    output.Add(existing[preserveIndex].DeepClone());
                    continue;
                }

                // A null patch means "preserve this existing opaque/unverified Step exactly". It must
                // never be accepted for a newly-created Step because there is no authoritative server
                // object to preserve and silently dropping it would make the editor appear to save a
                // Step that MOVEit never received.
                throw new InvalidOperationException($"A new {kind} Step cannot be saved without a verified MAUI write profile.");
            }

            if (kind is not ("Source" or "Process" or "Destination"))
                throw new InvalidOperationException($"Step type {kind} is not supported by the MAUI save contract.");

            if (kind is "Source" or "Destination")
            {
                var hostTypeCode = TraditionalTaskHostCapabilities.NormalizeTypeCode(GetString(patch, "Type"));
                if (!TraditionalTaskHostCapabilities.CanWrite(kind, hostTypeCode))
                    throw new InvalidOperationException($"The MAUI Task Editor does not yet have a verified write profile for {TraditionalTaskHostCapabilities.FriendlyName(hostTypeCode)} {kind} steps.");

                foreach (var field in patch.Select(pair => pair.Key))
                {
                    if (!TraditionalTaskHostCapabilities.IsPatchFieldAllowed(kind, hostTypeCode, field))
                        throw new InvalidOperationException($"Field {field} is not part of the verified {TraditionalTaskHostCapabilities.FriendlyName(hostTypeCode)} {kind} write profile.");
                }
            }

            var existingIndex = FindStep(existing, consumed, kind, id, GetInt(item, "index", -1));
            JsonObject wrapper;
            JsonObject target;
            if (existingIndex >= 0)
            {
                consumed.Add(existingIndex);
                wrapper = (JsonObject)existing[existingIndex].DeepClone();
                target = GetObject(wrapper, kind) ?? new JsonObject();
            }
            else
            {
                wrapper = new JsonObject();
                target = new JsonObject();
            }

            var hostChanged = false;
            if ((kind is "Source" or "Destination") && existingIndex >= 0)
            {
                var existingHostTypeCode = TraditionalTaskHostCapabilities.NormalizeTypeCode(GetString(target, "Type"));
                var existingHostId = GetString(target, "HostID").Trim();
                if (existingHostTypeCode.Length == 0 && existingHostId == "0")
                    existingHostTypeCode = "FileSystem";
                if (existingHostTypeCode.Length == 0)
                    throw new InvalidOperationException($"MAUI could not verify the existing {kind} Host type before saving. No changes were sent to MOVEit.");

                var requestedHostTypeCode = TraditionalTaskHostCapabilities.NormalizeTypeCode(GetString(patch, "Type"));
                var requestedHostId = GetString(patch, "HostID").Trim();
                var hostTypeChanged = !existingHostTypeCode.Equals(requestedHostTypeCode, StringComparison.OrdinalIgnoreCase);
                if (hostTypeChanged && !TraditionalTaskHostCapabilities.CanChangeHost(kind, existingHostTypeCode, requestedHostTypeCode))
                    throw new InvalidOperationException($"Changing this {kind} from {TraditionalTaskHostCapabilities.FriendlyName(existingHostTypeCode)} to {TraditionalTaskHostCapabilities.FriendlyName(requestedHostTypeCode)} is not part of a verified MOVEit Change Host profile.");

                hostChanged = hostTypeChanged || !existingHostId.Equals(requestedHostId, StringComparison.OrdinalIgnoreCase);

                if (hostChanged)
                {
                    if (RequiresConfiguredFolder(kind, requestedHostTypeCode) && GetConfiguredFolderFromPatch(kind, requestedHostTypeCode, patch).Length == 0)
                        throw new InvalidOperationException($"Select the {kind} Folder(s) for the newly selected Host before saving.");

                    // MOVEit's Change Host behavior retains the host-neutral General/After Transfer settings,
                    // but Folder(s) and Host Override values belong to the newly selected Host. Rebuild from
                    // only the explicitly verified cross-host fields before applying the new profile patch so
                    // stale credentials/timeouts/protocol-specific properties cannot leak across host types.
                    target = CreateHostChangeTarget(kind, target);
                }
            }

            if (kind is "Source" or "Destination")
            {
                var requestedHostTypeCode = TraditionalTaskHostCapabilities.NormalizeTypeCode(GetString(patch, "Type"));
                // Mail Steps share the same credential contract. New overrides and changes
                // between Legacy/OAuth require an explicit secret for that mode; an unchanged
                // override can omit it to preserve MOVEit's protected value.
                var mailCredentialsChanged = (requestedHostTypeCode is "POP3" or "SMTP") && !GetBool(patch, "UseDefUser", true)
                    && (GetBool(target, "UseDefUser", true) || GetBool(target, "UseOAuth") != GetBool(patch, "UseOAuth"));
                ValidateHostStepPatch(kind, requestedHostTypeCode, patch, hostChanged || existingIndex < 0 || GetBool(item,"clone") || mailCredentialsChanged);
                if (hostChanged || existingIndex < 0)
                    ApplyHostChangeProfileDefaults(kind, requestedHostTypeCode, target);
            }

            MergeStepPatch(target, patch);
            if (hostChanged || GetObject(wrapper, kind) is null) wrapper[kind] = target;
            output.Add(wrapper);
        }

        task["steps"] = output;
    }


    internal static bool RequiresConfiguredFolder(string kind, string hostTypeCode)
    {
        var profile = TraditionalTaskHostCapabilities.WriteProfile(kind, hostTypeCode);
        return !profile.Equals(TraditionalTaskHostCapabilities.Pop3WriteProfile, StringComparison.OrdinalIgnoreCase)
            && !profile.Equals(TraditionalTaskHostCapabilities.SmtpWriteProfile, StringComparison.OrdinalIgnoreCase);
    }

    internal static string GetConfiguredFolderFromPatch(string kind, string hostTypeCode, JsonObject patch)
    {
        var profile = TraditionalTaskHostCapabilities.WriteProfile(kind, hostTypeCode);
        return profile.Equals(TraditionalTaskHostCapabilities.MoveItTransferWriteProfile, StringComparison.OrdinalIgnoreCase)
            ? GetString(patch, "FolderName").Trim()
            : GetString(patch, "Path").Trim();
    }

    internal static void ValidateHostStepPatch(
        string kind,
        string hostTypeCode,
        JsonObject patch,
        bool requiresNewHostSpecificValues)
    {
        var profile = TraditionalTaskHostCapabilities.WriteProfile(kind, hostTypeCode);
        if (profile == TraditionalTaskHostCapabilities.FileSystemWriteProfile && GetString(patch, "HostID").Trim() != "0")
            throw new InvalidOperationException("Local File System Steps require Host ID 0.");

        // MOVEit's REST contract defines these values as integer/long fields. Keep whole-number,
        // non-negative validation at the shared save boundary so every verified file-host profile
        // gets the same guardrail without inventing undocumented upper limits.
        ValidateIntegerPatchField(patch, "RetryCount", $"{kind} Retry Count");
        ValidateIntegerPatchField(patch, "RetryTimeoutSecs", $"{kind} Retry Timeout");
        if (kind.Equals("Source", StringComparison.OrdinalIgnoreCase))
        {
            // An explicitly supplied empty filespec matches no files. Omitted fields remain
            // partial edits and must preserve the authoritative value on the existing step.
            if (patch.Any(field => field.Key.Equals("FileMask", StringComparison.OrdinalIgnoreCase))
                && GetString(patch, "FileMask").Trim().Length == 0)
                throw new InvalidOperationException("Source File(s) is required. Enter a filename or wildcard such as *.*.");
            ValidateIntegerPatchField(patch, "MxFi", "Source File Count Limit");
            ValidateIntegerPatchField(patch, "MxBy", "Source File Size Limit");
            if (profile.Equals(TraditionalTaskHostCapabilities.FileSystemWriteProfile, StringComparison.OrdinalIgnoreCase)
                || profile.Equals(TraditionalTaskHostCapabilities.ShareWriteProfile, StringComparison.OrdinalIgnoreCase)
                || profile.Equals(TraditionalTaskHostCapabilities.SftpWriteProfile, StringComparison.OrdinalIgnoreCase)
                || profile.Equals(TraditionalTaskHostCapabilities.S3WriteProfile, StringComparison.OrdinalIgnoreCase)
                || profile.Equals(TraditionalTaskHostCapabilities.AzureBlobWriteProfile, StringComparison.OrdinalIgnoreCase)
                || profile.Equals(TraditionalTaskHostCapabilities.GoogleStorageWriteProfile, StringComparison.OrdinalIgnoreCase))
                ValidateIntegerPatchField(patch, "RescanSecs", "Source Transfer Rescan Time");
        }

        if (profile.Equals(TraditionalTaskHostCapabilities.Pop3WriteProfile, StringComparison.OrdinalIgnoreCase)
            || profile.Equals(TraditionalTaskHostCapabilities.SmtpWriteProfile, StringComparison.OrdinalIgnoreCase))
        {
            var smtp = profile.Equals(TraditionalTaskHostCapabilities.SmtpWriteProfile, StringComparison.OrdinalIgnoreCase);
            var label = smtp ? "SMTP Destination" : "POP3 Source";
            ValidateIntegerPatchField(patch, "ConnTimeoutSecs", $"{label} Connect Timeout");
            ValidateIntegerPatchField(patch, "DataTimeoutSecs", $"{label} Data Timeout");
            if (smtp && GetString(patch, "AddressTo").Trim().Length == 0)
                throw new InvalidOperationException("SMTP Destination requires at least one recipient.");
            if (!GetBool(patch, "UseDefUser", true))
            {
                if (GetString(patch, "Username").Trim().Length == 0)
                    throw new InvalidOperationException($"{label} Username is required when default credentials are overridden.");
                var useOAuth = GetBool(patch, "UseOAuth");
                if (useOAuth)
                {
                    if (GetString(patch, "OAuthClientID").Trim().Length == 0)
                        throw new InvalidOperationException($"{label} OAuth Client ID is required.");
                    if (GetString(patch, "OAuthScope").Trim().Length == 0)
                        throw new InvalidOperationException($"{label} OAuth Authorization Scope is required.");
                    if (GetString(patch, "OAuthTokenURL").Trim().Length == 0)
                        throw new InvalidOperationException($"{label} OAuth Token URL is required.");
                }
                var field = useOAuth ? "OAuthClientSecret" : "Password";
                var secretLabel = useOAuth ? "OAuth Client Secret" : "Password";
                var supplied = GetNode(patch, field) is not null && GetString(patch, field).Length > 0;
                if (!supplied && requiresNewHostSpecificValues)
                    throw new InvalidOperationException($"{label} {secretLabel} must be set for a new override or authentication mode.");
                if (GetNode(patch, field) is not null && !supplied)
                    throw new InvalidOperationException($"{label} {secretLabel} cannot be blank.");
            }
            return;
        }

        if (profile.Equals(TraditionalTaskHostCapabilities.FtpWriteProfile, StringComparison.OrdinalIgnoreCase))
        {
            ValidateIntegerPatchField(patch, "ConnTimeoutSecs", $"{kind} Connect Timeout");
            ValidateIntegerPatchField(patch, "DataTimeoutSecs", $"{kind} Data Timeout");
            if (kind.Equals("Source", StringComparison.OrdinalIgnoreCase))
                ValidateIntegerPatchField(patch, "RescanSecs", "Source Transfer Rescan Time");

            if (GetInt(patch, "UseDefUser", 1) == 0)
            {
                if (GetString(patch, "Username").Trim().Length == 0)
                    throw new InvalidOperationException($"{kind} Username is required when FTP/FTPS default credentials are overridden.");
                var suppliedPassword = GetNode(patch, "Password") is not null && GetString(patch, "Password").Length > 0;
                if (!suppliedPassword && requiresNewHostSpecificValues)
                    throw new InvalidOperationException($"{kind} Password must be set when FTP/FTPS default credentials are overridden for a new or changed Host.");
                if (GetNode(patch, "Password") is not null && !suppliedPassword)
                    throw new InvalidOperationException($"{kind} Password cannot be blank when FTP/FTPS default credentials are overridden.");
            }

            if (GetInt(patch, "UseDefClientCert", 1) == 0)
            {
                var sha1 = GetString(patch, "ClientCertSHA1").Trim();
                var store = GetString(patch, "ClientCertStore").Trim();
                var subject = GetString(patch, "ClientCertSubject").Trim();
                var any = sha1.Length > 0 || store.Length > 0 || subject.Length > 0;
                var all = sha1.Length > 0 && store.Length > 0 && subject.Length > 0;
                if (any && !all)
                    throw new InvalidOperationException($"{kind} TLS client certificate selection is incomplete.");
            }

            if (GetInt(patch, "UseDefAB", 1) == 0)
            {
                var transferType = GetString(patch, "AB").Trim();
                if (!transferType.Equals("ascii", StringComparison.OrdinalIgnoreCase) && !transferType.Equals("binary", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"{kind} FTP/FTPS Transfer Type must be ASCII or Binary.");
            }

            if (kind.Equals("Source", StringComparison.OrdinalIgnoreCase) && GetInt(patch, "UseDefMD5Get", 1) == 0)
            {
                var md5Mode = GetString(patch, "MD5Get").Trim();
                var valid = md5Mode.Equals("Never", StringComparison.OrdinalIgnoreCase)
                    || md5Mode.Equals("IfPresent", StringComparison.OrdinalIgnoreCase)
                    || md5Mode.Equals("Always", StringComparison.OrdinalIgnoreCase);
                if (!valid) throw new InvalidOperationException("FTP/FTPS Source Use MD5 must be Never, If Present, or Always (required).");
            }
            if (kind.Equals("Source", StringComparison.OrdinalIgnoreCase)
                && GetInt(patch, "UseDefMD5File", 1) == 0
                && GetString(patch, "MD5File").Trim().Length == 0)
                throw new InvalidOperationException("FTP/FTPS Source MD5 File Name is required when its Host default is overridden.");

            return;
        }

        if (profile.Equals(TraditionalTaskHostCapabilities.SftpWriteProfile, StringComparison.OrdinalIgnoreCase))
        {
            ValidateIntegerPatchField(patch, "ConnTimeoutSecs", $"{kind} Connect Timeout");
            if (kind.Equals("Source", StringComparison.OrdinalIgnoreCase))
                ValidateIntegerPatchField(patch, "RescanSecs", "Source Transfer Rescan Time");

            if (GetInt(patch, "UseDefUser", 1) == 0)
            {
                if (GetString(patch, "Username").Trim().Length == 0)
                    throw new InvalidOperationException($"{kind} Username is required when SSH/SFTP default credentials are overridden.");
                var suppliedPassword = GetNode(patch, "Password") is not null && GetString(patch, "Password").Length > 0;
                if (!suppliedPassword && requiresNewHostSpecificValues)
                    throw new InvalidOperationException($"{kind} Password must be set when SSH/SFTP default credentials are overridden for a new or changed Host.");
                if (GetNode(patch, "Password") is not null && !suppliedPassword)
                    throw new InvalidOperationException($"{kind} Password cannot be blank when SSH/SFTP default credentials are overridden.");
            }

            if (GetInt(patch, "UseDefClientKey", 1) == 0)
            {
                var keyId = GetString(patch, "ClientKeyID").Trim();
                if (keyId.Length > 0
                    && (!long.TryParse(keyId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedKeyId) || parsedKeyId < 0))
                    throw new InvalidOperationException($"{kind} SSH/SFTP Client Key must be a valid non-negative MOVEit SSH key ID.");
            }

            if (kind.Equals("Source", StringComparison.OrdinalIgnoreCase))
            {
                if (GetInt(patch, "UseDefMD5Get", 1) == 0)
                {
                    var md5Mode = GetString(patch, "MD5Get").Trim();
                    var valid = md5Mode.Equals("Never", StringComparison.OrdinalIgnoreCase)
                        || md5Mode.Equals("IfPresent", StringComparison.OrdinalIgnoreCase)
                        || md5Mode.Equals("Always", StringComparison.OrdinalIgnoreCase);
                    if (!valid) throw new InvalidOperationException("SSH/SFTP Source Use MD5 must be Never, If Present, or Always (required).");
                }
                if (GetInt(patch, "UseDefMD5File", 1) == 0 && GetString(patch, "MD5File").Trim().Length == 0)
                    throw new InvalidOperationException("SSH/SFTP Source MD5 File Name is required when its Host default is overridden.");
            }
            else if (GetInt(patch, "UseDefSetFileAttr", 1) == 0 && GetInt(patch, "SetFileAttr", 0) != 0)
            {
                var attributes = GetString(patch, "DestFileAttr").Trim();
                if (attributes.Length != 3 || attributes.Any(ch => ch < '0' || ch > '7'))
                    throw new InvalidOperationException("SSH/SFTP Destination file permissions must be a three-digit octal value from 000 through 777.");
            }
            return;
        }

        if (profile.Equals(TraditionalTaskHostCapabilities.S3WriteProfile, StringComparison.OrdinalIgnoreCase))
        {
            ValidateIntegerPatchField(patch, "ConnTimeoutSecs", $"{kind} Connect Timeout");
            ValidateIntegerPatchField(patch, "DataTimeoutSecs", $"{kind} Data Timeout");
            if (kind.Equals("Source", StringComparison.OrdinalIgnoreCase))
                ValidateIntegerPatchField(patch, "RescanSecs", "Source Transfer Rescan Time");

            if (GetInt(patch, "UseDefUser", 1) == 0)
            {
                if (GetString(patch, "Username").Trim().Length == 0)
                    throw new InvalidOperationException($"{kind} Amazon S3 Access Key ID is required when default credentials are overridden.");
                var suppliedPassword = GetNode(patch, "Password") is not null && GetString(patch, "Password").Length > 0;
                if (!suppliedPassword && requiresNewHostSpecificValues)
                    throw new InvalidOperationException($"{kind} Amazon S3 Secret Access Key must be set when default credentials are overridden for a new or changed Host.");
                if (GetNode(patch, "Password") is not null && !suppliedPassword)
                    throw new InvalidOperationException($"{kind} Amazon S3 Secret Access Key cannot be blank when default credentials are overridden.");
            }

            if (GetInt(patch, "UseDefBucket", 1) == 0 && GetString(patch, "Bucket").Trim().Length == 0)
                throw new InvalidOperationException($"{kind} Amazon S3 Bucket Name is required when the Host default bucket is overridden.");

            if (kind.Equals("Source", StringComparison.OrdinalIgnoreCase) && GetInt(patch, "UseDefDLInteg", 1) == 0)
            {
                var mode = GetString(patch, "DLInteg").Trim().ToLowerInvariant();
                var valid = mode is "never" or "ifsure" or "ignore" or "always";
                if (!valid)
                    throw new InvalidOperationException("Amazon S3 Source Data Integrity Checking Rule is invalid.");
            }
            return;
        }

        if (profile.Equals(TraditionalTaskHostCapabilities.AzureBlobWriteProfile, StringComparison.OrdinalIgnoreCase))
        {
            ValidateIntegerPatchField(patch, "ConnTimeoutSecs", $"{kind} Connect Timeout");
            ValidateIntegerPatchField(patch, "DataTimeoutSecs", $"{kind} Data Timeout");
            if (kind.Equals("Source", StringComparison.OrdinalIgnoreCase))
                ValidateIntegerPatchField(patch, "RescanSecs", "Source Transfer Rescan Time");

            if (GetInt(patch, "UseDefUser", 1) == 0)
            {
                if (GetString(patch, "Username").Trim().Length == 0)
                    throw new InvalidOperationException($"{kind} Azure Blob Storage Account is required when default credentials are overridden.");
                var suppliedPassword = GetNode(patch, "Password") is not null && GetString(patch, "Password").Length > 0;
                if (!suppliedPassword && requiresNewHostSpecificValues)
                    throw new InvalidOperationException($"{kind} Azure Blob Key must be set when default credentials are overridden for a new or changed Host.");
                if (GetNode(patch, "Password") is not null && !suppliedPassword)
                    throw new InvalidOperationException($"{kind} Azure Blob Key cannot be blank when default credentials are overridden.");
            }

            if (GetInt(patch, "UseDefContainer", 1) == 0 && GetString(patch, "Container").Trim().Length == 0)
                throw new InvalidOperationException($"{kind} Azure Blob Container is required when the Host default container is overridden.");

            if (kind.Equals("Source", StringComparison.OrdinalIgnoreCase) && GetInt(patch, "UseDefDLInteg", 1) == 0)
            {
                var mode = GetString(patch, "DLInteg").Trim().ToLowerInvariant();
                // Progress documents Azure Blob download integrity values as never/ignore/always.
                var valid = mode is "never" or "ignore" or "always";
                if (!valid)
                    throw new InvalidOperationException("Azure Blob Source Data Integrity Checking Rule is invalid.");
            }
            return;
        }

        if (profile.Equals(TraditionalTaskHostCapabilities.GoogleStorageWriteProfile, StringComparison.OrdinalIgnoreCase))
        {
            ValidateIntegerPatchField(patch, "ConnTimeoutSecs", $"{kind} Connect Timeout");
            ValidateIntegerPatchField(patch, "DataTimeoutSecs", $"{kind} Data Timeout");
            if (kind.Equals("Source", StringComparison.OrdinalIgnoreCase))
                ValidateIntegerPatchField(patch, "RescanSecs", "Source Transfer Rescan Time");

            if (GetInt(patch, "UseDefBucket", 1) == 0 && GetString(patch, "Bucket").Trim().Length == 0)
                throw new InvalidOperationException($"{kind} Google Cloud Storage Bucket Name is required when the Host default bucket is overridden.");

            if (GetInt(patch, "UseDefUser", 1) == 0)
            {
                var useOAuth = GetInt(patch, "UseOAuth", 0) != 0;
                if (useOAuth)
                {
                    if (GetString(patch, "OAuthClientID").Trim().Length == 0)
                        throw new InvalidOperationException($"{kind} Google Cloud Storage OAuth Client ID is required.");
                    if (GetString(patch, "ProjectID").Trim().Length == 0)
                        throw new InvalidOperationException($"{kind} Google Cloud Storage OAuth Project ID is required.");
                    var suppliedPrivateKey = GetNode(patch, "OAuthClientSecret") is not null && GetString(patch, "OAuthClientSecret").Length > 0;
                    if (!suppliedPrivateKey && requiresNewHostSpecificValues)
                        throw new InvalidOperationException($"{kind} Google Cloud Storage OAuth private key must be set for a new or changed Host.");
                    if (GetNode(patch, "OAuthClientSecret") is not null && !suppliedPrivateKey)
                        throw new InvalidOperationException($"{kind} Google Cloud Storage OAuth private key cannot be blank.");
                }
                else
                {
                    if (GetString(patch, "Username").Trim().Length == 0)
                        throw new InvalidOperationException($"{kind} Google Cloud Storage Access Key ID is required for HMAC authentication.");
                    var suppliedSecret = GetNode(patch, "Password") is not null && GetString(patch, "Password").Length > 0;
                    if (!suppliedSecret && requiresNewHostSpecificValues)
                        throw new InvalidOperationException($"{kind} Google Cloud Storage Secret Access Key must be set for a new or changed Host.");
                    if (GetNode(patch, "Password") is not null && !suppliedSecret)
                        throw new InvalidOperationException($"{kind} Google Cloud Storage Secret Access Key cannot be blank.");
                }
            }
            return;
        }

        if (profile.Equals(TraditionalTaskHostCapabilities.SharePointWriteProfile, StringComparison.OrdinalIgnoreCase))
        {
            ValidateIntegerPatchField(patch, "ConnTimeoutSecs", $"{kind} Connect Timeout");
            ValidateIntegerPatchField(patch, "DataTimeoutSecs", $"{kind} Data Timeout");
            if (kind.Equals("Source", StringComparison.OrdinalIgnoreCase))
                ValidateIntegerPatchField(patch, "RescanSecs", "Source Transfer Rescan Time");

            if (GetInt(patch, "UseDefDocumentLibrary", 1) == 0)
            {
                if (GetString(patch, "DocumentLibrary").Trim().Length == 0)
                    throw new InvalidOperationException($"{kind} SharePoint Document Library is required when the Host default library is overridden.");
                if (GetString(patch, "DocumentLibraryGuid").Trim().Length == 0)
                    throw new InvalidOperationException($"{kind} SharePoint Document Library GUID is required. Use Browse to select the library.");
            }

            if (GetInt(patch, "UseDefUser", 1) == 0)
            {
                var authenticationMode = GetInt(patch, "UsingAppAuthentication", -1);
                if (authenticationMode == 1)
                {
                    if (GetString(patch, "ClientID").Trim().Length == 0)
                        throw new InvalidOperationException($"{kind} SharePoint Client ID is required for App Access.");
                    if (GetString(patch, "RealmID").Trim().Length == 0)
                        throw new InvalidOperationException($"{kind} SharePoint Realm ID is required for App Access.");
                    var suppliedSecret = GetNode(patch, "ClientSecret") is not null && GetString(patch, "ClientSecret").Length > 0;
                    if (!suppliedSecret && requiresNewHostSpecificValues)
                        throw new InvalidOperationException($"{kind} SharePoint Client Secret must be set for App Access on a new or changed Host.");
                    if (GetNode(patch, "ClientSecret") is not null && !suppliedSecret)
                        throw new InvalidOperationException($"{kind} SharePoint Client Secret cannot be blank.");
                }
                else if (authenticationMode == 2)
                {
                    if (GetString(patch, "ClientID").Trim().Length == 0)
                        throw new InvalidOperationException($"{kind} SharePoint Client ID is required for Certificate Access.");
                    if (GetString(patch, "TenantID").Trim().Length == 0)
                        throw new InvalidOperationException($"{kind} SharePoint Tenant ID is required for Certificate Access.");

                    var sha1 = GetString(patch, "ClientCertSHA1").Trim();
                    var store = GetString(patch, "ClientCertStore").Trim();
                    var subject = GetString(patch, "ClientCertSubject").Trim();
                    if (sha1.Length == 0 || store.Length == 0 || subject.Length == 0)
                        throw new InvalidOperationException($"{kind} SharePoint Certificate Access requires a complete client certificate selection.");
                }
                else
                {
                    throw new InvalidOperationException($"{kind} SharePoint authentication mode is invalid.");
                }
            }
            return;
        }

        if (!profile.Equals(TraditionalTaskHostCapabilities.MoveItTransferWriteProfile, StringComparison.OrdinalIgnoreCase))
            return;

        ValidateIntegerPatchField(patch, "ConnTimeoutSecs", $"{kind} Connect Timeout");
        ValidateIntegerPatchField(patch, "DataTimeoutSecs", $"{kind} Data Timeout");

        if (GetInt(patch, "UseDefUser", 1) == 0)
        {
            if (GetString(patch, "Username").Trim().Length == 0)
                throw new InvalidOperationException($"{kind} Username is required when MOVEit Transfer default credentials are overridden.");

            var suppliedPassword = GetNode(patch, "Password") is not null && GetString(patch, "Password").Length > 0;
            if (!suppliedPassword && requiresNewHostSpecificValues)
                throw new InvalidOperationException($"{kind} Password must be set when MOVEit Transfer default credentials are overridden for a new or changed Host.");
            if (GetNode(patch, "Password") is not null && !suppliedPassword)
                throw new InvalidOperationException($"{kind} Password cannot be blank when MOVEit Transfer default credentials are overridden.");
        }

        if (GetInt(patch, "UseDefClientCert", 1) == 0)
        {
            var sha1 = GetString(patch, "ClientCertSHA1").Trim();
            var store = GetString(patch, "ClientCertStore").Trim();
            var subject = GetString(patch, "ClientCertSubject").Trim();
            var any = sha1.Length > 0 || store.Length > 0 || subject.Length > 0;
            var all = sha1.Length > 0 && store.Length > 0 && subject.Length > 0;
            if (any && !all)
                throw new InvalidOperationException($"{kind} TLS client certificate selection is incomplete.");
        }

        if (kind.Equals("Source", StringComparison.OrdinalIgnoreCase) && GetInt(patch, "UseDefSort", 1) == 0)
        {
            var sort = GetString(patch, "Sort").Trim();
            var allowedSort = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Filename,A", "Filename,D", "Size,A", "Size,D", "Date,A", "Date,D"
            };
            if (!allowedSort.Contains(sort))
                throw new InvalidOperationException("MOVEit Transfer Source file sorting must specify File Name, Size, or Date in ascending or descending order.");
        }
    }

    internal static void ValidateIntegerPatchField(JsonObject patch, string fieldName, string label)
    {
        var node = GetNode(patch, fieldName);
        if (node is null) return;
        var text = NodeString(node).Trim();
        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0)
            throw new InvalidOperationException($"{label} must be a whole number of zero or greater.");
    }

    internal static void ApplyHostChangeProfileDefaults(string kind, string hostTypeCode, JsonObject target)
    {
        var profile = TraditionalTaskHostCapabilities.WriteProfile(kind, hostTypeCode);
        if (profile == TraditionalTaskHostCapabilities.FileSystemWriteProfile)
        {
            target["UseDefRetryCount"] = 1;
            target["UseDefRetryTimeoutSecs"] = 1;
            if (kind.Equals("Source", StringComparison.OrdinalIgnoreCase))
            {
                target["UseDefRescanSecs"] = 1;
                target["UDMxFi"] = 1;
                target["UDMxBy"] = 1;
            }
            else target["UseDefPreserveFileTimestamp"] = 1;
            return;
        }
        if (profile.Equals(TraditionalTaskHostCapabilities.Pop3WriteProfile, StringComparison.OrdinalIgnoreCase))
        {
            target["UseDefUser"] = 1;
            target["UseDefConnTimeoutSecs"] = 1;
            target["UseDefDataTimeoutSecs"] = 1;
            target["UseDefRetryCount"] = 1;
            target["UseDefRetryTimeoutSecs"] = 1;
            return;
        }

        if (profile.Equals(TraditionalTaskHostCapabilities.SmtpWriteProfile, StringComparison.OrdinalIgnoreCase))
        {
            target["UseDefUser"] = 1;
            target["UseDefConnTimeoutSecs"] = 1;
            target["UseDefDataTimeoutSecs"] = 1;
            target["UseDefRetryCount"] = 1;
            target["UseDefRetryTimeoutSecs"] = 1;
            return;
        }

        if (profile.Equals(TraditionalTaskHostCapabilities.FtpWriteProfile, StringComparison.OrdinalIgnoreCase))
        {
            // Seed only fields observed in the native FTP/FTPS Task representation. The editor then
            // applies its explicit patch over these defaults. This keeps Change Host from carrying
            // protocol-specific state from the previous Host while still producing a complete FTP Step.
            target["UseDefUser"] = 1;
            target["UseDefClientCert"] = 1;
            target["UseDefConnTimeoutSecs"] = 1;
            target["UseDefDataTimeoutSecs"] = 1;
            target["UseDefRetryCount"] = 1;
            target["UseDefRetryTimeoutSecs"] = 1;
            target["UseDefAB"] = 1;
            target["UseDefPassive"] = 1;
            target["UseDefXS"] = 1;
            target["UseDefRsm"] = 1;
            target["UseDefReuseSSL"] = 1;
            target["AddCom"] = string.Empty;
            target["AddComAX"] = string.Empty;
            if (kind.Equals("Source", StringComparison.OrdinalIgnoreCase))
            {
                target["UseDefRescanSecs"] = 1;
                target["UseDefBlindDL"] = 1;
                target["UDMxFi"] = 1;
                target["UDMxBy"] = 1;
                target["UseDefMD5Get"] = 1;
                target["UseDefMD5File"] = 1;
            }
            else
            {
                target["UseDefBlindUL"] = 1;
            }
            return;
        }

        if (profile.Equals(TraditionalTaskHostCapabilities.SftpWriteProfile, StringComparison.OrdinalIgnoreCase))
        {
            // Seed the generic compatibility fields observed in the native SSHFTP Task export as well
            // as the controls exposed by the SFTP override UI. These values are used only when a Step
            // is created or its Host changes; ordinary edits retain the fresh authoritative Step fields.
            target["UseDefUser"] = 1;
            target["UseDefClientKey"] = 1;
            target["UseDefClientCert"] = 1;
            target["UseDefConnTimeoutSecs"] = 1;
            target["UseDefDataTimeoutSecs"] = 1;
            target["UseDefRetryCount"] = 1;
            target["UseDefRetryTimeoutSecs"] = 1;
            target["UseDefRsm"] = 1;
            if (kind.Equals("Source", StringComparison.OrdinalIgnoreCase))
            {
                target["UseDefRescanSecs"] = 1;
                target["UseDefBlindDL"] = 1;
                target["UDMxFi"] = 1;
                target["UDMxBy"] = 1;
                target["UseDefMD5Get"] = 1;
                target["UseDefMD5File"] = 1;
                // Native SSHFTP Source carries these generic protocol members with fixed values even
                // though Web Admin does not expose them as SFTP overrides.
                target["UseDefAB"] = 0;
                target["AB"] = "binary";
                target["UseDefPassive"] = 0;
                target["Passive"] = 1;
                target["UseDefXS"] = 0;
                target["XS"] = 0;
                target["UseDefReuseSSL"] = 0;
                target["ReuseSSL"] = 1;
            }
            else
            {
                target["UseDefSetFileAttr"] = 1;
                target["UseDefPreserveFileTimestamp"] = 1;
                target["UseDefAB"] = 1;
                target["AB"] = "binary";
                target["UseDefPassive"] = 1;
                target["Passive"] = 1;
                target["UseDefXS"] = 1;
                target["XS"] = 0;
                target["UseDefBlindUL"] = 1;
                target["BlindUL"] = 0;
                target["UseDefReuseSSL"] = 1;
                target["ReuseSSL"] = 0;
            }
            return;
        }

        if (profile.Equals(TraditionalTaskHostCapabilities.S3WriteProfile, StringComparison.OrdinalIgnoreCase))
        {
            // Seed only members observed in the supplied native S3 Task export. S3 remains isolated
            // from FTP/SFTP even where MOVEit uses similarly named timeout/credential fields.
            target["UseDefBucket"] = 1;
            target["UseDefUser"] = 1;
            target["UseDefConnTimeoutSecs"] = 1;
            target["UseDefDataTimeoutSecs"] = 1;
            target["UseDefRetryCount"] = 1;
            target["UseDefRetryTimeoutSecs"] = 1;
            if (kind.Equals("Source", StringComparison.OrdinalIgnoreCase))
            {
                target["UseDefRescanSecs"] = 1;
                target["UDMxFi"] = 1;
                target["UDMxBy"] = 1;
                target["UseDefDLInteg"] = 1;

                // Generic protocol members observed in the native S3 Source representation are
                // seeded only during Change Host/new Step construction; they are not exposed as S3 controls.
                target["UseDefAB"] = 0;
                target["AB"] = "binary";
                target["UseDefBlindDL"] = 0;
                target["BlindDL"] = 1;
                target["UseDefClientCert"] = 1;
                target["UseDefClientKey"] = 1;
                target["ClientKeyID"] = 0;
                target["UseDefMD5Get"] = 0;
                target["MD5Get"] = "IfPresent";
                target["UseDefMD5File"] = 0;
                target["MD5File"] = "MD5SUM";
                target["UseDefDLInteg"] = 0;
                target["DLInteg"] = "never";
                target["AddCom"] = string.Empty;
                target["AddComAX"] = string.Empty;
                target["UseDefPassive"] = 0;
                target["Passive"] = 1;
                target["UseDefRsm"] = 0;
                target["Rsm"] = 1;
                target["UseDefXS"] = 0;
                target["XS"] = 0;
                target["UseDefReuseSSL"] = 0;
                target["ReuseSSL"] = 1;
            }
            else
            {
                // Native cloud Destination exports carry ForceDir=1 even though Web Admin does not
                // expose Create Directories for object-storage hosts. Reset it here on Host change
                // instead of retaining the previous Host's value.
                target["ForceDir"] = 1;
                target["UseDefAB"] = 1;
                target["AB"] = "binary";
                target["UseDefBlindUL"] = 1;
                target["BlindUL"] = 0;
                target["UseDefClientCert"] = 1;
                target["UseDefClientKey"] = 1;
                target["ClientKeyID"] = 0;
                target["UseDefSetFileAttr"] = 1;
                target["SetFileAttr"] = 0;
                target["DestFileAttr"] = string.Empty;
                target["UseDefPreserveFileTimestamp"] = 1;
                target["PreserveFileTimestamp"] = 0;
                target["AddCom"] = string.Empty;
                target["AddComAX"] = string.Empty;
                target["UseDefPassive"] = 1;
                target["Passive"] = 1;
                target["UseDefRsm"] = 1;
                target["Rsm"] = 1;
                target["UseDefXS"] = 1;
                target["XS"] = 0;
                target["UseDefReuseSSL"] = 1;
                target["ReuseSSL"] = 0;
            }
            return;
        }

        if (profile.Equals(TraditionalTaskHostCapabilities.AzureBlobWriteProfile, StringComparison.OrdinalIgnoreCase))
        {
            // Seed only fields observed in the supplied native Azure Blob Task export. Azure Blob
            // remains isolated from S3 even where MOVEit uses the same cloud credential/timeout members.
            target["UseDefContainer"] = 1;
            target["UseDefUser"] = 1;
            target["UseDefConnTimeoutSecs"] = 1;
            target["UseDefDataTimeoutSecs"] = 1;
            target["UseDefRetryCount"] = 1;
            target["UseDefRetryTimeoutSecs"] = 1;

            // MOVEit's Azure Blob Task representation also carries the generic Bucket members. They
            // are fixed compatibility members only; the Azure Blob editor never binds or patches them.
            target["UseDefBucket"] = 1;
            target["Bucket"] = string.Empty;

            if (kind.Equals("Source", StringComparison.OrdinalIgnoreCase))
            {
                target["UseDefRescanSecs"] = 1;
                target["UDMxFi"] = 1;
                target["UDMxBy"] = 1;
                target["UseDefDLInteg"] = 1;
                target["UseDefAB"] = 0;
                target["AB"] = "binary";
                target["UseDefBlindDL"] = 0;
                target["BlindDL"] = 1;
                target["UseDefClientCert"] = 1;
                target["UseDefClientKey"] = 1;
                target["UseDefMD5Get"] = 0;
                target["UseDefMD5File"] = 0;
                target["UseDefPassive"] = 0;
                target["Passive"] = 1;
                target["UseDefRsm"] = 0;
                target["Rsm"] = 1;
                target["UseDefXS"] = 0;
                target["XS"] = 0;
                target["UseDefReuseSSL"] = 0;
                target["ReuseSSL"] = 1;
            }
            else
            {
                // Native cloud Destination exports carry ForceDir=1 even though Web Admin does not
                // expose Create Directories for object-storage hosts. Reset it here on Host change
                // instead of retaining the previous Host's value.
                target["ForceDir"] = 1;
                target["UseDefAB"] = 1;
                target["AB"] = "binary";
                target["UseDefBlindUL"] = 1;
                target["BlindUL"] = 0;
                target["UseDefClientCert"] = 1;
                target["UseDefClientKey"] = 1;
                target["UseDefPassive"] = 1;
                target["Passive"] = 1;
                target["UseDefRsm"] = 1;
                target["Rsm"] = 1;
                target["UseDefSetFileAttr"] = 1;
                target["UseDefPreserveFileTimestamp"] = 1;
                target["UseDefXS"] = 1;
                target["XS"] = 0;
                target["UseDefReuseSSL"] = 1;
                target["ReuseSSL"] = 0;
            }
            return;
        }

        if (profile.Equals(TraditionalTaskHostCapabilities.GoogleStorageWriteProfile, StringComparison.OrdinalIgnoreCase))
        {
            // Google Storage is an isolated editor profile. Native Task exports confirm the generic
            // Bucket/timeout/retry/rescan/limit members plus Task-level HMAC/OAuth authentication.
            // New/changed Hosts begin by inheriting Host authentication; the explicit editor patch
            // switches UseDefUser/UseOAuth and supplies only the selected override credentials.
            target["UseDefBucket"] = 1;
            target["UseDefUser"] = 1;
            target["UseDefConnTimeoutSecs"] = 1;
            target["UseDefDataTimeoutSecs"] = 1;
            target["UseDefRetryCount"] = 1;
            target["UseDefRetryTimeoutSecs"] = 1;

            if (kind.Equals("Source", StringComparison.OrdinalIgnoreCase))
            {
                target["UseDefRescanSecs"] = 1;
                target["UDMxFi"] = 1;
                target["UDMxBy"] = 1;
                target["UseDefAB"] = 0;
                target["AB"] = "binary";
                target["UseDefBlindDL"] = 0;
                target["BlindDL"] = 1;
                target["UseDefClientCert"] = 1;
                target["UseDefClientKey"] = 1;
                target["ClientKeyID"] = 0;
                target["UseDefMD5Get"] = 0;
                target["MD5Get"] = "IfPresent";
                target["UseDefMD5File"] = 0;
                target["MD5File"] = "MD5SUM";
                target["UseDefDLInteg"] = 0;
                target["DLInteg"] = "never";
                target["AddCom"] = string.Empty;
                target["AddComAX"] = string.Empty;
                target["UseDefPassive"] = 0;
                target["Passive"] = 1;
                target["UseDefRsm"] = 0;
                target["Rsm"] = 1;
                target["UseDefXS"] = 0;
                target["XS"] = 0;
                target["UseDefReuseSSL"] = 0;
                target["ReuseSSL"] = 1;
            }
            else
            {
                target["ForceDir"] = 1;
                target["UseDefAB"] = 1;
                target["AB"] = "binary";
                target["UseDefBlindUL"] = 1;
                target["BlindUL"] = 0;
                target["UseDefClientCert"] = 1;
                target["UseDefClientKey"] = 1;
                target["ClientKeyID"] = 0;
                target["UseDefSetFileAttr"] = 1;
                target["SetFileAttr"] = 0;
                target["DestFileAttr"] = string.Empty;
                target["UseDefPreserveFileTimestamp"] = 1;
                target["PreserveFileTimestamp"] = 0;
                target["AddCom"] = string.Empty;
                target["AddComAX"] = string.Empty;
                target["UseDefPassive"] = 1;
                target["Passive"] = 1;
                target["UseDefRsm"] = 1;
                target["Rsm"] = 1;
                target["UseDefXS"] = 1;
                target["XS"] = 0;
                target["UseDefReuseSSL"] = 1;
                target["ReuseSSL"] = 0;
            }
            return;
        }

        if (profile.Equals(TraditionalTaskHostCapabilities.SharePointWriteProfile, StringComparison.OrdinalIgnoreCase))
        {
            // Seed only members observed in the supplied native SharePoint Task export. SharePoint
            // remains isolated from the other cloud profiles even where MOVEit reuses generic members.
            target["UseDefDocumentLibrary"] = 1;
            target["DocumentLibrary"] = string.Empty;
            target["DocumentLibraryGuid"] = string.Empty;
            target["UseDefUser"] = 1;
            target["UseDefClientCert"] = 1;
            target["UseDefConnTimeoutSecs"] = 1;
            target["UseDefDataTimeoutSecs"] = 1;
            target["UseDefRetryCount"] = 1;
            target["UseDefRetryTimeoutSecs"] = 1;
            target["UseDefBucket"] = 1;
            target["Bucket"] = string.Empty;
            target["UseDefContainer"] = 1;
            target["Container"] = string.Empty;
            if (kind.Equals("Source", StringComparison.OrdinalIgnoreCase))
            {
                target["UseDefRescanSecs"] = 1;
                target["UDMxFi"] = 1;
                target["UDMxBy"] = 1;
                target["UseDefAB"] = 0;
                target["AB"] = "binary";
                target["UseDefBlindDL"] = 0;
                target["BlindDL"] = 1;
                target["UseDefClientKey"] = 1;
                target["ClientKeyID"] = 0;
                target["UseDefMD5Get"] = 0;
                target["MD5Get"] = "IfPresent";
                target["UseDefMD5File"] = 0;
                target["MD5File"] = "MD5SUM";
                target["UseDefDLInteg"] = 0;
                target["DLInteg"] = "never";
                target["AddCom"] = string.Empty;
                target["AddComAX"] = string.Empty;
                target["UseDefPassive"] = 0;
                target["Passive"] = 1;
                target["UseDefRsm"] = 0;
                target["Rsm"] = 1;
                target["UseDefXS"] = 0;
                target["XS"] = 0;
                target["UseDefReuseSSL"] = 0;
                target["ReuseSSL"] = 1;
            }
            else
            {
                target["ForceDir"] = 1;
                target["UseDefAB"] = 1;
                target["AB"] = "binary";
                target["UseDefBlindUL"] = 1;
                target["BlindUL"] = 0;
                target["UseDefClientKey"] = 1;
                target["ClientKeyID"] = 0;
                target["UseDefSetFileAttr"] = 1;
                target["SetFileAttr"] = 0;
                target["DestFileAttr"] = string.Empty;
                target["UseDefPreserveFileTimestamp"] = 1;
                target["PreserveFileTimestamp"] = 0;
                target["AddCom"] = string.Empty;
                target["AddComAX"] = string.Empty;
                target["UseDefPassive"] = 1;
                target["Passive"] = 1;
                target["UseDefRsm"] = 1;
                target["Rsm"] = 1;
                target["UseDefXS"] = 1;
                target["XS"] = 0;
                target["UseDefReuseSSL"] = 1;
                target["ReuseSSL"] = 0;
            }
            return;
        }

        if (!profile.Equals(TraditionalTaskHostCapabilities.MoveItTransferWriteProfile, StringComparison.OrdinalIgnoreCase))
            return;

        // These generic/default fields are present in the native MOVEit Transfer Source/Destination
        // export supplied from Web Admin even though the MOVEit Transfer override UI does not expose
        // them. Seed them only when constructing a new/changed Host target; do not overwrite an
        // existing MOVEit Transfer Step during ordinary edits.
        if (kind.Equals("Source", StringComparison.OrdinalIgnoreCase))
        {
            target["UseDefRescanSecs"] = 1;
            target["UseDefWinCopyFileAPI"] = 1;
            target["UseWinCopyFileAPI"] = 1;
        }
        else
        {
            target["UseDefSetFileAttr"] = 1;
            target["UseDefPreserveFileTimestamp"] = 1;
            target["PreserveFileTimestamp"] = 0;
            target["UseDefWinCopyFileAPI"] = 1;
            target["UseWinCopyFileAPI"] = 1;
        }
    }

    internal static JsonObject CreateHostChangeTarget(string kind, JsonObject existingTarget)
    {
        var converted = new JsonObject();
        foreach (var field in TraditionalTaskHostCapabilities.RetainedFieldsOnHostChange(kind))
        {
            var propertyName = ExistingPropertyName(existingTarget, field);
            if (existingTarget.TryGetPropertyValue(propertyName, out var value) && value is not null)
                converted[propertyName] = value.DeepClone();
        }
        return converted;
    }

    internal static void MergeStepPatch(JsonObject target, JsonObject patch)
    {
        foreach (var pair in patch)
        {
            if (pair.Key.Equals("Parameters", StringComparison.OrdinalIgnoreCase))
            {
                target["Parameters"] = MergeParameters(GetNode(target, "Parameters"), pair.Value);
                continue;
            }
            if (pair.Key.Equals("Criteria", StringComparison.OrdinalIgnoreCase))
            {
                var propertyName = ExistingPropertyName(target, "Criteria");
                if (pair.Value is null)
                {
                    target.Remove(propertyName);
                }
                else
                {
                    ValidateSpecialFilterCriteria(pair.Value);
                    target[propertyName] = NormalizeSpecialFilterCriteriaForMoveIt(pair.Value);
                }
                continue;
            }
            target[pair.Key] = pair.Value?.DeepClone();
        }
    }

    internal static bool RequiresNativeScheduleXmlPersistence(JsonArray? requested)
        => requested?.OfType<JsonObject>().Any(item =>
            GetInt(item, "multi", 0) != 0 || GetString(item, "pairKey").Trim().Length > 0) == true;

    internal static bool TaskRestProjectionContainsNativeMulti(JsonObject task)
        => GetCollectionObjects(GetNode(task, "Schedules"), "Schedule")
            .Any(schedule => GetInt(schedule, "Multi", 0) != 0);

    internal static void ValidateScheduleCompleteness(JsonArray? requested)
    {
        if (requested is null) return;

        var scheduleNumber = 0;
        foreach (var item in requested.OfType<JsonObject>())
        {
            scheduleNumber++;
            var weekdays = (GetArray(item, "daysOfWeek") ?? new JsonArray())
                .Select(NodeString)
                .Where(value => value.Trim().Length > 0)
                .ToList();
            var monthDays = (GetArray(item, "daysOfMonth") ?? new JsonArray())
                .Select(NodeString)
                .Where(value => value.Trim().Length > 0)
                .ToList();
            var dateLists = (GetArray(item, "dateLists") ?? new JsonArray())
                .OfType<JsonObject>()
                .ToList();

            if (dateLists.Any(reference => GetString(reference, "id").Trim().Length == 0))
                throw new InvalidOperationException($"Schedule {scheduleNumber}: Select a Date List for each Custom entry or remove the incomplete entry.");

            var hasDateList = dateLists.Any(reference => GetString(reference, "id").Trim().Length > 0);
            if (weekdays.Count == 0 && monthDays.Count == 0 && !hasDateList)
                throw new InvalidOperationException($"Schedule {scheduleNumber}: Select at least one day of the week, day of the month, or Date List before saving.");
        }
    }

    internal static void ValidatePairAwareScheduleRequestShape(JsonArray? requested)
    {
        if (requested is null) throw new InvalidOperationException("Task Schedules are required.");
        var items = requested.OfType<JsonObject>().ToList();

        foreach (var item in items)
        {
            var crossing = RequestedScheduleHasCrossingInterval(item);
            var multi = GetInt(item, "multi", 0);
            var pairKey = GetString(item, "pairKey").Trim();
            if (HasRequestedDateList(item) && (multi != 0 || crossing))
                throw new InvalidOperationException("MOVEit Date List schedules must remain within the specified date and cannot span midnight. Use a separate Schedule for an overnight interval.");
            if (multi != 0 && pairKey.Length == 0)
                throw new InvalidOperationException("An unmatched MOVEit Multi Schedule segment cannot be modified. Reload the Task and edit the complete overnight Schedule pair.");
            if (crossing && multi == 0 && pairKey.Length == 0)
                throw new InvalidOperationException("An overnight Schedule must use MOVEit's native Multi=1/Multi=2 representation.");
        }

        var paired = items
            .Where(item => GetString(item, "pairKey").Trim().Length > 0)
            .GroupBy(item => GetString(item, "pairKey").Trim(), StringComparer.Ordinal);
        foreach (var group in paired)
        {
            var segments = group.ToList();
            if (segments.Count != 2)
                throw new InvalidOperationException("A paired overnight Schedule must contain exactly one before-midnight and one after-midnight segment.");
            var before = segments.SingleOrDefault(item => GetString(item, "pairRole").Equals("beforeMidnight", StringComparison.OrdinalIgnoreCase));
            var after = segments.SingleOrDefault(item => GetString(item, "pairRole").Equals("afterMidnight", StringComparison.OrdinalIgnoreCase));
            if (before is null || after is null || GetInt(before, "multi", 0) != 1 || GetInt(after, "multi", 0) != 2)
                throw new InvalidOperationException("The overnight Schedule pair does not match MOVEit's Multi=1/Multi=2 contract.");
            ValidateRequestedOvernightPair(before, after);
        }
    }

    internal static void PrepareOvernightScheduleLinkageIds(JsonObject task, JsonArray? requested)
    {
        if (requested is null) return;

        var requestedItems = requested.OfType<JsonObject>().ToList();
        var existingSchedules = GetCollectionObjects(GetNode(task, "Schedules"), "Schedule");
        var usedScheduleIds = new HashSet<int>();
        var usedIntervalIds = new HashSet<int>();

        static void AddNumericId(HashSet<int> target, string value)
        {
            if (int.TryParse((value ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
                target.Add(parsed);
        }

        foreach (var schedule in existingSchedules)
        {
            AddNumericId(usedScheduleIds, GetString(schedule, "ID"));
            var frequency = GetObject(schedule, "Frequency");
            foreach (var interval in GetDirectObjects(GetNode(frequency, "Interval")))
                AddNumericId(usedIntervalIds, GetString(interval, "ID"));
        }
        foreach (var schedule in requestedItems)
        {
            AddNumericId(usedScheduleIds, GetString(schedule, "id"));
            foreach (var interval in (GetArray(schedule, "intervals") ?? new JsonArray()).OfType<JsonObject>())
                AddNumericId(usedIntervalIds, GetString(interval, "id"));
        }

        static string NextId(HashSet<int> used, int first)
        {
            var candidate = first;
            while (used.Contains(candidate)) candidate++;
            used.Add(candidate);
            return candidate.ToString(CultureInfo.InvariantCulture);
        }

        var groups = requestedItems
            .Where(item => GetString(item, "pairKey").Trim().Length > 0)
            .GroupBy(item => GetString(item, "pairKey").Trim(), StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var segments = group.ToList();
            if (segments.Count != 2) continue; // validation below will provide the precise error
            var before = segments.SingleOrDefault(item => GetString(item, "pairRole").Equals("beforeMidnight", StringComparison.OrdinalIgnoreCase));
            var after = segments.SingleOrDefault(item => GetString(item, "pairRole").Equals("afterMidnight", StringComparison.OrdinalIgnoreCase));
            if (before is null || after is null) continue;

            var editingNativePair = GetInt(before, "serverIndex", -1) >= 0 && GetInt(after, "serverIndex", -1) >= 0;
            if (editingNativePair) continue;

            // The supplied native Web Admin exports establish that a newly-created overnight pair
            // is linked by the same Schedule ID on Multi=1/Multi=2 and the same Interval ID on each
            // corresponding interval (1001/2001 for the first observed pair).  Allocate the next
            // unused value in those native ID ranges instead of submitting unlinked Multi records.
            var scheduleId = NextId(usedScheduleIds, 1001);
            before["id"] = scheduleId;
            after["id"] = scheduleId;

            var beforeIntervals = (GetArray(before, "intervals") ?? new JsonArray()).OfType<JsonObject>().ToList();
            var afterIntervals = (GetArray(after, "intervals") ?? new JsonArray()).OfType<JsonObject>().ToList();
            for (var index = 0; index < Math.Min(beforeIntervals.Count, afterIntervals.Count); index++)
            {
                var intervalId = NextId(usedIntervalIds, 2001);
                beforeIntervals[index]["id"] = intervalId;
                afterIntervals[index]["id"] = intervalId;
            }
        }
    }

    internal static void ValidatePairAwareScheduleRequests(JsonObject task, JsonArray? requested)
    {
        if (requested is null) return;
        var items = requested.OfType<JsonObject>().ToList();
        foreach (var item in items)
        {
            var crossing = RequestedScheduleHasCrossingInterval(item);
            if (HasRequestedDateList(item) && (GetInt(item, "multi", 0) != 0 || crossing))
                throw new InvalidOperationException("MOVEit Date List schedules must remain within the specified date and cannot span midnight. Use a separate Schedule for an overnight interval.");
            if (crossing && GetInt(item, "multi", 0) == 0 && GetString(item, "pairKey").Trim().Length == 0)
                throw new InvalidOperationException("An overnight Schedule must be submitted using MOVEit's native Multi=1/Multi=2 representation.");
        }

        var paired = items
            .Where(item => GetString(item, "pairKey").Trim().Length > 0)
            .GroupBy(item => GetString(item, "pairKey").Trim(), StringComparer.Ordinal);
        var existing = GetCollectionObjects(GetNode(task, "Schedules"), "Schedule");

        foreach (var group in paired)
        {
            var segments = group.ToList();
            if (segments.Count != 2)
                throw new InvalidOperationException("A paired overnight Schedule must contain exactly one before-midnight and one after-midnight segment.");

            var before = segments.SingleOrDefault(item => GetString(item, "pairRole").Equals("beforeMidnight", StringComparison.OrdinalIgnoreCase));
            var after = segments.SingleOrDefault(item => GetString(item, "pairRole").Equals("afterMidnight", StringComparison.OrdinalIgnoreCase));
            if (before is null || after is null || GetInt(before, "multi", 0) != 1 || GetInt(after, "multi", 0) != 2)
                throw new InvalidOperationException("The overnight Schedule pair does not match MOVEit's Multi=1/Multi=2 contract.");

            ValidateRequestedOvernightPair(before, after);

            var beforeIndex = GetInt(before, "serverIndex", -1);
            var afterIndex = GetInt(after, "serverIndex", -1);
            var editingNativePair = beforeIndex >= 0 && afterIndex >= 0;
            var convertingNormalSchedule = beforeIndex >= 0 && afterIndex < 0;
            var creatingNewPair = beforeIndex < 0 && afterIndex < 0;

            if (!editingNativePair && !convertingNormalSchedule && !creatingNewPair)
                throw new InvalidOperationException("The overnight Schedule pair has an invalid server identity combination.");

            if (editingNativePair)
            {
                if (beforeIndex >= existing.Count || afterIndex >= existing.Count)
                    throw new InvalidOperationException("The native overnight Schedule changed before Save. Reload the Task and try again.");
                if (afterIndex != beforeIndex + 1)
                    throw new InvalidOperationException("MOVEit's native Multi=1/Multi=2 Schedule segments must remain adjacent in the Task definition.");

                var beforeTemplate = existing[beforeIndex];
                var afterTemplate = existing[afterIndex];
                ValidateNativeOvernightPair(beforeTemplate, afterTemplate);
                ValidateScheduleIdentity(before, beforeTemplate, "before-midnight");
                ValidateScheduleIdentity(after, afterTemplate, "after-midnight");
            }
            else if (convertingNormalSchedule)
            {
                if (beforeIndex >= existing.Count)
                    throw new InvalidOperationException("The Schedule changed before Save. Reload the Task and try again.");
                var template = existing[beforeIndex];
                if (GetInt(template, "Multi", 0) != 0)
                    throw new InvalidOperationException("Only a normal MOVEit Schedule can be converted into a new overnight pair through this path.");
                ValidateScheduleSourceIdentity(before, template);
                if ((GetArray(before, "intervals") ?? new JsonArray()).OfType<JsonObject>().Count() != 1)
                    throw new InvalidOperationException("The captured native MOVEit overnight creation pattern contains one linked time interval.");
            }
            else
            {
                // Native Web Admin exports use a shared Schedule ID and shared Interval ID to link
                // the Multi=1/Multi=2 halves (for example 1001 / 2001). PrepareOvernightScheduleLinkageIds
                // assigns those linkage IDs before validation for newly-created pairs.
                if ((GetArray(before, "intervals") ?? new JsonArray()).OfType<JsonObject>().Count() != 1)
                    throw new InvalidOperationException("The captured native MOVEit overnight creation pattern contains one linked time interval.");
            }
        }
    }

    internal static void ValidateRequestedOvernightPair(JsonObject before, JsonObject after)
    {
        if (HasRequestedDateList(before) || HasRequestedDateList(after))
            throw new InvalidOperationException("MOVEit Date List schedules must remain within the specified date and cannot span midnight.");

        var beforeMonthDays = RequestStrings(before, "daysOfMonth");
        var expectedAfterMonthDays = ShiftScheduleMonthDaysForward(beforeMonthDays);
        var afterMonthDays = RequestStrings(after, "daysOfMonth");
        if (!expectedAfterMonthDays.SequenceEqual(afterMonthDays, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("The post-midnight Schedule segment must use the following day-of-month value(s), matching MOVEit's native overnight representation.");

        var beforeWeekdays = RequestStrings(before, "daysOfWeek");
        var expectedAfterWeekdays = ShiftScheduleWeekdaysForward(beforeWeekdays);
        var afterWeekdays = RequestStrings(after, "daysOfWeek");
        if (!expectedAfterWeekdays.SequenceEqual(afterWeekdays, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("The post-midnight Schedule segment must use the following weekday(s), matching MOVEit's native overnight representation.");

        if (GetBool(before, "onlyUntilFirstSuccess") != GetBool(after, "onlyUntilFirstSuccess")
            || GetBool(before, "failIfNoSuccessInSchedule") != GetBool(after, "failIfNoSuccessInSchedule")
            || GetBool(before, "runEvenIfNotifications") != GetBool(after, "runEvenIfNotifications"))
            throw new InvalidOperationException("Both segments of an overnight Schedule must use the same Schedule options.");

        var beforeId = GetString(before, "id").Trim();
        var afterId = GetString(after, "id").Trim();
        if (beforeId.Length > 0 && afterId.Length > 0 && !beforeId.Equals(afterId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Both native overnight Schedule segments must share the same Schedule ID when an ID is present.");

        var beforeIntervals = (GetArray(before, "intervals") ?? new JsonArray()).OfType<JsonObject>().ToList();
        var afterIntervals = (GetArray(after, "intervals") ?? new JsonArray()).OfType<JsonObject>().ToList();
        if (beforeIntervals.Count == 0 || beforeIntervals.Count != afterIntervals.Count)
            throw new InvalidOperationException("Both segments of an overnight Schedule must contain matching time intervals.");

        for (var index = 0; index < beforeIntervals.Count; index++)
        {
            var pre = beforeIntervals[index];
            var post = afterIntervals[index];
            var everyMinutes = GetInt(pre, "everyMinutes", 0);
            if (everyMinutes <= 0 || everyMinutes != GetInt(post, "everyMinutes", 0))
                throw new InvalidOperationException("Both segments of an overnight Schedule must use the same positive Repeat interval.");

            if (!TryScheduleMinutes(GetString(pre, "startTime"), out var start)
                || !TryScheduleMinutes(GetString(pre, "endTime"), out var preEnd)
                || !TryScheduleMinutes(GetString(post, "startTime"), out var postStart)
                || !TryScheduleMinutes(GetString(post, "endTime"), out var end))
                throw new InvalidOperationException("An overnight Schedule contains an invalid time value.");

            var expectedPostStart = OvernightPostStartMinutes(start, everyMinutes);
            if (preEnd != 1439 || postStart != expectedPostStart)
                throw new InvalidOperationException($"MOVEit's native overnight representation ends the first segment at 23:59 and continues the {everyMinutes}-minute repeat cadence after midnight at {FormatScheduleMinutes(expectedPostStart)}.");
            if (start <= end)
                throw new InvalidOperationException("An overnight Schedule End Time must be earlier than its Start Time.");

            var preId = GetString(pre, "id").Trim();
            var postId = GetString(post, "id").Trim();
            if (preId.Length > 0 && postId.Length > 0 && !preId.Equals(postId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Both native overnight interval segments must share the same Interval ID when an ID is present.");
        }
    }

    internal static void ValidateScheduleIdentity(JsonObject requested, JsonObject template, string label)
    {
        var requestedId = GetString(requested, "id").Trim();
        var templateId = GetString(template, "ID").Trim();
        if (requestedId.Length > 0 && templateId.Length > 0 && !requestedId.Equals(templateId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The native overnight Schedule {label} identity changed before Save. Reload the Task before editing it again.");
    }

    internal static void ValidateScheduleSourceIdentity(JsonObject requested, JsonObject template)
    {
        // Conversion to an overnight pair receives a new native linkage ID, so validate the original
        // normal Schedule identity separately from the generated Multi pair ID.
        var sourceId = GetString(requested, "sourceId").Trim();
        var templateId = GetString(template, "ID").Trim();
        if (sourceId.Length > 0 && templateId.Length > 0 && !sourceId.Equals(templateId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Schedule being converted changed before Save. Reload the Task before editing it again.");
    }

    internal static IReadOnlyList<string> ShiftScheduleWeekdaysForward(IReadOnlyList<string> days)
    {
        var order = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
        var shifted = new List<string>();
        foreach (var value in days)
        {
            var text = value.Trim();
            var index = Array.FindIndex(order, day => day.Equals(text, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                throw new InvalidOperationException($"The overnight Schedule contains an unsupported weekday value '{value}'.");
            shifted.Add(order[(index + 1) % order.Length]);
        }
        return shifted;
    }

    internal static IReadOnlyList<string> ShiftScheduleMonthDaysForward(IReadOnlyList<string> days)
    {
        var shifted = new List<string>();
        foreach (var value in days)
        {
            if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var day)
                || day < 1 || day > 31)
                throw new InvalidOperationException($"The overnight Schedule contains an unsupported day-of-month value '{value}'.");

            // Native MOVEit stores the post-midnight half on the following DayOfMonth.
            // Day 31 rolls to day 1, just as weekday Sunday rolls to Monday.
            shifted.Add((day == 31 ? 1 : day + 1).ToString(CultureInfo.InvariantCulture));
        }
        return shifted;
    }

    internal static void ValidateNativeOvernightPair(JsonObject before, JsonObject after)
    {
        if (GetInt(before, "Multi", 0) != 1 || GetInt(after, "Multi", 0) != 2)
            throw new InvalidOperationException("The native Schedule templates are not a MOVEit Multi=1/Multi=2 overnight pair.");

        if (GetDirectObjects(GetNode(before, "DateListRef")).Count > 0 || GetDirectObjects(GetNode(after, "DateListRef")).Count > 0)
            throw new InvalidOperationException("MOVEit Date List schedules must remain within the specified date and cannot span midnight.");

        var beforeDays = GetObject(before, "Days");
        var afterDays = GetObject(after, "Days");
        var beforeMonthDays = GetStringValues(GetNode(beforeDays, "DayOfMonth"));
        var expectedAfterMonthDays = ShiftScheduleMonthDaysForward(beforeMonthDays);
        var afterMonthDays = GetStringValues(GetNode(afterDays, "DayOfMonth"));
        if (!expectedAfterMonthDays.SequenceEqual(afterMonthDays, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("The native Multi segments do not use MOVEit's captured following-day DayOfMonth rollover pattern.");

        var beforeWeekdays = GetStringValues(GetNode(beforeDays, "DayOfWeek"));
        var expectedAfterWeekdays = ShiftScheduleWeekdaysForward(beforeWeekdays);
        var afterWeekdays = GetStringValues(GetNode(afterDays, "DayOfWeek"));
        if (!expectedAfterWeekdays.SequenceEqual(afterWeekdays, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("The native Multi segments do not use MOVEit's captured following-day weekday rollover pattern.");

        if (GetBool(before, "OnlyUntilFirstSuccess") != GetBool(after, "OnlyUntilFirstSuccess")
            || GetBool(before, "FailIfNoSuccessInSched") != GetBool(after, "FailIfNoSuccessInSched")
            || GetBool(before, "RunEvenIfNotif") != GetBool(after, "RunEvenIfNotif"))
            throw new InvalidOperationException("The native Multi segments do not have matching Schedule options.");

        var beforeId = GetString(before, "ID").Trim();
        var afterId = GetString(after, "ID").Trim();
        if (beforeId.Length > 0 && afterId.Length > 0 && !beforeId.Equals(afterId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The native Multi segments do not share the same Schedule ID.");

        var beforeIntervals = GetScheduleIntervals(before);
        var afterIntervals = GetScheduleIntervals(after);
        if (beforeIntervals.Count == 0 || beforeIntervals.Count != afterIntervals.Count)
            throw new InvalidOperationException("The native Multi segments do not contain matching time intervals.");

        for (var index = 0; index < beforeIntervals.Count; index++)
        {
            var pre = beforeIntervals[index];
            var post = afterIntervals[index];
            var every = GetInt(pre, "EveryMinutes", 0);
            if (every <= 0 || every != GetInt(post, "EveryMinutes", 0))
                throw new InvalidOperationException("The native Multi segments do not use the same positive Repeat interval.");

            if (!TryScheduleMinutes(GetString(pre, "StartTime"), out var start)
                || !TryScheduleMinutes(GetString(pre, "EndTime"), out var preEnd)
                || !TryScheduleMinutes(GetString(post, "StartTime"), out var postStart)
                || !TryScheduleMinutes(GetString(post, "EndTime"), out var end))
                throw new InvalidOperationException("The native overnight Schedule contains an invalid time value.");

            var expectedPostStart = OvernightPostStartMinutes(start, every);
            if (preEnd != 1439 || postStart != expectedPostStart || start <= end)
                throw new InvalidOperationException("The native Multi segments do not match MOVEit's captured midnight split and repeat-cadence continuation.");

            var preId = GetString(pre, "ID").Trim();
            var postId = GetString(post, "ID").Trim();
            if (preId.Length > 0 && postId.Length > 0 && !preId.Equals(postId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The native Multi interval segments do not share the same Interval ID.");
        }
    }

    internal static IReadOnlyList<JsonObject> GetScheduleIntervals(JsonObject schedule)
    {
        var frequency = GetNode(schedule, "Frequency");
        return frequency is JsonObject frequencyObject
            ? GetDirectObjects(GetNode(frequencyObject, "Interval"))
            : GetDirectObjects(frequency);
    }

    internal static IReadOnlyList<string> RequestStrings(JsonObject item, string name)
        => (GetArray(item, name) ?? new JsonArray())
            .Select(NodeString)
            .Where(value => value.Length > 0)
            .ToList();

    internal static bool RequestedScheduleHasCrossingInterval(JsonObject item)
    {
        foreach (var interval in (GetArray(item, "intervals") ?? new JsonArray()).OfType<JsonObject>())
        {
            if (GetInt(interval, "everyMinutes", 0) <= 0) continue;
            if (TryScheduleMinutes(GetString(interval, "startTime"), out var start)
                && TryScheduleMinutes(GetString(interval, "endTime"), out var end)
                && start > end)
                return true;
        }
        return false;
    }

    internal static bool HasRequestedDateList(JsonObject item)
        => (GetArray(item, "dateLists") ?? new JsonArray())
            .OfType<JsonObject>()
            .Any(reference => GetString(reference, "id").Trim().Length > 0);

    internal static int OvernightPostStartMinutes(int startMinutes, int everyMinutes)
    {
        if (startMinutes < 0 || startMinutes >= 1440) throw new ArgumentOutOfRangeException(nameof(startMinutes));
        if (everyMinutes <= 0) throw new ArgumentOutOfRangeException(nameof(everyMinutes));
        var stepsToMidnight = (int)Math.Ceiling((1440 - startMinutes) / (double)everyMinutes);
        return (startMinutes + (stepsToMidnight * everyMinutes)) % 1440;
    }

    internal static string FormatScheduleMinutes(int minutes)
        => $"{minutes / 60:00}:{minutes % 60:00}";

    internal static bool TryScheduleMinutes(string value, out int minutes)
    {
        minutes = 0;
        var normalized = NormalizeTime(value);
        if (!TimeOnly.TryParseExact(normalized, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return false;
        minutes = parsed.Hour * 60 + parsed.Minute;
        return true;
    }

    internal static void ApplySchedules(JsonObject task, JsonArray? requested)
    {
        if (requested is null) throw new InvalidOperationException("Task Schedules are required.");

        // Preserve the collection/container shape returned by this MOVEit server. The REST contract
        // explicitly prescribes GET -> edit -> PUT; the editor must not normalize a server-returned
        // direct-array/single-item representation into a different JSON shape on write.
        var existingNode = GetNode(task, "Schedules");
        var existing = GetCollectionObjects(existingNode, "Schedule");
        var consumed = new HashSet<int>();
        var output = new JsonArray();

        foreach (var node in requested)
        {
            if (node is not JsonObject item) continue;
            var id = GetString(item, "id").Trim();
            var index = FindByIdOrIndex(existing, consumed, id, GetInt(item, "serverIndex", GetInt(item, "index", -1)));
            var cloneFromIndex = GetInt(item, "cloneFromServerIndex", -1);
            var schedule = index >= 0
                ? (JsonObject)existing[index].DeepClone()
                : (cloneFromIndex >= 0 && cloneFromIndex < existing.Count
                    ? (JsonObject)existing[cloneFromIndex].DeepClone()
                    : new JsonObject());
            if (index >= 0) consumed.Add(index);

            if (id.Length > 0) schedule[ExistingPropertyName(schedule, "ID")] = id;
            else schedule.Remove(ExistingPropertyName(schedule, "ID"));

            var multi = GetInt(item, "multi", 0);
            if (multi == 0) schedule.Remove(ExistingPropertyName(schedule, "Multi"));
            else schedule[ExistingPropertyName(schedule, "Multi")] = multi;

            var requestedWeekdays = GetArray(item, "daysOfWeek");
            var requestedMonthDays = GetArray(item, "daysOfMonth");
            var weekdays = ToStringArray(requestedWeekdays);
            var monthDays = ToIntArray(requestedMonthDays);
            if (weekdays.Count > 0 || monthDays.Count > 0)
            {
                var days = GetObject(schedule, "Days") ?? new JsonObject();
                SetOrRemoveCollectionProperty(days, "DayOfWeek", weekdays);
                SetOrRemoveCollectionProperty(days, "DayOfMonth", monthDays);
                schedule[ExistingPropertyName(schedule, "Days")] = days;
            }
            else
            {
                schedule.Remove(ExistingPropertyName(schedule, "Days"));
            }

            var existingRefs = GetDirectObjects(GetNode(schedule, "DateListRef"));
            var dateListRefs = new JsonArray();
            foreach (var referenceNode in GetArray(item, "dateLists") ?? new JsonArray())
            {
                if (referenceNode is not JsonObject reference) continue;
                var dateListId = GetString(reference, "id").Trim();
                if (dateListId.Length == 0) continue;
                var existingRef = existingRefs.FirstOrDefault(candidate => GetString(candidate, "ID").Equals(dateListId, StringComparison.OrdinalIgnoreCase));
                var refObject = existingRef is null ? new JsonObject() : (JsonObject)existingRef.DeepClone();
                refObject[ExistingPropertyName(refObject, "ID")] = dateListId;
                SetTaskPutFlag(refObject, "Run", GetBool(reference, "run"));
                dateListRefs.Add(refObject);
            }
            if (dateListRefs.Count > 0)
                schedule[ExistingPropertyName(schedule, "DateListRef")] = PreserveListShape(GetNode(schedule, "DateListRef"), dateListRefs);
            else
                schedule.Remove(ExistingPropertyName(schedule, "DateListRef"));

            var frequency = GetObject(schedule, "Frequency") ?? new JsonObject();
            var existingIntervals = GetDirectObjects(GetNode(frequency, "Interval"));
            var intervals = new JsonArray();
            var intervalOrdinal = 0;
            foreach (var intervalNode in GetArray(item, "intervals") ?? new JsonArray())
            {
                if (intervalNode is not JsonObject interval) continue;
                var intervalId = GetString(interval, "id").Trim();
                var intervalServerIndex = GetInt(interval, "serverIndex", intervalOrdinal);
                var existingInterval = existingIntervals.FirstOrDefault(candidate => intervalId.Length > 0 && GetString(candidate, "ID").Equals(intervalId, StringComparison.OrdinalIgnoreCase))
                    ?? (intervalServerIndex >= 0 && intervalServerIndex < existingIntervals.Count ? existingIntervals[intervalServerIndex] : null);
                var intervalJson = existingInterval is null ? new JsonObject() : (JsonObject)existingInterval.DeepClone();
                if (intervalId.Length > 0) intervalJson[ExistingPropertyName(intervalJson, "ID")] = intervalId;
                else intervalJson.Remove(ExistingPropertyName(intervalJson, "ID"));
                var startTime = NormalizeTime(GetString(interval, "startTime"));
                var endTime = NormalizeTime(GetString(interval, "endTime"));
                if (!TryScheduleMinutes(startTime, out _))
                    throw new InvalidOperationException("Schedule Start Time must be a valid time.");
                if (!TryScheduleMinutes(endTime, out _))
                    throw new InvalidOperationException("Schedule End Time must be a valid time.");
                intervalJson[ExistingPropertyName(intervalJson, "StartTime")] = startTime;
                intervalJson[ExistingPropertyName(intervalJson, "EndTime")] = endTime;
                var everyMinutes = Math.Max(0, GetInt(interval, "everyMinutes", 0));
                // MOVEit's own add-task example omits EveryMinutes for a one-time interval; the
                // exported configuration contains it only for a repeating interval.
                if (everyMinutes > 0) intervalJson[ExistingPropertyName(intervalJson, "EveryMinutes")] = everyMinutes;
                else intervalJson.Remove(ExistingPropertyName(intervalJson, "EveryMinutes"));
                intervals.Add(intervalJson);
                intervalOrdinal++;
            }
            frequency[ExistingPropertyName(frequency, "Interval")] = PreserveListShape(GetNode(frequency, "Interval"), intervals, preferArrayWhenUnknown: true);
            schedule[ExistingPropertyName(schedule, "Frequency")] = frequency;

            SetTaskPutFlag(schedule, "OnlyUntilFirstSuccess", GetBool(item, "onlyUntilFirstSuccess"));
            SetTaskPutFlag(schedule, "FailIfNoSuccessInSched", GetBool(item, "failIfNoSuccessInSchedule"));
            SetTaskPutFlag(schedule, "RunEvenIfNotif", GetBool(item, "runEvenIfNotifications"));
            output.Add(schedule);
        }

        task[ExistingPropertyName(task, "Schedules")] = PreserveNamedCollectionShape(existingNode, "Schedule", output);
    }

    internal static void ApplyNextActions(JsonObject task, JsonArray? requested)
    {
        if (requested is null) throw new InvalidOperationException("Task Next Actions are required.");
        var existingNode = GetNode(task, "NextActions");
        var existing = GetCollectionObjects(existingNode, "NextAction");
        var consumed = new HashSet<int>();
        var output = new JsonArray();

        foreach (var node in requested)
        {
            if (node is not JsonObject item) continue;
            var type = GetString(item, "type").Trim();
            var id = GetString(item, "id").Trim();
            var index = FindByIdOrIndex(existing, consumed, id, GetInt(item, "serverIndex", GetInt(item, "index", -1)));
            var cloneFromIndex = GetInt(item, "cloneFromServerIndex", -1);
            var action = index >= 0
                ? (JsonObject)existing[index].DeepClone()
                : (cloneFromIndex >= 0 && cloneFromIndex < existing.Count
                    ? (JsonObject)existing[cloneFromIndex].DeepClone()
                    : new JsonObject());
            if (index >= 0) consumed.Add(index);

            if (!type.Equals("Email", StringComparison.OrdinalIgnoreCase) && !type.Equals("Task", StringComparison.OrdinalIgnoreCase))
            {
                if (index >= 0) output.Add(action);
                continue;
            }

            if (id.Length > 0) action[ExistingPropertyName(action, "ID")] = id;
            else action.Remove(ExistingPropertyName(action, "ID"));
            action["Type"] = type.Equals("Email", StringComparison.OrdinalIgnoreCase) ? "Email" : "Task";
            action["DoAfter"] = FirstNonBlank(GetString(item, "doAfter"), "Task");
            action["DoIfFailure"] = Flag(GetBool(item, "doIfFailure"));
            action["DoIfNoAction"] = Flag(GetBool(item, "doIfNoAction"));
            action["DoIfSuccess"] = Flag(GetBool(item, "doIfSuccess"));

            if (type.Equals("Email", StringComparison.OrdinalIgnoreCase))
            {
                var hostId = GetString(item, "hostId").Trim();
                if (hostId.Length == 0) throw new InvalidOperationException("Every Email Next Action requires an SMTP Host.");
                if (GetString(item, "addressTo").Trim().Length == 0) throw new InvalidOperationException("Every Email Next Action requires at least one recipient.");
                action["HostID"] = hostId;
                action["AddressTo"] = NormalizeRecipients(GetString(item, "addressTo"));
                action["Subject"] = GetString(item, "subject");
                action["Message"] = GetString(item, "message");
                action["IsHTML"] = Flag(GetBool(item, "isHtml"));
            }
            else
            {
                var runTaskId = GetString(item, "taskId").Trim();
                if (runTaskId.Length == 0) throw new InvalidOperationException("Every Run Task Next Action requires a Task.");
                if (runTaskId.Equals(GetString(task, "ID").Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("A Run Task Next Action cannot run the current task.");
                action["TaskID"] = runTaskId;
                if (GetNode(item, "parameters") is not null)
                    action["Parameters"] = MergeParameters(GetNode(action, "Parameters"), GetNode(item, "parameters"), useNativeTaskPutShape: true);
            }
            output.Add(action);
        }

        task[ExistingPropertyName(task, "NextActions")] = PreserveNamedCollectionShape(existingNode, "NextAction", output);
    }

    internal static JsonNode MergeParameters(JsonNode? existingNode, JsonNode? requestedNode, bool useNativeTaskPutShape = false)
    {
        var existingItems = ParseParameterObjects(existingNode);
        var requestedItems = ParseParameterObjects(requestedNode);
        ValidateRequestedParameters(requestedItems);
        var replace = requestedNode is JsonObject requestedObject && GetBool(requestedObject, "replace");
        if (requestedItems.Count == 0 && !replace)
            return existingNode?.DeepClone() ?? new JsonObject();

        // Top-level Task Parameters use the native Web Admin PUT shape captured from MOVEit 2025.1.
        // Run Task Next Action parameters use the same native Parameter[] shape evidenced by the captured
        // MOVEit export. Process parameter collections retain their existing server-returned shape until
        // an equivalent native Process payload is captured.
        var result = replace
            ? new List<JsonObject>()
            : existingItems.Select(item => useNativeTaskPutShape
                ? CanonicalizeParameterForPut(item, ParameterName(item))
                : (JsonObject)item.DeepClone()).ToList();

        foreach (var requested in requestedItems)
        {
            var name = FirstNonBlank(GetString(requested, "Name"), GetString(requested, "name")).Trim();
            if (name.Length == 0) continue;

            var originalName = FirstNonBlank(GetString(requested, "originalName"), name).Trim();
            var existing = existingItems.FirstOrDefault(item => ParameterName(item).Equals(originalName, StringComparison.OrdinalIgnoreCase))
                ?? existingItems.FirstOrDefault(item => ParameterName(item).Equals(name, StringComparison.OrdinalIgnoreCase));

            JsonObject replacement;
            if (GetBool(requested, "preserve"))
            {
                if (existing is null)
                    throw new InvalidOperationException($"Secure parameter {originalName} could not be preserved because it no longer exists in the current MOVEit definition.");
                replacement = useNativeTaskPutShape
                    ? CanonicalizeParameterForPut(existing, name)
                    : (JsonObject)existing.DeepClone();
                if (!useNativeTaskPutShape && !name.Equals(originalName, StringComparison.OrdinalIgnoreCase))
                    SetParameterNameLikeServer(replacement, name, existingNode);
            }
            else if (useNativeTaskPutShape)
            {
                replacement = CanonicalizeParameterForPut(
                    existing ?? new JsonObject(),
                    name,
                    secureOverride: GetBool(requested, "secure"),
                    valueOverride: GetString(requested, "value"),
                    overrideValue: true);
            }
            else
            {
                replacement = existing is null ? new JsonObject() : (JsonObject)existing.DeepClone();
                SetParameterNameLikeServer(replacement, name, existingNode);
                replacement[ExistingPropertyName(replacement, "value")] = GetString(requested, "value");
                RemoveParameterMember(replacement, "DisplayValue");
                RemoveParameterMember(replacement, "countedDisplayValue");
                SetTaskPutFlag(replacement, "secure", GetBool(requested, "secure"));
            }

            var index = result.FindIndex(item => ParameterName(item).Equals(originalName, StringComparison.OrdinalIgnoreCase)
                || ParameterName(item).Equals(name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) result[index] = replacement; else result.Add(replacement);
        }

        if (!useNativeTaskPutShape)
        {
            // MOVEit 2025.1 requires numeric secure flags on Task PUT even when GET returns strings.
            foreach (var parameter in result)
                parameter[ExistingPropertyName(parameter, "secure")] = Flag(IsSecureParameter(parameter));
            return BuildParameterContainerLikeServer(existingNode, result);
        }

        return BuildNativeTaskParameterContainer(result);
    }

    internal static List<JsonObject> ParseParameterObjects(JsonNode? root)
    {
        if (root is JsonObject container)
        {
            var parameter = GetNode(container, "Parameter");
            if (parameter is not null) root = parameter;
            else
            {
                var any = GetNode(container, "any");
                if (any is not null) root = any;
            }
        }

        if (root is JsonArray array) return array.OfType<JsonObject>().ToList();
        if (root is JsonObject single)
        {
            if (ParameterName(single).Length > 0) return new List<JsonObject> { single };

            // Tolerate older keyed read shapes such as { ParmName:{ value, DisplayValue } }.
            var result = new List<JsonObject>();
            foreach (var pair in single)
            {
                if (pair.Value is not JsonObject value) continue;
                var clone = (JsonObject)value.DeepClone();
                if (ParameterName(clone).Length == 0) clone["Name"] = pair.Key;
                result.Add(clone);
            }
            return result;
        }
        return new List<JsonObject>();
    }


    internal static void VerifyTaskFields(JsonObject expected, JsonObject actual)
    {
        VerifyTextValue("Task Name", GetString(expected, "name").Trim(), GetString(actual, "Name").Trim());
        VerifyFlagValue("Task Enabled state", GetBool(expected, "enabled"), actual, "Active");

        var info = GetObject(actual, "Info");
        VerifyTextValue("Task Description", GetString(expected, "description"), GetString(info, "Description"));
        VerifyTextValue("Task Operator Notes", GetString(expected, "operatorNotes"), GetString(info, "Notes"));
        // A server projection may omit the default; an explicit orig must still read back as orig.
        VerifyTextValue("Task Cache Names", ResolveCacheNames(GetString(expected, "cacheNames")), ResolveCacheNames(GetString(actual, "CacheNames")));
        VerifyFlagValue("Task state-caching default", GetBool(expected, "useDefaultStateCaching", true), actual, "UseDefStateCaching");
        VerifyFlagValue("Task Auto Rerun", GetBool(expected, "autoRerun"), actual, "AR");

        if (!GetBool(expected, "useDefaultStateCaching", true))
        {
            VerifyOptionalNumber("Task State Cache Time", GetNode(expected, "stateCacheTime"), GetNode(actual, "StateCacheTime"));
            if (GetInt(expected, "stateCacheTime", 0) > 0)
            {
                var expectedUnit = GetString(expected, "stateCacheUnit").Trim();
                if (expectedUnit.Length == 0) expectedUnit = "Minutes";
                VerifyTextValue("Task State Cache Unit", expectedUnit, FirstNonBlank(GetString(actual, "StateCacheUnit"), "Minutes"));
            }
        }

        // MOVEit represents the global Task Log retention choice by omitting NumTaskLogsToKeep on write.
        // Some server versions may materialize an effective value on read, so only verify the explicit
        // per-Task override case; the global case is defined by the write contract, not a separate flag.
        if (!GetBool(expected, "useGlobalTaskLogRetention", true))
            VerifyOptionalNumber("Task Logs To Keep", GetNode(expected, "taskLogsToKeep"), GetNode(actual, "NumTaskLogsToKeep"));
    }

    internal static void VerifySteps(JsonArray? requested, JsonNode? persistedNode)
    {
        if (requested is null) return;
        var expectedItems = requested.OfType<JsonObject>().ToList();
        var persisted = GetStepObjects(persistedNode);
        if (persisted.Count != expectedItems.Count)
            throw new InvalidOperationException("MOVEit accepted the Task update but did not persist the requested Step collection.");

        var consumed = new HashSet<int>();
        foreach (var item in expectedItems)
        {
            var patch = GetObject(item, "patch");
            if (patch is null) continue;

            var kind = NormalizeStepKind(GetString(item, "kind"));
            var id = GetString(item, "id").Trim();
            var index = FindStep(persisted, consumed, kind, id, GetInt(item, "index", -1));
            if (index < 0)
                throw new InvalidOperationException($"MOVEit accepted the Task update but did not persist the requested {kind} Step.");

            consumed.Add(index);
            var actual = GetObject(persisted[index], kind)
                ?? throw new InvalidOperationException($"MOVEit accepted the Task update but did not return the requested {kind} Step.");
            VerifyStepPatch(kind, patch, actual);
        }
    }

    internal static void VerifyStepPatch(string kind, JsonObject expected, JsonObject actual)
    {
        foreach (var pair in expected)
        {
            if (pair.Key.Equals("ID", StringComparison.OrdinalIgnoreCase)) continue;
            if (pair.Key.Equals("Parameters", StringComparison.OrdinalIgnoreCase))
            {
                VerifyParameters(pair.Value, GetNode(actual, "Parameters"));
                continue;
            }
            if (pair.Key.Equals("Criteria", StringComparison.OrdinalIgnoreCase))
            {
                VerifySpecialFilterCriteria(pair.Value, GetNode(actual, "Criteria"));
                continue;
            }

            var actualNode = GetNode(actual, pair.Key);
            if (actualNode is null && pair.Key.Equals("Path", StringComparison.OrdinalIgnoreCase))
                actualNode = GetNode(actual, "FolderName");

            // These are protected Step values. MOVEit may return an encrypted, suppressed, or
            // otherwise normalized representation after PUT, so do not compare plaintext input or
            // require the secret to be visible on the authoritative GET. The surrounding authentication
            // mode, identity, and non-secret fields are still verified normally.
            if (pair.Key.Equals("Password", StringComparison.OrdinalIgnoreCase)
                || pair.Key.Equals("OAuthClientSecret", StringComparison.OrdinalIgnoreCase)
                || pair.Key.Equals("ClientSecret", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!EquivalentScalar(pair.Key, pair.Value, actualNode))
                throw new InvalidOperationException($"MOVEit accepted the Task update but did not persist {kind} field {pair.Key}.");
        }
    }

    internal static void ValidateSpecialFilterCriteria(JsonNode criteriaNode)
    {
        var parsed = ParseSpecialFilterCriteria(criteriaNode);
        if (parsed is null)
            throw new InvalidOperationException("Special Filter criteria are not in a supported MOVEit Source format.");
        if (parsed.Value.Comparisons.Count == 0)
            throw new InvalidOperationException("At least one Special Filter criterion is required.");
        if (parsed.Value.Comparisons.Count > 6)
            throw new InvalidOperationException("MOVEit Source Special Filters support up to six criteria.");

        foreach (var comparison in parsed.Value.Comparisons)
        {
            var left = comparison.A;
            var test = comparison.Test.ToUpperInvariant();
            var right = comparison.B;
            if (left.Equals("[FileSize]", StringComparison.OrdinalIgnoreCase))
            {
                if (test is not ("NEQ" or "NNE" or "NGT" or "NGE" or "NLT" or "NLE"))
                    throw new InvalidOperationException("The selected File Size comparison is not supported by MOVEit Source Special Filters.");
                if (!long.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes) || bytes < 0)
                    throw new InvalidOperationException("File Size Special Filter values must resolve to a non-negative byte count.");
                continue;
            }

            if (left.Equals("[FileDateStamp]", StringComparison.OrdinalIgnoreCase))
            {
                if (test is not ("DGT" or "DLT"))
                    throw new InvalidOperationException("The selected File Modified Timestamp comparison is not supported by the native Source filter editor.");
                if (!Regex.IsMatch(right, @"^\[DateSubtract\(\[Now\],\d+[HDMY]\)\]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    throw new InvalidOperationException("File Modified Timestamp Special Filters must use the native relative-time format.");
                continue;
            }

            throw new InvalidOperationException("Only File Size and File Modified Timestamp are supported by MOVEit Source Special Filters.");
        }
    }

    internal static JsonNode NormalizeSpecialFilterCriteriaForMoveIt(JsonNode criteriaNode)
    {
        var parsed = ParseSpecialFilterCriteria(criteriaNode)
            ?? throw new InvalidOperationException("Special Filter criteria are not in a supported MOVEit Source format.");

        JsonObject Comparison((string A, string Test, string B) value) => new()
        {
            ["a"] = value.A,
            ["test"] = value.Test,
            ["b"] = value.B
        };

        // MOVEit's schema requires And/Or to contain at least two <comp> entries. A single
        // criterion is represented directly as Criteria.comp instead of a one-item group.
        if (parsed.Comparisons.Count == 1)
            return new JsonObject { ["comp"] = Comparison(parsed.Comparisons[0]) };

        var comparisons = new JsonArray(parsed.Comparisons
            .Select(value => (JsonNode?)Comparison(value))
            .ToArray());
        var groupName = parsed.Mode.Equals("any", StringComparison.OrdinalIgnoreCase) ? "Or" : "And";
        return new JsonObject { [groupName] = new JsonObject { ["comp"] = comparisons } };
    }

    internal static void VerifySpecialFilterCriteria(JsonNode? expected, JsonNode? actual)
    {
        if (expected is null)
        {
            if (actual is null || actual is JsonObject emptyCriteria && emptyCriteria.Count == 0) return;
            throw new InvalidOperationException("MOVEit accepted the Task update but did not remove the requested Source Special Filters.");
        }

        var expectedParsed = ParseSpecialFilterCriteria(expected);
        var actualParsed = ParseSpecialFilterCriteria(actual);
        if (expectedParsed is null || actualParsed is null
            || expectedParsed.Value.Comparisons.Count != actualParsed.Value.Comparisons.Count)
            throw new InvalidOperationException("MOVEit accepted the Task update but did not persist the requested Source Special Filters.");

        // A single criterion has no meaningful all/any distinction because MOVEit's wire shape
        // does not wrap it in And/Or. Multi-criterion saves must preserve the requested grouping.
        if (expectedParsed.Value.Comparisons.Count > 1
            && !expectedParsed.Value.Mode.Equals(actualParsed.Value.Mode, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("MOVEit accepted the Task update but changed the requested Source Special Filter grouping.");

        for (var i = 0; i < expectedParsed.Value.Comparisons.Count; i++)
        {
            var expectedComparison = expectedParsed.Value.Comparisons[i];
            var actualComparison = actualParsed.Value.Comparisons[i];
            if (!expectedComparison.A.Equals(actualComparison.A, StringComparison.OrdinalIgnoreCase)
                || !expectedComparison.Test.Equals(actualComparison.Test, StringComparison.OrdinalIgnoreCase)
                || !expectedComparison.B.Equals(actualComparison.B, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("MOVEit accepted the Task update but changed a requested Source Special Filter criterion.");
        }
    }

    internal static (string Mode, List<(string A, string Test, string B)> Comparisons)? ParseSpecialFilterCriteria(JsonNode? root)
    {
        if (root is not JsonObject criteria || !HasOnlyProperties(criteria, "And", "Or", "comp", "a", "test", "b")) return null;
        var andNode = GetNode(criteria, "And");
        var orNode = GetNode(criteria, "Or");
        if (andNode is not null && orNode is not null) return null;
        var mode = orNode is not null ? "any" : "all";
        var group = andNode ?? orNode ?? criteria;
        if (group is JsonObject groupObject && !ReferenceEquals(groupObject, criteria) && !HasOnlyProperties(groupObject, "comp")) return null;
        var comparisons = GetCriteriaComparisonObjects(group);
        if (comparisons.Count == 0) return null;

        var result = new List<(string A, string Test, string B)>();
        foreach (var comparison in comparisons)
        {
            if (!HasOnlyProperties(comparison, "a", "test", "b")) return null;
            var a = GetString(comparison, "a").Trim();
            var test = GetString(comparison, "test").Trim();
            var b = GetString(comparison, "b").Trim();
            if (a.Length == 0 || test.Length == 0 || b.Length == 0) return null;
            result.Add((a, test, b));
        }
        return (mode, result);
    }

    internal static bool HasOnlyProperties(JsonObject value, params string[] allowed)
    {
        var names = new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase);
        return value.All(pair => names.Contains(pair.Key));
    }

    internal static IReadOnlyList<JsonObject> GetCriteriaComparisonObjects(JsonNode? group)
    {
        if (group is JsonObject groupObject)
        {
            var comp = GetNode(groupObject, "comp");
            if (comp is JsonArray compArray) return compArray.OfType<JsonObject>().ToList();
            if (comp is JsonObject compObject) return new[] { compObject };
            if (GetNode(groupObject, "a") is not null && GetNode(groupObject, "test") is not null && GetNode(groupObject, "b") is not null)
                return new[] { groupObject };
        }
        if (group is JsonArray groupArray) return groupArray.OfType<JsonObject>().ToList();
        return Array.Empty<JsonObject>();
    }

    internal static void VerifyNextActions(JsonArray? requested, JsonNode? persistedNode)
    {
        if (requested is null) return;
        var expectedItems = requested.OfType<JsonObject>().ToList();
        var persisted = GetCollectionObjects(persistedNode, "NextAction");
        if (persisted.Count != expectedItems.Count)
            throw new InvalidOperationException("MOVEit accepted the Task update but did not persist the requested Next Actions collection.");

        var consumed = new HashSet<int>();
        foreach (var item in expectedItems)
        {
            var type = GetString(item, "type").Trim();
            if (!type.Equals("Email", StringComparison.OrdinalIgnoreCase) && !type.Equals("Task", StringComparison.OrdinalIgnoreCase))
                continue;

            var id = GetString(item, "id").Trim();
            // ServerIndex identifies the pre-save object used during merge. After a remove, the
            // persisted collection is compacted, so verification must use the current output index
            // when an action has no server-assigned ID.
            var index = FindByIdOrIndex(persisted, consumed, id, GetInt(item, "index", -1));
            if (index < 0)
                throw new InvalidOperationException($"MOVEit accepted the Task update but did not persist the requested {type} Next Action.");

            consumed.Add(index);
            var actual = persisted[index];
            if (!GetString(actual, "Type").Equals(type, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("MOVEit accepted the Task update but changed the requested Next Action type.");

            VerifyTextValue("Next Action DoAfter", FirstNonBlank(GetString(item, "doAfter"), "Task"), FirstNonBlank(GetString(actual, "DoAfter"), "Task"));
            VerifyFlagValue("Next Action failure condition", GetBool(item, "doIfFailure"), actual, "DoIfFailure");
            VerifyFlagValue("Next Action no-action condition", GetBool(item, "doIfNoAction"), actual, "DoIfNoAction");
            VerifyFlagValue("Next Action success condition", GetBool(item, "doIfSuccess"), actual, "DoIfSuccess");

            if (type.Equals("Email", StringComparison.OrdinalIgnoreCase))
            {
                VerifyTextValue("Email Next Action SMTP Host", GetString(item, "hostId").Trim(), GetString(actual, "HostID").Trim());
                VerifyTextValue("Email Next Action recipients", NormalizeRecipients(GetString(item, "addressTo")), NormalizeRecipients(GetString(actual, "AddressTo")));
                VerifyTextValue("Email Next Action subject", GetString(item, "subject"), GetString(actual, "Subject"));
                VerifyTextValue("Email Next Action message", GetString(item, "message"), GetString(actual, "Message"));
                VerifyFlagValue("Email Next Action HTML setting", GetBool(item, "isHtml"), actual, "IsHTML");
            }
            else
            {
                VerifyTextValue("Run Task Next Action target", GetString(item, "taskId").Trim(), GetString(actual, "TaskID").Trim());
                if (GetNode(item, "parameters") is not null)
                    VerifyParameters(GetNode(item, "parameters"), GetNode(actual, "Parameters"));
            }
        }
    }

    internal static bool EquivalentScalar(string fieldName, JsonNode? expected, JsonNode? actual)
    {
        if (expected is null) return actual is null;
        if (expected is JsonValue expectedValue)
        {
            if (expectedValue.TryGetValue<int>(out var expectedInt))
            {
                var actualIntText = NodeString(actual);
                if (actualIntText.Length == 0 && expectedInt == 0) return true; // MOVEit may omit false/zero defaults.
                return long.TryParse(actualIntText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var actualInt) && actualInt == expectedInt;
            }
            if (expectedValue.TryGetValue<long>(out var expectedLong))
            {
                var actualLongText = NodeString(actual);
                if (actualLongText.Length == 0 && expectedLong == 0) return true;
                return long.TryParse(actualLongText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var actualLong) && actualLong == expectedLong;
            }
            if (expectedValue.TryGetValue<bool>(out var expectedBool))
                return ParseBool(actual) == expectedBool;
        }

        var expectedText = NodeString(expected);
        var actualText = NodeString(actual);
        if (fieldName.Equals("Run", StringComparison.OrdinalIgnoreCase) && actualText.Length == 0
            && expectedText.Equals("PerFile", StringComparison.OrdinalIgnoreCase))
            return true;
        if (fieldName.Equals("Type", StringComparison.OrdinalIgnoreCase) || fieldName.Equals("Run", StringComparison.OrdinalIgnoreCase))
            return expectedText.Equals(actualText, StringComparison.OrdinalIgnoreCase);
        return expectedText.Equals(actualText, StringComparison.Ordinal);
    }

    internal static void VerifyTextValue(string label, string expected, string actual)
    {
        // Browser textareas use LF while server-side values may use CRLF. Treat newline style as
        // transport normalization, not a failed save; otherwise preserve exact text semantics.
        static string NormalizeLineEndings(string value)
            => (value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

        if (!string.Equals(NormalizeLineEndings(expected), NormalizeLineEndings(actual), StringComparison.Ordinal))
            throw new InvalidOperationException($"MOVEit accepted the Task update but did not persist {label}.");
    }

    internal static void VerifyFlagValue(string label, bool expected, JsonObject actual, string fieldName)
    {
        if (GetBool(actual, fieldName) != expected)
            throw new InvalidOperationException($"MOVEit accepted the Task update but did not persist {label}.");
    }

    internal static void VerifyOptionalNumber(string label, JsonNode? expected, JsonNode? actual)
    {
        var expectedText = NodeString(expected).Trim();
        if (expectedText.Length == 0) return;
        if (!long.TryParse(expectedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var expectedValue)
            || !long.TryParse(NodeString(actual), NumberStyles.Integer, CultureInfo.InvariantCulture, out var actualValue)
            || expectedValue != actualValue)
            throw new InvalidOperationException($"MOVEit accepted the Task update but did not persist {label}.");
    }

    internal static bool ParseBool(JsonNode? node)
    {
        var value = NodeString(node).Trim();
        return value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1" || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    internal static void VerifySchedules(JsonArray? requested, JsonNode? persistedNode)
    {
        if (requested is null) return;
        var persisted = GetCollectionObjects(persistedNode, "Schedule");
        var requestedItems = requested.OfType<JsonObject>().ToList();
        if (persisted.Count != requestedItems.Count)
            throw new InvalidOperationException($"MOVEit accepted the Task update but returned {persisted.Count} Schedule record(s) after MAUI requested {requestedItems.Count}. The authoritative Task does not match the requested Schedules collection.");


        for (var i = 0; i < requestedItems.Count; i++)
        {
            var expected = requestedItems[i];
            var actual = persisted[i];
            var expectedDays = GetArray(expected, "daysOfWeek")?.Select(NodeString).Where(value => value.Length > 0).ToList() ?? new List<string>();
            var actualDays = GetStringValues(GetNode(GetObject(actual, "Days"), "DayOfWeek"));
            if (!expectedDays.SequenceEqual(actualDays, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("MOVEit accepted the Task update but did not persist one or more Schedule weekday changes.");

            var expectedMonthDays = GetArray(expected, "daysOfMonth")?.Select(NodeString).Where(value => value.Length > 0).ToList() ?? new List<string>();
            var actualMonthDays = GetStringValues(GetNode(GetObject(actual, "Days"), "DayOfMonth"));
            if (!expectedMonthDays.SequenceEqual(actualMonthDays, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("MOVEit accepted the Task update but did not persist one or more Schedule day-of-month changes.");

            if (GetInt(expected, "multi", 0) != GetInt(actual, "Multi", 0))
                throw new InvalidOperationException("MOVEit accepted the Task update but did not persist the Schedule overnight/multi segment state.");

            if (GetBool(expected, "onlyUntilFirstSuccess") != GetBool(actual, "OnlyUntilFirstSuccess")
                || GetBool(expected, "failIfNoSuccessInSchedule") != GetBool(actual, "FailIfNoSuccessInSched")
                || GetBool(expected, "runEvenIfNotifications") != GetBool(actual, "RunEvenIfNotif"))
                throw new InvalidOperationException("MOVEit accepted the Task update but did not persist one or more Schedule option changes.");

            var expectedDateLists = GetArray(expected, "dateLists")?.OfType<JsonObject>()
                .Where(reference => GetString(reference, "id").Trim().Length > 0)
                .Select(reference => (Id: GetString(reference, "id").Trim(), Run: GetBool(reference, "run")))
                .ToList() ?? new List<(string Id, bool Run)>();
            var actualDateLists = GetDirectObjects(GetNode(actual, "DateListRef"))
                .Where(reference => GetString(reference, "ID").Trim().Length > 0)
                .Select(reference => (Id: GetString(reference, "ID").Trim(), Run: GetBool(reference, "Run")))
                .ToList();
            if (expectedDateLists.Count != actualDateLists.Count
                || expectedDateLists.Any(reference =>
                {
                    var match = actualDateLists.FirstOrDefault(actualReference =>
                        reference.Id.Equals(actualReference.Id, StringComparison.OrdinalIgnoreCase));
                    return string.IsNullOrWhiteSpace(match.Id) || match.Run != reference.Run;
                }))
                throw new InvalidOperationException("MOVEit accepted the Task update but did not persist one or more Schedule Date List changes.");

            var expectedIntervals = GetArray(expected, "intervals")?.OfType<JsonObject>().ToList() ?? new List<JsonObject>();
            var actualFrequency = GetNode(actual, "Frequency");
            var actualIntervals = actualFrequency is JsonObject frequency ? GetDirectObjects(GetNode(frequency, "Interval")) : GetDirectObjects(actualFrequency);
            if (actualIntervals.Count != expectedIntervals.Count)
                throw new InvalidOperationException("MOVEit accepted the Task update but did not persist the requested Schedule time intervals.");

            for (var intervalIndex = 0; intervalIndex < expectedIntervals.Count; intervalIndex++)
            {
                var expectedInterval = expectedIntervals[intervalIndex];
                var actualInterval = actualIntervals[intervalIndex];
                if (!NormalizeTime(GetString(expectedInterval, "startTime")).Equals(NormalizeTime(GetString(actualInterval, "StartTime")), StringComparison.OrdinalIgnoreCase)
                    || !NormalizeTime(GetString(expectedInterval, "endTime")).Equals(NormalizeTime(GetString(actualInterval, "EndTime")), StringComparison.OrdinalIgnoreCase)
                    || GetInt(expectedInterval, "everyMinutes", 0) != GetInt(actualInterval, "EveryMinutes", 0))
                    throw new InvalidOperationException("MOVEit accepted the Task update but did not persist one or more Schedule time changes.");
            }
        }
    }

    internal static void VerifyParameters(JsonNode? requestedNode, JsonNode? persistedNode)
    {
        var requested = ParseParameterObjects(requestedNode);
        var persisted = ParseParameterObjects(persistedNode);
        var replace = requestedNode is JsonObject requestedObject && GetBool(requestedObject, "replace");
        if (replace && persisted.Count != requested.Count)
            throw new InvalidOperationException("MOVEit accepted the Task update but did not persist the requested Task Parameters collection.");

        foreach (var expected in requested)
        {
            var name = FirstNonBlank(GetString(expected, "name"), GetString(expected, "Name")).Trim();
            if (name.Length == 0) continue;
            var actual = persisted.FirstOrDefault(item => GetString(item, "name").Equals(name, StringComparison.OrdinalIgnoreCase) || GetString(item, "Name").Equals(name, StringComparison.OrdinalIgnoreCase));
            if (actual is null) throw new InvalidOperationException($"MOVEit accepted the Task update but did not persist Task Parameter {name}.");
            if (GetBool(expected, "preserve")) continue;
            if (GetBool(expected, "secure") != IsSecureParameter(actual))
                throw new InvalidOperationException($"MOVEit accepted the Task update but did not persist the secure setting for Task Parameter {name}.");
            if (!GetBool(expected, "secure") && !GetString(expected, "value").Equals(FirstNonBlank(GetString(actual, "value"), GetString(actual, "displayValue"), GetString(actual, "DisplayValue")), StringComparison.Ordinal))
                throw new InvalidOperationException($"MOVEit accepted the Task update but did not persist the value for Task Parameter {name}.");
        }
    }

    internal static bool IsSecureParameter(JsonObject parameter)
    {
        if (GetBool(parameter, "secure")) return true;
        var display = FirstNonBlank(GetString(parameter, "displayValue"), GetString(parameter, "DisplayValue"), GetString(parameter, "countedDisplayValue"));
        return display.Equals("(suppressed)", StringComparison.OrdinalIgnoreCase);
    }

    internal static IReadOnlyList<string> GetStringValues(JsonNode? node)
    {
        if (node is JsonArray array) return array.Select(NodeString).Select(value => value.Trim()).Where(value => value.Length > 0).ToList();
        var single = NodeString(node).Trim();
        return single.Length == 0 ? Array.Empty<string>() : new[] { single };
    }

    internal static int FindStep(IReadOnlyList<JsonObject> existing, HashSet<int> consumed, string kind, string id, int fallbackIndex)
    {
        for (var i = 0; i < existing.Count; i++)
        {
            if (consumed.Contains(i)) continue;
            var target = GetObject(existing[i], kind);
            if (target is null) continue;
            if (id.Length > 0 && GetString(target, "ID").Equals(id, StringComparison.OrdinalIgnoreCase)) return i;
        }
        if (fallbackIndex >= 0 && fallbackIndex < existing.Count && !consumed.Contains(fallbackIndex) && GetObject(existing[fallbackIndex], kind) is not null) return fallbackIndex;
        return -1;
    }

    internal static int FindByIdOrIndex(IReadOnlyList<JsonObject> existing, HashSet<int> consumed, string id, int fallbackIndex)
    {
        if (id.Length > 0)
            for (var i = 0; i < existing.Count; i++)
                if (!consumed.Contains(i) && GetString(existing[i], "ID").Equals(id, StringComparison.OrdinalIgnoreCase)) return i;
        if (fallbackIndex >= 0 && fallbackIndex < existing.Count && !consumed.Contains(fallbackIndex)) return fallbackIndex;
        return -1;
    }

    internal static IReadOnlyList<JsonObject> GetStepObjects(JsonNode? root)
    {
        if (root is JsonArray array) return array.OfType<JsonObject>().ToList();
        if (root is JsonObject obj)
        {
            if (GetNode(obj, "Step") is JsonArray steps) return steps.OfType<JsonObject>().ToList();
            if (GetNode(obj, "Step") is JsonObject single) return new[] { single };
        }
        return Array.Empty<JsonObject>();
    }

    internal static IReadOnlyList<JsonObject> GetDirectObjects(JsonNode? root)
    {
        if (root is JsonArray array) return array.OfType<JsonObject>().ToList();
        if (root is JsonObject single) return new[] { single };
        return Array.Empty<JsonObject>();
    }

    internal static IReadOnlyList<JsonObject> GetCollectionObjects(JsonNode? root, string itemName)
    {
        if (root is JsonArray array) return array.OfType<JsonObject>().ToList();
        if (root is JsonObject obj)
        {
            var items = GetNode(obj, itemName);
            if (items is JsonArray values) return values.OfType<JsonObject>().ToList();
            if (items is JsonObject single) return new[] { single };
        }
        return Array.Empty<JsonObject>();
    }

    internal static string NormalizeStepKind(string value)
    {
        if (value.Equals("Source", StringComparison.OrdinalIgnoreCase)) return "Source";
        if (value.Equals("Process", StringComparison.OrdinalIgnoreCase)) return "Process";
        if (value.Equals("Destination", StringComparison.OrdinalIgnoreCase)) return "Destination";
        return value.Trim();
    }

    internal static JsonNode PreserveNamedCollectionShape(JsonNode? existingRoot, string itemName, JsonArray items)
    {
        if (existingRoot is JsonArray) return (JsonArray)items.DeepClone();
        if (existingRoot is JsonObject existingObject)
        {
            var clone = (JsonObject)existingObject.DeepClone();
            var propertyName = ExistingPropertyName(clone, itemName);
            var existingItems = GetNode(existingObject, itemName);
            clone[propertyName] = PreserveListShape(existingItems, items, preferArrayWhenUnknown: true);
            return clone;
        }
        return new JsonObject { [itemName] = (JsonArray)items.DeepClone() };
    }

    internal static JsonNode PreserveListShape(JsonNode? existingNode, JsonArray items, bool preferArrayWhenUnknown = false)
    {
        if (existingNode is JsonObject && items.Count == 1) return items[0]!.DeepClone();
        if (existingNode is JsonArray || preferArrayWhenUnknown || items.Count != 1) return (JsonArray)items.DeepClone();
        return items.Count == 1 ? items[0]!.DeepClone() : (JsonArray)items.DeepClone();
    }

    internal static void SetOrRemoveCollectionProperty(JsonObject target, string name, JsonArray values)
    {
        var propertyName = ExistingPropertyName(target, name);
        if (values.Count == 0) target.Remove(propertyName);
        else target[propertyName] = PreserveListShape(GetNode(target, name), values, preferArrayWhenUnknown: true);
    }

    internal static string ExistingPropertyName(JsonObject target, string requestedName)
    {
        foreach (var pair in target)
            if (pair.Key.Equals(requestedName, StringComparison.OrdinalIgnoreCase)) return pair.Key;
        return requestedName;
    }

    internal static void RemovePropertyIgnoreCase(JsonObject target, string name)
    {
        var keys = target.Select(pair => pair.Key)
            .Where(key => key.Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var key in keys) target.Remove(key);
    }

    internal static void SetTaskPutFlag(JsonObject target, string name, bool value)
    {
        // MOVEit 2025.1 may return these enum flags as JSON strings on GET, but its Task PUT
        // deserializer requires numeric JSON values. Do not mirror the read representation here.
        // Keep reads tolerant of both forms; writes use the observed server contract consistently.
        target[ExistingPropertyName(target, name)] = Flag(value);
    }

    internal static string ParameterName(JsonObject parameter)
        => FirstNonBlank(GetString(parameter, "Name"), GetString(parameter, "name")).Trim();

    internal static void SetParameterNameLikeServer(JsonObject parameter, string name, JsonNode? existingContainer)
    {
        var isKeyedContainer = existingContainer is JsonObject obj
            && GetNode(obj, "Parameter") is null
            && GetNode(obj, "any") is null
            && obj.Count > 0
            && obj.All(pair => pair.Value is JsonObject);
        if (isKeyedContainer && GetNode(parameter, "Name") is null && GetNode(parameter, "name") is null) return;

        var fieldName = GetNode(parameter, "Name") is not null ? "Name" : "name";
        parameter[ExistingPropertyName(parameter, fieldName)] = name;
    }

    internal static JsonObject CanonicalizeParameterForPut(
        JsonObject source,
        string name,
        bool? secureOverride = null,
        string? valueOverride = null,
        bool overrideValue = false)
    {
        var clone = (JsonObject)source.DeepClone();
        var existingValue = GetNode(source, "value")?.DeepClone();
        var existingDisplay = FirstNonBlank(GetString(source, "DisplayValue"), GetString(source, "displayValue"));
        var secure = secureOverride ?? IsSecureParameter(source);

        RemoveParameterMember(clone, "Name");
        RemoveParameterMember(clone, "Secure");
        RemoveParameterMember(clone, "value");
        RemoveParameterMember(clone, "DisplayValue");
        RemoveParameterMember(clone, "countedDisplayValue");

        clone["Name"] = name;
        if (overrideValue) clone["value"] = valueOverride ?? string.Empty;
        else if (existingValue is not null) clone["value"] = existingValue;
        clone["Secure"] = Flag(secure);

        if (secure)
            clone["DisplayValue"] = string.IsNullOrWhiteSpace(existingDisplay) ? "(suppressed)" : existingDisplay;

        return clone;
    }

    internal static void RemoveParameterMember(JsonObject parameter, string name)
    {
        var matches = parameter.Select(pair => pair.Key)
            .Where(key => key.Equals(name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var key in matches) parameter.Remove(key);
    }

    internal static JsonNode BuildNativeTaskParameterContainer(IReadOnlyList<JsonObject> items)
    {
        if (items.Count == 0) return new JsonObject();
        var array = new JsonArray();
        foreach (var item in items) array.Add(item.DeepClone());
        return new JsonObject { ["Parameter"] = array };
    }

    internal static JsonNode BuildParameterContainerLikeServer(JsonNode? existingNode, IReadOnlyList<JsonObject> items)
    {
        if (items.Count == 0) return new JsonObject();

        var array = new JsonArray();
        foreach (var item in items) array.Add(item.DeepClone());
        if (existingNode is JsonArray) return array;

        if (existingNode is JsonObject existingObject)
        {
            if (GetNode(existingObject, "Parameter") is not null)
            {
                var clone = (JsonObject)existingObject.DeepClone();
                var propertyName = ExistingPropertyName(clone, "Parameter");
                clone[propertyName] = PreserveListShape(GetNode(existingObject, "Parameter"), array, preferArrayWhenUnknown: true);
                return clone;
            }

            if (GetNode(existingObject, "any") is not null)
            {
                var clone = (JsonObject)existingObject.DeepClone();
                var propertyName = ExistingPropertyName(clone, "any");
                clone[propertyName] = PreserveListShape(GetNode(existingObject, "any"), array, preferArrayWhenUnknown: true);
                return clone;
            }

            if (existingObject.Count > 0 && existingObject.All(pair => pair.Value is JsonObject))
            {
                var keyed = new JsonObject();
                foreach (var item in items)
                {
                    var name = ParameterName(item);
                    if (name.Length == 0) continue;
                    var clone = (JsonObject)item.DeepClone();
                    RemoveParameterMember(clone, "Name");
                    keyed[name] = clone;
                }
                return keyed;
            }
        }

        return new JsonObject { ["any"] = array };
    }

    internal static JsonArray ToStringArray(JsonArray? source)
    {
        var result = new JsonArray();
        if (source is null) return result;
        foreach (var value in source) result.Add(NodeString(value));
        return result;
    }

    internal static JsonArray ToIntArray(JsonArray? source)
    {
        var result = new JsonArray();
        if (source is null) return result;
        foreach (var value in source)
            if (int.TryParse(NodeString(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) result.Add(parsed);
        return result;
    }

    internal static void ValidateRequestedParameters(IReadOnlyList<JsonObject> parameters)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in parameters)
        {
            var name = FirstNonBlank(GetString(parameter, "Name"), GetString(parameter, "name")).Trim();
            if (name.Length == 0) throw new InvalidOperationException("Parameter Name is required.");
            if (!Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9._:]*$", RegexOptions.CultureInvariant))
                throw new InvalidOperationException($"Parameter Name '{name}' is invalid. Names must start with a letter or underscore and contain only letters, numbers, dots, underscores, or colons.");
            if (!names.Add(name)) throw new InvalidOperationException($"Parameter Name '{name}' is duplicated.");
        }
    }

    internal static int ReadRequiredWholeNumber(JsonObject source, string name, string label)
    {
        var text = GetString(source, name).Trim();
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new InvalidOperationException($"{label} must be a whole number.");
        return value;
    }

    internal static string NormalizeTime(string value)
    {
        var text = (value ?? string.Empty).Trim();
        if (TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return parsed.ToString("HH:mm", CultureInfo.InvariantCulture);
        return text;
    }

    internal static JsonNode? GetNode(JsonObject? obj, string name)
    {
        if (obj is null) return null;
        if (obj.TryGetPropertyValue(name, out var direct)) return direct;
        foreach (var pair in obj) if (pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)) return pair.Value;
        return null;
    }

    internal static JsonObject? GetObject(JsonObject? obj, string name) => GetNode(obj, name) as JsonObject;
    internal static JsonArray? GetArray(JsonObject? obj, string name) => GetNode(obj, name) as JsonArray;
    internal static string GetString(JsonObject? obj, string name) => NodeString(GetNode(obj, name));
    internal static int GetInt(JsonObject? obj, string name, int fallback) => int.TryParse(GetString(obj, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    internal static bool GetBool(JsonObject? obj, string name, bool fallback = false)
    {
        var value = GetString(obj, name).Trim();
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1" || value.Equals("yes", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Equals("false", StringComparison.OrdinalIgnoreCase) || value == "0" || value.Equals("no", StringComparison.OrdinalIgnoreCase)) return false;
        return fallback;
    }
    internal static string NodeString(JsonNode? node)
    {
        if (node is not JsonValue value) return string.Empty;
        if (value.TryGetValue<string>(out var text)) return text ?? string.Empty;
        if (value.TryGetValue<int>(out var i)) return i.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue<long>(out var l)) return l.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue<bool>(out var b)) return b ? "true" : "false";
        return string.Empty;
    }
    internal static string NormalizeRecipients(string value)
        => string.Join(",", (value ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => item.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));

    internal static int Flag(bool value) => value ? 1 : 0;
    internal static string FirstNonBlank(params string[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}

