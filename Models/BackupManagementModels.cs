// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Backup inventory and restore request/response models.

namespace MoveItAutomationUI.Models;

public sealed record MauiBackupDescriptor(
    string Id,
    DateTimeOffset BackupUtc,
    bool IsComplete,
    bool HasAppSettings,
    bool HasSqliteDatabase,
    string DatabaseProvider,
    int MoveItConfigurationCount,
    IReadOnlyList<string> MoveItEnvironments,
    long SizeBytes,
    bool CanRestore,
    string RestoreBlockReason);

public sealed record MauiBackupRestoreResult(
    bool Success,
    bool RestartScheduled,
    IReadOnlyList<string> Messages);

public sealed class MauiBackupActionRequest
{
    public string BackupId { get; set; } = string.Empty;
}

public sealed class MauiBackupRestoreRequest
{
    public string BackupId { get; set; } = string.Empty;
    // Backup environment labels selected explicitly; an empty list restores MAUI only.
    public IReadOnlyList<string> MoveItEnvironments { get; set; } = [];
    public bool Acknowledged { get; set; }
    public string ConfirmationText { get; set; } = string.Empty;
}
