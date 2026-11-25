namespace MoveItAutomationUI.Models;

public sealed class ReportApiAccessRequest
{
    public bool Enabled { get; set; }
    public string Revision { get; set; } = string.Empty;
    public string? LinkDraft { get; set; }
}

public sealed class RegenerateReportApiRequest
{
    public string Id { get; set; } = string.Empty;
    public string Revision { get; set; } = string.Empty;
}

public sealed record ReportApiAccessState(bool Enabled, string Revision, string? Path,
    DateTimeOffset? LastAccessUtc, string? LinkDraft = null);

public sealed record ReportApiCsv(byte[] Content, DateTimeOffset GeneratedUtc, int RowCount,
    string DefinitionRevision, int? RetrievalLimit, bool SourceLimitReached);
