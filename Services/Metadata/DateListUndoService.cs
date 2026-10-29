// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Services.Metadata;

public interface IDateListUndoService
{
    // Purpose: Captures the current Date List configuration into the per-user undo history.
    Task<DateListUndoSnapshot> CaptureAsync(string id, string username, CancellationToken cancellationToken = default);
    // Purpose: Returns available ids.
    Task<IReadOnlySet<string>> GetAvailableIdsAsync(string username, IEnumerable<string> ids, CancellationToken cancellationToken = default);
    // Purpose: Returns the available Date List undo history for the current user.
    Task<UndoHistoryResult> GetHistoryAsync(string id, string username, CancellationToken cancellationToken = default);
    // Purpose: Restores a selected Date List configuration snapshot.
    Task<DateListUndoRestoreResult> RestoreAsync(string id, string username, string snapshotId, CancellationToken cancellationToken = default);
    // Purpose: Deletes snapshots after applying the required guardrails.
    Task DeleteSnapshotsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default);
}

public sealed record DateListUndoSnapshot(string SnapshotId, string Id, string Name, string CreatedBy, DateTimeOffset CreatedUtc, string Xml);
public sealed record DateListUndoRestoreResult(string Id, string Name, DateTimeOffset SnapshotCreatedUtc, string Warning);

public sealed class DateListUndoService : IDateListUndoService
{
    private const string ObjectType = "DateList";
    private readonly IAutomationDateListService _service;
    private readonly IMetadataRepository _metadata;
    private readonly IConfigurationUndoWorkflow _workflow;

    // Purpose: Initializes date list undo service with its required dependencies.
    public DateListUndoService(
        IAutomationDateListService service,
        IMetadataRepository metadata,
        IConfigurationUndoWorkflow workflow)
    {
        _service = service;
        _metadata = metadata;
        _workflow = workflow;
    }

    // Purpose: Captures the current Date List configuration into the per-user undo history.
    public async Task<DateListUndoSnapshot> CaptureAsync(string id, string user, CancellationToken ct = default)
    {
        var stored = await _workflow.CaptureAsync(ObjectType, user, async token =>
        {
            var snapshot = await _service.GetConfigurationSnapshotAsync(id, token);
            return new UndoConfigurationSource(snapshot.Id, snapshot.Name, snapshot.Xml);
        }, ct);
        return new(stored.SnapshotId, stored.ObjectId, stored.ObjectName, stored.CreatedBy, stored.CreatedUtc, stored.ObjectXml);
    }

    // Purpose: Returns available ids.
    public Task<IReadOnlySet<string>> GetAvailableIdsAsync(string user, IEnumerable<string> ids, CancellationToken ct = default)
        => _workflow.GetAvailableIdsAsync(ObjectType, user, ids, ct);

    // Purpose: Returns the available Date List undo history for the current user.
    public Task<UndoHistoryResult> GetHistoryAsync(string id, string user, CancellationToken ct = default)
        => _workflow.GetHistoryAsync(ObjectType, "Date List", id, user, ct);

    // Purpose: Restores a selected Date List configuration snapshot.
    public async Task<DateListUndoRestoreResult> RestoreAsync(string id, string user, string snapshotId, CancellationToken ct = default)
    {
        var selected = await _workflow.GetRestorableSnapshotAsync(ObjectType, "Date List", id, user, snapshotId, ct);
        var current = await _service.GetConfigurationSnapshotAsync(id, ct);
        var restored = await _service.RestoreConfigurationAsync(id, selected.ObjectXml, ct);
        await _metadata.RecordObjectConfigurationModifiedAsync(_workflow.GetEnvironmentKey(), ObjectType, id, user, ct);
        await _workflow.SaveSnapshotAsync(ObjectType, user, new UndoConfigurationSource(current.Id, current.Name, current.Xml), ct);
        return new(restored.Id, restored.Name, selected.CreatedUtc, string.Empty);
    }

    // Purpose: Deletes snapshots after applying the required guardrails.
    public Task DeleteSnapshotsAsync(IEnumerable<string> ids, CancellationToken ct = default)
        => _workflow.DeleteSnapshotsAsync(ObjectType, ids, ct);
}
