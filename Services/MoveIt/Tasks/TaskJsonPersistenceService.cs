// Author: Marc R. Davis (marcdavis@comcast.net)
// Shared authoritative Task GET / detached patch / type-selected PUT / readback lifecycle.
using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;
using MoveItAutomationUI.Services.MoveIt.Tasks.AdvancedTasks;

namespace MoveItAutomationUI.Services.MoveIt.Tasks;

public interface ITaskDefinitionSaveService
{
    Task<JsonObject> GetDefinitionAsync(string taskId, MoveItTaskKind kind, CancellationToken cancellationToken = default);
    Task UpdateFreshAsync(string taskId, MoveItTaskKind? kind, Func<JsonObject, JsonObject> patch,
        Action<JsonObject>? verify = null, bool requireReadback = false, CancellationToken cancellationToken = default);
}

public sealed class TaskJsonPersistenceService : ITaskDefinitionSaveService
{
    private readonly IMoveItRestClient _rest;
    private readonly IMoveItComClient? _com;
    private readonly IMoveItObjectAccessService? _access;
    public TaskJsonPersistenceService(IMoveItRestClient rest, IMoveItComClient? com = null, IMoveItObjectAccessService? access = null) { _rest = rest; _com = com; _access = access; }

    public async Task<JsonObject> GetDefinitionAsync(string taskId, MoveItTaskKind kind, CancellationToken cancellationToken = default)
    {
        var id = RequireId(taskId);
        var task = await _rest.GetTaskAsync(id, cancellationToken);
        ValidateIdentity(task, id, kind);
        return task;
    }

    public async Task UpdateFreshAsync(string taskId, MoveItTaskKind? kind, Func<JsonObject, JsonObject> patch,
        Action<JsonObject>? verify = null, bool requireReadback = false, CancellationToken cancellationToken = default)
    {
        var id = RequireId(taskId);
        ArgumentNullException.ThrowIfNull(patch);
        if (_access is not null) await _access.RequireAsync("Task", id, "Edit", cancellationToken);
        var writeAttempted = false;
        try
        {
            await ObjectCheckout.ExecuteAsync("Task", id,
                ct => _rest.CheckoutTaskV0Async(id, ct),
                async ct =>
                {
                    await UpdateCheckedOutAsync(id, kind, patch, verify, requireReadback, () => writeAttempted = true, ct);
                    return true;
                }, ct => _rest.ReleaseTaskCheckoutV0Async(id, ct), cancellationToken);
        }
        catch (ObjectCheckoutReleaseException ex) when (writeAttempted)
        {
            throw new TraditionalTaskPartialSaveException(id, ex.Message, ex);
        }
    }

    private async Task UpdateCheckedOutAsync(string id, MoveItTaskKind? requestedKind, Func<JsonObject, JsonObject> patch,
        Action<JsonObject>? verify, bool requireReadback, Action dispatched, CancellationToken cancellationToken)
    {
        // Common information/state callers discover the type from this checked-out
        // read; editors also require it to match their previously selected kind.
        var current = await _rest.GetTaskAsync(id, cancellationToken);
        var code = Text(current, "TT");
        var kind = requestedKind ?? (code.Length == 0 ? MoveItTaskKind.Traditional : MoveItTaskKinds.Parse(code));
        ValidateIdentity(current, id, kind);
        if (ObjectEditVersion.Pending)
        {
            if (ObjectEditVersion.IsRequired("TaskInformation"))
                ObjectEditVersion.Verify("TaskInformation", id, ObjectEditVersion.TaskInformation(current));
            else
                ObjectEditVersion.Verify("Task", id, ObjectEditVersion.Definition(current,
                    (await (_com ?? throw new InvalidOperationException("Native Task revision verification is unavailable."))
                        .GetTaskConfigurationAsync(id, cancellationToken)).Xml));
        }
        var proposed = patch(current.DeepClone().AsObject())
            ?? throw new InvalidOperationException("The Task update builder returned no Task definition.");
        ValidateIdentity(proposed, id, kind);
        cancellationToken.ThrowIfCancellationRequested();
        dispatched();
        try
        {
            if (kind is MoveItTaskKind.Synchronization or MoveItTaskKind.Advanced)
            {
                var result = await _rest.UpdateTaskV0Async(id, proposed, cancellationToken);
                if (!string.Equals(Text(result, "id"), id, StringComparison.OrdinalIgnoreCase)
                    || !Text(result, "status").Equals("success", StringComparison.OrdinalIgnoreCase)
                    || Text(result, "errorcode") != "0")
                    throw new InvalidOperationException("MOVEit did not return the captured v0 Task save acknowledgement.");
            }
            else await _rest.UpdateTaskAsync(id, proposed, cancellationToken);
        }
        catch (MoveItApiException ex) when (ex.StatusCode is not null && (int)ex.StatusCode >= 400 && (int)ex.StatusCode < 500 && (int)ex.StatusCode != 408) { throw; }
        catch (Exception ex)
        {
            // A timeout, disconnect, cancellation or malformed acknowledgement after dispatch
            // cannot prove the full Task PUT was rejected. Never automatically repeat it.
            throw new TraditionalTaskPartialSaveException(id,
                "The Task save outcome is uncertain. Reopen or inspect the Task before retrying.", ex);
        }
        if (!requireReadback && verify is null) return;
        var verificationStage = "Task readback";
        try
        {
            var persisted = await GetDefinitionAsync(id, kind, cancellationToken);
            verificationStage = "Sync state verification";
            if (kind == MoveItTaskKind.Synchronization) SynchronizationTaskDefinitionComposer.VerifyNativeState(proposed, persisted);
            verificationStage = "Advanced flow verification";
            if (kind == MoveItTaskKind.Advanced) AdvancedTaskDefinition.Verify(proposed, persisted, current);
            verificationStage = "submitted Task field verification";
            verify?.Invoke(persisted);
            verificationStage = "native Task revision readback";
            if (ObjectEditVersion.IsRequired("TaskInformation"))
                ObjectEditVersion.Persisted("TaskInformation", id, ObjectEditVersion.TaskInformation(persisted));
            else if (ObjectEditVersion.Active)
                ObjectEditVersion.Persisted("Task", id, ObjectEditVersion.Definition(persisted,
                    (await _com!.GetTaskConfigurationAsync(id, cancellationToken)).Xml));
        }
        catch (Exception ex)
        {
            throw new TraditionalTaskPartialSaveException(id,
                $"MOVEit accepted the Task update, but {verificationStage} failed. "
                + (ex is AdvancedTaskReadbackException ? ex.Message + " " : "")
                + "Reload or inspect the Task before saving again.", ex);
        }
    }

    private static void ValidateIdentity(JsonObject task, string id, MoveItTaskKind kind)
    {
        var code = Text(task, "TT");
        var actual = code.Length == 0 ? MoveItTaskKind.Traditional : MoveItTaskKinds.Parse(code);
        if (actual != kind) throw new InvalidOperationException($"MOVEit Task '{id}' is not a {kind} Task.");
        var returnedId = Text(task, "ID");
        if (returnedId.Length == 0 || !returnedId.Equals(id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The Task definition contains a missing or different Task ID.");
    }
    private static string RequireId(string id) => string.IsNullOrWhiteSpace(id)
        ? throw new ArgumentException("Task ID is required.", nameof(id)) : id.Trim();
    private static string Text(JsonObject value, string key)
        => value.FirstOrDefault(pair => pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value?.ToString().Trim() ?? string.Empty;
}
