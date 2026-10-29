// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Services.Metadata;

public interface IScriptUndoService
{
    // Purpose: Captures the current Script configuration into the per-user undo history.
    Task<ScriptUndoSnapshot> CaptureAsync(string scriptId, string username, CancellationToken cancellationToken = default);
    // Purpose: Returns available script ids.
    Task<IReadOnlySet<string>> GetAvailableScriptIdsAsync(string username, IEnumerable<string> scriptIds, CancellationToken cancellationToken = default);
    // Purpose: Returns the available Script undo history for the current user.
    Task<UndoHistoryResult> GetHistoryAsync(string scriptId, string username, CancellationToken cancellationToken = default);
    // Purpose: Restores a selected Script configuration snapshot.
    Task<ScriptUndoRestoreResult> RestoreAsync(string scriptId, string username, string snapshotId, CancellationToken cancellationToken = default);
    // Purpose: Deletes snapshots after applying the required guardrails.
    Task DeleteSnapshotsAsync(IEnumerable<string> scriptIds, CancellationToken cancellationToken = default);
}

public sealed record ScriptUndoSnapshot(string SnapshotId, string ScriptId, string ScriptName, string CreatedBy, DateTimeOffset CreatedUtc, string ScriptXml);
public sealed record ScriptUndoRestoreResult(string ScriptId, string ScriptName, DateTimeOffset SnapshotCreatedUtc, string Warning);

public sealed class ScriptUndoService : IScriptUndoService
{
    private const string ObjectType = "Script";
    private readonly IAutomationScriptService _service;
    private readonly IMetadataRepository _metadata;
    private readonly IConfigurationUndoWorkflow _workflow;
    private readonly ILogger<ScriptUndoService> _logger;

    // Purpose: Initializes script undo service with its required dependencies.
    public ScriptUndoService(
        IAutomationScriptService service,
        IMetadataRepository metadata,
        IConfigurationUndoWorkflow workflow,
        ILogger<ScriptUndoService> logger)
    {
        _service = service;
        _metadata = metadata;
        _workflow = workflow;
        _logger = logger;
    }

    // Purpose: Captures the current Script configuration into the per-user undo history.
    public async Task<ScriptUndoSnapshot> CaptureAsync(string id, string user, CancellationToken ct = default)
    {
        var normalizedId = (id ?? string.Empty).Trim();
        if (normalizedId.Length == 0) throw new ArgumentException("Script ID is required.", nameof(id));

        var inventory = await _service.GetScriptSnapshotAsync(false, ct);
        var inventoryScript = inventory.Scripts.FirstOrDefault(script => script.Id.Equals(normalizedId, StringComparison.OrdinalIgnoreCase));
        if (inventoryScript?.IsBuiltIn == true)
            throw new InvalidOperationException("Built-in MOVEit Scripts do not support MAUI Undo snapshots.");

        var stored = await _workflow.CaptureAsync(ObjectType, user, async token =>
        {
            var snapshot = await _service.GetScriptConfigurationSnapshotAsync(normalizedId, token);
            return new UndoConfigurationSource(snapshot.Id, snapshot.Name, snapshot.Xml);
        }, ct);
        return new(stored.SnapshotId, stored.ObjectId, stored.ObjectName, stored.CreatedBy, stored.CreatedUtc, stored.ObjectXml);
    }

    // Purpose: Returns available script ids.
    public Task<IReadOnlySet<string>> GetAvailableScriptIdsAsync(string user, IEnumerable<string> ids, CancellationToken ct = default)
        => _workflow.GetAvailableIdsAsync(ObjectType, user, ids, ct);

    // Purpose: Returns the available Script undo history for the current user.
    public Task<UndoHistoryResult> GetHistoryAsync(string id, string user, CancellationToken ct = default)
        => _workflow.GetHistoryAsync(ObjectType, "Script", id, user, ct);

    // Purpose: Restores a selected Script configuration snapshot.
    public async Task<ScriptUndoRestoreResult> RestoreAsync(string id, string user, string snapshotId, CancellationToken ct = default)
    {
        var selected = await _workflow.GetRestorableSnapshotAsync(ObjectType, "Script", id, user, snapshotId, ct);
        var current = await _service.GetScriptConfigurationSnapshotAsync(id, ct);
        var restored = await _service.RestoreScriptConfigurationAsync(id, selected.ObjectXml, ct);
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
            _logger.LogError(ex, "MOVEit Script {ScriptId} was restored successfully, but MAUI could not fully update its local undo/audit metadata.", restored.Id);
            warning = "The Script was restored in MOVEit, but MAUI could not fully update its local undo/audit metadata.";
        }

        return new(restored.Id, restored.Name, selected.CreatedUtc, warning);
    }

    // Purpose: Deletes snapshots after applying the required guardrails.
    public Task DeleteSnapshotsAsync(IEnumerable<string> ids, CancellationToken ct = default)
        => _workflow.DeleteSnapshotsAsync(ObjectType, ids, ct);
}
