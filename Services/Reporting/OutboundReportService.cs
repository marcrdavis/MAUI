// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Coordinates report definition persistence, fresh scheduled data retrieval, and email delivery.

using System.Text.Json;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.Security;

namespace MoveItAutomationUI.Services.Reporting;

public interface IOutboundReportService
{
    Task<object> CreateAsync(CreateOutboundReportRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OutboundReportSummary>> ListAsync(string username, bool isAdmin, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OutboundReportHistorySummary>> HistoryAsync(string id, string username, bool isAdmin, CancellationToken cancellationToken = default);
    Task<OutboundReportSummary> UpdateAsync(UpdateOutboundReportRequest request, string username, bool isAdmin, CancellationToken cancellationToken = default);
    Task<OutboundReportEditDefinition?> GetEditDefinitionAsync(string id, string username, bool isAdmin, CancellationToken cancellationToken = default);
    Task<OutboundReportSummary> UpdateViewAsync(UpdateOutboundReportViewRequest request, string username, bool isAdmin, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, string username, bool isAdmin, CancellationToken cancellationToken = default);
    Task<OutboundReportSummary?> SetEnabledAsync(string id, bool enabled, string username, bool isAdmin, CancellationToken cancellationToken = default);
    Task<OutboundReportExecutionResult> RunSavedNowAsync(string id, string username, bool isAdmin, CancellationToken cancellationToken = default);
    Task<OutboundReportExecutionResult> ExecuteScheduledAsync(OutboundReportEntity report, CancellationToken cancellationToken = default);
}

public sealed class OutboundReportService : IOutboundReportService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IOutboundReportRepository _repository;
    private readonly IOutboundReportDataSource _dataSource;
    private readonly IOutboundReportEmailService _email;
    private readonly IReportScheduleCalculator _scheduleCalculator;
    private readonly IDataAccessCredentialService _dataAccessCredentials;
    private readonly IMoveItSessionService _sessionService;
    private readonly IMoveItRestClient _restClient;
    private readonly IMauiSecurityService _security;
    private readonly ILogger<OutboundReportService> _logger;

    public OutboundReportService(
        IOutboundReportRepository repository,
        IOutboundReportDataSource dataSource,
        IOutboundReportEmailService email,
        IReportScheduleCalculator scheduleCalculator,
        IDataAccessCredentialService dataAccessCredentials,
        IMoveItSessionService sessionService,
        IMoveItRestClient restClient,
        IMauiSecurityService security,
        ILogger<OutboundReportService> logger)
    {
        _repository = repository;
        _dataSource = dataSource;
        _email = email;
        _scheduleCalculator = scheduleCalculator;
        _dataAccessCredentials = dataAccessCredentials;
        _sessionService = sessionService;
        _restClient = restClient;
        _security = security;
        _logger = logger;
    }

    public async Task<object> CreateAsync(CreateOutboundReportRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var session = _sessionService.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        if (!OutboundReportGridRegistry.TryGetLabel(request.GridKey, out _)) throw new ArgumentException("This grid does not support reports.");
        OutboundReportSourcePolicy.EnsureAllowed(request.GridKey, _security.IsAdmin(session.Username));
        request.Title = (request.Title ?? string.Empty).Trim();
        request.Description = (request.Description ?? string.Empty).Trim();
        if (request.Title.Length == 0 || request.Title.Length > 200) throw new ArgumentException("Report Title is required and cannot exceed 200 characters.");
        if (request.Description.Length > 1000) throw new ArgumentException("Report Description cannot exceed 1000 characters.");
        var serverTimeZone = session.Info?.ServerTimezoneInfo?.TimeZoneName ?? string.Empty;
        var schedule = _scheduleCalculator.ValidateAndNormalize(request.Schedule, serverTimeZone);
        var recipients = OutboundReportRepository.NormalizeRecipients(request.Recipients);

        if (schedule.DeliveryMode == OutboundReportDeliveryModes.Now)
        {
            var emailStatus = _email.GetConfigurationStatus();
            if (!emailStatus.IsConfigured) throw new InvalidOperationException(emailStatus.Message);
            if (request.Snapshot is null) throw new ArgumentException("The current grid snapshot is required when sending a report now.");
            var started = DateTimeOffset.UtcNow;
            try
            {
                var data = _dataSource.NormalizeSnapshot(request.Snapshot);
                await _email.SendAsync(request.Title.Trim(), request.Description.Trim(), recipients, data, cancellationToken);
                var result = new OutboundReportExecutionResult(true, data.Rows.Count, "Report sent successfully.", DateTimeOffset.UtcNow);
                await _repository.RecordAdHocExecutionAsync(session.GetEnvironmentKey(), request.Title, started, result, recipients.Count, cancellationToken);
                return new { sent = true, saved = false, message = result.Message, rowCount = result.RowCount };
            }
            catch (Exception ex)
            {
                var failed = new OutboundReportExecutionResult(false, 0, ex.Message, DateTimeOffset.UtcNow);
                await _repository.RecordAdHocExecutionAsync(session.GetEnvironmentKey(), request.Title, started, failed, recipients.Count, cancellationToken);
                throw;
            }
        }

        var environment = session.GetEnvironmentKey();
        if (!_dataAccessCredentials.IsEnvironmentConfigured(environment))
            throw new InvalidOperationException($"Data Access Credentials must be configured for at least one active MOVEit server in environment '{environment}' before a scheduled report can be saved.");
        var nextRun = _scheduleCalculator.GetNextRunUtc(schedule, DateTimeOffset.UtcNow)
            ?? throw new ArgumentException("The selected report schedule does not contain a future delivery time.");
        // SMTP readiness is deliberately independent from report persistence. Save the definition
        // first; email readiness is advisory and must never make a valid schedule appear to fail.
        var saved = await _repository.CreateAsync(request, schedule, nextRun, cancellationToken);
        var smtpStatus = GetEmailConfigurationStatusSafe();
        var warning = smtpStatus.IsConfigured
            ? string.Empty
            : "Outbound email has not been configured. The report schedule was saved and will remain pending until SMTP Configuration is completed.";
        return new { sent = false, saved = true, id = saved.Id, message = "Report schedule saved.", warning, nextRunUtc = saved.NextRunUtc };
    }


    private OutboundEmailConfigurationStatus GetEmailConfigurationStatusSafe()
    {
        try
        {
            return _email.GetConfigurationStatus();
        }
        catch (Exception ex)
        {
            // Email readiness is advisory while a schedule is being saved. Never turn a valid
            // persisted report definition into a failed Create request because SMTP status
            // could not be inspected. The scheduler will retry once email becomes available.
            _logger.LogWarning(ex,
                "Unable to determine outbound email readiness while saving a report schedule. The report definition can still be saved and will remain pending until email is available.");
            return new OutboundEmailConfigurationStatus(false,
                "Unable to verify outbound email configuration. Configure SMTP under Configuration > Settings > SMTP Configuration before delivery is expected.");
        }
    }

    public async Task<IReadOnlyList<OutboundReportSummary>> ListAsync(string username, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var rows = await _repository.GetAllAsync(username, isAdmin, cancellationToken);
        return isAdmin ? rows : rows.Where(item => OutboundReportSourcePolicy.IsAllowed(item.GridKey, false)).ToArray();
    }

    public Task<IReadOnlyList<OutboundReportHistorySummary>> HistoryAsync(string id, string username, bool isAdmin, CancellationToken cancellationToken = default)
        => _repository.GetHistoryAsync(id, username, isAdmin, cancellationToken: cancellationToken);

    public async Task<OutboundReportSummary> UpdateAsync(UpdateOutboundReportRequest request, string username, bool isAdmin, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Schedule);
        var existing = await _repository.GetAsync(request.Id, username, isAdmin, cancellationToken)
            ?? throw new ArgumentException("The Report no longer exists or you do not have access to it.");
        OutboundReportSourcePolicy.EnsureAllowed(existing.GridKey, isAdmin);
        var existingSchedule = DeserializeSchedule(existing.ScheduleJson);
        var zone = string.IsNullOrWhiteSpace(request.Schedule.TimeZoneId) ? existingSchedule.TimeZoneId : request.Schedule.TimeZoneId;
        var schedule = _scheduleCalculator.ValidateAndNormalize(request.Schedule, zone);
        if (schedule.DeliveryMode == OutboundReportDeliveryModes.Now) throw new ArgumentException("Saved reports must use a One Time or Recurring schedule.");
        if (request.Enabled && !_dataAccessCredentials.IsEnvironmentConfigured(existing.EnvironmentKey))
            throw new InvalidOperationException($"Data Access Credentials are not configured for any active MOVEit server in environment '{existing.EnvironmentKey}'.");
        var next = request.Enabled ? _scheduleCalculator.GetNextRunUtc(schedule, DateTimeOffset.UtcNow) : null;
        if (request.Enabled && next is null) throw new ArgumentException("The selected report schedule does not contain a future delivery time.");
        var updated = await _repository.UpdateAsync(request, schedule, next, username, isAdmin, cancellationToken);
        return (await _repository.GetAllAsync(username, isAdmin, cancellationToken)).Single(item => item.Id == updated.Id);
    }

    public async Task<OutboundReportEditDefinition?> GetEditDefinitionAsync(string id, string username, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var report = await _repository.GetAsync(id, username, isAdmin, cancellationToken);
        if (report is null) return null;
        OutboundReportSourcePolicy.EnsureAllowed(report.GridKey, isAdmin);
        return new OutboundReportEditDefinition(
            report.Id,
            report.GridKey,
            report.Title,
            report.Description,
            DeserializeViewState(report.ViewStateJson),
            DeserializeSourceContext(report.SourceContextJson));
    }

    public async Task<OutboundReportSummary> UpdateViewAsync(UpdateOutboundReportViewRequest request, string username, bool isAdmin, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ViewState);
        ArgumentNullException.ThrowIfNull(request.SourceContext);
        var existing = await _repository.GetAsync(request.Id, username, isAdmin, cancellationToken)
            ?? throw new ArgumentException("The Report no longer exists or you do not have access to it.");
        OutboundReportSourcePolicy.EnsureAllowed(existing.GridKey, isAdmin);
        var updated = await _repository.UpdateViewAsync(request, username, isAdmin, cancellationToken);
        return (await _repository.GetAllAsync(username, isAdmin, cancellationToken)).Single(item => item.Id == updated.Id);
    }

    public Task<bool> DeleteAsync(string id, string username, bool isAdmin, CancellationToken cancellationToken = default)
        => _repository.DeleteAsync(id, username, isAdmin, cancellationToken);

    public async Task<OutboundReportSummary?> SetEnabledAsync(string id, bool enabled, string username, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var existing = await _repository.GetAsync(id, username, isAdmin, cancellationToken);
        if (existing is null) return null;
        OutboundReportSourcePolicy.EnsureAllowed(existing.GridKey, isAdmin);
        DateTimeOffset? next = null;
        if (enabled)
        {
            if (!_dataAccessCredentials.IsEnvironmentConfigured(existing.EnvironmentKey))
                throw new InvalidOperationException($"Data Access Credentials are not configured for any active MOVEit server in environment '{existing.EnvironmentKey}'.");
            next = _scheduleCalculator.GetNextRunUtc(DeserializeSchedule(existing.ScheduleJson), DateTimeOffset.UtcNow)
                ?? throw new ArgumentException("This report has no future delivery times. Edit its schedule before enabling it.");
        }
        var changed = await _repository.SetEnabledAsync(id, enabled, next, username, isAdmin, cancellationToken);
        return changed is null ? null : (await _repository.GetAllAsync(username, isAdmin, cancellationToken)).Single(item => item.Id == id);
    }

    public async Task<OutboundReportExecutionResult> RunSavedNowAsync(string id, string username, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var report = await _repository.GetAsync(id, username, isAdmin, cancellationToken)
            ?? throw new ArgumentException("The Report no longer exists or you do not have access to it.");
        OutboundReportSourcePolicy.EnsureAllowed(report.GridKey, isAdmin);
        return await ExecuteWithStoredCredentialAsync(report, updateSchedule: false, cancellationToken);
    }

    public Task<OutboundReportExecutionResult> ExecuteScheduledAsync(OutboundReportEntity report, CancellationToken cancellationToken = default)
        => ExecuteWithStoredCredentialAsync(report, updateSchedule: true, cancellationToken);

    private async Task<OutboundReportExecutionResult> ExecuteWithStoredCredentialAsync(OutboundReportEntity report, bool updateSchedule, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var recipients = OutboundReportRepository.NormalizeRecipients(report.Recipients);
        OutboundReportData? data = null;
        ReportScheduleDefinition? scheduledDefinition = null;
        try
        {
            // Re-evaluate restricted source authorization at execution time so role changes take effect
            // immediately for saved schedules instead of trusting the role held when the Report was created.
            if (!OutboundReportSourcePolicy.IsAllowed(report.GridKey, _security.IsAdmin(report.OwnerUsername)))
            {
                var denied = new OutboundReportExecutionResult(false, 0, "Report disabled because its source now requires MAUI Administrator access.", DateTimeOffset.UtcNow);
                await _repository.RecordExecutionAsync(report, started, denied, null, keepEnabled: false, recipientCount: recipients.Count, updateScheduleState: true, cancellationToken: cancellationToken);
                return denied;
            }

            // Validate a scheduled definition before generating or sending anything. Invalid persisted
            // schedules are failed and disabled rather than repeatedly sending without advancing.
            if (updateSchedule) scheduledDefinition = ValidateStoredSchedule(report);

            var emailStatus = _email.GetConfigurationStatus();
            if (!emailStatus.IsConfigured)
            {
                var unavailable = new OutboundReportExecutionResult(false, 0, emailStatus.Message, DateTimeOffset.UtcNow);
                // Scheduled reports stay due/pending until SMTP is available. A manual Run Now
                // attempt belongs to the saved Report's own History while preserving its schedule.
                if (!updateSchedule)
                    await RecordManualExecutionStateAsync(report, started, unavailable, recipients.Count, cancellationToken);
                return unavailable;
            }

            var credentials = _dataAccessCredentials.GetConfiguredForEnvironment(report.EnvironmentKey);
            if (credentials.Count == 0)
                throw new InvalidOperationException($"Data Access Credentials are not configured for any active MOVEit server in environment '{report.EnvironmentKey}'.");
            var state = await AuthenticateEnvironmentAsync(report.EnvironmentKey, credentials, cancellationToken);

            using (_sessionService.PushBackgroundSession(state))
                data = await _dataSource.LoadAsync(report, cancellationToken);

            await _email.SendAsync(report.Title, report.Description, recipients, data, cancellationToken);
            var success = new OutboundReportExecutionResult(true, data.Rows.Count, "Report sent successfully.", DateTimeOffset.UtcNow);
            if (updateSchedule) await UpdateExecutionStateAsync(report, scheduledDefinition!, started, success, recipients.Count, cancellationToken);
            else await RecordManualExecutionStateAsync(report, started, success, recipients.Count, cancellationToken);
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Outbound report {ReportId} ({ReportTitle}) failed.", report.Id, report.Title);
            var failed = new OutboundReportExecutionResult(false, data?.Rows.Count ?? 0, ex.Message, DateTimeOffset.UtcNow);
            if (updateSchedule)
            {
                if (scheduledDefinition is null)
                    await _repository.RecordExecutionAsync(report, started, failed, null, keepEnabled: false, recipientCount: recipients.Count, updateScheduleState: true, cancellationToken: cancellationToken);
                else
                    await UpdateExecutionStateAsync(report, scheduledDefinition, started, failed, recipients.Count, cancellationToken);
            }
            else await RecordManualExecutionStateAsync(report, started, failed, recipients.Count, cancellationToken);
            return failed;
        }
    }


    private async Task<MoveItSessionState> AuthenticateEnvironmentAsync(
        string environmentKey,
        IReadOnlyList<DataAccessCredential> credentials,
        CancellationToken cancellationToken)
    {
        Exception? last = null;
        foreach (var credential in credentials)
        {
            try
            {
                var state = await _restClient.AuthenticateAsync(credential.ServerKey, credential.Username, credential.Password, cancellationToken);
                var info = await _restClient.GetInfoAsync(state, cancellationToken);
                if (!info.IsAdmin)
                {
                    last = new InvalidOperationException($"Data Access user '{credential.Username}' authenticated to {credential.DisplayName}, but MOVEit Administrator access is required for scheduled reports and configuration backups.");
                    _logger.LogWarning("Data Access user {Username} on server {ServerKey} is not a MOVEit Administrator; another configured node will be tried if available.", credential.Username, credential.ServerKey);
                    continue;
                }

                state.Info = info;
                return state;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && (ex is MoveItAuthenticationException or MoveItApiException or HttpRequestException or TaskCanceledException))
            {
                last = ex;
                _logger.LogInformation("Data Access authentication did not succeed for MOVEit server {ServerKey} in environment {EnvironmentKey}; another configured node will be tried if available.", credential.ServerKey, environmentKey);
            }
        }

        throw new InvalidOperationException(
            $"Data Access authentication failed for MOVEit environment '{environmentKey}' on every active server with configured credentials.",
            last);
    }

    // Purpose: Records Run Now against the saved Report so its History includes manual executions,
    // while leaving the Report's enabled state and next scheduled run unchanged.
    private Task RecordManualExecutionStateAsync(OutboundReportEntity report, DateTimeOffset started, OutboundReportExecutionResult result, int recipientCount, CancellationToken cancellationToken)
         => _repository.RecordExecutionAsync(report, started, result, report.NextRunUtc, report.Enabled, recipientCount, updateScheduleState: false, cancellationToken: cancellationToken);

    private async Task UpdateExecutionStateAsync(OutboundReportEntity report, ReportScheduleDefinition schedule, DateTimeOffset started, OutboundReportExecutionResult result, int recipientCount, CancellationToken cancellationToken)
    {
        var after = report.NextRunUtc.HasValue && report.NextRunUtc.Value > started ? report.NextRunUtc.Value : started;
        var next = _scheduleCalculator.GetNextRunUtc(schedule, after.AddSeconds(1));
        var keepEnabled = next.HasValue;
        await _repository.RecordExecutionAsync(report, started, result, next, keepEnabled, recipientCount, updateScheduleState: true, cancellationToken: cancellationToken);
    }

    private ReportScheduleDefinition ValidateStoredSchedule(OutboundReportEntity report)
    {
        var schedule = DeserializeSchedule(report.ScheduleJson);
        return _scheduleCalculator.ValidateAndNormalize(schedule, schedule.TimeZoneId);
    }

    private static ReportGridViewState DeserializeViewState(string json)
    {
        try { return JsonSerializer.Deserialize<ReportGridViewState>(json, JsonOptions) ?? throw new InvalidOperationException("The saved report view is empty."); }
        catch (JsonException ex) { throw new InvalidOperationException("The saved report view is invalid.", ex); }
    }

    private static ReportSourceContext DeserializeSourceContext(string json)
    {
        try { return JsonSerializer.Deserialize<ReportSourceContext>(json, JsonOptions) ?? throw new InvalidOperationException("The saved report source context is empty."); }
        catch (JsonException ex) { throw new InvalidOperationException("The saved report source context is invalid.", ex); }
    }

    private static ReportScheduleDefinition DeserializeSchedule(string json)
    {
        try { return JsonSerializer.Deserialize<ReportScheduleDefinition>(json, JsonOptions) ?? throw new InvalidOperationException("The saved report schedule is empty."); }
        catch (JsonException ex) { throw new InvalidOperationException("The saved report schedule is invalid.", ex); }
    }
}
