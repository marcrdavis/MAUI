// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Globalization;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Services.MoveIt;

public interface ITraditionalTaskEditorFolderBrowseService
{
    Task<TraditionalTaskEditorFolderBrowseResult> BrowseLocalAsync(string path, CancellationToken cancellationToken = default);
    Task<TraditionalTaskEditorFolderBrowseResult> BrowseAsync(
        string taskId,
        string hostId,
        string kind,
        string path,
        string bucket = "",
        string container = "",
        string documentLibrary = "",
        string documentLibraryGuid = "",
        bool browseBuckets = false,
        bool browseContainers = false,
        bool browseDocumentLibraries = false,
        string stepId = "",
        CancellationToken cancellationToken = default);
}

// Editor-owned folder browser. Host browse support is dispatched through the editor capability catalog.
// Each enabled protocol keeps its own captured native browse contract while sharing the same checkout / release
// lifecycle used by Task Preflight / HostConnectionTestService. Protocol-specific request fields and response
// parsing remain isolated so adding one Host type cannot alter another Host type's browse behavior.
public sealed class TraditionalTaskEditorFolderBrowseService : ITraditionalTaskEditorFolderBrowseService
{
    private readonly IMoveItRestClient _restClient;
    private readonly ILogger<TraditionalTaskEditorFolderBrowseService> _logger;

    public TraditionalTaskEditorFolderBrowseService(
        IMoveItRestClient restClient,
        ILogger<TraditionalTaskEditorFolderBrowseService> logger)
    {
        _restClient = restClient;
        _logger = logger;
    }

    public async Task<TraditionalTaskEditorFolderBrowseResult> BrowseAsync(
        string taskId,
        string hostId,
        string kind,
        string path,
        string bucket = "",
        string container = "",
        string documentLibrary = "",
        string documentLibraryGuid = "",
        bool browseBuckets = false,
        bool browseContainers = false,
        bool browseDocumentLibraries = false,
        string stepId = "",
        CancellationToken cancellationToken = default)
    {
        var normalizedTaskId = (taskId ?? string.Empty).Trim();
        if (normalizedTaskId.Length == 0)
            throw new ArgumentException("Task ID is required before browsing a Task Step folder.", nameof(taskId));

        var normalizedHostId = (hostId ?? string.Empty).Trim();
        if (normalizedHostId.Length == 0)
            throw new ArgumentException("A Host must be selected before browsing.", nameof(hostId));

        var normalizedKind = NormalizeStepKind(kind);
        if (normalizedKind is not ("Source" or "Destination"))
            throw new ArgumentException("Folder browsing requires a Source or Destination context.", nameof(kind));

        if (normalizedHostId == "0") return await BrowseLocalAsync(path, cancellationToken);

        return await HostBrowseCheckout.ReadAsync(_restClient, normalizedHostId, async (checkout, browseToken) =>
        {
            var (typeCode, checkoutDefinition) = GetDefinition(checkout);
            if (checkoutDefinition is null)
                throw new InvalidOperationException("MOVEit did not return a Host definition that MAUI can browse.");

            var capability = TraditionalTaskEditorHostCapabilities.Get(typeCode);
            if (capability is null || !TraditionalTaskEditorHostCapabilities.CanBrowse(normalizedKind, typeCode))
                throw new InvalidOperationException($"Folder browsing is not enabled for {TraditionalTaskEditorHostCapabilities.FriendlyName(typeCode)} {normalizedKind} steps.");

            var browseProfile = TraditionalTaskEditorHostCapabilities.BrowseProfile(normalizedKind, typeCode);
            if (browseProfile.Equals(TraditionalTaskEditorHostCapabilities.ShareBrowseProfile, StringComparison.OrdinalIgnoreCase))
                return await BrowseShareAsync(normalizedHostId, capability, checkoutDefinition, path, browseToken);

            if (browseProfile.Equals(TraditionalTaskEditorHostCapabilities.MoveItTransferBrowseProfile, StringComparison.OrdinalIgnoreCase))
                return await BrowseMoveItTransferAsync(normalizedTaskId, normalizedHostId, capability, checkoutDefinition, path, browseToken);

            if (browseProfile.Equals(TraditionalTaskEditorHostCapabilities.FtpBrowseProfile, StringComparison.OrdinalIgnoreCase))
                return await BrowseFtpAsync(normalizedTaskId, normalizedHostId, capability, checkoutDefinition, path, browseToken);

            if (browseProfile.Equals(TraditionalTaskEditorHostCapabilities.SftpBrowseProfile, StringComparison.OrdinalIgnoreCase))
                return await BrowseSftpAsync(normalizedTaskId, normalizedHostId, capability, checkoutDefinition, path, browseToken);

            if (browseProfile.Equals(TraditionalTaskEditorHostCapabilities.S3BrowseProfile, StringComparison.OrdinalIgnoreCase))
                return await BrowseS3Async(normalizedTaskId, normalizedHostId, normalizedKind, capability, checkoutDefinition, path, bucket, browseBuckets, stepId, browseToken);

            if (browseProfile.Equals(TraditionalTaskEditorHostCapabilities.AzureBlobBrowseProfile, StringComparison.OrdinalIgnoreCase))
                return await BrowseAzureBlobAsync(normalizedTaskId, normalizedHostId, normalizedKind, capability, checkoutDefinition, path, container, browseContainers, browseToken);

            if (browseProfile.Equals(TraditionalTaskEditorHostCapabilities.GoogleStorageBrowseProfile, StringComparison.OrdinalIgnoreCase))
                return await BrowseGoogleStorageAsync(normalizedTaskId, normalizedHostId, normalizedKind, capability, checkoutDefinition, path, bucket, browseBuckets, browseToken);

            if (browseProfile.Equals(TraditionalTaskEditorHostCapabilities.SharePointBrowseProfile, StringComparison.OrdinalIgnoreCase))
                return await BrowseSharePointAsync(normalizedTaskId, normalizedHostId, normalizedKind, capability, checkoutDefinition, path, documentLibrary, documentLibraryGuid, browseDocumentLibraries, browseToken);

            throw new InvalidOperationException($"The {TraditionalTaskEditorHostCapabilities.FriendlyName(typeCode)} browse profile is not implemented by this MAUI build.");
        }, cancellationToken);
    }

    public async Task<TraditionalTaskEditorFolderBrowseResult> BrowseLocalAsync(string path, CancellationToken cancellationToken)
    {
        var requested = NormalizeLocalPath(path);
        var response = await _restClient.BrowseFileSystemHostV0Async(new JsonObject
        {
            ["showfiles"] = true, ["mask"] = "", ["hostid"] = "0",
            ["username"] = "", ["password"] = null, ["dirname"] = requested == "/" ? "" : requested
        }, cancellationToken);
        var current = NormalizeLocalPath(FirstNonBlank(Read(response, "CurrentDir"), requested));
        var items = ParseBrowseItems(response, current, NormalizeLocalPath)
            .Select(item => item with { Path = NormalizeLocalPath(item.Path) }).ToArray();
        var parent = current == "/" ? "" : current.Length == 3 && current[1] == ':' ? "/"
            : current.LastIndexOf('\\') is var separator && separator >= 2
                ? NormalizeLocalPath(current[..separator]) : "/";
        return new TraditionalTaskEditorFolderBrowseResult("0", "Local File System", "Local File System", current, parent, items);
    }

    internal static string NormalizeLocalPath(string? value)
    {
        var path = (value ?? "").Trim().Replace('/', '\\');
        if (path.Length == 0 || path == "\\") return "/";
        // Accept drive paths returned by the native browser, including C: drive entries.
        if (path.Length >= 3 && path[0] == '\\' && char.IsLetter(path[1]) && path[2] == ':') path = path[1..];
        if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')
        {
            path = char.ToUpperInvariant(path[0]) + path[1..];
            if (path.Length == 2) path += "\\";
            return path.Length == 3 ? path : path.TrimEnd('\\');
        }
        if (path.StartsWith("\\\\", StringComparison.Ordinal)) return path.TrimEnd('\\');
        throw new InvalidOperationException("Choose an absolute drive or UNC path on the MOVEit Automation server.");
    }

    private async Task<TraditionalTaskEditorFolderBrowseResult> BrowseShareAsync(
        string hostId,
        TraditionalTaskEditorHostCapability capability,
        JsonObject checkoutDefinition,
        string path,
        CancellationToken cancellationToken)
    {
        var hostDefinition = checkoutDefinition;
        if (Read(hostDefinition, "UNC").Length == 0 ||
            Read(hostDefinition, "Name").Length == 0 ||
            Read(hostDefinition, "DefUsername").Length == 0 ||
            Read(hostDefinition, "DefPreserveFileTimestamp").Length == 0)
        {
            var current = await _restClient.GetHostAsync(hostId, cancellationToken);
            var (currentType, currentDefinition) = GetDefinition(current);
            if (currentDefinition is not null && TraditionalTaskEditorHostCapabilities.NormalizeTypeCode(currentType).Equals("Share", StringComparison.OrdinalIgnoreCase))
                hostDefinition = MergeMissing(hostDefinition, currentDefinition, "Name", "UNC", "DefUsername", "DefPreserveFileTimestamp");
        }

        var hostName = Read(hostDefinition, "Name");
        var shareName = Read(hostDefinition, "UNC");
        if (shareName.Length == 0)
            throw new InvalidOperationException("The selected UNC Share Host does not contain a UNC path that MAUI can browse.");

        var requestedPath = NormalizeEditorPath(path);
        var browseDirectory = ResolveBrowseDirectory(shareName, requestedPath);
        var request = new JsonObject
        {
            ["showfiles"] = true,
            ["mask"] = string.Empty,
            ["hostid"] = hostId,
            ["username"] = Read(hostDefinition, "DefUsername"),
            ["password"] = null,
            ["dirname"] = browseDirectory
        };

        var preserveTimestamp = ReadNullableFlag(hostDefinition, "DefPreserveFileTimestamp");
        if (preserveTimestamp.HasValue)
            request["preservefiletimestamp"] = preserveTimestamp.Value;

        var response = await _restClient.BrowseFileSystemHostV0Async(request, cancellationToken);
        var responseDirectory = FirstNonBlank(Read(response, "CurrentDir"), browseDirectory);
        var currentPath = ToShareEditorPath(shareName, responseDirectory);
        var parentPath = ParentEditorPath(currentPath);
        var items = ParseBrowseItems(response, currentPath, returnedPath => ToShareEditorPath(shareName, returnedPath));

        return new TraditionalTaskEditorFolderBrowseResult(hostId, hostName, capability.Type, currentPath, parentPath, items);
    }

    private async Task<TraditionalTaskEditorFolderBrowseResult> BrowseMoveItTransferAsync(
        string taskId,
        string hostId,
        TraditionalTaskEditorHostCapability capability,
        JsonObject checkoutDefinition,
        string path,
        CancellationToken cancellationToken)
    {
        // Build the request from the same checked-out Host/default data used by Task Preflight, then
        // emit the exact native Web Admin /api/v0/hosts/dmz/browse payload captured from MOVEit.
        // Only presentation of the returned folders/files is editor-specific.
        var effective = await EnrichDefinitionAsync(
            hostId,
            "siLock",
            checkoutDefinition,
            cancellationToken,
            "Name",
            "Host",
            "DefUsername",
            "Port",
            "ScriptPath",
            "Secure",
            "IgnoreCertProbs",
            "DefClientCertStore",
            "DefClientCertSubject",
            "DefClientCertSHA1",
            "DefConnTimeoutSecs",
            "DefDataTimeoutSecs");

        var hostName = Read(effective, "Name");
        var server = Read(effective, "Host");
        var username = Read(effective, "DefUsername");
        var port = ReadNullableInt(effective, "Port") ?? 0;

        var missing = new List<string>();
        if (server.Length == 0) missing.Add("Host");
        if (port <= 0) missing.Add("Port");
        if (username.Length == 0) missing.Add("Username");
        if (missing.Count > 0)
            throw new InvalidOperationException($"The selected MOVEit Transfer Host is incomplete. Missing: {string.Join(", ", missing)}.");

        // Match the native Web Admin /api/v0/hosts/dmz/browse request exactly.  In particular,
        // the three client-certificate members are present even when blank, and taskid identifies
        // the Task being edited.  Omitting those members caused MOVEit validation errors in 250.
        var loginInfo = new JsonObject
        {
            ["serverip"] = server,
            ["serverport"] = port,
            ["scriptpath"] = FirstNonBlank(Read(effective, "ScriptPath"), "/"),
            ["secure"] = ReadNullableFlag(effective, "Secure") ?? false,
            ["ignorecertprobs"] = ReadNullableFlag(effective, "IgnoreCertProbs") ?? false,
            ["username"] = username,
            // MOVEit resolves the saved protected Host password internally when hostid is supplied.
            ["password"] = null,
            ["clientcertstore"] = Read(effective, "DefClientCertStore"),
            ["clientcertsubject"] = Read(effective, "DefClientCertSubject"),
            ["clientcertsha1"] = Read(effective, "DefClientCertSHA1"),
            ["conntimeoutsecs"] = ReadNullableInt(effective, "DefConnTimeoutSecs") ?? 60,
            ["datatimeoutsecs"] = ReadNullableInt(effective, "DefDataTimeoutSecs") ?? 60
        };

        var browseDirectory = string.IsNullOrWhiteSpace(path) ? "/" : path.Trim();
        var request = new JsonObject
        {
            ["showfiles"] = true,
            ["mask"] = string.Empty,
            ["hostid"] = hostId,
            ["logininfo"] = loginInfo,
            ["folderid"] = -1,
            ["folderpath"] = browseDirectory,
            ["taskid"] = taskId,
            ["stepid"] = null
        };

        var response = await _restClient.BrowseDmzHostV0Async(request, cancellationToken);
        var currentPath = NormalizeEditorPath(FirstNonBlank(Read(response, "CurrentDir"), browseDirectory));
        var parentPath = ParentEditorPath(currentPath);
        var items = ParseBrowseItems(response, currentPath, NormalizeEditorPath);

        return new TraditionalTaskEditorFolderBrowseResult(hostId, hostName, capability.Type, currentPath, parentPath, items);
    }

    private async Task<TraditionalTaskEditorFolderBrowseResult> BrowseSftpAsync(
        string taskId, string hostId, TraditionalTaskEditorHostCapability capability, JsonObject checkoutDefinition,
        string path, CancellationToken cancellationToken)
    {
        var effective = await EnrichDefinitionAsync(
            hostId, "SSHFTP", checkoutDefinition, cancellationToken,
            "Name", "Host", "DefUsername", "Port", "DefConnTimeoutSecs", "EncList",
            "IgnoreCertProbs", "DefClientKeyID", "SSHFingerprint", "ProxyType",
            "OptimizeSSHBuf", "DefBlindDL", "DefPreserveFileTimestamp");

        var hostName = Read(effective, "Name");
        var server = Read(effective, "Host");
        var username = Read(effective, "DefUsername");
        var port = ReadNullableInt(effective, "Port") ?? 0;
        var proxyType = Read(effective, "ProxyType").Trim();
        var missing = new List<string>();
        if (server.Length == 0) missing.Add("Host");
        if (port <= 0) missing.Add("Port");
        if (username.Length == 0) missing.Add("Username");
        if (missing.Count > 0) throw new InvalidOperationException($"The selected SSH/SFTP Host is incomplete. Missing: {string.Join(", ", missing)}.");
        if (ReadNullableFlag(effective, "DefBlindDL") == true)
            throw new InvalidOperationException("The selected SSH/SFTP Host uses Blind Downloads, so MOVEit does not provide a directory listing for Browse.");

        var loginInfo = new JsonObject
        {
            ["serverip"] = server, ["serverport"] = port, ["username"] = username, ["password"] = null,
            ["customdir"] = 0, ["customdircoldate"] = 0, ["customdircolfilename"] = 0,
            ["customdirskipbottom"] = 0, ["customdirskiptop"] = 0, ["dirscriptid"] = string.Empty
        };
        AddFlagIfPresent(loginInfo, "ignorecertprobs", effective, "IgnoreCertProbs");
        AddStringIfPresent(loginInfo, "clientkeyid", effective, "DefClientKeyID");
        AddStringIfPresent(loginInfo, "hostfingerprint", effective, "SSHFingerprint");
        AddIntIfPresent(loginInfo, "conntimeoutsecs", effective, "DefConnTimeoutSecs");
        AddFlagIfPresent(loginInfo, "optimizesshbuf", effective, "OptimizeSSHBuf");
        if (proxyType.Length > 0) loginInfo["proxytype"] = proxyType;
        AddStringIfPresent(loginInfo, "encryptionlist", effective, "EncList");
        AddFlagIfPresent(loginInfo, "blinddownloads", effective, "DefBlindDL");
        AddFlagIfPresent(loginInfo, "preservefiletimestamp", effective, "DefPreserveFileTimestamp");

        var browseDirectory = string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim();
        HostBrowseSettings.ApplyProxy(loginInfo, effective, "SSHFTP");

        var request = new JsonObject
        {
            ["showfiles"] = true, ["mask"] = string.Empty, ["hostid"] = hostId, ["logininfo"] = loginInfo,
            ["dirname"] = browseDirectory, ["taskid"] = null, ["stepid"] = null
        };
        var response = await _restClient.BrowseSshHostV0Async(request, cancellationToken);
        var currentPath = NormalizeEditorPath(FirstNonBlank(Read(response, "CurrentDir"), browseDirectory, "/"));
        return new TraditionalTaskEditorFolderBrowseResult(hostId, hostName, capability.Type, currentPath, ParentEditorPath(currentPath),
            ParseBrowseItems(response, currentPath, NormalizeEditorPath));
    }


    private async Task<TraditionalTaskEditorFolderBrowseResult> BrowseS3Async(
        string taskId,
        string hostId,
        string kind,
        TraditionalTaskEditorHostCapability capability,
        JsonObject checkoutDefinition,
        string path,
        string bucket,
        bool browseBuckets,
        string stepId,
        CancellationToken cancellationToken)
    {
        // The request body is based on the native Web Admin payload captured from MOVEit 2025.1.
        // The exact success response shape was not published in the Progress REST documentation, so
        // response parsing deliberately accepts only the same directory/file collection shapes already
        // observed on native v0 browse endpoints plus a conservative Buckets/Bucket collection.
        var effective = await EnrichDefinitionAsync(
            hostId,
            "S3",
            checkoutDefinition,
            cancellationToken,
            "Name", "DefUsername", "Secure", "DefConnTimeoutSecs", "DefDataTimeoutSecs",
            "ProxyType", "DefBucket", "S3AlternativeEndpoint");

        var hostName = Read(effective, "Name");
        var username = Read(effective, "DefUsername");
        var proxyType = Read(effective, "ProxyType").Trim();
        if (HostCloudAuthenticationSettings.S3Method(effective) == 0 && username.Length == 0)
            throw new InvalidOperationException("The selected Amazon S3 Host does not contain an Access Key ID that MAUI can use for Browse.");

        var loginInfo = new JsonObject
        {
            ["secure"] = ReadNullableFlag(effective, "Secure") ?? true,
            ["username"] = username,
            // MOVEit resolves the saved protected Secret Access Key internally when hostid is supplied.
            ["password"] = null,
            ["conntimeoutsecs"] = ReadNullableInt(effective, "DefConnTimeoutSecs") ?? 60,
            ["datatimeoutsecs"] = ReadNullableInt(effective, "DefDataTimeoutSecs") ?? 60,
            ["proxytype"] = proxyType.Length == 0 ? "None" : proxyType,
            ["proxypassword"] = null
        };

        var requestedBucket = (bucket ?? string.Empty).Trim();
        var browseDirectory = string.IsNullOrWhiteSpace(path) ? "/" : NormalizeEditorPath(path);
        HostBrowseSettings.ApplyProxy(loginInfo, effective, "S3");

        var request = new JsonObject
        {
            ["showfiles"] = !browseBuckets,
            ["mask"] = string.Empty,
            ["hostid"] = hostId,
            ["logininfo"] = loginInfo,
            ["dirname"] = browseDirectory,
            ["taskid"] = taskId,
            ["stepid"] = string.IsNullOrWhiteSpace(stepId) ? null : stepId.Trim(),
            ["bucket"] = browseBuckets ? string.Empty : requestedBucket,
            ["endpoint"] = HostBrowseSettings.Flag(effective, "IsNonAmazonEndPoint") ? Read(effective, "S3AlternativeEndpoint") : string.Empty
        };

        if (!browseBuckets && requestedBucket.Length == 0)
            throw new InvalidOperationException($"Select the {kind} Amazon S3 Bucket Name before browsing folders.");

        HostCloudAuthenticationSettings.ApplyS3Browse(request, effective);
        var response = await _restClient.BrowseS3HostV0Async(request, cancellationToken);

        if (browseBuckets)
        {
            var buckets = ParseS3BucketItems(response);
            return new TraditionalTaskEditorFolderBrowseResult(
                hostId,
                hostName,
                capability.Type,
                "/",
                string.Empty,
                buckets);
        }

        var currentPath = NormalizeEditorPath(FirstNonBlank(Read(response, "CurrentDir"), Read(response, "DirName"), browseDirectory, "/"));
        var items = ParseS3FolderItems(response, currentPath);
        return new TraditionalTaskEditorFolderBrowseResult(
            hostId,
            hostName,
            capability.Type,
            currentPath,
            ParentEditorPath(currentPath),
            items);
    }

    private async Task<TraditionalTaskEditorFolderBrowseResult> BrowseAzureBlobAsync(
        string taskId,
        string hostId,
        string kind,
        TraditionalTaskEditorHostCapability capability,
        JsonObject checkoutDefinition,
        string path,
        string container,
        bool browseContainers,
        CancellationToken cancellationToken)
    {
        // Request body follows the native MOVEit 2025.1 Azure Blob browse payload captured from Web Admin.
        // Container listing uses showfiles=false with an empty container; folder browsing uses the selected
        // container with showfiles=true. The successful container response supplied for this work uses the
        // common Buckets/Bucket collection shape, so that shape is accepted explicitly.
        var effective = await EnrichDefinitionAsync(
            hostId,
            "AzureBlob",
            checkoutDefinition,
            cancellationToken,
            "Name", "DefUsername", "Secure", "DefConnTimeoutSecs", "DefDataTimeoutSecs",
            "ProxyType", "DefContainer");

        var hostName = Read(effective, "Name");
        var username = Read(effective, "DefUsername");
        var proxyType = Read(effective, "ProxyType").Trim();
        if (username.Length == 0)
            throw new InvalidOperationException("The selected Azure Blob Host does not contain a Storage Account that MAUI can use for Browse.");

        var loginInfo = new JsonObject
        {
            ["secure"] = ReadNullableFlag(effective, "Secure") ?? true,
            ["username"] = username,
            // MOVEit resolves the saved protected Azure Storage Key internally when hostid is supplied.
            ["password"] = null,
            ["conntimeoutsecs"] = ReadNullableInt(effective, "DefConnTimeoutSecs") ?? 60,
            ["datatimeoutsecs"] = ReadNullableInt(effective, "DefDataTimeoutSecs") ?? 60,
            ["proxytype"] = proxyType.Length == 0 ? "None" : proxyType,
            ["proxypassword"] = null
        };

        var requestedContainer = (container ?? string.Empty).Trim();
        var browseDirectory = string.IsNullOrWhiteSpace(path)
            ? (browseContainers ? string.Empty : "/")
            : NormalizeEditorPath(path);
        HostBrowseSettings.ApplyProxy(loginInfo, effective, "AzureBlob");

        var request = new JsonObject
        {
            ["showfiles"] = !browseContainers,
            ["mask"] = string.Empty,
            ["hostid"] = hostId,
            ["logininfo"] = loginInfo,
            ["dirname"] = browseContainers ? string.Empty : browseDirectory,
            ["taskid"] = taskId,
            // Native Azure Blob browse requests captured from MOVEit Web Admin send stepid as null.
            ["stepid"] = null,
            ["container"] = browseContainers ? string.Empty : requestedContainer
        };

        if (!browseContainers && requestedContainer.Length == 0)
            throw new InvalidOperationException($"Select the {kind} Azure Blob Container before browsing folders.");

        var response = await _restClient.BrowseAzureBlobHostV0Async(request, cancellationToken);

        if (browseContainers)
        {
            return new TraditionalTaskEditorFolderBrowseResult(
                hostId,
                hostName,
                capability.Type,
                "/",
                string.Empty,
                ParseAzureBlobNamedItems(response, "/"));
        }

        var currentPath = NormalizeEditorPath(FirstNonBlank(Read(response, "CurrentDir"), Read(response, "DirName"), browseDirectory, "/"));
        var items = ParseAzureBlobFolderItems(response, currentPath);
        if (items.Count == 0)
        {
            // Some MOVEit cloud browse responses use the generic Buckets/Bucket collection even for
            // navigable named entries. Accept that captured shape as a folder-list fallback for Azure.
            items = ParseAzureBlobNamedItems(response, currentPath);
        }
        return new TraditionalTaskEditorFolderBrowseResult(
            hostId,
            hostName,
            capability.Type,
            currentPath,
            ParentEditorPath(currentPath),
            items);
    }


    private async Task<TraditionalTaskEditorFolderBrowseResult> BrowseGoogleStorageAsync(
        string taskId,
        string hostId,
        string kind,
        TraditionalTaskEditorHostCapability capability,
        JsonObject checkoutDefinition,
        string path,
        string bucket,
        bool browseBuckets,
        CancellationToken cancellationToken)
    {
        // Request bodies follow the supplied native HMAC/OAuth Google Storage browse captures.
        // Both modes use the same response parser, with authentication fields
        // populated by the shared authentication helper; MOVEit resolves the saved key by hostid.
        var effective = await EnrichDefinitionAsync(
            hostId,
            "GoogleStorage",
            checkoutDefinition,
            cancellationToken,
            "Name", "DefUsername", "Secure", "DefConnTimeoutSecs", "DefDataTimeoutSecs",
            "ProxyType", "DefBucket", "UseOAuth");

        var hostName = Read(effective, "Name");
        var username = Read(effective, "DefUsername");
        var proxyType = Read(effective, "ProxyType").Trim();
        var useOAuth = ReadNullableFlag(effective, "UseOAuth") ?? false;
        if (!useOAuth && username.Length == 0)
            throw new InvalidOperationException("The selected Google Storage Host does not contain an HMAC Access Key ID that MAUI can use for Browse.");

        var loginInfo = new JsonObject
        {
            ["secure"] = ReadNullableFlag(effective, "Secure") ?? true,
            ["username"] = username,
            // Match the existing cloud Host test/browse pattern: MOVEit resolves the saved protected
            // HMAC Secret Access Key internally from hostid; MAUI never exposes it to the browser.
            ["password"] = null,
            ["conntimeoutsecs"] = ReadNullableInt(effective, "DefConnTimeoutSecs") ?? 60,
            ["datatimeoutsecs"] = ReadNullableInt(effective, "DefDataTimeoutSecs") ?? 60,
            ["proxytype"] = proxyType.Length == 0 ? "None" : proxyType
        };

        var requestedBucket = (bucket ?? string.Empty).Trim();
        var browseDirectory = browseBuckets
            ? NormalizeGoogleStorageRequestPath(path)
            : (string.IsNullOrWhiteSpace(path) ? "/" : NormalizeEditorPath(path));
        if (!browseBuckets && requestedBucket.Length == 0)
            throw new InvalidOperationException($"Select the {kind} Google Cloud Storage Bucket Name before browsing folders.");

        HostBrowseSettings.ApplyProxy(loginInfo, effective, "GoogleStorage");

        var request = new JsonObject
        {
            ["showfiles"] = !browseBuckets,
            ["mask"] = string.Empty,
            ["hostid"] = hostId,
            ["logininfo"] = loginInfo,
            ["dirname"] = browseDirectory,
            ["taskid"] = taskId,
            // The captured native Google Storage browse requests send stepid as null.
            ["stepid"] = null,
            ["bucket"] = browseBuckets ? string.Empty : requestedBucket,
            ["useoauth"] = false,
            ["projectid"] = null,
            ["oauthclientid"] = null,
            ["oauthclientsecret"] = null
        };

        HostBrowseSettings.ApplyGoogleAuthentication(request, effective);
        var response = await _restClient.BrowseGoogleStorageHostV0Async(request, cancellationToken);
        if (browseBuckets)
        {
            return new TraditionalTaskEditorFolderBrowseResult(
                hostId,
                hostName,
                capability.Type,
                "/",
                string.Empty,
                ParseGoogleStorageBucketItems(response));
        }

        var currentPath = NormalizeEditorPath(FirstNonBlank(Read(response, "CurrentDir"), browseDirectory, "/"));
        return new TraditionalTaskEditorFolderBrowseResult(
            hostId,
            hostName,
            capability.Type,
            currentPath,
            ParentEditorPath(currentPath),
            ParseGoogleStorageFolderItems(response, currentPath));
    }


    private async Task<TraditionalTaskEditorFolderBrowseResult> BrowseSharePointAsync(
        string taskId,
        string hostId,
        string kind,
        TraditionalTaskEditorHostCapability capability,
        JsonObject checkoutDefinition,
        string path,
        string documentLibrary,
        string documentLibraryGuid,
        bool browseDocumentLibraries,
        CancellationToken cancellationToken)
    {
        // Request body follows the native MOVEit 2025.1 SharePoint browse payload captured from Web Admin.
        // App and Certificate Access reuse this envelope and parser. Deprecated User Access
        // stays excluded; the shared contract owns the selected authentication/proxy fields.
        var effective = await EnrichDefinitionAsync(
            hostId,
            "SharePoint",
            checkoutDefinition,
            cancellationToken,
            "Name", "Secure", "DefConnTimeoutSecs", "DefDataTimeoutSecs", "ProxyType",
            "TenantName", "SiteUrlPrefix", "Site", "UsingAppAuthentication", "ClientID", "TenantID",
            "DefDocumentLibrary", "DefDocumentLibraryGuid",
            "DefClientCertStore", "DefClientCertSubject", "DefClientCertSHA1");

        var hostName = Read(effective, "Name");
        var requestedLibrary = (documentLibrary ?? string.Empty).Trim();
        var requestedLibraryGuid = (documentLibraryGuid ?? string.Empty).Trim();
        if (!browseDocumentLibraries && requestedLibrary.Length == 0)
            throw new InvalidOperationException($"Select the {kind} SharePoint Document Library before browsing folders.");
        if (!browseDocumentLibraries && requestedLibraryGuid.Length == 0)
            throw new InvalidOperationException($"Select the {kind} SharePoint Document Library using Browse so its library ID is known before browsing folders.");

        // Native SharePoint browse preserves an empty dirname. Unlike file-system/cloud profiles,
        // forcing an empty SharePoint Step path to "/" changes the request semantics and can return
        // an empty result set, so keep blank blank and normalize only non-empty paths.
        var browseDirectory = string.IsNullOrWhiteSpace(path) ? string.Empty : NormalizeEditorPath(path);
        var request = HostSharePointBrowseContract.Build(hostId, effective, browseDirectory,
            documentLibrary: browseDocumentLibraries ? string.Empty : requestedLibrary,
            documentLibraryGuid: requestedLibraryGuid, taskId: taskId);

        var response = await _restClient.BrowseSharePointHostV0Async(request, cancellationToken);

        if (browseDocumentLibraries)
        {
            var libraryBrowsePath = string.IsNullOrWhiteSpace(browseDirectory) ? string.Empty : NormalizeEditorPath(browseDirectory);
            return new TraditionalTaskEditorFolderBrowseResult(
                hostId,
                hostName,
                capability.Type,
                libraryBrowsePath,
                string.Empty,
                ParseSharePointDocumentLibraries(response));
        }

        var responseDirectory = FirstNonBlank(Read(response, "CurrentDir"), browseDirectory);
        var currentPath = string.IsNullOrWhiteSpace(responseDirectory) ? string.Empty : NormalizeEditorPath(responseDirectory);
        var items = ParseSharePointBrowseItems(response, currentPath);
        var parentPath = string.IsNullOrWhiteSpace(currentPath) ? string.Empty : ParentEditorPath(currentPath);
        if (parentPath == "/") parentPath = string.Empty; // SharePoint native root is represented by blank dirname.
        return new TraditionalTaskEditorFolderBrowseResult(
            hostId,
            hostName,
            capability.Type,
            currentPath,
            parentPath,
            items);
    }

    private static IReadOnlyList<TraditionalTaskEditorFolderBrowseItem> ParseS3FolderItems(JsonObject response, string currentPath)
        => ParseCloudFilesWithDirectoryFlag(response, currentPath, includeGenericDirectoryCollections: true);

    private static IReadOnlyList<TraditionalTaskEditorFolderBrowseItem> ParseAzureBlobFolderItems(JsonObject response, string currentPath)
        => ParseCloudFilesWithDirectoryFlag(response, currentPath, includeGenericDirectoryCollections: true);

    private static IReadOnlyList<TraditionalTaskEditorFolderBrowseItem> ParseGoogleStorageFolderItems(JsonObject response, string currentPath)
        => ParseCloudFilesWithDirectoryFlag(response, currentPath, includeGenericDirectoryCollections: true);

    private static IReadOnlyList<TraditionalTaskEditorFolderBrowseItem> ParseCloudFilesWithDirectoryFlag(
        JsonObject response,
        string currentPath,
        bool includeGenericDirectoryCollections)
    {
        // Captured SharePoint cloud browse responses place directories under Files.File and identify
        // them with IsDir=1. The user confirmed that Amazon S3/Azure may use the same native v0 shape.
        // S3 and Azure call this helper only through their own profile parsers; it does not change the
        // UNC/FTP/SFTP/MOVEit Transfer parsing contract.
        var result = new List<TraditionalTaskEditorFolderBrowseItem>();
        foreach (var node in CollectionItems(response, "Files", "File"))
        {
            var item = node as JsonObject;
            if (item is null) continue;
            var name = FirstNonBlank(Read(item, "Name"), Read(item, "FileName"), LastPathSegment(Read(item, "Path")));
            if (name.Length == 0 || name is "." or "..") continue;
            var isDirectory = ReadNullableInt(item, "IsDir") == 1 || ReadNullableFlag(item, "IsDir") == true;
            var modified = FirstNonBlank(Read(item, "LastModified"), Read(item, "Modified"), Read(item, "Date"));
            var size = isDirectory ? string.Empty : FirstNonBlank(Read(item, "Size"), Read(item, "Length"));
            result.Add(new TraditionalTaskEditorFolderBrowseItem(
                name,
                isDirectory ? CombineEditorPath(currentPath, name) : string.Empty,
                isDirectory,
                size,
                modified));
        }

        if (includeGenericDirectoryCollections)
        {
            foreach (var node in CollectionItems(response, "Dirs", "Dir").Concat(CollectionItems(response, "Folders", "Folder")))
            {
                var item = node as JsonObject;
                var scalar = ScalarText(node);
                var name = item is not null
                    ? FirstNonBlank(Read(item, "Name"), Read(item, "FolderName"), LastPathSegment(Read(item, "DirName")), LastPathSegment(Read(item, "Path")))
                    : LastPathSegment(scalar);
                if (name.Length == 0 || name is "." or "..") continue;
                result.Add(new TraditionalTaskEditorFolderBrowseItem(name, CombineEditorPath(currentPath, name), true, string.Empty,
                    item is not null ? FirstNonBlank(Read(item, "LastModified"), Read(item, "Modified"), Read(item, "Date")) : string.Empty));
            }
        }

        return result
            .GroupBy(item => $"{(item.IsFolder ? "D" : "F")}:{item.Name}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(item => item.IsFolder)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<TraditionalTaskEditorFolderBrowseItem> ParseSharePointBrowseItems(JsonObject response, string currentPath)
    {
        // The captured SharePoint v0 response returns both files and folders under Files.File and
        // distinguishes folders with IsDir=1. Keep this parser protocol-specific rather than changing
        // the generic browse parser used by UNC/FTP/SFTP/cloud profiles.
        var result = new List<TraditionalTaskEditorFolderBrowseItem>();
        foreach (var node in CollectionItems(response, "Files", "File"))
        {
            var item = node as JsonObject;
            if (item is null) continue;
            var name = FirstNonBlank(Read(item, "Name"), Read(item, "FileName"), LastPathSegment(Read(item, "Path")));
            if (name.Length == 0 || name is "." or "..") continue;
            var isDirectory = ReadNullableInt(item, "IsDir") == 1 || ReadNullableFlag(item, "IsDir") == true;
            var modified = FirstNonBlank(Read(item, "LastModified"), Read(item, "Modified"), Read(item, "Date"));
            var size = isDirectory ? string.Empty : FirstNonBlank(Read(item, "Size"), Read(item, "Length"));
            result.Add(new TraditionalTaskEditorFolderBrowseItem(
                name,
                isDirectory ? CombineEditorPath(currentPath, name) : string.Empty,
                isDirectory,
                size,
                modified));
        }

        // Accept Dirs/Dir and Folders/Folder as conservative response variants without allowing this
        // SharePoint contract to alter other host-profile parsers.
        foreach (var node in CollectionItems(response, "Dirs", "Dir").Concat(CollectionItems(response, "Folders", "Folder")))
        {
            var item = node as JsonObject;
            var scalar = ScalarText(node);
            var name = item is not null
                ? FirstNonBlank(Read(item, "Name"), Read(item, "FolderName"), LastPathSegment(Read(item, "DirName")), LastPathSegment(Read(item, "Path")))
                : LastPathSegment(scalar);
            if (name.Length == 0 || name is "." or "..") continue;
            result.Add(new TraditionalTaskEditorFolderBrowseItem(name, CombineEditorPath(currentPath, name), true, string.Empty,
                item is not null ? FirstNonBlank(Read(item, "LastModified"), Read(item, "Modified"), Read(item, "Date")) : string.Empty));
        }

        return result
            .GroupBy(item => $"{(item.IsFolder ? "D" : "F")}:{item.Name}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(item => item.IsFolder)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<TraditionalTaskEditorFolderBrowseItem> ParseSharePointDocumentLibraries(JsonObject response)
    {
        var result = new List<TraditionalTaskEditorFolderBrowseItem>();
        foreach (var node in CollectionItems(response, "Buckets", "Bucket"))
        {
            var obj = node as JsonObject;
            var name = obj is not null ? Read(obj, "Name") : ScalarText(node);
            var guid = obj is not null ? FirstNonBlank(Read(obj, "GUID"), Read(obj, "Guid"), Read(obj, "ID")) : string.Empty;
            if (string.IsNullOrWhiteSpace(name)) continue;
            result.Add(new TraditionalTaskEditorFolderBrowseItem(
                name.Trim(),
                "/" + name.Trim().Trim('/'),
                true,
                string.Empty,
                string.Empty,
                guid.Trim()));
        }
        return result
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Keep S3 response parsing unchanged from the proven s3-v1 profile. Azure has its own
    // parser even when MOVEit happens to return the same Buckets/Bucket collection shape.
    private static IReadOnlyList<TraditionalTaskEditorFolderBrowseItem> ParseS3BucketItems(JsonObject response)
    {
        var nodes = CollectionItems(response, "Buckets", "Bucket")
            .Concat(CollectionItems(response, "Dirs", "Dir"))
            .Concat(CollectionItems(response, "Folders", "Folder"));

        var result = new List<TraditionalTaskEditorFolderBrowseItem>();
        foreach (var node in nodes)
        {
            var obj = node as JsonObject;
            var scalar = ScalarText(node);
            var name = obj is not null
                ? FirstNonBlank(
                    Read(obj, "Name"),
                    Read(obj, "Bucket"),
                    Read(obj, "BucketName"),
                    LastPathSegment(Read(obj, "Path")),
                    LastPathSegment(Read(obj, "DirName")))
                : LastPathSegment(scalar);
            if (name.Length == 0 || name is "." or "..") continue;
            result.Add(new TraditionalTaskEditorFolderBrowseItem(name, "/" + name.Trim('/'), true, string.Empty, string.Empty));
        }

        return result
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<TraditionalTaskEditorFolderBrowseItem> ParseGoogleStorageBucketItems(JsonObject response)
    {
        // The supplied Google Storage response uses Buckets/Bucket with Name and TimeCreated. Keep
        // this parser isolated from S3 so later Google-specific response fields cannot alter s3-v1.
        var result = new List<TraditionalTaskEditorFolderBrowseItem>();
        foreach (var node in CollectionItems(response, "Buckets", "Bucket"))
        {
            var obj = node as JsonObject;
            var name = obj is not null ? Read(obj, "Name") : ScalarText(node);
            if (string.IsNullOrWhiteSpace(name)) continue;
            var normalized = name.Trim();
            result.Add(new TraditionalTaskEditorFolderBrowseItem(normalized, "/" + normalized.Trim('/'), true, string.Empty,
                obj is not null ? Read(obj, "TimeCreated") : string.Empty));
        }
        return result
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<TraditionalTaskEditorFolderBrowseItem> ParseAzureBlobNamedItems(JsonObject response, string currentPath)
    {
        var nodes = CollectionItems(response, "Buckets", "Bucket")
            .Concat(CollectionItems(response, "Dirs", "Dir"))
            .Concat(CollectionItems(response, "Folders", "Folder"));

        var result = new List<TraditionalTaskEditorFolderBrowseItem>();
        foreach (var node in nodes)
        {
            var obj = node as JsonObject;
            var scalar = ScalarText(node);
            var name = obj is not null
                ? FirstNonBlank(
                    Read(obj, "Name"),
                    Read(obj, "Bucket"),
                    Read(obj, "BucketName"),
                    Read(obj, "Container"),
                    Read(obj, "ContainerName"),
                    LastPathSegment(Read(obj, "Path")),
                    LastPathSegment(Read(obj, "DirName")))
                : LastPathSegment(scalar);
            if (name.Length == 0 || name is "." or "..") continue;
            result.Add(new TraditionalTaskEditorFolderBrowseItem(name, CombineEditorPath(currentPath, name), true, string.Empty, string.Empty));
        }

        return result
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<TraditionalTaskEditorFolderBrowseResult> BrowseFtpAsync(
        string taskId,
        string hostId,
        TraditionalTaskEditorHostCapability capability,
        JsonObject checkoutDefinition,
        string path,
        CancellationToken cancellationToken)
    {
        // Reuse the verified native FTP/FTPS browse contract already exercised by Host Health and
        // Task Preflight. The editor changes only how the returned directory is presented.
        var effective = await EnrichDefinitionAsync(
            hostId,
            "FTP",
            checkoutDefinition,
            cancellationToken,
            "Name", "Host", "DefUsername", "Port", "Secure", "IgnoreCertProbs",
            "DefPassive", "DefAccount", "DefClientCertStore", "DefClientCertSubject",
            "DefClientCertSHA1", "DefConnTimeoutSecs", "DefDataTimeoutSecs", "DefReuseSSL",
            "NATUseServerIPForData", "NATClientIP", "AddCom", "ProxyType", "DefBlindDL");

        var hostName = Read(effective, "Name");
        var server = Read(effective, "Host");
        var username = Read(effective, "DefUsername");
        var port = ReadNullableInt(effective, "Port") ?? 0;
        var proxyType = Read(effective, "ProxyType").Trim();

        var missing = new List<string>();
        if (server.Length == 0) missing.Add("Host");
        if (port <= 0) missing.Add("Port");
        if (username.Length == 0) missing.Add("Username");
        if (missing.Count > 0)
            throw new InvalidOperationException($"The selected FTP/FTPS Host is incomplete. Missing: {string.Join(", ", missing)}.");

        if (ReadNullableFlag(effective, "DefBlindDL") == true)
            throw new InvalidOperationException("The selected FTP/FTPS Host uses Blind Downloads, so MOVEit does not provide a directory listing for Browse.");


        var loginInfo = new JsonObject
        {
            ["serverip"] = server,
            ["serverport"] = port,
            ["username"] = username,
            ["password"] = null,
            ["customdir"] = 0,
            ["customdircoldate"] = 0,
            ["customdircolfilename"] = 0,
            ["customdirskipbottom"] = 0,
            ["customdirskiptop"] = 0,
            ["dirscriptid"] = string.Empty
        };

        AddStringIfPresent(loginInfo, "secure", effective, "Secure");
        AddFlagIfPresent(loginInfo, "ignorecertprobs", effective, "IgnoreCertProbs");
        AddFlagIfPresent(loginInfo, "passive", effective, "DefPassive");
        AddStringIfPresent(loginInfo, "account", effective, "DefAccount");
        AddStringIfPresent(loginInfo, "clientcertstore", effective, "DefClientCertStore");
        AddStringIfPresent(loginInfo, "clientcertsubject", effective, "DefClientCertSubject");
        AddStringIfPresent(loginInfo, "clientcertsha1", effective, "DefClientCertSHA1");
        AddIntIfPresent(loginInfo, "conntimeoutsecs", effective, "DefConnTimeoutSecs");
        AddIntIfPresent(loginInfo, "datatimeoutsecs", effective, "DefDataTimeoutSecs");
        AddFlagIfPresent(loginInfo, "reuseSSLsession", effective, "DefReuseSSL");
        AddIntIfPresent(loginInfo, "natuseserveripfordata", effective, "NATUseServerIPForData");
        AddStringIfPresent(loginInfo, "natclientip", effective, "NATClientIP");
        AddStringIfPresent(loginInfo, "addcom", effective, "AddCom");
        if (proxyType.Length > 0) loginInfo["proxytype"] = proxyType;
        AddFlagIfPresent(loginInfo, "blinddownloads", effective, "DefBlindDL");

        var browseDirectory = string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim();
        HostBrowseSettings.ApplyProxy(loginInfo, effective, "FTP");

        var request = new JsonObject
        {
            ["showfiles"] = true,
            ["mask"] = string.Empty,
            ["hostid"] = hostId,
            ["logininfo"] = loginInfo,
            ["dirname"] = browseDirectory,
            ["taskid"] = null,
            ["stepid"] = null
        };

        var response = await _restClient.BrowseFtpHostV0Async(request, cancellationToken);
        var currentPath = NormalizeEditorPath(FirstNonBlank(Read(response, "CurrentDir"), browseDirectory, "/"));
        var parentPath = ParentEditorPath(currentPath);
        var items = ParseBrowseItems(response, currentPath, NormalizeEditorPath);
        return new TraditionalTaskEditorFolderBrowseResult(hostId, hostName, capability.Type, currentPath, parentPath, items);
    }

    private static IReadOnlyList<TraditionalTaskEditorFolderBrowseItem> ParseBrowseItems(
        JsonObject response,
        string currentPath,
        Func<string, string> normalizeReturnedPath)
    {
        var result = new List<TraditionalTaskEditorFolderBrowseItem>();

        // The native v0 /browse endpoint serializes Dirs.Dir and Files.File differently depending on
        // MOVEit build/runtime data. In particular, directory entries can be scalar strings rather than
        // objects. Do not use OfType<JsonObject>() here: doing so silently discarded legitimate folders
        // while file objects continued to render in the editor.
        var directoryNodes = CollectionItems(response, "Dirs", "Dir")
            .Concat(CollectionItems(response, "Folders", "Folder"));
        foreach (var folderNode in directoryNodes)
        {
            var folder = folderNode as JsonObject;
            var scalar = ScalarText(folderNode);
            // Native UNC browse returns directory objects as { DirType, DirName, DirComment }.
            // DirName is a full path, unlike Files.File which uses Name. Keep the older aliases as
            // compatibility for other MOVEit response variants, but prefer the captured native shape.
            var name = folder is not null
                ? FirstNonBlank(
                    LastPathSegment(Read(folder, "DirName")),
                    Read(folder, "Name"),
                    Read(folder, "FolderName"),
                    LastPathSegment(Read(folder, "Path")),
                    LastPathSegment(Read(folder, "FullPath")))
                : LastPathSegment(scalar);
            if (name.Length == 0 || name is "." or "..") continue;

            var returnedPath = folder is not null
                ? FirstNonBlank(Read(folder, "DirName"), Read(folder, "Path"), Read(folder, "FullPath"), Read(folder, "FolderName"))
                : scalar;
            var editorPath = returnedPath.Length > 0 && LooksLikePath(returnedPath)
                ? normalizeReturnedPath(returnedPath)
                : CombineEditorPath(currentPath, name);
            var modified = folder is not null
                ? FirstNonBlank(Read(folder, "LastModified"), Read(folder, "Modified"), Read(folder, "Date"))
                : string.Empty;

            result.Add(new TraditionalTaskEditorFolderBrowseItem(name, editorPath, true, string.Empty, modified));
        }

        foreach (var fileNode in CollectionItems(response, "Files", "File"))
        {
            var file = fileNode as JsonObject;
            var scalar = ScalarText(fileNode);
            var name = file is not null
                ? FirstNonBlank(Read(file, "Name"), Read(file, "FileName"), LastPathSegment(Read(file, "Path")), LastPathSegment(Read(file, "FullPath")))
                : LastPathSegment(scalar);
            if (name.Length == 0) continue;

            var size = file is not null ? FirstNonBlank(Read(file, "Size"), Read(file, "Length")) : string.Empty;
            var modified = file is not null
                ? FirstNonBlank(Read(file, "LastModified"), Read(file, "Modified"), Read(file, "Date"))
                : string.Empty;
            result.Add(new TraditionalTaskEditorFolderBrowseItem(name, string.Empty, false, size, modified));
        }

        return result
            .GroupBy(item => $"{(item.IsFolder ? "D" : "F")}:{item.Name}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(item => item.IsFolder)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<JsonNode> CollectionItems(JsonObject response, string containerName, string itemName)
    {
        var container = GetNode(response, containerName);
        if (container is JsonObject containerObject)
        {
            var nested = GetNode(containerObject, itemName);
            if (nested is not null) container = nested;
        }

        return container switch
        {
            JsonArray array => array.Where(node => node is not null).Select(node => node!).ToList(),
            JsonObject singleObject => new JsonNode[] { singleObject },
            JsonValue singleValue => new JsonNode[] { singleValue },
            _ => Array.Empty<JsonNode>()
        };
    }

    private static string ScalarText(JsonNode? node)
    {
        if (node is not JsonValue value) return string.Empty;
        if (value.TryGetValue<string>(out var text)) return text?.Trim() ?? string.Empty;
        return string.Empty;
    }

    private static bool LooksLikePath(string value)
        => value.Contains('\\') || value.Contains('/') || value.StartsWith(@"\\", StringComparison.Ordinal);

    private static string ResolveBrowseDirectory(string shareName, string editorPath)
    {
        if (editorPath == "/") return shareName.TrimEnd('\\', '/');
        return shareName.TrimEnd('\\', '/') + "\\" + editorPath.TrimStart('/').Replace('/', '\\');
    }

    private static string ToShareEditorPath(string shareName, string path)
    {
        var value = (path ?? string.Empty).Trim();
        if (value.Length == 0) return "/";

        var normalizedShare = shareName.TrimEnd('\\', '/');
        if (value.StartsWith(normalizedShare, StringComparison.OrdinalIgnoreCase))
            value = value[normalizedShare.Length..];

        value = value.Replace('\\', '/').Trim();
        while (value.Contains("//", StringComparison.Ordinal)) value = value.Replace("//", "/", StringComparison.Ordinal);
        if (value.Length == 0 || value == "/") return "/";
        return "/" + value.Trim('/');
    }

    private static string NormalizeGoogleStorageRequestPath(string? path)
    {
        var raw = (path ?? string.Empty).Trim().Replace('\\', '/');
        if (raw.Length == 0) return "/";
        var hadTrailingSlash = raw.Length > 1 && raw.EndsWith("/", StringComparison.Ordinal);
        while (raw.Contains("//", StringComparison.Ordinal)) raw = raw.Replace("//", "/", StringComparison.Ordinal);
        if (!raw.StartsWith("/", StringComparison.Ordinal)) raw = "/" + raw;
        var normalized = raw == "/" ? "/" : "/" + raw.Trim('/');
        return hadTrailingSlash && normalized != "/" ? normalized + "/" : normalized;
    }

    private static string NormalizeEditorPath(string path)
    {
        var value = (path ?? string.Empty).Trim().Replace('\\', '/');
        while (value.Contains("//", StringComparison.Ordinal)) value = value.Replace("//", "/", StringComparison.Ordinal);
        if (value.Length == 0 || value == "/") return "/";
        return "/" + value.Trim('/');
    }

    private static string ParentEditorPath(string path)
    {
        var normalized = NormalizeEditorPath(path);
        if (normalized == "/") return string.Empty;
        var index = normalized.LastIndexOf('/');
        return index <= 0 ? "/" : normalized[..index];
    }

    private static string CombineEditorPath(string currentPath, string name)
    {
        if (currentPath.StartsWith(@"\\", StringComparison.Ordinal) || currentPath.Length >= 2 && currentPath[1] == ':')
            return NormalizeLocalPath(currentPath.TrimEnd('\\') + "\\" + (name ?? "").Trim('\\', '/'));
        var current = NormalizeEditorPath(currentPath);
        var child = (name ?? string.Empty).Trim().Trim('/', '\\');
        if (child.Length == 0) return current;
        return current == "/" ? $"/{child}" : $"{current}/{child}";
    }

    private static string LastPathSegment(string value)
    {
        var normalized = (value ?? string.Empty).Trim().TrimEnd('/', '\\');
        if (normalized.Length == 0) return string.Empty;
        var slash = Math.Max(normalized.LastIndexOf('/'), normalized.LastIndexOf('\\'));
        return slash >= 0 ? normalized[(slash + 1)..] : normalized;
    }

    private static JsonObject MergeMissing(JsonObject primary, JsonObject secondary, params string[] names)
    {
        var result = (JsonObject)primary.DeepClone();
        foreach (var name in names)
        {
            if (Read(result, name).Length > 0) continue;
            var value = GetNode(secondary, name);
            if (value is not null) result[name] = value.DeepClone();
        }
        return result;
    }

    private static string NormalizeStepKind(string? kind)
    {
        var value = (kind ?? string.Empty).Trim();
        if (value.Equals("Source", StringComparison.OrdinalIgnoreCase)) return "Source";
        if (value.Equals("Destination", StringComparison.OrdinalIgnoreCase)) return "Destination";
        return value;
    }

    private static (string TypeCode, JsonObject? Definition) GetDefinition(JsonObject? wrapper)
    {
        if (wrapper is null) return (string.Empty, null);

        foreach (var key in TraditionalTaskEditorHostCapabilities.WrapperTypeCodes)
            if (GetNode(wrapper, key) is JsonObject typed)
                return (TraditionalTaskEditorHostCapabilities.NormalizeTypeCode(key), typed);

        if (Read(wrapper, "ID").Length > 0)
            return (TraditionalTaskEditorHostCapabilities.NormalizeTypeCode(Read(wrapper, "Type")), wrapper);

        // Match HostConnectionTestService's defensive polymorphic-wrapper fallback.
        var firstObject = wrapper.FirstOrDefault(pair => pair.Value is JsonObject);
        return firstObject.Value is JsonObject fallback
            ? (TraditionalTaskEditorHostCapabilities.NormalizeTypeCode(firstObject.Key), fallback)
            : (string.Empty, null);
    }

    private static JsonNode? GetNode(JsonObject? obj, string name)
    {
        if (obj is null) return null;
        if (obj.TryGetPropertyValue(name, out var direct)) return direct;
        foreach (var pair in obj)
            if (pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)) return pair.Value;
        return null;
    }

    private static string Read(JsonObject? obj, string name)
    {
        var node = GetNode(obj, name);
        if (node is not JsonValue value) return string.Empty;
        if (value.TryGetValue<string>(out var text)) return text?.Trim() ?? string.Empty;
        if (value.TryGetValue<int>(out var intValue)) return intValue.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue<long>(out var longValue)) return longValue.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue<bool>(out var boolValue)) return boolValue ? "true" : "false";
        return string.Empty;
    }

    private static void AddStringIfPresent(JsonObject target, string targetName, JsonObject source, string sourceName)
    {
        var value = Read(source, sourceName);
        if (value.Length > 0) target[targetName] = value;
    }

    private static void AddIntIfPresent(JsonObject target, string targetName, JsonObject source, string sourceName)
    {
        var value = ReadNullableInt(source, sourceName);
        if (value.HasValue) target[targetName] = value.Value;
    }

    private static void AddFlagIfPresent(JsonObject target, string targetName, JsonObject source, string sourceName)
    {
        var value = ReadNullableFlag(source, sourceName);
        if (value.HasValue) target[targetName] = value.Value;
    }

    private static int? ReadNullableInt(JsonObject obj, string name)
        => int.TryParse(Read(obj, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private async Task<JsonObject> EnrichDefinitionAsync(
        string hostId,
        string expectedTypeCode,
        JsonObject definition,
        CancellationToken cancellationToken,
        params string[] fieldNames)
    {
        // This mirrors HostConnectionTestService: only fields absent from the checkout payload are
        // enriched from v1. Empty-but-present native values are intentional defaults and are not replaced.
        var missingFields = fieldNames.Concat(HostBrowseSettings.Fields(expectedTypeCode))
            .Where(name => !HasValue(definition, name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (missingFields.Length == 0)
            return definition;

        try
        {
            var liveHost = await _restClient.GetHostAsync(hostId, cancellationToken);
            var (liveTypeCode, liveDefinition) = GetDefinition(liveHost);
            if (liveDefinition is null || !liveTypeCode.Equals(expectedTypeCode, StringComparison.OrdinalIgnoreCase))
                return definition;

            var effective = (JsonObject)definition.DeepClone();
            foreach (var fieldName in missingFields)
            {
                var liveValue = GetNode(liveDefinition, fieldName);
                if (liveValue is not null)
                    effective[fieldName] = liveValue.DeepClone();
            }
            return effective;
        }
        catch (MoveItAuthenticationException)
        {
            throw;
        }
        catch (MoveItApiException ex)
        {
            _logger.LogDebug(
                ex,
                "MAUI could not enrich optional {HostType} Host browse settings for Host {HostId} from the v1 Host definition.",
                expectedTypeCode,
                hostId);
            return definition;
        }
    }

    private static bool HasValue(JsonObject? source, string name)
        => source is not null && GetNode(source, name) is not null;

    private static bool? ReadNullableFlag(JsonObject obj, string name)
    {
        var value = Read(obj, name);
        if (value.Equals("1", StringComparison.OrdinalIgnoreCase) || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Equals("0", StringComparison.OrdinalIgnoreCase) || value.Equals("false", StringComparison.OrdinalIgnoreCase) || value.Equals("no", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }

    private static string FirstNonBlank(params string[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}

