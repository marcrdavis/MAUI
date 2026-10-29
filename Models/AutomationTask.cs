// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

public sealed class AutomationTask
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string OperatorNotes { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public bool Enabled { get; init; }
    public string Scheduled { get; init; } = string.Empty;
    public string Schedule { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset? LastRun { get; init; }
    public string LastResult { get; init; } = string.Empty;
    public int? FilesSent { get; init; }
    public double? TotalBytesSent { get; init; }
    public string Group { get; init; } = string.Empty;
    public bool CanEditMoveItFields { get; init; }
    public bool CanRun { get; init; }
    public bool CanAddEditDelete { get; init; }
}
