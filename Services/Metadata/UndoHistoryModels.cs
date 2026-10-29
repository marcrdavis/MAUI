// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Shared models/helpers for MAUI's per-user three-version configuration undo history.

namespace MoveItAutomationUI.Services.Metadata;

public sealed record UndoHistoryEntry(
    string SnapshotId,
    DateTimeOffset CreatedUtc,
    string CreatedBy,
    bool CanRestore,
    string Message);

public sealed record UndoHistoryResult(
    string ObjectId,
    string ObjectName,
    IReadOnlyList<UndoHistoryEntry> Snapshots,
    string Message)
{
    public bool CanUndo => Snapshots.Any(item => item.CanRestore);
}

internal static class UndoHistoryEvaluator
{
    // Purpose: Builds the requested result from the supplied state.
    public static UndoHistoryResult Build(
        string objectId,
        string fallbackName,
        string username,
        string objectLabel,
        IReadOnlyList<ConfigurationUndoSnapshot> snapshots,
        ObjectConfigurationAudit audit)
    {
        if (snapshots.Count == 0)
            return new UndoHistoryResult(objectId, fallbackName, Array.Empty<UndoHistoryEntry>(), $"No undo data is available for this {objectLabel} and user.");

        var entries = snapshots.Select(snapshot =>
        {
            var blocked = audit.ModifiedUtc.HasValue
                && audit.ModifiedUtc.Value > snapshot.CreatedUtc
                && !string.IsNullOrWhiteSpace(audit.ModifiedBy)
                && !audit.ModifiedBy.Equals(username, StringComparison.OrdinalIgnoreCase);
            var message = blocked
                ? $"This {objectLabel} was modified through MAUI by {audit.ModifiedBy} after this undo point was created. This saved version cannot be safely restored."
                : string.Empty;
            return new UndoHistoryEntry(snapshot.SnapshotId, snapshot.CreatedUtc, snapshot.CreatedBy, !blocked, message);
        }).ToList();

        var newest = snapshots[0];
        return new UndoHistoryResult(newest.ObjectId, string.IsNullOrWhiteSpace(newest.ObjectName) ? fallbackName : newest.ObjectName, entries,
            entries.Any(item => item.CanRestore) ? string.Empty : entries.FirstOrDefault()?.Message ?? $"No undo data is available for this {objectLabel} and user.");
    }
}
