// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.MoveIt;

public interface IResourceGroupPermissionsService
{
    Task<ResourceGroupPermissionsSnapshot> GetSnapshotAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
    Task<ResourceGroupUserGroupSetup> PrepareUserGroupsAsync(string resourceGroupName, string type, CancellationToken cancellationToken = default);
    Task<ResourceGroupUserGroupMutation> AddUserGroupsAsync(ResourceGroupUserGroupsRequest request, CancellationToken cancellationToken = default);
    Task<ResourceGroupUserGroupMutation> RemoveUserGroupAsync(ResourceGroupRemoveUserGroupRequest request, CancellationToken cancellationToken = default);
    Task<ResourceGroupPermissionsSaveResult> SavePermissionsAsync(ResourceGroupPermissionsSaveRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Shared Resource Group ACL reads and mutations. Assignments are not effective user permissions.</summary>
public sealed partial class ResourceGroupPermissionsService(IMoveItRestClient rest, IMoveItSessionService sessions, IMemoryCache cache)
    : IResourceGroupPermissionsService
{
    public static IReadOnlyList<string> PermissionKeys { get; } = Array.AsReadOnly(new[]
    {
        "TaskRun", "TaskAddEditDelete", "TaskEditExisting", "HostAddEditDelete", "ScriptAddEditDelete",
        "SSLAddEditDelete", "SSHAddEditDelete", "PGPAddEditDelete"
    });

    public Task<ResourceGroupPermissionsSnapshot> GetSnapshotAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        return MoveItServiceCache.ResolveAsync<ResourceGroupPermissionsSnapshot>(cache, MoveItServiceCache.UserScopedKey("resource-group-permissions:", sessions),
            60, forceRefresh, entry => entry.RefreshedUtc, LoadAsync, cancellationToken);
    }

    private async Task<ResourceGroupPermissionsSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        var groups = rest.GetResourceGroupDefinitionsAsync(cancellationToken);
        var acls = rest.GetResourceGroupAclDefinitionsAsync(cancellationToken);
        await Task.WhenAll(groups, acls);
        // Publish only a complete, validated pair; failed refreshes leave the previous cache intact.
        return BuildSnapshot(await groups, await acls, DateTimeOffset.UtcNow);
    }

    internal static ResourceGroupPermissionsSnapshot BuildSnapshot(IReadOnlyList<JsonObject> definitions,
        IReadOnlyList<JsonObject> acls, DateTimeOffset refreshedUtc)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in definitions)
        {
            var name = RequiredName(definition, "Name");
            if (!names.TryAdd(name, name)) throw InvalidInventory();
        }
        var grants = names.Keys.ToDictionary(name => name, _ => new List<ResourceGroupGrant>(), StringComparer.OrdinalIgnoreCase);
        var aclIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var acl in acls)
        {
            var group = RequiredName(acl, "ResourceGroupName");
            // The Resource Groups endpoint is the authoritative visibility boundary.
            if (!grants.TryGetValue(group, out var assigned)) continue;
            var aclId = acl["ACLID"]?.ToString();
            if (!ValidAclId(aclId) || !aclIds.Add(aclId!)) throw InvalidInventory();
            var name = RequiredName(acl, "UserGroupName");
            if (assigned.Any(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))) throw InvalidInventory();
            if (acl["Permissions"] is not JsonObject permissions) throw InvalidInventory();
            var values = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var key in PermissionKeys)
            {
                var value = permissions[key]?.ToString();
                values[key] = value switch
                {
                    "1" or "true" => true,
                    "0" or "false" => false,
                    _ => throw InvalidInventory()
                };
            }
            assigned.Add(new ResourceGroupGrant(aclId!, name, values, AclVersion(acl)));
        }
        var rows = names.Values.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).Select(name =>
        {
            var children = grants[name].OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            var summary = PermissionKeys.ToDictionary(key => key, key =>
            {
                var count = children.Count(item => item.Permissions[key]);
                return new ResourceGroupPermissionSummary(count == 0 ? "None" : count == children.Length ? "All" : "Some", count, children.Length);
            });
            return new ResourceGroupPermissionRow(name, children, summary);
        }).ToArray();
        return new ResourceGroupPermissionsSnapshot(rows, refreshedUtc);
    }

    private static string RequiredName(JsonObject value, string property)
    {
        var name = value[property] is JsonValue node && node.TryGetValue<string>(out var text) ? text.Trim() : string.Empty;
        return name.Length > 0 ? name : throw InvalidInventory();
    }

    private static InvalidOperationException InvalidInventory()
        => new("MOVEit returned incomplete or ambiguous Resource Group permissions. Refresh to try again; no permission changes were made.");
}
