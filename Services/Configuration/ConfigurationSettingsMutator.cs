// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Applies validated Configuration > Settings changes to the installation-owned appsettings document.

using System.Text.Json;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Features.Editors;
using MoveItAutomationUI.Features.EnvironmentCopy;
using MoveItAutomationUI.Services.Reporting;

namespace MoveItAutomationUI.Services.Configuration;

public sealed class ConfigurationMutationResult
{
    public MoveItOptions MoveIt { get; init; } = new();
    public MetadataDatabaseOptions Database { get; init; } = new();
    public IReadOnlyList<string> ClearedDataAccessServerKeys { get; init; } = Array.Empty<string>();
}

public interface IConfigurationSettingsMutator
{
    // Purpose: Applies a validated settings request to an appsettings JSON document.
    ConfigurationMutationResult Apply(JsonObject root, ConfigurationSettingsSaveRequest request);
}

public sealed class ConfigurationSettingsMutator : IConfigurationSettingsMutator
{
    private readonly ISmtpSecretProtector _smtpSecretProtector;
    private readonly IAppSettingsDocumentStore _appSettings;

    public ConfigurationSettingsMutator(
        ISmtpSecretProtector smtpSecretProtector,
        IAppSettingsDocumentStore appSettings)
    {
        _smtpSecretProtector = smtpSecretProtector;
        _appSettings = appSettings;
    }

    // Purpose: Applies a validated settings request to an appsettings JSON document.
    public ConfigurationMutationResult Apply(JsonObject root, ConfigurationSettingsSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(request);

        // The release template is the schema/default source for Settings that may not yet exist
        // in an installation-owned appsettings.json. Missing sections/settings are cloned from
        // the template before the administrator-selected Value is applied.
        var templateRoot = _appSettings.ReadTemplate();
        AppSettingsJson.SetSettingValue(root, KeepAliveOptions.SectionName, nameof(KeepAliveOptions.Enabled), request.KeepAliveEnabled, templateRoot);
        AppSettingsJson.SetSettingValue(root, KeepAliveOptions.SectionName, nameof(KeepAliveOptions.IntervalMinutes), request.KeepAliveIntervalMinutes, templateRoot);
        AppSettingsJson.SetSettingValue(root, "EnvironmentCopy", "RecoveryRetentionDays", request.CopyRecoveryRetentionDays, templateRoot);
        if (request.SshAlgorithms?.Encryption is not null)
            AppSettingsJson.SetSettingValue(root, SshAlgorithmOptions.SectionName, nameof(SshAlgorithmOptions.Encryption), request.SshAlgorithms.Encryption, templateRoot);
        if (request.SshAlgorithms?.KeyExchange is not null)
            AppSettingsJson.SetSettingValue(root, SshAlgorithmOptions.SectionName, nameof(SshAlgorithmOptions.KeyExchange), request.SshAlgorithms.KeyExchange, templateRoot);

        AppSettingsJson.SetSettingValue(root, MoveItOptions.SectionName, nameof(MoveItOptions.IdleSessionTimeoutMinutes), request.IdleSessionTimeoutMinutes, templateRoot);
        AppSettingsJson.SetSettingValue(root, UiOptions.SectionName, nameof(UiOptions.CheckoutObjectsForDurationOfEdit), request.CheckoutObjectsForDurationOfEdit, templateRoot);
        AppSettingsJson.SetSettingValue(root, UiOptions.SectionName, nameof(UiOptions.AutoFormatNewTaskHostNames), request.AutoFormatNewTaskHostNames, templateRoot);
        AppSettingsJson.SetSettingValue(root, UiOptions.SectionName, nameof(UiOptions.ImportRowLimit), request.ImportRowLimit, templateRoot);
        AppSettingsJson.SetSettingValue(root, UiOptions.SectionName, nameof(UiOptions.TaskJsonEditor), request.TaskJsonEditor, templateRoot);
        AppSettingsJson.SetSettingValue(root, UiOptions.SectionName, nameof(UiOptions.EnableQuickTransfer), request.EnableQuickTransfer, templateRoot);
        AppSettingsJson.SetSettingValue(root, MauiEditorOptions.SectionName, nameof(MauiEditorOptions.UseMauiTaskEditor), request.UseMauiTaskEditor, templateRoot);
        AppSettingsJson.SetSettingValue(root, MauiEditorOptions.SectionName, nameof(MauiEditorOptions.UseMauiHostEditor), request.UseMauiHostEditor, templateRoot);
        AppSettingsJson.SetSettingValue(root, MauiEditorOptions.SectionName, nameof(MauiEditorOptions.UseMauiScriptEditor), request.UseMauiScriptEditor, templateRoot);
        AppSettingsJson.SetSettingValue(root, MauiEditorOptions.SectionName, nameof(MauiEditorOptions.UseMauiDateListEditor), request.UseMauiDateListEditor, templateRoot);
        AppSettingsJson.SetSettingValue(root, MauiEditorOptions.SectionName, nameof(MauiEditorOptions.UseMauiGroupsAndPermissionsEditor), request.UseMauiGroupsAndPermissionsEditor, templateRoot);
        AppSettingsJson.SetSettingValue(root, MauiEditorOptions.SectionName, nameof(MauiEditorOptions.UseMauiSystemSettingsEditor), request.UseMauiSystemSettingsEditor, templateRoot);
        AppSettingsJson.SetSettingValue(root, UiOptions.SectionName, nameof(UiOptions.TaskFailureAlertsEnabled), request.TaskFailureAlertsEnabled, templateRoot);
        AppSettingsJson.SetSettingValue(root, "RecycleBin", "Enabled", request.RecycleBinEnabled, templateRoot);
        AppSettingsJson.SetSettingValue(root, "RecycleBin", "RetentionDays", request.RecycleBinRetentionDays, templateRoot);
        AppSettingsJson.SetSettingValue(root, ApplicationLogOptions.SectionName, nameof(ApplicationLogOptions.RetentionDays), request.LogRetentionDays, templateRoot);
        AppSettingsJson.SetSettingValue(root, JobHistoryOptions.SectionName, nameof(JobHistoryOptions.RetentionDays), request.JobHistoryRetentionDays, templateRoot);
        AppSettingsJson.SetSettingValue(root, SystemMessageOptions.SectionName, nameof(SystemMessageOptions.Enabled), request.SystemMessageEnabled, templateRoot);
        AppSettingsJson.SetSettingValue(root, SystemMessageOptions.SectionName, nameof(SystemMessageOptions.MessageType), request.SystemMessageType, templateRoot);
        AppSettingsJson.SetSettingValue(root, SystemMessageOptions.SectionName, nameof(SystemMessageOptions.MessageBody), request.SystemMessageBody, templateRoot);
        AppSettingsJson.SetSettingValue(root, BackupOptions.SectionName, nameof(BackupOptions.Enabled), request.BackupEnabled, templateRoot);
        AppSettingsJson.SetSettingValue(root, BackupOptions.SectionName, nameof(BackupOptions.FrequencyHours), request.BackupFrequencyHours, templateRoot);
        AppSettingsJson.SetSettingValue(root, BackupOptions.SectionName, nameof(BackupOptions.RetentionDays), request.BackupRetentionDays, templateRoot);
        AppSettingsJson.SetSettingValue(root, "MoveIt", "TaskRunHistoryMaxCount", request.TaskRunHistoryMaxCount, templateRoot);
        AppSettingsJson.SetSettingValue(root, "MoveIt", "RequestTimeoutSeconds", request.RequestTimeoutSeconds, templateRoot);
        AppSettingsJson.SetSettingValue(root, "MoveIt", "AllowInvalidServerCertificates", request.AllowInvalidServerCertificates, templateRoot);

        // Preserve the live per-server Data Access Credentials while replacing editable server topology.
        // Credentials are managed independently from the normal Settings save so they never round-trip
        // through the browser and are not accidentally cleared by unrelated connection edits.
        var existingServers = ReadExistingServers(root)
            .ToDictionary(server => server.Key, StringComparer.OrdinalIgnoreCase);
        var clearedDataAccessServerKeys = new List<string>();
        var normalizedServers = request.Servers.Select(server =>
        {
            existingServers.TryGetValue(server.Key.Trim(), out var existing);
            // Passwords may never follow a server Key to a different MOVEit endpoint/environment.
            // Preserve credentials for display-name changes, but require re-entry when the
            // underlying REST/COM destination or metadata scope changes.
            var preserveCredentials = existing is not null && SameConnectionIdentity(existing, server);
            if (!preserveCredentials && existing is not null &&
                (!string.IsNullOrWhiteSpace(existing.DataAccessUsername) || !string.IsNullOrWhiteSpace(existing.DataAccessProtectedPassword)))
                clearedDataAccessServerKeys.Add(server.Key.Trim());
            return new MoveItServerDefinition
            {
                Key = server.Key.Trim(),
                EnvironmentKey = server.EnvironmentKey.Trim().ToUpperInvariant(),
                DisplayName = server.DisplayName.Trim(),
                WebAdminUrl = server.WebAdminUrl.Trim().TrimEnd('/'),
                ServerHost = server.ServerHost.Trim(),
                DataAccessUsername = preserveCredentials ? existing?.DataAccessUsername : null,
                DataAccessProtectedPassword = preserveCredentials ? existing?.DataAccessProtectedPassword : null
            };
        }).ToArray();

        // Persist the effective value, including when connections are removed in the same save.
        AppSettingsJson.SetSettingValue(root, "EnvironmentCopy", "Enabled",
            request.CopyBetweenEnvironments && EnvironmentCopyAvailability.HasMultipleEnvironments(normalizedServers), templateRoot);
        AppSettingsJson.GetOrCreateSection(root, MoveItOptions.SectionName, templateRoot)[nameof(MoveItOptions.Servers)] =
            JsonSerializer.SerializeToNode(normalizedServers);

        AppSettingsJson.SetSettingValue(root, "MetadataDatabase", "Provider", request.DatabaseProvider.Trim(), templateRoot);
        AppSettingsJson.SetSettingValue(root, "MetadataDatabase", "ConnectionString", request.DatabaseConnectionString.Trim(), templateRoot);
        AppSettingsJson.SetSettingValue(root, "Grid", "FitColumnsToGrid", request.FitColumnsToGrid, templateRoot);
        AppSettingsJson.SetSettingValue(root, "Grid", "HighlightFailedTasksInGrid", request.HighlightFailedTasksInGrid, templateRoot);
        AppSettingsJson.SetSettingValue(root, "Grid", "DefaultRowSize", request.DefaultRowSize, templateRoot);
        AppSettingsJson.SetSettingValue(root, "Grid", "SelectRowsBeyondGridSize", request.SelectRowsBeyondGridSize, templateRoot);

        foreach (var refresh in request.RefreshSettings)
        {
            AppSettingsJson.SetSettingValue(root, refresh.Key, "CacheRefreshSeconds", refresh.CacheRefreshSeconds, templateRoot);
            AppSettingsJson.SetSettingValue(root, refresh.Key, "UiRefreshIntervalSeconds", refresh.UiRefreshIntervalSeconds, templateRoot);
        }

        foreach (var report in request.ReportSettings)
        {
            AppSettingsJson.SetNestedSettingValue(root, ReportBehaviorOptions.SectionName, report.Key, "BatchResultLimit", report.BatchResultLimit, templateRoot);
            AppSettingsJson.SetNestedSettingValue(root, ReportBehaviorOptions.SectionName, report.Key, "UiRefreshIntervalSeconds", report.UiRefreshIntervalSeconds, templateRoot);
        }

        ApplySmtp(root, request.Smtp, templateRoot);

        var templateMetadata = templateRoot[MetadataUiOptions.SectionName] as JsonObject;
        var metadataObject = AppSettingsJson.GetOrCreateSection(root, MetadataUiOptions.SectionName, templateRoot);
        foreach (var resource in request.MetadataResources)
        {
            var resourceObject = AppSettingsJson.GetOrCreateObject(
                metadataObject,
                resource.Key,
                $"{MetadataUiOptions.SectionName}:{resource.Key}",
                templateMetadata);
            resourceObject["Fields"] = JsonSerializer.SerializeToNode(resource.Fields.Select(NormalizeMetadataField).ToArray());
        }

        var templateUi = templateRoot[UiOptions.SectionName] as JsonObject;
        var uiObject = AppSettingsJson.GetOrCreateSection(root, UiOptions.SectionName, templateRoot);
        var gridsObject = AppSettingsJson.GetOrCreateObject(uiObject, "Grids", $"{UiOptions.SectionName}:Grids", templateUi);
        var templateGrids = templateUi?["Grids"] as JsonObject;
        foreach (var definition in request.GridDefinitions)
        {
            var gridObject = AppSettingsJson.GetOrCreateObject(
                gridsObject,
                definition.Key,
                $"{UiOptions.SectionName}:Grids:{definition.Key}",
                templateGrids);
            AppSettingsJson.MergeMissingProperties(gridObject, templateGrids?[definition.Key] as JsonObject);

            gridObject["DefaultColumns"] = JsonSerializer.SerializeToNode(definition.DefaultColumns);
            gridObject["SearchColumns"] = JsonSerializer.SerializeToNode(definition.SearchColumns);
        }

        return new ConfigurationMutationResult
        {
            ClearedDataAccessServerKeys = clearedDataAccessServerKeys,
            MoveIt = new MoveItOptions
            {
                RequestTimeoutSeconds = new SettingDefinition<int> { Value = request.RequestTimeoutSeconds },
                IdleSessionTimeoutMinutes = new SettingDefinition<int> { Value = request.IdleSessionTimeoutMinutes },
                TaskRunHistoryMaxCount = new SettingDefinition<int> { Value = request.TaskRunHistoryMaxCount },
                AllowInvalidServerCertificates = new SettingDefinition<bool> { Value = request.AllowInvalidServerCertificates },
                Servers = normalizedServers.ToList()
            },
            Database = new MetadataDatabaseOptions
            {
                Provider = new SettingDefinition<string> { Value = request.DatabaseProvider.Trim() },
                ConnectionString = new SettingDefinition<string> { Value = request.DatabaseConnectionString.Trim() }
            }
        };
    }

    // Purpose: Persists SMTP in the common Settings document while preserving the write-only protected password contract.
    private void ApplySmtp(JsonObject root, SmtpConfigurationSaveRequest request, JsonObject templateRoot)
    {
        ArgumentNullException.ThrowIfNull(request);
        var smtp = AppSettingsJson.GetOrCreateSection(root, SmtpOptions.SectionName, templateRoot);

        var existingProtectedPassword = smtp[nameof(SmtpOptions.ProtectedPassword)]?.GetValue<string>() ?? string.Empty;
        var protectedPassword = string.Empty;
        if (request.Username.Length > 0)
        {
            protectedPassword = request.Password.Length > 0
                ? _smtpSecretProtector.Protect(request.Password)
                : existingProtectedPassword;
        }

        smtp[nameof(SmtpOptions.Host)] = request.Host;
        smtp[nameof(SmtpOptions.Port)] = request.Port;
        smtp[nameof(SmtpOptions.EnableSsl)] = request.EnableSsl;
        smtp[nameof(SmtpOptions.IgnoreCertificateErrors)] = request.IgnoreCertificateErrors;
        smtp[nameof(SmtpOptions.Username)] = request.Username;
        smtp[nameof(SmtpOptions.ProtectedPassword)] = protectedPassword;
        smtp[nameof(SmtpOptions.FromAddress)] = request.FromAddress;
        smtp[nameof(SmtpOptions.FromName)] = request.FromName.Length > 0 ? request.FromName : "MAUI Reports";
    }

    // Prevents unattended credentials from being reused against a different MOVEit endpoint.
    internal static bool SameConnectionIdentity(MoveItServerDefinition original, ConfigurationServerSaveRequest updated)
        => string.Equals(original.WebAdminUrl?.Trim().TrimEnd('/'), updated.WebAdminUrl?.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
            && string.Equals(MoveItEnvironmentKey.Resolve(original), MoveItEnvironmentKey.Normalize(string.IsNullOrWhiteSpace(updated.EnvironmentKey) ? updated.Key : updated.EnvironmentKey), StringComparison.OrdinalIgnoreCase)
            && string.Equals(original.ServerHost?.Trim(), updated.ServerHost?.Trim(), StringComparison.OrdinalIgnoreCase);

    // Purpose: Reads existing server definitions so protected Data Access Credentials survive an unrelated settings save.
    private static IReadOnlyList<MoveItServerDefinition> ReadExistingServers(JsonObject root)
    {
        if (root[MoveItOptions.SectionName] is not JsonObject moveItNode) return Array.Empty<MoveItServerDefinition>();
        try
        {
            var moveIt = JsonSerializer.Deserialize<MoveItOptions>(moveItNode.ToJsonString(), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new MoveItOptions();
            return moveIt.Servers;
        }
        catch (JsonException)
        {
            return Array.Empty<MoveItServerDefinition>();
        }
    }

    // Purpose: Normalizes one metadata field before writing it to appsettings.json.
    private static MetadataFieldDefinition NormalizeMetadataField(MetadataFieldDefinition field) => new()
    {
        Name = field.Name.Trim(),
        Label = string.IsNullOrWhiteSpace(field.Label) ? field.Name.Trim() : field.Label.Trim(),
        Description = field.Description?.Trim() ?? string.Empty,
        Type = string.IsNullOrWhiteSpace(field.Type) ? "String" : field.Type.Trim(),
        PossibleValues = field.PossibleValues.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        Required = field.Required
    };


}
