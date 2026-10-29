// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

public sealed record AutomationHostBrowseItem(
    string Name,
    string Path,
    bool IsDirectory,
    string NativeId = "",
    string NativeType = "",
    string Size = "",
    string LastModified = "");

public sealed record AutomationHostBrowseResult(
    string HostId,
    string HostName,
    string HostTypeCode,
    string CurrentDirectory,
    string ParentDirectory,
    IReadOnlyList<AutomationHostBrowseItem> Items,
    string RootName = "",
    string RootId = "",
    string RootKind = "");

public sealed class AutomationHostBrowseRequest
{
    public string HostId { get; set; } = string.Empty;
    public string Directory { get; set; } = string.Empty;
    public string FileMask { get; set; } = string.Empty;
    public string RootName { get; set; } = string.Empty;
    public string RootId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string StepId { get; set; } = string.Empty;
}
