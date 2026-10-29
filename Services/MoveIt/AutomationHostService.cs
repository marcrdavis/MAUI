// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Services.MoveIt;

public interface IAutomationHostService
{
    // Retrieves the permitted MOVEit Host inventory and derives visible Task assignments from HostID references.
    Task<IReadOnlyList<AutomationHost>> GetHostsAsync(CancellationToken cancellationToken = default);
    // Returns any existing Host cache immediately for initial navigation and indicates whether it should be reconciled in the background.
    Task<InitialCacheSnapshot<HostCacheSnapshot>> GetInitialHostSnapshotAsync(CancellationToken cancellationToken = default);
    // Returns Host data plus the timestamp of the live MOVEit read that populated the cache.
    Task<HostCacheSnapshot> GetHostSnapshotAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
    // Returns the visible Host count without loading task dependency data.
    Task<int> GetHostCountAsync(CancellationToken cancellationToken = default);
    // Returns Host ID -> friendly Host type without loading Task assignment/dependency data.
    Task<IReadOnlyDictionary<string, string>> GetHostTypeMapAsync(CancellationToken cancellationToken = default);
    // Retrieves one Host with the same common fields used by the Host Information modal.
    Task<AutomationHost> GetHostAsync(string hostId, CancellationToken cancellationToken = default);
    // Retrieves the visible Tasks that reference the requested Host ID.
    Task<IReadOnlyList<AutomationHostTaskReference>> GetAssignedTasksAsync(string hostId, CancellationToken cancellationToken = default);
    Task<JsonObject> GetHostDefinitionAsync(string hostId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HostSshClientKeyOption>> GetSshClientKeysAsync(CancellationToken cancellationToken = default);
    Task<HostSshAlgorithmCatalog> GetSshAlgorithmsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HostTlsCertificateOption>> GetHostTlsCertificatesAsync(string hostId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HostDocumentLibraryOption>> BrowseDocumentLibrariesAsync(HostDocumentLibraryRequest request, CancellationToken cancellationToken = default);
    Task<string> RetrieveSshHostFingerprintAsync(HostFingerprintRequest request, CancellationToken cancellationToken = default);
    Task<HostDefinitionSaveResult> SaveDefinitionAsync(string hostId, HostDefinitionSaveRequest request,
        string username, CancellationToken cancellationToken = default);
    Task<MoveItCreatedHost> CreateConfiguredHostAsync(HostBasicDefinition definition, CancellationToken cancellationToken = default);
    Task<HostConfigurationSnapshot> UpdateConfiguredHostAsync(string hostId, HostBasicUpdate update, CancellationToken cancellationToken = default);
    // Creates a minimal persistent Host through v0 REST, then completes it in the preferred editor.
    Task<MoveItCreatedHost> CreateHostAsync(string name, string hostType, CancellationToken cancellationToken = default, string? unc = null, string? resourceGroup = null);
    Task<MoveItCreatedHost> CloneHostAsync(string sourceId, string name, CancellationToken cancellationToken = default);
    // Updates the common editable Host Name and Description while preserving the rest of the native Host configuration.
    Task UpdateBasicDetailsAsync(string hostId, string name, string description, CancellationToken cancellationToken = default);
    // Reads the complete Host configuration used by the per-user undo snapshot.
    Task<HostConfigurationSnapshot> GetHostConfigurationSnapshotAsync(string hostId, CancellationToken cancellationToken = default);
    // Restores a complete Host configuration from an undo snapshot and invalidates cached Host state.
    Task<HostConfigurationSnapshot> RestoreHostConfigurationAsync(string hostId, string hostXml, CancellationToken cancellationToken = default);
    // Performs a live safety check before destructive Host deletion.
    Task<HostDeletePreflightResult> PreflightDeleteHostAsync(string hostId, CancellationToken cancellationToken = default);
    // Performs one live inventory/dependency read and evaluates every selected Host against the same deletion guardrails.
    Task<IReadOnlyList<HostDeletePreflightResult>> PreflightDeleteHostsAsync(IEnumerable<string> hostIds, CancellationToken cancellationToken = default);
    // Re-runs deletion safety checks, deletes the Host through COM, and invalidates cached Host state.
    Task<HostDeleteResult> DeleteHostAsync(string hostId, CancellationToken cancellationToken = default);
}

public sealed record HostCacheSnapshot(IReadOnlyList<AutomationHost> Hosts, DateTimeOffset RefreshedUtc);
public sealed record HostConfigurationSnapshot(string Id, string Name, string TypeCode, string Xml);
public sealed record HostDeleteAlternateReference(string Id, string Name);
public sealed record HostDeletePreflightResult(
    bool CanDelete,
    string HostId,
    string HostName,
    IReadOnlyList<AutomationHostTaskReference> TaskReferences,
    IReadOnlyList<HostDeleteAlternateReference> AlternateHostReferences,
    string Message);
public sealed record HostDeleteResult(string Id, string Name);

public sealed class HostNameAlreadyExistsException : InvalidOperationException
{
    public string HostName { get; }

    // Initializes the duplicate-name exception with the conflicting Host Name.
    public HostNameAlreadyExistsException(string hostName)
        : base($"A host named '{hostName}' already exists. Enter a unique Host Name.")
    {
        HostName = hostName;
    }
}

public sealed class AutomationHostService : IAutomationHostService
{
    private const string CachePrefix = "MoveItAutomationUI:Hosts:";

    private readonly IMoveItRestClient _restClient;
    private readonly IHostCrudCoordinator _crud;
    private readonly IMoveItSessionService _sessionService;
    private readonly IMemoryCache _memoryCache;
    private readonly HostBehaviorOptions _hostBehaviorOptions;

    // REST supplies Host inventory/dependencies and New Host shells; native XML workflows retain COM.
    public AutomationHostService(
        IMoveItRestClient restClient,
        IHostCrudCoordinator crud,
        IMoveItSessionService sessionService,
        IMemoryCache memoryCache,
        IOptionsSnapshot<HostBehaviorOptions> hostBehaviorOptions)
    {
        _restClient = restClient;
        _crud = crud;
        _sessionService = sessionService;
        _memoryCache = memoryCache;
        _hostBehaviorOptions = hostBehaviorOptions.Value;
    }

    // Purpose: Returns host count.
    public async Task<int> GetHostCountAsync(CancellationToken cancellationToken = default)
    {
        var cacheKey = GetCacheKey();
        if (_memoryCache.TryGetValue<HostCacheEntry>(cacheKey, out var cached) && cached is not null && MoveItServiceCache.IsFresh(cached.RefreshedUtc, _hostBehaviorOptions.CacheRefreshSeconds.Value))
            return cached.Hosts.Count;
        return (await _crud.GetDefinitionsAsync(cancellationToken)).Count;
    }

    // Returns the lightweight Host-type lookup used by the Task Calendar. This deliberately avoids
    // the normal Host inventory's Task-assignment enrichment because the Calendar already loads the
    // task definitions it needs for source/reference classification.
    public async Task<IReadOnlyDictionary<string, string>> GetHostTypeMapAsync(CancellationToken cancellationToken = default)
    {
        var emptyAssignments = new Dictionary<string, IReadOnlyList<AutomationHostTaskReference>>(StringComparer.OrdinalIgnoreCase);
        return (await _crud.GetDefinitionsAsync(cancellationToken))
            .Select(item => ParseHost(item, emptyAssignments))
            .Where(host => host.Id.Length > 0)
            .GroupBy(host => host.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Type, StringComparer.OrdinalIgnoreCase);
    }

    // Purpose: Returns the current normalized Host inventory.
    public async Task<IReadOnlyList<AutomationHost>> GetHostsAsync(CancellationToken cancellationToken = default)
        => (await GetHostSnapshotAsync(false, cancellationToken)).Hosts;

    // Returns the initial Host snapshot through the shared cached-inventory navigation contract.
    public async Task<InitialCacheSnapshot<HostCacheSnapshot>> GetInitialHostSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var cacheKey = GetCacheKey();
        return await MoveItServiceCache.ResolveInitialAsync<HostCacheEntry, HostCacheSnapshot>(
            _memoryCache,
            cacheKey,
            _hostBehaviorOptions.CacheRefreshSeconds.Value,
            entry => entry.RefreshedUtc,
            entry => new HostCacheSnapshot(entry.Hosts, entry.RefreshedUtc),
            token => GetHostSnapshotAsync(false, token),
            cancellationToken);
    }

    // Returns Host data plus the timestamp of the live MOVEit read that populated the cache. UI refreshes can force a live read.
    public async Task<HostCacheSnapshot> GetHostSnapshotAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        var cacheKey = GetCacheKey();
        var refreshed = await RefreshHostsCoreAsync(cacheKey, forceRefresh, cancellationToken);
        return new HostCacheSnapshot(refreshed.Hosts, refreshed.RefreshedUtc);
    }

    // Reads common Host Information from the loaded Host cache when possible, matching Task Information behavior.
    public async Task<AutomationHost> GetHostAsync(string hostId, CancellationToken cancellationToken = default)
    {
        var normalizedHostId = NormalizeHostId(hostId);
        var hosts = await GetHostsAsync(cancellationToken);
        var cached = hosts.FirstOrDefault(host => string.Equals(host.Id, normalizedHostId, StringComparison.OrdinalIgnoreCase));
        if (cached is not null) return cached;

        // Fallback for a Host created after this process's cache snapshot.
        var hostTask = _crud.GetDefinitionAsync(normalizedHostId, cancellationToken);
        var taskReferencesTask = _restClient.GetTaskHostReferenceDefinitionsAsync(cancellationToken);
        await Task.WhenAll(hostTask, taskReferencesTask);
        var assignments = BuildTaskAssignments(await taskReferencesTask);
        var host = ParseHost(await hostTask, assignments);
        if (string.IsNullOrWhiteSpace(host.Id))
            throw new MoveItApiException($"MOVEit returned an invalid Host response for Host ID '{normalizedHostId}'.");
        return host;
    }

    // Purpose: Returns assigned tasks.
    public async Task<IReadOnlyList<AutomationHostTaskReference>> GetAssignedTasksAsync(string hostId, CancellationToken cancellationToken = default)
        => (await GetHostAsync(hostId, cancellationToken)).AssignedTasks;

    public Task<JsonObject> GetHostDefinitionAsync(string hostId, CancellationToken cancellationToken = default)
        => _crud.GetDefinitionAsync(NormalizeHostId(hostId), cancellationToken);

    public Task<HostSshAlgorithmCatalog> GetSshAlgorithmsAsync(CancellationToken cancellationToken = default)
        => _crud.GetSshAlgorithmsAsync(cancellationToken);

    public Task<IReadOnlyList<HostSshClientKeyOption>> GetSshClientKeysAsync(CancellationToken cancellationToken = default)
        => _crud.GetSshClientKeysAsync(cancellationToken);

    public async Task<string> RetrieveSshHostFingerprintAsync(HostFingerprintRequest request, CancellationToken cancellationToken = default)
    {
        var id = NormalizeHostId(request.HostId);
        var live = (await GetHostSnapshotAsync(true, cancellationToken)).Hosts.FirstOrDefault(host => host.Id == id);
        if (live is null || !ObjectCheckout.CanEditCheckedOut("Host", id, live.CanEditDelete) || live.TypeCode != "SSHFTP")
            throw new InvalidOperationException("The current MOVEit account cannot retrieve a key for this SFTP Host.");
        return await _crud.RetrieveSshHostFingerprintAsync(request, cancellationToken);
    }

    public async Task<IReadOnlyList<HostTlsCertificateOption>> GetHostTlsCertificatesAsync(string hostId, CancellationToken cancellationToken = default)
    {
        await EnsureHostEditorLookupAllowed(hostId, new[] { "FTP", "SharePoint", "siLock" }, cancellationToken);
        return await _crud.GetTlsCertificatesAsync(cancellationToken);
    }
    public async Task<IReadOnlyList<HostDocumentLibraryOption>> BrowseDocumentLibrariesAsync(HostDocumentLibraryRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureHostEditorLookupAllowed(request.HostId, new[] { "SharePoint" }, cancellationToken);
        return await _crud.BrowseDocumentLibrariesAsync(request, cancellationToken);
    }
    private async Task EnsureHostEditorLookupAllowed(string hostId, IReadOnlyList<string> types, CancellationToken cancellationToken)
    {
        var id = NormalizeHostId(hostId);
        var live = (await GetHostSnapshotAsync(true, cancellationToken)).Hosts.FirstOrDefault(host => host.Id == id);
        if (live is null || !ObjectCheckout.CanEditCheckedOut("Host", id, live.CanEditDelete) || !types.Contains(live.TypeCode, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("The current MOVEit account cannot use this Host lookup.");
    }
    public async Task<HostDefinitionSaveResult> SaveDefinitionAsync(string hostId, HostDefinitionSaveRequest request,
        string username, CancellationToken cancellationToken = default)
    {
        var id = NormalizeHostId(hostId);
        var live = await GetHostSnapshotAsync(true, cancellationToken);
        try { return await _crud.SaveDefinitionAsync(id, request, username, live, cancellationToken); }
        finally { InvalidateHostCache(); } // Includes committed-but-unverified mutations.
    }

    private async Task<T> MutateAsync<T>(Func<Task<T>> mutation)
    {
        try { return await mutation(); }
        finally { InvalidateHostCache(); }
    }

    private async Task MutateAsync(Func<Task> mutation)
    {
        try { await mutation(); }
        finally { InvalidateHostCache(); }
    }

    // Performs a live duplicate-name check immediately before REST v0 creates the New Host shell.
    public async Task<MoveItCreatedHost> CreateHostAsync(string name, string hostType, CancellationToken cancellationToken = default, string? unc = null, string? resourceGroup = null)
    {
        var normalizedName = (name ?? string.Empty).Trim();
        if (normalizedName.Length == 0) throw new InvalidOperationException("Host Name is required.");

        if (await _crud.NameExistsAsync(normalizedName, cancellationToken))
            throw new HostNameAlreadyExistsException(normalizedName);

        var normalizedType = (hostType ?? string.Empty).Trim();
        // Fresh visible inventory catches duplicate UNC connections before the write.
        // MOVEit remains authoritative if another creator races this read.
        if (string.Equals(normalizedType, "Share", StringComparison.OrdinalIgnoreCase))
        {
            unc = HostV0Creation.ValidateUnc(unc);
            var definitions = await _crud.GetDefinitionsAsync(cancellationToken);
            foreach (var definition in definitions)
            {
                var (type, host) = HostDefinitionJson.UnwrapHost(definition);
                var existing = HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(host, "UNC")).Trim().TrimEnd('\\');
                if (type.Equals("Share", StringComparison.OrdinalIgnoreCase)
                    && existing.Equals(unc.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("A UNC Share Host already uses this Resource UNC. Enter a unique UNC path.");
            }
        }

        return await MutateAsync(() => _crud.CreateShellAsync(normalizedName, normalizedType, cancellationToken, unc, resourceGroup));
    }

    public async Task<MoveItCreatedHost> CloneHostAsync(string sourceId, string name, CancellationToken cancellationToken = default)
    {
        var id = NormalizeHostId(sourceId);
        var cloneName = (name ?? "").Trim();
        if (cloneName.Length == 0 || cloneName.Length > 254) throw new ArgumentException("Enter a Host Name from 1 to 254 characters.");
        var source = (await GetHostSnapshotAsync(true, cancellationToken)).Hosts.FirstOrDefault(host => host.Id == id);
        if (source is null || !source.CanEditDelete || id == "0" || source.TypeCode == "FileSystem")
            throw new InvalidOperationException("This Host is unavailable or cannot be cloned by the current MOVEit user.");
        if (await _crud.NameExistsAsync(cloneName, cancellationToken)) throw new HostNameAlreadyExistsException(cloneName);
        return await MutateAsync(() => _crud.CloneAsync(id, cloneName, cancellationToken));
    }

    // Shared create path for CSV/XLSX and future native Host Editors.
    public async Task<MoveItCreatedHost> CreateConfiguredHostAsync(HostBasicDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (await _crud.NameExistsAsync(definition.Name, cancellationToken))
            throw new HostNameAlreadyExistsException(definition.Name);
        return await MutateAsync(() => _crud.CreateConfiguredAsync(definition, cancellationToken));
    }

    // Shared Host CRUD update: preserve the existing ID and ensure the current user can edit
    // this exact Host before the COM layer patches its checked-out native definition.
    public async Task<HostConfigurationSnapshot> UpdateConfiguredHostAsync(string hostId, HostBasicUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        var id = NormalizeHostId(hostId);
        var matches = (await GetHostSnapshotAsync(true, cancellationToken)).Hosts
            .Where(host => host.Name.Equals(update.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1 || !matches[0].Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The target Host no longer matches the analyzed Host. Analyze the spreadsheet again.");
        var current = matches[0];
        if (!current.CanEditDelete)
            throw new InvalidOperationException("The MOVEit account does not have permission to edit the target Host.");
        if (!current.TypeCode.Equals(update.HostType, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The target Host type changed. Analyze the spreadsheet again.");
        var result = await MutateAsync(() => _crud.UpdateConfiguredAsync(id, update, cancellationToken));
        return new HostConfigurationSnapshot(result.Id, result.Name, result.TypeCode, result.Xml);
    }

    // Validates and updates the two common MOVEit Host information fields exposed by MAUI.
    public async Task UpdateBasicDetailsAsync(string hostId, string name, string description, CancellationToken cancellationToken = default)
    {
        var normalizedHostId = NormalizeHostId(hostId);
        var normalizedName = (name ?? string.Empty).Trim();
        if (normalizedName.Length == 0) throw new InvalidOperationException("Host Name is required.");
        if (normalizedName.Length > 254) throw new InvalidOperationException("Host Name cannot exceed 254 characters.");

        // Force a live inventory read so edit permission and the current Friendly Name are not taken from stale cache data.
        var live = (await GetHostSnapshotAsync(true, cancellationToken)).Hosts
            .FirstOrDefault(host => string.Equals(host.Id, normalizedHostId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Host '{normalizedHostId}' is no longer available to the current MOVEit user.");
        if (!live.CanEditDelete)
            throw new InvalidOperationException($"Your MOVEit account does not have permission to edit Host '{live.Name}'.");

        if (!live.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase)
            && await _crud.NameExistsAsync(normalizedName, cancellationToken))
            throw new HostNameAlreadyExistsException(normalizedName);

        await MutateAsync(() => _crud.UpdateBasicDetailsAsync(normalizedHostId, normalizedName, description ?? string.Empty, cancellationToken));
    }

    // Purpose: Returns host configuration snapshot.
    public async Task<HostConfigurationSnapshot> GetHostConfigurationSnapshotAsync(string hostId, CancellationToken cancellationToken = default)
    {
        var result = await _crud.GetNativeConfigurationAsync(hostId, cancellationToken);
        return new HostConfigurationSnapshot(result.Id, result.Name, result.TypeCode, result.Xml);
    }

    // Purpose: Restores host configuration from a previously captured configuration.
    public async Task<HostConfigurationSnapshot> RestoreHostConfigurationAsync(string hostId, string hostXml, CancellationToken cancellationToken = default)
    {
        var result = await MutateAsync(() => _crud.RestoreNativeConfigurationAsync(hostId, hostXml, cancellationToken));
        return new HostConfigurationSnapshot(result.Id, result.Name, result.TypeCode, result.Xml);
    }

    // Performs a live preflight so stale cache data can never authorize a destructive Host deletion.
    public async Task<HostDeletePreflightResult> PreflightDeleteHostAsync(string hostId, CancellationToken cancellationToken = default)
    {
        var normalizedHostId = NormalizeHostId(hostId);
        var results = await PreflightDeleteHostsAsync(new[] { normalizedHostId }, cancellationToken);
        return results[0];
    }

    // Evaluates a selected Host set using one authoritative live Host/Task snapshot.
    public async Task<IReadOnlyList<HostDeletePreflightResult>> PreflightDeleteHostsAsync(IEnumerable<string> hostIds, CancellationToken cancellationToken = default)
    {
        var normalizedHostIds = (hostIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(NormalizeHostId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalizedHostIds.Count == 0) return Array.Empty<HostDeletePreflightResult>();

        var hostDefinitionsTask = _crud.GetDefinitionsAsync(cancellationToken);
        var taskDefinitionsTask = _restClient.GetTaskHostReferenceDefinitionsAsync(cancellationToken);
        await Task.WhenAll(hostDefinitionsTask, taskDefinitionsTask);

        var hostDefinitions = await hostDefinitionsTask;
        var taskAssignments = BuildTaskAssignments(await taskDefinitionsTask);
        return normalizedHostIds
            .Select(hostId => BuildDeletePreflight(hostId, hostDefinitions, taskAssignments))
            .ToList();
    }

    // Re-runs preflight immediately before COM deletion so dependencies added after the confirmation dialog block the delete.
    public async Task<HostDeleteResult> DeleteHostAsync(string hostId, CancellationToken cancellationToken = default)
    {
        var normalizedHostId = NormalizeHostId(hostId);
        var preflight = await PreflightDeleteHostAsync(normalizedHostId, cancellationToken);
        if (!preflight.CanDelete)
            throw new InvalidOperationException(preflight.Message);

        await MutateAsync(() => _crud.DeleteAsync(normalizedHostId, cancellationToken));
        return new HostDeleteResult(preflight.HostId, preflight.HostName);
    }

    // Checks existence/permissions plus Task and alternate-Host references against one already-loaded live snapshot.
    private static HostDeletePreflightResult BuildDeletePreflight(
        string hostId,
        IReadOnlyList<JsonObject> hostDefinitions,
        IReadOnlyDictionary<string, IReadOnlyList<AutomationHostTaskReference>> taskAssignments)
    {
        JsonObject? targetWrapper = null;
        JsonObject? targetPayload = null;
        string targetTypeCode = string.Empty;

        foreach (var wrapper in hostDefinitions)
        {
            var (typeCode, payload) = UnwrapHost(wrapper);
            if (!GetHostString(payload, "ID").Trim().Equals(hostId, StringComparison.OrdinalIgnoreCase)) continue;
            targetWrapper = wrapper;
            targetPayload = payload;
            targetTypeCode = typeCode;
            break;
        }

        if (targetWrapper is null || targetPayload is null)
        {
            return new HostDeletePreflightResult(false, hostId, hostId,
                Array.Empty<AutomationHostTaskReference>(), Array.Empty<HostDeleteAlternateReference>(),
                "This Host no longer exists or is no longer accessible. Refresh the Hosts list and try again.");
        }

        var targetName = GetHostString(targetPayload, "Name").Trim();
        if (targetName.Length == 0) targetName = hostId;

        if (NormalizeTypeCode(targetTypeCode).Equals("FileSystem", StringComparison.OrdinalIgnoreCase))
        {
            return new HostDeletePreflightResult(false, hostId, targetName,
                Array.Empty<AutomationHostTaskReference>(), Array.Empty<HostDeleteAlternateReference>(),
                "The Local File System Host is managed by MOVEit Automation and cannot be deleted.");
        }

        // xAddEditDelete is authoritative when MOVEit returns it. Do not treat an omitted property as denial,
        // because some Host models/versions do not expose the flag consistently.
        var permissionNode = GetHostNode(targetPayload, "xAddEditDelete");
        if (permissionNode is not null && !ReadBoolean(permissionNode))
        {
            return new HostDeletePreflightResult(false, hostId, targetName,
                Array.Empty<AutomationHostTaskReference>(), Array.Empty<HostDeleteAlternateReference>(),
                $"Your MOVEit account does not have permission to delete Host '{targetName}'.");
        }

        taskAssignments.TryGetValue(hostId, out var taskReferences);
        taskReferences ??= Array.Empty<AutomationHostTaskReference>();

        var alternateReferences = new List<HostDeleteAlternateReference>();
        foreach (var wrapper in hostDefinitions)
        {
            var (_, payload) = UnwrapHost(wrapper);
            var referringId = GetHostString(payload, "ID").Trim();
            if (referringId.Length == 0 || referringId.Equals(hostId, StringComparison.OrdinalIgnoreCase)) continue;
            var secondaryHostId = GetHostString(payload, "ID2").Trim();
            if (!secondaryHostId.Equals(hostId, StringComparison.OrdinalIgnoreCase)) continue;
            var referringName = GetHostString(payload, "Name").Trim();
            alternateReferences.Add(new HostDeleteAlternateReference(referringId, referringName.Length == 0 ? referringId : referringName));
        }

        alternateReferences = alternateReferences
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (taskReferences.Count > 0 || alternateReferences.Count > 0)
        {
            var blockers = new List<string>();
            if (taskReferences.Count > 0)
                blockers.Add($"{taskReferences.Count} Task reference(s)");
            if (alternateReferences.Count > 0)
                blockers.Add($"{alternateReferences.Count} alternate Host reference(s)");

            return new HostDeletePreflightResult(false, hostId, targetName, taskReferences, alternateReferences,
                $"Cannot delete Host '{targetName}' because {string.Join(" and ", blockers)} still exist. Remove those assignments first.");
        }

        return new HostDeletePreflightResult(true, hostId, targetName, taskReferences, alternateReferences, string.Empty);
    }

    // Builds a HostID -> distinct visible Task list map from every nested task step carrying HostID.
    private static IReadOnlyDictionary<string, IReadOnlyList<AutomationHostTaskReference>> BuildTaskAssignments(IReadOnlyList<JsonObject> tasks)
    {
        var map = new Dictionary<string, Dictionary<string, AutomationHostTaskReference>>(StringComparer.OrdinalIgnoreCase);

        foreach (var task in tasks)
        {
            var taskId = MoveItRestClient.GetString(task, "ID").Trim();
            var taskName = MoveItRestClient.GetString(task, "Name").Trim();
            if (taskId.Length == 0) continue;

            var hostIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectHostIds(task["steps"], hostIds);
            CollectHostIds(task["NextActions"], hostIds);
            foreach (var hostId in hostIds)
            {
                if (hostId.Length == 0 || hostId == "0") continue;
                if (!map.TryGetValue(hostId, out var assignedTasks))
                {
                    assignedTasks = new Dictionary<string, AutomationHostTaskReference>(StringComparer.OrdinalIgnoreCase);
                    map[hostId] = assignedTasks;
                }
                assignedTasks[taskId] = new AutomationHostTaskReference(taskId, taskName);
            }
        }

        return map.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<AutomationHostTaskReference>)pair.Value.Values
                .OrderBy(task => task.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(task => task.Id, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            StringComparer.OrdinalIgnoreCase);
    }

    // Recursively walks Advanced/Traditional task step JSON and collects every HostID value.
    private static void CollectHostIds(JsonNode? node, ISet<string> hostIds)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj)
            {
                if (pair.Key.Equals("HostID", StringComparison.OrdinalIgnoreCase))
                {
                    var value = ReadNodeString(pair.Value).Trim();
                    if (value.Length > 0) hostIds.Add(value);
                    continue;
                }
                CollectHostIds(pair.Value, hostIds);
            }
            return;
        }

        if (node is JsonArray array)
        {
            foreach (var item in array) CollectHostIds(item, hostIds);
        }
    }

    // Normalizes the polymorphic REST Host wrapper into the common MAUI Host model.
    // Host property casing is not completely consistent across MOVEit Host models (for example,
    // the FTP REST schema exposes host/port in lower case), so common fields are read
    // case-insensitively here rather than changing the shared REST JSON helpers.
    private static AutomationHost ParseHost(JsonObject wrapper, IReadOnlyDictionary<string, IReadOnlyList<AutomationHostTaskReference>> assignments)
    {
        var (typeCode, host) = UnwrapHost(wrapper);
        var id = GetHostString(host, "ID").Trim();
        var destination = ReadHostDestination(wrapper, host);
        var group = ReadGroups(GetHostNode(host, "Group"));
        var secure = FormatSecure(typeCode, FirstNonBlank(
            GetHostString(wrapper, "Secure"),
            GetHostString(host, "Secure")));
        assignments.TryGetValue(id, out var assignedTasks);

        // MOVEit does not expose xAddEditDelete consistently across every polymorphic Host response.
        // When it is present, it is authoritative; when it is omitted, omission is not a permission denial.
        // Check both the type-specific Host payload and the outer wrapper because MOVEit versions/Host types
        // may place the permission flag at either level. This matches the live delete-preflight semantics.
        var permissionNode = GetHostNode(host, "xAddEditDelete") ?? GetHostNode(wrapper, "xAddEditDelete");
        var canEditDelete = permissionNode is null || ReadBoolean(permissionNode);

        return new AutomationHost
        {
            Id = id,
            Name = GetHostString(host, "Name").Trim(),
            Description = GetHostString(host, "Desc").Trim(),
            TypeCode = typeCode,
            Type = FriendlyHostType(typeCode),
            Destination = destination,
            Secure = secure,
            Port = GetHostNullableInt(host, "Port"),
            Username = GetHostString(host, "DefUsername").Trim(),
            Group = group,
            CanEditDelete = canEditDelete,
            AssignedTasks = assignedTasks ?? Array.Empty<AutomationHostTaskReference>()
        };
    }

    // Finds the one type-specific Host payload inside the REST polymorphic wrapper.
    private static (string TypeCode, JsonObject Host) UnwrapHost(JsonObject wrapper)
    {
        string[] typeKeys =
        {
            "FileSystem", "Share", "siLock", "SiLock", "FTP", "SSHFTP", "POP3", "SMTP", "S3",
            "AzureBlob", "SharePoint", "GoogleStorage", "OneDrive", "AS1", "AS2", "AS3"
        };

        foreach (var key in typeKeys)
        {
            if (wrapper[key] is JsonObject typedHost)
                return (NormalizeTypeCode(key), typedHost);
        }

        if (!string.IsNullOrWhiteSpace(MoveItRestClient.GetString(wrapper, "ID")))
        {
            var type = MoveItRestClient.GetString(wrapper, "Type");
            return (NormalizeTypeCode(type), wrapper);
        }

        var firstObject = wrapper.FirstOrDefault(pair => pair.Value is JsonObject);
        return firstObject.Value is JsonObject fallback
            ? (NormalizeTypeCode(firstObject.Key), fallback)
            : (string.Empty, wrapper);
    }

    // Converts MOVEit Host type codes into labels used by the grid and Host Information modal.
    internal static string FriendlyHostType(string typeCode) => NormalizeTypeCode(typeCode) switch
    {
        "FileSystem" => "Local File System",
        "Share" => "UNC Share",
        "siLock" => "MOVEit Transfer",
        "FTP" => "FTP/FTPS",
        "SSHFTP" => "SSH/SFTP",
        "POP3" => "POP3 (Incoming Email)",
        "SMTP" => "SMTP (Outgoing Email)",
        "S3" => "Amazon S3",
        "OneDrive" => "Microsoft OneDrive",
        "AzureBlob" => "Azure Blob",
        "SharePoint" => "SharePoint",
        "GoogleStorage" => "Google Storage",
        "AS1" => "AS1",
        "AS2" => "AS2",
        "AS3" => "AS3",
        _ => string.IsNullOrWhiteSpace(typeCode) ? "Unknown" : typeCode
    };

    // Normalizes the one MOVEit type-code spelling difference used by REST wrappers.
    private static string NormalizeTypeCode(string? typeCode)
    {
        var value = (typeCode ?? string.Empty).Trim();
        if (value.Equals("SiLock", StringComparison.OrdinalIgnoreCase) || value.Equals("silock", StringComparison.OrdinalIgnoreCase)) return "siLock";
        foreach (var known in new[] { "FileSystem", "Share", "FTP", "SSHFTP", "POP3", "SMTP", "S3", "AzureBlob", "SharePoint", "GoogleStorage", "OneDrive", "AS1", "AS2", "AS3" })
            if (value.Equals(known, StringComparison.OrdinalIgnoreCase)) return known;
        return value;
    }

    // Formats MOVEit's type-specific Secure value into the same Secure/Insecure summary used by Web Admin.
    private static string FormatSecure(string typeCode, string secureValue)
    {
        var normalizedType = NormalizeTypeCode(typeCode);
        var value = (secureValue ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            // SFTP is intrinsically encrypted. Local/UNC filesystem Hosts have no encrypted
            // transport layer, so the native summary is effectively Insecure. Other Host types
            // are left blank if MOVEit does not expose enough information to determine the state.
            if (normalizedType == "SSHFTP") return "Secure";
            if (normalizedType is "FileSystem" or "Share") return "Insecure";
            return string.Empty;
        }

        if (value.Equals("0", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("no", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("off", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("insecure", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("never", StringComparison.OrdinalIgnoreCase))
            return "Insecure";

        // MOVEit's Secure property may contain a Boolean-like value or an encryption mode such
        // as tls-p / explicit / implicit. Any non-empty mode other than the insecure values above
        // means the Host is using an encrypted connection.
        return "Secure";
    }

    // Returns the native Hosts-page Destination value from the common and type-specific REST fields.
    private static string ReadHostDestination(JsonObject wrapper, JsonObject host)
        => FirstNonBlank(
            GetHostString(wrapper, "Destination"),
            GetHostString(host, "Destination"),
            GetHostString(host, "Host"),
            GetHostString(host, "UNC"),
            GetHostString(host, "StorageAccount"),
            GetHostString(host, "SiteUrl"),
            GetHostString(host, "SiteURL"),
            GetHostString(host, "S3AlternativeEndpoint"),
            GetHostString(host, "DefBucket"),
            GetHostString(host, "Bucket"),
            GetHostString(host, "Container"));

    // Reads one Host property case-insensitively. Some MOVEit REST Host models differ only in
    // property casing, so this keeps Host parsing resilient without changing the shared REST client.
    private static JsonNode? GetHostNode(JsonObject? obj, string propertyName)
    {
        if (obj is null || string.IsNullOrWhiteSpace(propertyName)) return null;
        if (obj[propertyName] is JsonNode exact) return exact;

        foreach (var pair in obj)
            if (pair.Key.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                return pair.Value;

        return null;
    }

    // Reads a Host scalar case-insensitively into display text.
    private static string GetHostString(JsonObject? obj, string propertyName)
        => ReadNodeString(GetHostNode(obj, propertyName));

    // Reads a Host integer case-insensitively.
    private static int? GetHostNullableInt(JsonObject? obj, string propertyName)
        => int.TryParse(GetHostString(obj, propertyName), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    // Reads a MOVEit string/list Group field into one display string.
    private static string ReadGroups(JsonNode? node)
    {
        if (node is JsonArray array)
            return string.Join(", ", array.Select(ReadNodeString).Where(value => !string.IsNullOrWhiteSpace(value)));
        return ReadNodeString(node).Trim();
    }

    // Reads a JSON scalar into text without throwing on null or non-string scalar values.
    private static string ReadNodeString(JsonNode? node)
    {
        if (node is null) return string.Empty;
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text)) return text ?? string.Empty;
            if (value.TryGetValue<int>(out var intValue)) return intValue.ToString(CultureInfo.InvariantCulture);
            if (value.TryGetValue<long>(out var longValue)) return longValue.ToString(CultureInfo.InvariantCulture);
            if (value.TryGetValue<bool>(out var boolValue)) return boolValue ? "true" : "false";
        }
        return node.ToJsonString();
    }

    // Reads a permissive MOVEit boolean representation.
    private static bool ReadBoolean(JsonNode? node)
    {
        var value = ReadNodeString(node).Trim();
        return value.Equals("1", StringComparison.OrdinalIgnoreCase) || value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    // Returns the first non-empty display value from common Host destination fields.
    private static string FirstNonBlank(params string[] values)
        => values.Select(value => (value ?? string.Empty).Trim()).FirstOrDefault(value => value.Length > 0) ?? string.Empty;

    // Resolves Host inventory through the shared MAUI cache contract.
    private Task<HostCacheEntry> RefreshHostsCoreAsync(string cacheKey, bool forceRefresh, CancellationToken cancellationToken)
        => MoveItServiceCache.ResolveAsync<HostCacheEntry>(
            _memoryCache, cacheKey, _hostBehaviorOptions.CacheRefreshSeconds.Value, forceRefresh,
            entry => entry.RefreshedUtc,
            async token => new HostCacheEntry(await LoadHostsFromMoveItAsync(token), DateTimeOffset.UtcNow),
            cancellationToken);

    // Purpose: Loads hosts from move it.
    private async Task<IReadOnlyList<AutomationHost>> LoadHostsFromMoveItAsync(CancellationToken cancellationToken)
    {
        var hostsTask = _crud.GetDefinitionsAsync(cancellationToken);
        var taskReferencesTask = _restClient.GetTaskHostReferenceDefinitionsAsync(cancellationToken);
        await Task.WhenAll(hostsTask, taskReferencesTask);
        var assignments = BuildTaskAssignments(await taskReferencesTask);
        return (await hostsTask)
            .Select(item => ParseHost(item, assignments))
            .Where(host => !string.IsNullOrWhiteSpace(host.Id))
            .OrderBy(host => host.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Purpose: Returns whether cache fresh.

    // Purpose: Invalidates host cache.
    private void InvalidateHostCache() => _memoryCache.Remove(GetCacheKey());

    // Purpose: Returns cache key.
    private string GetCacheKey() => MoveItServiceCache.UserScopedKey(CachePrefix, _sessionService);

    private sealed record HostCacheEntry(IReadOnlyList<AutomationHost> Hosts, DateTimeOffset RefreshedUtc);

    // Validates and normalizes a Host ID received from UI routes.
    private static string NormalizeHostId(string? hostId)
    {
        var value = (hostId ?? string.Empty).Trim();
        if (value.Length == 0) throw new ArgumentException("Host ID is required.", nameof(hostId));
        return value;
    }
}

