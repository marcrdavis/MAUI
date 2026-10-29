// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

public sealed class AutomationScript
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string LanguageCode { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Group { get; init; } = string.Empty;
    public bool CanEditDelete { get; init; }
    public bool IsBuiltIn { get; init; }
}

public sealed record AutomationScriptTaskReference(string Id, string Name);
