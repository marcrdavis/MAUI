// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Controllers;

// RC2 controller boundary for MAUI-owned Traditional Task editing.
// Traditional Task persistence uses a fresh-definition GET/edit/PUT path; host write/browse support is governed by the editor capability catalog.
[Authorize]
public sealed partial class TraditionalTaskEditorController : Controller
{
    private const string ObjectType = "Task";
    private readonly ITraditionalTaskEditorService _editorService;
    private readonly ITraditionalTaskEditorSaveService _saveService;
    private readonly IAutomationTaskService _taskService;
    private readonly ITraditionalTaskEditorFolderBrowseService _folderBrowseService;
    private readonly ITraditionalTaskEditorTransferExceptionService _transferExceptionService;
    private readonly ITraditionalTaskEditorSourceTimestampService _sourceTimestampService;
    private readonly IMetadataRepository _metadataRepository;
    private readonly IMetadataValueValidationService _metadataValueValidation;
    private readonly ITaskUndoService _taskUndoService;
    private readonly IMoveItSessionService _sessionService;
    private readonly ITaskNotificationTemplateService _notificationTemplateService;
    private readonly IUserPreferencesService _userPreferences;
    private readonly MetadataUiOptions _metadataOptions;
    private readonly ILogger<TraditionalTaskEditorController> _logger;

    public TraditionalTaskEditorController(
        ITraditionalTaskEditorService editorService,
        ITraditionalTaskEditorSaveService saveService,
        IAutomationTaskService taskService,
        ITraditionalTaskEditorFolderBrowseService folderBrowseService,
        ITraditionalTaskEditorTransferExceptionService transferExceptionService,
        ITraditionalTaskEditorSourceTimestampService sourceTimestampService,
        IMetadataRepository metadataRepository,
        IMetadataValueValidationService metadataValueValidation,
        ITaskUndoService taskUndoService,
        IMoveItSessionService sessionService,
        ITaskNotificationTemplateService notificationTemplateService,
        IUserPreferencesService userPreferences,
        IOptionsSnapshot<MetadataUiOptions> metadataOptions,
        ILogger<TraditionalTaskEditorController> logger)
    {
        _editorService = editorService;
        _saveService = saveService;
        _taskService = taskService;
        _folderBrowseService = folderBrowseService;
        _transferExceptionService = transferExceptionService;
        _sourceTimestampService = sourceTimestampService;
        _metadataRepository = metadataRepository;
        _metadataValueValidation = metadataValueValidation;
        _taskUndoService = taskUndoService;
        _sessionService = sessionService;
        _notificationTemplateService = notificationTemplateService;
        _userPreferences = userPreferences;
        _metadataOptions = metadataOptions.Value;
        _logger = logger;
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [MoveItObjectPermission("Task", "Read", "id")]
    public async Task<IActionResult> Shell(string id, CancellationToken cancellationToken)
    {
        var normalizedTaskId = (id ?? string.Empty).Trim();
        if (normalizedTaskId.Length == 0) return BadRequest(new { message = "Task ID is required." });

        var session = _sessionService.Get();
        if (session is null) return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });

        try
        {
            var environmentKey = session.GetEnvironmentKey();
            var definitionTask = _editorService.GetDefinitionAsync(normalizedTaskId, cancellationToken);
            var metadataTask = _metadataRepository.GetObjectMetadataAsync(environmentKey, ObjectType, normalizedTaskId, cancellationToken);
            var foldersTask = _metadataRepository.GetFoldersAsync(environmentKey, ObjectType, cancellationToken);
            var templatesTask = _notificationTemplateService.GetCurrentAsync(cancellationToken);
            var preferencesTask = _userPreferences.GetCurrentTraditionalTaskEditorPreferencesAsync(cancellationToken);
            await Task.WhenAll(definitionTask, metadataTask, foldersTask, templatesTask, preferencesTask);

            var metadata = await metadataTask;
            var definition = await definitionTask;
            return Json(new TraditionalTaskEditorShellViewModel
            {
                CheckoutObjectsForDurationOfEdit = definition.Task.CanAlter && HttpContext.RequestServices.GetRequiredService<IOptionsSnapshot<UiOptions>>().Value.CheckoutObjectsForDurationOfEdit.Value,
                EditVersion = definition.EditVersion,
                Task = definition.Task,
                HostCapabilities = definition.HostCapabilities,
                SourceHosts = definition.SourceHosts,
                DestinationHosts = definition.DestinationHosts,
                ScriptOptions = definition.ScriptOptions,
                SmtpHosts = definition.SmtpHosts,
                TaskOptions = definition.TaskOptions,
                DateListOptions = definition.DateListOptions,
                TlsCertificates = definition.TlsCertificates,
                SshClientKeys = definition.SshClientKeys,
                NotificationTemplates = (await templatesTask).Select(template => new TraditionalTaskEditorNotificationTemplate(
                    template.Id, template.Name, template.Subject, template.Message, template.Format)).ToList(),
                Metadata = metadata.Values,
                FolderId = metadata.FolderId,
                Fields = _metadataOptions.Tasks.Fields,
                Folders = FlattenFolders(await foldersTask),
                Audit = new TraditionalTaskEditorAudit(metadata.CreatedUtc, metadata.CreatedBy, metadata.ModifiedUtc, metadata.ModifiedBy),
                Preferences = await preferencesTask
            });
        }
        catch (MoveItAuthenticationException)
        {
            return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning(ex, "Traditional Task editor shell could not load Task {TaskId}.", normalizedTaskId);
            return BadRequest(new { message = ex.Message });
        }
    }


    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ScriptMetadata([FromServices] MoveItAutomationUI.Services.MoveIt.Resources.Scripts.IScriptCrudCoordinator scripts,
        string id, CancellationToken cancellationToken)
    {
        if (_sessionService.Get() is null) return Unauthorized(new { message = "MOVEit session expired. Your Task draft has been retained." });
        try { return Json(new { success = true, script = await scripts.ReadBuiltInMetadataAsync(id, cancellationToken) }); }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException)
        { return MauiControllerExceptionMapper.Map(ex); }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [MoveItObjectPermission("Task", "Edit", "id")]
    public async Task<IActionResult> Save(string id, [FromBody] JsonObject? request, CancellationToken cancellationToken)
    {
        var normalizedTaskId = (id ?? string.Empty).Trim();
        if (normalizedTaskId.Length == 0) return BadRequest(new { message = "Task ID is required." });
        if (request is null) return BadRequest(new { message = "Task editor save payload is required." });
        var session = _sessionService.Get();
        if (session is null) return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });

        try
        {
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var edit = ObjectEditVersion.Require("Task", normalizedTaskId, request["editVersion"]?.ToString());
            if (request["metadata"] is JsonObject metadataObject)
            {
                foreach (var pair in metadataObject)
                {
                    if (pair.Value is JsonValue value && value.TryGetValue<string>(out var text)) metadata[pair.Key] = text ?? string.Empty;
                    else metadata[pair.Key] = string.Empty;
                }
            }
            var validated = await _metadataValueValidation.FilterAndValidateAsync(
                session.GetEnvironmentKey(), _metadataOptions.Tasks, metadata, cancellationToken);

            var captureUndo = true;
            if (request["captureUndo"] is JsonValue captureUndoValue)
            {
                if (captureUndoValue.TryGetValue<bool>(out var captureUndoBoolean)) captureUndo = captureUndoBoolean;
                else if (captureUndoValue.TryGetValue<int>(out var captureUndoNumber)) captureUndo = captureUndoNumber != 0;
                else if (captureUndoValue.TryGetValue<string>(out var captureUndoText) && bool.TryParse(captureUndoText, out captureUndoBoolean)) captureUndo = captureUndoBoolean;
            }

            int? folderId = null;
            if (request["folderId"] is JsonValue folderValue)
            {
                if (folderValue.TryGetValue<int>(out var numericFolder)) folderId = numericFolder;
                else if (folderValue.TryGetValue<string>(out var folderText) && int.TryParse(folderText, out numericFolder)) folderId = numericFolder;
            }

            if (captureUndo)
                await _taskUndoService.CaptureAsync(normalizedTaskId, session.Username, cancellationToken);

            // MOVEit persistence and verification are one boundary. Once this returns, the Task itself
            // is saved. Keep subsequent MAUI metadata/folder/audit/cache work separate so a local
            // failure cannot be misreported as though MOVEit rejected the Task update.
            await _saveService.SaveAsync(normalizedTaskId, request, cancellationToken);

            try
            {
                await _metadataRepository.SaveObjectMetadataAsync(
                    session.GetEnvironmentKey(), ObjectType, normalizedTaskId, validated, session.Username, cancellationToken);
                // Task edit rights do not grant administration of the MAUI hierarchy.
                if (session.Info?.IsAdmin == true)
                    await _metadataRepository.AssignObjectToFolderAsync(
                        session.GetEnvironmentKey(), ObjectType, normalizedTaskId, folderId, session.Username, cancellationToken);
                await _metadataRepository.RecordObjectConfigurationModifiedAsync(
                    session.GetEnvironmentKey(), ObjectType, normalizedTaskId, session.Username, cancellationToken);
                await _taskService.GetTaskSnapshotAsync(true, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "Traditional Task {TaskId} was saved to MOVEit but MAUI could not finish metadata/folder/audit/cache updates.",
                    normalizedTaskId);
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    message = "The Task was saved and verified in MOVEit, but MAUI could not finish its metadata/folder/audit update. Reload the Task to confirm its saved state before applying any remaining MAUI metadata or folder changes.",
                    moveItSaved = true,
                    taskId = normalizedTaskId
                });
            }

            return Json(new { success = true, taskId = normalizedTaskId, editVersion = ObjectEditVersion.SavedVersion });
        }
        catch (ObjectCheckoutRejectedException ex) { return Conflict(ex.Feedback()); }
        catch (MoveItAuthenticationException)
        {
            return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        }
        catch (ObjectEditLeaseException ex)
        {
            return Conflict(new { message = ex.Message, leaseLost = true, editConflict = true, moveItSaved = ex.MayHaveSaved });
        }
        catch (Exception ex) when (ex is ObjectEditConflictException or ObjectCheckoutReleaseException)
        {
            return Conflict(new { message = ex.Message, moveItSaved = false, editConflict = true });
        }
        catch (TraditionalTaskEditorPartialSaveException ex)
        {
            _logger.LogError(ex,
                "Task {TaskId} has an unverified or partial MOVEit save outcome.",
                normalizedTaskId);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = ex.Message,
                moveItSaved = true,
                taskId = normalizedTaskId
            });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning(ex, "Traditional Task editor could not save Task {TaskId}.", normalizedTaskId);
            return BadRequest(new { message = ex.Message });
        }
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> SavePreferences([FromBody] TraditionalTaskEditorPreferences? request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequest(new { message = "Task editor preferences payload is required." });
        if (_sessionService.Get() is null) return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        try
        {
            var saved = await _userPreferences.SaveCurrentTraditionalTaskEditorPreferencesAsync(request, cancellationToken);
            return Json(new { success = true, autoSave = saved.AutoSave });
        }
        catch (InvalidOperationException ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Traditional Task editor preferences could not be saved.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "The Traditional Task editor preference could not be saved." });
        }
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> BrowseFolders(
        string taskId,
        string hostId,
        string kind,
        string? path,
        string? bucket,
        string? container,
        string? documentLibrary,
        string? documentLibraryGuid,
        bool browseBuckets,
        bool browseContainers,
        bool browseDocumentLibraries,
        string? stepId,
        CancellationToken cancellationToken)
    {
        if (_sessionService.Get() is null) return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });

        try
        {
            var result = await _folderBrowseService.BrowseAsync(
                taskId,
                hostId,
                kind,
                path ?? string.Empty,
                bucket ?? string.Empty,
                container ?? string.Empty,
                documentLibrary ?? string.Empty,
                documentLibraryGuid ?? string.Empty,
                browseBuckets,
                browseContainers,
                browseDocumentLibraries,
                stepId ?? string.Empty,
                cancellationToken);
            return Json(result);
        }
        catch (MoveItAuthenticationException)
        {
            return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning(ex, "Traditional Task editor could not browse Task {TaskId} {Kind} Host {HostId} bucket {Bucket} container {Container} library {DocumentLibrary} path {Path}.", taskId, kind, hostId, bucket, container, documentLibrary, path);
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> TransferExceptions(string id, CancellationToken cancellationToken)
    {
        var normalizedTaskId = (id ?? string.Empty).Trim();
        if (normalizedTaskId.Length == 0) return BadRequest(new { message = "Task ID is required." });
        if (_sessionService.Get() is null) return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });

        try
        {
            var entries = await _transferExceptionService.GetAsync(normalizedTaskId, cancellationToken);
            return Json(new { success = true, entries });
        }
        catch (MoveItAuthenticationException)
        {
            return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning(ex, "Traditional Task editor could not retrieve transfer exceptions for Task {TaskId}.", normalizedTaskId);
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [MoveItObjectPermission("Task", "Edit", "id")]
    public async Task<IActionResult> DeleteTransferExceptions(
        string id,
        [FromBody] IReadOnlyList<string>? exceptionIds,
        CancellationToken cancellationToken)
    {
        var normalizedTaskId = (id ?? string.Empty).Trim();
        if (normalizedTaskId.Length == 0) return BadRequest(new { message = "Task ID is required." });
        if (exceptionIds is null) return BadRequest(new { message = "Transfer exception IDs are required." });
        if (_sessionService.Get() is null) return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });

        try
        {
            await _transferExceptionService.DeleteAsync(normalizedTaskId, exceptionIds, cancellationToken);
            return Json(new { success = true });
        }
        catch (MoveItAuthenticationException)
        {
            return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning(ex, "Traditional Task editor could not remove transfer exceptions for Task {TaskId}.", normalizedTaskId);
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> SourceTimestamps(string id, string hostId, CancellationToken cancellationToken)
    {
        var normalizedTaskId = (id ?? string.Empty).Trim();
        var normalizedHostId = (hostId ?? string.Empty).Trim();
        if (normalizedTaskId.Length == 0) return BadRequest(new { message = "Task ID is required." });
        if (normalizedHostId.Length == 0) return BadRequest(new { message = "Host ID is required." });
        if (_sessionService.Get() is null) return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });

        try
        {
            var entries = await _sourceTimestampService.GetAsync(normalizedTaskId, normalizedHostId, cancellationToken);
            return Json(new { success = true, entries });
        }
        catch (MoveItAuthenticationException)
        {
            return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning(ex, "Traditional Task editor could not retrieve Source Timestamps for Task {TaskId}, Host {HostId}.", normalizedTaskId, normalizedHostId);
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [MoveItObjectPermission("Task", "Edit", "id")]
    public async Task<IActionResult> UpdateSourceTimestamp(
        string id,
        [FromBody] TraditionalTaskEditorSourceTimestampUpdateRequest? request,
        CancellationToken cancellationToken)
    {
        var normalizedTaskId = (id ?? string.Empty).Trim();
        if (normalizedTaskId.Length == 0) return BadRequest(new { message = "Task ID is required." });
        if (request is null) return BadRequest(new { message = "Source Timestamp payload is required." });
        if (_sessionService.Get() is null) return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });

        try
        {
            var saved = await _sourceTimestampService.UpdateAsync(normalizedTaskId, request, cancellationToken);
            return Json(new { success = true, entry = saved });
        }
        catch (MoveItAuthenticationException)
        {
            return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning(ex, "Traditional Task editor could not update a Source Timestamp for Task {TaskId}.", normalizedTaskId);
            return BadRequest(new { message = ex.Message });
        }
    }

    private static IReadOnlyList<TaskFolderOption> FlattenFolders(IReadOnlyList<MetadataFolderRecord> folders)
    {
        var roots = folders.Where(folder => !folder.ParentId.HasValue)
            .OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var byParent = folders.Where(folder => folder.ParentId.HasValue)
            .GroupBy(folder => folder.ParentId!.Value)
            .ToDictionary(group => group.Key, group => group.OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase).ToList());
        var result = new List<TaskFolderOption>();
        var visited = new HashSet<int>();

        void Append(MetadataFolderRecord folder, int depth, string parentPath)
        {
            if (!visited.Add(folder.Id)) return;
            var path = string.IsNullOrWhiteSpace(parentPath) ? folder.Name : $"{parentPath} / {folder.Name}";
            result.Add(new TaskFolderOption { Id = folder.Id, ParentId = folder.ParentId, Name = folder.Name, DisplayName = path, Depth = depth });
            if (!byParent.TryGetValue(folder.Id, out var children)) return;
            foreach (var child in children) Append(child, depth + 1, path);
        }

        foreach (var root in roots) Append(root, 0, string.Empty);
        return result;
    }
}

