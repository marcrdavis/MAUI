// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Services.MoveIt.TraditionalTasks;

/// <summary>
/// An accepted REST update followed by a failed verification is *not* a confirmed
/// failed MOVEit write. Callers must reconcile the live Task before retrying.
/// </summary>
public sealed class TraditionalTaskPartialSaveException : InvalidOperationException
{
    public string TaskId { get; }

    public TraditionalTaskPartialSaveException(string taskId, string message, Exception innerException)
        : base(message, innerException) => TaskId = taskId;
}

/// <summary>
/// A successful REST POST whose identity cannot be uniquely established is an
/// indeterminate outcome, not evidence that MOVEit failed to create the Task.
/// Never automatically repeat the POST.
/// </summary>
public sealed class TraditionalTaskCreationUnverifiedException : InvalidOperationException
{
    public string TaskName { get; }
    public string? TaskId { get; }
    public TraditionalTaskCreationUnverifiedException(string taskName, string message, Exception? inner = null, string? taskId = null)
        : base(message, inner)
    {
        TaskName = taskName;
        TaskId = taskId;
    }
}

/// <summary>
/// Native Task persistence shared by Quick Transfer, CSV/XLSX Import and the
/// optional editor. Feature-specific licenses, Undo, audit, batching and
/// execution remain outside this shared Task object CRUD boundary.
/// </summary>
public interface ITraditionalTaskPersistenceService
{
    // Shared Traditional Task strategy: create a complete definition through REST.
    // MOVEit may omit the ID; caller must reconcile before any retry or follow-up operation.
    // Also used to create a disabled/unscheduled definition for the interactive MAUI editor.
    Task<JsonObject> CreateDisabledDefinitionAsync(string taskName, string? resourceGroup = null, CancellationToken cancellationToken = default);
    Task<JsonObject> CreateDefinitionAsync(JsonObject definition, CancellationToken cancellationToken = default);
    Task<TaskCreationResult> CreateResolvedDefinitionAsync(JsonObject definition, CancellationToken cancellationToken = default,
        Action<string, JsonObject>? verifyPersisted = null, bool requireReadback = false);
    Task<TaskCreationResult> CreateResolvedDisabledDefinitionAsync(string taskName, string? resourceGroup = null, CancellationToken cancellationToken = default);
    Task<JsonObject> GetDefinitionAsync(string taskId, CancellationToken cancellationToken = default);

    // Fetch the authoritative full Task, apply a detached patch, PUT and optionally
    // read it back. When requested, verification occurs *after* MOVEit accepted PUT.
    Task UpdateFreshAsync(
        string taskId,
        Func<JsonObject, JsonObject> applyToFreshDefinition,
        Action<JsonObject>? verifyPersisted = null,
        bool requireReadback = false,
        CancellationToken cancellationToken = default);

    // Opt-in native schedule persistence for the RC2 editor's Multi=1/Multi=2 case.
    // Never invoked automatically by existing RC1 clients.
    Task<MoveItTaskConfiguration> UpdateNativeSchedulesAsync(
        string taskId, JsonArray editorSchedulePatch, CancellationToken cancellationToken = default);
}

public sealed class TraditionalTaskPersistenceService : ITraditionalTaskPersistenceService
{
    private readonly IMoveItRestClient _rest;
    private readonly IMoveItComClient _com;
    private readonly ITraditionalTaskDefinitionComposer _composer;
    private readonly Tasks.ITaskDefinitionSaveService _definitions;

    public TraditionalTaskPersistenceService(IMoveItRestClient rest, IMoveItComClient com, ITraditionalTaskDefinitionComposer composer,
        Tasks.ITaskDefinitionSaveService? definitions = null)
    {
        _rest = rest;
        _com = com;
        _composer = composer;
        _definitions = definitions ?? new Tasks.TaskJsonPersistenceService(rest, com);
    }

    public Task<JsonObject> CreateDisabledDefinitionAsync(string taskName, string? resourceGroup = null, CancellationToken cancellationToken = default)
    {
        var definition = _composer.BuildDisabledTaskDefinition(taskName, resourceGroup);
        return CreateDefinitionAsync(definition, cancellationToken);
    }

    public Task<TaskCreationResult> CreateResolvedDisabledDefinitionAsync(string taskName, string? resourceGroup = null, CancellationToken cancellationToken = default)
        => CreateResolvedDefinitionAsync(_composer.BuildDisabledTaskDefinition(taskName, resourceGroup), cancellationToken);

    public async Task<TaskCreationResult> CreateResolvedDefinitionAsync(JsonObject definition, CancellationToken cancellationToken = default,
        Action<string, JsonObject>? verifyPersisted = null, bool requireReadback = false)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var name = GetText(definition, "Name");
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A Traditional Task Name is required for REST creation.", nameof(definition));

        // There must only ever be one POST. GET/list failures after a successful POST
        // cannot safely be interpreted as creation failures.
        var created = await CreateDefinitionAsync(definition, cancellationToken);
        var id = GetCreatedId(created);
        if (id.Length == 0)
        {
            try
            {
                var matches = (await _rest.GetTasksAsync(cancellationToken))
                    .Where(task => GetText(task, "Name").Equals(name, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (matches.Length == 1) id = GetCreatedId(matches[0]);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw new TraditionalTaskCreationUnverifiedException(name,
                    $"MOVEit accepted creation of '{name}', but MAUI could not reconcile its Task ID. Inspect MOVEit before retrying.", ex);
            }
            if (id.Length == 0)
                throw new TraditionalTaskCreationUnverifiedException(name,
                    $"MOVEit accepted creation of '{name}', but the Task ID could not be uniquely resolved. Inspect MOVEit before retrying.");
        }
        if (requireReadback || verifyPersisted is not null)
        {
            try
            {
                var persisted = await GetDefinitionAsync(id, cancellationToken);
                verifyPersisted?.Invoke(id, persisted);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw new TraditionalTaskCreationUnverifiedException(name,
                    $"MOVEit accepted creation of '{name}' (ID {id}), but MAUI could not verify the saved Task. Inspect MOVEit before retrying.", ex, id);
            }
        }
        var responseName = GetText(created, "Name");
        return new TaskCreationResult(id, responseName.Length > 0 ? responseName : name, "Traditional");
    }

    private static string GetCreatedId(JsonObject value)
    {
        foreach (var key in new[] { "ID", "Id", "TaskID", "taskId" })
        {
            var matching = value.FirstOrDefault(pair => pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            var text = matching.Value?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        return string.Empty;
    }

    public Task<JsonObject> CreateDefinitionAsync(JsonObject definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        // A detached definition avoids accidental mutation of the cached analyzed row.
        return _rest.CreateTaskDefinitionAsync(definition.DeepClone().AsObject(), cancellationToken);
    }

    public Task<JsonObject> GetDefinitionAsync(string taskId, CancellationToken cancellationToken = default)
        => _definitions.GetDefinitionAsync(taskId, Tasks.MoveItTaskKind.Traditional, cancellationToken);

    public Task UpdateFreshAsync(string taskId, Func<JsonObject, JsonObject> applyToFreshDefinition,
        Action<JsonObject>? verifyPersisted = null, bool requireReadback = false, CancellationToken cancellationToken = default)
        => _definitions.UpdateFreshAsync(taskId, Tasks.MoveItTaskKind.Traditional,
            applyToFreshDefinition, verifyPersisted, requireReadback, cancellationToken);

    public Task<MoveItTaskConfiguration> UpdateNativeSchedulesAsync(string taskId, JsonArray editorSchedulePatch, CancellationToken cancellationToken = default)
        => Tasks.TaskNativeSchedules.UpdateAsync(_com, taskId, editorSchedulePatch, cancellationToken);

    private static string GetText(JsonObject obj, string key)
    {
        var found = obj.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        return found.Value?.ToString()?.Trim() ?? string.Empty;
    }
}

