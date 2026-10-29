// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Outbound report configuration, scheduling, and grid snapshot contracts.

namespace MoveItAutomationUI.Models;

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 25;
    public bool EnableSsl { get; set; }
    public bool IgnoreCertificateErrors { get; set; }
    public string Username { get; set; } = string.Empty;
    public string ProtectedPassword { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "MAUI Reports";
}

public static class OutboundReportDeliveryModes
{
    public const string Now = "Now";
    public const string Once = "Once";
    public const string Recurring = "Recurring";
}

public static class OutboundReportFrequencies
{
    public const string Daily = "Daily";
    public const string Weekly = "Weekly";
    public const string Monthly = "Monthly";
}

public sealed class ReportGridColumnState
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Type { get; set; } = "text";
    public int Order { get; set; }
    public double? Width { get; set; }
}

public sealed class ReportGridSortState
{
    public string Column { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string Type { get; set; } = "text";
}

public sealed class ReportGridViewState
{
    public int SchemaVersion { get; set; } = 1;
    public List<ReportGridColumnState> Columns { get; set; } = new();
    public string SearchText { get; set; } = string.Empty;
    public List<string> SearchColumns { get; set; } = new();
    public Dictionary<string, string> Filters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    // Captures each active column filter's grid type so scheduled report regeneration can apply
    // relative-date macros to datetime columns with the same semantics as the interactive grid.
    public Dictionary<string, string> FilterTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ScopeFilters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public ReportGridSortState? Sort { get; set; }
}

public sealed class ReportGridSnapshotRow
{
    public string Id { get; set; } = string.Empty;
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> RawValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ReportGridSnapshot
{
    public List<ReportGridColumnState> Columns { get; set; } = new();
    public List<ReportGridSnapshotRow> Rows { get; set; } = new();
}

public sealed class ReportSourceContext
{
    public string Path { get; set; } = string.Empty;
    public Dictionary<string, string> Query { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ReportScheduleDefinition
{
    public string DeliveryMode { get; set; } = OutboundReportDeliveryModes.Now;
    public string Frequency { get; set; } = string.Empty;
    public string TimeOfDay { get; set; } = "08:00";
    public List<string> Dates { get; set; } = new();
    public List<int> DaysOfWeek { get; set; } = new();
    public List<int> DaysOfMonth { get; set; } = new();
    public string TimeZoneId { get; set; } = string.Empty;
}

public sealed class CreateOutboundReportRequest
{
    public string GridKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Recipients { get; set; } = string.Empty;
    public ReportGridViewState ViewState { get; set; } = new();
    public ReportSourceContext SourceContext { get; set; } = new();
    public ReportScheduleDefinition Schedule { get; set; } = new();
    public ReportGridSnapshot? Snapshot { get; set; }
}

public sealed class UpdateOutboundReportRequest
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Recipients { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public ReportScheduleDefinition Schedule { get; set; } = new();
}

public sealed class UpdateOutboundReportViewRequest
{
    public string Id { get; set; } = string.Empty;
    public string GridKey { get; set; } = string.Empty;
    public ReportGridViewState ViewState { get; set; } = new();
    public ReportSourceContext SourceContext { get; set; } = new();
}

public sealed record OutboundReportEditDefinition(
    string Id,
    string GridKey,
    string Title,
    string Description,
    ReportGridViewState ViewState,
    ReportSourceContext SourceContext);

public sealed class OutboundReportIdRequest
{
    public string Id { get; set; } = string.Empty;
}

public sealed class SetOutboundReportEnabledRequest
{
    public string Id { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}

public sealed class BulkOutboundReportRequest
{
    public List<string> ReportIds { get; set; } = new();
}

public sealed class OutboundReportImportPreflightRequest
{
    public IFormFile? File { get; set; }
}

// Imported definitions are always created disabled; there is intentionally no import-time enable option.
public sealed class OutboundReportImportRequest
{
    public IFormFile? File { get; set; }
}

public sealed record OutboundReportImportPreflightItem(string Title, string EnvironmentKey, bool Valid, string Message);

public sealed record OutboundReportImportPreflightResult(
    int ItemCount,
    int ErrorCount,
    IReadOnlyList<OutboundReportImportPreflightItem> Items,
    string Message);

public sealed record OutboundReportImportResult(
    int ItemCount,
    int ImportedCount,
    IReadOnlyList<string> Titles);

public sealed class SmtpConfigurationSaveRequest
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 25;
    public bool EnableSsl { get; set; }
    public bool IgnoreCertificateErrors { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "MAUI Reports";
}

public sealed class SmtpTestRequest
{
    public string Recipient { get; set; } = string.Empty;
}

public sealed record OutboundEmailConfigurationStatus(
    bool IsConfigured,
    string Message);

public sealed record OutboundReportSummary(
    string Id,
    string EnvironmentKey,
    string OwnerUsername,
    string GridKey,
    string GridLabel,
    string Title,
    string Description,
    IReadOnlyList<string> Recipients,
    bool Enabled,
    ReportScheduleDefinition Schedule,
    DateTimeOffset? NextRunUtc,
    DateTimeOffset? LastRunUtc,
    string LastStatus,
    string LastMessage,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ModifiedUtc);

public sealed record OutboundReportExecutionResult(
    bool Success,
    int RowCount,
    string Message,
    DateTimeOffset CompletedUtc);

public sealed record OutboundReportHistorySummary(
    long Id,
    string ReportId,
    string EnvironmentKey,
    string Title,
    DateTimeOffset StartedUtc,
    DateTimeOffset CompletedUtc,
    string Status,
    int RowCount,
    int RecipientCount,
    string Message);

public sealed record OutboundReportData(
    IReadOnlyList<ReportGridColumnState> Columns,
    IReadOnlyList<ReportGridSnapshotRow> Rows);

public static class OutboundReportGridRegistry
{
    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["tasks"] = "Tasks",
        ["upcomingruns"] = "Upcoming Runs",
        ["hosts"] = "Hosts",
        ["scripts"] = "Scripts",
        ["datelists"] = "Date Lists",
        ["globalparameters"] = "Global Parameters",
        ["keysandcertificates"] = "Keys & Certificates",
        ["taskruns"] = "Task Run",
        ["fileactivity"] = "File Activity",
        ["audit"] = "Audit",
        ["logs"] = "Logs"
    };

    public static bool TryGetLabel(string? gridKey, out string label)
        => Labels.TryGetValue((gridKey ?? string.Empty).Trim(), out label!);

    public static IReadOnlyCollection<string> Keys => Labels.Keys.ToArray();
}
