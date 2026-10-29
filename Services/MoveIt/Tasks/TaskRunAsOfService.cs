using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.Metadata;

namespace MoveItAutomationUI.Services.MoveIt.Tasks;

public interface ITaskRunAsOfService
{
    Task<TaskRunAsOfState> RunAsync(string taskId, string jobId, DateTime selected, string username, string owner, string server);
}

// Keeps the request scope alive through restoration. RequestAborted is deliberately not
// propagated after acceptance: closing the browser cannot cancel the restoration workflow.
public sealed class TaskRunAsOfService(ITaskCrudCoordinator tasks, IAutomationTaskService automation,
    IMoveItRestClient rest, ITaskUndoService undo, TaskRunAsOfProgress progress, ILogger<TaskRunAsOfService> logger, ITaskRunAsOfRecovery? recovery = null, IMoveItSessionService? sessions = null) : ITaskRunAsOfService
{
    public async Task<TaskRunAsOfState> RunAsync(string taskId, string jobId, DateTime selected, string username, string owner, string server)
    {
        if (!long.TryParse(taskId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0) throw new ArgumentException("A valid Task ID is required.");
        if (!Guid.TryParse(jobId, out _)) throw new ArgumentException("A valid Run As Of operation ID is required.");
        var taskKey = server + ":" + taskId;
        progress.Begin(owner, taskKey, jobId, taskId);
        IReadOnlyList<TaskRunAsOfDefinition.Change> changes = [];
        var attempted = false; var originallyEnabled = false; var mayRestore = true;
        var restored = true; var ran = false; var runStatus = ""; var failure = "";
        var kind = MoveItTaskKind.Traditional;
        RunAsOfRecoveryRecord? recoveryRecord = null;
        MoveItSessionState? recoverySession = null;
        MoveItSessionState CurrentRecoverySession()
        {
            recoverySession = sessions?.Get() ?? recoverySession
                ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
            recoverySession.CleanupOwnerSessionId = owner;
            return recoverySession;
        }
        void Report(string message, int percent) => progress.Set(jobId, value => value with { Message = message, Progress = percent });
        try
        {
            if (recovery is not null)
            {
                _ = CurrentRecoverySession();
            }
            var definition = await tasks.GetDefinitionAsync(taskId, CancellationToken.None);
            var type = TaskRunAsOfDefinition.Text(definition, "TT");
            kind = type.Length == 0 ? MoveItTaskKind.Traditional : MoveItTaskKinds.Parse(type);
            if (kind == MoveItTaskKind.Synchronization) throw new InvalidOperationException("Run As Of is available for Traditional and Advanced Tasks.");
            if (TaskRunAsOfDefinition.Text(definition, "Scheduled").Equals("INCOMPLETE", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Complete the Task configuration before using Run As Of.");
            foreach (var permission in new[] { "xAlter" })
                if (!ObjectCheckout.CanEditCheckedOut("Task", taskId, !definition.Any(pair => pair.Key.Equals(permission, StringComparison.OrdinalIgnoreCase)) || TaskRunAsOfDefinition.Text(definition, permission) is "1" or "true" or "True"))
                    throw new InvalidOperationException("Run As Of requires permission to run and alter this Task.");
            changes = TaskRunAsOfDefinition.Describe(definition, selected);
            if (changes.Count == 0) throw new InvalidOperationException("No supported date/time macros were found in the Task Source or Destination filenames or folders.");
            if ((await rest.GetRunningTasksAsync(CancellationToken.None)).Any(run => run.TaskId == taskId))
                throw new InvalidOperationException("Wait for the current Task run to finish before using Run As Of.");
            var priorRuns = await rest.GetTaskRunReportAsync("TaskID==" + taskId, "!StartTime", 20, CancellationToken.None);
            var prior = priorRuns.Select(row => TaskRunAsOfDefinition.Text(row, "NominalStart")).Where(value => value.Length != 0).ToHashSet(StringComparer.Ordinal);
            originallyEnabled = TaskRunAsOfDefinition.Enabled(definition);
            if (recovery is not null) await recovery.EnsureCanBeginAsync(CurrentRecoverySession(), definition, CancellationToken.None);
            Report("Saving the original Task to Undo…", 10);
            var snapshot = await undo.CaptureAsync(taskId, username, CancellationToken.None);
            if (recovery is not null)
                recoveryRecord = await recovery.BeginAsync(jobId, taskId, kind, originallyEnabled, changes, prior, snapshot.SnapshotId, CurrentRecoverySession(), CancellationToken.None);
            Report("Applying the selected date and time…", 20);
            attempted = true; restored = false;
            await tasks.UpdateEditableFreshAsync(taskId, kind,
                current => TaskRunAsOfDefinition.Apply(current, changes, false, originallyEnabled),
                current => Verify(current, changes, false, originallyEnabled), true, CancellationToken.None);
            if (recoveryRecord is not null) await recovery!.CheckpointAsync(recoveryRecord, RunAsOfRecoveryStage.Applied, CurrentRecoverySession());
            // Active=0 suppresses scheduled starts during this temporary manual execution.
            if ((await rest.GetRunningTasksAsync(CancellationToken.None)).Any(run => run.TaskId == taskId))
            {
                mayRestore = false;
                Report("Waiting for a concurrent Task run before restoring settings…", 45);
                var concurrentDeadline = DateTimeOffset.UtcNow.AddHours(24);
                while ((await rest.GetRunningTasksAsync(CancellationToken.None)).Any(run => run.TaskId == taskId))
                {
                    if (DateTimeOffset.UtcNow >= concurrentDeadline) throw new InvalidOperationException("A concurrent Task run is still active. Inspect it, then restore the Task from Undo.");
                    await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
                }
                mayRestore = true;
                throw new InvalidOperationException("The Task started concurrently. MAUI did not start another instance.");
            }
            Report("Starting the Task…", 30);
            var nominal = "";
            if (recoveryRecord is not null) await recovery!.CheckpointAsync(recoveryRecord, RunAsOfRecoveryStage.StartPending, CurrentRecoverySession());
            mayRestore = false;
            try
            {
                nominal = (await automation.RunTaskNowAsync(taskId, CancellationToken.None)).NominalStart;
                if (recoveryRecord is not null) await recovery!.CheckpointAsync(recoveryRecord, RunAsOfRecoveryStage.Running, CurrentRecoverySession(), nominal);
            }
            catch (MoveItApiException ex) when (ex.StatusCode is not null && (int)ex.StatusCode >= 400
                && (int)ex.StatusCode < 500 && ex.StatusCode != HttpStatusCode.RequestTimeout)
            { mayRestore = true; throw; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Run As Of start acknowledgement was uncertain for Task {TaskId}; reconciling history before restoration.", taskId);
                Report("Confirming the Task run after an uncertain start response…", 35);
            }
            Report("Task running; waiting for its completion…", 45);
            var deadline = DateTimeOffset.UtcNow.AddHours(24);
            while (true)
            {
                try
                {
                    var running = await rest.GetRunningTasksAsync(CancellationToken.None);
                    var history = await rest.GetTaskRunReportAsync("TaskID==" + taskId, "!StartTime", 20, CancellationToken.None);
                    var completed = history.FirstOrDefault(row =>
                    {
                        var value = TaskRunAsOfDefinition.Text(row, "NominalStart");
                        var matches = nominal.Length > 0 ? SameRun(value, nominal) : value.Length > 0 && !prior.Contains(value);
                        return matches && DateTimeOffset.TryParse(TaskRunAsOfDefinition.Text(row, "EndTime"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var end) && end.Year > 1970;
                    });
                    if (completed is not null && !running.Any(run => run.TaskId == taskId))
                    {
                        ran = true; mayRestore = true;
                        runStatus = TaskRunAsOfDefinition.Text(completed, "StatusMsg");
                        if (runStatus.Length == 0) runStatus = TaskRunAsOfDefinition.Text(completed, "Status");
                        break;
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is MoveItApiException api &&
                    (api.StatusCode is null || api.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)api.StatusCode >= 500))
                {
                    logger.LogWarning(ex, "Run As Of status unavailable for Task {TaskId}; retaining the original Undo snapshot and waiting.", taskId);
                    Report("Reconnecting to confirm completion before restoring the Task…", 50);
                }
                if (DateTimeOffset.UtcNow >= deadline)
                    throw new InvalidOperationException("Run completion could not be confirmed. The Task remains disabled. Inspect its run state, then restore the original Task from Undo.");
                await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            failure = ex.Message;
            logger.LogError(ex, "Run As Of encountered an error for Task {TaskId}.", taskId);
        }
        finally
        {
            if (attempted && mayRestore)
            {
                Report("Restoring the original Task settings…", 85);
                try
                {
                    if (recoveryRecord is not null) await recovery!.CheckpointAsync(recoveryRecord, RunAsOfRecoveryStage.RestoreReady, CurrentRecoverySession());
                    var current = await tasks.GetDefinitionAsync(taskId, CancellationToken.None);
                    var alreadyOriginal = TaskRunAsOfDefinition.Enabled(current) == originallyEnabled;
                    try { Verify(current, changes, true, originallyEnabled); } catch { alreadyOriginal = false; }
                    if (!alreadyOriginal)
                    {
                        if (recoveryRecord is not null) await recovery!.CheckpointAsync(recoveryRecord, RunAsOfRecoveryStage.Restoring, CurrentRecoverySession());
                        await tasks.UpdateEditableFreshAsync(taskId, kind,
                            value => TaskRunAsOfDefinition.Apply(value, changes, true, originallyEnabled),
                            value => Verify(value, changes, true, originallyEnabled), true, CancellationToken.None);
                    }
                    restored = true;
                }
                catch (Exception ex)
                {
                    failure = "The original Task could not be fully restored. Inspect the Task and restore it from its Undo snapshot. " + ex.Message;
                    logger.LogError(ex, "Run As Of restoration requires attention for Task {TaskId}.", taskId);
                }
            }
            if (recoveryRecord is not null)
            {
                await recovery!.FinishAsync(recoveryRecord, restored, CurrentRecoverySession());
                if (!restored) failure += " Recovery details were retained; review Configuration > Logs.";
            }
            try { automation.InvalidateCache(); }
            catch (Exception ex) { logger.LogWarning(ex, "Task cache refresh failed after Run As Of for Task {TaskId}.", taskId); }
            var message = failure.Length > 0 ? failure + (restored && attempted ? " Original Task settings were restored."
                    : attempted && !mayRestore ? " Run completion could not be confirmed. The Task remains disabled. Inspect its run state, then restore the original Task from Undo." : "")
                : $"Task completed{(runStatus.Length > 0 ? ": " + runStatus : "")}. Original Task settings were restored.";
            progress.Set(jobId, value => value with { Message = message, Progress = 100, Completed = true, Restored = restored, Success = ran && restored && failure.Length == 0, RunStatus = runStatus });
            progress.Release(taskKey);
        }
        return progress.Get(owner, jobId)!;
    }

    private static bool SameRun(string left, string right) => left == right ||
        DateTimeOffset.TryParse(left, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var a) &&
        DateTimeOffset.TryParse(right, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var b) && a == b;

    private static void Verify(JsonObject current, IReadOnlyList<TaskRunAsOfDefinition.Change> changes, bool restored, bool originalEnabled)
    {
        foreach (var change in changes)
        {
            var rows = TaskRunAsOfDefinition.Steps(current).Where(step => step.Kind.Equals(change.Kind, StringComparison.OrdinalIgnoreCase) && TaskRunAsOfDefinition.Text(step.Step, "ID") == change.StepId).ToArray();
            if (rows.Length != 1 || TaskRunAsOfDefinition.Text(rows[0].Step, change.Field) != (restored ? change.Before : change.After))
                throw new InvalidOperationException("The saved Task fields could not be confirmed.");
        }
        if (TaskRunAsOfDefinition.Enabled(current) != (restored && originalEnabled)) throw new InvalidOperationException("The Task enabled state could not be confirmed.");
    }
}
