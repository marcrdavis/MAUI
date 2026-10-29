// Shared verified Host patch policy. Optional editors must only adapt requests to this service.
using System.Globalization;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Services.MoveIt.Hosts;

public interface IHostDefinitionSaveService
{
    Task UpdateBasicDetailsAsync(string hostId, string name, string description, CancellationToken cancellationToken = default);
    Task<HostDefinitionSaveResult> SaveDefinitionAsync(
        string hostId,
        HostDefinitionSaveRequest request,
        string username,
        HostCacheSnapshot liveSnapshot,
        CancellationToken cancellationToken = default);
}

public sealed partial class HostDefinitionSaveService : IHostDefinitionSaveService
{
    private static readonly HashSet<string> UncWritableKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "UNC", "DriveLetter", "UseNotif", "DefUsername", "DefPassword", HostAuthenticationSettings.UncModeKey,
        "DefMxFi", "DefMxBy", "DefRescanSecs", "DefRetryCount", "DefRetryTimeoutSecs",
        "DefWinCopyFileAPI", "ID2", "DelOldStateDays", "AdjDST",
        "DefPreserveFileTimestamp", "UseDefStateCaching", "StateCacheTime", "StateCacheUnit"
    };

    private static readonly HashSet<string> MoveItTransferWritableKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Port", "ScriptPath", "DefSort", "UseNotif", "DefUsername", "DefPassword",
        "DefClientCertSHA1", "DefClientCertStore", "DefClientCertSubject",
        "Secure", "IgnoreCertProbs",
        "DefMxFi", "DefMxBy",
        "DefConnTimeoutSecs", "DefDataTimeoutSecs", "DefRetryCount", "DefRetryTimeoutSecs",
        "ID2", "DelOldStateDays", "UseDefStateCaching", "StateCacheTime", "StateCacheUnit"
    };

    private static readonly HashSet<string> MoveItTransferSortValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "Filename,A", "Filename,D", "Date,A", "Date,D", "Size,A", "Size,D"
    };

    private readonly IMoveItRestClient _restClient;
    private readonly IMoveItComClient _comClient;
    private readonly IConfigurationUndoWorkflow _undoWorkflow;
    private readonly ILogger<HostDefinitionSaveService> _logger;
    private readonly IMoveItSessionService? _sessions;

    public HostDefinitionSaveService(
        IMoveItRestClient restClient,
        IMoveItComClient comClient,
        IConfigurationUndoWorkflow undoWorkflow,
        ILogger<HostDefinitionSaveService> logger, IMoveItSessionService? sessions = null)
    {
        _restClient = restClient;
        _comClient = comClient;
        _undoWorkflow = undoWorkflow;
        _logger = logger;
        _sessions = sessions;
    }

    public async Task<HostDefinitionSaveResult> SaveDefinitionAsync(
        string hostId,
        HostDefinitionSaveRequest request,
        string username,
        HostCacheSnapshot liveSnapshot,
        CancellationToken cancellationToken = default)
    {
        var id = (hostId ?? string.Empty).Trim();
        if (id.Length == 0) throw new ArgumentException("Host ID is required.", nameof(hostId));
        ArgumentNullException.ThrowIfNull(request);

        var requestedName = (request.Name ?? string.Empty).Trim();
        if (requestedName.Length == 0) throw new InvalidOperationException("Host Name is required.");
        if (requestedName.Length > 254) throw new InvalidOperationException("Host Name cannot exceed 254 characters.");

        var live = liveSnapshot.Hosts.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Host '{id}' is no longer available to the current MOVEit user.");

        var (typeCode, rawHost, _) = await ReadHostForSaveAsync(id, cancellationToken);
        VerifyIdentity(rawHost, id, typeCode, live.TypeCode);
        ValidateLocalFileSystemName(rawHost, id, typeCode, requestedName);

        var commonChanged = !requestedName.Equals(live.Name, StringComparison.Ordinal)
            || !string.Equals(request.Description ?? string.Empty, live.Description ?? string.Empty, StringComparison.Ordinal);
        if (!live.Name.Equals(requestedName, StringComparison.OrdinalIgnoreCase)
            && await _restClient.HostNameExistsAsync(requestedName, cancellationToken))
            throw new HostNameAlreadyExistsException(requestedName);
        var configuration = request.Configuration ?? new JsonObject();

        if (HostCapturedProfiles.TryGet(typeCode, out var capturedFields))
            return await SaveCapturedProfileAsync(id, request, username, liveSnapshot, live, rawHost,
                typeCode, capturedFields, commonChanged, cancellationToken);

        if (typeCode is "Share" or "siLock")
        {
            var unc = typeCode == "Share";
            var writableKeys = unc ? UncWritableKeys : MoveItTransferWritableKeys;
            if (unc) ValidateUncRequestKeys(configuration);
            else ValidateMoveItTransferRequestKeys(configuration);

            void Compose(JsonObject host)
            {
                ApplyCommonFields(host, requestedName, request.Description ?? string.Empty);
                if (unc)
                {
                    ApplyUncFields(host, configuration, id, liveSnapshot.Hosts);
                }
                else
                {
                    ApplyMoveItTransferFields(host, configuration, id, liveSnapshot.Hosts);
                }
            }

            void Validate(JsonObject host)
            {
                if (unc) ValidateEffectiveUncHost(host);
                else { ValidateEffectiveMoveItTransferHost(host); HostResourceContracts.ValidateAtomicFields(configuration); }
            }

            var preview = (JsonObject)rawHost.DeepClone();
            Compose(preview);
            if (!commonChanged && !HasRequestedChanges(rawHost, preview, configuration, writableKeys)
                && !HostAuthenticationSettings.HasPasswordReplacement(configuration)
                && !(unc && HostAuthenticationSettings.UncModeChanged(rawHost, preview, configuration)))
                return new HostDefinitionSaveResult(false, live.Name, typeCode);

            EnsureCanEdit(live);
            Validate(preview);
            if (!unc && HostResourceContracts.CertificateFields.Any(key => configuration.ContainsKey(key) && HostResourceContracts.Text(configuration, key).Length > 0))
                HostResourceContracts.ValidateCertificate(configuration, HostResourceContracts.Certificates(await _restClient.GetHostTlsCertificatesV0Async(cancellationToken)));
            var profileUndoSnapshotId = await CaptureUndoAsync(id, username, cancellationToken);
            await WriteCheckedOutHostAsync(id, typeCode, host =>
            {
                Compose(host); Validate(host);
                if (!unc || HostAuthenticationSettings.UncMode(host) == "credentials") HostAuthenticationSettings.PreservePassword(host, configuration);
            }, configuration, writableKeys, cancellationToken);
            return new HostDefinitionSaveResult(true, requestedName, typeCode, profileUndoSnapshotId);
        }

        if (configuration.Count > 0)
            throw new InvalidOperationException("Protocol configuration is not writable for this Host type.");

        if (!commonChanged)
            return new HostDefinitionSaveResult(false, live.Name, typeCode);

        EnsureCanEdit(live);
        var undoSnapshotId = await CaptureUndoAsync(id, username, cancellationToken);
        await _comClient.UpdateHostBasicDetailsAsync(id, requestedName, request.Description ?? string.Empty, cancellationToken);

        try
        {
            var (_, verified) = HostDefinitionJson.UnwrapHost(await _restClient.GetHostAsync(id, cancellationToken));
            VerifyCommonFields(verified, requestedName, request.Description ?? string.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            throw new HostPartialSaveException(
                "MOVEit accepted the Host information update, but MAUI could not verify the complete saved state. Close and reopen the Host Editor before retrying changes.",
                ex);
        }

        return new HostDefinitionSaveResult(true, requestedName, typeCode, undoSnapshotId);
    }

    private async Task<HostDefinitionSaveResult> SaveCapturedProfileAsync(
        string id, HostDefinitionSaveRequest request, string username, HostCacheSnapshot liveSnapshot,
        AutomationHost live, JsonObject rawHost, string typeCode, IReadOnlyList<HostProfileField> fields,
        bool commonChanged, CancellationToken cancellationToken)
    {
        var configuration = request.Configuration ?? new JsonObject();
        var preview = (JsonObject)rawHost.DeepClone();
        HostCapturedProfiles.Apply(preview, configuration, typeCode);
        MoveItFeatureSupport.RequireHost(new JsonObject { [typeCode] = preview.DeepClone() }, _sessions?.Get()?.Info);
        var changed = commonChanged || HostCloudAuthenticationSettings.HasChanges(rawHost, preview, configuration, typeCode) || HostStateCacheSettings.HasChanges(rawHost, preview, configuration)
            || typeCode is "POP3" or "SMTP" && HostMailOAuthSettings.HasChanges(rawHost, preview, configuration)
            || typeCode == "FTP" && HostFtpDirectoryParsing.HasChanges(rawHost, preview, configuration) || fields.Any(field => configuration.ContainsKey(field.Key)
            && (field.Sensitive ? HostDefinitionJson.ReadNode(configuration[field.Key]).Length > 0
                : !Equivalent(HostDefinitionJson.GetNode(rawHost, field.Key), HostDefinitionJson.GetNode(preview, field.Key))));
        if (!changed) return new HostDefinitionSaveResult(false, live.Name, typeCode);
        EnsureCanEdit(live);
        ValidateCapturedEffective(preview, configuration, typeCode, id, liveSnapshot.Hosts);
        await ValidateClientKeyAsync(configuration, typeCode, cancellationToken);
        if (typeCode == "FTP" && HostFtpDirectoryParsing.Mode(preview) == "script"
            && (configuration.ContainsKey(HostFtpDirectoryParsing.ModeKey) || configuration.ContainsKey("DirScriptID")))
        {
            var custom = _restClient.GetCustomScriptsAsync(cancellationToken);
            var standard = _restClient.GetStandardScriptsAsync(cancellationToken);
            await Task.WhenAll(custom, standard);
            var scriptId = HostResourceContracts.Text(preview, "DirScriptID");
            if (!(await custom).Concat(await standard).Any(script => HostResourceContracts.Text(script, "ID") == scriptId))
                throw new InvalidOperationException("The selected directory parsing Script is no longer visible. Reload the Host editor.");
        }
        if (typeCode is "FTP" or "SharePoint" or "OneDrive" && HostResourceContracts.CertificateFields.Any(key => configuration.ContainsKey(key) && HostResourceContracts.Text(configuration, key).Length > 0))
            HostResourceContracts.ValidateCertificate(configuration, HostResourceContracts.Certificates(await _restClient.GetHostTlsCertificatesV0Async(cancellationToken)));
        var undoSnapshotId = await CaptureUndoAsync(id, username, cancellationToken);

        await WriteCheckedOutHostAsync(id, typeCode, host =>
        {
            ValidateLocalFileSystemName(host, id, typeCode, request.Name.Trim());
            ApplyCommonFields(host, request.Name.Trim(), request.Description ?? "");
            HostCapturedProfiles.Apply(host, configuration, typeCode);
            ValidateCapturedEffective(host, configuration, typeCode, id, liveSnapshot.Hosts);
            HostCapturedProfiles.PreserveUnsubmittedSecrets(host, configuration, typeCode);
        }, configuration, fields.Where(field => !field.Sensitive).Select(field => field.Key), cancellationToken);
        return new HostDefinitionSaveResult(true, request.Name.Trim(), typeCode, undoSnapshotId);
    }

    // One checkout/write/release lifecycle for every verified REST Host profile.
    // Explicit HTTP rejections with confirmed checkout cleanup are safe; lost acknowledgements are uncertain.
    private async Task<JsonObject> WriteCheckedOutHostAsync(string id, string typeCode,
        Action<JsonObject> compose, JsonObject configuration, IEnumerable<string> writableKeys, CancellationToken cancellationToken, bool informationOnly = false)
    {
        bool writeAttempted = false;
        var stage = "checkout";
        try
        {
            JsonObject? wrapper = null;
            return await ObjectCheckout.ExecuteAsync("Host", id,
                async ct => { wrapper = await _restClient.CheckoutHostV0Async(id, ct); },
                async ct =>
                {
                    // Compare the same GET projection used to render the editor, while checkout is held.
                    var (freshType, fresh, freshWrapper) = await ReadHostForSaveAsync(id, ct);
                    // The lifetime checkout was acquired when the editor opened. Its save uses the
                    // same authoritative typed GET projection without issuing another checkout POST.
                    if (ObjectCheckout.HasLifetimeFor("Host", id)) wrapper = freshWrapper;
                    VerifyIdentity(fresh, id, freshType, typeCode);
                    if (informationOnly) EnsureInformationPermission(freshWrapper, fresh);
                    if (ObjectEditVersion.Pending)
                        ObjectEditVersion.Verify("Host", id, informationOnly ? InformationRevision(fresh)
                            : ObjectEditVersion.Definition(fresh, (await _comClient.GetHostConfigurationAsync(id, ct)).Xml));
                    var (checkoutType, checkout) = await ResolveHostForSaveAsync(wrapper!, id, ct, freshType);
                    VerifyIdentity(checkout, id, checkoutType, typeCode);
                    if (informationOnly)
                    {
                        EnsureInformationPermission(wrapper!, checkout);
                        var freshInformation = InformationRevision(fresh);
                        var checkoutInformation = InformationRevision(checkout);
                        foreach (var field in new[] { "Name", "Description" })
                            if (NormalizeNewlines(freshInformation[field]!.GetValue<string>()) != NormalizeNewlines(checkoutInformation[field]!.GetValue<string>()))
                                throw new ObjectEditConflictException("Host");
                    }
                    var patched = (JsonObject)checkout.DeepClone();
                    compose(patched);
                    var payload = new JsonObject
                    {
                        [ResolveTypedWrapperKey(wrapper!, typeCode, typeCode == "siLock" ? "SiLock" : typeCode)] = patched
                    };
                    stage = "PUT";
                    writeAttempted = true;
                    var acknowledgement = await _restClient.UpdateHostV0Async(id, payload, ct);
                    stage = "acknowledgement";
                    ValidatePutResponse(acknowledgement, id);
                    stage = "readback";
                    await VerifySavedHostAsync(id, typeCode, patched, configuration, writableKeys, ct, informationOnly);
                    stage = "checkout-release";
                    return patched;
                }, ct => _restClient.ReleaseHostCheckoutV0Async(id, ct), cancellationToken);
        }
        catch (MoveItAuthenticationException) { throw; }
        catch (ObjectEditLeaseException) { throw; }
        catch (ObjectCheckoutRejectedException) { throw; }
        catch (HostPartialSaveException) { throw; }
        catch (MoveItApiException ex) when (stage == "PUT" && ex.StatusCode is not null
            && (int)ex.StatusCode >= 400 && (int)ex.StatusCode < 500 && ex.StatusCode != System.Net.HttpStatusCode.RequestTimeout)
        {
            // Like Task saves, a definitive PUT rejection leaves the draft editable.
            // Do not expose native response bodies: they can echo submitted credentials.
            // A cleanup failure is wrapped by ObjectCheckout and cannot enter this branch.
            throw new MoveItApiException($"MOVEit rejected the Host update (HTTP {(int)ex.StatusCode}). Review the Host settings before saving again.", ex.StatusCode);
        }
        catch (Exception ex) when (writeAttempted)
        {
            // Native errors can contain submitted credentials. Keep controlled stage
            // diagnostics, never the native exception body, and never retry the PUT.
            if (ex is ObjectCheckoutReleaseException) stage = "checkout-release";
            throw new HostPartialSaveException("The Host save result could not be confirmed. Review the Host in MOVEit and reload it before retrying.",
                new InvalidOperationException("Native Host save confirmation failed."), id, stage, SaveFailureReason(ex));
        }
    }

    private async Task VerifySavedHostAsync(string id, string typeCode, JsonObject expected,
        JsonObject configuration, IEnumerable<string> writableKeys, CancellationToken cancellationToken, bool informationOnly = false)
    {
        var reason = "readback-definition-or-type-unavailable";
        try
        {
            var (verifiedType, verified, _) = await ReadHostForSaveAsync(id, cancellationToken);
            reason = "readback-identity-or-type-mismatch";
            VerifyIdentity(verified, id, verifiedType, typeCode);
            reason = "saved-name-or-description-mismatch";
            VerifyCommonFields(verified, HostResourceContracts.Text(expected, "Name"),
                HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(expected, "Desc")));
            // Stored secrets are suppressed; successful PUT acknowledges replacement without decrypting them.
            reason = "saved-requested-field-mismatch";
            VerifyRequestedFields(expected, verified, configuration, writableKeys, typeCode);
            if (informationOnly) ObjectEditVersion.Persisted("Host", id, InformationRevision(verified));
        }
        catch (Exception ex)
        {
            throw new HostPartialSaveException("MOVEit accepted the Host update, but the saved settings could not be verified. Reload the Host before retrying.",
                new InvalidOperationException("Host saved-state verification failed."), id, "readback",
                ex is MoveItApiException or HttpRequestException or OperationCanceledException or System.Text.Json.JsonException
                    ? SaveFailureReason(ex) : reason);
        }
    }

    private static void VerifyIdentity(JsonObject host, string id, string actualType, string expectedType)
    {
        if (!actualType.Equals(expectedType, StringComparison.OrdinalIgnoreCase)
            || HostResourceContracts.Text(host, "ID") != id)
            throw new InvalidOperationException("MOVEit returned a different Host identity or type. Reload the Host before saving.");
    }

    private async Task ValidateClientKeyAsync(JsonObject configuration, string type, CancellationToken cancellationToken)
    {
        if (type != "SSHFTP" || !configuration.ContainsKey("DefClientKeyID")) return;
        var keyId = HostDefinitionJson.ReadNode(configuration["DefClientKeyID"]).Trim();
        if (keyId == "0") return;
        if (!long.TryParse(keyId, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
            throw new InvalidOperationException("Select a valid SSH client key, or None.");
        var keys = await _restClient.GetSshKeysAsync(cancellationToken);
        if (!keys.Any(key => HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(key, "ID")).Trim() == keyId))
            throw new InvalidOperationException("The selected SSH client key is no longer visible. Reload the Host editor.");
    }

    private static void ValidateCapturedEffective(JsonObject host, JsonObject configuration, string type,
        string id, IReadOnlyList<AutomationHost> hosts)
    {
        string Text(string key) => HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(host, key)).Trim();
        HostResourceContracts.ValidateAtomicFields(configuration);
        if (type is "POP3" or "SMTP" or "SSHFTP" or "FTP") ValidateHostAndPort(host, type);
        if (type is "S3" or "AzureBlob" or "GoogleStorage")
        {
            if (Text(type == "AzureBlob" ? "DefContainer" : "DefBucket").Length == 0)
                throw new InvalidOperationException("A cloud Host bucket/container is required.");
            if (!(type == "GoogleStorage" && ReadFlag(host, "UseOAuth")) && !(type == "S3" && HostCloudAuthenticationSettings.S3Method(host) != 0) && Text("DefUsername").Length == 0)
                throw new InvalidOperationException("The account/access key identifier is required.");
        }
        if (type == "OneDrive" && configuration.Any(pair => pair.Key is "TenantID" or "DefUsername" or "OAuthClientID" or "OAuthClientSecret" or HostCloudAuthenticationSettings.OneDriveModeKey || HostResourceContracts.CertificateFields.Contains(pair.Key)))
        {
            var required = new[] { "TenantID", "DefUsername", "OAuthClientID" };
            if (required.Any(key => Text(key).Length == 0) || HostCloudAuthenticationSettings.OneDriveMode(host) == "certificate" && HostResourceContracts.CertificateFields.Any(key => Text(key).Length == 0))
                throw new InvalidOperationException("Complete the selected OneDrive authentication settings before saving.");
        }
        if (configuration.ContainsKey("ID2"))
            SetAlternateHost(host, configuration, id, hosts, type, type);
        if (type == "S3" && ReadFlag(host, "IsNonAmazonEndPoint") && Text("S3AlternativeEndpoint").Length == 0)
            throw new InvalidOperationException("An alternative S3 provider requires an Alternative Provider URL.");
        if (type == "SSHFTP") ValidateSftpPermissions(host);
        if (type == "SharePoint" && configuration.Any(pair => pair.Key is "UsingAppAuthentication" or "ClientID" or "ClientSecret" or "RealmID" or "TenantID" or "TenantName" or "Site" || HostResourceContracts.CertificateFields.Contains(pair.Key)))
        {
            var mode = Text("UsingAppAuthentication");
            // A blank Site is the native default site, in both supported authentication modes.
            var required = mode == "2" ? new[] { "TenantName", "ClientID", "TenantID", "DefClientCertSHA1", "DefClientCertStore", "DefClientCertSubject" }
                : mode == "1" ? new[] { "TenantName", "ClientID", "RealmID" } : Array.Empty<string>();
            if (required.Length == 0 || required.Any(key => Text(key).Length == 0))
                throw new InvalidOperationException("Complete the selected SharePoint authentication and site settings before saving.");
        }
        if (Text("ProxyType").Length > 0 && !Text("ProxyType").Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            if (Text("ProxyHost").Length == 0 || !int.TryParse(Text("ProxyPort"), out var port) || port < 1 || port > 65535)
                throw new InvalidOperationException("An enabled proxy requires a hostname and port from 1 through 65535.");
        }
        if (type != "OneDrive" && ReadFlag(host, "UseOAuth") && configuration.Any(pair => pair.Key == "UseOAuth" || pair.Key.StartsWith("OAuth", StringComparison.Ordinal) || pair.Key == "ProjectID"))
        {
            var required = type == "GoogleStorage" ? new[] { "OAuthClientID", "ProjectID" }
                : new[] { "OAuthClientID", "OAuthScope", "OAuthTokenURL" };
            if (required.Any(key => Text(key).Length == 0))
                throw new InvalidOperationException("Complete the OAuth client and endpoint/project settings before saving.");
        }
    }

    private async Task<string> CaptureUndoAsync(string id, string username, CancellationToken cancellationToken)
    {
        var snapshot = await _undoWorkflow.CaptureAsync("Host", username, async token =>
        {
            var source = await _comClient.GetHostConfigurationAsync(id, token);
            return new UndoConfigurationSource(source.Id, source.Name, source.Xml);
        }, cancellationToken);
        return snapshot?.SnapshotId ?? "";
    }

    private static void EnsureCanEdit(AutomationHost live)
    {
        if (!ObjectCheckout.CanEditCheckedOut("Host", live.Id, live.CanEditDelete))
            throw new InvalidOperationException($"Your MOVEit account does not have permission to edit Host '{live.Name}'.");
    }

    // Local File System settings/Description are writable; MOVEit owns its identity and fixed name.
    private static void ValidateLocalFileSystemName(JsonObject host, string id, string type, string name)
    {
        if (type != "FileSystem" && id != "0") return;
        if (type != "FileSystem" || id != "0" || name != HostResourceContracts.Text(host, "Name"))
            throw new InvalidOperationException("The built-in Local File System Host cannot be renamed or replaced.");
    }

    private static void ValidateUncRequestKeys(JsonObject configuration)
    {
        var unexpected = configuration.Select(pair => pair.Key).FirstOrDefault(key => !UncWritableKeys.Contains(key));
        if (!string.IsNullOrWhiteSpace(unexpected))
            throw new InvalidOperationException($"UNC Share field '{unexpected}' is not part of the verified MAUI UNC write profile.");
    }

    private static void ValidateMoveItTransferRequestKeys(JsonObject configuration)
    {
        var unexpected = configuration.Select(pair => pair.Key).FirstOrDefault(key => !MoveItTransferWritableKeys.Contains(key));
        if (!string.IsNullOrWhiteSpace(unexpected))
            throw new InvalidOperationException($"MOVEit Transfer field '{unexpected}' is not part of the verified MAUI write profile.");
    }

    private static void ApplyUncFields(
        JsonObject host,
        JsonObject configuration,
        string currentHostId,
        IReadOnlyList<AutomationHost> hosts)
    {
        if (configuration.ContainsKey("UNC"))
        {
            var unc = GetString(configuration, "UNC").Trim();
            if (unc.Length == 0) throw new InvalidOperationException("Resource UNC is required.");
            if (!unc.StartsWith("\\\\", StringComparison.Ordinal))
                throw new InvalidOperationException("Resource UNC must begin with \\\\.");
            Set(host, "UNC", unc);
        }

        if (configuration.ContainsKey("DriveLetter"))
        {
            var drive = GetString(configuration, "DriveLetter").Trim().ToUpperInvariant();
            if (drive.Length > 0 && (drive.Length != 2 || drive[0] < 'D' || drive[0] > 'Z' || drive[1] != ':'))
                throw new InvalidOperationException("Drive Letter must be None or D: through Z:.");
            Set(host, "DriveLetter", drive);
        }
        if (configuration.ContainsKey("UseNotif")) Set(host, "UseNotif", Flag(GetBool(configuration, "UseNotif")));
        if (configuration.ContainsKey("DefUsername")) Set(host, "DefUsername", GetString(configuration, "DefUsername"));
        HostAuthenticationSettings.ApplyUnc(host, configuration);

        if (configuration.ContainsKey("DefMxFi")) Set(host, "DefMxFi", ReadNonNegativeWholeNumber(configuration, "DefMxFi", "File Count Download Limit"));
        if (configuration.ContainsKey("DefMxBy")) Set(host, "DefMxBy", ReadNonNegativeWholeNumber(configuration, "DefMxBy", "File Size Download Limit"));
        if (configuration.ContainsKey("DefRescanSecs")) Set(host, "DefRescanSecs", ReadNonNegativeWholeNumber(configuration, "DefRescanSecs", "Transfer Rescan Time"));
        if (configuration.ContainsKey("DefRetryCount")) Set(host, "DefRetryCount", ReadNonNegativeWholeNumber(configuration, "DefRetryCount", "Retry Count"));
        if (configuration.ContainsKey("DefRetryTimeoutSecs")) Set(host, "DefRetryTimeoutSecs", ReadNonNegativeWholeNumber(configuration, "DefRetryTimeoutSecs", "Retry Timeout"));
        if (configuration.ContainsKey("DefWinCopyFileAPI")) Set(host, "DefWinCopyFileAPI", Flag(GetBool(configuration, "DefWinCopyFileAPI")));

        if (configuration.ContainsKey("ID2"))
        {
            var alternateHostId = GetString(configuration, "ID2").Trim();
            if (alternateHostId.Length > 0)
            {
                if (alternateHostId.Equals(currentHostId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("A UNC Share Host cannot use itself as its Alternate Host.");
                var alternate = hosts.FirstOrDefault(item => item.Id.Equals(alternateHostId, StringComparison.OrdinalIgnoreCase));
                if (alternate is null || !alternate.TypeCode.Equals("Share", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Alternate Host must reference another visible UNC Share Host.");
            }
            Set(host, "ID2", alternateHostId);
        }

        if (configuration.ContainsKey("DelOldStateDays")) Set(host, "DelOldStateDays", ReadNonNegativeWholeNumber(configuration, "DelOldStateDays", "Delete State After"));
        if (configuration.ContainsKey("AdjDST")) Set(host, "AdjDST", Flag(GetBool(configuration, "AdjDST")));
        if (configuration.ContainsKey("DefPreserveFileTimestamp")) Set(host, "DefPreserveFileTimestamp", Flag(GetBool(configuration, "DefPreserveFileTimestamp")));
        HostStateCacheSettings.Apply(host, configuration);
    }


    private static void ApplyMoveItTransferFields(
        JsonObject host,
        JsonObject configuration,
        string currentHostId,
        IReadOnlyList<AutomationHost> hosts)
    {
        if (configuration.ContainsKey("Host"))
        {
            var hostName = GetString(configuration, "Host").Trim();
            if (hostName.Length == 0) throw new InvalidOperationException("Hostname / IP Address is required.");
            Set(host, "Host", hostName);
        }

        if (configuration.ContainsKey("Port"))
            Set(host, "Port", ReadPort(configuration, "Port"));

        if (configuration.ContainsKey("ScriptPath"))
            Set(host, "ScriptPath", GetString(configuration, "ScriptPath"));

        if (configuration.ContainsKey("DefSort"))
        {
            var sort = GetString(configuration, "DefSort").Trim();
            if (!MoveItTransferSortValues.Contains(sort))
                throw new InvalidOperationException("Sort Files By must be one of the supported MOVEit Transfer sort values.");
            Set(host, "DefSort", sort);
        }

        if (configuration.ContainsKey("UseNotif")) Set(host, "UseNotif", Flag(GetBool(configuration, "UseNotif")));
        if (configuration.ContainsKey("DefUsername")) Set(host, "DefUsername", GetString(configuration, "DefUsername"));
        HostAuthenticationSettings.ApplyPassword(host, configuration);
        foreach (var key in HostResourceContracts.CertificateFields)
            if (configuration.ContainsKey(key)) Set(host, key, GetString(configuration, key));
        if (configuration.ContainsKey("Secure")) Set(host, "Secure", Flag(GetBool(configuration, "Secure")));
        if (configuration.ContainsKey("IgnoreCertProbs")) Set(host, "IgnoreCertProbs", Flag(GetBool(configuration, "IgnoreCertProbs")));

        if (configuration.ContainsKey("DefMxFi")) Set(host, "DefMxFi", ReadNonNegativeWholeNumber(configuration, "DefMxFi", "File Count Download Limit"));
        if (configuration.ContainsKey("DefMxBy")) Set(host, "DefMxBy", ReadNonNegativeWholeNumber(configuration, "DefMxBy", "File Size Download Limit"));
        if (configuration.ContainsKey("DefConnTimeoutSecs")) Set(host, "DefConnTimeoutSecs", ReadNonNegativeWholeNumber(configuration, "DefConnTimeoutSecs", "Connect Timeout"));
        if (configuration.ContainsKey("DefDataTimeoutSecs")) Set(host, "DefDataTimeoutSecs", ReadNonNegativeWholeNumber(configuration, "DefDataTimeoutSecs", "Data Timeout"));
        if (configuration.ContainsKey("DefRetryCount")) Set(host, "DefRetryCount", ReadNonNegativeWholeNumber(configuration, "DefRetryCount", "Retry Count"));
        if (configuration.ContainsKey("DefRetryTimeoutSecs")) Set(host, "DefRetryTimeoutSecs", ReadNonNegativeWholeNumber(configuration, "DefRetryTimeoutSecs", "Retry Timeout"));

        if (configuration.ContainsKey("ID2"))
        {
            var alternateHostId = GetString(configuration, "ID2").Trim();
            if (alternateHostId.Length > 0)
            {
                if (alternateHostId.Equals(currentHostId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("A MOVEit Transfer Host cannot use itself as its Alternate Host.");
                var alternate = hosts.FirstOrDefault(item => item.Id.Equals(alternateHostId, StringComparison.OrdinalIgnoreCase));
                if (alternate is null || !alternate.TypeCode.Equals("siLock", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Alternate Host must reference another visible MOVEit Transfer Host.");
            }
            Set(host, "ID2", alternateHostId);
        }

        if (configuration.ContainsKey("DelOldStateDays"))
            Set(host, "DelOldStateDays", ReadNonNegativeWholeNumber(configuration, "DelOldStateDays", "Delete State After"));
        HostStateCacheSettings.Apply(host, configuration);
    }

    private static void SetAlternateHost(
        JsonObject host,
        JsonObject configuration,
        string currentHostId,
        IReadOnlyList<AutomationHost> hosts,
        string requiredTypeCode,
        string label)
    {
        var alternateHostId = GetString(configuration, "ID2").Trim();
        if (alternateHostId.Length > 0)
        {
            if (alternateHostId.Equals(currentHostId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"A {label} Host cannot use itself as its Alternate Host.");
            var alternate = hosts.FirstOrDefault(item => item.Id.Equals(alternateHostId, StringComparison.OrdinalIgnoreCase));
            if (alternate is null || !alternate.TypeCode.Equals(requiredTypeCode, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Alternate Host must reference another visible {label} Host.");
        }
        Set(host, "ID2", alternateHostId);
    }

    private static void ValidateSftpPermissions(JsonObject host)
    {
        var enabledText = HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(host, "DefSetFileAttr")).Trim();
        if (!TryBool(enabledText, out var enabled) || !enabled)
            return;

        var permissions = HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(host, "DefDestFileAttr")).Trim();
        if (permissions.Length != 3 || permissions.Any(ch => ch < '0' || ch > '7'))
            throw new InvalidOperationException("Default Destination File Permissions must be a three-digit octal value from 000 through 777 when file permissions are enabled.");
    }

    private static bool ReadFlag(JsonObject host, string key)
    {
        var text = HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(host, key)).Trim();
        return TryBool(text, out var value) && value;
    }

    private static void ValidateHostAndPort(JsonObject host, string label)
    {
        var target = HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(host, "Host")).Trim();
        if (target.Length == 0)
            throw new InvalidOperationException($"Hostname / IP Address is required before this {label} Host can be saved.");

        var portText = HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(host, "Port")).Trim();
        if (!int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) || port < 1 || port > 65535)
            throw new InvalidOperationException("Port must be a whole number from 1 through 65535.");
    }

    private static void ValidateEffectiveMoveItTransferHost(JsonObject host)
    {
        var target = HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(host, "Host")).Trim();
        if (target.Length == 0)
            throw new InvalidOperationException("Hostname / IP Address is required before this MOVEit Transfer Host can be saved.");

        var portText = HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(host, "Port")).Trim();
        if (!int.TryParse(portText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) || port < 1 || port > 65535)
            throw new InvalidOperationException("Port must be a whole number from 1 through 65535.");

        var sort = HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(host, "DefSort")).Trim();
        if (sort.Length > 0 && !MoveItTransferSortValues.Contains(sort))
            throw new InvalidOperationException("The saved MOVEit Transfer sort value is not recognized by MAUI.");

    }

    private static void ValidateEffectiveUncHost(JsonObject host)
    {
        var unc = HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(host, "UNC")).Trim();
        if (unc.Length == 0)
            throw new InvalidOperationException("Resource UNC is required before this UNC Share Host can be saved.");
        if (!unc.StartsWith("\\\\", StringComparison.Ordinal))
            throw new InvalidOperationException("Resource UNC must begin with \\\\.");
    }

    private static void ApplyCommonFields(JsonObject host, string name, string description)
    {
        Set(host, "Name", name);
        Set(host, "Desc", description);
    }

    private static bool HasRequestedChanges(
        JsonObject before,
        JsonObject after,
        JsonObject request,
        IEnumerable<string> writableKeys)
    {
        foreach (var key in writableKeys)
        {
            if (key is "DefPassword" or HostAuthenticationSettings.UncModeKey || HostMailOAuthSettings.IsField(key) || HostCloudAuthenticationSettings.IsCommand(key) || key == "AuthMethod") continue;
            if (!request.ContainsKey(key)) continue;
            if (!Equivalent(HostDefinitionJson.GetNode(before, key), HostDefinitionJson.GetNode(after, key))) return true;
        }
        return HostStateCacheSettings.HasChanges(before, after, request);
    }

    private static string NormalizeNewlines(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);

    private static void VerifyCommonFields(JsonObject host, string name, string description)
    {
        if (!HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(host, "Name")).Equals(name, StringComparison.Ordinal))
            throw new InvalidOperationException("MOVEit did not persist the requested Host Name.");
        if (!NormalizeNewlines(HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(host, "Desc"))).Equals(NormalizeNewlines(description), StringComparison.Ordinal))
            throw new InvalidOperationException("MOVEit did not persist the requested Host Description.");
    }

    private static void VerifyRequestedFields(
        JsonObject expected,
        JsonObject actual,
        JsonObject request,
        IEnumerable<string> writableKeys,
        string profileLabel)
    {
        foreach (var key in writableKeys)
        {
            // Native GET suppresses secrets; editor helper fields have no native representation.
            if (key is "DefPassword" or HostAuthenticationSettings.UncModeKey || HostMailOAuthSettings.IsField(key) || HostCloudAuthenticationSettings.IsCommand(key) || key == "AuthMethod") continue;
            if (profileLabel == "FTP" && HostFtpDirectoryParsing.IsField(key)) continue;
            if (!request.ContainsKey(key)) continue;
            if (key == "DefClientCertSHA1" && HostResourceContracts.Thumbprint(HostResourceContracts.Text(expected, key)) == HostResourceContracts.Thumbprint(HostResourceContracts.Text(actual, key))) continue;
            if (!Equivalent(HostDefinitionJson.GetNode(expected, key), HostDefinitionJson.GetNode(actual, key)))
                throw new InvalidOperationException($"MOVEit did not persist the requested '{key}' {profileLabel} Host value.");
        }
        HostCloudAuthenticationSettings.Verify(expected, actual, request, profileLabel);
        HostStateCacheSettings.Verify(expected, actual, request);
        if (profileLabel == "Share") HostAuthenticationSettings.VerifyUnc(expected, actual, request);
        if (profileLabel is "POP3" or "SMTP") HostMailOAuthSettings.Verify(expected, actual, request);
        if (profileLabel == "FTP") HostFtpDirectoryParsing.Verify(expected, actual, request);
    }

    private static string ResolveTypedWrapperKey(JsonObject wrapper, string normalizedType, string fallback)
    {
        foreach (var pair in wrapper)
        {
            if (pair.Value is not JsonObject) continue;
            if (pair.Key.Equals(normalizedType, StringComparison.OrdinalIgnoreCase))
                return pair.Key;
        }
        return fallback;
    }

    private static void ValidatePutResponse(JsonObject response, string hostId)
    {
        var status = HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(response, "status")).Trim();
        var responseId = HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(response, "id")).Trim();
        var errorCodeText = HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(response, "errorcode")).Trim();
        var errorCode = int.TryParse(errorCodeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : -1;
        if (!status.Equals("success", StringComparison.OrdinalIgnoreCase) || errorCode != 0)
        {
            var message = HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(response, "message")).Trim();
            throw new MoveItApiException(message.Length > 0 ? message : "MOVEit rejected the Host update.");
        }
        if (responseId.Length > 0 && !responseId.Equals(hostId, StringComparison.OrdinalIgnoreCase))
            throw new MoveItApiException("MOVEit returned an unexpected Host ID after the Host update.");
    }

    private static bool Equivalent(JsonNode? left, JsonNode? right)
    {
        var leftText = HostDefinitionJson.ReadNode(left).Trim().Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
        var rightText = HostDefinitionJson.ReadNode(right).Trim().Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
        if (long.TryParse(leftText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var leftNumber)
            && long.TryParse(rightText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rightNumber))
            return leftNumber == rightNumber;
        if (TryBool(leftText, out var leftBool) && TryBool(rightText, out var rightBool))
            return leftBool == rightBool;
        return leftText.Equals(rightText, StringComparison.Ordinal);
    }

    private static bool TryBool(string value, out bool result)
    {
        if (value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            result = true;
            return true;
        }
        if (value.Equals("0", StringComparison.OrdinalIgnoreCase)
            || value.Equals("false", StringComparison.OrdinalIgnoreCase)
            || value.Equals("no", StringComparison.OrdinalIgnoreCase)
            || value.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            result = false;
            return true;
        }
        result = false;
        return false;
    }

    private static long ReadNonNegativeWholeNumber(JsonObject request, string key, string label)
    {
        var text = HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(request, key)).Trim();
        if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0)
            throw new InvalidOperationException($"{label} must be a whole number greater than or equal to 0.");
        return value;
    }

    private static int ReadPort(JsonObject request, string key)
    {
        var text = HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(request, key)).Trim();
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 1 || value > 65535)
            throw new InvalidOperationException("Port must be a whole number from 1 through 65535.");
        return value;
    }

    private static string GetString(JsonObject request, string key)
        => HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(request, key));

    private static bool GetBool(JsonObject request, string key)
    {
        var value = GetString(request, key).Trim();
        if (TryBool(value, out var result)) return result;
        throw new InvalidOperationException($"{key} must be Yes or No.");
    }

    private static int Flag(bool value) => value ? 1 : 0;

    private static void Set(JsonObject obj, string key, JsonNode? value)
    {
        var actual = obj.Select(pair => pair.Key).FirstOrDefault(candidate => candidate.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? key;
        obj[actual] = value;
    }
}
