// Author: Marc R. Davis (marcdavis@comcast.net)
// Captured Sync actions belong to the shared Task boundary, alongside its definition CRUD.
using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;
using static MoveItAutomationUI.Services.MoveIt.TraditionalTasks.TraditionalTaskDefinitionEditorOperations;

namespace MoveItAutomationUI.Services.MoveIt.Tasks;

public sealed class SynchronizationActionPartialSaveException : InvalidOperationException
{
    public SynchronizationActionPartialSaveException(string message, Exception inner) : base(message, inner) { }
}

public sealed partial class TaskCrudCoordinator
{
    public async Task<SynchronizationPreview> GetSynchronizationPreviewAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var id = RequireId(taskId);
        SynchronizationTaskDefinitionComposer.RequireFolders(await ReadSynchronizationAsync(id, cancellationToken));
        return SynchronizationPreview.Parse(await _rest.GetSynchronizationCompareV0Async(id, cancellationToken));
    }

    public async Task ClearSynchronizationListingsAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var id = RequireId(taskId);
        var current = await ReadSynchronizationAsync(id, cancellationToken);
        RequireSynchronizationAlter(current);
        // Disable and verify before resetting runtime history; this also protects an indeterminate DELETE.
        if (GetBool(current, "Active"))
            await UpdateEditableFreshAsync(id, MoveItTaskKind.Synchronization,
                task => { RequireSynchronizationAlter(task); task["Active"] = 0; return task; },
                task => { if (GetBool(task, "Active")) throw new InvalidOperationException("The Sync Task is still enabled."); },
                requireReadback: true, cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await ObjectCheckout.ExecuteAsync("Task", id, ct => _rest.CheckoutTaskV0Async(id, ct), async ct =>
            {
                var locked = await ReadSynchronizationAsync(id, ct);
                RequireSynchronizationAlter(locked);
                if (GetBool(locked, "Active")) throw new InvalidOperationException("The Sync Task was enabled concurrently. Reopen it before clearing listings.");
                if (ObjectEditVersion.Pending)
                    ObjectEditVersion.Verify("Task", id, ObjectEditVersion.Definition(locked, (await _com.GetTaskConfigurationAsync(id, ct)).Xml));
                var result = await _rest.ClearSynchronizationCompareV0Async(id, ct);
                if (GetString(result, "status").Equals("error", StringComparison.OrdinalIgnoreCase))
                    throw new SynchronizationActionPartialSaveException("MOVEit rejected clearing the Sync listings. The Task remains disabled. Inspect the Task before retrying.", new InvalidOperationException("Native Sync listing reset was rejected."));
                if (GetString(result, "id") != "-1" || GetString(result, "status") != "success" || GetString(result, "errorcode") != "0")
                    throw new InvalidOperationException("MOVEit did not return the captured Sync listing-clear acknowledgement.");
                var saved = await ReadSynchronizationAsync(id, ct);
                if (GetBool(saved, "Active")) throw new InvalidOperationException("MOVEit did not leave the Sync Task disabled.");
                return true;
            }, ct => _rest.ReleaseTaskCheckoutV0Async(id, ct), cancellationToken);
        }
        catch (ObjectEditConflictException) { throw; }
        catch (SynchronizationActionPartialSaveException) { throw; }
        catch (MoveItAuthenticationException) { throw; }
        catch (MoveItApiException ex) when (ex.StatusCode is not null && (int)ex.StatusCode >= 400 && (int)ex.StatusCode < 500 && (int)ex.StatusCode != 408)
        {
            throw new SynchronizationActionPartialSaveException("MOVEit rejected clearing the Sync listings. The Task remains disabled. Inspect the Task before retrying.", ex);
        }
        catch (Exception ex)
        {
            // Never retry a dispatched reset. Configuration snapshots cannot restore Sync listings.
            throw new SynchronizationActionPartialSaveException(
                "The Sync listing-clear outcome could not be confirmed. Reopen the Task and inspect a Sync Preview before retrying or enabling it.", ex);
        }
    }

    public Task SwapSynchronizationFoldersAsync(string taskId, CancellationToken cancellationToken = default)
    {
        JsonObject? expected = null;
        return UpdateEditableFreshAsync(taskId, MoveItTaskKind.Synchronization,
            task =>
            {
                RequireSynchronizationAlter(task);
                SynchronizationTaskDefinitionComposer.SwapFolders(task);
                expected = task.DeepClone().AsObject();
                return task;
            },
            saved => SynchronizationTaskDefinitionComposer.VerifySwappedFolders(expected!, saved),
            requireReadback: true, cancellationToken: cancellationToken);
    }

    // Native Preview OK submits the current Task through the same captured v0 PUT lifecycle.
    public Task SaveSynchronizationPreviewAsync(string taskId, CancellationToken cancellationToken = default)
        => UpdateEditableFreshAsync(taskId, MoveItTaskKind.Synchronization,
            task => { RequireSynchronizationAlter(task); SynchronizationTaskDefinitionComposer.RequireFolders(task); return task; },
            requireReadback: true, cancellationToken: cancellationToken);

    private Task<JsonObject> ReadSynchronizationAsync(string taskId, CancellationToken cancellationToken)
        => _definitions.GetDefinitionAsync(taskId, MoveItTaskKind.Synchronization, cancellationToken);

    private static void RequireSynchronizationAlter(JsonObject task)
    {
        if (!ObjectCheckout.CanEditCheckedOut("Task", GetString(task, "ID"), GetNode(task, "xAlter") is null || GetBool(task, "xAlter")))
            throw new InvalidOperationException("The current MOVEit user cannot alter this Task.");
    }
}
