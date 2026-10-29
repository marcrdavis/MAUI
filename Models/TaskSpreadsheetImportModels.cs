// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Task spreadsheet import contracts. Kept separate from MAUI native portability packages.

namespace MoveItAutomationUI.Models;


public sealed class TaskSpreadsheetPreviewForm
{
    public IFormFile? File { get; set; }
}

public sealed record TaskSpreadsheetFilePreviewRow(
    int RowNumber,
    IReadOnlyDictionary<string, string> Values);

public sealed record TaskSpreadsheetFilePreviewResult(
    string FileName,
    IReadOnlyList<string> Columns,
    IReadOnlyList<TaskSpreadsheetFilePreviewRow> Rows,
    int SkippedBlankRows);

public sealed class TaskSpreadsheetAnalyzeForm
{
    public IFormFile? File { get; set; }
    public string TaskNamePrefix { get; set; } = string.Empty;
    public bool UpdateExisting { get; set; }
    public bool LeaveTasksDisabled { get; set; } = true;
}

public sealed class TaskSpreadsheetImportRowRequest
{
    public string AnalysisId { get; set; } = string.Empty;
    public int RowNumber { get; set; }
}

public sealed record TaskSpreadsheetImportIssue(
    string Severity,
    int? RowNumber,
    string Column,
    string Message);

public sealed record TaskSpreadsheetPreviewRow(
    int RowNumber,
    string Status,
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyList<TaskSpreadsheetImportIssue> Issues);

public sealed record TaskSpreadsheetAnalysisResult(
    string AnalysisId,
    string FileName,
    string TaskNamePrefix,
    bool UpdateExisting,
    bool LeaveTasksDisabled,
    IReadOnlyList<string> Columns,
    IReadOnlyList<TaskSpreadsheetPreviewRow> Rows,
    IReadOnlyList<TaskSpreadsheetImportIssue> Issues,
    int ValidRows,
    int WarningRows,
    int ErrorRows,
    int IgnoredColumns,
    bool CanImport);

public sealed record TaskSpreadsheetImportRowResult(
    int RowNumber,
    string TaskName,
    string Outcome,
    string TaskId,
    string Message,
    IReadOnlyList<string> Warnings);

