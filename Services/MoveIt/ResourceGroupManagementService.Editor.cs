// Author: Marc R. Davis (marcdavis@comcast.net)
// Resource Group editor operations share the same dataset checkout/commit boundary as Task bulk actions.
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.MoveIt;

public sealed partial class ResourceGroupManagementService
{
    private static readonly IReadOnlyDictionary<string, string> MemberTypes = new Dictionary<string, string>(StringComparer.Ordinal)
        { ["task"] = "Task", ["host"] = "Host", ["script"] = "Script", ["ssh"] = "SSH Key", ["ssl"] = "TLS Certificate", ["pgp"] = "PGP Key" };

    private async Task<IReadOnlyList<ResourceGroupEditorItem>> CatalogAsync(CancellationToken ct)
    {
        var tasks = _tasks.GetTasksAsync(ct); var hosts = _hosts.GetHostsAsync(ct);
        var scripts = _scripts.GetScriptsAsync(ct); var keys = _keys.GetSnapshotAsync(false, ct);
        await Task.WhenAll(tasks, hosts, scripts, keys);
        var items = new List<ResourceGroupEditorItem>();
        void Add(string type, string id, string name, string description)
        {
            if (id.Length > 0) items.Add(new(type, MemberTypes[type], id, First(name, id), description, Array.Empty<string>()));
        }
        foreach (var task in await tasks) Add("task", task.Id, task.Name, task.Description);
        foreach (var host in await hosts) Add("host", host.Id, host.Name, host.Description);
        foreach (var script in await scripts) Add("script", script.Id, script.Name, script.Description);
        foreach (var key in (await keys).Items)
            Add(KeyType(key.Kind), key.Id, key.Name, First(key.IssuedTo, key.Uid, key.Type));
        return items.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
    }

    private static string KeyType(KeyCertificateKind kind) => kind switch
        { KeyCertificateKind.Ssh => "ssh", KeyCertificateKind.Tls => "ssl", KeyCertificateKind.Pgp => "pgp", _ => throw new InvalidOperationException("Unsupported Key type.") };
    private static string Key(ResourceGroupMember member) => member.MemberType + "|" + member.Id;
    private static string Key(ResourceGroupEditorItem item) => item.MemberType + "|" + item.Id;

    private static ResourceGroupMember[] NormalizeMembers(IEnumerable<ResourceGroupMember>? members)
    {
        var result = new List<ResourceGroupMember>();
        foreach (var member in members ?? Array.Empty<ResourceGroupMember>())
        {
            var type = member?.MemberType ?? "";
            var id = member?.Id ?? "";
            if (!MemberTypes.ContainsKey(type) || string.IsNullOrWhiteSpace(id)
                || id.Length > 254 || id.Any(char.IsControl))
                throw new InvalidOperationException("Select a valid Resource Group member.");
            result.Add(new(type, id.Trim()));
        }
        return result.DistinctBy(Key, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<IReadOnlyList<ResourceGroupEditorItem>> ResolveMembersAsync(IEnumerable<ResourceGroupMember> members, CancellationToken cancellationToken = default)
        => await ResolveAsync(NormalizeMembers(members), await CatalogAsync(cancellationToken), cancellationToken);

    private async Task<IReadOnlyList<ResourceGroupEditorItem>> ResolveAsync(ResourceGroupMember[] selected,
        IReadOnlyList<ResourceGroupEditorItem> catalog, CancellationToken ct)
    {
        var available = catalog.ToDictionary(Key, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, ResourceGroupEditorItem>(StringComparer.OrdinalIgnoreCase);
        var required = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        ResourceGroupEditorItem Include(string type, string id, string? parent = null)
        {
            var key = type + "|" + id;
            if (!available.TryGetValue(key, out var item))
                throw new InvalidOperationException($"{MemberTypes[type]} ID '{id}' is not visible in the current MOVEit inventory. Reload the Group before changing membership.");
            result.TryAdd(key, item);
            if (!required.TryGetValue(key, out var parents)) required[key] = parents = new(StringComparer.OrdinalIgnoreCase);
            if (parent is not null) parents.Add(parent);
            return item;
        }
        foreach (var member in selected) Include(member.MemberType, member.Id);
        var roots = new List<(JsonObject Definition, string Parent)>();
        foreach (var member in selected.Where(member => member.MemberType == "task"))
        {
            var task = await _taskCrud.GetDefinitionAsync(member.Id, ct);
            var parent = "Task: " + available[Key(member)].Name;
            roots.Add((task, parent));
            foreach (var id in CollectReferenceValues(task, key => NormalizeKey(key).EndsWith("hostid", StringComparison.OrdinalIgnoreCase)))
                Include("host", id, parent);
        }
        // Include inherited Host credentials/keys and directory-parsing Scripts for every selected or derived Host.
        foreach (var host in result.Values.Where(item => item.MemberType == "host").ToArray())
        {
            if (host.Id == "0") continue;
            roots.Add((await _rest.GetHostAsync(host.Id, ct), "Host: " + host.Name));
        }
        var keyInventory = (await _keys.GetSnapshotAsync(false, ct)).Items;
        foreach (var (definition, parent) in roots)
        {
            foreach (var id in CollectReferenceValues(definition, key => NormalizeKey(key).EndsWith("scriptid", StringComparison.OrdinalIgnoreCase)))
                Include("script", id, parent);
            foreach (var item in keyInventory)
                if (ContainsKeyCertificateReference(definition, item)) Include(KeyType(item.Kind), item.Id, parent);
        }
        ValidateExplicitKeyReferences(roots.Select(root => (JsonNode?)root.Definition), keyInventory);
        return result.Values.Select(item => item with { RequiredBy = required[Key(item)].OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray() })
            .OrderBy(item => Array.IndexOf(MemberTypes.Keys.ToArray(), item.MemberType))
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<ResourceGroupEditorSetup> PrepareEditorAsync(string? groupName, CancellationToken cancellationToken = default)
    {
        var catalog = await CatalogAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(groupName))
            return new(new("", "", "", Array.Empty<ResourceGroupMember>(), Array.Empty<ResourceGroupEditorItem>()), catalog);
        var definitions = await _rest.GetResourceGroupDefinitionsAsync(cancellationToken);
        var group = definitions.SingleOrDefault(item => Read(item, "Name").Equals(groupName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("This Resource Group no longer exists or is no longer visible. Refresh the grid.");
        var members = ReadMembers(group);
        var items = await ResolveAsync(members, catalog, cancellationToken);
        // Native datasets may store just Tasks or may include expanded dependencies. Normalize both to explicit roots.
        var roots = ExplicitRoots(members, items);
        return new(new(Read(group, "Name"), Read(group, "Notes"), Version(group), roots, items), catalog);
    }

    // Deletion needs only the live Group definition, so unavailable member inventories cannot block it.
    public async Task<ResourceGroupDeleteSetup> PrepareDeleteAsync(string groupName, CancellationToken cancellationToken = default)
    {
        var name = NormalizeGroupName(groupName);
        var definitions = await _rest.GetResourceGroupDefinitionsAsync(cancellationToken);
        var group = definitions.SingleOrDefault(item => Read(item, "Name").Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ObjectEditConflictException("Resource Group");
        return new(Read(group, "Name"), Read(group, "Notes"), Version(group));
    }

    public async Task DeleteAsync(ResourceGroupDeleteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = NormalizeGroupName(request.Name);
        if (string.IsNullOrWhiteSpace(request.EditVersion)) throw new ObjectEditConflictException("Resource Group");
        await MutateAsync(checkout =>
        {
            var groups = RequireGroupArray(checkout);
            var target = groups.OfType<JsonObject>().SingleOrDefault(group => Read(group, "Name").Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? throw new ObjectEditConflictException("Resource Group");
            if (request.EditVersion != Version(target)) throw new ObjectEditConflictException("Resource Group");
            // Native deletion removes one Group from the checked-out section, retaining all unrelated data.
            groups.Remove(target);
            return true;
        }, cancellationToken);
    }

    private static ResourceGroupMember[] ExplicitRoots(ResourceGroupMember[] members, IReadOnlyList<ResourceGroupEditorItem> items)
    {
        var required = items.Where(item => item.RequiredBy.Count > 0).Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return members.Where(member => !required.Contains(Key(member))).ToArray();
    }

    private static ResourceGroupMember[] ReadMembers(JsonObject group)
        => NativeMembers(group)
            .Where(member => MemberTypes.ContainsKey(Read(member, "t")))
            .Select(member => new ResourceGroupMember(Read(member, "t"), Read(member, "value"))).DistinctBy(Key, StringComparer.OrdinalIgnoreCase).ToArray();

    private static JsonObject[] NativeMembers(JsonObject group)
    {
        var node = GetNode(group, "Members");
        if (node is null) return [];
        if (node is not JsonArray array || array.Any(member => member is not JsonObject))
            throw new MoveItApiException("MOVEit returned invalid Resource Group members. Reload before editing.");
        return array.Cast<JsonObject>().ToArray();
    }

    // v1 reads and v0 checkout may order members differently or include different read-only attributes.
    // Compare the same editable configuration semantics, including unknown member kinds that must be preserved.
    internal static string Version(JsonObject group)
    {
        var members = new JsonArray();
        foreach (var member in NativeMembers(group)
            .OrderBy(MemberKey, StringComparer.OrdinalIgnoreCase))
            members.Add(new JsonObject { ["t"] = Read(member, "t"), ["value"] = Read(member, "value") });
        return ObjectEditVersion.Create("ResourceGroup", Read(group, "Name"), new JsonObject
            { ["Name"] = Read(group, "Name"), ["Notes"] = Read(group, "Notes"), ["Members"] = members });
    }

    public async Task<ResourceGroupEditorState> SaveEditorAsync(ResourceGroupEditorSaveRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = NormalizeGroupName(request.Name); var description = NormalizeDescription(request.Description);
        var selected = NormalizeMembers(request.Members);
        var items = await ResolveMembersAsync(selected, cancellationToken);
        selected = ExplicitRoots(selected, items);
        var original = (request.OriginalName ?? "").Trim();
        return await MutateAsync(checkout =>
        {
            var groups = RequireGroupArray(checkout);
            var target = original.Length == 0 ? null : groups.OfType<JsonObject>()
                .SingleOrDefault(group => Read(group, "Name").Equals(original, StringComparison.OrdinalIgnoreCase))
                ?? (original.Length == 0 ? null : throw new ObjectEditConflictException("Resource Group"));
            if (target is not null && (string.IsNullOrWhiteSpace(request.EditVersion) || request.EditVersion != Version(target)))
                throw new ObjectEditConflictException("Resource Group");
            if (groups.OfType<JsonObject>().Any(group => !ReferenceEquals(group, target) && Read(group, "Name").Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"A Resource Group named '{name}' already exists. Enter a unique Group Name.");
            if (target is null) { target = new JsonObject(); groups.Add(target); }
            var existing = NativeMembers(target).ToDictionary(MemberKey, StringComparer.OrdinalIgnoreCase);
            var members = new JsonArray();
            // Keep unsupported native member kinds intact instead of silently dropping them from this six-type editor.
            foreach (var member in existing.Values.Where(member => !MemberTypes.ContainsKey(Read(member, "t")))) members.Add(member.DeepClone());
            foreach (var item in items)
                members.Add(existing.TryGetValue(Key(item), out var member) ? member.DeepClone() : new JsonObject { ["t"] = item.MemberType, ["value"] = item.Id });
            target["Name"] = name; target["Notes"] = description; target["Members"] = members;
            return new ResourceGroupEditorState(name, description, Version(target), selected, items);
        }, cancellationToken);
    }

    internal static string[] NormalizeGroupNames(IEnumerable<string> names) => names.Select(NormalizeGroupName)
        .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();

    public async Task<IReadOnlyList<string>> EnsureGroupsAsync(IEnumerable<string> groupNames, CancellationToken cancellationToken = default)
    {
        var requested = NormalizeGroupNames(groupNames);
        if (requested.Length == 0) return [];
        var visible = await _rest.GetResourceGroupDefinitionsAsync(cancellationToken);
        if (requested.All(name => visible.Any(group => Read(group, "Name").Equals(name, StringComparison.OrdinalIgnoreCase)))) return [];
        // Re-check the live dataset under checkout so concurrent same-name creation cannot duplicate a Group.
        return await MutateAsync<IReadOnlyList<string>>(checkout =>
        {
            var groups = RequireGroupArray(checkout); var created = new List<string>();
            foreach (var name in requested)
            {
                if (groups.OfType<JsonObject>().Any(group => Read(group, "Name").Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
                groups.Add(new JsonObject { ["Name"] = name, ["Notes"] = "", ["Members"] = new JsonArray() }); created.Add(name);
            }
            return created;
        }, cancellationToken);
    }

    public async Task SetObjectGroupsAsync(ResourceGroupMember member, IReadOnlyList<string> groupNames, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeMembers([member]).Single();
        var desired = groupNames.Select(name => name.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visible = await _rest.GetResourceGroupDefinitionsAsync(cancellationToken);
        static bool Contains(JsonObject group, ResourceGroupMember item) => NativeMembers(group).Any(value =>
            Read(value, "t").Equals(item.MemberType, StringComparison.OrdinalIgnoreCase) && Read(value, "value").Equals(item.Id, StringComparison.OrdinalIgnoreCase));
        if (desired.Any(name => !visible.Any(group => Read(group, "Name").Equals(name, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("A destination Resource Group no longer exists. No group was created.");
        if (visible.All(group => Contains(group, normalized) == desired.Contains(Read(group, "Name")))) return;
        var catalog = await CatalogAsync(cancellationToken);
        if (!catalog.Any(item => Key(item).Equals(Key(normalized), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The copied object no longer exists. Group placement was not changed.");
        await MutateAsync(checkout =>
        {
            var groups = RequireGroupArray(checkout).OfType<JsonObject>().ToArray();
            if (desired.Any(name => !groups.Any(group => Read(group, "Name").Equals(name, StringComparison.OrdinalIgnoreCase))))
                throw new InvalidOperationException("A destination Resource Group changed before placement. Review its assignments.");
            foreach (var group in groups)
            {
                var members = NativeMembers(group); var existing = members.FirstOrDefault(value =>
                    Read(value, "t").Equals(normalized.MemberType, StringComparison.OrdinalIgnoreCase) && Read(value, "value").Equals(normalized.Id, StringComparison.OrdinalIgnoreCase));
                var keep = desired.Contains(Read(group, "Name"));
                if (keep == (existing is not null)) continue;
                var updated = new JsonArray(members.Where(value => !ReferenceEquals(value, existing)).Select(value => value.DeepClone()).ToArray());
                if (keep) updated.Add(new JsonObject { ["t"] = normalized.MemberType, ["value"] = normalized.Id });
                group["Members"] = updated;
            }
            return true;
        }, cancellationToken);
    }

    // One authoritative full-dataset write boundary, also used by the existing Task bulk workflows.
    // Never restore an old snapshot after PUT was attempted: the server may already have committed it.
    private async Task<T> MutateAsync<T>(Func<JsonObject, T> patch, CancellationToken ct, bool force = false)
    {
        await UpdateGate.WaitAsync(ct);
        try
        {
            var checkout = force ? await _rest.ForceCheckoutObjectV0Async("Resource Groups", "-1", ct) : await _rest.CheckoutResourceGroupsV0Async(ct);
            var untouched = checkout.DeepClone().AsObject(); var attempted = false;
            try
            {
                var result = patch(checkout);
                attempted = true;
                try { await CommitAsync(checkout, ct); }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException
                    || ex is MoveItApiException api && ex is not MoveItAuthenticationException && (api.StatusCode is null
                        || api.StatusCode == HttpStatusCode.RequestTimeout || api.StatusCode is { } code && (int)code >= 500))
                {
                    _logger.LogError(ex, "The Resource Group PUT result could not be confirmed. No old dataset was written back.");
                    throw new ResourceGroupSaveUnconfirmedException(ex);
                }
                return result;
            }
            finally { if (!attempted) await ReleaseCheckoutWithoutChangesAsync(untouched); }
        }
        finally { UpdateGate.Release(); }
    }
}

public sealed class ResourceGroupSaveUnconfirmedException(Exception inner)
    : InvalidOperationException("MOVEit did not confirm the Resource Group save result. Close this dialog and inspect the refreshed Groups before retrying to avoid creating a duplicate or overwriting changes.", inner);
