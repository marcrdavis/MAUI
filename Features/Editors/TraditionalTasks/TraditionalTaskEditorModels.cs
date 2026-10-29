using System.Text.Json.Nodes;
// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

// RC2 editor contract for the MAUI-owned Traditional Task load/edit/save workflow.
// Host-specific write/browse support is advertised through the editor host-capability catalog; unverified patterns remain fail-closed.
public sealed record TraditionalTaskEditorTask(
    string Id,
    string Name,
    string Description,
    string OperatorNotes,
    string Type,
    bool Enabled,
    bool CanRun,
    bool CanAlter,
    string Scheduled,
    int StepCount,
    int ScheduleCount,
    int NextActionCount,
    IReadOnlyList<TraditionalTaskEditorStep> Steps,
    IReadOnlyList<TraditionalTaskEditorSchedule> Schedules,
    IReadOnlyList<TraditionalTaskEditorNextAction> NextActions,
    IReadOnlyList<TraditionalTaskEditorParameter> Parameters,
    TraditionalTaskEditorAdvancedSettings Advanced,
    SynchronizationTaskEditorSettings? Synchronization = null, JsonArray? AdvancedFlow = null,
    bool CanDelete = true, bool CanCreateTask = true, string ResourceGroups = "");

public sealed record SynchronizationTaskEditorSettings(
    IReadOnlyList<SynchronizationTaskEditorFolderSettings> Folders, int? SameSeconds);

public sealed record SynchronizationTaskEditorFolderSettings(
    string Id, int? From, int? To, int? Deletes, int? LowerCaseComparisons);


public sealed record TraditionalTaskEditorSchedule(
    int Index,
    int ServerIndex,
    string Id,
    int Multi,
    IReadOnlyList<string> DaysOfWeek,
    IReadOnlyList<int> DaysOfMonth,
    IReadOnlyList<TraditionalTaskEditorScheduleDateListReference> DateLists,
    IReadOnlyList<TraditionalTaskEditorScheduleInterval> Intervals,
    bool OnlyUntilFirstSuccess,
    bool FailIfNoSuccessInSchedule,
    bool RunEvenIfNotifications);

public sealed record TraditionalTaskEditorScheduleDateListReference(
    string Id,
    string Name,
    bool Run);

public sealed record TraditionalTaskEditorScheduleInterval(
    int ServerIndex,
    string Id,
    string StartTime,
    string EndTime,
    int EveryMinutes);

public sealed record TraditionalTaskEditorParameter(
    string Name,
    string Value,
    bool Secure);

public sealed record TraditionalTaskEditorStep(
    int Index,
    string Kind,
    string Id,
    string Title,
    string Subtitle,
    TraditionalTaskEditorSourceStep? Source,
    TraditionalTaskEditorProcessStep? Process,
    TraditionalTaskEditorDestinationStep? Destination);

public sealed record TraditionalTaskEditorSourceStep(
    string Id,
    string HostId,
    string HostName,
    string HostTypeCode,
    string HostType,
    string Path,
    bool UseDefaultBucket,
    string Bucket,
    bool UseDefaultContainer,
    string Container,
    bool UseDefaultDocumentLibrary,
    string DocumentLibrary,
    string DocumentLibraryGuid,
    string FileMask,
    string ExcludeFolders,
    bool SearchSubdirectories,
    string ExcludeFiles,
    bool HasSpecialFilters,
    TraditionalTaskEditorSpecialFilters? SpecialFilters,
    bool CollectOnlyNewFiles,
    bool ExpandZipFiles,
    bool RetryIfNoFilesFound,
    int DeleteOriginalAction,
    string RenameTo,
    bool RenamedFilesMayOverwrite,
    bool UseDefaultRetryCount,
    int? RetryCount,
    bool UseDefaultRetryTimeout,
    int? RetryTimeoutSeconds,
    bool UseDefaultRescanTime,
    int? RescanSeconds,
    bool UseDefaultMaxFiles,
    int? MaxFiles,
    bool UseDefaultMaxBytes,
    long? MaxBytes,
    bool UseDefaultWindowsCopyFileApi,
    bool? UseWindowsCopyFileApi,
    bool UseDefaultCredentials,
    string Username,
    bool PasswordConfigured,
    string Account,
    bool AccountConfigured,
    bool UseDefaultClientCertificate,
    string ClientCertificateSha1,
    string ClientCertificateStore,
    string ClientCertificateSubject,
    bool UseDefaultClientKey,
    string ClientKeyId,
    int? SharePointAuthenticationMode,
    string SharePointClientId,
    string SharePointTenantId,
    string SharePointRealmId,
    bool SharePointClientSecretConfigured,
    bool? GoogleUseOAuth,
    string GoogleProjectId,
    string GoogleOAuthClientId,
    bool GoogleOAuthClientSecretConfigured,
    bool? EmailUseOAuth,
    string EmailOAuthClientId,
    string EmailOAuthScope,
    string EmailOAuthTokenUrl,
    string EmailOAuthTenantId,
    bool EmailOAuthClientSecretConfigured,
    bool UseDefaultFileSorting,
    string Sort,
    bool UseDefaultConnectTimeout,
    int? ConnectTimeoutSeconds,
    bool UseDefaultDataTimeout,
    int? DataTimeoutSeconds,
    string AdditionalCommandsBefore,
    string AdditionalCommandsAfter,
    bool UseDefaultTransferType,
    string TransferType,
    bool UseDefaultPassive,
    bool? Passive,
    bool UseDefaultXSha1,
    bool? UseXSha1,
    bool UseDefaultBlindDownload,
    bool? BlindDownload,
    bool UseDefaultResume,
    bool? Resume,
    bool UseDefaultReuseTlsSession,
    bool? ReuseTlsSession,
    bool UseDefaultMd5,
    string Md5Mode,
    bool UseDefaultMd5FileName,
    string Md5FileName,
    bool UseDefaultDataIntegrity,
    string DataIntegrityRule,
    TraditionalTaskEditorHostDefaults HostDefaults,
    bool UseDefaultPreserveFileTimestamp = true, bool PreserveFileTimestamp = false);




public sealed record TraditionalTaskEditorSpecialFilters(
    string MatchMode,
    IReadOnlyList<TraditionalTaskEditorSpecialFilterCriterion> Filters,
    bool HasUnsupportedCriteria);

public sealed record TraditionalTaskEditorSpecialFilterCriterion(
    string Type,
    string Operator,
    string Value,
    string Unit);

public sealed record TraditionalTaskEditorProcessStep(
    string Id,
    string ScriptId,
    string ScriptName,
    string ScriptType,
    string Run,
    bool IsDestination,
    IReadOnlyList<TraditionalTaskEditorParameter> Parameters);

public sealed record TraditionalTaskEditorDestinationStep(
    string Id,
    string HostId,
    string HostName,
    string HostTypeCode,
    string HostType,
    string Path,
    bool UseDefaultBucket,
    string Bucket,
    bool UseDefaultContainer,
    string Container,
    bool UseDefaultDocumentLibrary,
    string DocumentLibrary,
    string DocumentLibraryGuid,
    string AddressTo,
    string Subject,
    string Message,
    bool IsHtml,
    bool UseRelativeSubdirectories,
    bool ForceDirectory,
    bool Zip,
    bool UseOriginalName,
    string FileName,
    int OverwriteAction,
    bool UseDefaultRetryCount,
    int? RetryCount,
    bool UseDefaultRetryTimeout,
    int? RetryTimeoutSeconds,
    bool UseDefaultWindowsCopyFileApi,
    bool? UseWindowsCopyFileApi,
    bool UseDefaultPreserveFileTimestamp,
    bool? PreserveFileTimestamp,
    bool UseDefaultCredentials,
    string Username,
    bool PasswordConfigured,
    string Account,
    bool AccountConfigured,
    bool UseDefaultClientCertificate,
    string ClientCertificateSha1,
    string ClientCertificateStore,
    string ClientCertificateSubject,
    bool UseDefaultClientKey,
    string ClientKeyId,
    int? SharePointAuthenticationMode,
    string SharePointClientId,
    string SharePointTenantId,
    string SharePointRealmId,
    bool SharePointClientSecretConfigured,
    bool? GoogleUseOAuth,
    string GoogleProjectId,
    string GoogleOAuthClientId,
    bool GoogleOAuthClientSecretConfigured,
    bool? EmailUseOAuth,
    string EmailOAuthClientId,
    string EmailOAuthScope,
    string EmailOAuthTokenUrl,
    string EmailOAuthTenantId,
    bool EmailOAuthClientSecretConfigured,
    bool UseDefaultConnectTimeout,
    int? ConnectTimeoutSeconds,
    bool UseDefaultDataTimeout,
    int? DataTimeoutSeconds,
    string AdditionalCommandsBefore,
    string AdditionalCommandsAfter,
    bool UseDefaultTransferType,
    string TransferType,
    bool UseDefaultPassive,
    bool? Passive,
    bool UseDefaultXSha1,
    bool? UseXSha1,
    bool UseDefaultBlindUpload,
    bool? BlindUpload,
    bool UseDefaultResume,
    bool? Resume,
    bool UseDefaultReuseTlsSession,
    bool? ReuseTlsSession,
    bool UseDefaultFileAttributes,
    bool? SetFileAttributes,
    string DestinationFileAttributes,
    TraditionalTaskEditorHostDefaults HostDefaults);

public sealed record TraditionalTaskEditorHostDefaults(
    int? RetryCount,
    int? RetryTimeoutSeconds,
    int? RescanSeconds,
    int? MaxFiles,
    long? MaxBytes,
    bool? UseWindowsCopyFileApi,
    bool? PreserveFileTimestamp,
    string Username,
    string ClientCertificateSha1,
    string ClientCertificateStore,
    string ClientCertificateSubject,
    string ClientKeyId,
    bool? SetFileAttributes,
    string DestinationFileAttributes,
    string Sort,
    int? ConnectTimeoutSeconds,
    int? DataTimeoutSeconds,
    string TransferType,
    bool? Passive,
    bool? UseXSha1,
    bool? BlindDownload,
    bool? BlindUpload,
    bool? Resume,
    bool? ReuseTlsSession,
    string Md5Mode,
    string Md5FileName,
    string Bucket,
    string Container,
    string DocumentLibrary,
    string DocumentLibraryGuid,
    int? SharePointAuthenticationMode,
    string SharePointClientId,
    string SharePointTenantId,
    string SharePointRealmId,
    bool? GoogleUseOAuth,
    string GoogleProjectId,
    string GoogleOAuthClientId,
    bool? EmailUseOAuth,
    string EmailOAuthClientId,
    string EmailOAuthScope,
    string EmailOAuthTokenUrl,
    string EmailOAuthTenantId,
    string DataIntegrityRule);

public sealed record TraditionalTaskEditorFolderBrowseResult(
    string HostId,
    string HostName,
    string HostType,
    string CurrentPath,
    string ParentPath,
    IReadOnlyList<TraditionalTaskEditorFolderBrowseItem> Items);

public sealed record TraditionalTaskEditorFolderBrowseItem(
    string Name,
    string Path,
    bool IsFolder,
    string Size,
    string LastModified,
    string Id = "");


public sealed record TraditionalTaskEditorHostCapability(
    string TypeCode,
    string Type,
    bool SupportsSource,
    bool SupportsDestination,
    bool SupportsSourceTimestamps,
    bool SourceWritable,
    bool DestinationWritable,
    bool SourceBrowsable,
    bool DestinationBrowsable,
    string SourceWriteProfile,
    string DestinationWriteProfile,
    string SourceBrowseProfile,
    string DestinationBrowseProfile,
    string SourceChangeHostProfile,
    string DestinationChangeHostProfile);

public sealed record TraditionalTaskEditorHostOption(
    string Id,
    string Name,
    string TypeCode,
    string Type,
    string Destination,
    TraditionalTaskEditorHostDefaults Defaults);

public sealed record TraditionalTaskEditorTlsCertificateOption(
    string Name,
    string Sha1Thumbprint,
    string Store,
    string ExpirationDate);

public sealed record TraditionalTaskEditorNamedOption(
    string Id,
    string Name,
    string Type,
    string Description = "");

public sealed record TraditionalTaskEditorNextAction(
    int Index,
    int ServerIndex,
    string Id,
    string Type,
    string DoAfter,
    bool DoIfFailure,
    bool DoIfNoAction,
    bool DoIfSuccess,
    bool IsHtml,
    string HostId,
    string HostName,
    string AddressTo,
    string Subject,
    string Message,
    string TaskId,
    string TaskName,
    IReadOnlyList<TraditionalTaskEditorParameter> Parameters, bool Wait = false);

public sealed record TraditionalTaskEditorNotificationTemplate(
    string Id,
    string Name,
    string Subject,
    string Message,
    string? Format = null);

public sealed record TraditionalTaskEditorMoveItDefinition(
    TraditionalTaskEditorTask Task,
    IReadOnlyList<TraditionalTaskEditorHostCapability> HostCapabilities,
    IReadOnlyList<TraditionalTaskEditorHostOption> SourceHosts,
    IReadOnlyList<TraditionalTaskEditorHostOption> DestinationHosts,
    IReadOnlyList<TraditionalTaskEditorNamedOption> ScriptOptions,
    IReadOnlyList<TraditionalTaskEditorNamedOption> SmtpHosts,
    IReadOnlyList<TraditionalTaskEditorNamedOption> TaskOptions,
    IReadOnlyList<TraditionalTaskEditorNamedOption> DateListOptions,
    IReadOnlyList<TraditionalTaskEditorTlsCertificateOption> TlsCertificates,
    IReadOnlyList<TraditionalTaskEditorNamedOption> SshClientKeys,
    string EditVersion = "");

public sealed record TraditionalTaskEditorAdvancedSettings(
    string CacheNames,
    bool UseDefaultStateCaching,
    int? StateCacheTime,
    string StateCacheUnit,
    bool AutoRerun,
    int? TaskLogsToKeep);

public sealed class TraditionalTaskEditorShellViewModel
{
    public bool CheckoutObjectsForDurationOfEdit { get; init; }
    public string EditVersion { get; init; } = string.Empty;
    public TraditionalTaskEditorTask Task { get; init; } = new(
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        "Traditional",
        false,
        false,
        false,
        string.Empty,
        0,
        0,
        0,
        Array.Empty<TraditionalTaskEditorStep>(),
        Array.Empty<TraditionalTaskEditorSchedule>(),
        Array.Empty<TraditionalTaskEditorNextAction>(),
        Array.Empty<TraditionalTaskEditorParameter>(),
        new TraditionalTaskEditorAdvancedSettings(string.Empty, true, null, string.Empty, false, null));

    public IReadOnlyList<TraditionalTaskEditorHostCapability> HostCapabilities { get; init; } = Array.Empty<TraditionalTaskEditorHostCapability>();
    public IReadOnlyList<TraditionalTaskEditorHostOption> SourceHosts { get; init; } = Array.Empty<TraditionalTaskEditorHostOption>();
    public IReadOnlyList<TraditionalTaskEditorHostOption> DestinationHosts { get; init; } = Array.Empty<TraditionalTaskEditorHostOption>();
    public IReadOnlyList<TraditionalTaskEditorNamedOption> ScriptOptions { get; init; } = Array.Empty<TraditionalTaskEditorNamedOption>();
    public IReadOnlyList<TraditionalTaskEditorNamedOption> SmtpHosts { get; init; } = Array.Empty<TraditionalTaskEditorNamedOption>();
    public IReadOnlyList<TraditionalTaskEditorNamedOption> TaskOptions { get; init; } = Array.Empty<TraditionalTaskEditorNamedOption>();
    public IReadOnlyList<TraditionalTaskEditorNamedOption> DateListOptions { get; init; } = Array.Empty<TraditionalTaskEditorNamedOption>();
    public IReadOnlyList<TraditionalTaskEditorTlsCertificateOption> TlsCertificates { get; init; } = Array.Empty<TraditionalTaskEditorTlsCertificateOption>();
    public IReadOnlyList<TraditionalTaskEditorNamedOption> SshClientKeys { get; init; } = Array.Empty<TraditionalTaskEditorNamedOption>();
    public IReadOnlyList<TraditionalTaskEditorNotificationTemplate> NotificationTemplates { get; init; } = Array.Empty<TraditionalTaskEditorNotificationTemplate>();
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public int? FolderId { get; init; }
    public IReadOnlyList<MetadataFieldDefinition> Fields { get; init; } = Array.Empty<MetadataFieldDefinition>();
    public IReadOnlyList<TaskFolderOption> Folders { get; init; } = Array.Empty<TaskFolderOption>();
    public TraditionalTaskEditorAudit Audit { get; init; } = new(null, string.Empty, null, string.Empty);
    public TraditionalTaskEditorPreferences Preferences { get; init; } = new(false);
}

public sealed record TraditionalTaskEditorAudit(
    DateTimeOffset? CreatedUtc,
    string CreatedBy,
    DateTimeOffset? ModifiedUtc,
    string ModifiedBy);

public sealed record TraditionalTaskEditorTransferException(
    string ExceptionId,
    string SourceId,
    string FullPath,
    long SizeBytes,
    string Timestamp,
    string Done,
    string SourceDone,
    int Virus,
    int ProcessingState);

public sealed record TraditionalTaskEditorSourceTimestamp(
    string HostId,
    string Path,
    string Mask,
    string Timestamp,
    string Dst,
    int Version,
    string UpdatedStamp);

public sealed record TraditionalTaskEditorSourceTimestampUpdateRequest(
    string HostId,
    string Path,
    string Mask,
    string Timestamp);

