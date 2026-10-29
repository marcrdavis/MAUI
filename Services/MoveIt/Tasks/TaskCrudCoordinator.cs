// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Shared Task CRUD boundary: native format/transport selection belongs here, not in feature controllers.

using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Services.MoveIt.Tasks;

public enum MoveItTaskKind { Traditional, Advanced, Synchronization }

public static class MoveItTaskKinds
{
    public static MoveItTaskKind Parse(string? taskType)
    {
        var value = (taskType ?? string.Empty).Trim();
        if (value.Equals("Traditional", StringComparison.OrdinalIgnoreCase) || value.Equals("Tr", StringComparison.OrdinalIgnoreCase)) return MoveItTaskKind.Traditional;
        if (value.Equals("Advanced", StringComparison.OrdinalIgnoreCase) || value.Equals("COS", StringComparison.OrdinalIgnoreCase)) return MoveItTaskKind.Advanced;
        if (value.Equals("Synchronization", StringComparison.OrdinalIgnoreCase) || value.Equals("Sync", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Sy", StringComparison.OrdinalIgnoreCase)) return MoveItTaskKind.Synchronization;
        throw new InvalidOperationException("Task Type must be Traditional, Advanced, or Synchronization.");
    }
}

/// <summary>
/// The object-level Task CRUD interface. Feature services retain workflow-specific
/// license limits, bulk dependency ordering, metadata/audit, execution and Undo.
/// Traditional uses v1 JSON; Advanced and Sync use the captured v0 JSON write. Native XML
/// configuration retains COM; empty Advanced/Sync creation uses the captured v0 POST.
/// </summary>
public interface ITaskCrudCoordinator
{
    Task<TaskCreationResult> CreateEmptyAsync(string name, string taskType, string? resourceGroup = null, CancellationToken cancellationToken = default);
    Task<TaskCreationResult> CreateAdvancedFromTraditionalAsync(string taskId, CancellationToken cancellationToken = default);
    Task<TaskCreationResult> CreateTraditionalResolvedAsync(JsonObject definition, CancellationToken cancellationToken = default,
        Action<string, JsonObject>? verifyPersisted = null, bool requireReadback = false);
    Task<JsonObject> GetTraditionalDefinitionAsync(string taskId, CancellationToken cancellationToken = default);
    Task<JsonObject> GetDefinitionAsync(string taskId, CancellationToken cancellationToken = default);
    Task UpdateEditableFreshAsync(string taskId, MoveItTaskKind kind, Func<JsonObject, JsonObject> patch,
        Action<JsonObject>? verify = null, bool requireReadback = false, CancellationToken cancellationToken = default);
    Task UpdateTraditionalFreshAsync(string taskId, Func<JsonObject, JsonObject> patch,
        Action<JsonObject>? verify = null, bool requireReadback = false, CancellationToken cancellationToken = default);
    // Preserve the authoritative native overnight Schedule subtree after a value-only edit.
    Task<MoveItTaskConfiguration> UpdateNativeScheduleDefinitionAsync(string taskId, string schedulesXml, CancellationToken cancellationToken = default);
    Task<MoveItTaskConfiguration> UpdateTraditionalNativeSchedulesAsync(string taskId, JsonArray schedulePatch, CancellationToken cancellationToken = default);
    Task<MoveItTaskConfiguration> CreateFromNativeConfigurationAsync(string name, string xml, bool leaveDisabled, CancellationToken cancellationToken = default);
    Task UpdateBasicDetailsAsync(string taskId, string name, string description, string notes, CancellationToken cancellationToken = default);
    Task SetEnabledAsync(string taskId, bool enabled, CancellationToken cancellationToken = default);
    Task<MoveItTaskConfiguration> GetNativeConfigurationAsync(string taskId, CancellationToken cancellationToken = default);
    Task<MoveItTaskConfiguration> RestoreNativeConfigurationAsync(string taskId, string xml, CancellationToken cancellationToken = default);
    Task DeleteAsync(string taskId, CancellationToken cancellationToken = default);
    Task<SynchronizationPreview> GetSynchronizationPreviewAsync(string taskId, CancellationToken cancellationToken = default);
    Task ClearSynchronizationListingsAsync(string taskId, CancellationToken cancellationToken = default);
    Task SwapSynchronizationFoldersAsync(string taskId, CancellationToken cancellationToken = default);
    Task SaveSynchronizationPreviewAsync(string taskId, CancellationToken cancellationToken = default);
    // Traditional Task Source Timestamp is a native Task subresource, not an editor-owned CRUD transport.
    Task<JsonObject> GetSourceTimestampsAsync(string taskId, string hostId, CancellationToken cancellationToken = default);
    Task<JsonObject> SetSourceTimestampAsync(string taskId, string hostId, string path, string mask, string timestamp, CancellationToken cancellationToken = default);
}

/// <summary>
/// Explicitly captures the native representation a Task type uses for edits.
/// Advanced/Sync empty creation and definition updates use their captured v0 JSON contracts; native XML operations retain COM.
/// The common coordinator's Basic Details, Enable/Disable and Delete operations are
/// type-independent and remain shared instead of being duplicated per strategy.
/// </summary>
public enum MoveItTaskDefinitionFormat { RestJson, RestJsonV0 }

public interface ITaskCrudStrategy
{
    MoveItTaskKind Kind { get; }
    MoveItTaskDefinitionFormat DefinitionFormat { get; }
    Task<TaskCreationResult> CreateEmptyAsync(string name, string? resourceGroup, CancellationToken cancellationToken);
}

public sealed class TraditionalTaskCrudStrategy : ITaskCrudStrategy
{
    private readonly ITraditionalTaskPersistenceService _definitions;
    public MoveItTaskKind Kind => MoveItTaskKind.Traditional;
    public MoveItTaskDefinitionFormat DefinitionFormat => MoveItTaskDefinitionFormat.RestJson;
    public TraditionalTaskCrudStrategy(ITraditionalTaskPersistenceService definitions)
        => _definitions = definitions;

    public Task<TaskCreationResult> CreateEmptyAsync(string name, string? resourceGroup, CancellationToken cancellationToken)
        => _definitions.CreateResolvedDisabledDefinitionAsync(name, resourceGroup, cancellationToken);
}

// Retained COM creation strategy for explicit reuse; production registration uses RestV0TaskCrudStrategy.
public sealed class NativeTaskCrudStrategy : ITaskCrudStrategy
{
    private readonly IMoveItComClient _com;
    public MoveItTaskKind Kind { get; }
    public MoveItTaskDefinitionFormat DefinitionFormat => MoveItTaskDefinitionFormat.RestJsonV0;
    public NativeTaskCrudStrategy(MoveItTaskKind kind, IMoveItComClient com)
    {
        if (kind == MoveItTaskKind.Traditional) throw new ArgumentException("Native skeleton strategy is for Advanced/Sync only.", nameof(kind));
        Kind = kind;
        _com = com;
    }
    public async Task<TaskCreationResult> CreateEmptyAsync(string name, string? resourceGroup, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(resourceGroup))
            throw new InvalidOperationException("Native Advanced/Sync Task creation does not accept a Resource Group through this operation.");
        var type = Kind == MoveItTaskKind.Advanced ? "Advanced" : "Synchronization";
        var created = await _com.CreateTaskAsync(name, type, cancellationToken);
        return new TaskCreationResult(created.Id, created.Name, created.TaskType);
    }

}

public sealed partial class TaskCrudCoordinator : ITaskCrudCoordinator
{
    private readonly IReadOnlyDictionary<MoveItTaskKind, ITaskCrudStrategy> _strategies;
    private readonly ITaskDefinitionSaveService _definitions;
    private readonly ITraditionalTaskPersistenceService _traditional;
    private readonly IMoveItRestClient _rest;
    private readonly IMoveItObjectAccessService? _access;
    private readonly IMoveItComClient _com;

    public TaskCrudCoordinator(IEnumerable<ITaskCrudStrategy> strategies,
        ITraditionalTaskPersistenceService traditional, IMoveItRestClient rest, IMoveItComClient com,
        ITaskDefinitionSaveService? definitions = null, IMoveItObjectAccessService? access = null)
    {
        _strategies = strategies.ToDictionary(strategy => strategy.Kind);
        foreach (var kind in Enum.GetValues<MoveItTaskKind>())
            if (!_strategies.ContainsKey(kind)) throw new InvalidOperationException($"Task CRUD strategy missing for {kind}.");
        _access = access;
        _definitions = definitions ?? new TaskJsonPersistenceService(rest, com, access);
        if (_strategies[MoveItTaskKind.Traditional].DefinitionFormat != MoveItTaskDefinitionFormat.RestJson
            || _strategies[MoveItTaskKind.Advanced].DefinitionFormat != MoveItTaskDefinitionFormat.RestJsonV0
            || _strategies[MoveItTaskKind.Synchronization].DefinitionFormat != MoveItTaskDefinitionFormat.RestJsonV0)
            throw new InvalidOperationException("Task strategy definition formats do not match their verified MOVEit transports.");
        _traditional = traditional;
        _rest = rest;
        _com = com;
    }

    public async Task<TaskCreationResult> CreateEmptyAsync(string name, string taskType, string? resourceGroup = null, CancellationToken cancellationToken = default)
    {
        if (_access is not null) resourceGroup = await _access.RequireCreationAsync("Task", resourceGroup, cancellationToken);
        return await _strategies[MoveItTaskKinds.Parse(taskType)].CreateEmptyAsync(name, resourceGroup, cancellationToken);
    }

    public async Task<TaskCreationResult> CreateTraditionalResolvedAsync(JsonObject definition, CancellationToken cancellationToken = default,
        Action<string, JsonObject>? verifyPersisted = null, bool requireReadback = false)
    {
        if (_access is not null) await _access.RequireDefinitionCreationAsync("Task", definition, cancellationToken);
        return await _traditional.CreateResolvedDefinitionAsync(definition, cancellationToken, verifyPersisted, requireReadback);
    }

    public Task<JsonObject> GetTraditionalDefinitionAsync(string taskId, CancellationToken cancellationToken = default)
        => _definitions.GetDefinitionAsync(RequireId(taskId), MoveItTaskKind.Traditional, cancellationToken);

    // Generic read works for Traditional, Advanced, and Synchronization Tasks.
    public Task<JsonObject> GetDefinitionAsync(string taskId, CancellationToken cancellationToken = default)
        => _rest.GetTaskAsync(RequireId(taskId), cancellationToken);

    public Task UpdateTraditionalFreshAsync(string taskId, Func<JsonObject, JsonObject> patch,
        Action<JsonObject>? verify = null, bool requireReadback = false, CancellationToken cancellationToken = default)
        => UpdateEditableFreshAsync(taskId, MoveItTaskKind.Traditional, patch, verify, requireReadback, cancellationToken);

    public Task UpdateEditableFreshAsync(string taskId, MoveItTaskKind kind, Func<JsonObject, JsonObject> patch,
        Action<JsonObject>? verify = null, bool requireReadback = false, CancellationToken cancellationToken = default)
        => _definitions.UpdateFreshAsync(RequireId(taskId), kind, patch, verify, requireReadback, cancellationToken);

    public async Task<MoveItTaskConfiguration> UpdateNativeScheduleDefinitionAsync(string taskId, string schedulesXml, CancellationToken cancellationToken = default)
    {
        var id = RequireId(taskId);
        if (_access is not null) await _access.RequireAsync("Task", id, "Edit", cancellationToken);
        return await TaskNativeSchedules.UpdateDefinitionAsync(_com, id, schedulesXml, cancellationToken);
    }

    public async Task<MoveItTaskConfiguration> UpdateTraditionalNativeSchedulesAsync(string taskId, JsonArray schedulePatch, CancellationToken cancellationToken = default)
    {
        if (_access is not null) await _access.RequireAsync("Task", RequireId(taskId), "Edit", cancellationToken);
        return await TaskNativeSchedules.UpdateAsync(_com, taskId, schedulePatch, cancellationToken);
    }

    public async Task<MoveItTaskConfiguration> CreateFromNativeConfigurationAsync(string name, string xml, bool leaveDisabled, CancellationToken cancellationToken = default)
    {
        if (_access is not null) await _access.RequireXmlCreationAsync("Task", System.Xml.Linq.XElement.Parse(xml), cancellationToken);
        return await _com.CreateTaskFromConfigurationAsync(name, xml, leaveDisabled, cancellationToken);
    }

    public Task<MoveItTaskConfiguration> GetNativeConfigurationAsync(string taskId, CancellationToken cancellationToken = default)
        => _com.GetTaskConfigurationAsync(RequireId(taskId), cancellationToken);

    public async Task<MoveItTaskConfiguration> RestoreNativeConfigurationAsync(string taskId, string xml, CancellationToken cancellationToken = default)
    {
        if (_access is not null) await _access.RequireAsync("Task", RequireId(taskId), "Edit", cancellationToken);
        return await _com.RestoreTaskConfigurationAsync(RequireId(taskId), xml, cancellationToken);
    }

    // MOVEit's documented REST DELETE is common to all three Task types.
    // Bulk preflight and deletion-order safeguards live in AutomationTaskService.
    public async Task DeleteAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (_access is not null) await _access.RequireAsync("Task", RequireId(taskId), "Delete", cancellationToken);
        await _rest.DeleteTaskAsync(RequireId(taskId), cancellationToken);
    }

    public Task<JsonObject> GetSourceTimestampsAsync(string taskId, string hostId, CancellationToken cancellationToken = default)
        => _rest.GetTaskNewFileTimestampsV0Async(RequireId(taskId), RequireId(hostId), cancellationToken);

    public Task<JsonObject> SetSourceTimestampAsync(string taskId, string hostId, string path, string mask, string timestamp, CancellationToken cancellationToken = default)
        => _rest.UpdateTaskNewFileTimestampV0Async(RequireId(taskId), RequireId(hostId), path, mask, timestamp, cancellationToken);

    private static string RequireId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Task ID is required.", nameof(id));
        return id.Trim();
    }
}

