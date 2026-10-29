// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Reusable Resource Group inventory and whole-dataset mutation workflow.

using System.Text.Json.Nodes;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt.Tasks;

namespace MoveItAutomationUI.Services.MoveIt;

public interface IResourceGroupManagementService
{
    Task RecoverCheckoutAsync(CancellationToken cancellationToken = default);
    Task<ResourceGroupDeleteSetup> PrepareDeleteAsync(string groupName, CancellationToken cancellationToken = default);
    Task DeleteAsync(ResourceGroupDeleteRequest request, CancellationToken cancellationToken = default);
    Task<ResourceGroupEditorSetup> PrepareEditorAsync(string? groupName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ResourceGroupEditorItem>> ResolveMembersAsync(IEnumerable<ResourceGroupMember> members, CancellationToken cancellationToken = default);
    Task<ResourceGroupEditorState> SaveEditorAsync(ResourceGroupEditorSaveRequest request, CancellationToken cancellationToken = default);
    Task<ResourceGroupSetupResult> PrepareForTasksAsync(IEnumerable<string> taskIds, CancellationToken cancellationToken = default);
    Task<ResourceGroupMutationResult> CreateForTasksAsync(IEnumerable<string> taskIds, string name, string description, CancellationToken cancellationToken = default);
    Task<ResourceGroupMutationResult> AddTasksToGroupAsync(IEnumerable<string> taskIds, string groupName, CancellationToken cancellationToken = default);
    // Idempotent name-only creation for portability; existing Groups and ACL assignments remain untouched.
    Task<IReadOnlyList<string>> EnsureGroupsAsync(IEnumerable<string> groupNames, CancellationToken cancellationToken = default);
    // Explicit placement for portability: unlike the interactive member editor, do not add referenced/reused dependencies.
    Task SetObjectGroupsAsync(ResourceGroupMember member, IReadOnlyList<string> groupNames, CancellationToken cancellationToken = default);
}

public sealed partial class ResourceGroupManagementService : IResourceGroupManagementService
{
    // Resource Groups releases checkout by PUT of the complete dataset; no DELETE pattern is known.
    // This administrator-confirmed recovery keeps all current fields/members unchanged.
    public async Task RecoverCheckoutAsync(CancellationToken cancellationToken = default)
        => _ = await MutateAsync(checkout => { RequireGroupArray(checkout); return true; }, cancellationToken, force: true);
    // Serialize MAUI Resource Group dataset and ACL mutations through one shared boundary.
    internal static readonly SemaphoreSlim UpdateGate = new(1, 1);
    private readonly IMoveItRestClient _rest;
    private readonly IResourceGroupLookupService _resourceGroupLookup;
    private readonly ITaskCrudCoordinator _taskCrud;
    private readonly IAutomationHostService _hosts;
    private readonly IAutomationScriptService _scripts;
    private readonly IAutomationKeyCertificateService _keys;
    private readonly ILogger<ResourceGroupManagementService> _logger;
    private readonly IAutomationTaskService _tasks;

    public ResourceGroupManagementService(
        IMoveItRestClient rest,
        IResourceGroupLookupService resourceGroupLookup,
        ITaskCrudCoordinator taskCrud,
        IAutomationHostService hosts,
        IAutomationScriptService scripts,
        IAutomationKeyCertificateService keys,
        ILogger<ResourceGroupManagementService> logger,
        IAutomationTaskService tasks)
    {
        _rest = rest;
        _resourceGroupLookup = resourceGroupLookup;
        _taskCrud = taskCrud;
        _hosts = hosts;
        _scripts = scripts;
        _keys = keys;
        _logger = logger;
        _tasks = tasks;
    }

    public async Task<ResourceGroupSetupResult> PrepareForTasksAsync(IEnumerable<string> taskIds, CancellationToken cancellationToken = default)
    {
        var ids = NormalizeTaskIds(taskIds);
        var groupsTask = _resourceGroupLookup.GetVisibleDefinitionsAsync(cancellationToken);
        var inventoryTask = BuildInventoryAsync(ids, cancellationToken);
        await Task.WhenAll(groupsTask, inventoryTask);

        var groups = (await groupsTask)
            .Select(group => new ResourceGroupOption(_resourceGroupLookup.ReadName(group), Read(group, "Notes")))
            .Where(group => group.Name.Length > 0)
            .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ResourceGroupSetupResult(groups, await inventoryTask);
    }

    public async Task<ResourceGroupMutationResult> CreateForTasksAsync(
        IEnumerable<string> taskIds, string name, string description, CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeGroupName(name);
        var normalizedDescription = NormalizeDescription(description);
        var inventory = await BuildInventoryAsync(NormalizeTaskIds(taskIds), cancellationToken);
        return await MutateAsync(checkout =>
        {
            var groups = RequireGroupArray(checkout);
            if (groups.OfType<JsonObject>().Any(group => _resourceGroupLookup.ReadName(group).Equals(normalizedName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"A Resource Group named '{normalizedName}' already exists. Enter a unique Name.");
            var members = new JsonArray();
            foreach (var item in inventory) members.Add(new JsonObject { ["t"] = item.MemberType, ["value"] = item.Id });
            groups.Add(new JsonObject { ["Name"] = normalizedName, ["Notes"] = normalizedDescription, ["Members"] = members });
            return new ResourceGroupMutationResult(normalizedName, inventory.Count, 0, inventory.Count);
        }, cancellationToken);
    }

    public async Task<ResourceGroupMutationResult> AddTasksToGroupAsync(
        IEnumerable<string> taskIds, string groupName, CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeGroupName(groupName);
        var inventory = await BuildInventoryAsync(NormalizeTaskIds(taskIds), cancellationToken);
        return await MutateAsync(checkout =>
        {
            var groups = RequireGroupArray(checkout);
            var target = groups.OfType<JsonObject>().FirstOrDefault(group => _resourceGroupLookup.ReadName(group).Equals(normalizedName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Resource Group '{normalizedName}' no longer exists or is no longer visible.");
            var members = GetNode(target, "Members") as JsonArray;
            if (members is null) { members = new JsonArray(); target["Members"] = members; }
            var existing = members.OfType<JsonObject>().Select(MemberKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var added = 0;
            foreach (var item in inventory)
                if (existing.Add($"{item.MemberType}|{item.Id}"))
                { members.Add(new JsonObject { ["t"] = item.MemberType, ["value"] = item.Id }); added++; }
            return new ResourceGroupMutationResult(normalizedName, added, inventory.Count - added, members.Count);
        }, cancellationToken);
    }

    private async Task<IReadOnlyList<ResourceGroupInventoryItem>> BuildInventoryAsync(
        IReadOnlyList<string> taskIds, CancellationToken cancellationToken)
        => (await ResolveMembersAsync(taskIds.Select(id => new ResourceGroupMember("task", id)), cancellationToken))
            .Select(item => new ResourceGroupInventoryItem(item.MemberType, item.Type, item.Id, item.Name)).ToArray();

    // The native v0 Resource Group contract does not expose a separate release call in the captured workflow.
    // If MAUI fails after checkout but before mutation commit, PUT the untouched snapshot back with a cleanup
    // token so the server-side edit section is not left checked out.
    private async Task ReleaseCheckoutWithoutChangesAsync(JsonObject untouchedCheckout)
    {
        try
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await CommitAsync(untouchedCheckout, cleanup.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MAUI could not release a Resource Group checkout after an aborted update.");
        }
    }

    private async Task CommitAsync(JsonObject checkout, CancellationToken cancellationToken)
    {
        var response = await _rest.UpdateResourceGroupsV0Async(checkout, cancellationToken);
        var status = Read(response, "status");
        var errorCode = Read(response, "errorcode");
        // Captured v0 saves acknowledge both status=success and errorcode=0. An empty/partial body is not proof of a save.
        if (!status.Equals("success", StringComparison.OrdinalIgnoreCase))
            throw new MoveItApiException($"MOVEit Resource Group update failed with status '{status}'{(errorCode.Length > 0 ? $" (error code {errorCode})" : string.Empty)}.");
        if (!int.TryParse(errorCode, out var code) || code != 0)
            throw new MoveItApiException($"MOVEit Resource Group update returned error code '{errorCode}'.");
    }

    private static JsonArray RequireGroupArray(JsonObject checkout)
    {
        var groups = GetNode(checkout, "Groups") as JsonObject
            ?? throw new MoveItApiException("MOVEit Resource Group checkout did not return a Groups object.");
        var node = GetNode(groups, "Group");
        if (node is JsonArray existing)
        {
            if (existing.Any(group => group is not JsonObject))
                throw new MoveItApiException("MOVEit Resource Group checkout returned an invalid Group entry. Reload before editing.");
            return existing;
        }
        if (node is not null) throw new MoveItApiException("MOVEit Resource Group checkout returned an invalid Group collection. Reload before editing.");

        // Normalize an omitted/null collection to the exact whole-dataset shape used by the native UI.
        var groupArray = new JsonArray();
        groups["Group"] = groupArray;
        return groupArray;
    }

    private static void ValidateExplicitKeyReferences(IEnumerable<JsonNode?> roots, IReadOnlyList<AutomationKeyCertificate> inventory)
    {
        var known = inventory.SelectMany(KeyCertificateCandidates)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizeReferenceValue)
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            foreach (var reference in CollectExplicitKeyCertificateReferences(root))
            {
                if (known.Contains(NormalizeReferenceValue(reference.Value))) continue;
                throw new InvalidOperationException($"A selected Task dependency references '{reference.Value}' in '{reference.Property}', but the matching SSH key or TLS certificate is not visible in the current MOVEit inventory.");
            }
        }
    }

    private static bool ContainsKeyCertificateReference(JsonNode? node, AutomationKeyCertificate item, bool referenceContext = false)
    {
        if (node is JsonObject obj)
        {
            var parameterName = Read(obj, "Name");
            if ((parameterName.Contains("pgp", StringComparison.OrdinalIgnoreCase)
                || parameterName.Contains("key", StringComparison.OrdinalIgnoreCase)
                || parameterName.Contains("cert", StringComparison.OrdinalIgnoreCase))
                && ReferenceMatches(GetNode(obj, "value"), item)) return true;
            foreach (var pair in obj)
            {
                var key = NormalizeKey(pair.Key);
                var nextContext = referenceContext
                    || key.Contains("key", StringComparison.OrdinalIgnoreCase)
                    || key.Contains("cert", StringComparison.OrdinalIgnoreCase)
                    || key.Contains("pgp", StringComparison.OrdinalIgnoreCase)
                    || key.Contains("sha1", StringComparison.OrdinalIgnoreCase)
                    || key.Contains("thumbprint", StringComparison.OrdinalIgnoreCase)
                    || key.Contains("fingerprint", StringComparison.OrdinalIgnoreCase);
                if (nextContext && ReferenceMatches(pair.Value, item)) return true;
                if (ContainsKeyCertificateReference(pair.Value, item, nextContext)) return true;
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array) if (ContainsKeyCertificateReference(child, item, referenceContext)) return true;
        }
        else if (referenceContext && ReferenceMatches(node, item)) return true;
        return false;
    }

    private static bool ReferenceMatches(JsonNode? node, AutomationKeyCertificate item)
    {
        var value = NormalizeReferenceValue(NodeString(node));
        if (value.Length == 0) return false;
        return KeyCertificateCandidates(item)
            .Select(NormalizeReferenceValue)
            .Any(candidate => candidate.Length > 0 && value.Equals(candidate, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record ExplicitKeyCertificateReference(string Property, string Value);

    private static IReadOnlyList<ExplicitKeyCertificateReference> CollectExplicitKeyCertificateReferences(JsonNode? node)
    {
        var result = new List<ExplicitKeyCertificateReference>();
        Walk(node);
        return result
            .Where(item => item.Value.Length > 0)
            .DistinctBy(item => $"{item.Property}|{item.Value}", StringComparer.OrdinalIgnoreCase)
            .ToArray();

        void Walk(JsonNode? current)
        {
            if (current is JsonObject obj)
            {
                foreach (var pair in obj)
                {
                    var key = NormalizeKey(pair.Key);
                    if (key.Equals("clientkeyid", StringComparison.OrdinalIgnoreCase)
                        || key.Equals("defclientkeyid", StringComparison.OrdinalIgnoreCase)
                        || key.Equals("clientcertsha1", StringComparison.OrdinalIgnoreCase)
                        || key.Equals("defclientcertsha1", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = NodeString(pair.Value).Trim();
                        if (value.Length > 0 && value != "0") result.Add(new ExplicitKeyCertificateReference(pair.Key, value));
                    }
                    Walk(pair.Value);
                }
            }
            else if (current is JsonArray array)
            {
                foreach (var child in array) Walk(child);
            }
        }
    }

    private static IEnumerable<string> KeyCertificateCandidates(AutomationKeyCertificate item)
    {
        yield return item.Id;
        yield return item.Sha1Thumbprint;
        yield return item.Fingerprint;
        yield return item.Name;
        yield return item.Uid;
        yield return item.IssuedTo;
    }

    private static HashSet<string> CollectReferenceValues(JsonNode? node, Func<string, bool> keyMatch)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Walk(node);
        return values;

        void Walk(JsonNode? current)
        {
            if (current is JsonObject obj)
            {
                foreach (var pair in obj)
                {
                    if (keyMatch(pair.Key))
                    {
                        var value = NodeString(pair.Value).Trim();
                        if (value.Length > 0) values.Add(value);
                    }
                    Walk(pair.Value);
                }
            }
            else if (current is JsonArray array)
            {
                foreach (var child in array) Walk(child);
            }
        }
    }

    private static IReadOnlyList<string> NormalizeTaskIds(IEnumerable<string>? taskIds)
    {
        var ids = (taskIds ?? Array.Empty<string>())
            .Select(value => (value ?? string.Empty).Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (ids.Length == 0) throw new ArgumentException("Select at least one Task.", nameof(taskIds));
        return ids;
    }

    private static string NormalizeGroupName(string? name)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length == 0) throw new InvalidOperationException("Resource Group Name is required.");
        if (value.Length > 254) throw new InvalidOperationException("Resource Group Name cannot exceed 254 characters.");
        if (value.Any(char.IsControl)) throw new InvalidOperationException("Resource Group Name contains unsupported control characters.");
        return value;
    }

    private static string NormalizeDescription(string? description)
    {
        var value = (description ?? string.Empty).Trim();
        if (value.Length > 2000) throw new InvalidOperationException("Resource Group Description cannot exceed 2000 characters.");
        return value;
    }

    private static string MemberKey(JsonObject member)
    {
        var type = Read(member, "t");
        var value = Read(member, "value");
        return type.Length == 0 || value.Length == 0 ? string.Empty : $"{type}|{value}";
    }

    private static string NormalizeReferenceValue(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0) return string.Empty;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return text;
        if (text.All(character => char.IsLetterOrDigit(character) || character is ':' or ' ' or '-'))
            return text.Replace(":", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        return text;
    }

    private static string NormalizeKey(string? key)
        => (key ?? string.Empty).Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).Trim();

    private static string Read(JsonObject raw, string name) => NodeString(GetNode(raw, name)).Trim();

    private static JsonNode? GetNode(JsonObject raw, string name)
    {
        if (raw.TryGetPropertyValue(name, out var exact)) return exact;
        foreach (var pair in raw)
            if (pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)) return pair.Value;
        return null;
    }

    private static string NodeString(JsonNode? node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text)) return text ?? string.Empty;
            if (value.TryGetValue<int>(out var integer)) return integer.ToString();
            if (value.TryGetValue<long>(out var longValue)) return longValue.ToString();
            if (value.TryGetValue<bool>(out var boolean)) return boolean ? "true" : "false";
        }
        return node?.ToJsonString() ?? string.Empty;
    }

    private static string First(params string[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}

