// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
namespace MoveItAutomationUI.Models;

public sealed record ResourceGroupGrant(string AclId, string Name, IReadOnlyDictionary<string, bool> Permissions, string EditVersion = "");
public sealed record ResourceGroupPermissionSummary(string State, int Granted, int Total);
public sealed record ResourceGroupPermissionRow(string Name, IReadOnlyList<ResourceGroupGrant> UserGroups,
    IReadOnlyDictionary<string, ResourceGroupPermissionSummary> Permissions);
public sealed record ResourceGroupPermissionsSnapshot(IReadOnlyList<ResourceGroupPermissionRow> Groups, DateTimeOffset RefreshedUtc);
public sealed record GroupsAndPermissionsPageViewModel(int DefaultPageSize, IReadOnlyList<int> PageSizeOptions);

public sealed record ResourceUserGroupOption(string Name, string Description);
public sealed record ResourceGroupUserGroupSetup(string ResourceGroupName, string UserGroupType,
    IReadOnlyList<ResourceUserGroupOption> Groups, IReadOnlyList<string> AssignedGroups);
public sealed record ResourceUserGroupSelection(string Name, string Type);
public sealed class ResourceGroupUserGroupsRequest
{
    public string ResourceGroupName { get; set; } = string.Empty;
    public List<ResourceUserGroupSelection> UserGroups { get; set; } = new();
}
public sealed class ResourceGroupRemoveUserGroupRequest
{
    public string ResourceGroupName { get; set; } = string.Empty;
    public string UserGroupName { get; set; } = string.Empty;
    public string AclId { get; set; } = string.Empty;
    public string EditVersion { get; set; } = string.Empty;
}
public sealed record ResourceGroupUserGroupResult(string Name, string Status, string Message);
public sealed record ResourceGroupUserGroupMutation(string ResourceGroupName,
    IReadOnlyList<ResourceGroupUserGroupResult> Items, bool SaveUnconfirmed = false);

public sealed class ResourceGroupPermissionUpdate
{
    public string AclId { get; set; } = string.Empty;
    public string UserGroupName { get; set; } = string.Empty;
    public string EditVersion { get; set; } = string.Empty;
    public Dictionary<string, bool> Permissions { get; set; } = new(StringComparer.Ordinal);
}
public sealed class ResourceGroupPermissionsSaveRequest
{
    public string ResourceGroupName { get; set; } = string.Empty;
    public List<ResourceGroupPermissionUpdate> Updates { get; set; } = new();
}
public sealed record ResourceGroupPermissionSaveResult(string AclId, string Name, string Status, string Message);
public sealed record ResourceGroupPermissionsSaveResult(string ResourceGroupName,
    IReadOnlyList<ResourceGroupPermissionSaveResult> Items, bool SaveUnconfirmed = false, bool EditConflict = false);
