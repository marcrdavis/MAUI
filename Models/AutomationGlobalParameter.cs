// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

public sealed class AutomationGlobalParameter
{
    public string Name { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string DisplayValue { get; init; } = string.Empty;
    public string CountedDisplayValue { get; init; } = string.Empty;
    public string Group { get; init; } = string.Empty;
    public bool Secure { get; init; }

    public string GridValue
    {
        get
        {
            if (!Secure) return Value;
            if (!string.IsNullOrWhiteSpace(DisplayValue)) return DisplayValue;
            if (!string.IsNullOrWhiteSpace(CountedDisplayValue)) return CountedDisplayValue;
            return "••••••••";
        }
    }
}

public sealed record GlobalParameterCacheSnapshot(
    IReadOnlyList<AutomationGlobalParameter> Parameters,
    DateTimeOffset RefreshedUtc);

public sealed record GlobalParameterDeletePreflightResult(
    string Name,
    bool Secure,
    bool CanDelete,
    string Message);

public sealed record GlobalParameterBulkDeleteItemResult(
    string Name,
    bool Success,
    bool Skipped,
    string Message);

public sealed record GlobalParameterBulkDeleteResult(
    IReadOnlyList<GlobalParameterBulkDeleteItemResult> Items)
{
    public int Succeeded => Items.Count(item => item.Success);
    public int Skipped => Items.Count(item => item.Skipped);
    public int Failed => Items.Count(item => !item.Success && !item.Skipped);
}

public sealed record GlobalParameterTaskReference(string Id, string Name);
