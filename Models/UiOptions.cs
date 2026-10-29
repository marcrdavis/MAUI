// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

public sealed class UiOptions
{
    public const string SectionName = "Ui";

    public string ApplicationName { get; set; } = "MAUI";
    public string ApplicationLogo { get; set; } = "/images/automation-mark.svg";
    public string Version { get; set; } = string.Empty;
    public string CopyrightText { get; set; } = "Copyright (C) 2026 Marc R. Davis";
    public string DefaultServer { get; set; } = "Automation";
    public SettingDefinition<bool> AutoFormatNewTaskHostNames { get; set; } = new()
    {
        Name = "Auto Format New Task/Host Names",
        Value = true,
        Description = "Controls if the Auto Format Name option is enabled by default for Tasks and Hosts.",
        Type = "Boolean",
        PossibleValues = new List<bool> { false, true }
    };
    public SettingDefinition<bool> CheckoutObjectsForDurationOfEdit { get; set; } = new()
    {
        Name = "Checkout Objects for Duration of Edit",
        Value = false,
        Description = "Reserve objects while their MAUI editor is open to prevent concurrent edits. Checkout is released when the editor closes or its editing session expires.",
        Type = "Boolean",
        PossibleValues = new List<bool> { false, true }
    };
    public SettingDefinition<bool> EnableQuickTransfer { get; set; } = new()
    {
        Name = "Enable Quick Transfer",
        Value = false,
        Description = "Allow MOVEit administrators to access Transfers > Quick Transfer. When disabled, the navigation item is hidden and all Quick Transfer endpoints are unavailable.",
        Type = "Boolean",
        PossibleValues = new List<bool> { false, true }
    };
    public SettingDefinition<bool> TaskJsonEditor { get; set; } = new()
    {
        Name = "Task JSON Editor", Value = false, Type = "Boolean", Experimental = true,
        Description = "Edit individual Task JSON values or replace matching values across selected Tasks. Review changes before saving. Existing Task edit permissions and checkout safeguards apply.",
        PossibleValues = new List<bool> { false, true }
    };
    // Maximum spreadsheet rows rendered on a preview page and imported per action.
    public SettingDefinition<int> ImportRowLimit { get; set; } = new()
    {
        Name = "Import Row Limit",
        Value = 500,
        Description = "Maximum number of CSV/Excel Task rows displayed per page and imported per batch (1–1000).",
        Type = "Number"
    };
    public SettingDefinition<bool> TaskFailureAlertsEnabled { get; set; } = new()
    {
        Name = "Task Failure Alerts",
        Value = false,
        Description = "Displays a temporary System Message with an audible alert when one or more MOVEit Task runs fail.",
        Type = "Boolean",
        PossibleValues = new List<bool> { false, true }
    };
    // Empty means use the release-defined navigation order from ConfigurationSettingsCatalog.
    public List<string> SettingsNavigationOrder { get; set; } = new();
    public Dictionary<string, GridOptions> Grids { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class GridOptions
{
    public List<string> DefaultColumns { get; set; } = new();
    public List<string> AvailableColumns { get; set; } = new();
    public List<string> BulkActions { get; set; } = new();
    public List<string> AddActions { get; set; } = new();
    public List<string> SearchColumns { get; set; } = new();
}

// Describes one application setting together with metadata that can later drive the Configuration UI.
public sealed class SettingDefinition<T>
{
    public string Name { get; set; } = string.Empty;
    public T Value { get; set; } = default!;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = "String";
    public List<T> PossibleValues { get; set; } = new();
    public bool Hidden { get; set; } = false;
    // Marks a setting as experimental so the Configuration UI can surface it separately.
    public bool Experimental { get; set; } = false;
}

public sealed class GridBehaviorOptions
{
    public const string SectionName = "Grid";
    public static readonly IReadOnlyList<int> SupportedDefaultRowSizes = new[] { 10, 50, 100, 200, 500, 1000 };

    public SettingDefinition<bool> FitColumnsToGrid { get; set; } = new()
    {
        Name = "Fit Columns to Grid",
        Value = true,
        Description = "When enabled, unused horizontal grid space is assigned to the last visible column.",
        Type = "Boolean",
        PossibleValues = new List<bool> { false, true }
    };

    public SettingDefinition<bool> HighlightFailedTasksInGrid { get; set; } = new()
    {
        Name = "Highlight Failed Tasks in Grid",
        Value = false,
        Description = "When enabled, failed Task rows are highlighted when the Status column is visible.",
        Type = "Boolean",
        PossibleValues = new List<bool> { false, true }
    };

    public SettingDefinition<int> DefaultRowSize { get; set; } = new()
    {
        Name = "Default Row Size",
        Value = 50,
        Description = "Default number of rows displayed per page. Users can override this value with their personal Grid Page Size preference.",
        Type = "List"
    };

    public SettingDefinition<bool> SelectRowsBeyondGridSize { get; set; } = new()
    {
        Name = "Select Rows Beyond Grid Size",
        Value = false,
        Description = "When enabled, the grid header checkbox selects all rows matching the current filters, including rows on pages that are not currently visible. When disabled, it selects only rows visible on the current page.",
        Type = "Boolean",
        PossibleValues = new List<bool> { false, true }
    };
}

