// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

public static class SavedViewGridRegistry
{
    private static readonly IReadOnlyDictionary<string, SavedViewGridDefinition> Definitions =
        new Dictionary<string, SavedViewGridDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["tasks"] = new("tasks", "Tasks", "Tasks", "Index"),
            ["hosts"] = new("hosts", "Hosts", "Hosts", "Index"),
            ["scripts"] = new("scripts", "Scripts", "Resources", "Scripts"),
            ["datelists"] = new("datelists", "Date Lists", "Resources", "DateLists"),
            ["globalparameters"] = new("globalparameters", "Global Parameters", "Resources", "GlobalParameters"),
            ["keysandcertificates"] = new("keysandcertificates", "Keys and Certificates", "Resources", "KeysAndCerts"),
            // MOVEit exposes these native history grids as Reports; MAUI routes them through RecordsController
            // so saved views remain distinct from MAUI-generated Reports.
            ["taskruns"] = new("taskruns", "Task Run", "Records", "TaskRun"),
            ["fileactivity"] = new("fileactivity", "File Activity", "Records", "FileActivity"),
            ["audit"] = new("audit", "Audit", "Records", "Audit"),
            ["logs"] = new("logs", "Logs", "Configuration", "Logs")
        };

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "All", "All Tasks", "Running", "Success", "Failed", "Disabled", "Incomplete", "No Transfers",
        "All Hosts", "Local File System", "UNC Share", "MOVEit Transfer", "FTP/FTPS", "SSH/SFTP",
        "POP3 (Incoming Email)", "SMTP (Outgoing Email)", "Amazon S3", "Azure Blob", "SharePoint", "Google Storage",
        "Tasks", "Hosts", "Scripts", "Date Lists", "Global Parameters", "Keys and Certificates",
        "Task Run", "File Activity", "Audit", "Records", "Logs", "Dashboard", "My Views"
    };

    public static IReadOnlyCollection<SavedViewGridDefinition> All => Definitions.Values.ToArray();

    // Purpose: Resolves a supported saved-view grid definition by grid key.
    public static bool TryGet(string? gridKey, out SavedViewGridDefinition definition)
    {
        if (Definitions.TryGetValue((gridKey ?? string.Empty).Trim(), out var found))
        {
            definition = found;
            return true;
        }
        definition = null!;
        return false;
    }

    // Purpose: Returns whether a view name is reserved by a built-in MAUI view or workspace.
    public static bool IsReservedName(string? name)
        => ReservedNames.Contains((name ?? string.Empty).Trim());
}

public sealed record SavedViewGridDefinition(string GridKey, string Label, string Controller, string Action);

public sealed class SavedGridViewState
{
    public int SchemaVersion { get; set; } = 1;
    public List<SavedGridViewColumnState> Columns { get; set; } = new();
    public SavedGridViewSortState? Sort { get; set; }
    public Dictionary<string, string> Filters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SavedGridViewColumnState
{
    public string Key { get; set; } = string.Empty;
    public bool Visible { get; set; }
    public int Order { get; set; }
    public double? Width { get; set; }
}

public sealed class SavedGridViewSortState
{
    public string Column { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
}

public sealed class SavedViewSaveRequest
{
    public string Id { get; set; } = string.Empty;
    public string GridKey { get; set; } = string.Empty;
    public string ViewName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Overwrite { get; set; }
    public SavedGridViewState State { get; set; } = new();
}

public sealed class SavedViewDeleteRequest
{
    public string Id { get; set; } = string.Empty;
}

public sealed record SavedViewSummary(
    string Id,
    string GridKey,
    string GridLabel,
    string ViewName,
    string Description,
    string Controller,
    string Action,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ModifiedUtc);

public sealed record SavedViewDetail(SavedViewSummary Summary, SavedGridViewState? State, string Warning = "");

public enum SavedViewSaveStatus
{
    Saved,
    Conflict,
    ConflictOtherGrid
}

public sealed record SavedViewSaveResult(
    SavedViewSaveStatus Status,
    SavedViewSummary? View = null,
    SavedViewSummary? ExistingView = null);
