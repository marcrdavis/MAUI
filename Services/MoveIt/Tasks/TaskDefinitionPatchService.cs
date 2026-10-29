// Author: Marc R. Davis (marcdavis@comcast.net)
// Single editor patch orchestration for Traditional, Advanced and Synchronization Tasks.
using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.MoveIt.Tasks.AdvancedTasks;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;
using static MoveItAutomationUI.Services.MoveIt.TraditionalTasks.TraditionalTaskDefinitionEditorOperations;

namespace MoveItAutomationUI.Services.MoveIt.Tasks;

public interface ITaskDefinitionPatchService
{
    Task ApplyEditorPatchAsync(string taskId, JsonObject request, CancellationToken cancellationToken = default);
}

// Shared native Task patch implementation used by all three MAUI Task editors,
// including verified new Step construction, Host-change behavior, schedules and parameters.
// Persists editor-owned Task fields by applying only verified host-capability write profiles
// to a freshly-read full MOVEit Task definition. Unknown/version-specific fields remain on the fresh
// server document and are therefore preserved by the verified type-specific full-task PUT contract.
public sealed class TaskDefinitionPatchService : ITaskDefinitionPatchService
{
    private readonly IMoveItRestClient _restClient;
    private readonly ITaskCrudCoordinator _taskCrud;
    private readonly ITraditionalTaskDefinitionComposer _composer;

    public TaskDefinitionPatchService(IMoveItRestClient restClient, ITaskCrudCoordinator taskCrud, ITraditionalTaskDefinitionComposer composer)
    {
        _restClient = restClient;
        _taskCrud = taskCrud;
        _composer = composer;
    }

    public async Task ApplyEditorPatchAsync(string taskId, JsonObject request, CancellationToken cancellationToken = default)
    {
        var id = (taskId ?? string.Empty).Trim();
        if (id.Length == 0) throw new ArgumentException("Task ID is required.", nameof(taskId));
        ArgumentNullException.ThrowIfNull(request);

        var task = await _taskCrud.GetDefinitionAsync(id, cancellationToken);
        var taskTypeCode = GetString(task, "TT").Trim();
        var kind = MoveItTaskKinds.Parse(taskTypeCode.Length == 0 ? "Tr" : taskTypeCode);
        if (!ObjectCheckout.CanEditCheckedOut("Task", id, task["xAlter"] is null || GetBool(task,"xAlter"))) throw new InvalidOperationException("You do not have permission to edit this Task.");
        var syncSettings = kind == MoveItTaskKind.Synchronization ? GetObject(request, "syncSettings") : null;

        var taskPatch = (GetObject(request, "task") ?? throw new InvalidOperationException("Task values are required.")).DeepClone().AsObject();
        var requestedName = GetString(taskPatch, "name").Trim();
        if (requestedName.Length == 0) throw new InvalidOperationException("Task Name is required.");
        if (!requestedName.Equals(GetString(task, "Name").Trim(), StringComparison.OrdinalIgnoreCase)
            && await _restClient.TaskNameExistsForOtherTaskAsync(requestedName, id, cancellationToken))
            throw new InvalidOperationException($"A MOVEit Task named \"{requestedName}\" already exists.");

        var requestedSteps = GetArray(request, "steps");
        var requestedSchedules = GetArray(request, "schedules");
        var requestedParameters = GetNode(request, "parameters");
        var requestedNextActions = GetArray(request, "nextActions");
        if (kind == MoveItTaskKind.Synchronization)
        {
            var hosts = await _restClient.GetHostsAsync(cancellationToken);
            foreach (var item in (requestedSteps ?? new JsonArray()).OfType<JsonObject>())
            {
                var folder = GetObject(item, "patch");
                if (folder is null) continue;
                var hostId = GetString(folder, "HostID").Trim();
                var type = GetString(folder, "Type").Trim();
                if (hostId == "0" && type == "FileSystem") continue;
                var matches = hosts.Any(wrapper => wrapper.Any(pair => pair.Value is JsonObject host
                    && pair.Key.Equals(type, StringComparison.OrdinalIgnoreCase)
                    && GetString(host, "ID").Equals(hostId, StringComparison.OrdinalIgnoreCase)));
                if (!matches) throw new InvalidOperationException("The selected Sync Host is unavailable or its type changed. Reopen the editor.");
            }
        }


        var useNativeScheduleXml = GetBool(request, "nativeScheduleXmlRequired")
            || RequiresNativeScheduleXmlPersistence(requestedSchedules)
            || TaskRestProjectionContainsNativeMulti(task);

        ValidateScheduleCompleteness(requestedSchedules);

        // The shared persistence boundary owns authoritative GET -> fresh edit ->
        // PUT -> readback. This shared patch service owns granular Host/Step field patching,
        // pair-aware Schedule validation, and semantic equivalence checking.
        var clearSyncListings = false;
        try
        {
            await _taskCrud.UpdateEditableFreshAsync(id, kind,
                current =>
                {
                    // Resolve against the checked-out definition, then verify that exact choice.
                    // taskPatch is detached from the request, including for older clients sending "".
                    taskPatch["cacheNames"] = ResolveCacheNames(GetString(taskPatch, "cacheNames"), GetString(current, "CacheNames"));
                    ApplyTaskFields(current, taskPatch);
                    if (kind == MoveItTaskKind.Synchronization)
                    {
                        SynchronizationTaskDefinitionComposer.ApplyFolders(current, requestedSteps);
                        if (clearSyncListings = SynchronizationTaskDefinitionComposer.ApplySettings(current, syncSettings))
                        {
                            if (!GetBool(request, "syncChangesConfirmed"))
                                throw new InvalidOperationException("Confirm the Sync changes before saving them with the Task disabled. Clear its Sync listing and review a preview before enabling it.");
                            current["Active"] = 0;
                            taskPatch["enabled"] = false;
                        }
                    }
                    else if (kind == MoveItTaskKind.Advanced) AdvancedTaskDefinition.Apply(current,
                        request["advancedFlow"] as JsonArray ?? throw new InvalidOperationException("Advanced flow is required."), requestedSteps, requestedNextActions, _composer);
                    else _composer.ApplyEditorSteps(current, requestedSteps);
                    if (useNativeScheduleXml)
                    {
                        ValidatePairAwareScheduleRequestShape(requestedSchedules);
                    }
                    else
                    {
                        ValidatePairAwareScheduleRequests(current, requestedSchedules);
                        ApplySchedules(current, requestedSchedules);
                    }
                    if (requestedParameters is not null)
                        current["Parameters"] = MergeParameters(GetNode(current, "Parameters"), requestedParameters, useNativeTaskPutShape: true);
                    if (kind != MoveItTaskKind.Advanced) ApplyNextActions(current, requestedNextActions);
                    return current;
                },
                persisted =>
                {
                    VerifyTaskFields(taskPatch, persisted);
                    if (kind == MoveItTaskKind.Synchronization) SynchronizationTaskDefinitionComposer.VerifyFolders(requestedSteps, GetNode(persisted, "steps"));
                    else if (kind != MoveItTaskKind.Advanced) _composer.VerifyEditorSteps(requestedSteps, GetNode(persisted, "steps"));
                    if (!useNativeScheduleXml) VerifySchedules(requestedSchedules, GetNode(persisted, "Schedules"));
                    if (requestedParameters is not null) VerifyParameters(requestedParameters, GetNode(persisted, "Parameters"));
                    if (kind != MoveItTaskKind.Advanced) VerifyNextActions(requestedNextActions, GetNode(persisted, "NextActions"));
                },
                requireReadback: true,
                cancellationToken: cancellationToken);
        }
        catch (TraditionalTaskPartialSaveException ex)
        {
            throw new TraditionalTaskPatchPartialSaveException(
                ex.Message, ex);
        }

        if (useNativeScheduleXml)
        {
            if (requestedSchedules is null) throw new InvalidOperationException("Task Schedules are required.");
            try
            {
                var savedConfiguration = await _taskCrud.UpdateTraditionalNativeSchedulesAsync(id, requestedSchedules, cancellationToken);
                var nativeSchedules = new JsonObject
                {
                    ["Schedule"] = new JsonArray(TraditionalTaskScheduleXml.ReadSchedules(savedConfiguration.Xml)
                        .Select(schedule => (JsonNode?)schedule.DeepClone()).ToArray())
                };
                VerifySchedules(requestedSchedules, nativeSchedules);
                if (ObjectEditVersion.Active)
                    ObjectEditVersion.CompleteNativeSchedules(id, await _taskCrud.GetDefinitionAsync(id, cancellationToken), savedConfiguration.Xml);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
            {
                throw new TraditionalTaskPatchPartialSaveException(
                    "The Task fields were saved and verified in MOVEit, but the native Schedule update did not complete. Close and reopen the Task Editor before retrying Schedule changes.",
                    ex);
            }
        }
        if (clearSyncListings)
        {
            try { await _taskCrud.ClearSynchronizationListingsAsync(id, cancellationToken); }
            catch (Exception ex)
            {
                throw new TraditionalTaskPatchPartialSaveException(
                    "The Sync settings were saved with the Task disabled, but clearing its listings could not be confirmed. Reopen the Task and inspect a Sync Preview before retrying or enabling it.", ex);
            }
        }
    }

}

