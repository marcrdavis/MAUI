// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Builds the Configuration > Settings view model from active MAUI configuration and runtime state.

using Microsoft.Extensions.Configuration;
using MoveItAutomationUI.Features.Editors;
using MoveItAutomationUI.Features.EnvironmentCopy;
using System.Text.Json;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.Security;

namespace MoveItAutomationUI.Services.Configuration;

public interface IConfigurationSettingsViewModelBuilder
{
    // Purpose: Builds the current editable Configuration > Settings model.
    ConfigurationSettingsViewModel Build();
}

public sealed class ConfigurationSettingsViewModelBuilder : IConfigurationSettingsViewModelBuilder
{
    private readonly IAppSettingsConfigurationSnapshotReader _snapshots;
    private readonly IAppSettingsDocumentStore _appSettings;
    private readonly IMauiSecurityService _securityService;
    private readonly MauiDatabaseSchemaState _databaseSchemaState;
    private readonly IConfigurationRestartDetector _restartDetector;

    // Purpose: Initializes the settings view-model builder with read-only configuration dependencies.
    public ConfigurationSettingsViewModelBuilder(
        IAppSettingsConfigurationSnapshotReader snapshots,
        IAppSettingsDocumentStore appSettings,
        IMauiSecurityService securityService,
        MauiDatabaseSchemaState databaseSchemaState,
        IConfigurationRestartDetector restartDetector)
    {
        _snapshots = snapshots;
        _appSettings = appSettings;
        _securityService = securityService;
        _databaseSchemaState = databaseSchemaState;
        _restartDetector = restartDetector;
    }

    // Purpose: Builds the current editable Configuration > Settings model.
    public ConfigurationSettingsViewModel Build()
    {
        // The shipped template supplies release defaults for editable settings that may not yet
        // exist in an installation-owned appsettings.json. The live file remains authoritative
        // wherever a setting/grid definition is already present.
        var snapshot = _snapshots.Read();
        var liveRoot = snapshot.Document;
        var configuration = snapshot.Configuration;
        var templateRoot = _appSettings.ReadTemplate();
        var releaseUi = DeserializeTemplateSection<UiOptions>(templateRoot, UiOptions.SectionName);
        var releaseMetadata = DeserializeTemplateSection<MetadataUiOptions>(templateRoot, MetadataUiOptions.SectionName);

        var ui = configuration.GetSection(UiOptions.SectionName).Get<UiOptions>() ?? new UiOptions();
        var moveIt = configuration.GetSection(MoveItOptions.SectionName).Get<MoveItOptions>() ?? new MoveItOptions();
        var database = configuration.GetSection(MetadataDatabaseOptions.SectionName).Get<MetadataDatabaseOptions>() ?? new MetadataDatabaseOptions();
        var grid = configuration.GetSection(GridBehaviorOptions.SectionName).Get<GridBehaviorOptions>() ?? new GridBehaviorOptions();
        var metadata = configuration.GetSection(MetadataUiOptions.SectionName).Get<MetadataUiOptions>() ?? new MetadataUiOptions();
        var recycleBin = configuration.GetSection(RecycleBinOptions.SectionName).Get<RecycleBinOptions>() ?? new RecycleBinOptions();
        var sshAlgorithms = configuration.GetSection(SshAlgorithmOptions.SectionName).Get<SshAlgorithmOptions>() ?? new SshAlgorithmOptions();
        var editors = configuration.GetSection(MauiEditorOptions.SectionName).Get<MauiEditorOptions>() ?? new MauiEditorOptions();
        var environmentCopy = configuration.GetSection(MoveItAutomationUI.Features.EnvironmentCopy.EnvironmentCopyOptions.SectionName)
            .Get<MoveItAutomationUI.Features.EnvironmentCopy.EnvironmentCopyOptions>() ?? new();
        var applicationLogs = configuration.GetSection(ApplicationLogOptions.SectionName).Get<ApplicationLogOptions>() ?? new ApplicationLogOptions();
        var jobHistory = configuration.GetSection(JobHistoryOptions.SectionName).Get<JobHistoryOptions>() ?? new JobHistoryOptions();
        var backup = configuration.GetSection(BackupOptions.SectionName).Get<BackupOptions>() ?? new BackupOptions();
        var systemMessage = configuration.GetSection(SystemMessageOptions.SectionName).Get<SystemMessageOptions>() ?? new SystemMessageOptions();
        var smtp = configuration.GetSection(SmtpOptions.SectionName).Get<SmtpOptions>() ?? new SmtpOptions();
        var recycleBinRetentionChoices = (recycleBin.RetentionDays.PossibleValues.Count > 0
                ? recycleBin.RetentionDays.PossibleValues
                : RecycleBinOptions.SupportedRetentionDays)
            .Where(value => value >= RecycleBinOptions.MinimumRetentionDays && value <= RecycleBinOptions.MaximumRetentionDays)
            .Append(recycleBin.RetentionDays.Value)
            .Where(value => value >= RecycleBinOptions.MinimumRetentionDays && value <= RecycleBinOptions.MaximumRetentionDays)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        var logRetentionChoices = (applicationLogs.RetentionDays.PossibleValues.Count > 0
                ? applicationLogs.RetentionDays.PossibleValues
                : ApplicationLogOptions.SupportedRetentionDays)
            .Where(value => value >= ApplicationLogOptions.MinimumRetentionDays && value <= ApplicationLogOptions.MaximumRetentionDays)
            .Append(applicationLogs.RetentionDays.Value)
            .Where(value => value >= ApplicationLogOptions.MinimumRetentionDays && value <= ApplicationLogOptions.MaximumRetentionDays)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        var jobHistoryRetentionChoices = jobHistory.RetentionDays.PossibleValues.Count > 0
            ? jobHistory.RetentionDays.PossibleValues.Distinct().ToArray()
            : JobHistoryOptions.SupportedRetentionDays.Distinct().ToArray();
        var backupFrequencyChoices = backup.FrequencyHours.PossibleValues.Count > 0
            ? backup.FrequencyHours.PossibleValues.Distinct().ToArray()
            : BackupOptions.SupportedFrequencyHours.Distinct().ToArray();
        var backupRetentionChoices = backup.RetentionDays.PossibleValues.Count > 0
            ? backup.RetentionDays.PossibleValues.Where(BackupOptions.SupportedRetentionDays.Contains).Distinct().OrderBy(value => value).ToArray()
            : BackupOptions.SupportedRetentionDays.ToArray();
        var idleSessionTimeoutChoices = moveIt.IdleSessionTimeoutMinutes.PossibleValues.Count > 0
            ? moveIt.IdleSessionTimeoutMinutes.PossibleValues.Distinct().ToArray()
            : MoveItOptions.SupportedIdleSessionTimeoutMinutes.Distinct().ToArray();
        var defaultRowSizeChoices = grid.DefaultRowSize.PossibleValues.Count > 0
            ? grid.DefaultRowSize.PossibleValues.Distinct().ToArray()
            : GridBehaviorOptions.SupportedDefaultRowSizes.Distinct().ToArray();

        var keepAlive = configuration.GetSection(KeepAliveOptions.SectionName).Get<KeepAliveOptions>() ?? new KeepAliveOptions();
        var restartReasons = _restartDetector.GetRestartReasons(moveIt, database);

        return new ConfigurationSettingsViewModel
        {
            Version = ui.Version,
            SshEncryption = sshAlgorithms.Encryption.Value,
            SshKeyExchange = sshAlgorithms.KeyExchange.Value,
            ConfigurationRevision = snapshot.Revision,
            TaskJsonEditor = ui.TaskJsonEditor.Value,
            UseMauiTaskEditor = editors.UseMauiTaskEditor.Value,
            CopyBetweenEnvironmentsAvailable = EnvironmentCopyAvailability.HasMultipleEnvironments(moveIt.Servers),
            CopyBetweenEnvironments = environmentCopy.Enabled.Value && EnvironmentCopyAvailability.HasMultipleEnvironments(moveIt.Servers),
            CopyRecoveryRetentionDays = environmentCopy.RecoveryRetentionDays.Value,
            KeepAliveEnabled = keepAlive.Enabled.Value,
            KeepAliveIntervalMinutes = (int)keepAlive.GetInterval().TotalMinutes,
            UseMauiHostEditor = editors.UseMauiHostEditor.Value,
            UseMauiScriptEditor = editors.UseMauiScriptEditor.Value,
            UseMauiDateListEditor = editors.UseMauiDateListEditor.Value,
            UseMauiSystemSettingsEditor = editors.UseMauiSystemSettingsEditor.Value,
            UseMauiSystemSettingsEditorExperimental = editors.UseMauiSystemSettingsEditor.Experimental,
            UseMauiSystemSettingsEditorHidden = editors.UseMauiSystemSettingsEditor.Hidden,
            UseMauiGroupsAndPermissionsEditor = editors.UseMauiGroupsAndPermissionsEditor.Value,
            UseMauiGroupsAndPermissionsEditorExperimental = editors.UseMauiGroupsAndPermissionsEditor.Experimental,
            UseMauiGroupsAndPermissionsEditorHidden = editors.UseMauiGroupsAndPermissionsEditor.Hidden,
            UseMauiTaskEditorExperimental = editors.UseMauiTaskEditor.Experimental,
            UseMauiHostEditorExperimental = editors.UseMauiHostEditor.Experimental,
            UseMauiScriptEditorExperimental = editors.UseMauiScriptEditor.Experimental,
            UseMauiDateListEditorExperimental = editors.UseMauiDateListEditor.Experimental,
            UseMauiTaskEditorHidden = editors.UseMauiTaskEditor.Hidden,
            UseMauiHostEditorHidden = editors.UseMauiHostEditor.Hidden,
            UseMauiScriptEditorHidden = editors.UseMauiScriptEditor.Hidden,
            UseMauiDateListEditorHidden = editors.UseMauiDateListEditor.Hidden,
            RecycleBinEnabled = recycleBin.Enabled.Value,
            RecycleBinRetentionDays = recycleBin.RetentionDays.Value,
            RecycleBinRetentionChoices = recycleBinRetentionChoices,
            LogRetentionDays = applicationLogs.RetentionDays.Value,
            LogRetentionChoices = logRetentionChoices,
            JobHistoryRetentionDays = jobHistory.RetentionDays.Value,
            JobHistoryRetentionChoices = jobHistoryRetentionChoices,
            SystemMessageEnabled = systemMessage.Enabled.Value,
            SystemMessageType = SystemMessageOptions.SupportedMessageTypes.Contains(systemMessage.MessageType.Value, StringComparer.OrdinalIgnoreCase) ? systemMessage.MessageType.Value : "Information",
            SystemMessageBody = systemMessage.MessageBody.Value ?? string.Empty,
            SystemMessageTypeChoices = SystemMessageOptions.SupportedMessageTypes,
            BackupEnabled = backup.Enabled.Value,
            BackupFrequencyHours = backup.FrequencyHours.Value,
            BackupFrequencyChoices = backupFrequencyChoices,
            BackupRetentionDays = BackupOptions.SupportedRetentionDays.Contains(backup.RetentionDays.Value) ? backup.RetentionDays.Value : BackupOptions.DefaultRetentionDays,
            BackupRetentionChoices = backupRetentionChoices,
            IdleSessionTimeoutMinutes = moveIt.IdleSessionTimeoutMinutes.Value,
            IdleSessionTimeoutChoices = idleSessionTimeoutChoices,
            CheckoutObjectsForDurationOfEdit = ui.CheckoutObjectsForDurationOfEdit.Value,
            AutoFormatNewTaskHostNames = ui.AutoFormatNewTaskHostNames.Value,
            EnableQuickTransfer = ui.EnableQuickTransfer.Value,
            ImportRowLimit = ui.ImportRowLimit.Value is >= 1 and <= 1000 ? ui.ImportRowLimit.Value : 500,
            TaskFailureAlertsEnabled = ui.TaskFailureAlertsEnabled.Value,
            TaskRunHistoryMaxCount = moveIt.TaskRunHistoryMaxCount.Value,
            RequestTimeoutSeconds = moveIt.RequestTimeoutSeconds.Value,
            AllowInvalidServerCertificates = moveIt.AllowInvalidServerCertificates.Value,
            IdleSessionTimeoutMinutesHidden = moveIt.IdleSessionTimeoutMinutes.Hidden,
            RecycleBinEnabledHidden = recycleBin.Enabled.Hidden,
            RecycleBinEnabledExperimental = recycleBin.Enabled.Experimental,
            RecycleBinRetentionDaysHidden = recycleBin.RetentionDays.Hidden,
            RecycleBinRetentionDaysExperimental = recycleBin.RetentionDays.Experimental,
            TaskRunHistoryMaxCountHidden = moveIt.TaskRunHistoryMaxCount.Hidden,
            RequestTimeoutSecondsHidden = moveIt.RequestTimeoutSeconds.Hidden,
            AllowInvalidServerCertificatesHidden = moveIt.AllowInvalidServerCertificates.Hidden,
            Servers = moveIt.Servers.Select(server => new ConfigurationServerViewModel
            {
                Key = server.Key,
                EnvironmentKey = string.IsNullOrWhiteSpace(server.EnvironmentKey) ? server.Key : server.EnvironmentKey,
                DisplayName = server.DisplayName,
                WebAdminUrl = server.WebAdminUrl,
                ServerHost = server.ServerHost,
                DataAccessUsername = server.DataAccessUsername?.Trim() ?? string.Empty,
                DataAccessCredentialsConfigured = !string.IsNullOrWhiteSpace(server.DataAccessUsername)
                    && !string.IsNullOrWhiteSpace(server.DataAccessProtectedPassword)
            }).ToArray(),
            DatabaseProvider = database.Provider.Value,
            DatabaseConnectionString = database.ConnectionString.Value,
            DatabaseProviderHidden = database.Provider.Hidden,
            DatabaseConnectionStringHidden = database.ConnectionString.Hidden,
            DatabaseSchemaVersion = _databaseSchemaState.CurrentVersion,
            DatabaseSchemaTargetVersion = _databaseSchemaState.TargetVersion,
            FitColumnsToGrid = grid.FitColumnsToGrid.Value,
            HighlightFailedTasksInGrid = grid.HighlightFailedTasksInGrid.Value,
            DefaultRowSize = grid.DefaultRowSize.Value,
            DefaultRowSizeChoices = defaultRowSizeChoices,
            SelectRowsBeyondGridSize = grid.SelectRowsBeyondGridSize.Value,
            FitColumnsToGridHidden = grid.FitColumnsToGrid.Hidden,
            HighlightFailedTasksInGridHidden = grid.HighlightFailedTasksInGrid.Hidden,
            DefaultRowSizeHidden = grid.DefaultRowSize.Hidden,
            SelectRowsBeyondGridSizeHidden = grid.SelectRowsBeyondGridSize.Hidden,
            RefreshSettings = BuildRefreshSettings(templateRoot, configuration),
            ReportSettings = BuildReportSettings(configuration),
            SmtpHost = smtp.Host?.Trim() ?? string.Empty,
            SmtpPort = smtp.Port,
            SmtpEnableSsl = smtp.EnableSsl,
            SmtpIgnoreCertificateErrors = smtp.IgnoreCertificateErrors,
            SmtpUsername = smtp.Username?.Trim() ?? string.Empty,
            SmtpPasswordConfigured = !string.IsNullOrWhiteSpace(smtp.ProtectedPassword),
            SmtpFromAddress = smtp.FromAddress?.Trim() ?? string.Empty,
            SmtpFromName = string.IsNullOrWhiteSpace(smtp.FromName) ? "MAUI Reports" : smtp.FromName.Trim(),
            MetadataResources = BuildMetadataResources(metadata, releaseMetadata, liveRoot),
            GridDefinitions = BuildGridDefinitions(ui, releaseUi, metadata, releaseMetadata, liveRoot),
            SecurityUsers = _securityService.GetUsers().Select(user => new ConfigurationSecurityUserViewModel
            {
                Username = user.Username,
                Role = user.Role,
                LastLoginUtc = user.LastLoginUtc
            }).ToArray(),
            ApplicationLogo = ui.ApplicationLogo,
            AllowedHosts = configuration["AllowedHosts"] ?? string.Empty,
            DefaultLogLevel = configuration["Logging:LogLevel:Default"] ?? string.Empty,
            AspNetLogLevel = configuration["Logging:LogLevel:Microsoft.AspNetCore"] ?? string.Empty,
            RestartRequired = restartReasons.Count > 0,
            RestartReasons = restartReasons,
            NavigationSections = BuildSettingsNavigation(ui.SettingsNavigationOrder)
        };
    }

    // Purpose: Builds refresh settings for resource workspaces. Missing live keys use the
    // release-defined defaults instead of falling through IConfiguration.GetValue<int>() to 0.
    private IReadOnlyList<ConfigurationRefreshViewModel> BuildRefreshSettings(JsonObject templateRoot, IConfiguration configuration)
    {
        return ConfigurationSettingsCatalog.RefreshSectionKeys.Select(key =>
        {
            var defaults = GetRefreshDefaults(key);
            var cacheValue = ReadTemplateSettingProperty(templateRoot, key, "CacheRefreshSeconds", "Value", defaults.Cache.Value);
            var uiValue = ReadTemplateSettingProperty(templateRoot, key, "UiRefreshIntervalSeconds", "Value", defaults.Ui.Value);
            var cacheHidden = ReadTemplateSettingProperty(templateRoot, key, "CacheRefreshSeconds", "Hidden", defaults.Cache.Hidden);
            var uiHidden = ReadTemplateSettingProperty(templateRoot, key, "UiRefreshIntervalSeconds", "Hidden", defaults.Ui.Hidden);

            return new ConfigurationRefreshViewModel
            {
                Key = key,
                Label = ConfigurationSettingsCatalog.RefreshLabels[key],
                CacheRefreshSeconds = configuration.GetValue<int?>($"{key}:CacheRefreshSeconds:Value") ?? cacheValue,
                UiRefreshIntervalSeconds = configuration.GetValue<int?>($"{key}:UiRefreshIntervalSeconds:Value") ?? uiValue,
                CacheRefreshSecondsHidden = configuration.GetValue<bool?>($"{key}:CacheRefreshSeconds:Hidden") ?? cacheHidden,
                UiRefreshIntervalSecondsHidden = configuration.GetValue<bool?>($"{key}:UiRefreshIntervalSeconds:Hidden") ?? uiHidden
            };
        }).ToArray();
    }

    // Purpose: Builds report retrieval and refresh settings.
    private IReadOnlyList<ConfigurationReportViewModel> BuildReportSettings(IConfiguration configuration)
    {
        var reports = configuration.GetSection(ReportBehaviorOptions.SectionName).Get<ReportBehaviorOptions>() ?? new ReportBehaviorOptions();
        var byKey = new Dictionary<string, ReportTypeBehaviorOptions>(StringComparer.OrdinalIgnoreCase)
        {
            ["TaskRun"] = reports.TaskRun,
            ["FileActivity"] = reports.FileActivity,
            ["Audit"] = reports.Audit
        };

        return ConfigurationSettingsCatalog.ReportSectionKeys.Select(key =>
        {
            var definition = byKey[key];
            var choices = (definition.BatchResultLimit.PossibleValues.Count > 0
                ? definition.BatchResultLimit.PossibleValues
                : ConfigurationSettingsCatalog.AllowedReportBatchSizes.OrderBy(value => value).ToList())
                .Distinct()
                .ToArray();
            return new ConfigurationReportViewModel
            {
                Key = key,
                Label = ConfigurationSettingsCatalog.ReportLabels[key],
                BatchResultLimit = definition.BatchResultLimit.Value,
                BatchResultLimitChoices = choices,
                UiRefreshIntervalSeconds = definition.UiRefreshIntervalSeconds.Value,
                BatchResultLimitHidden = definition.BatchResultLimit.Hidden,
                UiRefreshIntervalSecondsHidden = definition.UiRefreshIntervalSeconds.Hidden
            };
        }).ToArray();
    }

    // Purpose: Returns code-defined refresh defaults as a final fallback if a release template
    // property is unexpectedly absent.
    private static (SettingDefinition<int> Cache, SettingDefinition<int> Ui) GetRefreshDefaults(string key)
    {
        if (key.Equals(TaskBehaviorOptions.SectionName, StringComparison.OrdinalIgnoreCase))
        {
            var options = new TaskBehaviorOptions();
            return (options.CacheRefreshSeconds, options.UiRefreshIntervalSeconds);
        }
        if (key.Equals(HostBehaviorOptions.SectionName, StringComparison.OrdinalIgnoreCase))
        {
            var options = new HostBehaviorOptions();
            return (options.CacheRefreshSeconds, options.UiRefreshIntervalSeconds);
        }
        if (key.Equals(ScriptBehaviorOptions.SectionName, StringComparison.OrdinalIgnoreCase))
        {
            var options = new ScriptBehaviorOptions();
            return (options.CacheRefreshSeconds, options.UiRefreshIntervalSeconds);
        }
        if (key.Equals(DateListBehaviorOptions.SectionName, StringComparison.OrdinalIgnoreCase))
        {
            var options = new DateListBehaviorOptions();
            return (options.CacheRefreshSeconds, options.UiRefreshIntervalSeconds);
        }
        if (key.Equals(GlobalParameterBehaviorOptions.SectionName, StringComparison.OrdinalIgnoreCase))
        {
            var options = new GlobalParameterBehaviorOptions();
            return (options.CacheRefreshSeconds, options.UiRefreshIntervalSeconds);
        }
        if (key.Equals(KeyCertificateBehaviorOptions.SectionName, StringComparison.OrdinalIgnoreCase))
        {
            var options = new KeyCertificateBehaviorOptions();
            return (options.CacheRefreshSeconds, options.UiRefreshIntervalSeconds);
        }

        throw new InvalidOperationException($"Refresh configuration resource '{key}' is not supported.");
    }

    // Purpose: Reads one SettingDefinition property from the release template with a code default fallback.
    private static T ReadTemplateSettingProperty<T>(
        JsonObject templateRoot,
        string sectionName,
        string settingName,
        string propertyName,
        T fallback)
    {
        if (templateRoot[sectionName] is not JsonObject section
            || section[settingName] is not JsonObject setting
            || setting[propertyName] is not JsonNode node)
        {
            return fallback;
        }

        try { return node.GetValue<T>(); }
        catch (InvalidOperationException) { return fallback; }
        catch (FormatException) { return fallback; }
    }

    // Purpose: Deserializes one release-template section into the same options model used for live configuration.
    private static T DeserializeTemplateSection<T>(JsonObject templateRoot, string sectionName) where T : new()
    {
        if (templateRoot[sectionName] is not JsonObject section) return new T();
        return JsonSerializer.Deserialize<T>(section.ToJsonString(), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new T();
    }

    // Purpose: Builds ordered Configuration > Settings navigation sections.
    private static IReadOnlyList<ConfigurationNavigationItemViewModel> BuildSettingsNavigation(IEnumerable<string>? configuredOrder)
    {
        var orderedKeys = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawKey in configuredOrder ?? Array.Empty<string>())
        {
            var key = rawKey?.Trim();
            if (string.IsNullOrWhiteSpace(key) || !ConfigurationSettingsCatalog.SettingsNavigationLabels.ContainsKey(key) || !seen.Add(key))
            {
                continue;
            }

            orderedKeys.Add(key);
        }

        // Insert release-defined sections that are missing from an older installation-owned appsettings.json
        // near their default neighbors instead of dumping every new section at the bottom. Existing configured
        // ordering remains authoritative for sections already present.
        var defaults = ConfigurationSettingsCatalog.DefaultSettingsNavigationOrder;
        for (var defaultIndex = 0; defaultIndex < defaults.Length; defaultIndex++)
        {
            var key = defaults[defaultIndex];
            if (!seen.Add(key)) continue;

            var insertAt = orderedKeys.Count;
            for (var predecessorIndex = defaultIndex - 1; predecessorIndex >= 0; predecessorIndex--)
            {
                var predecessorPosition = orderedKeys.FindIndex(item => item.Equals(defaults[predecessorIndex], StringComparison.OrdinalIgnoreCase));
                if (predecessorPosition < 0) continue;
                insertAt = predecessorPosition + 1;
                while (insertAt < orderedKeys.Count)
                {
                    var existingDefaultIndex = Array.FindIndex(defaults, item => item.Equals(orderedKeys[insertAt], StringComparison.OrdinalIgnoreCase));
                    if (existingDefaultIndex > defaultIndex) break;
                    insertAt++;
                }
                break;
            }
            orderedKeys.Insert(insertAt, key);
        }

        return orderedKeys
            .Select(key => new ConfigurationNavigationItemViewModel
            {
                Key = key,
                Label = ConfigurationSettingsCatalog.SettingsNavigationLabels[key]
            })
            .ToArray();
    }

    // Purpose: Builds editable grid definitions and metadata-derived columns.
    private static IReadOnlyList<ConfigurationGridDefinitionViewModel> BuildGridDefinitions(UiOptions ui, UiOptions releaseUi, MetadataUiOptions metadata, MetadataUiOptions releaseMetadata, JsonObject liveRoot)
    {
        // Start with the release grid definitions so a grid introduced after an installation's
        // appsettings.json was created still appears in Settings. Overlay every live definition
        // so installation-owned customizations remain authoritative. Unknown live grids are
        // preserved in appsettings.json but are not emitted into this fixed Settings contract.
        var grids = new Dictionary<string, GridOptions>(releaseUi.Grids, StringComparer.OrdinalIgnoreCase);
        var liveUi = ReadOptionalObject(liveRoot, UiOptions.SectionName);
        var liveGrids = liveUi is null ? null : ReadOptionalObject(liveUi, "Grids", $"{UiOptions.SectionName}:Grids");
        if (liveGrids is not null)
        {
            foreach (var key in ConfigurationSettingsCatalog.GridSectionKeys)
            {
                if (!liveGrids.TryGetPropertyValue(key, out var liveGridNode) || liveGridNode is null) continue;
                if (liveGridNode is not JsonObject)
                    throw new InvalidOperationException($"Configuration property '{UiOptions.SectionName}:Grids:{key}' exists but is not a JSON object.");
                if (ui.Grids.TryGetValue(key, out var configured)) grids[key] = configured;
            }
        }

        return ConfigurationSettingsCatalog.GridSectionKeys
            .Select(key =>
            {
                if (!grids.TryGetValue(key, out var options))
                {
                    throw new InvalidOperationException($"Grid definition '{key}' is missing from both appsettings.json and appsettings.template.json.");
                }

                return new KeyValuePair<string, GridOptions>(key, options);
            })
            .Select(pair =>
            {
                var hasMetadataDefinition = MetadataResourceCatalog.TryGetByGridKey(pair.Key, out var metadataResource);
                var definition = hasMetadataDefinition ? GetEffectiveMetadataDefinition(metadata, releaseMetadata, liveRoot, metadataResource) : null;
                var metadataColumns = definition is not null
                    ? definition.Fields.Where(field => !string.IsNullOrWhiteSpace(field.Name)).Select(field => $"Metadata_{field.Name.Trim()}")
                    : Enumerable.Empty<string>();
                var systemColumns = pair.Key.Equals("hosts", StringComparison.OrdinalIgnoreCase)
                    ? new[] { "Health", "LastHealthCheck", "TestResults" }
                    : Array.Empty<string>();
                IEnumerable<string> metadataAuditColumns = hasMetadataDefinition
                    ? MetadataGridSupport.AuditColumnKeys
                    : Array.Empty<string>();
                var available = pair.Value.AvailableColumns
                    .Concat(systemColumns)
                    .Concat(metadataColumns)
                    .Concat(metadataAuditColumns)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var defaultColumns = pair.Value.DefaultColumns.ToList();
                if (pair.Key.Equals("hosts", StringComparison.OrdinalIgnoreCase))
                {
                    defaultColumns.RemoveAll(column => column.Equals("Health", StringComparison.OrdinalIgnoreCase));
                    defaultColumns.Add("Health");
                }

                var bulkActions = pair.Value.BulkActions.ToList();
                if (pair.Key.Equals("hosts", StringComparison.OrdinalIgnoreCase)
                    && !bulkActions.Contains("Test Hosts", StringComparer.OrdinalIgnoreCase))
                {
                    bulkActions.Insert(0, "Test Hosts");
                }

                return new ConfigurationGridDefinitionViewModel
                {
                    Key = pair.Key,
                    Label = ConfigurationSettingsCatalog.GridLabels.TryGetValue(pair.Key, out var label) ? label : pair.Key,
                    DefaultColumns = defaultColumns,
                    AvailableColumns = available,
                    SearchableColumns = pair.Value.AvailableColumns.Concat(systemColumns).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    SearchColumns = pair.Value.SearchColumns.Concat(pair.Key.Equals("hosts", StringComparison.OrdinalIgnoreCase) ? new[] { "Health", "TestResults" } : Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    AddActions = pair.Value.AddActions,
                    BulkActions = bulkActions
                };
            }).ToArray();
    }

    // Purpose: Builds metadata-resource definitions for Configuration > Settings. A resource
    // missing from an older live file is surfaced with the release-template defaults; an existing
    // resource, including administrator customizations, remains authoritative.
    private static IReadOnlyList<ConfigurationMetadataResourceViewModel> BuildMetadataResources(
        MetadataUiOptions metadata,
        MetadataUiOptions releaseMetadata,
        JsonObject liveRoot)
        => MetadataResourceCatalog.Resources.Select(resource =>
        {
            var definition = GetEffectiveMetadataDefinition(metadata, releaseMetadata, liveRoot, resource);
            return new ConfigurationMetadataResourceViewModel
            {
                Key = resource.ConfigurationKey,
                Label = resource.Label,
                ObjectType = resource.ObjectType,
                AllowedTypes = resource.AllowedFieldTypes
                    .Where(type => !type.Equals("Tags", StringComparison.OrdinalIgnoreCase))
                    .ToArray(),
                Fields = definition.Fields
            };
        }).ToArray();

    // Purpose: Resolves one metadata resource from the live file when its Fields array exists;
    // otherwise uses the release template so older installations expose the current defaults.
    private static ObjectMetadataDefinition GetEffectiveMetadataDefinition(
        MetadataUiOptions metadata,
        MetadataUiOptions releaseMetadata,
        JsonObject liveRoot,
        MetadataResourceDescriptor resource)
    {
        var liveMetadata = ReadOptionalObject(liveRoot, MetadataUiOptions.SectionName);
        if (liveMetadata is null
            || !liveMetadata.TryGetPropertyValue(resource.ConfigurationKey, out var liveResourceNode)
            || liveResourceNode is null)
        {
            return resource.GetDefinition(releaseMetadata);
        }

        if (liveResourceNode is not JsonObject liveResource)
            throw new InvalidOperationException($"Configuration property '{MetadataUiOptions.SectionName}:{resource.ConfigurationKey}' exists but is not a JSON object.");

        if (!liveResource.TryGetPropertyValue("Fields", out var fieldsNode) || fieldsNode is null)
            return resource.GetDefinition(releaseMetadata);

        if (fieldsNode is not JsonArray)
            throw new InvalidOperationException($"Configuration property '{MetadataUiOptions.SectionName}:{resource.ConfigurationKey}:Fields' exists but is not a JSON array.");

        return resource.GetDefinition(metadata);
    }

    // Purpose: Reads an optional configuration object while distinguishing an absent node from
    // an existing malformed node.
    private static JsonObject? ReadOptionalObject(JsonObject parent, string propertyName, string? configurationPath = null)
    {
        if (!parent.TryGetPropertyValue(propertyName, out var node) || node is null) return null;
        return node as JsonObject
            ?? throw new InvalidOperationException($"Configuration property '{configurationPath ?? propertyName}' exists but is not a JSON object.");
    }
}
