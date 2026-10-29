// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

public static class UserPreferenceThemes
{
    public const string Light = "light";
    public const string Dark = "dark";

    // Purpose: Normalizes a theme preference to a supported value.
    public static string Normalize(string? value)
        => string.Equals(value?.Trim(), Dark, StringComparison.OrdinalIgnoreCase) ? Dark : Light;
}

public static class UserPreferenceStartPages
{
    public const string Dashboard = "Dashboard";
    public const string Tasks = "Tasks";
    public const string Hosts = "Hosts";

    public static IReadOnlyList<string> Allowed { get; } = new[] { Dashboard, Tasks, Hosts };

    // Purpose: Normalizes a start-page preference to a supported workspace.
    public static string Normalize(string? value)
    {
        var candidate = value?.Trim() ?? string.Empty;
        return Allowed.FirstOrDefault(item => item.Equals(candidate, StringComparison.OrdinalIgnoreCase)) ?? Dashboard;
    }

    // Purpose: Resolves a start-page preference to its MVC route.
    public static (string Controller, string Action) ResolveRoute(string? value)
        => Normalize(value) switch
        {
            Tasks => ("Tasks", "Index"),
            Hosts => ("Hosts", "Index"),
            _ => ("Home", "Index")
        };

    // Canonical, allowlisted browser destination; never treat a stored preference as a URL.
    public static string ResolvePath(string? value)
    {
        var route = ResolveRoute(value);
        return $"/{route.Controller}/{route.Action}";
    }
}

// Hidden deployment defaults for user-level preferences. Grid page size intentionally continues
// to use Grid:DefaultRowSize so administrators retain the existing Configuration control.
public sealed class UserPreferencesOptions
{
    public const string SectionName = "UserPreferences";

    public SettingDefinition<string> Theme { get; set; } = new()
    {
        Name = "Default User Theme",
        Value = UserPreferenceThemes.Light,
        Description = "Default theme used when a user has not saved a personal preference.",
        Type = "List",
        Hidden = true
    };

    public SettingDefinition<string> StartPage { get; set; } = new()
    {
        Name = "Default User Start Page",
        Value = UserPreferenceStartPages.Dashboard,
        Description = "Default landing page used when a user has not saved a personal preference.",
        Type = "List",
        Hidden = true
    };
}

public sealed record UserPreferencesSnapshot(
    string Theme,
    string StartPage,
    int GridPageSize,
    DateTimeOffset? CreatedUtc = null,
    DateTimeOffset? ModifiedUtc = null);

public sealed class UserPreferencesSaveRequest
{
    public string Theme { get; set; } = UserPreferenceThemes.Light;
    public string StartPage { get; set; } = UserPreferenceStartPages.Dashboard;
    public int GridPageSize { get; set; } = 50;
}

public static class UserGridLayoutSchema
{
    public const int CurrentVersion = 1;
}

public sealed class UserGridLayoutState
{
    public int SchemaVersion { get; set; } = UserGridLayoutSchema.CurrentVersion;
    public List<string> VisibleColumns { get; set; } = new();
    public List<string> ColumnOrder { get; set; } = new();
    public Dictionary<string, double> Widths { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record UserGridLayoutSnapshot(
    string GridKey,
    UserGridLayoutState Layout,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ModifiedUtc);

public sealed class UserGridLayoutSaveRequest
{
    public string GridKey { get; set; } = string.Empty;
    public UserGridLayoutState Layout { get; set; } = new();
}

public sealed class UserGridLayoutResetRequest
{
    public string GridKey { get; set; } = string.Empty;
}

public sealed class UserPreferenceValueRequest<T>
{
    public T Value { get; set; } = default!;
}

// Uses the existing UserGridPreferences JSON record; no database migration required.
public sealed record TraditionalTaskEditorPreferences(bool AutoSave);


public sealed record HostEditorPreferences(bool AutoSave);

public sealed record ScriptEditorPreferences(bool AutoSave);

public sealed record DateListEditorPreferences(bool AutoSave);
