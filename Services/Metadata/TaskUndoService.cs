// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Services.Metadata;

public interface ITaskUndoService
{
    // Purpose: Captures the current Task configuration into the per-user undo history.
    Task<TaskUndoSnapshot> CaptureAsync(string taskId, string username, CancellationToken cancellationToken = default);
    // Purpose: Returns available task ids.
    Task<IReadOnlySet<string>> GetAvailableTaskIdsAsync(string username, IEnumerable<string> taskIds, CancellationToken cancellationToken = default);
    // Purpose: Returns the available Task undo history for the current user.
    Task<UndoHistoryResult> GetHistoryAsync(string taskId, string username, CancellationToken cancellationToken = default);
    // Purpose: Restores a selected Task configuration snapshot.
    Task<TaskUndoRestoreResult> RestoreAsync(string taskId, string username, string snapshotId, CancellationToken cancellationToken = default);
    // Purpose: Deletes snapshots after applying the required guardrails.
    Task DeleteSnapshotsAsync(IEnumerable<string> taskIds, CancellationToken cancellationToken = default);
}

public sealed record TaskUndoSnapshot(string SnapshotId, string TaskId, string TaskName, string CreatedBy, DateTimeOffset CreatedUtc, string TaskXml);
public sealed record TaskUndoRestoreResult(string TaskId, string TaskName, DateTimeOffset SnapshotCreatedUtc, string Warning);

public sealed class TaskUndoService : ITaskUndoService
{
    private const string ObjectType = "Task";
    private readonly IAutomationTaskService _taskService;
    private readonly IMetadataRepository _metadataRepository;
    private readonly IConfigurationUndoWorkflow _workflow;
    private readonly ILogger<TaskUndoService> _logger;

    // Purpose: Initializes task undo service with its required dependencies.
    public TaskUndoService(
        IAutomationTaskService taskService,
        IMetadataRepository metadataRepository,
        IConfigurationUndoWorkflow workflow,
        ILogger<TaskUndoService> logger)
    {
        _taskService = taskService;
        _metadataRepository = metadataRepository;
        _workflow = workflow;
        _logger = logger;
    }

    // Purpose: Captures the current Task configuration into the per-user undo history.
    public async Task<TaskUndoSnapshot> CaptureAsync(string taskId, string username, CancellationToken cancellationToken = default)
    {
        var stored = await _workflow.CaptureAsync(ObjectType, username, async token =>
        {
            var snapshot = await _taskService.GetTaskConfigurationSnapshotAsync(taskId, token);
            return new UndoConfigurationSource(snapshot.Id, snapshot.Name, snapshot.Xml);
        }, cancellationToken);
        return new(stored.SnapshotId, stored.ObjectId, stored.ObjectName, stored.CreatedBy, stored.CreatedUtc, stored.ObjectXml);
    }

    // Purpose: Returns available task ids.
    public Task<IReadOnlySet<string>> GetAvailableTaskIdsAsync(string username, IEnumerable<string> taskIds, CancellationToken cancellationToken = default)
        => _workflow.GetAvailableIdsAsync(ObjectType, username, taskIds, cancellationToken);

    // Purpose: Returns the available Task undo history for the current user.
    public Task<UndoHistoryResult> GetHistoryAsync(string taskId, string username, CancellationToken cancellationToken = default)
        => _workflow.GetHistoryAsync(ObjectType, "task", taskId, username, cancellationToken);

    // Purpose: Restores a selected Task configuration snapshot.
    public async Task<TaskUndoRestoreResult> RestoreAsync(string taskId, string username, string snapshotId, CancellationToken cancellationToken = default)
    {
        var normalizedSnapshotId = (snapshotId ?? string.Empty).Trim();
        var selected = await _workflow.GetRestorableSnapshotAsync(ObjectType, "task", taskId, username, normalizedSnapshotId, cancellationToken);
        var current = await _taskService.GetTaskConfigurationSnapshotAsync(taskId, cancellationToken);
        var restored = await _taskService.RestoreTaskConfigurationAsync(selected.ObjectId, selected.ObjectXml, cancellationToken);
        var warning = string.Empty;
        try
        {
            await _metadataRepository.RecordObjectConfigurationModifiedAsync(_workflow.GetEnvironmentKey(), ObjectType, restored.Id, username, cancellationToken);
            await _workflow.SaveSnapshotAsync(ObjectType, username, new UndoConfigurationSource(current.Id, current.Name, current.Xml), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MOVEit task {TaskId} was restored successfully, but MAUI could not fully update its local undo/audit metadata.", restored.Id);
            warning = "The task was restored in MOVEit, but MAUI could not fully update its local undo/audit metadata.";
        }

        return new(restored.Id, restored.Name, selected.CreatedUtc, warning);
    }

    // Purpose: Deletes snapshots after applying the required guardrails.
    public Task DeleteSnapshotsAsync(IEnumerable<string> taskIds, CancellationToken cancellationToken = default)
        => _workflow.DeleteSnapshotsAsync(ObjectType, taskIds, cancellationToken);
}
