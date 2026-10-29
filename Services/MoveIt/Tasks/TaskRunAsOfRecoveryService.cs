// Restart recovery for accepted Run As Of operations. Never starts or replays a Task run.
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.Upgrade;

namespace MoveItAutomationUI.Services.MoveIt.Tasks;

public enum RunAsOfRecoveryStage { Prepared, Applied, StartPending, Running, RestoreReady, Restoring }
public sealed class RunAsOfRecoveryRecord
{
    public string JobId { get; set; } = "";
    public string TaskId { get; set; } = "";
    public string UndoSnapshotId { get; set; } = "";
    public MoveItTaskKind Kind { get; set; }
    public bool OriginallyEnabled { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public RunAsOfRecoveryStage Stage { get; set; }
    public string NominalStart { get; set; } = "";
    public string[] PriorRuns { get; set; } = [];
    public TaskRunAsOfDefinition.Change[] Changes { get; set; } = [];
    public ObjectEditLeaseRecord Authentication { get; set; } = new();
    public string ReviewReason { get; set; } = "";
}
public interface ITaskRunAsOfRecovery
{
    Task EnsureCanBeginAsync(MoveItSessionState session, JsonObject definition, CancellationToken ct);
    Task<RunAsOfRecoveryRecord> BeginAsync(string jobId, string taskId, MoveItTaskKind kind, bool enabled,
        IReadOnlyList<TaskRunAsOfDefinition.Change> changes, IEnumerable<string> priorRuns, string snapshotId, MoveItSessionState session, CancellationToken ct);
    Task CheckpointAsync(RunAsOfRecoveryRecord record, RunAsOfRecoveryStage stage, MoveItSessionState session, string nominal = "");
    Task FinishAsync(RunAsOfRecoveryRecord record, bool restored, MoveItSessionState session);
}

// The operation file and cleanup tokens use the existing Data Protection key ring, so a
// restart can reconcile using the original account without persisting its password or elevating it.
public sealed class TaskRunAsOfRecoveryService : BackgroundService, ITaskRunAsOfRecovery
{
    private readonly string _directory;
    private readonly IDataProtector _protection;
    private readonly IObjectEditLeaseJournal _tokens;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<TaskRunAsOfRecoveryService> _logger;
    private readonly SemaphoreSlim _files = new(1, 1);
    private readonly ConcurrentDictionary<string, RunAsOfRecoveryRecord> _records = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _live = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _reported = new(StringComparer.Ordinal);

    public TaskRunAsOfRecoveryService(IWebHostEnvironment environment, IDataProtectionProvider protection,
        IObjectEditLeaseJournal tokens, IServiceScopeFactory scopes, ILogger<TaskRunAsOfRecoveryService> logger)
    {
        _directory = Path.Combine(environment.ContentRootPath, "Data", "RunAsOfRecovery");
        _protection = protection.CreateProtector("MAUI.TaskRunAsOf.Recovery.v1");
        _tokens = tokens; _scopes = scopes; _logger = logger;
    }
    private string FilePath(string id) => Path.Combine(_directory, Guid.Parse(id).ToString("N") + ".recovery");
    private static bool SameTask(RunAsOfRecoveryRecord record, MoveItSessionState session, string id)
        => record.TaskId == id && record.Authentication.Server.Equals(session.WebAdminUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    public async Task EnsureCanBeginAsync(MoveItSessionState session, JsonObject definition, CancellationToken ct)
    {
        foreach (var record in _records.Values.Where(record => SameTask(record, session, TaskRunAsOfDefinition.Text(definition, "ID"))))
        {
            if (!_live.ContainsKey(record.JobId) && Original(record, definition)) await DeleteAsync(record, ct);
            else throw new InvalidOperationException("An earlier Run As Of operation still needs recovery. Inspect the Task and restore its original settings from Undo before starting another run. See Configuration > Logs.");
        }
    }
    public async Task<RunAsOfRecoveryRecord> BeginAsync(string jobId, string taskId, MoveItTaskKind kind, bool enabled,
        IReadOnlyList<TaskRunAsOfDefinition.Change> changes, IEnumerable<string> priorRuns, string snapshotId, MoveItSessionState session, CancellationToken ct)
    {
        var record = new RunAsOfRecoveryRecord { JobId = Guid.Parse(jobId).ToString("N"), TaskId = taskId, Kind = kind,
            OriginallyEnabled = enabled, Changes = changes.ToArray(), PriorRuns = priorRuns.ToArray(), UndoSnapshotId = snapshotId,
            Authentication = new() { Server = session.WebAdminUrl.TrimEnd('/'), Username = session.Username } };
        _tokens.ProtectSession(record.Authentication, session);
        // The in-process Task gate rejects a second Run As Of; pending restart records are checked above.
        _live[record.JobId] = 0;
        try { await WriteAsync(record, ct); _records[record.JobId] = record; return record; }
        catch { _live.TryRemove(record.JobId, out _); throw; }
    }
    public async Task CheckpointAsync(RunAsOfRecoveryRecord record, RunAsOfRecoveryStage stage, MoveItSessionState session, string nominal = "")
    {
        record.Stage = stage;
        if (nominal.Length > 0) record.NominalStart = nominal;
        _tokens.ProtectSession(record.Authentication, session);
        await WriteAsync(record, CancellationToken.None);
    }
    public async Task FinishAsync(RunAsOfRecoveryRecord record, bool restored, MoveItSessionState session)
    {
        try
        {
            if (restored) await DeleteAsync(record, CancellationToken.None);
            else
            {
                _tokens.ProtectSession(record.Authentication, session);
                await WriteAsync(record, CancellationToken.None);
                Report(record, "Original Task settings have not yet been confirmed. Restart recovery will reconcile the run without starting it again.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        { _logger.LogError(ex, "Run As Of recovery journal {Operation} needs review for Task {TaskId}.", record.JobId, record.TaskId); }
        finally { _live.TryRemove(record.JobId, out _); }
    }
    private async Task WriteAsync(RunAsOfRecoveryRecord record, CancellationToken ct)
    {
        await _files.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(_directory);
            var path = FilePath(record.JobId); var temp = path + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temp, _protection.Protect(JsonSerializer.Serialize(record)), ct);
                File.Move(temp, path, true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        finally { _files.Release(); }
    }
    private async Task DeleteAsync(RunAsOfRecoveryRecord record, CancellationToken ct)
    {
        await _files.WaitAsync(ct);
        try { File.Delete(FilePath(record.JobId)); _records.TryRemove(record.JobId, out _); _reported.TryRemove(record.JobId, out _); }
        finally { _files.Release(); }
    }
    public override async Task StartAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        await base.StartAsync(ct);
    }
    internal async Task LoadAsync(CancellationToken ct)
    {
        try
        {
            if (Directory.Exists(_directory))
                foreach (var path in Directory.EnumerateFiles(_directory, "*.recovery"))
                {
                    try
                    {
                        var record = JsonSerializer.Deserialize<RunAsOfRecoveryRecord>(_protection.Unprotect(await File.ReadAllTextAsync(path, ct)));
                        if (record is null || !Guid.TryParseExact(record.JobId, "N", out _) || FilePath(record.JobId) != path
                            || !long.TryParse(record.TaskId, out var id) || id <= 0 || record.Authentication is null
                            || !Uri.TryCreate(record.Authentication.Server, UriKind.Absolute, out var server) || server.Scheme is not ("http" or "https")
                            || string.IsNullOrWhiteSpace(record.Authentication.Username) || record.Changes is null || record.Changes.Length == 0
                            || record.Changes.Any(change => change is null || string.IsNullOrEmpty(change.StepId) || string.IsNullOrEmpty(change.Field) || change.Before is null || change.After is null)
                            || record.PriorRuns is null || record.PriorRuns.Any(value => value is null) || record.NominalStart is null || record.ReviewReason is null
                            || record.Kind is not (MoveItTaskKind.Traditional or MoveItTaskKind.Advanced) || !Enum.IsDefined(record.Stage))
                            throw new InvalidOperationException("Invalid Run As Of recovery record.");
                        _records[record.JobId] = record;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException or InvalidOperationException or ArgumentException)
                    { _logger.LogError(ex, "Run As Of recovery record {Record} could not be read. Inspect its Task and Undo history.", Path.GetFileName(path)); }
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _logger.LogError(ex, "Run As Of recovery files could not be enumerated. Inspect Task run state and Undo history before editing affected Tasks."); }
    }
    private static bool Original(RunAsOfRecoveryRecord record, JsonObject current)
    {
        try { VerifyFields(record, current, true); return true; }
        catch (InvalidOperationException) { return false; }
    }
    internal static void VerifyFields(RunAsOfRecoveryRecord record, JsonObject current, bool original)
    {
        var type = TaskRunAsOfDefinition.Text(current, "TT");
        if (TaskRunAsOfDefinition.Text(current, "ID") != record.TaskId
            || (type.Length == 0 ? MoveItTaskKind.Traditional : MoveItTaskKinds.Parse(type)) != record.Kind)
            throw new InvalidOperationException("Task identity or type changed.");
        foreach (var change in record.Changes)
        {
            var matches = TaskRunAsOfDefinition.Steps(current).Where(step => step.Kind.Equals(change.Kind, StringComparison.OrdinalIgnoreCase)
                && TaskRunAsOfDefinition.Text(step.Step, "ID") == change.StepId).ToArray();
            if (matches.Length != 1 || TaskRunAsOfDefinition.Text(matches[0].Step, change.Field) != (original ? change.Before : change.After))
                throw new InvalidOperationException("A temporary Task field was changed. Inspect the Task and its original Undo snapshot; recovery will preserve the current values.");
        }
        if (TaskRunAsOfDefinition.Enabled(current) != (original && record.OriginallyEnabled))
            throw new InvalidOperationException("The Task enabled state was changed. Inspect the Task and its Undo snapshot.");
    }
    internal static bool CompletionConfirmed(RunAsOfRecoveryRecord record, IReadOnlyList<JsonObject> history)
    {
        var candidates = history.Where(row =>
        {
            var id = TaskRunAsOfDefinition.Text(row, "TaskID");
            if (id.Length > 0 && id != record.TaskId) return false;
            var nominal = TaskRunAsOfDefinition.Text(row, "NominalStart");
            return record.NominalStart.Length > 0 ? SameRun(nominal, record.NominalStart)
                : nominal.Length > 0 && !record.PriorRuns.Any(prior => SameRun(prior, nominal));
        }).ToArray();
        return candidates.Length == 1 && DateTimeOffset.TryParse(TaskRunAsOfDefinition.Text(candidates[0], "EndTime"), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var end) && end.Year > 1970;
    }
    private static bool SameRun(string a, string b) => a == b || DateTimeOffset.TryParse(a, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var x)
        && DateTimeOffset.TryParse(b, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var y) && x == y;
    private void Report(RunAsOfRecoveryRecord record, string reason, Exception? error = null)
    {
        // Repeated failed authentication must not flood logs on each reconciliation tick.
        // A changed reason and the first reconciliation after restart still need reporting.
        if (_reported.TryGetValue(record.JobId, out var previous) && previous == reason) return;
        _reported[record.JobId] = reason;
        _logger.LogWarning(error, "Run As Of recovery for Task {TaskId} on {Server}, operation {Operation}, Undo {Snapshot}: {Reason}",
            record.TaskId, record.Authentication.Server, record.JobId, record.UndoSnapshotId, reason);
    }
    internal async Task ReconcileAsync(CancellationToken ct)
    {
        foreach (var record in _records.Values.Where(record => !_live.ContainsKey(record.JobId)).ToArray())
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(30));
            MoveItSessionState? recoverySession = null;
            try
            {
                using var scope = _scopes.CreateScope();
                // Setup/Upgrade suspend writes, just as for the existing MAUI background scheduler.
                if (scope.ServiceProvider.GetService<MauiSetupState>() is { IsConfiguredAtStartup: false }
                    || scope.ServiceProvider.GetService<MauiUpgradeState>() is { IsPending: true }) continue;
                var session = _tokens.ReadSession(record.Authentication);
                recoverySession = session;
                using var authentication = scope.ServiceProvider.GetRequiredService<IMoveItSessionService>().PushBackgroundSession(session);
                using var server = ObjectCheckout.ForServer(session.WebAdminUrl);
                var tasks = scope.ServiceProvider.GetRequiredService<ITaskCrudCoordinator>();
                var rest = scope.ServiceProvider.GetRequiredService<IMoveItRestClient>();
                var current = await tasks.GetDefinitionAsync(record.TaskId, timeout.Token);
                if (Original(record, current)) { await DeleteAsync(record, timeout.Token); continue; }
                if (record.ReviewReason.Length > 0) { Report(record, record.ReviewReason); continue; }
                VerifyFields(record, current, false);
                if ((await rest.GetRunningTasksAsync(timeout.Token)).Any(run => run.TaskId == record.TaskId)) continue;
                if (record.Stage is RunAsOfRecoveryStage.StartPending or RunAsOfRecoveryStage.Running)
                {
                    var history = await rest.GetTaskRunReportAsync("TaskID==" + record.TaskId, "!StartTime", 100, timeout.Token);
                    if (!CompletionConfirmed(record, history))
                    {
                        Report(record, "Run completion is not yet confirmed. The Task remains disabled. Inspect the run state and original Undo snapshot.");
                        continue;
                    }
                }
                // Explicit false/omitted availability is never authority to override a native editor.
                if (MoveItObjectAccessService.Flag(current, "xAlter") != true)
                {
                    Report(record, "Native edit availability could not be confirmed. Recovery will wait; inspect the Task checkout and original Undo snapshot if it remains unavailable.");
                    continue;
                }
                record.Stage = RunAsOfRecoveryStage.Restoring;
                await WriteAsync(record, timeout.Token);
                await tasks.UpdateEditableFreshAsync(record.TaskId, record.Kind, fresh =>
                {
                    // Recheck the same fields under the normal shared checkout. Preserve unrelated edits.
                    if (Original(record, fresh)) return fresh;
                    VerifyFields(record, fresh, false);
                    return TaskRunAsOfDefinition.Apply(fresh, record.Changes, true, record.OriginallyEnabled);
                }, saved => VerifyFields(record, saved, true), true, timeout.Token);
                await DeleteAsync(record, timeout.Token);
                try { scope.ServiceProvider.GetService<IAutomationTaskService>()?.InvalidateCache(); }
                catch (Exception ex) { _logger.LogWarning(ex, "Task cache refresh failed after Run As Of recovery for {TaskId}.", record.TaskId); }
                _logger.LogInformation("Original Task {TaskId} settings were restored after interrupted Run As Of operation {Operation}. No run was replayed.", record.TaskId, record.JobId);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException || ex is MoveItApiException api &&
                ex is not MoveItAuthenticationException && (api.StatusCode is null || (int)api.StatusCode >= 500 || api.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests))
            { Report(record, "MOVEit is unavailable. Recovery was retained and will retry without starting the Task again."); }
            catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or CryptographicException)
            {
                if (record.ReviewReason.Length == 0)
                {
                    record.ReviewReason = "Automatic restoration could not be confirmed. Inspect the Task, checkout and original Undo snapshot before editing. See the recovery operation in Configuration > Logs.";
                    try { await WriteAsync(record, ct); } catch (Exception journalError) when (journalError is IOException or UnauthorizedAccessException) { }
                }
                Report(record, record.ReviewReason, ex);
            }
            finally
            {
                if (recoverySession is not null && _records.ContainsKey(record.JobId))
                {
                    try { _tokens.ProtectSession(record.Authentication, recoverySession); await WriteAsync(record, ct); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or CryptographicException)
                    { _logger.LogWarning(ex, "Run As Of recovery tokens could not be refreshed on disk for {Operation}.", record.JobId); }
                }
            }
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try { do { await ReconcileAsync(stoppingToken); } while (await timer.WaitForNextTickAsync(stoppingToken)); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
