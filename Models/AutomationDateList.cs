// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

public sealed class AutomationDateList
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Group { get; init; } = string.Empty;
    public bool CanEditDelete { get; init; }
    public int EntryCount { get; init; }
}

public sealed record AutomationDateListTaskReference(string Id, string Name);
