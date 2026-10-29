// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

public sealed class ConfigurationSettingsViewModel
{
    public bool TaskJsonEditor { get; init; }
    public bool CopyBetweenEnvironmentsAvailable { get; init; }
    public bool CopyBetweenEnvironments { get; init; }
    public int CopyRecoveryRetentionDays { get; init; } = 30;
    public bool KeepAliveEnabled { get; init; } = true;
    public int KeepAliveIntervalMinutes { get; init; } = KeepAliveOptions.DefaultIntervalMinutes;
    public string ConfigurationRevision { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string SshEncryption { get; init; } = SshAlgorithmOptions.InitialEncryption;
    public string SshKeyExchange { get; init; } = SshAlgorithmOptions.InitialKeyExchange;
    public int IdleSessionTimeoutMinutes { get; init; }
    public IReadOnlyList<int> IdleSessionTimeoutChoices { get; init; } = Array.Empty<int>();
    public bool CheckoutObjectsForDurationOfEdit { get; init; }
    public bool AutoFormatNewTaskHostNames { get; init; } = true;
    public int ImportRowLimit { get; init; } = 500;
    public bool EnableQuickTransfer { get; init; }
    public bool TaskFailureAlertsEnabled { get; init; }
    public bool UseMauiTaskEditor { get; init; }
    public bool UseMauiHostEditor { get; init; }
    public bool UseMauiScriptEditor { get; init; }
    public bool UseMauiDateListEditor { get; init; }
    public bool UseMauiSystemSettingsEditor { get; init; }
    public bool UseMauiSystemSettingsEditorExperimental { get; init; }
    public bool UseMauiSystemSettingsEditorHidden { get; init; }
    public bool UseMauiGroupsAndPermissionsEditor { get; init; }
    public bool UseMauiGroupsAndPermissionsEditorExperimental { get; init; }
    public bool UseMauiGroupsAndPermissionsEditorHidden { get; init; }
    public bool UseMauiScriptEditorExperimental { get; init; }
    public bool UseMauiDateListEditorExperimental { get; init; }
    public bool UseMauiScriptEditorHidden { get; init; }
    public bool UseMauiDateListEditorHidden { get; init; }
    public bool UseMauiTaskEditorExperimental { get; init; }
    public bool UseMauiHostEditorExperimental { get; init; }
    public bool UseMauiTaskEditorHidden { get; init; }
    public bool UseMauiHostEditorHidden { get; init; }
    public bool RecycleBinEnabled { get; init; }
    public int RecycleBinRetentionDays { get; init; }
    public IReadOnlyList<int> RecycleBinRetentionChoices { get; init; } = Array.Empty<int>();
    public int LogRetentionDays { get; init; }
    public IReadOnlyList<int> LogRetentionChoices { get; init; } = Array.Empty<int>();
    public int JobHistoryRetentionDays { get; init; }
    public bool SystemMessageEnabled { get; init; }
    public string SystemMessageType { get; init; } = "Information";
    public string SystemMessageBody { get; init; } = string.Empty;
    public IReadOnlyList<string> SystemMessageTypeChoices { get; init; } = Array.Empty<string>();
    public IReadOnlyList<int> JobHistoryRetentionChoices { get; init; } = Array.Empty<int>();
    public bool BackupEnabled { get; init; }
    public int BackupFrequencyHours { get; init; }
    public IReadOnlyList<int> BackupFrequencyChoices { get; init; } = Array.Empty<int>();
    public int BackupRetentionDays { get; init; }
    public IReadOnlyList<int> BackupRetentionChoices { get; init; } = Array.Empty<int>();
    public int TaskRunHistoryMaxCount { get; init; }
    public int RequestTimeoutSeconds { get; init; }
    public bool AllowInvalidServerCertificates { get; init; }
    public bool IdleSessionTimeoutMinutesHidden { get; init; }
    public bool RecycleBinEnabledHidden { get; init; }
    public bool RecycleBinEnabledExperimental { get; init; }
    public bool RecycleBinRetentionDaysHidden { get; init; }
    public bool RecycleBinRetentionDaysExperimental { get; init; }
    public bool TaskRunHistoryMaxCountHidden { get; init; }
    public bool RequestTimeoutSecondsHidden { get; init; }
    public bool AllowInvalidServerCertificatesHidden { get; init; }
    public IReadOnlyList<ConfigurationServerViewModel> Servers { get; init; } = Array.Empty<ConfigurationServerViewModel>();
    public string DatabaseProvider { get; init; } = "SQLite";
    public string DatabaseConnectionString { get; init; } = string.Empty;
    public bool DatabaseProviderHidden { get; init; }
    public bool DatabaseConnectionStringHidden { get; init; }
    public int DatabaseSchemaVersion { get; init; }
    public int DatabaseSchemaTargetVersion { get; init; }
    public bool FitColumnsToGrid { get; init; }
    public bool HighlightFailedTasksInGrid { get; init; }
    public int DefaultRowSize { get; init; }
    public IReadOnlyList<int> DefaultRowSizeChoices { get; init; } = Array.Empty<int>();
    public bool SelectRowsBeyondGridSize { get; init; }
    public bool FitColumnsToGridHidden { get; init; }
    public bool HighlightFailedTasksInGridHidden { get; init; }
    public bool DefaultRowSizeHidden { get; init; }
    public bool SelectRowsBeyondGridSizeHidden { get; init; }
    public IReadOnlyList<ConfigurationRefreshViewModel> RefreshSettings { get; init; } = Array.Empty<ConfigurationRefreshViewModel>();
    public IReadOnlyList<ConfigurationReportViewModel> ReportSettings { get; init; } = Array.Empty<ConfigurationReportViewModel>();
    public string SmtpHost { get; init; } = string.Empty;
    public int SmtpPort { get; init; } = 25;
    public bool SmtpEnableSsl { get; init; }
    public bool SmtpIgnoreCertificateErrors { get; init; }
    public string SmtpUsername { get; init; } = string.Empty;
    public bool SmtpPasswordConfigured { get; init; }
    public string SmtpFromAddress { get; init; } = string.Empty;
    public string SmtpFromName { get; init; } = "MAUI Reports";
    public IReadOnlyList<ConfigurationMetadataResourceViewModel> MetadataResources { get; init; } = Array.Empty<ConfigurationMetadataResourceViewModel>();
    public IReadOnlyList<ConfigurationGridDefinitionViewModel> GridDefinitions { get; init; } = Array.Empty<ConfigurationGridDefinitionViewModel>();
    public IReadOnlyList<ConfigurationSecurityUserViewModel> SecurityUsers { get; init; } = Array.Empty<ConfigurationSecurityUserViewModel>();
    public string ApplicationLogo { get; init; } = string.Empty;
    public string AllowedHosts { get; init; } = string.Empty;
    public string DefaultLogLevel { get; init; } = string.Empty;
    public string AspNetLogLevel { get; init; } = string.Empty;
    public bool RestartRequired { get; init; }
    public IReadOnlyList<string> RestartReasons { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ConfigurationNavigationItemViewModel> NavigationSections { get; init; } = Array.Empty<ConfigurationNavigationItemViewModel>();
}

public sealed class DataAccessCredentialSaveRequest
{
    public string ServerKey { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    // Empty preserves the existing protected password when editing an already-configured server.
    public string Password { get; set; } = string.Empty;
}

public sealed class DataAccessServerRequest
{
    public string ServerKey { get; set; } = string.Empty;
}

public sealed class ConfigurationNavigationItemViewModel
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

public sealed class ConfigurationServerViewModel
{
    public string Key { get; init; } = string.Empty;
    public string EnvironmentKey { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string WebAdminUrl { get; init; } = string.Empty;
    public string ServerHost { get; init; } = string.Empty;
    public string DataAccessUsername { get; init; } = string.Empty;
    public bool DataAccessCredentialsConfigured { get; init; }
}

public sealed class ConfigurationRefreshViewModel
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public int CacheRefreshSeconds { get; init; }
    public int UiRefreshIntervalSeconds { get; init; }
    public bool CacheRefreshSecondsHidden { get; init; }
    public bool UiRefreshIntervalSecondsHidden { get; init; }
}



public sealed class ConfigurationReportViewModel
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public int BatchResultLimit { get; init; }
    public IReadOnlyList<int> BatchResultLimitChoices { get; init; } = Array.Empty<int>();
    public int UiRefreshIntervalSeconds { get; init; }
    public bool BatchResultLimitHidden { get; init; }
    public bool UiRefreshIntervalSecondsHidden { get; init; }
}

public sealed class ConfigurationMetadataResourceViewModel
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string ObjectType { get; init; } = string.Empty;
    public IReadOnlyList<string> AllowedTypes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<MetadataFieldDefinition> Fields { get; init; } = Array.Empty<MetadataFieldDefinition>();
}

public sealed class ConfigurationSecurityUserViewModel
{
    public string Username { get; init; } = string.Empty;
    public string Role { get; init; } = MauiSecurityRoles.User;
    public DateTimeOffset? LastLoginUtc { get; init; }
}

public sealed class ConfigurationGridDefinitionViewModel
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public IReadOnlyList<string> DefaultColumns { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AvailableColumns { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SearchableColumns { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SearchColumns { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AddActions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> BulkActions { get; init; } = Array.Empty<string>();
}

public sealed class SshAlgorithmSettingsSaveRequest
{
    public string? Encryption { get; set; }
    public string? KeyExchange { get; set; }
}

public sealed class ConfigurationSettingsSaveRequest
{
    public bool TaskJsonEditor { get; set; }
    public bool CopyBetweenEnvironments { get; set; }
    public int CopyRecoveryRetentionDays { get; set; } = 30;
    public bool KeepAliveEnabled { get; set; } = true;
    public int KeepAliveIntervalMinutes { get; set; } = KeepAliveOptions.DefaultIntervalMinutes;
    public SshAlgorithmSettingsSaveRequest? SshAlgorithms { get; set; }
    public string ConfigurationRevision { get; set; } = string.Empty;
    public int IdleSessionTimeoutMinutes { get; set; }
    public bool CheckoutObjectsForDurationOfEdit { get; set; }
    public bool AutoFormatNewTaskHostNames { get; set; } = true;
    public int ImportRowLimit { get; set; } = 500;
    public bool EnableQuickTransfer { get; set; }
    public bool TaskFailureAlertsEnabled { get; set; }
    public bool UseMauiTaskEditor { get; set; } = true;
    public bool UseMauiHostEditor { get; set; }
    public bool UseMauiScriptEditor { get; set; } = false;
    public bool UseMauiDateListEditor { get; set; } = false;
    public bool UseMauiSystemSettingsEditor { get; set; }
    public bool UseMauiGroupsAndPermissionsEditor { get; set; }
    public bool RecycleBinEnabled { get; set; }
    public int RecycleBinRetentionDays { get; set; }
    public int LogRetentionDays { get; set; }
    public int JobHistoryRetentionDays { get; set; }
    public bool SystemMessageEnabled { get; set; }
    public string SystemMessageType { get; set; } = "Information";
    public string SystemMessageBody { get; set; } = string.Empty;
    public bool BackupEnabled { get; set; }
    public int BackupFrequencyHours { get; set; }
    public int BackupRetentionDays { get; set; }
    public int TaskRunHistoryMaxCount { get; set; }
    public int RequestTimeoutSeconds { get; set; }
    public bool AllowInvalidServerCertificates { get; set; }
    public List<ConfigurationServerSaveRequest> Servers { get; set; } = new();
    public string DatabaseProvider { get; set; } = "SQLite";
    public string DatabaseConnectionString { get; set; } = string.Empty;
    public bool FitColumnsToGrid { get; set; }
    public bool HighlightFailedTasksInGrid { get; set; }
    public int DefaultRowSize { get; set; }
    public bool SelectRowsBeyondGridSize { get; set; }
    public List<ConfigurationRefreshSaveRequest> RefreshSettings { get; set; } = new();
    public List<ConfigurationReportSaveRequest> ReportSettings { get; set; } = new();
    // SMTP participates in the same Configuration > Settings save/dirty workflow as the rest of the page.
    // Password is write-only: blank retains the currently protected password when authenticated SMTP remains configured.
    public SmtpConfigurationSaveRequest Smtp { get; set; } = new();
    public List<ConfigurationMetadataResourceSaveRequest> MetadataResources { get; set; } = new();
    public List<ConfigurationGridDefinitionSaveRequest> GridDefinitions { get; set; } = new();
    public List<ConfigurationSecurityUserSaveRequest> SecurityUsers { get; set; } = new();
    public List<string> SecurityOriginalUsernames { get; set; } = new();
}

public sealed class ConfigurationSecurityUserSaveRequest
{
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = MauiSecurityRoles.User;
}

public sealed class ConfigurationServerSaveRequest
{
    public string Key { get; set; } = string.Empty;
    public string EnvironmentKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string WebAdminUrl { get; set; } = string.Empty;
    public string ServerHost { get; set; } = string.Empty;
}

public sealed class ConfigurationRefreshSaveRequest
{
    public string Key { get; set; } = string.Empty;
    public int CacheRefreshSeconds { get; set; }
    public int UiRefreshIntervalSeconds { get; set; }
}


public sealed class ConfigurationReportSaveRequest
{
    public string Key { get; set; } = string.Empty;
    public int BatchResultLimit { get; set; }
    public int UiRefreshIntervalSeconds { get; set; }
}

public sealed class ConfigurationMetadataResourceSaveRequest
{
    public string Key { get; set; } = string.Empty;
    public List<MetadataFieldDefinition> Fields { get; set; } = new();
}

public sealed class ConfigurationGridDefinitionSaveRequest
{
    public string Key { get; set; } = string.Empty;
    public List<string> DefaultColumns { get; set; } = new();
    public List<string> SearchColumns { get; set; } = new();
}

public sealed class ConfigurationSaveResult
{
    public string ConfigurationRevision { get; init; } = string.Empty;
    public bool RestartRequired { get; init; }
    public IReadOnlyList<string> RestartReasons { get; init; } = Array.Empty<string>();
    public bool SmtpPasswordConfigured { get; init; }
    public IReadOnlyList<string> ClearedDataAccessServerKeys { get; init; } = Array.Empty<string>();
}
