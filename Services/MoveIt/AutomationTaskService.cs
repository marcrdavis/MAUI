// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt.Tasks;

namespace MoveItAutomationUI.Services.MoveIt;

public interface IAutomationTaskService
{
    // Purpose: Retrieve tasks.
    Task<IReadOnlyList<AutomationTask>> GetTasksAsync(CancellationToken cancellationToken = default);
    // Returns the visible Task count without loading Task Run history or running-state enrichment when no fresh cache exists.
    Task<int> GetTaskCountAsync(CancellationToken cancellationToken = default);
    // Returns any existing Task cache immediately for initial navigation and indicates whether it should be reconciled in the background.
    Task<InitialCacheSnapshot<TaskCacheSnapshot>> GetInitialTaskSnapshotAsync(CancellationToken cancellationToken = default);
    // Purpose: Retrieve task snapshot.
    Task<TaskCacheSnapshot> GetTaskSnapshotAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
    // Purpose: Retrieve basic details.
    Task<TaskBasicDetails> GetBasicDetailsAsync(string taskId, CancellationToken cancellationToken = default);
    // Returns the live Task state used by secondary Task action menus, using the same run-eligibility rule as the Tasks grid.
    Task<TaskActionState> GetActionStateAsync(string taskId, CancellationToken cancellationToken = default);
    // Purpose: Retrieve task logs.
    Task<IReadOnlyList<TaskLogSummary>> GetTaskLogsAsync(string taskId, CancellationToken cancellationToken = default);
    // Returns visible Task-to-Task relationships using the same MOVEit reference graph as delete preflight.
    Task<TaskDependencyRelationships> GetTaskDependenciesAsync(string taskId, CancellationToken cancellationToken = default);
    // Purpose: Retrieve task log.
    Task<TaskLogViewerData> GetTaskLogAsync(string taskId, long taskLogId, CancellationToken cancellationToken = default);
    // Purpose: Create task.
    Task<TaskCreationResult> CreateTaskAsync(string name, string taskType, CancellationToken cancellationToken = default);
    // Creates a Task from a complete exported configuration using a new MOVEit-generated ID.
    Task<TaskConfigurationSnapshot> CreateTaskFromConfigurationAsync(string name, string taskXml, bool leaveDisabled, CancellationToken cancellationToken = default);
    // Purpose: Update basic details.
    Task UpdateBasicDetailsAsync(string taskId, string name, string description, string operatorNotes, CancellationToken cancellationToken = default);
    // Reads the complete task configuration used by the per-user undo snapshot.
    Task<TaskConfigurationSnapshot> GetTaskConfigurationSnapshotAsync(string taskId, CancellationToken cancellationToken = default);
    // Restores a complete task configuration from an undo snapshot and invalidates cached task state.
    Task<TaskConfigurationSnapshot> RestoreTaskConfigurationAsync(string taskId, string taskXml, CancellationToken cancellationToken = default);
    // Purpose: Run task now.
    Task<TaskRunNowResult> RunTaskNowAsync(string taskId, CancellationToken cancellationToken = default);
    // Purpose: Validate and snapshot the running task instances before Kill confirmation.
    Task<TaskKillPreflightResult> PreflightKillTaskAsync(string taskId, CancellationToken cancellationToken = default);
    // Purpose: Kill task.
    Task<TaskKillResult> KillTaskAsync(string taskId, IReadOnlyCollection<string> nominalStarts, CancellationToken cancellationToken = default);
    // Reads one Traditional Task's task-level Email Next Actions for the Notifications editor.
    Task<TaskNotificationSnapshot> GetNotificationSnapshotAsync(string taskId, CancellationToken cancellationToken = default);
    // Adds, updates, or removes discrete task-level Email Next Actions while preserving unrelated Next Actions.
    Task<TaskNotificationUpdateResult> UpdateNotificationAsync(string taskId, TaskNotificationUpdateRequest request, CancellationToken cancellationToken = default);
    // Purpose: Set tasks enabled.
    Task<BulkTaskActionResult> SetTasksEnabledAsync(IReadOnlyCollection<string> taskIds, bool enabled, CancellationToken cancellationToken = default);
    // Purpose: Validate task deletion dependencies before confirmation.
    Task<TaskDeletePreflightResult> PreflightDeleteTasksAsync(IReadOnlyCollection<string> taskIds, CancellationToken cancellationToken = default);
    // Purpose: Delete tasks.
    Task<BulkTaskDeleteResult> DeleteTasksAsync(IReadOnlyCollection<string> taskIds, CancellationToken cancellationToken = default);
    // Invalidates the current user/server Task cache after an external full-definition mutation.
    void InvalidateCache();
}

public sealed record TaskCacheSnapshot(IReadOnlyList<AutomationTask> Tasks, DateTimeOffset RefreshedUtc);

public sealed record TaskCreationResult(string Id, string Name, string TaskType);
public sealed record TaskEmailNotification(
    int ActionIndex,
    bool DoIfFailure,
    bool DoIfNoAction,
    bool DoIfSuccess,
    bool IsHtml,
    string HostId,
    string AddressTo,
    string Subject,
    string Message);
public sealed record TaskNotificationSnapshot(string TaskId, string TaskName, string TaskType, IReadOnlyList<TaskEmailNotification> Notifications);
public sealed record TaskNotificationUpdateRequest(
    string Mode,
    int? ActionIndex,
    bool MatchExactResults,
    bool DoIfFailure,
    bool DoIfNoAction,
    bool DoIfSuccess,
    bool IsHtml,
    string HostId,
    string AddressTo,
    bool PreserveExistingRecipients,
    string Subject,
    string Message);
public sealed record TaskNotificationUpdateResult(string TaskId, string TaskName, bool Changed, string Message);

public sealed record TaskConfigurationSnapshot(string Id, string Name, string Xml);
public sealed record TaskRunNowResult(string TaskId, string TaskName, string NominalStart);
public sealed record TaskKillPreflightResult(string TaskId, string TaskName, IReadOnlyList<string> NominalStarts, string Message);
public sealed record TaskKillItem(string NominalStart, bool Success, bool Skipped, string Message);
public sealed record TaskKillResult(string TaskId, string TaskName, IReadOnlyList<TaskKillItem> Items)
{
    public int Killed => Items.Count(item => item.Success && !item.Skipped);
    public int Skipped => Items.Count(item => item.Skipped);
    public int Failed => Items.Count(item => !item.Success);
}
public sealed record BulkTaskActionItem(string Id, string Name, bool Success, bool Skipped, string Message);
public sealed record BulkTaskActionResult(IReadOnlyList<BulkTaskActionItem> Items)
{
    public int Succeeded => Items.Count(item => item.Success && !item.Skipped);
    public int Skipped => Items.Count(item => item.Skipped);
    public int Failed => Items.Count(item => !item.Success);
}
public sealed record TaskDeleteBlocker(string TaskId, string TaskName, string ReferencedByTaskId, string ReferencedByTaskName);
public sealed record TaskDeleteTarget(string TaskId, string TaskName);
public sealed record TaskDeletePreflightResult(IReadOnlyList<string> TaskIds, IReadOnlyList<TaskDeleteTarget> Targets, IReadOnlyList<TaskDeleteBlocker> Blockers, bool HasCycle, string Message);
public sealed record BulkTaskDeleteItem(string Id, string Name, bool Success, string Message);
public sealed record BulkTaskDeleteResult(IReadOnlyList<BulkTaskDeleteItem> Items, string AuthenticationError = "")
{
    public int Succeeded => Items.Count(item => item.Success);
    public int Failed => Items.Count(item => !item.Success);
    public bool AuthenticationFailed => !string.IsNullOrWhiteSpace(AuthenticationError);
}

public sealed class TaskNameAlreadyExistsException : InvalidOperationException
{
    public string TaskName { get; }

    // Initializes the duplicate-name exception with the conflicting Task Name.
    public TaskNameAlreadyExistsException(string taskName)
        : base($"A task named '{taskName}' already exists. Enter a unique Task Name.")
    {
        TaskName = taskName;
    }
}


public sealed record TaskBasicDetails(
    string Id,
    string Name,
    string Description,
    string OperatorNotes,
    string Type,
    string ResourceGroups,
    bool Enabled,
    bool CanEditMoveItFields,
    bool CanRun);

public sealed record TaskActionState(string Id, string Name, bool Enabled, bool Runnable);

public sealed record TaskLogSummary(
    long Id,
    string ExitStatus,
    string LogPath,
    string StartTime);

public sealed record TaskLogViewerData(
    string TaskId,
    string TaskName,
    long LogId,
    string ExitStatus,
    string StartTime,
    string LogPath,
    string Content);

public sealed record TaskDependencyReference(string Id, string Name);
public sealed record TaskDependencyRelationships(
    IReadOnlyList<TaskDependencyReference> CalledBy,
    IReadOnlyList<TaskDependencyReference> Calls);

public sealed class AutomationTaskService : IAutomationTaskService
{
    private const string CachePrefix = "MoveItAutomationUI:Tasks:";

    private readonly IMoveItRestClient _restClient;
    private readonly ITaskCrudCoordinator _crud;
    private readonly IMoveItSessionService _sessionService;
    private readonly IMemoryCache _memoryCache;
    private readonly TaskBehaviorOptions _taskBehaviorOptions;
    private readonly ILogger<AutomationTaskService> _logger;

    // The shared coordinator selects v1 for Traditional and v0 for Advanced/Sync
    // JSON mutations; native XML operations retain COM. Inventory/status reads use
    // REST with a per-user/server cache for normal navigation.
    public AutomationTaskService(
        IMoveItRestClient restClient,
        ITaskCrudCoordinator crud,
        IMoveItSessionService sessionService,
        IMemoryCache memoryCache,
        IOptionsSnapshot<TaskBehaviorOptions> taskBehaviorOptions,
        ILogger<AutomationTaskService> logger)
    {
        _restClient = restClient;
        _crud = crud;
        _sessionService = sessionService;
        _memoryCache = memoryCache;
        _taskBehaviorOptions = taskBehaviorOptions.Value;
        _logger = logger;
    }

    // Returns the current task inventory. Cached data is reused only while it is within the configured backend freshness window.
    public async Task<IReadOnlyList<AutomationTask>> GetTasksAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await GetTaskSnapshotAsync(false, cancellationToken);
        return snapshot.Tasks;
    }

    // Returns a lightweight visible Task count for Dashboard/summary surfaces. A fresh full Task cache is reused when
    // available; otherwise only the Task inventory endpoint is read, avoiding Task Run history/status enrichment.
    public async Task<int> GetTaskCountAsync(CancellationToken cancellationToken = default)
    {
        var cacheKey = GetCacheKey();
        if (_memoryCache.TryGetValue<TaskCacheEntry>(cacheKey, out var cached) && cached is not null && MoveItServiceCache.IsFresh(cached.RefreshedUtc, _taskBehaviorOptions.CacheRefreshSeconds.Value))
            return cached.Tasks.Count;
        return (await _restClient.GetTasksAsync(cancellationToken)).Count;
    }

    // Returns the initial Task snapshot through the shared cached-inventory navigation contract.
    public async Task<InitialCacheSnapshot<TaskCacheSnapshot>> GetInitialTaskSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var cacheKey = GetCacheKey();
        return await MoveItServiceCache.ResolveInitialAsync<TaskCacheEntry, TaskCacheSnapshot>(
            _memoryCache,
            cacheKey,
            _taskBehaviorOptions.CacheRefreshSeconds.Value,
            entry => entry.RefreshedUtc,
            entry => new TaskCacheSnapshot(entry.Tasks, entry.RefreshedUtc),
            token => GetTaskSnapshotAsync(false, token),
            cancellationToken);
    }

    // Returns Task data plus the timestamp of the live MOVEit read that populated the cache. UI refreshes can force a live read.
    public async Task<TaskCacheSnapshot> GetTaskSnapshotAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        var cacheKey = GetCacheKey();
        var refreshed = await RefreshTasksCoreAsync(cacheKey, forceRefresh, cancellationToken);
        return new TaskCacheSnapshot(refreshed.Tasks, refreshed.RefreshedUtc);
    }

    // Reads authoritative Task Information so reopening an editor resolves a stale revision.
    public async Task<TaskBasicDetails> GetBasicDetailsAsync(string taskId, CancellationToken cancellationToken = default)
    {
        // Editing must reopen from the authoritative object, never a cached grid row.
        var task = await _crud.GetDefinitionAsync(taskId, cancellationToken);
        var type = GetTaskType(MoveItRestClient.GetString(task, "TT"));
        var info = task["Info"] as JsonObject;
        return new TaskBasicDetails(
            MoveItRestClient.GetString(task, "ID"),
            MoveItRestClient.GetString(task, "Name"),
            MoveItRestClient.GetString(info, "Description").Trim(),
            MoveItRestClient.GetString(info, "Notes").Trim(),
            type,
            JoinGroups(task["Group"]),
            IsOne(task, "Active"),
            true,
            IsTrue(task, "xRun"));
    }

    // Returns the action state used outside the Tasks grid without treating MOVEit's xRun flag as the grid's run-eligibility rule.
    public async Task<TaskActionState> GetActionStateAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var normalizedTaskId = (taskId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedTaskId)) throw new ArgumentException("Task ID is required.", nameof(taskId));

        // Reuse a fresh Task-grid snapshot when available. Action-state lookups do not need Task Run/running-state enrichment.
        var cacheKey = GetCacheKey();
        if (_memoryCache.TryGetValue<TaskCacheEntry>(cacheKey, out var cachedEntry)
            && cachedEntry is not null
            && MoveItServiceCache.IsFresh(cachedEntry.RefreshedUtc, _taskBehaviorOptions.CacheRefreshSeconds.Value))
        {
            var cached = cachedEntry.Tasks.FirstOrDefault(task => string.Equals(task.Id, normalizedTaskId, StringComparison.OrdinalIgnoreCase));
            if (cached is not null)
            {
                return new TaskActionState(
                    cached.Id,
                    cached.Name,
                    cached.Enabled,
                    !cached.Scheduled.Equals("Incomplete", StringComparison.OrdinalIgnoreCase));
            }
        }

        // Read only the lightweight Task inventory when the display cache is stale or missing; this avoids loading run history just to open a menu.
        var inventory = await _restClient.GetTasksAsync(cancellationToken);
        var task = inventory.FirstOrDefault(item => string.Equals(MoveItRestClient.GetString(item, "ID"), normalizedTaskId, StringComparison.OrdinalIgnoreCase));
        if (task is null) task = await _crud.GetDefinitionAsync(normalizedTaskId, cancellationToken);
        var scheduled = NormalizeScheduled(MoveItRestClient.GetString(task, "Scheduled"));
        return new TaskActionState(
            MoveItRestClient.GetString(task, "ID"),
            MoveItRestClient.GetString(task, "Name"),
            IsOne(task, "Active"),
            !scheduled.Equals("Incomplete", StringComparison.OrdinalIgnoreCase));
    }


    // Retrieves recent task logs only when the Task Logs tab is requested.
    public async Task<IReadOnlyList<TaskLogSummary>> GetTaskLogsAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var logs = await _restClient.GetTaskLogsAsync(taskId, cancellationToken);

        return logs
            .OrderByDescending(log => ParseLogStartTime(log.StartTime))
            .ThenByDescending(log => log.Id)
            .Select(log => new TaskLogSummary(log.Id, log.ExitStatus, log.LogPath, log.StartTime))
            .ToList();
    }

    // Retrieves one retained log and enough task/log metadata for the standalone MAUI log viewer window.
    public async Task<TaskLogViewerData> GetTaskLogAsync(string taskId, long taskLogId, CancellationToken cancellationToken = default)
    {
        var taskDetailsTask = GetBasicDetailsAsync(taskId, cancellationToken);
        var logsTask = _restClient.GetTaskLogsAsync(taskId, cancellationToken);
        var contentTask = _restClient.GetTaskLogTextAsync(taskId, taskLogId, cancellationToken);

        await Task.WhenAll(new Task[] { taskDetailsTask, logsTask, contentTask });

        var task = await taskDetailsTask;
        var logs = await logsTask;
        var content = await contentTask;
        var log = logs.FirstOrDefault(item => item.Id == taskLogId);

        return new TaskLogViewerData(
            taskId,
            task.Name,
            taskLogId,
            log?.ExitStatus ?? string.Empty,
            log?.StartTime ?? string.Empty,
            log?.LogPath ?? string.Empty,
            content);
    }

    // Creates one persistent Task after a live REST exact-name check.
    // The shared Task CRUD coordinator chooses v1 REST for Traditional and captured v0 REST for Advanced/Sync.
    // Purpose: Validate Task uniqueness, dispatch the type-specific create strategy, and invalidate the cached Task view.
    public async Task<TaskCreationResult> CreateTaskAsync(
        string name,
        string taskType,
        CancellationToken cancellationToken = default)
    {
        var normalizedName = (name ?? string.Empty).Trim();
        if (normalizedName.Length == 0) throw new InvalidOperationException("Task Name is required.");
        if (normalizedName.Length > 254) throw new InvalidOperationException("Task Name cannot exceed 254 characters.");

        if (await _restClient.TaskNameExistsAsync(normalizedName, cancellationToken))
        {
            throw new TaskNameAlreadyExistsException(normalizedName);
        }

        var created = await _crud.CreateEmptyAsync(normalizedName, taskType, cancellationToken: cancellationToken);

        // Forces the next grid/background refresh to come from MOVEit so the newly created task and its
        // server-computed status/scheduling fields are represented exactly as MOVEit returns them.
        InvalidateCache();

        return new TaskCreationResult(created.Id, created.Name, created.TaskType);
    }

    // Creates a complete Task from portable configuration XML after a live exact-name collision check.
    public async Task<TaskConfigurationSnapshot> CreateTaskFromConfigurationAsync(string name, string taskXml, bool leaveDisabled, CancellationToken cancellationToken = default)
    {
        var normalizedName = (name ?? string.Empty).Trim();
        if (normalizedName.Length == 0) throw new InvalidOperationException("Task Name is required.");
        if (normalizedName.Length > 254) throw new InvalidOperationException("Task Name cannot exceed 254 characters.");
        if (await _restClient.TaskNameExistsAsync(normalizedName, cancellationToken)) throw new TaskNameAlreadyExistsException(normalizedName);
        var result = await _crud.CreateFromNativeConfigurationAsync(normalizedName, taskXml, leaveDisabled, cancellationToken);
        InvalidateCache();
        return new TaskConfigurationSnapshot(result.Id, result.Name, result.Xml);
    }

    // Delegates common Task Information writes through the shared Task CRUD coordinator, then patches the cache.
    // Task Name validation mirrors CreateTaskAsync and the live duplicate check excludes the Task being edited.
    // Purpose: Validate and save common Task Information through the shared type-selected REST lifecycle.
    public async Task UpdateBasicDetailsAsync(string taskId, string name, string description, string operatorNotes, CancellationToken cancellationToken = default)
    {
        var normalizedTaskId = (taskId ?? string.Empty).Trim();
        var normalizedName = (name ?? string.Empty).Trim();
        if (normalizedTaskId.Length == 0) throw new InvalidOperationException("Task ID is required.");
        if (normalizedName.Length == 0) throw new InvalidOperationException("Task Name is required.");
        if (normalizedName.Length > 254) throw new InvalidOperationException("Task Name cannot exceed 254 characters.");

        if (await _restClient.TaskNameExistsForOtherTaskAsync(normalizedName, normalizedTaskId, cancellationToken))
        {
            throw new TaskNameAlreadyExistsException(normalizedName);
        }

        try { await _crud.UpdateBasicDetailsAsync(normalizedTaskId, normalizedName, description, operatorNotes, cancellationToken); }
        catch { InvalidateCache(); throw; } // A failed acknowledgement/readback may follow a committed PUT.
        PatchCachedTask(normalizedTaskId, normalizedName, description, operatorNotes);
    }

    // Reads a complete task XML snapshot through COM without changing the task or its cache state.
    public async Task<TaskConfigurationSnapshot> GetTaskConfigurationSnapshotAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var result = await _crud.GetNativeConfigurationAsync(taskId, cancellationToken);
        return new TaskConfigurationSnapshot(result.Id, result.Name, result.Xml);
    }

    // Restores a complete task XML snapshot through COM and invalidates the task cache after success.
    public async Task<TaskConfigurationSnapshot> RestoreTaskConfigurationAsync(string taskId, string taskXml, CancellationToken cancellationToken = default)
    {
        var result = await _crud.RestoreNativeConfigurationAsync(taskId, taskXml, cancellationToken);
        InvalidateCache();
        return new TaskConfigurationSnapshot(result.Id, result.Name, result.Xml);
    }

    // Starts a task immediately through REST. MOVEit permits enabled, disabled, scheduled, or
    // unscheduled tasks to be run manually; only an incomplete task is blocked by MAUI. MOVEit
    // remains authoritative for authorization when the start request is submitted.
    // Purpose: Validate task-state eligibility and start the task through REST.
    public async Task<TaskRunNowResult> RunTaskNowAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var normalizedTaskId = (taskId ?? string.Empty).Trim();
        if (normalizedTaskId.Length == 0) throw new InvalidOperationException("Task ID is required.");

        var task = await _crud.GetDefinitionAsync(normalizedTaskId, cancellationToken);
        var actualTaskId = MoveItRestClient.GetString(task, "ID").Trim();
        var taskName = MoveItRestClient.GetString(task, "Name").Trim();
        var scheduled = MoveItRestClient.GetString(task, "Scheduled").Trim();

        if (scheduled.Equals("INCOMPLETE", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Task '{FirstNonBlank(taskName, normalizedTaskId)}' is incomplete and cannot be run.");

        var nominalStart = await _restClient.StartTaskAsync(normalizedTaskId, cancellationToken);
        InvalidateCache();
        return new TaskRunNowResult(FirstNonBlank(actualTaskId, normalizedTaskId), FirstNonBlank(taskName, normalizedTaskId), nominalStart);
    }

    // Snapshots the exact currently-running instances before the destructive Kill confirmation is shown.
    // Accounts for multiple simultaneous runs because MOVEit's kill endpoint requires NominalStart for each one.
    // Purpose: Snapshot the exact running instances before prompting the user to confirm Kill.
    public async Task<TaskKillPreflightResult> PreflightKillTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var normalizedTaskId = (taskId ?? string.Empty).Trim();
        if (normalizedTaskId.Length == 0) throw new InvalidOperationException("Task ID is required.");

        var running = (await _restClient.GetRunningTasksAsync(cancellationToken))
            .Where(item => item.TaskId.Equals(normalizedTaskId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (running.Count == 0)
            return new TaskKillPreflightResult(normalizedTaskId, normalizedTaskId, Array.Empty<string>(), "The task is no longer running.");

        var missingNominalStart = running.FirstOrDefault(item => string.IsNullOrWhiteSpace(item.NominalStart));
        if (missingNominalStart is not null)
            throw new InvalidOperationException("MOVEit reports the task as running but did not return the NominalStart value required to kill the run. Refresh the Tasks grid and try again.");

        var taskName = FirstNonBlank(running[0].TaskName, normalizedTaskId);
        var nominalStarts = running
            .Select(item => item.NominalStart.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new TaskKillPreflightResult(normalizedTaskId, taskName, nominalStarts, string.Empty);
    }

    // Kills only the running instances the user confirmed. A new run that begins after preflight is
    // deliberately not included. Instances that ended naturally before confirmation are treated as skipped.
    // Purpose: Kill only the running task instances that the user explicitly confirmed.
    public async Task<TaskKillResult> KillTaskAsync(
        string taskId,
        IReadOnlyCollection<string> nominalStarts,
        CancellationToken cancellationToken = default)
    {
        var normalizedTaskId = (taskId ?? string.Empty).Trim();
        var requestedStarts = (nominalStarts ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (normalizedTaskId.Length == 0) throw new InvalidOperationException("Task ID is required.");
        if (requestedStarts.Count == 0) throw new InvalidOperationException("No running task instances were selected to kill.");

        var running = (await _restClient.GetRunningTasksAsync(cancellationToken))
            .Where(item => item.TaskId.Equals(normalizedTaskId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var taskName = FirstNonBlank(running.FirstOrDefault()?.TaskName, normalizedTaskId);
        var currentStarts = running
            .Where(item => !string.IsNullOrWhiteSpace(item.NominalStart))
            .Select(item => item.NominalStart.Trim())
            .ToHashSet(StringComparer.Ordinal);

        var results = new List<TaskKillItem>(requestedStarts.Count);
        try
        {
            foreach (var nominalStart in requestedStarts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!currentStarts.Contains(nominalStart))
                {
                    results.Add(new TaskKillItem(nominalStart, true, true, "The run already ended before Kill was confirmed."));
                    continue;
                }

                try
                {
                    await _restClient.KillTaskAsync(normalizedTaskId, nominalStart, cancellationToken);
                    results.Add(new TaskKillItem(nominalStart, true, false, "Killed."));
                }
                catch (MoveItAuthenticationException)
                {
                    throw;
                }
                catch (MoveItApiException ex)
                {
                    _logger.LogWarning(ex, "Unable to kill MOVEit task {TaskId} ({TaskName}) run {NominalStart}.", normalizedTaskId, taskName, nominalStart);
                    results.Add(new TaskKillItem(nominalStart, false, false, ex.Message));
                }
            }

            return new TaskKillResult(normalizedTaskId, taskName, results);
        }
        finally
        {
            if (results.Any(item => item.Success && !item.Skipped)) InvalidateCache();
        }
    }

    // Reads task-level Email Next Actions only. FileDone notifications and Run Task actions are deliberately excluded.
    public async Task<TaskNotificationSnapshot> GetNotificationSnapshotAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var id = (taskId ?? string.Empty).Trim();
        if (id.Length == 0) throw new ArgumentException("Task ID is required.", nameof(taskId));
        var task = await _crud.GetDefinitionAsync(id, cancellationToken);
        var type = GetTaskType(MoveItRestClient.GetString(task, "TT"));
        var name = MoveItRestClient.GetString(task, "Name").Trim();
        var actions = GetOrCreateNextActionArray(task);
        var notifications = actions
            .Select((node, index) => node is JsonObject action && IsTaskEmailNextAction(action) ? ToTaskEmailNotification(index, action) : null)
            .Where(item => item is not null)
            .Cast<TaskEmailNotification>()
            .ToList();
        return new TaskNotificationSnapshot(id, string.IsNullOrWhiteSpace(name) ? id : name, type, notifications);
    }

    // Updates one selected notification for a single Task, or exact result-combination matches for bulk operations.
    // Every unrelated NextAction is preserved exactly as returned by MOVEit.
    public async Task<TaskNotificationUpdateResult> UpdateNotificationAsync(string taskId, TaskNotificationUpdateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = (taskId ?? string.Empty).Trim();
        if (id.Length == 0) throw new ArgumentException("Task ID is required.", nameof(taskId));
        var task = await _crud.GetDefinitionAsync(id, cancellationToken);
        var type = GetTaskType(MoveItRestClient.GetString(task, "TT"));
        var name = MoveItRestClient.GetString(task, "Name").Trim();
        if (!type.Equals("Traditional", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Notifications can be managed only for Traditional Tasks.");

        // Full-definition PUT must not silently replace another user's concurrent
        // NextAction changes between the initial read and the shared fresh read.
        var originalNextActions = task["NextActions"]?.ToJsonString();
        var actions = GetOrCreateNextActionArray(task);
        var existing = GetIndexedTaskEmailNextActions(actions);
        var mode = (request.Mode ?? string.Empty).Trim();
        if (!mode.Equals("upsert", StringComparison.OrdinalIgnoreCase) && !mode.Equals("remove", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Notification action must be Add/Update or Remove.");

        if (mode.Equals("remove", StringComparison.OrdinalIgnoreCase))
        {
            List<IndexedTaskEmailAction> matches;
            if (request.ActionIndex.HasValue)
            {
                matches = existing.Where(item => item.Index == request.ActionIndex.Value).ToList();
            }
            else
            {
                if (!HasSelectedNotificationResult(request))
                    throw new InvalidOperationException("Select at least one notification result: Failure, No Transfer, or Success.");
                matches = existing.Where(item => IsExactNotificationResultMatch(item.Action, request)).ToList();
            }

            if (matches.Count == 0)
                return new TaskNotificationUpdateResult(id, string.IsNullOrWhiteSpace(name) ? id : name, false, "No matching task-level Email notification exists.");

            foreach (var item in matches) actions.Remove(item.Action);
            await _crud.UpdateTraditionalFreshAsync(id, fresh =>
            {
                if (!string.Equals(fresh["NextActions"]?.ToJsonString(), originalNextActions, StringComparison.Ordinal))
                    throw new InvalidOperationException("Task notifications changed since they were loaded. Reopen Edit Notifications before saving.");
                fresh["NextActions"] = task["NextActions"]?.DeepClone();
                return fresh;
            }, cancellationToken: cancellationToken);
            InvalidateCache();
            return new TaskNotificationUpdateResult(id, string.IsNullOrWhiteSpace(name) ? id : name, true,
                matches.Count == 1 ? "Notification removed." : $"{matches.Count} matching notifications removed.");
        }

        if (!HasSelectedNotificationResult(request))
            throw new InvalidOperationException("Select at least one notification result: Failure, No Transfer, or Success.");

        var hostId = (request.HostId ?? string.Empty).Trim();
        var addressTo = NormalizeRecipients(request.AddressTo);
        var subject = (request.Subject ?? string.Empty).Trim();
        var message = request.Message ?? string.Empty;
        if (hostId.Length == 0) throw new InvalidOperationException("Select an SMTP Host.");
        if (subject.Length == 0) throw new InvalidOperationException("Subject is required.");
        if (message.Trim().Length == 0) throw new InvalidOperationException("Message Body is required.");

        var targets = new List<JsonObject>();
        var created = false;
        if (request.ActionIndex.HasValue)
        {
            var selected = existing.FirstOrDefault(item => item.Index == request.ActionIndex.Value);
            if (selected is null)
                throw new InvalidOperationException("The selected notification no longer exists. Close and reopen Edit Notifications, then try again.");
            targets.Add(selected.Action);
        }
        else if (request.MatchExactResults)
        {
            targets.AddRange(existing.Where(item => IsExactNotificationResultMatch(item.Action, request)).Select(item => item.Action));
            if (targets.Count == 0)
            {
                var added = new JsonObject();
                actions.Add(added);
                targets.Add(added);
                created = true;
            }
        }
        else
        {
            var added = new JsonObject();
            actions.Add(added);
            targets.Add(added);
            created = true;
        }

        var preserveRecipientsForUpdate = request.PreserveExistingRecipients && !created;
        if (!preserveRecipientsForUpdate && addressTo.Length == 0)
            throw new InvalidOperationException("Enter at least one recipient.");

        foreach (var target in targets)
            ApplyEmailNotificationValues(target, request, hostId, addressTo, subject, message, preserveRecipientsForUpdate);

        await _crud.UpdateTraditionalFreshAsync(id, fresh =>
            {
                if (!string.Equals(fresh["NextActions"]?.ToJsonString(), originalNextActions, StringComparison.Ordinal))
                    throw new InvalidOperationException("Task notifications changed since they were loaded. Reopen Edit Notifications before saving.");
                fresh["NextActions"] = task["NextActions"]?.DeepClone();
                return fresh;
            }, cancellationToken: cancellationToken);
        InvalidateCache();
        var resultMessage = created
            ? "Notification added."
            : targets.Count == 1 ? "Notification updated." : $"{targets.Count} matching notifications updated.";
        return new TaskNotificationUpdateResult(id, string.IsNullOrWhiteSpace(name) ? id : name, true, resultMessage);
    }

    private sealed record IndexedTaskEmailAction(int Index, JsonObject Action);

    private static List<IndexedTaskEmailAction> GetIndexedTaskEmailNextActions(JsonArray actions)
        => actions.Select((node, index) => new { node, index })
            .Where(item => item.node is JsonObject action && IsTaskEmailNextAction(action))
            .Select(item => new IndexedTaskEmailAction(item.index, (JsonObject)item.node!))
            .ToList();

    private static bool IsTaskEmailNextAction(JsonObject action)
        => MoveItRestClient.GetString(action, "Type").Equals("Email", StringComparison.OrdinalIgnoreCase)
            && MoveItRestClient.GetString(action, "DoAfter").Equals("Task", StringComparison.OrdinalIgnoreCase);

    private static bool HasSelectedNotificationResult(TaskNotificationUpdateRequest request)
        => request.DoIfFailure || request.DoIfNoAction || request.DoIfSuccess;

    private static bool IsExactNotificationResultMatch(JsonObject action, TaskNotificationUpdateRequest request)
        => IsOne(action, "DoIfFailure") == request.DoIfFailure
            && IsOne(action, "DoIfNoAction") == request.DoIfNoAction
            && IsOne(action, "DoIfSuccess") == request.DoIfSuccess;

    private static void ApplyEmailNotificationValues(JsonObject target, TaskNotificationUpdateRequest request, string hostId, string addressTo, string subject, string message, bool preserveRecipients)
    {
        target["Type"] = "Email";
        target["DoAfter"] = "Task";
        target["DoIfFailure"] = request.DoIfFailure ? 1 : 0;
        target["DoIfNoAction"] = request.DoIfNoAction ? 1 : 0;
        target["DoIfSuccess"] = request.DoIfSuccess ? 1 : 0;
        target["IsHTML"] = request.IsHtml ? 1 : 0;
        target["HostID"] = hostId;
        if (!preserveRecipients) target["AddressTo"] = addressTo;
        target["Subject"] = subject;
        target["Message"] = message;
    }

    // Normalizes MOVEit's NextAction collection while preserving a single-object response shape if one is ever returned.
    private static JsonArray GetOrCreateNextActionArray(JsonObject task)
    {
        var nextActions = task["NextActions"] as JsonObject;
        if (nextActions is null)
        {
            nextActions = new JsonObject();
            task["NextActions"] = nextActions;
        }
        if (nextActions["NextAction"] is JsonArray array) return array;
        var result = new JsonArray();
        if (nextActions["NextAction"] is JsonObject single)
        {
            nextActions.Remove("NextAction");
            result.Add(single);
        }
        nextActions["NextAction"] = result;
        return result;
    }

    private static TaskEmailNotification ToTaskEmailNotification(int actionIndex, JsonObject action)
        => new(actionIndex, IsOne(action, "DoIfFailure"), IsOne(action, "DoIfNoAction"), IsOne(action, "DoIfSuccess"), IsOne(action, "IsHTML"),
            MoveItRestClient.GetString(action, "HostID").Trim(), MoveItRestClient.GetString(action, "AddressTo").Trim(),
            MoveItRestClient.GetString(action, "Subject"), MoveItRestClient.GetString(action, "Message"));

    private static string NormalizeRecipients(string? value)
        => string.Join(",", (value ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => item.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));

    // Purpose: Set tasks enabled.
    public async Task<BulkTaskActionResult> SetTasksEnabledAsync(IReadOnlyCollection<string> taskIds, bool enabled, CancellationToken cancellationToken = default)
    {
        var ids = NormalizeTaskIds(taskIds);
        if (ids.Count == 0) throw new InvalidOperationException("Select at least one task.");

        var snapshot = await GetTaskSnapshotAsync(true, cancellationToken);
        var byId = snapshot.Tasks.ToDictionary(task => task.Id, StringComparer.OrdinalIgnoreCase);
        var results = new List<BulkTaskActionItem>(ids.Count);
        var mutationAttempted = false;

        try
        {
            foreach (var id in ids)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!byId.TryGetValue(id, out var task))
                {
                    results.Add(new BulkTaskActionItem(id, id, false, false, "Task was not found or is no longer accessible."));
                    continue;
                }
                if (task.Enabled == enabled)
                {
                    results.Add(new BulkTaskActionItem(task.Id, task.Name, true, true, enabled ? "Task is already enabled." : "Task is already disabled."));
                    continue;
                }
                if (enabled && (task.Scheduled.Equals("Incomplete", StringComparison.OrdinalIgnoreCase) || task.Scheduled.Equals("Unscheduled", StringComparison.OrdinalIgnoreCase)))
                {
                    results.Add(new BulkTaskActionItem(task.Id, task.Name, false, false,
                        task.Scheduled.Equals("Incomplete", StringComparison.OrdinalIgnoreCase) ? "Task is incomplete and cannot be enabled." : "Task has no schedule and cannot be enabled."));
                    continue;
                }

                try
                {
                    mutationAttempted = true;
                    await _crud.SetEnabledAsync(task.Id, enabled, cancellationToken);
                    results.Add(new BulkTaskActionItem(task.Id, task.Name, true, false, enabled ? "Enabled." : "Disabled."));
                }
                catch (MoveItAuthenticationException)
                {
                    // Treats authentication failures as session-wide and let the
                    // controller return 401 so the browser follows the same sign-in flow as other MAUI operations.
                    throw;
                }
                catch (Exception ex) when (ex is MoveItComException or MoveItApiException or InvalidOperationException or ArgumentException)
                {
                    _logger.LogWarning(ex, "Unable to {Action} MOVEit task {TaskId} ({TaskName}).", enabled ? "enable" : "disable", task.Id, task.Name);
                    results.Add(new BulkTaskActionItem(task.Id, task.Name, false, false, ex.Message));
                }
            }

            return new BulkTaskActionResult(results);
        }
        finally
        {
            // Invalidates cached task state because some selected tasks may already have changed if a later task fails, the session is
            // lost, or the request is canceled. Never leave a cache populated with pre-change state.
            if (mutationAttempted) InvalidateCache();
        }
    }

    // Returns the visible tasks that call the selected Task and the visible tasks called by it.
    // Uses the exact same reference graph as delete preflight so dependency reporting and delete safety cannot drift.
    public async Task<TaskDependencyRelationships> GetTaskDependenciesAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var normalizedTaskId = (taskId ?? string.Empty).Trim();
        if (normalizedTaskId.Length == 0) throw new ArgumentException("Task ID is required.", nameof(taskId));

        var definitions = await _restClient.GetTaskDependencyDefinitionsAsync(cancellationToken);
        var snapshot = BuildDependencySnapshot(definitions);
        if (!snapshot.Names.ContainsKey(normalizedTaskId))
            throw new InvalidOperationException("The selected task no longer exists or is no longer accessible. Refresh the task list and try again.");

        var calls = snapshot.Graph.TryGetValue(normalizedTaskId, out var targets)
            ? targets.Where(snapshot.Names.ContainsKey)
                .Select(id => new TaskDependencyReference(id, snapshot.Names[id]))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : new List<TaskDependencyReference>();

        var calledBy = snapshot.Graph
            .Where(pair => !pair.Key.Equals(normalizedTaskId, StringComparison.OrdinalIgnoreCase)
                && pair.Value.Contains(normalizedTaskId)
                && snapshot.Names.ContainsKey(pair.Key))
            .Select(pair => new TaskDependencyReference(pair.Key, snapshot.Names[pair.Key]))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new TaskDependencyRelationships(calledBy, calls);
    }

    // Purpose: Validate task deletion dependencies before confirmation.
    public async Task<TaskDeletePreflightResult> PreflightDeleteTasksAsync(IReadOnlyCollection<string> taskIds, CancellationToken cancellationToken = default)
    {
        var ids = NormalizeTaskIds(taskIds);
        if (ids.Count == 0) throw new InvalidOperationException("Select at least one task.");
        var definitions = await _restClient.GetTaskDependencyDefinitionsAsync(cancellationToken);
        return BuildDeletePreflight(ids, BuildDependencySnapshot(definitions));
    }

    // Purpose: Delete tasks.
    public async Task<BulkTaskDeleteResult> DeleteTasksAsync(IReadOnlyCollection<string> taskIds, CancellationToken cancellationToken = default)
    {
        var ids = NormalizeTaskIds(taskIds);
        if (ids.Count == 0) throw new InvalidOperationException("Select at least one task.");

        var definitions = await _restClient.GetTaskDependencyDefinitionsAsync(cancellationToken);
        var dependencySnapshot = BuildDependencySnapshot(definitions);
        var preflight = BuildDeletePreflight(ids, dependencySnapshot);
        if (preflight.Blockers.Count > 0 || preflight.HasCycle || !string.IsNullOrWhiteSpace(preflight.Message))
            throw new InvalidOperationException(preflight.Message);

        var names = dependencySnapshot.Names;
        var graph = dependencySnapshot.Graph;
        var deleteOrder = GetDeleteOrder(ids, graph, out var hasCycle);
        if (hasCycle) throw new InvalidOperationException("The selected tasks contain a circular task reference and cannot be safely deleted as a bulk operation.");

        var results = new List<BulkTaskDeleteItem>(ids.Count);
        string authenticationError = string.Empty;
        try
        {
            for (var index = 0; index < deleteOrder.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var id = deleteOrder[index];
                var name = names.TryGetValue(id, out var taskName) && !string.IsNullOrWhiteSpace(taskName) ? taskName : id;
                try
                {
                    await _crud.DeleteAsync(id, cancellationToken);
                    results.Add(new BulkTaskDeleteItem(id, name, true, "Deleted."));
                }
                catch (MoveItAuthenticationException ex)
                {
                    // Preserves earlier successful deletions when a later delete in the same bulk request loses authentication.
                    // those successes so the controller can clean MAUI metadata before returning 401.
                    authenticationError = ex.Message;
                    break;
                }
                catch (MoveItApiException ex)
                {
                    _logger.LogWarning(ex, "Unable to delete MOVEit task {TaskId} ({TaskName}). Remaining selected tasks will not be deleted.", id, name);
                    results.Add(new BulkTaskDeleteItem(id, name, false, ex.Message));

                    // Stops deletion after a failure because delete order places referrers before referenced tasks and the failed
                    // task still exists and may continue to reference tasks later in the order. Stop here
                    // rather than risk creating a dangling reference or an avoidable partial delete.
                    for (var remainingIndex = index + 1; remainingIndex < deleteOrder.Count; remainingIndex++)
                    {
                        var remainingId = deleteOrder[remainingIndex];
                        var remainingName = names.TryGetValue(remainingId, out var remainingTaskName) && !string.IsNullOrWhiteSpace(remainingTaskName)
                            ? remainingTaskName
                            : remainingId;
                        results.Add(new BulkTaskDeleteItem(remainingId, remainingName, false, "Not attempted because an earlier task deletion failed."));
                    }
                    break;
                }
            }

            return new BulkTaskDeleteResult(results, authenticationError);
        }
        finally
        {
            // Invalidates the cache after partial deletion so later failures or cancellation cannot leave deleted tasks cached.
            if (results.Any(item => item.Success)) InvalidateCache();
        }
    }

    // Purpose: Build delete preflight.
    private static TaskDeletePreflightResult BuildDeletePreflight(IReadOnlyList<string> selectedIds, TaskDependencySnapshot dependencySnapshot)
    {
        var selected = selectedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = dependencySnapshot.Names;
        var targets = selectedIds.Select(id => new TaskDeleteTarget(id, names.GetValueOrDefault(id, id))).ToList();
        var missing = selected.Where(id => !names.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            return new TaskDeletePreflightResult(selectedIds, targets, Array.Empty<TaskDeleteBlocker>(), false, $"{missing.Count} selected task(s) no longer exist or are no longer accessible. Refresh the task list and try again.");

        var graph = dependencySnapshot.Graph;
        var blockers = new List<TaskDeleteBlocker>();
        foreach (var pair in graph)
        {
            if (selected.Contains(pair.Key)) continue;
            foreach (var targetId in pair.Value.Where(selected.Contains))
            {
                blockers.Add(new TaskDeleteBlocker(targetId, names.GetValueOrDefault(targetId, targetId), pair.Key, names.GetValueOrDefault(pair.Key, pair.Key)));
            }
        }

        if (blockers.Count > 0)
        {
            var first = blockers[0];
            var message = blockers.Count == 1
                ? $"Cannot delete '{first.TaskName}' because it is referenced by task '{first.ReferencedByTaskName}'."
                : $"Cannot delete the selected tasks because {blockers.Count} task reference(s) exist outside the selection.";
            return new TaskDeletePreflightResult(selectedIds, targets, blockers, false, message);
        }

        _ = GetDeleteOrder(selectedIds, graph, out var cycle);
        if (cycle)
            return new TaskDeletePreflightResult(selectedIds, targets, Array.Empty<TaskDeleteBlocker>(), true, "The selected tasks contain a circular task reference and cannot be safely deleted as a bulk operation.");

        return new TaskDeletePreflightResult(selectedIds, targets, Array.Empty<TaskDeleteBlocker>(), false, string.Empty);
    }

    private sealed record TaskDependencySnapshot(
        Dictionary<string, string> Names,
        Dictionary<string, HashSet<string>> Graph);

    // Builds the canonical Task dependency snapshot consumed by both Information and delete safety.
    private static TaskDependencySnapshot BuildDependencySnapshot(IReadOnlyList<JsonObject> definitions)
        => new(BuildTaskNameMap(definitions), BuildReferenceGraph(definitions));

    // Purpose: Build task name map.
    private static Dictionary<string, string> BuildTaskNameMap(IReadOnlyList<JsonObject> definitions)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var task in definitions)
        {
            var id = MoveItRestClient.GetString(task, "ID").Trim();
            if (id.Length > 0) result[id] = MoveItRestClient.GetString(task, "Name").Trim();
        }
        return result;
    }

    // Purpose: Build reference graph.
    private static Dictionary<string, HashSet<string>> BuildReferenceGraph(IReadOnlyList<JsonObject> definitions)
    {
        var graph = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var task in definitions)
        {
            var sourceId = MoveItRestClient.GetString(task, "ID").Trim();
            if (sourceId.Length == 0) continue;
            var targets = graph.TryGetValue(sourceId, out var existing) ? existing : (graph[sourceId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            if (task["NextActions"] is JsonObject nextActions && nextActions["NextAction"] is JsonArray nextActionArray)
            {
                foreach (var action in nextActionArray.OfType<JsonObject>())
                {
                    if (!MoveItRestClient.GetString(action, "Type").Equals("Task", StringComparison.OrdinalIgnoreCase)) continue;
                    var targetId = MoveItRestClient.GetString(action, "TaskID").Trim();
                    if (targetId.Length > 0) targets.Add(targetId);
                }
            }
            CollectRunTaskReferences(task["steps"], targets);
        }
        return graph;
    }

    // Purpose: Collect run task references.
    private static void CollectRunTaskReferences(JsonNode? node, HashSet<string> targets)
    {
        if (node is JsonObject obj)
        {
            // Supports both documented Run Task representations because MOVEit REST describes the advanced step through the
            // MicRunTaskType schema, while task payloads may expose the friendlier RunTask key.
            // Support both documented shapes so dependency preflight does not miss a reference.
            CollectRunTaskReference(obj["RunTask"] as JsonObject, targets);
            CollectRunTaskReference(obj["MicRunTaskType"] as JsonObject, targets);

            foreach (var property in obj) CollectRunTaskReferences(property.Value, targets);
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array) CollectRunTaskReferences(child, targets);
        }
    }

    // Purpose: Collect run task reference.
    private static void CollectRunTaskReference(JsonObject? runTask, HashSet<string> targets)
    {
        if (runTask is null) return;
        var targetId = MoveItRestClient.GetString(runTask, "TaskID").Trim();
        if (targetId.Length > 0) targets.Add(targetId);
    }

    // Purpose: Retrieve delete order.
    private static IReadOnlyList<string> GetDeleteOrder(IReadOnlyList<string> selectedIds, IReadOnlyDictionary<string, HashSet<string>> graph, out bool hasCycle)
    {
        var selected = selectedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var postorder = new List<string>();
        var cycleDetected = false;

        // Purpose: Perform the depth-first traversal used to order dependent task deletions.
        void Visit(string id)
        {
            // Tracks cycle state locally because C# local functions cannot capture ref/out/in parameters, then
            // assign the out parameter after traversal is complete.
            if (cycleDetected) return;
            if (state.TryGetValue(id, out var current))
            {
                if (current == 1) cycleDetected = true;
                return;
            }

            state[id] = 1;
            if (graph.TryGetValue(id, out var targets))
            {
                foreach (var target in targets.Where(selected.Contains)) Visit(target);
            }
            state[id] = 2;
            postorder.Add(id);
        }

        foreach (var id in selectedIds) Visit(id);
        hasCycle = cycleDetected;
        postorder.Reverse();
        return postorder;
    }

    // Purpose: Normalize task ids.
    private static IReadOnlyList<string> NormalizeTaskIds(IEnumerable<string>? taskIds)
        => (taskIds ?? Array.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    // Loads task definitions, documented v1 running-task state, and run history concurrently.
    // The browser does not receive rows until this complete grid model is ready, so visible columns never appear half-populated.
    private async Task<IReadOnlyList<AutomationTask>> LoadTasksFromMoveItAsync(CancellationToken cancellationToken)
    {
        var taskDefinitionsTask = _restClient.GetTaskGridDefinitionsAsync(cancellationToken);
        var runningTasksTask = TryGetRunningTasksAsync(cancellationToken);
        var taskRunsTask = TryGetTaskRunsAsync(cancellationToken);

        await Task.WhenAll(taskDefinitionsTask, runningTasksTask, taskRunsTask);

        var taskJson = await taskDefinitionsTask;
        var runningTasks = await runningTasksTask;
        var taskRuns = await taskRunsTask;

        var runningByTaskId = runningTasks
            .GroupBy(item => item.TaskId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var latestRunByTaskId = taskRuns
            .OrderByDescending(item => item.StartTime)
            .GroupBy(item => item.TaskId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return taskJson.Select(task => MapTask(task, runningByTaskId, latestRunByTaskId)).ToList();
    }

    // Resolves full Task inventory through the shared MAUI cache contract.
    private Task<TaskCacheEntry> RefreshTasksCoreAsync(string cacheKey, bool forceRefresh, CancellationToken cancellationToken)
        => MoveItServiceCache.ResolveAsync<TaskCacheEntry>(
            _memoryCache,
            cacheKey,
            _taskBehaviorOptions.CacheRefreshSeconds.Value,
            forceRefresh,
            entry => entry.RefreshedUtc,
            async token =>
            {
                var tasks = await LoadTasksFromMoveItAsync(token);
                return new TaskCacheEntry(tasks, DateTimeOffset.UtcNow);
            },
            cancellationToken);

    // Purpose: Stores task cache.
    private void StoreTaskCache(string cacheKey, TaskCacheEntry entry)
    {
        _memoryCache.Set(cacheKey, entry, new MemoryCacheEntryOptions
        {
            SlidingExpiration = TimeSpan.FromHours(12)
        });
    }


    // Removes only the current user's/server's Task cache entry after a configuration create/delete operation.
    public void InvalidateCache()
    {
        _memoryCache.Remove(GetCacheKey());
    }

    // Updates the cached display fields after a successful COM save so the following page reload does not need a live REST refresh.
    private void PatchCachedTask(string taskId, string name, string description, string operatorNotes)
    {
        var cacheKey = GetCacheKey();
        if (!_memoryCache.TryGetValue<TaskCacheEntry>(cacheKey, out var entry) || entry is null) return;

        var changed = false;
        var updated = new List<AutomationTask>(entry.Tasks.Count);
        foreach (var task in entry.Tasks)
        {
            if (!string.Equals(task.Id, taskId, StringComparison.OrdinalIgnoreCase))
            {
                updated.Add(task);
                continue;
            }

            changed = true;
            updated.Add(new AutomationTask
            {
                Id = task.Id,
                Name = name ?? string.Empty,
                Description = description ?? string.Empty,
                OperatorNotes = operatorNotes ?? string.Empty,
                Type = task.Type,
                Enabled = task.Enabled,
                Scheduled = task.Scheduled,
                Schedule = task.Schedule,
                Status = task.Status,
                LastRun = task.LastRun,
                LastResult = task.LastResult,
                FilesSent = task.FilesSent,
                TotalBytesSent = task.TotalBytesSent,
                Group = task.Group,
                CanEditMoveItFields = task.CanEditMoveItFields,
                CanAddEditDelete = task.CanAddEditDelete,
                CanRun = task.CanRun
            });
        }

        if (changed)
        {
            StoreTaskCache(cacheKey, new TaskCacheEntry(updated, entry.RefreshedUtc));
        }
    }

    // Builds a cache key that is isolated by MOVEit server and user identity.
    private string GetCacheKey() => MoveItServiceCache.UserScopedKey(CachePrefix, _sessionService);

    // Parses MOVEit's log start time only for sorting. The original value is retained for browser/local formatting.
    private static DateTimeOffset ParseLogStartTime(string value)
    {
        return DateTimeOffset.TryParse(value, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
    }

    // Purpose: Map task.
    private static AutomationTask MapTask(
        JsonObject task,
        IReadOnlyDictionary<string, MoveItRunningTask> runningByTaskId,
        IReadOnlyDictionary<string, MoveItTaskRunRecord> latestRunByTaskId)
    {
        var id = MoveItRestClient.GetString(task, "ID");
        var type = GetTaskType(MoveItRestClient.GetString(task, "TT"));
        var enabled = IsOne(task, "Active");
        var info = task["Info"] as JsonObject;
        runningByTaskId.TryGetValue(id, out var running);
        latestRunByTaskId.TryGetValue(id, out var lastRun);

        var status = running is not null ? "Running" : NormalizeRunStatus(lastRun, enabled);
        var lastResult = running is not null
            ? FirstNonBlank(running.LastErrorText, running.Status, "Running")
            : FirstNonBlank(lastRun?.StatusMessage, lastRun?.Status, enabled ? "—" : "Disabled");

        return new AutomationTask
        {
            Id = id,
            Name = MoveItRestClient.GetString(task, "Name"),
            Description = MoveItRestClient.GetString(info, "Description").Trim(),
            OperatorNotes = MoveItRestClient.GetString(info, "Notes").Trim(),
            Type = type,
            Enabled = enabled,
            Scheduled = NormalizeScheduled(MoveItRestClient.GetString(task, "Scheduled")),
            Schedule = AutomationScheduleDescriptionFormatter.DescribeTaskSchedules(task["Schedules"]),
            Status = status,
            LastRun = lastRun?.StartTime,
            LastResult = lastResult,
            FilesSent = lastRun?.FilesSent,
            TotalBytesSent = lastRun?.TotalBytesSent,
            Group = JoinGroups(task["Group"]),
            // Explicit effective flags are authoritative; detail checks handle omitted projections.
            CanEditMoveItFields = task["xAlter"] is null && task["xAddEditDelete"] is null
                || IsTrue(task, "xAlter") || IsTrue(task, "xAddEditDelete"),
            CanAddEditDelete = IsTrue(task, "xAddEditDelete"),
            CanRun = IsTrue(task, "xRun")
        };
    }

    // Purpose: Try to retrieve task runs.
    private async Task<IReadOnlyList<MoveItTaskRunRecord>> TryGetTaskRunsAsync(CancellationToken cancellationToken)
    {
        try { return await _restClient.GetTaskRunsAsync(cancellationToken); }
        catch (MoveItApiException ex)
        {
            _logger.LogWarning(ex, "Task-run history could not be loaded; task status history will be unavailable.");
            return new List<MoveItTaskRunRecord>();
        }
    }

    // Purpose: Try to retrieve running tasks.
    private async Task<IReadOnlyList<MoveItRunningTask>> TryGetRunningTasksAsync(CancellationToken cancellationToken)
    {
        try { return await _restClient.GetRunningTasksAsync(cancellationToken); }
        catch (MoveItApiException ex)
        {
            _logger.LogWarning(ex, "Running-task state could not be loaded.");
            return new List<MoveItRunningTask>();
        }
    }

    // Purpose: Retrieve task type.
    private static string GetTaskType(string taskType) => taskType switch
    {
        "COS" => "Advanced",
        "Sy" => "Synchronization",
        _ => "Traditional"
    };

    // Purpose: Normalize scheduled.
    private static string NormalizeScheduled(string value) => value switch
    {
        "ENABLED" => "Enabled",
        "DISABLED" => "Disabled",
        "INCOMPLETE" => "Incomplete",
        "UNSCHEDULED" => "Unscheduled",
        _ => string.IsNullOrWhiteSpace(value) ? "—" : value
    };

    // Purpose: Normalize run status.
    private static string NormalizeRunStatus(MoveItTaskRunRecord? run, bool enabled)
    {
        // Enabled controls scheduling, not the outcome of a manually started run.
        // Share the native result vocabulary used by MAUI's reports and Dashboard.
        var status = TaskRunStatus.Normalize(run?.Status ?? string.Empty);
        if (status is "Failed" or "No Transfers" or "Success") return status;
        return enabled ? "Ready" : "Disabled";
    }

    // Purpose: Check whether a MOVEit JSON flag is represented as 1.
    private static bool IsOne(JsonObject obj, string name)
    {
        var value = MoveItRestClient.GetString(obj, name);
        return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    // Purpose: Check whether a MOVEit JSON flag is represented as true.
    private static bool IsTrue(JsonObject obj, string name)
    {
        var value = MoveItRestClient.GetString(obj, name);
        return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }


    // Purpose: Join groups.
    private static string JoinGroups(JsonNode? node)
    {
        if (node is not JsonArray groups) return string.Empty;
        return string.Join(", ", groups.Select(item => item?.GetValue<string>() ?? string.Empty).Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    // Purpose: Return the first nonblank candidate value.
    private static string FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private sealed record TaskCacheEntry(IReadOnlyList<AutomationTask> Tasks, DateTimeOffset RefreshedUtc);
}

