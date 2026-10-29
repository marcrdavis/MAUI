// MAUI Quick Transfer — server-owned completion cleanup for opted-in temporary Tasks.
// Only safe correlation metadata is persisted. MOVEit credentials never enter this file.
using System.Collections.Concurrent;
using System.Net;
using System.Globalization;
using System.Text;
using MoveItAutomationUI.Services.Reporting;
using System.Text.Json;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt.Tasks;

namespace MoveItAutomationUI.Services.MoveIt;

public interface IQuickTransferCleanupTracker
{
    Task TrackAsync(string token, string taskId, string taskName, DateTimeOffset startAttemptUtc, MoveItSessionState session, CancellationToken cancellationToken = default,
        bool deleteAfterSuccess = true, string recipients = "", string sourceHost = "", string sourcePath = "",
        string sourceFiles = "", string destinationHost = "", string destinationPath = "");
    Task SetNominalStartAsync(string token, string nominalStart, CancellationToken cancellationToken = default);
    Task StopTrackingAsync(string token, CancellationToken cancellationToken = default);
    Task<bool> TryCleanupAsync(string token, CancellationToken cancellationToken = default);
}

internal sealed record QuickTransferCleanupEntry(
    string Token, string EnvironmentKey, string ServerKey, string TaskId, string TaskName,
    DateTimeOffset StartAttemptUtc, string NominalStart,
    bool DeleteAfterSuccess = true, string Recipients = "", string SourceHost = "", string SourcePath = "",
    string SourceFiles = "", string DestinationHost = "", string DestinationPath = "",
    bool NotificationAttempted = false);

/// <summary>
/// One worker per MAUI instance. Persisted entries survive application restart; without configured
/// Data Access Credentials they remain pending for review/retry rather than triggering an unsafe delete.
/// </summary>
public sealed class QuickTransferCleanupWorker : BackgroundService, IQuickTransferCleanupTracker
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<QuickTransferCleanupWorker> _logger;
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, QuickTransferCleanupEntry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, MoveItSessionState> _inMemorySessions = new(StringComparer.Ordinal);
    private bool _loaded;

    public QuickTransferCleanupWorker(IServiceScopeFactory scopes, IHostEnvironment environment, ILogger<QuickTransferCleanupWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
        _filePath = Path.Combine(environment.ContentRootPath, "data", "quick-transfer-cleanup.json");
    }

    public async Task TrackAsync(string token, string taskId, string taskName, DateTimeOffset startAttemptUtc, MoveItSessionState session, CancellationToken cancellationToken = default,
        bool deleteAfterSuccess = true, string recipients = "", string sourceHost = "", string sourcePath = "",
        string sourceFiles = "", string destinationHost = "", string destinationPath = "")
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(taskId) || string.IsNullOrWhiteSpace(taskName)
            || string.IsNullOrWhiteSpace(session.ServerKey) || string.IsNullOrWhiteSpace(session.GetEnvironmentKey())
            || startAttemptUtc == default)
            throw new InvalidOperationException("A Quick Transfer cleanup record requires a Task identity and start time.");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            LoadIfNeeded();
            // A token must never be reused for another MOVEit object or server.
            // Different servers can have identical numeric Task IDs.
            if (_entries.TryGetValue(token, out var previous)
                && (!previous.TaskId.Equals(taskId, StringComparison.Ordinal)
                    || !previous.TaskName.Equals(taskName, StringComparison.Ordinal)
                    || !previous.ServerKey.Equals(session.ServerKey, StringComparison.OrdinalIgnoreCase)
                    || !previous.EnvironmentKey.Equals(session.GetEnvironmentKey(), StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Quick Transfer cleanup token already belongs to a different Task or MOVEit server.");
            _entries[token] = new QuickTransferCleanupEntry(token, session.GetEnvironmentKey(), session.ServerKey,
                taskId, taskName, startAttemptUtc, string.Empty, deleteAfterSuccess, recipients,
                sourceHost, sourcePath, sourceFiles, destinationHost, destinationPath);
            Save();
            // Ephemeral only. After restart the worker authenticates with protected Data Access Credentials.
            _inMemorySessions[token] = session;
        }
        finally { _gate.Release(); }
    }

    public async Task SetNominalStartAsync(string token, string nominalStart, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            LoadIfNeeded();
            if (_entries.TryGetValue(token, out var entry))
            {
                _entries[token] = entry with { NominalStart = (nominalStart ?? string.Empty).Trim() };
                Save();
            }
        }
        finally { _gate.Release(); }
    }

    public async Task StopTrackingAsync(string token, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            LoadIfNeeded();
            if (_entries.Remove(token)) Save();
            _inMemorySessions.TryRemove(token, out _);
        }
        finally { _gate.Release(); }
    }

    public async Task<bool> TryCleanupAsync(string token, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            LoadIfNeeded();
            if (!_entries.TryGetValue(token, out var entry)) return true;
            using var scope = _scopes.CreateScope();
            var services = scope.ServiceProvider;
            var sessionService = services.GetRequiredService<IMoveItSessionService>();
            var session = await ResolveSessionAsync(entry, services, cancellationToken);
            if (session is null) return false;
            using (sessionService.PushBackgroundSession(session))
            {
                var running = await services.GetRequiredService<IMoveItRestClient>().GetRunningTasksAsync(cancellationToken);
                if (running.Any(run => run.TaskId.Equals(entry.TaskId, StringComparison.OrdinalIgnoreCase))) return false;
                var reports = await services.GetRequiredService<IAutomationReportService>()
                    .GetTaskRunsAsync(entry.TaskId, 100, null, 100, cancellationToken);
                var matched = QuickTransferService.SelectRunRecord(reports.Items, entry.NominalStart, entry.StartAttemptUtc);
                if (matched is null || !string.Equals(matched.TaskId, entry.TaskId, StringComparison.OrdinalIgnoreCase)) return false;
                // If another run occurred after the correlated transfer, retain the Task for
                // manual review rather than deleting an object someone may have reused.
                if (matched.StartTime.HasValue && reports.Items.Any(other =>
                    !string.Equals(other.RunId, matched.RunId, StringComparison.OrdinalIgnoreCase)
                    && other.StartTime.HasValue && other.StartTime > matched.StartTime)) return false;
                var outcome = TaskRunStatus.Normalize(matched.Status);
                if (!outcome.Equals("Success", StringComparison.OrdinalIgnoreCase))
                {
                    // Failed, No Transfers, and cancelled/indeterminate runs never authorize cleanup.
                    if (outcome.Equals("Failed", StringComparison.OrdinalIgnoreCase)
                        || outcome.Equals("No Transfers", StringComparison.OrdinalIgnoreCase)
                        || outcome.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
                        RemoveCompleted(entry.Token);
                    return false;
                }
                var taskCrud = services.GetRequiredService<ITaskCrudCoordinator>();
                var definition = await taskCrud.GetDefinitionAsync(entry.TaskId, cancellationToken);
                var currentName = definition.FirstOrDefault(item => item.Key.Equals("Name", StringComparison.OrdinalIgnoreCase)).Value?.ToString();
                if (!QuickTransferCleanupPolicy.CanDelete(entry, matched, currentName, isRunning: false))
                {
                    _logger.LogWarning("Quick Transfer Task {TaskId} no longer matches its cleanup identity; manual review is required.", entry.TaskId);
                    return false;
                }
                if (!string.IsNullOrWhiteSpace(entry.Recipients) && !entry.NotificationAttempted)
                {
                    // Persist the send attempt before SMTP: at-most-once delivery even across worker restarts.
                    // A crash after this write might result in no email, but can never send duplicates.
                    entry = entry with { NotificationAttempted = true };
                    _entries[entry.Token] = entry;
                    Save();
                    try
                    {
                        var email = services.GetRequiredService<IOutboundReportEmailService>();
                        var recipients = OutboundReportRepository.NormalizeRecipients(entry.Recipients);
                        await email.SendHtmlAsync($"MAUI Quick Transfer — Successful: {entry.TaskName}",
                            BuildConfirmationHtml(entry, matched), recipients, cancellationToken);
                        _logger.LogInformation("Quick Transfer confirmation sent for Task {TaskId}.", entry.TaskId);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogError(ex, "Quick Transfer succeeded, but confirmation email failed for Task {TaskId}. Delivery will not be automatically retried to avoid duplicate messages.", entry.TaskId);
                    }
                }
                if (!entry.DeleteAfterSuccess)
                {
                    RemoveCompleted(entry.Token);
                    return true;
                }
                // The last Task existence/name verification precedes deletion. No retries on indeterminate DELETE.
                await taskCrud.DeleteAsync(entry.TaskId, cancellationToken);
                services.GetRequiredService<IAutomationTaskService>().InvalidateCache();
                try
                {
                    await services.GetRequiredService<IMetadataRepository>().DeleteObjectsAsync(
                        entry.EnvironmentKey, "Task", new[] { entry.TaskId }, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Quick Transfer Task {TaskId} was deleted but metadata cleanup failed.", entry.TaskId);
                }
                _logger.LogInformation("Background cleanup deleted successfully completed Quick Transfer Task {TaskId} ({TaskName}).", entry.TaskId, entry.TaskName);
                RemoveCompleted(entry.Token);
                return true;
            }
        }
        catch (MoveItAuthenticationException)
        {
            // A browser-session token can expire before cleanup completes. On the next
            // pass prefer protected administrator Data Access Credentials if configured.
            _inMemorySessions.TryRemove(token, out _);
            throw;
        }
        finally { _gate.Release(); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Let startup configuration and registered services initialize before the first check.
            using var ticker = new PeriodicTimer(TimeSpan.FromMinutes(1));
            while (await ticker.WaitForNextTickAsync(stoppingToken))
            {
                string[] tokens;
                await _gate.WaitAsync(stoppingToken);
                try { LoadIfNeeded(); tokens = _entries.Keys.ToArray(); }
                finally { _gate.Release(); }
                foreach (var token in tokens)
                {
                    try { await TryCleanupAsync(token, stoppingToken); }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                    catch (Exception ex) { _logger.LogWarning(ex, "Quick Transfer cleanup check failed; record remains pending for the next check."); }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex) { _logger.LogError(ex, "Quick Transfer cleanup worker stopped unexpectedly; persisted records are retained."); }
    }

    private async Task<MoveItSessionState?> ResolveSessionAsync(QuickTransferCleanupEntry entry, IServiceProvider services, CancellationToken cancellationToken)
    {
        if (_inMemorySessions.TryGetValue(entry.Token, out var active))
        {
            if (string.Equals(active.ServerKey, entry.ServerKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(active.GetEnvironmentKey(), entry.EnvironmentKey, StringComparison.OrdinalIgnoreCase))
                return active;
            _inMemorySessions.TryRemove(entry.Token, out _);
        }
        var credentials = services.GetRequiredService<IDataAccessCredentialService>()
            .GetConfiguredForEnvironment(entry.EnvironmentKey);
        var rest = services.GetRequiredService<IMoveItRestClient>();
        // Destructive cleanup must never fall back to another MOVEit server in the
        // same environment: Task IDs are not guaranteed to identify the same object
        // on different servers. If the original server has no usable credentials,
        // retain the cleanup record for later review/retry.
        foreach (var credential in CredentialsForServer(credentials, entry.ServerKey))
        {
            try
            {
                var session = await rest.AuthenticateAsync(credential.ServerKey, credential.Username, credential.Password, cancellationToken);
                if (!string.Equals(session.ServerKey, entry.ServerKey, StringComparison.OrdinalIgnoreCase))
                    continue;
                var info = await rest.GetInfoAsync(session, cancellationToken);
                if (!info.IsAdmin) continue;
                session.Info = info;
                return session;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) { _logger.LogWarning(ex, "Quick Transfer cleanup could not authenticate to MOVEit server {ServerKey}.", credential.ServerKey); }
        }
        // Credentials are deliberately not persisted in the tracker. Pending items survive until an
        // authorized background account is configured, or until manual review.
        return null;
    }

    internal static IReadOnlyList<DataAccessCredential> CredentialsForServer(
        IEnumerable<DataAccessCredential> credentials, string serverKey)
        => credentials.Where(item => string.Equals(item.ServerKey, serverKey, StringComparison.OrdinalIgnoreCase)).ToArray();

    internal static string BuildConfirmationHtml(QuickTransferCleanupEntry entry, TaskRunReportRecord run)
    {
        static string Enc(string? value) => WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(value) ? "—" : value);
        var rows = new (string Label, string Value)[]
        {
            ("Task", entry.TaskName), ("Status", "Success"),
            ("Started", run.StartTime?.ToString("MMM d, yyyy h:mm tt zzz", CultureInfo.CurrentCulture) ?? ""),
            ("Completed", run.EndTime?.ToString("MMM d, yyyy h:mm tt zzz", CultureInfo.CurrentCulture) ?? ""),
            ("Source Host", entry.SourceHost),
            ("Source Path", string.Join("/", new[] { entry.SourcePath.TrimEnd('/'), entry.SourceFiles.TrimStart('/') }.Where(x => x.Length > 0))),
            ("Destination Host", entry.DestinationHost), ("Destination Path", entry.DestinationPath),
            ("Files Transferred", run.FilesSent?.ToString("N0", CultureInfo.CurrentCulture) ?? ""),
            ("Total Transferred", run.TotalBytesSent.HasValue ? run.TotalBytesSent.Value.ToString("N0", CultureInfo.CurrentCulture) + " bytes" : "")
        };
        var html = new StringBuilder("<!doctype html><html><body style=\"font-family:Segoe UI,Arial,sans-serif;color:#172033\"><h2>Transfer Completed Successfully</h2><p>The following MOVEit Automation transfer completed successfully.</p><table style=\"border-collapse:collapse\">");
        foreach (var row in rows)
            html.Append("<tr><th style=\"text-align:left;padding:7px 14px 7px 0;border-bottom:1px solid #ddd\">")
                .Append(Enc(row.Label)).Append("</th><td style=\"padding:7px 0;border-bottom:1px solid #ddd\">")
                .Append(Enc(row.Value)).Append("</td></tr>");
        html.Append("</table><p style=\"font-size:12px;color:#596579\">This is an automated confirmation generated by MAUI sent from an unattended mailbox.</p></body></html>");
        return html.ToString();
    }

    private void RemoveCompleted(string token)
    {
        if (_entries.Remove(token)) Save();
        _inMemorySessions.TryRemove(token, out _);
    }

    private void LoadIfNeeded()
    {
        if (_loaded) return;
        if (File.Exists(_filePath))
        {
            // Reject corrupt records instead of silently discarding them and overwriting the file.
            var loaded = JsonSerializer.Deserialize<List<QuickTransferCleanupEntry>>(File.ReadAllText(_filePath))
                ?? throw new InvalidOperationException("Quick Transfer cleanup store contains an invalid null document.");
            foreach (var entry in loaded)
            {
                if (string.IsNullOrWhiteSpace(entry.Token) || string.IsNullOrWhiteSpace(entry.TaskId)
                    || string.IsNullOrWhiteSpace(entry.TaskName) || string.IsNullOrWhiteSpace(entry.ServerKey)
                    || string.IsNullOrWhiteSpace(entry.EnvironmentKey) || entry.StartAttemptUtc == default)
                    throw new InvalidOperationException("Quick Transfer cleanup store contains an invalid Task record.");
                _entries.Add(entry.Token, entry);
            }
        }
        _loaded = true;
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_filePath) ?? throw new InvalidOperationException("Missing Quick Transfer cleanup store directory.");
        Directory.CreateDirectory(directory);
        var temp = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(_entries.Values.ToArray()));
            File.Move(temp, _filePath, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

// Pure, testable deletion guard. The worker also requires a non-running Task and no
// later Task Run; a success label alone must never authorize object deletion.
internal static class QuickTransferCleanupPolicy
{
    public static bool CanDelete(QuickTransferCleanupEntry entry, TaskRunReportRecord? run, string? currentTaskName, bool isRunning)
        => !isRunning && run is not null
            && run.TaskId.Equals(entry.TaskId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(currentTaskName, entry.TaskName, StringComparison.Ordinal)
            && TaskRunStatus.Normalize(run.Status).Equals("Success", StringComparison.OrdinalIgnoreCase)
            && QuickTransferService.SelectRunRecord(new[] { run }, entry.NominalStart, entry.StartAttemptUtc) is not null;
}
