// MOVEit v1 ACL operations belong to the shared permissions layer. No whole-dataset rewrites or permission grants.
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.MoveIt;

public sealed partial class ResourceGroupPermissionsService
{
    private static SemaphoreSlim AclUpdateGate => ResourceGroupManagementService.UpdateGate;
    private void RequireAdministrator()
    {
        var session = sessions.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        if (session.Info?.IsAdmin != true) throw new UnauthorizedAccessException("Only MOVEit administrators can manage Groups and Permissions.");
    }
    private static string UserGroupType(string type)
        => type is "local" or "remote" ? type : throw new ArgumentException("User Group Type must be Local or Domain.");
    private static string Name(string? value)
        => !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new ArgumentException("Resource Group and User Group names are required.");
    private static string AclVersion(JsonObject acl)
        => ObjectEditVersion.Create("ResourceGroupACL", acl["ACLID"]?.ToString() ?? "", acl);
    private static bool IsPair(JsonObject acl, string group, string user)
        => string.Equals(acl["ResourceGroupName"]?.ToString(), group, StringComparison.OrdinalIgnoreCase)
            && string.Equals(acl["UserGroupName"]?.ToString(), user, StringComparison.OrdinalIgnoreCase);
    private async Task<string> ExistingGroupAsync(string name, CancellationToken ct)
    {
        var matches = (await rest.GetResourceGroupDefinitionsAsync(ct)).Where(row => string.Equals(row["Name"]?.ToString(), name, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length == 1 ? RequiredName(matches[0], "Name") : throw new InvalidOperationException("The Resource Group no longer exists or could not be identified. Refresh Groups and Permissions.");
    }
    private async Task<IReadOnlyList<ResourceUserGroupOption>> UserGroupsAsync(string type, CancellationToken ct)
    {
        var result = (await rest.GetUserGroupDefinitionsAsync(UserGroupType(type), ct)).Select(row => new ResourceUserGroupOption(
            RequiredName(row, "Name"), row["Description"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "")).ToArray();
        if (result.Select(row => row.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Length)
            throw new InvalidOperationException("MOVEit returned ambiguous User Groups. Reload the list before saving.");
        return result.OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public async Task<ResourceGroupUserGroupSetup> PrepareUserGroupsAsync(string resourceGroupName, string type, CancellationToken cancellationToken = default)
    {
        RequireAdministrator(); UserGroupType(type);
        var group = await ExistingGroupAsync(Name(resourceGroupName), cancellationToken);
        var available = UserGroupsAsync(type, cancellationToken);
        var acls = rest.GetResourceGroupAclDefinitionsAsync(cancellationToken);
        await Task.WhenAll(available, acls);
        return new(group, type, await available, (await acls).Where(row => string.Equals(row["ResourceGroupName"]?.ToString(), group, StringComparison.OrdinalIgnoreCase))
            .Select(row => RequiredName(row, "UserGroupName")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }
    public async Task<ResourceGroupUserGroupMutation> AddUserGroupsAsync(ResourceGroupUserGroupsRequest request, CancellationToken cancellationToken = default)
    {
        RequireAdministrator(); ArgumentNullException.ThrowIfNull(request);
        var requested = request.UserGroups ?? throw new ArgumentException("Select at least one User Group.");
        if (requested.Count == 0 || requested.Any(item => item is null)) throw new ArgumentException("Select at least one User Group.");
        var selected = requested.Select(item => new ResourceUserGroupSelection(Name(item.Name), UserGroupType(item.Type))).ToArray();
        if (selected.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != selected.Length) throw new ArgumentException("Select each User Group only once.");
        var cacheKey = MoveItServiceCache.UserScopedKey("resource-group-permissions:", sessions);
        await AclUpdateGate.WaitAsync(cancellationToken);
        try
        {
            RequireAdministrator();
            var group = await ExistingGroupAsync(Name(request.ResourceGroupName), cancellationToken);
            // Validate every selection against its live Local/Domain inventory before the first POST.
            var catalogs = new Dictionary<string, IReadOnlyList<ResourceUserGroupOption>>(StringComparer.Ordinal);
            foreach (var type in selected.Select(item => item.Type).Distinct()) catalogs[type] = await UserGroupsAsync(type, cancellationToken);
            selected = selected.Select(item => new ResourceUserGroupSelection(catalogs[item.Type].FirstOrDefault(row => string.Equals(row.Name, item.Name, StringComparison.OrdinalIgnoreCase))?.Name
                ?? throw new InvalidOperationException($"User Group '{item.Name}' is no longer available. Reload the list."), item.Type)).ToArray();
            var acls = (await rest.GetResourceGroupAclDefinitionsAsync(cancellationToken)).ToList();
            var results = new List<ResourceGroupUserGroupResult>(); var uncertain = false;
            foreach (var item in selected)
            {
                if (uncertain) { results.Add(new(item.Name, "notAttempted", "Not attempted after an unconfirmed assignment.")); continue; }
                if (acls.Any(acl => IsPair(acl, group, item.Name))) { results.Add(new(item.Name, "alreadyAssigned", "Already assigned; permissions retained.")); continue; }
                RequireAdministrator(); cancellationToken.ThrowIfCancellationRequested();
                var payload = new JsonObject { ["ACLID"] = null, ["ResourceGroupName"] = group, ["UserGroupName"] = item.Name,
                    ["Permissions"] = new JsonObject(PermissionKeys.Select(key => KeyValuePair.Create<string, JsonNode?>(key, JsonValue.Create(0)))) };
                try
                {
                    var saved = await rest.CreateResourceGroupAclAsync(payload, cancellationToken);
                    if (!IsPair(saved, group, item.Name) || !ValidAclId(saved["ACLID"]?.ToString())
                        || saved["Permissions"] is not JsonObject permissions || PermissionKeys.Any(key => permissions[key]?.ToString() is not ("0" or "false")))
                        throw new InvalidOperationException("The User Group assignment acknowledgement could not be confirmed.");
                    acls.Add(saved); results.Add(new(item.Name, "added", "Added with all permissions off."));
                }
                catch (MoveItAuthenticationException) { throw; }
                catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or JsonException or HttpRequestException or OperationCanceledException)
                {
                    // Clear 4xx rejections are safe failures, even if a subsequent inventory read is unavailable.
                    // A conflict may mean another user assigned it; an accepted POST can also lose its acknowledgement.
                    if (Rejected(ex) && ex is MoveItApiException { StatusCode: not HttpStatusCode.Conflict })
                    { results.Add(new(item.Name, "failed", ex.Message)); continue; }
                    var observed = await ReconcileAclsAsync();
                    var matches = observed?.Where(acl => IsPair(acl, group, item.Name)).ToArray();
                    if (matches?.Length == 1) { acls = observed!.ToList(); results.Add(new(item.Name, "alreadyAssigned", "Assignment confirmed; current permissions retained.")); }
                    else if (Rejected(ex)) results.Add(new(item.Name, "failed", ex.Message));
                    else { uncertain = true; results.Add(new(item.Name, "unconfirmed", "The assignment result could not be confirmed. Close and inspect the refreshed grid before retrying.")); }
                }
            }
            return new(group, results, uncertain);
        }
        finally { cache.Remove(cacheKey); AclUpdateGate.Release(); }
    }
    public async Task<ResourceGroupUserGroupMutation> RemoveUserGroupAsync(ResourceGroupRemoveUserGroupRequest request, CancellationToken cancellationToken = default)
    {
        RequireAdministrator(); ArgumentNullException.ThrowIfNull(request);
        var group = Name(request.ResourceGroupName); var user = Name(request.UserGroupName);
        if (!ValidAclId(request.AclId) || string.IsNullOrWhiteSpace(request.EditVersion)) throw new ArgumentException("Refresh the assignment before removing this User Group.");
        var cacheKey = MoveItServiceCache.UserScopedKey("resource-group-permissions:", sessions);
        await AclUpdateGate.WaitAsync(cancellationToken);
        try
        {
            RequireAdministrator(); group = await ExistingGroupAsync(group, cancellationToken);
            var acls = await rest.GetResourceGroupAclDefinitionsAsync(cancellationToken);
            var matches = acls.Where(acl => acl["ACLID"]?.ToString() == request.AclId).ToArray();
            if (matches.Length == 0 && !acls.Any(acl => IsPair(acl, group, user))) return new(group, [new(user, "alreadyRemoved", "This User Group is already unassigned.")]);
            if (matches.Length != 1 || !IsPair(matches[0], group, user) || AclVersion(matches[0]) != request.EditVersion)
                throw new ObjectEditConflictException("User Group assignment");
            RequireAdministrator(); cancellationToken.ThrowIfCancellationRequested();
            try { await rest.DeleteResourceGroupAclAsync(request.AclId, cancellationToken); }
            catch (MoveItAuthenticationException) { throw; }
            catch (Exception ex) when (ex is MoveItApiException or HttpRequestException or OperationCanceledException)
            {
                var observed = await ReconcileAclsAsync();
                if (observed is not null && !observed.Any(acl => IsPair(acl, group, user))) return new(group, [new(user, "removed", "User Group removed.")]);
                if (Rejected(ex)) throw;
                return new(group, [new(user, "unconfirmed", "The removal result could not be confirmed. Close and inspect the refreshed grid before retrying.")], true);
            }
            return new(group, [new(user, "removed", "User Group removed.")]);
        }
        finally { cache.Remove(cacheKey); AclUpdateGate.Release(); }
    }
    private static bool ValidAclId(string? value) => ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _);
    private static bool Rejected(Exception ex) => ex is MoveItApiException api && api.StatusCode.HasValue
        && (int)api.StatusCode.Value >= 400 && (int)api.StatusCode.Value < 500 && api.StatusCode != HttpStatusCode.RequestTimeout;
    private async Task<IReadOnlyList<JsonObject>?> ReconcileAclsAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { return await rest.GetResourceGroupAclDefinitionsAsync(timeout.Token); }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or JsonException or HttpRequestException or OperationCanceledException) { return null; }
    }
}
