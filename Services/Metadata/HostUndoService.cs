// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Services.Metadata;

public interface IHostUndoService
{
    // Purpose: Captures the current Host configuration into the per-user undo history.
    Task<HostUndoSnapshot> CaptureAsync(string hostId, string username, CancellationToken cancellationToken = default);
    // Purpose: Returns available host ids.
    Task<IReadOnlySet<string>> GetAvailableHostIdsAsync(string username, IEnumerable<string> hostIds, CancellationToken cancellationToken = default);
    // Purpose: Returns the available Host undo history for the current user.
    Task<UndoHistoryResult> GetHistoryAsync(string hostId, string username, CancellationToken cancellationToken = default);
    // Purpose: Restores a selected Host configuration snapshot.
    Task<HostUndoRestoreResult> RestoreAsync(string hostId, string username, string snapshotId, CancellationToken cancellationToken = default);
    // Purpose: Deletes snapshots after applying the required guardrails.
    Task DeleteSnapshotsAsync(IEnumerable<string> hostIds, CancellationToken cancellationToken = default);
}

public sealed record HostUndoSnapshot(string SnapshotId, string HostId, string HostName, string CreatedBy, DateTimeOffset CreatedUtc, string HostXml);
public sealed record HostUndoRestoreResult(string HostId, string HostName, DateTimeOffset SnapshotCreatedUtc, string Warning);

public sealed class HostUndoService : IHostUndoService
{
    private const string ObjectType = "Host";
    private readonly IAutomationHostService _service;
    private readonly IMetadataRepository _metadata;
    private readonly IConfigurationUndoWorkflow _workflow;
    private readonly ILogger<HostUndoService> _logger;

    // Purpose: Initializes host undo service with its required dependencies.
    public HostUndoService(
        IAutomationHostService service,
        IMetadataRepository metadata,
        IConfigurationUndoWorkflow workflow,
        ILogger<HostUndoService> logger)
    {
        _service = service;
        _metadata = metadata;
        _workflow = workflow;
        _logger = logger;
    }

    // Purpose: Captures the current Host configuration into the per-user undo history.
    public async Task<HostUndoSnapshot> CaptureAsync(string id, string user, CancellationToken ct = default)
    {
        var stored = await _workflow.CaptureAsync(ObjectType, user, async token =>
        {
            var snapshot = await _service.GetHostConfigurationSnapshotAsync(id, token);
            return new UndoConfigurationSource(snapshot.Id, snapshot.Name, snapshot.Xml);
        }, ct);
        return new(stored.SnapshotId, stored.ObjectId, stored.ObjectName, stored.CreatedBy, stored.CreatedUtc, stored.ObjectXml);
    }

    // Purpose: Returns available host ids.
    public Task<IReadOnlySet<string>> GetAvailableHostIdsAsync(string user, IEnumerable<string> ids, CancellationToken ct = default)
        => _workflow.GetAvailableIdsAsync(ObjectType, user, ids, ct);

    // Purpose: Returns the available Host undo history for the current user.
    public Task<UndoHistoryResult> GetHistoryAsync(string id, string user, CancellationToken ct = default)
        => _workflow.GetHistoryAsync(ObjectType, "Host", id, user, ct);

    // Purpose: Restores a selected Host configuration snapshot.
    public async Task<HostUndoRestoreResult> RestoreAsync(string id, string user, string snapshotId, CancellationToken ct = default)
    {
        var selected = await _workflow.GetRestorableSnapshotAsync(ObjectType, "Host", id, user, snapshotId, ct);
        var current = await _service.GetHostConfigurationSnapshotAsync(id, ct);
        var restored = await _service.RestoreHostConfigurationAsync(id, selected.ObjectXml, ct);
        var warning = string.Empty;
        try
        {
            await _metadata.RecordObjectConfigurationModifiedAsync(_workflow.GetEnvironmentKey(), ObjectType, restored.Id, user, ct);
            await _workflow.SaveSnapshotAsync(ObjectType, user, new UndoConfigurationSource(current.Id, current.Name, current.Xml), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MOVEit Host {HostId} was restored successfully, but MAUI could not fully update its local undo/audit metadata.", restored.Id);
            warning = "The Host was restored in MOVEit, but MAUI could not fully update its local undo/audit metadata.";
        }

        return new(restored.Id, restored.Name, selected.CreatedUtc, warning);
    }

    // Purpose: Deletes snapshots after applying the required guardrails.
    public Task DeleteSnapshotsAsync(IEnumerable<string> ids, CancellationToken ct = default)
        => _workflow.DeleteSnapshotsAsync(ObjectType, ids, ct);
}
