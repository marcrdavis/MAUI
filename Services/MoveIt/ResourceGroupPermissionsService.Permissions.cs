// Author: Marc R. Davis (marcdavis@comcast.net)
// Inline permission edits use one v1 PUT per changed ACL; all composition/concurrency checks stay in this layer.
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.MoveIt;

public sealed partial class ResourceGroupPermissionsService
{
    public async Task<ResourceGroupPermissionsSaveResult> SavePermissionsAsync(ResourceGroupPermissionsSaveRequest request, CancellationToken cancellationToken = default)
    {
        RequireAdministrator(); ArgumentNullException.ThrowIfNull(request);
        var group = Name(request.ResourceGroupName);
        var updates = request.Updates ?? throw new ArgumentException("Permission changes are required.");
        if (updates.Count == 0 || updates.Any(update => update is null)) throw new ArgumentException("Permission changes are required.");
        foreach (var update in updates)
        {
            if (!ValidAclId(update.AclId) || string.IsNullOrWhiteSpace(update.UserGroupName) || string.IsNullOrWhiteSpace(update.EditVersion))
                throw new ArgumentException("Refresh the User Group assignments before editing permissions.");
            if (update.Permissions is null || update.Permissions.Count != PermissionKeys.Count || PermissionKeys.Any(key => !update.Permissions.ContainsKey(key)))
                throw new ArgumentException("Every supported permission must be specified exactly once.");
            if (update.Permissions["TaskAddEditDelete"] && !update.Permissions["TaskEditExisting"])
                throw new ArgumentException("Tasks Add/Edit/Delete requires Edit Existing.");
        }
        if (updates.Select(update => update.AclId).Distinct(StringComparer.Ordinal).Count() != updates.Count
            || updates.Select(update => update.UserGroupName.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != updates.Count)
            throw new ArgumentException("Each User Group assignment may be updated only once.");
        var cacheKey = MoveItServiceCache.UserScopedKey("resource-group-permissions:", sessions);
        await AclUpdateGate.WaitAsync(cancellationToken);
        try
        {
            RequireAdministrator(); group = await ExistingGroupAsync(group, cancellationToken);
            var inventory = await rest.GetResourceGroupAclDefinitionsAsync(cancellationToken);
            // Reject stale/forged batches before writing anything. Recheck later ACLs immediately before their PUT.
            foreach (var update in updates) CurrentAcl(inventory, group, update);
            var results = new List<ResourceGroupPermissionSaveResult>(); var uncertain = false; var conflict = false; var first = true;
            foreach (var update in updates)
            {
                if (uncertain || conflict)
                { results.Add(new(update.AclId, update.UserGroupName, "notAttempted", "Not attempted; cancel editing and reload the current permissions.")); continue; }
                JsonObject current;
                try
                {
                    RequireAdministrator(); cancellationToken.ThrowIfCancellationRequested();
                    if (!first) inventory = await rest.GetResourceGroupAclDefinitionsAsync(cancellationToken);
                    current = CurrentAcl(inventory, group, update); first = false;
                }
                catch (ObjectEditConflictException)
                { conflict = true; results.Add(new(update.AclId, update.UserGroupName, "conflict", "This assignment changed. Cancel editing and reload its current permissions.")); continue; }
                catch (MoveItAuthenticationException) { throw; }
                catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or JsonException or HttpRequestException or OperationCanceledException)
                { uncertain = true; results.Add(new(update.AclId, update.UserGroupName, "notAttempted", "Current permissions could not be checked. Cancel editing and reload before retrying.")); continue; }
                // Retain native fields and any additional permission keys; only the eight editable flags are replaced.
                var payload = current.DeepClone().AsObject(); var permissions = payload["Permissions"]!.AsObject();
                foreach (var key in PermissionKeys) permissions[key] = update.Permissions[key] ? 1 : 0;
                if (HasPermissions(current, update.Permissions))
                { results.Add(new(update.AclId, update.UserGroupName, "unchanged", "No permission changes.")); continue; }
                try
                {
                    await rest.UpdateResourceGroupAclAsync(update.AclId, payload, cancellationToken);
                    results.Add(new(update.AclId, update.UserGroupName, "saved", "Permissions saved."));
                }
                catch (MoveItAuthenticationException) { throw; }
                catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or JsonException or HttpRequestException or OperationCanceledException)
                {
                    if (Rejected(ex))
                    {
                        conflict = ex is MoveItApiException { StatusCode: HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed };
                        results.Add(new(update.AclId, update.UserGroupName, conflict ? "conflict" : "failed", ex.Message)); continue;
                    }
                    // A lost acknowledgement must never cause an automatic repeat PUT.
                    var observed = await ReconcileAclsAsync();
                    var matches = observed?.Where(acl => acl["ACLID"]?.ToString() == update.AclId && IsPair(acl, group, update.UserGroupName.Trim())).ToArray();
                    if (matches?.Length == 1 && HasPermissions(matches[0], update.Permissions))
                        results.Add(new(update.AclId, update.UserGroupName, "saved", "Saved permissions confirmed."));
                    else
                    { uncertain = true; results.Add(new(update.AclId, update.UserGroupName, "unconfirmed", "The save result could not be confirmed. Cancel editing and inspect the refreshed permissions before retrying.")); }
                }
            }
            return new(group, results, uncertain, conflict);
        }
        finally { cache.Remove(cacheKey); AclUpdateGate.Release(); }
    }
    private static JsonObject CurrentAcl(IReadOnlyList<JsonObject> inventory, string group, ResourceGroupPermissionUpdate update)
    {
        var matches = inventory.Where(acl => acl["ACLID"]?.ToString() == update.AclId).ToArray();
        if (matches.Length != 1 || !IsPair(matches[0], group, update.UserGroupName.Trim()) || AclVersion(matches[0]) != update.EditVersion
            || matches[0]["Permissions"] is not JsonObject)
            throw new ObjectEditConflictException("User Group permissions");
        return matches[0];
    }
    private static bool HasPermissions(JsonObject acl, IReadOnlyDictionary<string, bool> expected)
        => acl["Permissions"] is JsonObject permissions && PermissionKeys.All(key =>
            permissions[key]?.ToString() is { } value && (expected[key] ? value is "1" or "true" : value is "0" or "false"));
}
