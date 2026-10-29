// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using Microsoft.Extensions.Caching.Memory;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Services.Metadata;

public interface INativeEditorUndoService
{
    Task<NativeEditorUndoState> InitializeAsync(string launchId, string objectType, string objectId, string username, CancellationToken cancellationToken = default);
    Task<NativeEditorUndoState> AdvanceAsync(string launchId, string objectType, string objectId, string username, CancellationToken cancellationToken = default);
    // Rebinds an active Task editor undo session when MOVEit replaces the editor route with a newly-created Task ID.
    Task<NativeEditorUndoState> RebindAsync(string launchId, string objectType, string previousObjectId, string objectId, string username, CancellationToken cancellationToken = default);
    Task<NativeEditorUndoRestoreResult> RestoreAsync(string launchId, string objectType, string objectId, string username, CancellationToken cancellationToken = default);
    Task DiscardAsync(string launchId, string objectType, string objectId, string username, CancellationToken cancellationToken = default);
}

public sealed record NativeEditorUndoState(bool UndoAvailable, string ObjectType, string ObjectId, string ObjectName);
public sealed record NativeEditorUndoRestoreResult(string ObjectType, string ObjectId, string ObjectName, string Warning);

public sealed class NativeEditorUndoService : INativeEditorUndoService
{
    private static readonly TimeSpan EntryLifetime = TimeSpan.FromHours(8);
    private readonly IMemoryCache _cache;
    private readonly IConfigurationUndoWorkflow _workflow;
    private readonly IAutomationTaskService _tasks;
    private readonly IAutomationHostService _hosts;
    private readonly IAutomationScriptService _scripts;
    private readonly IAutomationDateListService _dateLists;
    private readonly IMetadataRepository _metadata;
    private readonly ILogger<NativeEditorUndoService> _logger;
    private readonly IMoveItObjectAccessService _access;

    public NativeEditorUndoService(
        IMemoryCache cache,
        IConfigurationUndoWorkflow workflow,
        IAutomationTaskService tasks,
        IAutomationHostService hosts,
        IAutomationScriptService scripts,
        IAutomationDateListService dateLists,
        IMetadataRepository metadata,
        ILogger<NativeEditorUndoService> logger,
        IMoveItObjectAccessService access)
    {
        _cache = cache;
        _workflow = workflow;
        _tasks = tasks;
        _hosts = hosts;
        _scripts = scripts;
        _dateLists = dateLists;
        _metadata = metadata;
        _logger = logger;
        _access = access;
    }

    public async Task<NativeEditorUndoState> InitializeAsync(string launchId, string objectType, string objectId, string username, CancellationToken cancellationToken = default)
    {
        var identity = NormalizeIdentity(launchId, objectType, objectId, username);
        // Native launchers skip Undo for read-only objects; direct requests must also
        // verify current edit access before a privileged snapshot or history lookup.
        await _access.RequireAsync(identity.ObjectType, identity.ObjectId, "Edit", cancellationToken);
        if (identity.ObjectType.Equals("Script", StringComparison.Ordinal))
        {
            var inventory = await _scripts.GetScriptSnapshotAsync(false, cancellationToken);
            var script = inventory.Scripts.FirstOrDefault(item => item.Id.Equals(identity.ObjectId, StringComparison.OrdinalIgnoreCase));
            if (script?.IsBuiltIn == true)
                throw new InvalidOperationException("Undo Last Change is not available for built-in MOVEit Scripts.");
        }

        var current = await CaptureWithRetryAsync(identity.ObjectType, identity.ObjectId, cancellationToken);
        var entry = new EditorUndoEntry(identity.ObjectType, current.Id, current.Name, identity.Username, current.Xml);
        _cache.Set(BuildCacheKey(identity), entry, new MemoryCacheEntryOptions { SlidingExpiration = EntryLifetime });
        return new(false, identity.ObjectType, current.Id, current.Name);
    }

    public async Task<NativeEditorUndoState> AdvanceAsync(string launchId, string objectType, string objectId, string username, CancellationToken cancellationToken = default)
    {
        var identity = NormalizeIdentity(launchId, objectType, objectId, username);
        await _access.RequireAsync(identity.ObjectType, identity.ObjectId, "Edit", cancellationToken);
        var entry = GetEntry(identity);
        await entry.Gate.WaitAsync(cancellationToken);
        try
        {
            ValidateEntry(entry, identity);
            var current = await CaptureWithRetryAsync(entry.ObjectType, entry.ObjectId, cancellationToken);
            if (!string.Equals(current.Xml, entry.BaselineXml, StringComparison.Ordinal))
            {
                entry.UndoXml = entry.BaselineXml;
                entry.BaselineXml = current.Xml;
                entry.ObjectName = current.Name;
            }
            return new(!string.IsNullOrEmpty(entry.UndoXml), entry.ObjectType, entry.ObjectId, entry.ObjectName);
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    public async Task<NativeEditorUndoState> RebindAsync(
        string launchId,
        string objectType,
        string previousObjectId,
        string objectId,
        string username,
        CancellationToken cancellationToken = default)
    {
        var previousIdentity = NormalizeIdentity(launchId, objectType, previousObjectId, username);
        var nextIdentity = NormalizeIdentity(launchId, objectType, objectId, username);
        if (!previousIdentity.ObjectType.Equals("Task", StringComparison.Ordinal))
            throw new InvalidOperationException("Native editor identity changes are supported only for MOVEit Tasks.");
        if (!previousIdentity.ObjectType.Equals(nextIdentity.ObjectType, StringComparison.Ordinal)
            || !previousIdentity.LaunchId.Equals(nextIdentity.LaunchId, StringComparison.Ordinal)
            || !previousIdentity.Username.Equals(nextIdentity.Username, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The native-editor undo session cannot be rebound to a different editor context.");
        await _access.RequireAsync(previousIdentity.ObjectType, previousIdentity.ObjectId, "Edit", cancellationToken);
        await _access.RequireAsync(nextIdentity.ObjectType, nextIdentity.ObjectId, "Edit", cancellationToken);
        if (previousIdentity.ObjectId.Equals(nextIdentity.ObjectId, StringComparison.Ordinal))
        {
            var existing = GetEntry(previousIdentity);
            ValidateEntry(existing, previousIdentity);
            return new(!string.IsNullOrEmpty(existing.UndoXml), existing.ObjectType, existing.ObjectId, existing.ObjectName);
        }

        var previousEntry = GetEntry(previousIdentity);
        await previousEntry.Gate.WaitAsync(cancellationToken);
        try
        {
            ValidateEntry(previousEntry, previousIdentity);

            // MOVEit creates a separate Advanced Task during Traditional -> Advanced conversion.
            // Never carry the previous Task XML forward as an undo candidate for the new ID;
            // establish the new Task's current configuration as a fresh baseline instead.
            var current = await CaptureWithRetryAsync(nextIdentity.ObjectType, nextIdentity.ObjectId, cancellationToken);
            var nextEntry = new EditorUndoEntry(nextIdentity.ObjectType, current.Id, current.Name, nextIdentity.Username, current.Xml);
            _cache.Set(BuildCacheKey(nextIdentity), nextEntry, new MemoryCacheEntryOptions { SlidingExpiration = EntryLifetime });
            _cache.Remove(BuildCacheKey(previousIdentity));
            _logger.LogInformation(
                "Rebound native-editor Undo Last Change from Task {PreviousTaskId} to Task {TaskId} for launch {LaunchId}.",
                previousIdentity.ObjectId, current.Id, nextIdentity.LaunchId);

            return new(false, nextIdentity.ObjectType, current.Id, current.Name);
        }
        finally
        {
            previousEntry.Gate.Release();
        }
    }

    public async Task<NativeEditorUndoRestoreResult> RestoreAsync(string launchId, string objectType, string objectId, string username, CancellationToken cancellationToken = default)
    {
        var identity = NormalizeIdentity(launchId, objectType, objectId, username);
        await _access.RequireAsync(identity.ObjectType, identity.ObjectId, "Edit", cancellationToken);
        var entry = GetEntry(identity);
        await entry.Gate.WaitAsync(cancellationToken);
        try
        {
            ValidateEntry(entry, identity);
            if (string.IsNullOrWhiteSpace(entry.UndoXml))
                throw new InvalidOperationException("Undo Last Change is no longer available for this editor.");

            var current = await CaptureWithRetryAsync(entry.ObjectType, entry.ObjectId, cancellationToken);
            if (!string.Equals(current.Xml, entry.BaselineXml, StringComparison.Ordinal))
                throw new InvalidOperationException("Undo Last Change cannot continue because this item changed after the last editor update. Reload the editor before making another change.");

            var undoXml = entry.UndoXml;
            var restored = await RestoreConfigurationAsync(entry.ObjectType, entry.ObjectId, undoXml, cancellationToken);
            entry.BaselineXml = undoXml;
            entry.UndoXml = null;
            entry.ObjectName = restored.Name;

            var warning = string.Empty;
            try
            {
                await _metadata.RecordObjectConfigurationModifiedAsync(
                    _workflow.GetEnvironmentKey(), entry.ObjectType, entry.ObjectId, identity.Username, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MOVEit {ObjectType} {ObjectId} was restored by native-editor Undo Last Change, but MAUI could not update its modification audit metadata.", entry.ObjectType, entry.ObjectId);
                warning = "The item was restored in MOVEit, but MAUI could not update its modification audit metadata.";
            }

            return new(entry.ObjectType, restored.Id, restored.Name, warning);
        }
        finally
        {
            entry.Gate.Release();
        }
    }


    public Task DiscardAsync(string launchId, string objectType, string objectId, string username, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var identity = NormalizeIdentity(launchId, objectType, objectId, username);
        var key = BuildCacheKey(identity);
        var entry = _cache.Get<EditorUndoEntry>(key);
        if (entry is not null)
            ValidateEntry(entry, identity);
        _cache.Remove(key);
        return Task.CompletedTask;
    }

    private EditorUndoEntry GetEntry(EditorUndoIdentity identity)
        => _cache.Get<EditorUndoEntry>(BuildCacheKey(identity))
           ?? throw new InvalidOperationException("Undo Last Change is no longer available for this editor. Close and reopen the native editor to start a new editor undo session.");

    private static void ValidateEntry(EditorUndoEntry entry, EditorUndoIdentity identity)
    {
        if (!entry.ObjectType.Equals(identity.ObjectType, StringComparison.Ordinal)
            || !entry.ObjectId.Equals(identity.ObjectId, StringComparison.Ordinal)
            || !entry.Username.Equals(identity.Username, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The native-editor undo session does not match this item.");
    }

    private string BuildCacheKey(EditorUndoIdentity identity)
        => $"NativeEditorUndo|{_workflow.GetEnvironmentKey()}|{identity.Username.ToUpperInvariant()}|{identity.LaunchId}|{identity.ObjectType}|{identity.ObjectId}";

    private static EditorUndoIdentity NormalizeIdentity(string launchId, string objectType, string objectId, string username)
    {
        var launch = (launchId ?? string.Empty).Trim();
        var type = NormalizeObjectType(objectType);
        var id = (objectId ?? string.Empty).Trim();
        var user = (username ?? string.Empty).Trim();
        if (launch.Length == 0) throw new ArgumentException("Native editor launch ID is required.");
        if (id.Length == 0) throw new ArgumentException($"{type} ID is required.");
        if (user.Length == 0) throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        return new(launch, type, id, user);
    }

    private static string NormalizeObjectType(string objectType)
        => (objectType ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "task" => "Task",
            "host" => "Host",
            "script" => "Script",
            "datelist" or "date list" => "DateList",
            _ => throw new ArgumentException("This MOVEit editor type does not support Undo Last Change.")
        };

    private Task<UndoConfigurationSource> CaptureWithRetryAsync(string objectType, string objectId, CancellationToken cancellationToken)
        => NativeEditorSnapshotRetry.CaptureAsync(
            token => CaptureAsync(objectType, objectId, token),
            objectType,
            objectId,
            _logger,
            cancellationToken);

    private async Task<UndoConfigurationSource> CaptureAsync(string objectType, string objectId, CancellationToken cancellationToken)
        => objectType switch
        {
            "Task" => ToSource(await _tasks.GetTaskConfigurationSnapshotAsync(objectId, cancellationToken)),
            "Host" => ToSource(await _hosts.GetHostConfigurationSnapshotAsync(objectId, cancellationToken)),
            "Script" => ToSource(await _scripts.GetScriptConfigurationSnapshotAsync(objectId, cancellationToken)),
            "DateList" => ToSource(await _dateLists.GetConfigurationSnapshotAsync(objectId, cancellationToken)),
            _ => throw new ArgumentException("This MOVEit editor type does not support Undo Last Change.")
        };

    private async Task<UndoConfigurationSource> RestoreConfigurationAsync(string objectType, string objectId, string xml, CancellationToken cancellationToken)
        => objectType switch
        {
            "Task" => ToSource(await _tasks.RestoreTaskConfigurationAsync(objectId, xml, cancellationToken)),
            "Host" => ToSource(await _hosts.RestoreHostConfigurationAsync(objectId, xml, cancellationToken)),
            "Script" => ToSource(await _scripts.RestoreScriptConfigurationAsync(objectId, xml, cancellationToken)),
            "DateList" => ToSource(await _dateLists.RestoreConfigurationAsync(objectId, xml, cancellationToken)),
            _ => throw new ArgumentException("This MOVEit editor type does not support Undo Last Change.")
        };

    private static UndoConfigurationSource ToSource(TaskConfigurationSnapshot value) => new(value.Id, value.Name, value.Xml);
    private static UndoConfigurationSource ToSource(HostConfigurationSnapshot value) => new(value.Id, value.Name, value.Xml);
    private static UndoConfigurationSource ToSource(ScriptConfigurationSnapshot value) => new(value.Id, value.Name, value.Xml);
    private static UndoConfigurationSource ToSource(DateListConfigurationSnapshot value) => new(value.Id, value.Name, value.Xml);

    private sealed class EditorUndoEntry
    {
        public EditorUndoEntry(string objectType, string objectId, string objectName, string username, string baselineXml)
        {
            ObjectType = objectType;
            ObjectId = objectId;
            ObjectName = objectName;
            Username = username;
            BaselineXml = baselineXml;
        }
        public string ObjectType { get; }
        public string ObjectId { get; }
        public string ObjectName { get; set; }
        public string Username { get; }
        public string BaselineXml { get; set; }
        public string? UndoXml { get; set; }
        public SemaphoreSlim Gate { get; } = new(1, 1);
    }

    private sealed record EditorUndoIdentity(string LaunchId, string ObjectType, string ObjectId, string Username);
}

