// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

public sealed class GridViewModel
{
    public string GridId { get; init; } = string.Empty;
    // Optional persistence scope for user column layout preferences. GridId remains the stable functional grid type.
    public string PreferenceKey { get; init; } = string.Empty;
    public string EntityName { get; init; } = string.Empty;
    public string SearchPlaceholder { get; init; } = string.Empty;
    public IReadOnlyList<GridColumn> Columns { get; init; } = Array.Empty<GridColumn>();
    public IReadOnlyList<GridRow> Rows { get; init; } = Array.Empty<GridRow>();
    public GridOptions Options { get; init; } = new();
    public IReadOnlyList<string> SearchColumns { get; init; } = Array.Empty<string>();
    public bool FitColumnsToGrid { get; init; }
    public bool HighlightFailedTasksInGrid { get; init; }
    public bool SelectRowsBeyondGridSize { get; init; }
    public int DefaultPageSize { get; init; } = 10;
    public IReadOnlyList<int> PageSizeOptions { get; init; } = GridBehaviorOptions.SupportedDefaultRowSizes;
    public int? TotalCountOverride { get; init; }
    public int? DisplayedCountOverride { get; init; }
    public string CountActionText { get; init; } = string.Empty;
    public bool EnableRowSelection { get; init; } = true;
    public bool ShowBulkActions { get; init; } = true;
    // Controls shared toolbar items that are not meaningful on specialized management grids.
    public bool ShowSavedViewActions { get; init; } = true;
    public bool ShowCreateReportAction { get; init; } = true;
    public string DefaultSortColumn { get; init; } = string.Empty;
    public string DefaultSortDirection { get; init; } = string.Empty;
}

public sealed class GridColumn
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Type { get; init; } = "text";
    // Optional initial width in pixels. A saved user/saved-view width takes precedence.
    // Values <= 0 use the grid's normal initial width calculation.
    public double DefaultWidth { get; init; }
}

public sealed class GridRow
{
    public string Id { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> RawValues { get; init; } = new Dictionary<string, string>();
    public string InfoActionUrl { get; init; } = string.Empty;
}
