// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Text.Json.Nodes;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Services.MoveIt;

public interface IAutomationHostBrowseService
{
    bool SupportsHostType(string? typeCode);
    Task<AutomationHostBrowseResult> BrowseAsync(
        string hostId,
        string directory,
        string fileMask = "",
        string rootName = "",
        string rootId = "",
        string taskId = "",
        string stepId = "",
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Reusable browser over native MOVEit Host browse contracts. Host-type capability comes from
/// ITraditionalTaskHostProfileCatalog. Authentication/proxy composition is shared with Task editors
/// and Host resource pickers; this operational workflow continues to exclude local Host 0.
/// </summary>
public sealed class AutomationHostBrowseService : IAutomationHostBrowseService
{
    private readonly IMoveItRestClient _rest;
    private readonly IMoveItSessionService? _sessions;
    private readonly ITraditionalTaskHostProfileCatalog _profiles;
    private readonly ILogger<AutomationHostBrowseService> _logger;

    public AutomationHostBrowseService(
        IMoveItRestClient rest,
        ITraditionalTaskHostProfileCatalog profiles,
        ILogger<AutomationHostBrowseService> logger, IMoveItSessionService? sessions = null)
    {
        _rest = rest;
        _profiles = profiles;
        _logger = logger;
        _sessions = sessions;
    }

    public bool SupportsHostType(string? typeCode)
        => string.Equals(typeCode, "OneDrive", StringComparison.OrdinalIgnoreCase) ? MoveItFeatureSupport.Supports(_sessions?.Get()?.Info, MoveItFeature.OneDriveHosts)
            : !string.Equals(_profiles.NormalizeType(typeCode), "FileSystem", StringComparison.OrdinalIgnoreCase)
            && _profiles.Find(typeCode)?.BrowseSupported == true;

    public async Task<AutomationHostBrowseResult> BrowseAsync(
        string hostId,
        string directory,
        string fileMask = "",
        string rootName = "",
        string rootId = "",
        string taskId = "",
        string stepId = "",
        CancellationToken cancellationToken = default)
    {
        var normalizedHostId = (hostId ?? string.Empty).Trim();
        if (normalizedHostId.Length == 0) throw new ArgumentException("Host ID is required.", nameof(hostId));
        if (normalizedHostId == "0")
            throw new InvalidOperationException("Local File System browsing is not exposed through the verified MOVEit Host browse contract. Enter the server-local folder manually.");

        var requestedDirectory = (directory ?? string.Empty).Trim();
        var mask = (fileMask ?? string.Empty).Trim();
        var requestedRoot = (rootName ?? string.Empty).Trim();
        var requestedRootId = (rootId ?? string.Empty).Trim();
        var activeTaskId = (taskId ?? string.Empty).Trim();
        var activeStepId = (stepId ?? string.Empty).Trim();

        return await HostBrowseCheckout.ReadAsync(_rest, normalizedHostId, async (checkout, browseToken) =>
        {
            var (typeCode, definition) = GetDefinition(checkout);
            var profile = typeCode == "OneDrive" && SupportsHostType(typeCode)
                ? new TraditionalTaskHostProfile("OneDrive", "Microsoft OneDrive", "", false, false, true)
                : _profiles.Find(typeCode);
            if (definition is null || profile?.BrowseSupported != true)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(typeCode)
                    ? "MOVEit did not return a browsable Host definition."
                    : $"Folder browsing is not available for Host type '{typeCode}'.");

            var effective = await EnrichAsync(normalizedHostId, profile.TypeCode, definition, browseToken);
            var hostName = Read(effective, "Name");

            JsonObject response = profile.TypeCode switch
            {
                "Share" => await BrowseShareAsync(normalizedHostId, effective, requestedDirectory, mask, browseToken),
                "siLock" => await BrowseMoveItTransferAsync(normalizedHostId, effective, requestedDirectory, mask, activeTaskId, activeStepId, browseToken),
                "SSHFTP" => await BrowseSshAsync(normalizedHostId, effective, requestedDirectory, mask, activeTaskId, activeStepId, browseToken),
                "FTP" => await BrowseFtpAsync(normalizedHostId, effective, requestedDirectory, mask, activeTaskId, activeStepId, browseToken),
                "OneDrive" => await _rest.BrowseOneDriveHostV0Async(HostCloudAuthenticationSettings.OneDriveBrowse(normalizedHostId, effective, requestedDirectory, mask, true, activeTaskId, activeStepId), browseToken),
                "S3" => await BrowseS3Async(normalizedHostId, effective, requestedDirectory, mask, requestedRoot, activeTaskId, activeStepId, browseToken),
                "AzureBlob" => await BrowseAzureBlobAsync(normalizedHostId, effective, requestedDirectory, mask, requestedRoot, activeTaskId, activeStepId, browseToken),
                "SharePoint" => await BrowseSharePointAsync(normalizedHostId, effective, requestedDirectory, mask, requestedRoot, requestedRootId, activeTaskId, activeStepId, browseToken),
                "GoogleStorage" => await BrowseGoogleStorageAsync(normalizedHostId, effective, requestedDirectory, mask, requestedRoot, activeTaskId, activeStepId, browseToken),
                _ => throw new InvalidOperationException($"Folder browsing is not available for Host type '{profile.TypeCode}'.")
            };

            if (profile.RootKind != TraditionalTaskLocationRootKind.None && requestedRoot.Length == 0)
            {
                var rootItems = ParseRootItems(response, profile.RootKind);
                return new AutomationHostBrowseResult(
                    normalizedHostId,
                    hostName,
                    profile.TypeCode,
                    string.Empty,
                    string.Empty,
                    rootItems,
                    string.Empty,
                    string.Empty,
                    profile.RootKind.ToString());
            }

            var rawCurrent = First(Read(response, "CurrentDir"), Read(response, "CurrentDirectory"), requestedDirectory);
            var rawItems = ParseItems(response, rawCurrent, profile.TypeCode);
            var shareRoot = profile.TypeCode.Equals("Share", StringComparison.OrdinalIgnoreCase) ? Read(effective, "UNC") : string.Empty;
            var current = profile.TypeCode.Equals("Share", StringComparison.OrdinalIgnoreCase)
                ? ToShareTaskPath(rawCurrent, shareRoot)
                : rawCurrent;
            var items = profile.TypeCode.Equals("Share", StringComparison.OrdinalIgnoreCase)
                ? rawItems.Select(item => item with { Path = ToShareTaskPath(item.Path, shareRoot) }).ToArray()
                : rawItems;

            return new AutomationHostBrowseResult(
                normalizedHostId,
                hostName,
                profile.TypeCode,
                current,
                GetParentDirectory(current, profile.TypeCode),
                items,
                requestedRoot,
                requestedRootId,
                profile.RootKind == TraditionalTaskLocationRootKind.None ? string.Empty : profile.RootKind.ToString());
        }, cancellationToken);
    }

    private async Task<JsonObject> BrowseShareAsync(string hostId, JsonObject host, string directory, string mask, CancellationToken cancellationToken)
    {
        var share = Read(host, "UNC");
        if (share.Length == 0) throw new InvalidOperationException("The selected UNC Share Host does not have a configured UNC path.");
        var browseDirectory = ResolveShareDirectory(share, directory);
        var request = new JsonObject
        {
            ["showfiles"] = true,
            ["mask"] = mask,
            ["hostid"] = hostId,
            ["username"] = string.Empty,
            ["password"] = string.Empty,
            ["dirname"] = browseDirectory,
            ["sharename"] = share,
            ["firstbrowse"] = true
        };
        CopyBool(request, "preservefiletimestamp", host, "DefPreserveFileTimestamp");
        return await _rest.BrowseFileSystemHostV0Async(request, cancellationToken);
    }

    private async Task<JsonObject> BrowseMoveItTransferAsync(string hostId, JsonObject host, string directory, string mask, string taskId, string stepId, CancellationToken cancellationToken)
    {
        var server = Read(host, "Host");
        var username = Read(host, "DefUsername");
        var port = ReadInt(host, "Port");
        if (server.Length == 0 || username.Length == 0 || port <= 0)
            throw new InvalidOperationException("The selected MOVEit Transfer Host is incomplete. Host, Port, and Username are required.");

        var login = new JsonObject
        {
            ["serverip"] = server,
            ["serverport"] = port,
            ["username"] = username,
            ["password"] = null
        };
        CopyString(login, "scriptpath", host, "ScriptPath", includeEmpty: true);
        CopyBool(login, "secure", host, "Secure");
        CopyBool(login, "ignorecertprobs", host, "IgnoreCertProbs");
        CopyString(login, "clientcertstore", host, "DefClientCertStore", includeEmpty: true);
        CopyString(login, "clientcertsubject", host, "DefClientCertSubject", includeEmpty: true);
        CopyString(login, "clientcertsha1", host, "DefClientCertSHA1", includeEmpty: true);
        CopyInt(login, "conntimeoutsecs", host, "DefConnTimeoutSecs");
        CopyInt(login, "datatimeoutsecs", host, "DefDataTimeoutSecs");

        var folder = directory.Length == 0 ? "/" : directory;
        var request = new JsonObject
        {
            ["showfiles"] = true,
            ["mask"] = mask,
            ["hostid"] = hostId,
            ["logininfo"] = login,
            ["folderid"] = -1,
            ["folderpath"] = folder,
            ["taskid"] = taskId.Length == 0 ? null : taskId,
            ["stepid"] = stepId.Length == 0 ? null : stepId
        };
        return await _rest.BrowseDmzHostV0Async(request, cancellationToken);
    }

    private async Task<JsonObject> BrowseSshAsync(string hostId, JsonObject host, string directory, string mask, string taskId, string stepId, CancellationToken cancellationToken)
    {
        EnsureListingHost(host, "SSH/SFTP");
        var login = BuildCommonLogin(host);
        CopyBool(login, "ignorecertprobs", host, "IgnoreCertProbs");
        CopyString(login, "clientkeyid", host, "DefClientKeyID", includeEmpty: true);
        CopyString(login, "hostfingerprint", host, "SSHFingerprint", includeEmpty: true);
        CopyInt(login, "conntimeoutsecs", host, "DefConnTimeoutSecs");
        CopyBool(login, "optimizesshbuf", host, "OptimizeSSHBuf");
        CopyString(login, "encryptionlist", host, "EncList", includeEmpty: true);
        CopyBool(login, "blinddownloads", host, "DefBlindDL");
        CopyBool(login, "preservefiletimestamp", host, "DefPreserveFileTimestamp");
        HostBrowseSettings.ApplyProxy(login, host, "SSHFTP");
        return await _rest.BrowseSshHostV0Async(BuildBrowseRequest(hostId, login, directory, mask, taskId, stepId), cancellationToken);
    }

    private async Task<JsonObject> BrowseFtpAsync(string hostId, JsonObject host, string directory, string mask, string taskId, string stepId, CancellationToken cancellationToken)
    {
        EnsureListingHost(host, "FTP/FTPS");
        var login = BuildCommonLogin(host);
        CopyString(login, "secure", host, "Secure");
        CopyBool(login, "ignorecertprobs", host, "IgnoreCertProbs");
        CopyBool(login, "passive", host, "DefPassive");
        CopyString(login, "account", host, "DefAccount");
        CopyString(login, "clientcertstore", host, "DefClientCertStore", includeEmpty: true);
        CopyString(login, "clientcertsubject", host, "DefClientCertSubject", includeEmpty: true);
        CopyString(login, "clientcertsha1", host, "DefClientCertSHA1", includeEmpty: true);
        CopyInt(login, "conntimeoutsecs", host, "DefConnTimeoutSecs");
        CopyInt(login, "datatimeoutsecs", host, "DefDataTimeoutSecs");
        CopyBool(login, "reuseSSLsession", host, "DefReuseSSL");
        CopyInt(login, "natuseserveripfordata", host, "NATUseServerIPForData");
        CopyString(login, "natclientip", host, "NATClientIP", includeEmpty: true);
        CopyString(login, "addcom", host, "AddCom", includeEmpty: true);
        CopyBool(login, "blinddownloads", host, "DefBlindDL");
        HostBrowseSettings.ApplyProxy(login, host, "FTP");
        return await _rest.BrowseFtpHostV0Async(BuildBrowseRequest(hostId, login, directory, mask, taskId, stepId), cancellationToken);
    }

    private async Task<JsonObject> BrowseS3Async(string hostId, JsonObject host, string directory, string mask, string bucket, string taskId, string stepId, CancellationToken cancellationToken)
    {
        var login = BuildCloudLogin(host);
        HostBrowseSettings.ApplyProxy(login, host, "S3");
        var request = new JsonObject
        {
            ["showfiles"] = bucket.Length > 0,
            ["mask"] = mask,
            ["hostid"] = hostId,
            ["logininfo"] = login,
            ["dirname"] = directory,
            ["taskid"] = taskId.Length == 0 ? null : taskId,
            ["stepid"] = stepId.Length == 0 ? null : stepId,
            ["bucket"] = bucket,
            ["endpoint"] = HostBrowseSettings.Flag(host, "IsNonAmazonEndPoint") ? Read(host, "S3AlternativeEndpoint") : string.Empty
        };
        HostCloudAuthenticationSettings.ApplyS3Browse(request, host);
        return await _rest.BrowseS3HostV0Async(request, cancellationToken);
    }

    private async Task<JsonObject> BrowseAzureBlobAsync(string hostId, JsonObject host, string directory, string mask, string container, string taskId, string stepId, CancellationToken cancellationToken)
    {
        if (container.Equals("$logs", StringComparison.OrdinalIgnoreCase) || container.Equals("$blobchangefeed", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Azure container '{container}' cannot be browsed by MOVEit. Enter the folder manually.");
        var login = BuildCloudLogin(host);
        HostBrowseSettings.ApplyProxy(login, host, "AzureBlob");
        var request = new JsonObject
        {
            ["showfiles"] = container.Length > 0,
            ["mask"] = mask,
            ["hostid"] = hostId,
            ["logininfo"] = login,
            ["dirname"] = container.Length == 0 ? string.Empty : (directory.Length == 0 ? "/" : directory),
            ["taskid"] = taskId.Length == 0 ? null : taskId,
            ["stepid"] = stepId.Length == 0 ? null : stepId,
            ["container"] = container
        };
        return await _rest.BrowseAzureBlobHostV0Async(request, cancellationToken);
    }

    private async Task<JsonObject> BrowseGoogleStorageAsync(string hostId, JsonObject host, string directory, string mask, string bucket, string taskId, string stepId, CancellationToken cancellationToken)
    {
        var login = BuildCloudLogin(host);
        HostBrowseSettings.ApplyProxy(login, host, "GoogleStorage");
        var request = new JsonObject
        {
            ["showfiles"] = bucket.Length > 0,
            ["mask"] = mask,
            ["hostid"] = hostId,
            ["logininfo"] = login,
            ["dirname"] = directory,
            ["taskid"] = taskId.Length == 0 ? null : taskId,
            ["stepid"] = stepId.Length == 0 ? null : stepId,
            ["bucket"] = bucket,
            ["useoauth"] = false,
            ["projectid"] = null,
            ["oauthclientid"] = null,
            ["oauthclientsecret"] = null
        };
        HostBrowseSettings.ApplyGoogleAuthentication(request, host);
        return await _rest.BrowseGoogleStorageHostV0Async(request, cancellationToken);
    }

    private async Task<JsonObject> BrowseSharePointAsync(
        string hostId,
        JsonObject host,
        string directory,
        string mask,
        string documentLibrary,
        string documentLibraryGuid,
        string taskId,
        string stepId,
        CancellationToken cancellationToken)
    {
        var request = HostSharePointBrowseContract.Build(hostId, host, directory, mask, documentLibrary, documentLibraryGuid, taskId, stepId);
        return await _rest.BrowseSharePointHostV0Async(request, cancellationToken);
    }

    private static JsonObject BuildCommonLogin(JsonObject host)
    {
        var server = Read(host, "Host");
        var username = Read(host, "DefUsername");
        var port = ReadInt(host, "Port");
        if (server.Length == 0 || username.Length == 0 || port <= 0)
            throw new InvalidOperationException("The selected Host is incomplete. Host, Port, and Username are required.");
        return new JsonObject
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
    }

    private static JsonObject BuildCloudLogin(JsonObject host)
        => new()
        {
            ["secure"] = ReadBool(host, "Secure"),
            ["username"] = Read(host, "DefUsername"),
            ["password"] = null,
            ["conntimeoutsecs"] = ReadInt(host, "DefConnTimeoutSecs"),
            ["datatimeoutsecs"] = ReadInt(host, "DefDataTimeoutSecs"),
            ["proxytype"] = First(Read(host, "ProxyType"), "None"),
            ["proxypassword"] = null
        };

    private static JsonObject BuildBrowseRequest(string hostId, JsonObject login, string directory, string mask, string taskId, string stepId)
        => new()
        {
            ["showfiles"] = true,
            ["mask"] = mask,
            ["hostid"] = hostId,
            ["logininfo"] = login,
            ["dirname"] = directory,
            ["taskid"] = taskId.Length == 0 ? null : taskId,
            ["stepid"] = stepId.Length == 0 ? null : stepId
        };

    private static void EnsureListingHost(JsonObject host, string label)
    {
        if (ReadBool(host, "DefBlindDL"))
            throw new InvalidOperationException($"The selected {label} Host uses Blind Downloads, so MOVEit cannot browse its directories.");
    }

    private async Task<JsonObject> EnrichAsync(string hostId, string expectedType, JsonObject checkoutDefinition, CancellationToken cancellationToken)
    {
        try
        {
            var live = await _rest.GetHostAsync(hostId, cancellationToken);
            var (liveType, liveDefinition) = GetDefinition(live);
            if (liveDefinition is null || !liveType.Equals(expectedType, StringComparison.OrdinalIgnoreCase)) return checkoutDefinition;
            var merged = (JsonObject)checkoutDefinition.DeepClone();
            foreach (var pair in liveDefinition)
            {
                if (!HasValue(merged, pair.Key) && pair.Value is not null) merged[pair.Key] = pair.Value.DeepClone();
            }
            return merged;
        }
        catch (MoveItAuthenticationException) { throw; }
        catch (MoveItApiException ex)
        {
            _logger.LogDebug(ex, "Browse could not enrich Host {HostId}; continuing with native checkout fields.", hostId);
            return checkoutDefinition;
        }
    }

    private static IReadOnlyList<AutomationHostBrowseItem> ParseRootItems(JsonObject response, TraditionalTaskLocationRootKind rootKind)
    {
        var result = new List<AutomationHostBrowseItem>();
        var buckets = GetNode(response, "Buckets") as JsonObject;
        var node = buckets is null ? null : GetNode(buckets, "Bucket");
        if (node is JsonArray array)
        {
            foreach (var item in array) AddRootItem(result, item, rootKind);
        }
        else if (node is not null)
        {
            AddRootItem(result, node, rootKind);
        }
        return result.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddRootItem(List<AutomationHostBrowseItem> target, JsonNode? node, TraditionalTaskLocationRootKind rootKind)
    {
        if (node is null) return;
        if (node is JsonValue scalar)
        {
            var value = NodeString(scalar).Trim();
            if (value.Length > 0) target.Add(new AutomationHostBrowseItem(value, string.Empty, true, string.Empty, rootKind.ToString()));
            return;
        }
        if (node is not JsonObject obj) return;
        var name = First(Read(obj, "Name"), Read(obj, "Bucket"), Read(obj, "Container"), Read(obj, "DocumentLibrary"));
        if (name.Length == 0) return;
        target.Add(new AutomationHostBrowseItem(
            name,
            string.Empty,
            true,
            First(Read(obj, "GUID"), Read(obj, "ID")),
            rootKind.ToString()));
    }

    private static IReadOnlyList<AutomationHostBrowseItem> ParseItems(JsonObject response, string currentDirectory, string typeCode)
    {
        var result = new List<AutomationHostBrowseItem>();
        AddCollection(result, response, "Dirs", "Dir", true, currentDirectory, typeCode);
        AddCollection(result, response, "Folders", "Folder", true, currentDirectory, typeCode);
        AddCollection(result, response, "Files", "File", false, currentDirectory, typeCode);
        return result
            .GroupBy(item => $"{(item.IsDirectory ? 'D' : 'F')}\u001f{item.Path}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(item => item.IsDirectory)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddCollection(List<AutomationHostBrowseItem> target, JsonObject response, string containerName, string itemName, bool isDirectory, string currentDirectory, string typeCode)
    {
        var container = GetNode(response, containerName) as JsonObject;
        var node = container is null ? null : GetNode(container, itemName);
        if (node is null) return;
        if (node is JsonArray array)
        {
            foreach (var item in array) AddItem(target, item, isDirectory, currentDirectory, typeCode);
            return;
        }
        AddItem(target, node, isDirectory, currentDirectory, typeCode);
    }

    private static void AddItem(List<AutomationHostBrowseItem> target, JsonNode? node, bool defaultDirectory, string currentDirectory, string typeCode)
    {
        if (node is null) return;
        if (node is JsonValue scalar)
        {
            var value = NodeString(scalar).Trim();
            if (value.Length == 0) return;
            target.Add(new AutomationHostBrowseItem(GetLeafName(value), value, defaultDirectory));
            return;
        }
        if (node is not JsonObject obj) return;

        var isDirectory = defaultDirectory;
        var nativeIsDir = Read(obj, "IsDir");
        if (nativeIsDir.Length > 0) isDirectory = nativeIsDir == "1" || nativeIsDir.Equals("true", StringComparison.OrdinalIgnoreCase);

        var path = First(
            Read(obj, "DirName"),
            Read(obj, "FolderPath"),
            Read(obj, "FullPath"),
            Read(obj, "Path"),
            Read(obj, "FilePath"));
        var name = First(
            Read(obj, "Name"),
            Read(obj, "FileName"),
            Read(obj, "Filename"),
            Read(obj, "FolderName"),
            path.Length > 0 ? GetLeafName(path) : string.Empty);
        if (path.Length == 0 && name.Length > 0) path = CombinePath(currentDirectory, name, typeCode);
        if (name.Length == 0 || path.Length == 0) return;
        target.Add(new AutomationHostBrowseItem(
            name,
            path,
            isDirectory,
            First(Read(obj, "ID"), Read(obj, "FolderID"), Read(obj, "FileID"), Read(obj, "GUID")),
            First(Read(obj, "Type"), Read(obj, "FolderType")),
            First(Read(obj, "Size"), Read(obj, "Length")),
            First(Read(obj, "LastModified"), Read(obj, "Modified"), Read(obj, "Date"))));
    }

    private static string ToShareTaskPath(string path, string shareRoot)
    {
        var value = (path ?? string.Empty).Trim();
        var root = (shareRoot ?? string.Empty).Trim().TrimEnd('\\', '/');
        if (value.Length == 0) return string.Empty;
        if (root.Length > 0 && value.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            var relative = value[root.Length..].Replace('\\', '/');
            return relative.Length == 0 ? "/" : (relative.StartsWith('/') ? relative : "/" + relative);
        }
        return value.Replace('\\', '/');
    }

    private static string ResolveShareDirectory(string share, string directory)
    {
        if (directory.Length == 0) return share;
        if (directory.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase) || directory.StartsWith(share, StringComparison.OrdinalIgnoreCase)) return directory;
        return share.TrimEnd('\\', '/') + "\\" + directory.TrimStart('\\', '/');
    }

    private static string GetParentDirectory(string path, string typeCode)
    {
        var value = (path ?? string.Empty).Trim();
        if (value.Length == 0 || value == "/") return string.Empty;
        if (typeCode.Equals("Share", StringComparison.OrdinalIgnoreCase) && value.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var trimmed = value.TrimEnd('\\');
            var idx = trimmed.LastIndexOf('\\');
            if (idx <= 1) return string.Empty;
            var parent = trimmed[..idx];
            var segments = parent.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
            return segments.Length < 2 ? string.Empty : parent;
        }
        var normalized = value.Replace('\\', '/').TrimEnd('/');
        var slash = normalized.LastIndexOf('/');
        if (slash < 0) return string.Empty;
        return slash == 0 ? "/" : normalized[..slash];
    }

    private static string CombinePath(string directory, string name, string typeCode)
    {
        var separator = typeCode.Equals("Share", StringComparison.OrdinalIgnoreCase) ? '\\' : '/';
        if (string.IsNullOrWhiteSpace(directory)) return typeCode.Equals("SharePoint", StringComparison.OrdinalIgnoreCase) ? name : "/" + name.TrimStart('/');
        return directory.TrimEnd('\\', '/') + separator + name.TrimStart('\\', '/');
    }

    private static string GetLeafName(string path)
    {
        var trimmed = (path ?? string.Empty).TrimEnd('\\', '/');
        var idx = Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf('\\'));
        return idx >= 0 ? trimmed[(idx + 1)..] : trimmed;
    }

    private (string TypeCode, JsonObject? Definition) GetDefinition(JsonObject? wrapper)
    {
        if (wrapper is null) return (string.Empty, null);
        var (knownType, knownHost) = HostDefinitionJson.UnwrapHost(wrapper);
        if (knownType == "OneDrive") return (knownType, knownHost);
        foreach (var profile in _profiles.Profiles)
        {
            if (GetNode(wrapper, profile.TypeCode) is JsonObject value) return (profile.TypeCode, value);
            if (profile.TypeCode.Equals("siLock", StringComparison.OrdinalIgnoreCase)
                && GetNode(wrapper, "SiLock") is JsonObject aliasValue) return (profile.TypeCode, aliasValue);
        }
        if (Read(wrapper, "ID").Length > 0) return (_profiles.NormalizeType(Read(wrapper, "Type")), wrapper);
        return (string.Empty, null);
    }

    private static JsonNode? GetNode(JsonObject? obj, string name)
    {
        if (obj is null) return null;
        foreach (var pair in obj) if (pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)) return pair.Value;
        return null;
    }

    private static bool HasValue(JsonObject obj, string name) => GetNode(obj, name) is not null && NodeString(GetNode(obj, name)).Trim().Length > 0;
    private static string Read(JsonObject? obj, string name) => NodeString(GetNode(obj, name)).Trim();
    private static int ReadInt(JsonObject obj, string name) => int.TryParse(Read(obj, name), out var value) ? value : 0;
    private static bool ReadBool(JsonObject obj, string name) => Read(obj, name) is var value && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));
    private static string NodeString(JsonNode? node)
    {
        if (node is null) return string.Empty;
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text)) return text ?? string.Empty;
            return value.ToJsonString().Trim('"');
        }
        return string.Empty;
    }

    private static string First(params string[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    private static void CopyString(JsonObject target, string targetName, JsonObject source, string sourceName, bool includeEmpty = false)
    {
        var value = Read(source, sourceName);
        if (value.Length > 0 || includeEmpty) target[targetName] = value;
    }
    private static void CopyInt(JsonObject target, string targetName, JsonObject source, string sourceName) { var value = Read(source, sourceName); if (int.TryParse(value, out var parsed)) target[targetName] = parsed; }
    private static void CopyBool(JsonObject target, string targetName, JsonObject source, string sourceName) { var value = Read(source, sourceName); if (value.Length > 0) target[targetName] = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase); }
}
