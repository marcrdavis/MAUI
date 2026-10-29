// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Models shared by reusable Resource Group inventory/create/add workflows.

namespace MoveItAutomationUI.Models;

public sealed record ResourceGroupOption(string Name, string Description);

public sealed record ResourceGroupInventoryItem(
    string MemberType,
    string Type,
    string Id,
    string Name);

public sealed record ResourceGroupSetupResult(
    IReadOnlyList<ResourceGroupOption> Groups,
    IReadOnlyList<ResourceGroupInventoryItem> Items);

public sealed record ResourceGroupMutationResult(
    string GroupName,
    int AddedCount,
    int ExistingCount,
    int TotalMembers);

public sealed class ResourceGroupSetupRequest
{
    public List<string> TaskIds { get; set; } = new();
}

public sealed class ResourceGroupCreateRequest
{
    public List<string> TaskIds { get; set; } = new();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class ResourceGroupAddRequest
{
    public List<string> TaskIds { get; set; } = new();
    public string GroupName { get; set; } = string.Empty;
}

// The browser sends explicit selections only. The shared layer derives required members again on every save.
public sealed record ResourceGroupMember(string MemberType, string Id);
public sealed record ResourceGroupEditorItem(string MemberType, string Type, string Id, string Name,
    string Description, IReadOnlyList<string> RequiredBy);
public sealed record ResourceGroupEditorState(string Name, string Description, string EditVersion,
    IReadOnlyList<ResourceGroupMember> Members, IReadOnlyList<ResourceGroupEditorItem> Items);
public sealed record ResourceGroupEditorSetup(ResourceGroupEditorState Editor, IReadOnlyList<ResourceGroupEditorItem> AvailableItems);
public sealed class ResourceGroupMembersRequest
{
    public List<ResourceGroupMember> Members { get; set; } = new();
}
public sealed class ResourceGroupEditorSaveRequest
{
    public string OriginalName { get; set; } = string.Empty;
    public string EditVersion { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<ResourceGroupMember> Members { get; set; } = new();
}


public sealed record ResourceGroupDeleteSetup(string Name, string Description, string EditVersion);
public sealed class ResourceGroupDeleteRequest
{
    public string Name { get; set; } = string.Empty;
    public string EditVersion { get; set; } = string.Empty;
}
