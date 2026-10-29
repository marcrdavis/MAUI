// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Validates and normalizes Configuration > Settings save requests before persistence.

using Microsoft.Extensions.Configuration;
using System.Net.Mail;
using MoveItAutomationUI.Services.MoveIt.Hosts;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;

namespace MoveItAutomationUI.Services.Configuration;

public interface IConfigurationSettingsValidator
{
    // Purpose: Validates and normalizes one settings save request, including staged database safety checks.
    Task ValidateAndNormalizeAsync(ConfigurationSettingsSaveRequest request, IConfiguration currentSettings, CancellationToken cancellationToken = default);
}

public sealed class ConfigurationSettingsValidator : IConfigurationSettingsValidator
{
    private readonly IDatabaseConnectionTester _databaseTester;

    // Purpose: Initializes configuration validation with active settings and database-test support.
    public ConfigurationSettingsValidator(IDatabaseConnectionTester databaseTester)
    {
        _databaseTester = databaseTester;
    }

    // Purpose: Validates and normalizes one settings save request, including staged database safety checks.
    public async Task ValidateAndNormalizeAsync(ConfigurationSettingsSaveRequest request, IConfiguration currentSettings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(currentSettings);
        ValidateAndNormalizeRequest(request);
        ValidateAndNormalizeSmtp(request);
        if (request.SshAlgorithms?.Encryption is not null) request.SshAlgorithms.Encryption = HostSshAlgorithms.NormalizeSelection(request.SshAlgorithms.Encryption);
        if (request.SshAlgorithms?.KeyExchange is not null) request.SshAlgorithms.KeyExchange = HostSshAlgorithms.NormalizeSelection(request.SshAlgorithms.KeyExchange);

        // Validate a changed staged database target server-side before it can become the next-start target.
        var currentDatabase = currentSettings.GetSection(MetadataDatabaseOptions.SectionName).Get<MetadataDatabaseOptions>() ?? new MetadataDatabaseOptions();
        var databaseConfigurationChanged =
            !string.Equals(currentDatabase.Provider.Value?.Trim(), request.DatabaseProvider, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(currentDatabase.ConnectionString.Value?.Trim(), request.DatabaseConnectionString, StringComparison.Ordinal);
        if (!databaseConfigurationChanged) return;

        var databaseTest = await _databaseTester.TestAsync(request.DatabaseProvider, request.DatabaseConnectionString, cancellationToken);
        try
        {
            MetadataDatabaseTargetGuard.EnsureSafeForMauiInitialization(databaseTest);
        }
        catch (InvalidOperationException ex)
        {
            // Preserve Configuration > Settings' validation exception contract for the controller.
            throw new ArgumentException(ex.Message, ex);
        }
    }


    // Purpose: Validates and normalizes SMTP values that now participate in the common Configuration save.
    // SMTP may remain unconfigured or incomplete; report readiness/Test Email surfaces that state without blocking
    // unrelated Configuration changes. Blank Password preserves the currently protected password when one exists.
    private void ValidateAndNormalizeSmtp(ConfigurationSettingsSaveRequest request)
    {
        request.Smtp ??= new SmtpConfigurationSaveRequest();
        var smtp = request.Smtp;
        smtp.Host = smtp.Host?.Trim() ?? string.Empty;
        smtp.Username = smtp.Username?.Trim() ?? string.Empty;
        smtp.FromAddress = smtp.FromAddress?.Trim() ?? string.Empty;
        smtp.FromName = string.IsNullOrWhiteSpace(smtp.FromName) ? "MAUI Reports" : smtp.FromName.Trim();
        smtp.Password ??= string.Empty;

        if (smtp.Port is < 1 or > 65535)
            throw new ArgumentException("SMTP Port must be between 1 and 65535.");

        if (smtp.FromAddress.Length > 0)
        {
            try { _ = new MailAddress(smtp.FromAddress); }
            catch (FormatException) { throw new ArgumentException("SMTP From Address is not a valid email address."); }
        }
    }

    // Purpose: Applies the existing Configuration > Settings validation and request normalization rules.
    private static void ValidateAndNormalizeRequest(ConfigurationSettingsSaveRequest request)
    {
        if (request.KeepAliveIntervalMinutes is < KeepAliveOptions.MinimumIntervalMinutes or > KeepAliveOptions.MaximumIntervalMinutes) throw new ArgumentException("Ping Interval must be between 1 and 20 minutes.");
        if (request.RequestTimeoutSeconds < 5 || request.RequestTimeoutSeconds > 300) throw new ArgumentException("REST Request Timeout must be between 5 and 300 seconds.");
        if (!MoveItOptions.SupportedIdleSessionTimeoutMinutes.Contains(request.IdleSessionTimeoutMinutes)) throw new ArgumentException("Idle Session Timeout must be 15, 30, 60, 120, 240, or 480 minutes.");
        if (request.ImportRowLimit < 1 || request.ImportRowLimit > 1000) throw new ArgumentException("Import Row Limit must be between 1 and 1,000.");
        if (request.CopyRecoveryRetentionDays is < 1 or > 90) throw new ArgumentException("Copy recovery retention must be between 1 and 90 days.");
        if (request.TaskRunHistoryMaxCount < 1 || request.TaskRunHistoryMaxCount > 100000) throw new ArgumentException("Task Run History Maximum must be between 1 and 100,000.");
        if (request.RecycleBinRetentionDays < RecycleBinOptions.MinimumRetentionDays || request.RecycleBinRetentionDays > RecycleBinOptions.MaximumRetentionDays) throw new ArgumentException("Recycle Bin Retention must be between 1 and 365 days.");
        if (request.LogRetentionDays < ApplicationLogOptions.MinimumRetentionDays || request.LogRetentionDays > ApplicationLogOptions.MaximumRetentionDays) throw new ArgumentException("Log Retention must be between 1 and 365 days.");
        if (!JobHistoryOptions.SupportedRetentionDays.Contains(request.JobHistoryRetentionDays)) throw new ArgumentException("Job History Retention must be 7, 14, 30, 60, 90, 180, or 365 days.");
        request.SystemMessageType = request.SystemMessageType?.Trim() ?? string.Empty;
        request.SystemMessageBody = request.SystemMessageBody?.Trim() ?? string.Empty;
        if (!SystemMessageOptions.SupportedMessageTypes.Contains(request.SystemMessageType, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("System Message Type must be Information, Important, or Critical.");
        request.SystemMessageType = SystemMessageOptions.SupportedMessageTypes.First(type => type.Equals(request.SystemMessageType, StringComparison.OrdinalIgnoreCase));
        if (request.BackupFrequencyHours == 0) request.BackupFrequencyHours = BackupOptions.DefaultFrequencyHours;
        if (!BackupOptions.SupportedFrequencyHours.Contains(request.BackupFrequencyHours)) throw new ArgumentException("Backup Frequency must be 4, 8, 12, or 24 hours.");
        if (request.BackupRetentionDays == 0) request.BackupRetentionDays = BackupOptions.DefaultRetentionDays;
        if (!BackupOptions.SupportedRetentionDays.Contains(request.BackupRetentionDays)) throw new ArgumentException("Backup Retention must be between 1 and 30 days.");
        if (!GridBehaviorOptions.SupportedDefaultRowSizes.Contains(request.DefaultRowSize)) throw new ArgumentException("Default Row Size is not valid.");

        request.DatabaseProvider = request.DatabaseProvider?.Trim() ?? string.Empty;
        _ = MetadataDatabaseProvider.Normalize(request.DatabaseProvider);
        request.DatabaseConnectionString = request.DatabaseConnectionString?.Trim() ?? string.Empty;
        if (request.DatabaseConnectionString.Length == 0) throw new ArgumentException("Database Connection String is required.");

        if (request.Servers.Count == 0) throw new ArgumentException("At least one MOVEit server must be configured.");
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var server in request.Servers)
        {
            server.Key = server.Key?.Trim() ?? string.Empty;
            server.EnvironmentKey = server.EnvironmentKey?.Trim() ?? string.Empty;
            server.DisplayName = server.DisplayName?.Trim() ?? string.Empty;
            server.WebAdminUrl = server.WebAdminUrl?.Trim() ?? string.Empty;
            server.ServerHost = server.ServerHost?.Trim() ?? string.Empty;
            if (server.Key.Length == 0) throw new ArgumentException("Every MOVEit server requires a Key.");
            if (!keys.Add(server.Key)) throw new ArgumentException($"MOVEit server Key '{server.Key}' is duplicated.");
            if (server.DisplayName.Length == 0) throw new ArgumentException($"MOVEit server '{server.Key}' requires a Display Name.");
            if (!Uri.TryCreate(server.WebAdminUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                throw new ArgumentException($"MOVEit server '{server.Key}' requires a valid HTTP or HTTPS Web Admin URL.");
            if (server.EnvironmentKey.Length == 0) throw new ArgumentException($"MOVEit server '{server.Key}' requires an Environment Key.");
            MoveItEnvironmentKey.Normalize(server.EnvironmentKey);
        }

        var refreshKeys = new HashSet<string>(ConfigurationSettingsCatalog.RefreshSectionKeys, StringComparer.OrdinalIgnoreCase);
        if (request.RefreshSettings.Count != ConfigurationSettingsCatalog.RefreshSectionKeys.Length || request.RefreshSettings.Any(item => !refreshKeys.Contains(item.Key)))
            throw new ArgumentException("Refresh configuration is incomplete or contains an unsupported resource.");
        foreach (var refresh in request.RefreshSettings)
        {
            if (refresh.CacheRefreshSeconds < 0 || refresh.CacheRefreshSeconds > 86400 || refresh.UiRefreshIntervalSeconds < 0 || refresh.UiRefreshIntervalSeconds > 86400)
                throw new ArgumentException($"Refresh values for {refresh.Key} must be between 0 and 86,400 seconds.");
        }

        var reportKeys = new HashSet<string>(ConfigurationSettingsCatalog.ReportSectionKeys, StringComparer.OrdinalIgnoreCase);
        if (request.ReportSettings.Count != ConfigurationSettingsCatalog.ReportSectionKeys.Length
            || request.ReportSettings.Select(item => item.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != ConfigurationSettingsCatalog.ReportSectionKeys.Length
            || request.ReportSettings.Any(item => !reportKeys.Contains(item.Key)))
        {
            throw new ArgumentException("Report configuration is incomplete or contains an unsupported report type.");
        }
        foreach (var report in request.ReportSettings)
        {
            if (!ConfigurationSettingsCatalog.AllowedReportBatchSizes.Contains(report.BatchResultLimit))
                throw new ArgumentException($"Batch result limit for {ConfigurationSettingsCatalog.ReportLabels[report.Key]} is not supported.");
            if (report.UiRefreshIntervalSeconds < 0 || report.UiRefreshIntervalSeconds > 86400)
                throw new ArgumentException($"UI refresh interval for {ConfigurationSettingsCatalog.ReportLabels[report.Key]} must be between 0 and 86,400 seconds.");
        }

        var metadataResources = MetadataResourceCatalog.Resources;
        var metadataKeys = metadataResources.Select(item => item.ConfigurationKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (request.MetadataResources.Count != metadataResources.Count
            || request.MetadataResources.Select(resource => resource.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != metadataResources.Count
            || request.MetadataResources.Any(resource => !metadataKeys.Contains(resource.Key)))
        {
            throw new ArgumentException("Metadata configuration is incomplete or contains an unsupported resource.");
        }

        foreach (var resource in request.MetadataResources)
        {
            var descriptor = MetadataResourceCatalog.GetByConfigurationKey(resource.Key);
            var allowedTypes = descriptor.AllowedFieldTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tagFieldCount = 0;
            foreach (var field in resource.Fields)
            {
                field.Name = field.Name?.Trim() ?? string.Empty;
                field.Label = field.Label?.Trim() ?? string.Empty;
                field.Description = field.Description?.Trim() ?? string.Empty;
                field.Type = string.IsNullOrWhiteSpace(field.Type) ? "String" : field.Type.Trim();
                field.PossibleValues ??= new List<string>();
                field.PossibleValues = field.PossibleValues.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (field.Name.Length == 0) throw new ArgumentException($"Every {descriptor.Label} metadata field requires a Name.");
                if (field.Name.StartsWith("__MAUI_", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException($"Metadata field names beginning with '__MAUI_' are reserved for MAUI system-managed values.");
                if (!names.Add(field.Name)) throw new ArgumentException($"Metadata field '{field.Name}' is duplicated for {descriptor.Label}.");
                if (!allowedTypes.Contains(field.Type)) throw new ArgumentException($"Metadata field '{field.Name}' uses unsupported type '{field.Type}' for {descriptor.Label}.");
                if (field.Type.Equals("List", StringComparison.OrdinalIgnoreCase) && field.PossibleValues.Count == 0)
                    throw new ArgumentException($"List metadata field '{field.Name}' for {descriptor.Label} requires at least one possible value.");
                if (field.Type.Equals("Tags", StringComparison.OrdinalIgnoreCase) && ++tagFieldCount > 1)
                    throw new ArgumentException($"Only one Tags metadata field may be configured for {descriptor.Label}.");
                if (field.Label.Length == 0) field.Label = field.Name;
            }
        }

        request.SecurityUsers ??= new List<ConfigurationSecurityUserSaveRequest>();
        request.SecurityOriginalUsernames ??= new List<string>();
        var securityNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var securityUser in request.SecurityUsers)
        {
            securityUser.Username = MauiUsername.Normalize(securityUser.Username);
            securityUser.Role = securityUser.Role?.Trim() ?? string.Empty;
            if (securityUser.Username.Length == 0) throw new ArgumentException("Every MAUI security entry requires a MOVEit Username.");
            if (securityUser.Username.Length > MauiSecurityUser.UsernameMaxLength) throw new ArgumentException($"MOVEit usernames used by MAUI Security cannot exceed {MauiSecurityUser.UsernameMaxLength} characters.");
            if (!securityNames.Add(securityUser.Username)) throw new ArgumentException($"MOVEit username '{securityUser.Username}' is duplicated in Security.");
            if (!MauiSecurityRoles.IsValid(securityUser.Role)) throw new ArgumentException($"Security role '{securityUser.Role}' is not valid. Use User or Admin.");
            securityUser.Role = MauiSecurityRoles.Normalize(securityUser.Role);
        }
        request.SecurityOriginalUsernames = request.SecurityOriginalUsernames
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(MauiUsername.Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (request.SecurityUsers.Count == 0
            || !request.SecurityUsers.Any(user => user.Role.Equals(MauiSecurityRoles.Admin, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("At least one MAUI Admin must remain configured.");
        }

        var gridKeys = new HashSet<string>(ConfigurationSettingsCatalog.GridSectionKeys, StringComparer.OrdinalIgnoreCase);
        if (request.GridDefinitions.Count != ConfigurationSettingsCatalog.GridSectionKeys.Length
            || request.GridDefinitions.Select(definition => definition.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != ConfigurationSettingsCatalog.GridSectionKeys.Length
            || request.GridDefinitions.Any(definition => !gridKeys.Contains(definition.Key)))
        {
            throw new ArgumentException("Grid configuration is incomplete or contains an unsupported resource.");
        }

        foreach (var definition in request.GridDefinitions)
        {
            definition.DefaultColumns = NormalizeList(definition.DefaultColumns);
            definition.SearchColumns = NormalizeList(definition.SearchColumns);
            if (definition.DefaultColumns.Count == 0) throw new ArgumentException($"{ConfigurationSettingsCatalog.GridLabels[definition.Key]} must have at least one default column.");
        }
    }

    // Purpose: Trims and de-duplicates a configuration string list while preserving order.
    private static List<string> NormalizeList(IEnumerable<string> values) => values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}
