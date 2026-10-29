// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

public sealed class TasksPageViewModel
{
    public string ActiveFilter { get; init; } = "all";
    public int? SelectedFolderId { get; init; }
    public IReadOnlyList<TaskFolderOption> Folders { get; init; } = Array.Empty<TaskFolderOption>();
    public IReadOnlyList<TaskTreeItem> FolderTreeTasks { get; init; } = Array.Empty<TaskTreeItem>();
    public int UiTaskRefreshIntervalSeconds { get; init; } = 10;
    public bool AutoFormatNewTaskHostNames { get; init; } = true;
    public int ImportRowLimit { get; init; } = 500;
    public bool AdvancedTasksLicensed { get; init; } = true;
    public bool IsMoveItAdmin { get; init; }
    public DateTimeOffset? LastRefreshUtc { get; init; }
    public GridViewModel Grid { get; init; } = new();
}


public sealed class UpcomingRunsPageViewModel
{
    public GridViewModel Grid { get; init; } = new();
}

public sealed class TaskCalendarPageViewModel
{
    public string InitialDate { get; init; } = string.Empty;
}

public sealed class TaskFolderOption
{
    public int Id { get; init; }
    public int? ParentId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public int Depth { get; init; }
}

public sealed class TaskTreeItem
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int? FolderId { get; init; }
}


public sealed class ScriptsPageViewModel
{
    public bool CustomScriptsLicensed { get; init; } = true;
    public int UiScriptRefreshIntervalSeconds { get; init; } = 60;
    public DateTimeOffset? LastRefreshUtc { get; init; }
    public GridViewModel Grid { get; init; } = new();
}



public sealed class ResourceImportDialogViewModel
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

public sealed class ResourceImportPreflightRequest
{
    public IFormFile? File { get; set; }
    public string Mode { get; set; } = "create";
}

public sealed class ResourceImportRequest
{
    public IFormFile? File { get; set; }
    public string Mode { get; set; } = "create";
    public bool AcknowledgeUnsupportedMetadata { get; set; }
}

public sealed class ScriptCreateRequest
{
    public string ResourceGroup { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = "PS1";
}

public sealed class ScriptActionRequest
{
    public string ScriptId { get; set; } = string.Empty;
    public string SnapshotId { get; set; } = string.Empty;
    public bool DeleteWithoutRecycle { get; set; }
}

public sealed class BulkScriptRequest
{
    public List<string> ScriptIds { get; set; } = new();
    public bool DeleteWithoutRecycle { get; set; }
}

public sealed class ScriptDetailsUpdateRequest
{
    public string EditVersion { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}


public sealed class DateListsPageViewModel
{
    public int UiDateListRefreshIntervalSeconds { get; init; } = 60;
    public DateTimeOffset? LastRefreshUtc { get; init; }
    public GridViewModel Grid { get; init; } = new();
}

public sealed class DateListCreateRequest
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class DateListActionRequest
{
    public string DateListId { get; set; } = string.Empty;
    public string SnapshotId { get; set; } = string.Empty;
}

public sealed class BulkDateListRequest
{
    public List<string> DateListIds { get; set; } = new();
    public bool DeleteWithoutRecycle { get; set; }
}

public sealed class DateListDetailsUpdateRequest
{
    public string EditVersion { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}


public sealed class GlobalParametersPageViewModel
{
    public int UiGlobalParameterRefreshIntervalSeconds { get; init; } = 60;
    public DateTimeOffset? LastRefreshUtc { get; init; }
    public GridViewModel Grid { get; init; } = new();
}

public sealed class GlobalParameterCreateRequest
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool Secure { get; set; }
}

public sealed class GlobalParameterDetailsUpdateRequest
{
    public string EditVersion { get; set; } = string.Empty;
    public string OriginalName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public bool Secure { get; set; }
    public bool PreserveExistingSecureValue { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class GlobalParameterMetadataUpdateRequest
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class GlobalParameterActionRequest
{
    public string Name { get; set; } = string.Empty;
}

public sealed class BulkGlobalParameterRequest
{
    public List<string> Names { get; set; } = new();
}

public sealed class HostsPageViewModel
{
    public int ImportRowLimit { get; init; } = 500;
    public string ActiveFilter { get; init; } = "all";
    public int UiHostRefreshIntervalSeconds { get; init; } = 10;
    public bool AutoFormatNewTaskHostNames { get; init; } = true;
    public DateTimeOffset? LastRefreshUtc { get; init; }
    public GridViewModel Grid { get; init; } = new();
}

public sealed class NativePortabilityDialogViewModel
{
    public string ObjectType { get; init; } = string.Empty;
    public string PluralLabel { get; init; } = string.Empty;
    public bool ShowLeaveTasksDisabled { get; init; }
    public bool ShowHostSpreadsheetImport { get; init; }
}

public sealed class NativePortabilityExportRequest
{
    public List<string> ItemIds { get; set; } = new();
    public string ProtectionMode { get; set; } = "server-key";
    public string Passphrase { get; set; } = string.Empty;
    public bool IncludeKeys { get; set; } = true;
}

public sealed class NativePortabilityImportPreflightRequest
{
    public IFormFile? File { get; set; }
    public string Passphrase { get; set; } = string.Empty;
}

public sealed class NativePortabilityImportRequest
{
    public IFormFile? File { get; set; }
    public string Passphrase { get; set; } = string.Empty;
    public string ResolutionsJson { get; set; } = "[]";
    public bool LeaveTasksDisabled { get; set; } = true;
    public bool AcknowledgeUnsupportedMetadata { get; set; }
}

public sealed class HostCreateRequest
{
    public string ResourceGroup { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string HostType { get; set; } = string.Empty;
    public string Unc { get; set; } = string.Empty;
}

public sealed class HostImportPreflightRequest
{
    public IFormFile? File { get; set; }
    public string Mode { get; set; } = "create";
}

public sealed class HostImportRequest
{
    public IFormFile? File { get; set; }
    public string Mode { get; set; } = "create";
    public bool AcknowledgeUnsupportedMetadata { get; set; }
}

public sealed class BulkHostRequest
{
    public List<string> HostIds { get; set; } = new();
    public bool DeleteWithoutRecycle { get; set; }
}

public sealed class HostActionRequest
{
    public string HostId { get; set; } = string.Empty;
    public string SnapshotId { get; set; } = string.Empty;
    public bool DeleteWithoutRecycle { get; set; }
}

public sealed class HostAssignedTaskStateRequest
{
    public string HostId { get; set; } = string.Empty;
    public bool Enabled { get; set; }
}

public sealed class HostDetailsUpdateRequest
{
    public string EditVersion { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool UpdateMoveItFields { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}


public sealed class KeysAndCertificatesPageViewModel
{
    public bool PgpLicensed { get; init; } = true;
    public int UiKeyCertificateRefreshIntervalSeconds { get; init; } = 60;
    public DateTimeOffset? LastRefreshUtc { get; init; }
    public GridViewModel Grid { get; init; } = new();
}

public sealed class SshKeyCreateRequest
{
    public string ResourceGroup { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string KeyType { get; set; } = "RSA";
    public int KeySize { get; set; } = 1024;
}

public sealed class TlsCertificateCreateRequest
{
    public string ResourceGroup { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string EmailAddress { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;
    public string OrganizationalUnit { get; set; } = string.Empty;
    public int ExpiresAfter { get; set; } = 2;
    public string ExpireUnit { get; set; } = "Years";
    public int KeySize { get; set; } = 1024;
}

public sealed class PgpKeyCreateRequest
{
    public string ResourceGroup { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string EmailAddress { get; set; } = string.Empty;
    public string KeyType { get; set; } = "RSA";
    public int KeyLength { get; set; } = 1024;
    public string HashAlgorithm { get; set; } = "SHA256";
    public int ExpiresAfter { get; set; } = 2;
    public string ExpireUnit { get; set; } = "Years";
    public string Password { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class KeyCertificateImportRequest
{
    public string ResourceGroup { get; set; } = string.Empty;
    public string Type { get; set; } = "SSH";
    public string Name { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public IFormFile? File { get; set; }
}

public sealed class KeyCertificateActionRequest
{
    public string RowId { get; set; } = string.Empty;
}

public sealed class BulkKeyCertificateRequest
{
    public List<string> RowIds { get; set; } = new();
}

public sealed class KeyCertificateMetadataUpdateRequest
{
    public string RowId { get; set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}



public sealed class ReportsPageViewModel
{
    public GridViewModel Grid { get; init; } = new();
}

public sealed class ReportPageViewModel
{
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ContextLabel { get; init; } = string.Empty;
    public string ClearFilterUrl { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
    public GridViewModel Grid { get; init; } = new();
    public string BatchUrl { get; init; } = string.Empty;
    public int BatchSize { get; init; } = 500;
    public int UiRefreshIntervalSeconds { get; init; } = 60;
    public int MaximumEntries { get; init; } = 10000;
    public int CurrentOffset { get; init; }
    public string CurrentCursor { get; init; } = string.Empty;
    public string NextCursor { get; init; } = string.Empty;
    public bool HasMore { get; init; }
    public int NextCount { get; init; }
    public string TaskId { get; init; } = string.Empty;
    public string TaskName { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
    public string RunNominalStart { get; init; } = string.Empty;
    public string TargetType { get; init; } = string.Empty;
    public string TargetId { get; init; } = string.Empty;
    public string TargetName { get; init; } = string.Empty;
    public string ReturnUrl { get; init; } = string.Empty;
    public string ReturnLabel { get; init; } = string.Empty;
    public string ReportPreset { get; init; } = string.Empty;
    public DateTimeOffset? LastRefreshUtc { get; init; }
}

public sealed class TaskLogViewerViewModel
{
    public string TaskId { get; init; } = string.Empty;
    public string TaskName { get; init; } = string.Empty;
    public long LogId { get; init; }
    public string ExitStatus { get; init; } = string.Empty;
    public string StartTime { get; init; } = string.Empty;
    public string LogPath { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
}

public sealed class TaskDetailsUpdateRequest
{
    public string EditVersion { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string OperatorNotes { get; set; } = string.Empty;
    public bool UpdateMoveItFields { get; set; }
    public int? FolderId { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}


public sealed class TaskCreateRequest
{
    public string Name { get; set; } = string.Empty;
    public string TaskType { get; set; } = "Traditional";
    public string ResourceGroup { get; set; } = string.Empty;
}

public sealed class TaskCloneRequest
{
    public string TaskId { get; set; } = string.Empty;
    public string NewTaskName { get; set; } = string.Empty;
}

public sealed class TaskImportPreflightRequest
{
    public IFormFile? File { get; set; }
    public string Mode { get; set; } = "create";
}

public sealed class TaskImportRequest
{
    public IFormFile? File { get; set; }
    public string Mode { get; set; } = "create";
    public bool LeaveDisabled { get; set; } = true;
    public bool AcknowledgeMissingDependencies { get; set; }
    public bool AcknowledgeUnsupportedMetadata { get; set; }
}

public sealed class BulkTaskRequest
{
    public List<string> TaskIds { get; set; } = new();
    public bool DeleteWithoutRecycle { get; set; }
}


public sealed class TaskNotificationRequest
{
    public string TaskId { get; set; } = string.Empty;
    public string Mode { get; set; } = "upsert";
    public int? ActionIndex { get; set; }
    public bool MatchExactResults { get; set; }
    public bool DoIfFailure { get; set; }
    public bool DoIfNoAction { get; set; }
    public bool DoIfSuccess { get; set; }
    public bool IsHtml { get; set; } = true;
    public string HostId { get; set; } = string.Empty;
    public string AddressTo { get; set; } = string.Empty;
    public bool PreserveExistingRecipients { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class TaskPreflightStageRequest
{
    public string TaskId { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
}

public sealed class TaskActionRequest
{
    public string TaskId { get; set; } = string.Empty;
    public string SnapshotId { get; set; } = string.Empty;
}

public sealed class TaskKillRequest
{
    public string TaskId { get; set; } = string.Empty;
    public List<string> NominalStarts { get; set; } = new();
}

public sealed class TaskMoveRequest
{
    public string TaskId { get; set; } = string.Empty;
    public int? FolderId { get; set; }
}

public sealed class TasksMoveRequest
{
    public List<string> TaskIds { get; set; } = new List<string>();
    public int? FolderId { get; set; }
}

public sealed class FolderCreateRequest
{
    public string Name { get; set; } = string.Empty;
    public int? ParentId { get; set; }
}

public sealed class FolderRenameRequest
{
    public int FolderId { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class FolderMoveRequest
{
    public int FolderId { get; set; }
    public int? ParentId { get; set; }
}

public sealed class FolderDeleteRequest
{
    public int FolderId { get; set; }
}
