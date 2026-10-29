// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Xml.Linq;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Services.Metadata;

public interface IRecycleBinService
{
    bool Enabled { get; }
    // Purpose: Captures for deletion for recovery or undo processing.
    Task<RecycleCaptureResult> CaptureForDeletionAsync(string objectType, IEnumerable<string> objectIds, string username, bool allowDeleteWithoutRecovery, CancellationToken cancellationToken = default);
    // Purpose: Discards captured that is no longer needed.
    Task DiscardCapturedAsync(IEnumerable<string> recycleItemIds, CancellationToken cancellationToken = default);
    // Purpose: Returns visible items.
    Task<IReadOnlyList<RecycleBinItemView>> GetVisibleItemsAsync(string username, bool isAdmin, CancellationToken cancellationToken = default);
    // Purpose: Validates restore and returns the information required before execution.
    Task<RecycleRestorePreflight> PreflightRestoreAsync(IEnumerable<string> recycleItemIds, string username, bool isAdmin, CancellationToken cancellationToken = default);
    // Purpose: Restores selected Recycle Bin items through their resource-specific restore workflows.
    Task<RecycleRestoreResult> RestoreAsync(IEnumerable<string> recycleItemIds, string username, bool isAdmin, bool overwriteConflicts, CancellationToken cancellationToken = default);
    // Purpose: Permanently empties the current environment Recycle Bin.
    Task<int> EmptyAsync(bool isAdmin, CancellationToken cancellationToken = default);
}

public sealed record RecycleCaptureFailure(string ObjectId, string Message);
public sealed record RecycleCaptureResult(IReadOnlyDictionary<string, string> RecycleIdsByObjectId, IReadOnlyList<RecycleCaptureFailure> Failures)
{
    public bool NeedsConfirmation => Failures.Count > 0;
}
public sealed record RecycleBinItemView(string Id, string ObjectType, string ObjectName, string DeletedBy, DateTimeOffset DeletedUtc);
public sealed record RecycleRestoreConflict(string RecycleItemId, string ObjectType, string ObjectName, string ExistingObjectId, bool CanOverwrite, string Message);
public sealed record RecycleRestorePreflight(IReadOnlyList<RecycleRestoreConflict> Conflicts, int SelectedCount);
public sealed record RecycleRestoreItemResult(string RecycleItemId, string ObjectType, string ObjectName, bool Success, bool Overwritten, string NewObjectId, string Message);
public sealed record RecycleRestoreResult(IReadOnlyList<RecycleRestoreItemResult> Items)
{
    public int Succeeded => Items.Count(item => item.Success);
    public int Failed => Items.Count(item => !item.Success);
}

public sealed class RecycleBinService : IRecycleBinService
{
    private readonly IRecycleBinRepository _repository;
    private readonly IMoveItObjectAccessService? _access;
    private readonly IAutomationTaskService _tasks;
    private readonly IAutomationHostService _hosts;
    private readonly IAutomationScriptService _scripts;
    private readonly IAutomationDateListService _dateLists;
    private readonly ITaskUndoService _taskUndo;
    private readonly IHostUndoService _hostUndo;
    private readonly IScriptUndoService _scriptUndo;
    private readonly IDateListUndoService _dateListUndo;
    private readonly IMetadataRepository _metadata;
    private readonly IMoveItSessionService _session;
    private readonly RecycleBinOptions _options;
    private readonly ILogger<RecycleBinService> _logger;

    // Purpose: Initializes recycle bin service with its required dependencies.
    public RecycleBinService(IRecycleBinRepository repository, IAutomationTaskService tasks, IAutomationHostService hosts, IAutomationScriptService scripts, IAutomationDateListService dateLists,
        ITaskUndoService taskUndo, IHostUndoService hostUndo, IScriptUndoService scriptUndo, IDateListUndoService dateListUndo, IMetadataRepository metadata, IMoveItSessionService session,
        IOptionsSnapshot<RecycleBinOptions> options, ILogger<RecycleBinService> logger, IMoveItObjectAccessService? access = null)
    {
        _repository = repository; _tasks = tasks; _hosts = hosts; _scripts = scripts; _dateLists = dateLists;
        _taskUndo = taskUndo; _hostUndo = hostUndo; _scriptUndo = scriptUndo; _dateListUndo = dateListUndo; _metadata = metadata; _session = session;
        _options = options.Value; _logger = logger; _access = access;
    }

    public bool Enabled => _options.Enabled.Value;

    // Purpose: Captures for deletion for recovery or undo processing.
    public async Task<RecycleCaptureResult> CaptureForDeletionAsync(string objectType, IEnumerable<string> objectIds, string username, bool allowDeleteWithoutRecovery, CancellationToken cancellationToken = default)
    {
        if (!Enabled) return new RecycleCaptureResult(new Dictionary<string, string>(), Array.Empty<RecycleCaptureFailure>());
        await PurgeExpiredAsync(cancellationToken);
        var ids = objectIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var normalizedType = NormalizeType(objectType);
        if (normalizedType.Equals("Script", StringComparison.OrdinalIgnoreCase) && ids.Count > 0)
        {
            var scriptInventory = await _scripts.GetScriptSnapshotAsync(false, cancellationToken);
            var builtInIds = scriptInventory.Scripts
                .Where(script => script.IsBuiltIn && !string.IsNullOrWhiteSpace(script.Id))
                .Select(script => script.Id.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            ids = ids.Where(id => !builtInIds.Contains(id)).ToList();
        }

        var captures = new List<RecycleBinItemEntity>();
        var failures = new List<RecycleCaptureFailure>();
        var env = GetEnvironmentKey();
        var user = (username ?? string.Empty).Trim();

        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var item = await CaptureOneAsync(env, normalizedType, id, user, cancellationToken);
                captures.Add(item);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                // A recovery-copy failure must never bypass the caller's explicit "Delete Anyway"
                // safeguard. Treat any non-cancellation snapshot exception as a capture failure.
                failures.Add(new RecycleCaptureFailure(id, ex.Message));
                _logger.LogWarning(ex, "MAUI could not create a Recycle Bin snapshot for {ObjectType} {ObjectId}.", objectType, id);
            }
        }

        if (failures.Count > 0 && !allowDeleteWithoutRecovery)
            return new RecycleCaptureResult(new Dictionary<string, string>(), failures);

        if (captures.Count > 0)
        {
            try { await _repository.AddRangeAsync(captures, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MAUI could not store Recycle Bin snapshots before deletion.");
                if (!allowDeleteWithoutRecovery)
                    return new RecycleCaptureResult(new Dictionary<string, string>(), ids.Select(id => new RecycleCaptureFailure(id, "The recovery copy could not be stored in the MAUI database.")).ToList());
                captures.Clear();
            }
        }

        return new RecycleCaptureResult(captures.ToDictionary(item => item.OriginalObjectId, item => item.Id, StringComparer.OrdinalIgnoreCase), failures);
    }

    // Purpose: Discards captured that is no longer needed.
    public Task DiscardCapturedAsync(IEnumerable<string> recycleItemIds, CancellationToken cancellationToken = default)
        => _repository.DeleteByIdsAsync(recycleItemIds, cancellationToken);

    // Purpose: Returns visible items.
    public async Task<IReadOnlyList<RecycleBinItemView>> GetVisibleItemsAsync(string username, bool isAdmin, CancellationToken cancellationToken = default)
    {
        if (!Enabled) return Array.Empty<RecycleBinItemView>();
        await PurgeExpiredAsync(cancellationToken);
        var items = await _repository.GetVisibleAsync(GetEnvironmentKey(), username, isAdmin, cancellationToken);
        return items.Select(item => new RecycleBinItemView(item.Id, item.ObjectType, item.ObjectName, item.DeletedBy, item.DeletedUtc)).ToList();
    }

    // Purpose: Validates restore and returns the information required before execution.
    public async Task<RecycleRestorePreflight> PreflightRestoreAsync(IEnumerable<string> recycleItemIds, string username, bool isAdmin, CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        var items = await GetSelectedAsync(recycleItemIds, username, isAdmin, cancellationToken);
        var conflicts = await FindConflictsAsync(items, cancellationToken);
        await RequireRestoreAccessAsync(items, conflicts, cancellationToken);
        await ValidateRestoreLicensePreflightAsync(items, conflicts, cancellationToken);
        return new RecycleRestorePreflight(conflicts, items.Count);
    }

    // Purpose: Restores selected Recycle Bin items through their resource-specific restore workflows.
    public async Task<RecycleRestoreResult> RestoreAsync(IEnumerable<string> recycleItemIds, string username, bool isAdmin, bool overwriteConflicts, CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        var items = await GetSelectedAsync(recycleItemIds, username, isAdmin, cancellationToken);
        var conflicts = (await FindConflictsAsync(items, cancellationToken)).ToDictionary(item => item.RecycleItemId, StringComparer.OrdinalIgnoreCase);
        await RequireRestoreAccessAsync(items, conflicts.Values.ToArray(), cancellationToken);
        var session = _session.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        var capabilities = MoveItLicensePolicy.GetCapabilities(session);
        if (!capabilities.CustomScriptsLicensed && items.Any(item => item.ObjectType.Equals("Script", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(MoveItLicensePolicy.GetCustomScriptBlock(session));

        var newTasks = items.Where(item => item.ObjectType.Equals("Task", StringComparison.OrdinalIgnoreCase) && !conflicts.ContainsKey(item.Id)).ToList();
        var newHosts = items.Where(item => item.ObjectType.Equals("Host", StringComparison.OrdinalIgnoreCase) && !conflicts.ContainsKey(item.Id)).ToList();
        if (!capabilities.AdvancedTasksLicensed && newTasks.Any(item => IsAdvancedTaskConfiguration(item.ObjectXml)))
            throw new InvalidOperationException("The connected MOVEit Automation license does not include Advanced Tasks. Advanced Tasks cannot be restored as new Tasks from the Recycle Bin.");

        using var taskCreationLease = newTasks.Count > 0
            ? await MoveItLicensePolicy.AcquireTaskCreationAsync(session, cancellationToken)
            : null;
        using var hostCreationLease = newHosts.Count > 0
            ? await MoveItLicensePolicy.AcquireHostCreationAsync(session, cancellationToken)
            : null;

        if (newTasks.Count > 0)
        {
            var currentTaskCount = (await _tasks.GetTaskSnapshotAsync(true, cancellationToken)).Tasks.Count;
            var taskCapacityBlock = MoveItLicensePolicy.GetTaskCapacityBlock(session, currentTaskCount, newTasks.Count);
            if (!string.IsNullOrWhiteSpace(taskCapacityBlock)) throw new InvalidOperationException(taskCapacityBlock);
        }
        if (newHosts.Count > 0)
        {
            var currentHostCount = (await _hosts.GetHostSnapshotAsync(true, cancellationToken)).Hosts.Count;
            var hostCapacityBlock = MoveItLicensePolicy.GetHostCapacityBlock(session, currentHostCount, newHosts.Count);
            if (!string.IsNullOrWhiteSpace(hostCapacityBlock)) throw new InvalidOperationException(hostCapacityBlock);
        }

        if (conflicts.Values.Any(conflict => !conflict.CanOverwrite))
            throw new InvalidOperationException("One or more selected items conflict with an existing MOVEit object that cannot be safely overwritten. Resolve the listed conflict before restoring.");
        if (conflicts.Count > 0 && !overwriteConflicts)
            throw new InvalidOperationException("One or more selected items conflict with an existing MOVEit object. Confirm overwrite before restoring.");

        var results = new List<RecycleRestoreItemResult>();
        foreach (var item in items.OrderBy(i => i.DeletedUtc))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = conflicts.TryGetValue(item.Id, out var conflict)
                    ? await OverwriteExistingAsync(item, conflict, username, cancellationToken)
                    : await RestoreAsNewAsync(item, username, cancellationToken);
                await _repository.DeleteByIdsAsync(new[] { item.Id }, cancellationToken);
                results.Add(result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
            {
                _logger.LogWarning(ex, "Recycle Bin restore failed for {ObjectType} {ObjectName} ({RecycleId}).", item.ObjectType, item.ObjectName, item.Id);
                results.Add(new RecycleRestoreItemResult(item.Id, item.ObjectType, item.ObjectName, false, conflicts.ContainsKey(item.Id), string.Empty, ex.Message));
            }
        }
        return new RecycleRestoreResult(results);
    }

    // Purpose: Permanently empties the current environment Recycle Bin.
    public Task<int> EmptyAsync(bool isAdmin, CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        if (!isAdmin) throw new UnauthorizedAccessException("Only a MAUI Admin can empty the Recycle Bin.");
        return _repository.EmptyAsync(GetEnvironmentKey(), cancellationToken);
    }

    // Purpose: Captures one.
    private async Task<RecycleBinItemEntity> CaptureOneAsync(string env, string type, string id, string username, CancellationToken ct)
    {
        string name, xml;
        switch (type)
        {
            case "Task":
                var task = await _tasks.GetTaskConfigurationSnapshotAsync(id, ct); name = task.Name; xml = task.Xml; break;
            case "Host":
                var host = await _hosts.GetHostConfigurationSnapshotAsync(id, ct); name = host.Name; xml = host.Xml; break;
            case "Script":
                var script = await _scripts.GetScriptConfigurationSnapshotAsync(id, ct); name = script.Name; xml = script.Xml; break;
            case "DateList":
                var dateList = await _dateLists.GetConfigurationSnapshotAsync(id, ct); name = dateList.Name; xml = dateList.Xml; break;
            default: throw new InvalidOperationException($"Recycle Bin does not support object type '{type}'.");
        }
        return new RecycleBinItemEntity
        {
            Id = Guid.NewGuid().ToString("D"), EnvironmentKey = env, ObjectType = type, OriginalObjectId = id,
            ObjectName = name, DeletedBy = username, DeletedByKey = username.Trim().ToUpperInvariant(), DeletedUtc = DateTimeOffset.UtcNow, ObjectXml = xml
        };
    }

    // Purpose: Finds conflicts.
    private async Task<IReadOnlyList<RecycleRestoreConflict>> FindConflictsAsync(IReadOnlyList<RecycleBinItemEntity> items, CancellationToken ct)
    {
        var conflicts = new List<RecycleRestoreConflict>();
        var duplicateSelections = items
            .GroupBy(item => $"{item.ObjectType}\u001f{item.ObjectName}", StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group)
            .ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var duplicate in duplicateSelections.Values)
            conflicts.Add(new RecycleRestoreConflict(duplicate.Id, duplicate.ObjectType, duplicate.ObjectName, string.Empty, false,
                "Multiple selected Recycle Bin items have the same type and name. Restore only one version of this item at a time."));

        var tasks = items.Any(i => i.ObjectType == "Task") ? (await _tasks.GetTaskSnapshotAsync(true, ct)).Tasks : Array.Empty<AutomationTask>();
        var hosts = items.Any(i => i.ObjectType == "Host") ? (await _hosts.GetHostSnapshotAsync(true, ct)).Hosts : Array.Empty<AutomationHost>();
        var scripts = items.Any(i => i.ObjectType == "Script") ? (await _scripts.GetScriptSnapshotAsync(true, ct)).Scripts : Array.Empty<AutomationScript>();
        var dateLists = items.Any(i => i.ObjectType == "DateList") ? (await _dateLists.GetSnapshotAsync(true, ct)).DateLists : Array.Empty<AutomationDateList>();

        foreach (var item in items)
        {
            if (duplicateSelections.ContainsKey(item.Id)) continue;
            switch (item.ObjectType)
            {
                case "Task":
                {
                    var existing = tasks.FirstOrDefault(x => x.Name.Equals(item.ObjectName, StringComparison.OrdinalIgnoreCase));
                    if (existing is null) break;
                    var canOverwrite = existing.CanEditMoveItFields && !existing.Status.Equals("Running", StringComparison.OrdinalIgnoreCase);
                    var message = !existing.CanEditMoveItFields
                        ? "MOVEit reports that the current user cannot edit the existing task."
                        : canOverwrite ? string.Empty : "The existing task is currently running and cannot be overwritten.";
                    if (canOverwrite)
                    {
                        var current = await _tasks.GetTaskConfigurationSnapshotAsync(existing.Id, ct);
                        canOverwrite = SameRootConfigurationType(item.ObjectXml, current.Xml, "TT");
                        if (!canOverwrite) message = "The existing task has a different MOVEit task type and cannot be overwritten safely.";
                    }
                    conflicts.Add(new(item.Id, item.ObjectType, item.ObjectName, existing.Id, canOverwrite, message));
                    break;
                }
                case "Host":
                {
                    var existing = hosts.FirstOrDefault(x => x.Name.Equals(item.ObjectName, StringComparison.OrdinalIgnoreCase));
                    if (existing is null)
                    {
                        var recycledType = XElement.Parse(item.ObjectXml, LoadOptions.PreserveWhitespace).Name.LocalName;
                        if (!CanCreateHostType(recycledType))
                            conflicts.Add(new(item.Id, item.ObjectType, item.ObjectName, string.Empty, false,
                                $"MAUI cannot currently recreate MOVEit Host type '{recycledType}' as a new Host. An existing same-type Host would be required for an overwrite restore."));
                        break;
                    }
                    if (!existing.CanEditDelete)
                    {
                        conflicts.Add(new(item.Id, item.ObjectType, item.ObjectName, existing.Id, false, "MOVEit reports that the current user cannot edit the existing Host."));
                        break;
                    }
                    var current = await _hosts.GetHostConfigurationSnapshotAsync(existing.Id, ct);
                    var canOverwrite = SameRootName(item.ObjectXml, current.Xml);
                    conflicts.Add(new(item.Id, item.ObjectType, item.ObjectName, existing.Id, canOverwrite,
                        canOverwrite ? string.Empty : "The existing host has a different MOVEit host type and cannot be overwritten safely."));
                    break;
                }
                case "Script":
                {
                    var existing = scripts.FirstOrDefault(x => x.Name.Equals(item.ObjectName, StringComparison.OrdinalIgnoreCase));
                    if (existing is null) break;
                    if (existing.IsBuiltIn)
                    {
                        conflicts.Add(new(item.Id, item.ObjectType, item.ObjectName, existing.Id, false, "A built-in MOVEit Script uses this name and cannot be overwritten."));
                        break;
                    }
                    if (!existing.CanEditDelete)
                    {
                        conflicts.Add(new(item.Id, item.ObjectType, item.ObjectName, existing.Id, false, "MOVEit reports that the current user cannot edit the existing Script."));
                        break;
                    }
                    var current = await _scripts.GetScriptConfigurationSnapshotAsync(existing.Id, ct);
                    var canOverwrite = SameRootConfigurationType(item.ObjectXml, current.Xml, "Lang");
                    conflicts.Add(new(item.Id, item.ObjectType, item.ObjectName, existing.Id, canOverwrite,
                        canOverwrite ? string.Empty : "The existing Script uses a different language and cannot be overwritten safely."));
                    break;
                }
                case "DateList":
                {
                    var existing = dateLists.FirstOrDefault(x => x.Name.Equals(item.ObjectName, StringComparison.OrdinalIgnoreCase));
                    if (existing is null) break;
                    if (!existing.CanEditDelete)
                    {
                        conflicts.Add(new(item.Id, item.ObjectType, item.ObjectName, existing.Id, false, "MOVEit reports that the current user cannot edit the existing Date List."));
                        break;
                    }
                    var current = await _dateLists.GetConfigurationSnapshotAsync(existing.Id, ct);
                    var canOverwrite = SameRootName(item.ObjectXml, current.Xml);
                    conflicts.Add(new(item.Id, item.ObjectType, item.ObjectName, existing.Id, canOverwrite,
                        canOverwrite ? string.Empty : "The existing object is not a compatible MOVEit Date List and cannot be overwritten safely."));
                    break;
                }
            }
        }
        return conflicts;
    }

    // Purpose: Restores a Recycle Bin item over an existing MOVEit object after validation.
    private async Task<RecycleRestoreItemResult> OverwriteExistingAsync(RecycleBinItemEntity item, RecycleRestoreConflict conflict, string username, CancellationToken ct)
    {
        // Overwrite is always protected with the existing per-user Undo mechanism before the live object is changed.
        // Once MOVEit accepts the replacement, update MAUI's Modified By/On audit fields for the live object.
        switch (item.ObjectType)
        {
            case "Task":
            {
                await _taskUndo.CaptureAsync(conflict.ExistingObjectId, username, ct);
                var taskXml = RewriteIdentity(item.ObjectXml, conflict.ExistingObjectId, item.ObjectName, disableTask: true);
                var task = await _tasks.RestoreTaskConfigurationAsync(conflict.ExistingObjectId, taskXml, ct);
                var auditWarning = await RecordRestoreMetadataAsync(item.ObjectType, task.Id, username, created: false, ct);
                return new(item.Id, item.ObjectType, item.ObjectName, true, true, task.Id,
                    CombineMessages("Existing task was overwritten. The restored task was left disabled and the previous version is available through Undo.", auditWarning));
            }
            case "Host":
            {
                await _hostUndo.CaptureAsync(conflict.ExistingObjectId, username, ct);
                var hostXml = RewriteIdentity(item.ObjectXml, conflict.ExistingObjectId, item.ObjectName, disableTask: false);
                var host = await _hosts.RestoreHostConfigurationAsync(conflict.ExistingObjectId, hostXml, ct);
                var auditWarning = await RecordRestoreMetadataAsync(item.ObjectType, host.Id, username, created: false, ct);
                return new(item.Id, item.ObjectType, item.ObjectName, true, true, host.Id,
                    CombineMessages("Existing host was overwritten. The previous version is available through Undo.", auditWarning));
            }
            case "Script":
            {
                await _scriptUndo.CaptureAsync(conflict.ExistingObjectId, username, ct);
                var scriptXml = RewriteIdentity(item.ObjectXml, conflict.ExistingObjectId, item.ObjectName, disableTask: false);
                var script = await _scripts.RestoreScriptConfigurationAsync(conflict.ExistingObjectId, scriptXml, ct);
                var auditWarning = await RecordRestoreMetadataAsync(item.ObjectType, script.Id, username, created: false, ct);
                return new(item.Id, item.ObjectType, item.ObjectName, true, true, script.Id,
                    CombineMessages("Existing Script was overwritten. The previous version is available through Undo.", auditWarning));
            }
            case "DateList":
            {
                await _dateListUndo.CaptureAsync(conflict.ExistingObjectId, username, ct);
                var dateListXml = RewriteIdentity(item.ObjectXml, conflict.ExistingObjectId, item.ObjectName, disableTask: false);
                var dateList = await _dateLists.RestoreConfigurationAsync(conflict.ExistingObjectId, dateListXml, ct);
                var auditWarning = await RecordRestoreMetadataAsync(item.ObjectType, dateList.Id, username, created: false, ct);
                return new(item.Id, item.ObjectType, item.ObjectName, true, true, dateList.Id,
                    CombineMessages("Existing Date List was overwritten. The previous version is available through Undo.", auditWarning));
            }
            default: throw new InvalidOperationException("Unsupported Recycle Bin object type.");
        }
    }

    // Purpose: Restores as new.
    // A restore is an import: require New, plus Edit on each overwritten destination,
    // before changing any object in a mixed selection. Preserve native group assignments.
    private async Task RequireRestoreAccessAsync(IReadOnlyList<RecycleBinItemEntity> items,
        IReadOnlyList<RecycleRestoreConflict> conflicts, CancellationToken ct)
    {
        if (_access is null) return;
        foreach (var item in items)
        {
            await _access.RequireXmlCreationAsync(item.ObjectType, XElement.Parse(item.ObjectXml), ct);
            var conflict = conflicts.FirstOrDefault(value => value.RecycleItemId == item.Id);
            if (conflict is not null) await _access.RequireAsync(item.ObjectType, conflict.ExistingObjectId, "Edit", ct);
        }
    }

    private async Task<RecycleRestoreItemResult> RestoreAsNewAsync(RecycleBinItemEntity item, string username, CancellationToken ct)
    {
        switch (item.ObjectType)
        {
            case "Task":
            {
                var task = await _tasks.CreateTaskFromConfigurationAsync(item.ObjectName, item.ObjectXml, leaveDisabled: true, ct);
                var auditWarning = await RecordRestoreMetadataAsync(item.ObjectType, task.Id, username, created: true, ct);
                var restoreNote = task.Id == item.OriginalObjectId ? string.Empty : "The task was restored with a new MOVEit ID and was left disabled for review.";
                return new(item.Id, item.ObjectType, item.ObjectName, true, false, task.Id, CombineMessages(restoreNote, auditWarning));
            }
            case "Host":
            {
                var root = XElement.Parse(item.ObjectXml, LoadOptions.PreserveWhitespace);
                var typeCode = root.Name.LocalName;
                // Share creation requires its original connection even before configuration restore.
                var unc = (string?)root.Attribute("UNC") ?? (string?)root.Element("UNC");
                var shell = await _hosts.CreateHostAsync(item.ObjectName, typeCode, ct, unc, root.Elements().FirstOrDefault(element => element.Name.LocalName == "Group")?.Value);
                try
                {
                    var restored = await _hosts.RestoreHostConfigurationAsync(shell.Id, RewriteIdentity(item.ObjectXml, shell.Id, item.ObjectName, false), ct);
                    var auditWarning = await RecordRestoreMetadataAsync(item.ObjectType, restored.Id, username, created: true, ct);
                    var restoreNote = restored.Id == item.OriginalObjectId ? string.Empty : "The Host was restored with a new MOVEit ID.";
                    return new(item.Id, item.ObjectType, item.ObjectName, true, false, restored.Id, CombineMessages(restoreNote, auditWarning));
                }
                catch
                {
                    try { await _hosts.DeleteHostAsync(shell.Id, ct); } catch (Exception cleanupEx) { _logger.LogWarning(cleanupEx, "Unable to remove temporary Host shell {HostId} after Recycle Bin restore failure.", shell.Id); }
                    throw;
                }
            }
            case "Script":
            {
                var root = XElement.Parse(item.ObjectXml, LoadOptions.PreserveWhitespace);
                var language = ((string?)root.Attribute("Lang") ?? "VBScript").Trim();
                var shell = await _scripts.CreateScriptAsync(item.ObjectName, language, ct, root.Elements().FirstOrDefault(element => element.Name.LocalName == "Group")?.Value);
                try
                {
                    var restored = await _scripts.RestoreScriptConfigurationAsync(shell.Id, RewriteIdentity(item.ObjectXml, shell.Id, item.ObjectName, false), ct);
                    var auditWarning = await RecordRestoreMetadataAsync(item.ObjectType, restored.Id, username, created: true, ct);
                    var restoreNote = restored.Id == item.OriginalObjectId ? string.Empty : "The Script was restored with a new MOVEit ID.";
                    return new(item.Id, item.ObjectType, item.ObjectName, true, false, restored.Id, CombineMessages(restoreNote, auditWarning));
                }
                catch
                {
                    try { await _scripts.DeleteScriptAsync(shell.Id, ct); } catch (Exception cleanupEx) { _logger.LogWarning(cleanupEx, "Unable to remove temporary Script shell {ScriptId} after Recycle Bin restore failure.", shell.Id); }
                    throw;
                }
            }
            case "DateList":
            {
                var shell = await _dateLists.CreateAsync(item.ObjectName, string.Empty, ct);
                try
                {
                    var restored = await _dateLists.RestoreConfigurationAsync(shell.Id, RewriteIdentity(item.ObjectXml, shell.Id, item.ObjectName, false), ct);
                    var auditWarning = await RecordRestoreMetadataAsync(item.ObjectType, restored.Id, username, created: true, ct);
                    var restoreNote = restored.Id == item.OriginalObjectId ? string.Empty : "The Date List was restored with a new MOVEit ID.";
                    return new(item.Id, item.ObjectType, item.ObjectName, true, false, restored.Id, CombineMessages(restoreNote, auditWarning));
                }
                catch
                {
                    try { await _dateLists.DeleteAsync(new[] { shell.Id }, ct); } catch (Exception cleanupEx) { _logger.LogWarning(cleanupEx, "Unable to remove temporary Date List shell {DateListId} after Recycle Bin restore failure.", shell.Id); }
                    throw;
                }
            }
            default: throw new InvalidOperationException("Unsupported Recycle Bin object type.");
        }
    }

    // Purpose: Records restore metadata.
    private async Task<string> RecordRestoreMetadataAsync(string objectType, string objectId, string username, bool created, CancellationToken ct)
    {
        try
        {
            var environmentKey = GetEnvironmentKey();
            if (created)
                await _metadata.RecordObjectCreatedAsync(environmentKey, objectType, objectId, username, ct);
            else
                await _metadata.RecordObjectConfigurationModifiedAsync(environmentKey, objectType, objectId, username, ct);
            return string.Empty;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recycle Bin restored {ObjectType} {ObjectId}, but MAUI could not update Created/Modified audit metadata.", objectType, objectId);
            return "The item was restored in MOVEit, but MAUI could not fully update its Created/Modified audit metadata.";
        }
    }

    // Purpose: Combines nonblank warning messages into one user-facing message.
    private static string CombineMessages(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first)) return second ?? string.Empty;
        if (string.IsNullOrWhiteSpace(second)) return first;
        return $"{first} {second}";
    }

    // Purpose: Returns selected.
    private async Task<IReadOnlyList<RecycleBinItemEntity>> GetSelectedAsync(IEnumerable<string> ids, string username, bool isAdmin, CancellationToken ct)
    {
        await PurgeExpiredAsync(ct);
        var selected = ids.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (selected.Count == 0) throw new InvalidOperationException("Select at least one Recycle Bin item.");
        var items = await _repository.GetByIdsAsync(GetEnvironmentKey(), selected, username, isAdmin, ct);
        if (items.Count != selected.Count) throw new InvalidOperationException("One or more selected Recycle Bin items are no longer available or are not available to the current user.");
        return items;
    }


    // Purpose: Applies the same license/capacity rules during preflight that RestoreAsync enforces under creation locks.
    private async Task ValidateRestoreLicensePreflightAsync(
        IReadOnlyList<RecycleBinItemEntity> items,
        IReadOnlyList<RecycleRestoreConflict> conflicts,
        CancellationToken cancellationToken)
    {
        var session = _session.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        var capabilities = MoveItLicensePolicy.GetCapabilities(session);
        if (!capabilities.CustomScriptsLicensed && items.Any(item => item.ObjectType.Equals("Script", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(MoveItLicensePolicy.GetCustomScriptBlock(session));

        var conflictIds = conflicts.Select(item => item.RecycleItemId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newTasks = items.Where(item => item.ObjectType.Equals("Task", StringComparison.OrdinalIgnoreCase) && !conflictIds.Contains(item.Id)).ToList();
        var newHosts = items.Where(item => item.ObjectType.Equals("Host", StringComparison.OrdinalIgnoreCase) && !conflictIds.Contains(item.Id)).ToList();

        if (!capabilities.AdvancedTasksLicensed && newTasks.Any(item => IsAdvancedTaskConfiguration(item.ObjectXml)))
            throw new InvalidOperationException("The connected MOVEit Automation license does not include Advanced Tasks. Advanced Tasks cannot be restored as new Tasks from the Recycle Bin.");

        if (newTasks.Count > 0)
        {
            var currentTaskCount = (await _tasks.GetTaskSnapshotAsync(false, cancellationToken)).Tasks.Count;
            var taskCapacityBlock = MoveItLicensePolicy.GetTaskCapacityBlock(session, currentTaskCount, newTasks.Count);
            if (!string.IsNullOrWhiteSpace(taskCapacityBlock)) throw new InvalidOperationException(taskCapacityBlock);
        }

        if (newHosts.Count > 0)
        {
            var currentHostCount = (await _hosts.GetHostSnapshotAsync(false, cancellationToken)).Hosts.Count;
            var hostCapacityBlock = MoveItLicensePolicy.GetHostCapacityBlock(session, currentHostCount, newHosts.Count);
            if (!string.IsNullOrWhiteSpace(hostCapacityBlock)) throw new InvalidOperationException(hostCapacityBlock);
        }
    }

    // Purpose: Returns whether create host type.
    private static bool CanCreateHostType(string? hostType)
    {
        var value = (hostType ?? string.Empty).Trim();
        return value.Equals("Share", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("siLock", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("FTP", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("SSHFTP", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("POP3", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("SMTP", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("S3", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("AzureBlob", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("SharePoint", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("GoogleStorage", StringComparison.OrdinalIgnoreCase);
    }

    // Purpose: Returns whether a recycled Task configuration is an Advanced Task (native TT="COS").
    private static bool IsAdvancedTaskConfiguration(string xml)
    {
        var root = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
        if (!root.Name.LocalName.Equals("Task", StringComparison.OrdinalIgnoreCase)) return false;
        return string.Equals(((string?)root.Attribute("TT") ?? string.Empty).Trim(), "COS", StringComparison.OrdinalIgnoreCase);
    }

    // Purpose: Returns whether two configuration documents have the same root element name.
    private static bool SameRootName(string leftXml, string rightXml)
    {
        var left = XElement.Parse(leftXml, LoadOptions.PreserveWhitespace);
        var right = XElement.Parse(rightXml, LoadOptions.PreserveWhitespace);
        return left.Name.LocalName.Equals(right.Name.LocalName, StringComparison.OrdinalIgnoreCase);
    }

    // Purpose: Returns whether two configuration documents share the same root type attribute.
    private static bool SameRootConfigurationType(string leftXml, string rightXml, string attributeName)
    {
        var left = XElement.Parse(leftXml, LoadOptions.PreserveWhitespace);
        var right = XElement.Parse(rightXml, LoadOptions.PreserveWhitespace);
        var leftValue = ((string?)left.Attribute(attributeName) ?? string.Empty).Trim();
        var rightValue = ((string?)right.Attribute(attributeName) ?? string.Empty).Trim();
        return left.Name.LocalName.Equals(right.Name.LocalName, StringComparison.OrdinalIgnoreCase) &&
               leftValue.Equals(rightValue, StringComparison.OrdinalIgnoreCase);
    }

    // Purpose: Rewrites restored configuration identity values before applying the snapshot.
    private static string RewriteIdentity(string xml, string id, string name, bool disableTask)
    {
        var root = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
        root.SetAttributeValue("ID", id);
        root.SetAttributeValue("Name", name);
        if (disableTask && root.Name.LocalName.Equals("Task", StringComparison.OrdinalIgnoreCase)) root.SetAttributeValue("Active", "0");
        return root.ToString(SaveOptions.DisableFormatting);
    }

    // Purpose: Purges Recycle Bin items that exceed the configured retention period.
    private async Task PurgeExpiredAsync(CancellationToken cancellationToken)
    {
        try
        {
            var retentionDays = Math.Clamp(_options.RetentionDays.Value, 1, 365);
            var count = await _repository.PurgeOlderThanAsync(GetEnvironmentKey(), DateTimeOffset.UtcNow.AddDays(-retentionDays), cancellationToken);
            if (count > 0) _logger.LogInformation("Purged {Count} Recycle Bin item(s) older than {RetentionDays} days.", count, retentionDays);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // Retention cleanup is maintenance, not a prerequisite for listing or protecting a delete.
            // Log it and continue so a cleanup problem cannot disable the Recycle Bin workflow.
            _logger.LogWarning(ex, "MAUI could not purge expired Recycle Bin items. The current operation will continue.");
        }
    }

    // Purpose: Returns environment key.
    private string GetEnvironmentKey() => (_session.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.")).GetEnvironmentKey();
    // Purpose: Ensures enabled.
    private void EnsureEnabled() { if (!Enabled) throw new InvalidOperationException("The MAUI Recycle Bin is disabled in Configuration."); }
    // Purpose: Normalizes type.
    private static string NormalizeType(string type)
    {
        var value = (type ?? string.Empty).Trim();
        if (value.Equals("Task", StringComparison.OrdinalIgnoreCase)) return "Task";
        if (value.Equals("Host", StringComparison.OrdinalIgnoreCase)) return "Host";
        if (value.Equals("Script", StringComparison.OrdinalIgnoreCase)) return "Script";
        if (value.Equals("DateList", StringComparison.OrdinalIgnoreCase) || value.Equals("Date List", StringComparison.OrdinalIgnoreCase)) return "DateList";
        throw new InvalidOperationException("Recycle Bin supports Tasks, Hosts, Scripts, and Date Lists only.");
    }
}

