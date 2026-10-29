// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

public sealed class AutomationHost
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string TypeCode { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Destination { get; init; } = string.Empty;
    public string Secure { get; init; } = string.Empty;
    public int? Port { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Group { get; init; } = string.Empty;
    public bool CanEditDelete { get; init; }
    public IReadOnlyList<AutomationHostTaskReference> AssignedTasks { get; init; } = Array.Empty<AutomationHostTaskReference>();
    public int AssignedCount => AssignedTasks.Count;
}

public sealed record AutomationHostTaskReference(string Id, string Name);
