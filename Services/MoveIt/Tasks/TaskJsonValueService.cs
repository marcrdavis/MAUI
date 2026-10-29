// Task JSON workflow over the authoritative Task CRUD coordinator; never writes a browser-supplied full definition.
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;

namespace MoveItAutomationUI.Services.MoveIt.Tasks;

public interface ITaskJsonValueService
{
    Task<TaskJsonEditorDocument> ReadAsync(string id, CancellationToken ct);
    Task ValidateAsync(string id, TaskJsonEditRequest request, CancellationToken ct);
    Task<string> SaveAsync(string id, TaskJsonEditRequest request, CancellationToken ct);
}

public sealed class TaskJsonValueService(ITaskCrudCoordinator crud, IMoveItRestClient rest, IMoveItObjectAccessService access,
    IOptionsSnapshot<UiOptions> ui) : ITaskJsonValueService
{
    private sealed record Prepared(string Xml, MoveItTaskKind Kind, bool NativeSchedules);
    public async Task<TaskJsonEditorDocument> ReadAsync(string id, CancellationToken ct)
    {
        await access.RequireAsync("Task", id, "Edit", ct);
        var task = await crud.GetDefinitionAsync(id, ct); var native = await crud.GetNativeConfigurationAsync(id, ct);
        if (MoveItRestClient.GetString(task, "ID") != id || native.Id != id)
            throw new InvalidOperationException("MOVEit returned a different Task. Close and reopen the JSON Editor.");
        var kind = TaskJsonValues.Kind(task); var projected = TaskJsonValues.Project(task, native.Xml);
        var paths = TaskJsonValues.ReadOnlyPaths(projected);
        return new(id, MoveItRestClient.GetString(task, "Name"), kind.ToString(),
            ObjectEditVersion.Create("Task", id, ObjectEditVersion.Definition(task, native.Xml)), projected, paths,
            ui.Value.CheckoutObjectsForDurationOfEdit.Value);
    }
    private async Task<Prepared> PrepareAsync(string id, TaskJsonEditRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Changes is null) throw new ArgumentException("Changed Task values are required.");
        await access.RequireAsync("Task", id, "Edit", ct);
        var task = await crud.GetDefinitionAsync(id, ct); var native = await crud.GetNativeConfigurationAsync(id, ct);
        if (MoveItRestClient.GetString(task, "ID") != id || native.Id != id)
            throw new InvalidOperationException("MOVEit returned a different Task. Close and reopen the JSON Editor.");
        if (string.IsNullOrWhiteSpace(request.EditVersion)
            || ObjectEditVersion.Create("Task", id, ObjectEditVersion.Definition(task, native.Xml)) != request.EditVersion)
            throw new ObjectEditConflictException("Task");
        var original = TaskJsonValues.Project(task, native.Xml);
        var proposed = TaskJsonValues.Apply(original, request.Changes);
        var name = MoveItRestClient.GetString(proposed, "Name");
        if (!string.Equals(name, MoveItRestClient.GetString(task, "Name"), StringComparison.OrdinalIgnoreCase)
            && await rest.TaskNameExistsForOtherTaskAsync(name, id, ct))
            throw new InvalidOperationException("Another Task already has this name.");
        var nativeSchedules = TraditionalTaskDefinitionEditorOperations.TaskRestProjectionContainsNativeMulti(original);
        // Validate the exact XML transformation before the first REST PUT, not halfway through a mixed save.
        if (nativeSchedules) _ = TaskJsonValues.NativeSchedules(native.Xml, request.Changes);
        return new(native.Xml, TaskJsonValues.Kind(task), nativeSchedules);
    }
    public async Task ValidateAsync(string id, TaskJsonEditRequest request, CancellationToken ct) => _ = await PrepareAsync(id, request, ct);
    public async Task<string> SaveAsync(string id, TaskJsonEditRequest request, CancellationToken ct)
    {
        var prepared = await PrepareAsync(id, request, ct);
        using var revision = ObjectEditVersion.Require("Task", id, request.EditVersion);
        var restChanges = request.Changes.Where(c => !prepared.NativeSchedules || c.Path[0] != "Schedules").ToArray();
        await crud.UpdateEditableFreshAsync(id, prepared.Kind,
            current =>
            {
                if (current.ContainsKey("CacheNames")) current["CacheNames"] = TraditionalTaskDefinitionEditorOperations.ResolveCacheNames(current["CacheNames"]?.ToString() ?? "");
                return restChanges.Length == 0 ? current : TaskJsonValues.Apply(current, restChanges);
            },
            saved => TaskJsonValues.Verify(saved, restChanges), requireReadback: true, cancellationToken: ct);
        if (prepared.NativeSchedules)
        {
            try
            {
                var native = await crud.UpdateNativeScheduleDefinitionAsync(id, TaskJsonValues.NativeSchedules(prepared.Xml, request.Changes), ct);
                var saved = await crud.GetDefinitionAsync(id, ct);
                TaskJsonValues.Verify(TaskJsonValues.Project(saved, native.Xml), request.Changes);
                ObjectEditVersion.CompleteNativeSchedules(id, saved, native.Xml);
            }
            catch (Exception ex)
            { throw new TraditionalTaskPartialSaveException(id, "Task fields were saved, but the overnight Schedule update could not be confirmed. Reload this Task before retrying.", ex); }
        }
        return ObjectEditVersion.SavedVersion ?? throw new TraditionalTaskPartialSaveException(id,
            "The saved Task revision could not be confirmed. Reload this Task before retrying.", new InvalidOperationException("Missing Task revision."));
    }
}
