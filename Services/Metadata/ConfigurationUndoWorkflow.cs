// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Services.Metadata;

/// <summary>
/// Centralizes the shared persistence and safety checks used by resource-specific configuration Undo services.
/// The owning resource service remains responsible for obtaining/restoring MOVEit configuration XML.
/// </summary>
public interface IConfigurationUndoWorkflow
{
    // Purpose: Captures a resource configuration snapshot in the current environment's per-user Undo history.
    Task<ConfigurationUndoSnapshot> CaptureAsync(string objectType, string username, Func<CancellationToken, Task<UndoConfigurationSource>> captureSource, CancellationToken cancellationToken = default);
    // Purpose: Returns object IDs that currently have Undo history for the specified user.
    Task<IReadOnlySet<string>> GetAvailableIdsAsync(string objectType, string username, IEnumerable<string> objectIds, CancellationToken cancellationToken = default);
    // Purpose: Builds resource Undo history using the current MAUI audit state.
    Task<UndoHistoryResult> GetHistoryAsync(string objectType, string objectLabel, string objectId, string username, CancellationToken cancellationToken = default);
    // Purpose: Returns a selected snapshot only when the corresponding Undo history entry is still safe to restore.
    Task<ConfigurationUndoSnapshot> GetRestorableSnapshotAsync(string objectType, string objectLabel, string objectId, string username, string snapshotId, CancellationToken cancellationToken = default);
    // Purpose: Stores a caller-supplied configuration snapshot, typically the pre-restore configuration used to support redo-like Undo history.
    Task<ConfigurationUndoSnapshot> SaveSnapshotAsync(string objectType, string username, UndoConfigurationSource snapshot, CancellationToken cancellationToken = default);
    // Purpose: Deletes Undo history for the specified resource IDs in the current environment.
    Task DeleteSnapshotsAsync(string objectType, IEnumerable<string> objectIds, CancellationToken cancellationToken = default);
    // Purpose: Returns the current environment key used to scope MAUI Undo metadata.
    string GetEnvironmentKey();
}

public sealed record UndoConfigurationSource(string Id, string Name, string Xml);

public sealed class ConfigurationUndoWorkflow : IConfigurationUndoWorkflow
{
    private readonly IMetadataRepository _metadataRepository;
    private readonly IConfigurationUndoRepository _undoRepository;
    private readonly IMoveItSessionService _sessionService;

    // Purpose: Initializes the shared Undo workflow with metadata, snapshot storage, and current-session access.
    public ConfigurationUndoWorkflow(
        IMetadataRepository metadataRepository,
        IConfigurationUndoRepository undoRepository,
        IMoveItSessionService sessionService)
    {
        _metadataRepository = metadataRepository;
        _undoRepository = undoRepository;
        _sessionService = sessionService;
    }

    // Purpose: Captures a resource configuration snapshot in the current environment's per-user Undo history.
    public async Task<ConfigurationUndoSnapshot> CaptureAsync(
        string objectType,
        string username,
        Func<CancellationToken, Task<UndoConfigurationSource>> captureSource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(captureSource);
        var snapshot = await captureSource(cancellationToken);
        return await SaveSnapshotAsync(objectType, username, snapshot, cancellationToken);
    }

    // Purpose: Returns object IDs that currently have Undo history for the specified user.
    public Task<IReadOnlySet<string>> GetAvailableIdsAsync(
        string objectType,
        string username,
        IEnumerable<string> objectIds,
        CancellationToken cancellationToken = default)
        => _undoRepository.GetSnapshotObjectIdsAsync(GetEnvironmentKey(), objectType, username, objectIds, cancellationToken);

    // Purpose: Builds resource Undo history using the current MAUI audit state.
    public async Task<UndoHistoryResult> GetHistoryAsync(
        string objectType,
        string objectLabel,
        string objectId,
        string username,
        CancellationToken cancellationToken = default)
    {
        var id = (objectId ?? string.Empty).Trim();
        var user = (username ?? string.Empty).Trim();
        if (id.Length == 0) throw new InvalidOperationException($"{objectLabel} ID is required.");

        var environmentKey = GetEnvironmentKey();
        var snapshots = await _undoRepository.GetSnapshotsAsync(environmentKey, objectType, id, user, cancellationToken);
        var audit = await _metadataRepository.GetObjectConfigurationAuditAsync(environmentKey, objectType, id, cancellationToken);
        return UndoHistoryEvaluator.Build(id, id, user, objectLabel, snapshots, audit);
    }

    // Purpose: Returns a selected snapshot only when the corresponding Undo history entry is still safe to restore.
    public async Task<ConfigurationUndoSnapshot> GetRestorableSnapshotAsync(
        string objectType,
        string objectLabel,
        string objectId,
        string username,
        string snapshotId,
        CancellationToken cancellationToken = default)
    {
        var history = await GetHistoryAsync(objectType, objectLabel, objectId, username, cancellationToken);
        var requestedSnapshotId = snapshotId ?? string.Empty;
        var option = history.Snapshots.FirstOrDefault(item => item.SnapshotId.Equals(requestedSnapshotId, StringComparison.Ordinal));
        if (option is null) throw new InvalidOperationException($"The selected {objectLabel} undo snapshot is no longer available.");
        if (!option.CanRestore) throw new InvalidOperationException(option.Message);

        return await _undoRepository.GetSnapshotAsync(GetEnvironmentKey(), objectType, objectId, username, requestedSnapshotId, cancellationToken)
            ?? throw new InvalidOperationException($"The selected {objectLabel} undo snapshot is no longer available.");
    }

    // Purpose: Stores a caller-supplied configuration snapshot in the current environment's Undo history.
    public Task<ConfigurationUndoSnapshot> SaveSnapshotAsync(
        string objectType,
        string username,
        UndoConfigurationSource snapshot,
        CancellationToken cancellationToken = default)
        => _undoRepository.SaveSnapshotAsync(GetEnvironmentKey(), objectType, snapshot.Id, snapshot.Name, username, snapshot.Xml, cancellationToken);

    // Purpose: Deletes Undo history for the specified resource IDs in the current environment.
    public Task DeleteSnapshotsAsync(string objectType, IEnumerable<string> objectIds, CancellationToken cancellationToken = default)
        => _undoRepository.DeleteSnapshotsAsync(GetEnvironmentKey(), objectType, objectIds, cancellationToken);

    // Purpose: Returns the current environment key used to scope MAUI Undo metadata.
    public string GetEnvironmentKey()
        => (_sessionService.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.")).GetEnvironmentKey();
}
