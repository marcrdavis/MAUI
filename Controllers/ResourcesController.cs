// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Resources.Scripts;

namespace MoveItAutomationUI.Controllers;

[Authorize]
public sealed partial class ResourcesController : Controller
{
    private const string ScriptObjectType = "Script";
    private const string DateListObjectType = "DateList";
    private const string GlobalParameterObjectType = "GlobalParameter";
    private const string KeyCertificateObjectType = "KeyCertificate";
    private const long MaxResourceImportBytes = MauiPortablePackage.MaxPackageBytes;
    private readonly IAutomationScriptService _scriptService;
    private readonly ScriptMetadataSaveService _scriptMetadataSaves;
    private readonly IAutomationDateListService _dateListService;
    private readonly IAutomationGlobalParameterService _globalParameterService;
    private readonly IAutomationKeyCertificateService _keyCertificateService;
    private readonly IMetadataRepository _metadataRepository;
    private readonly IMetadataValueValidationService _metadataValueValidation;
    private readonly IScriptUndoService _scriptUndoService;
    private readonly IRecycleBinService _recycleBinService;
    private readonly IResourcePortabilityService _resourcePortabilityService;
    private readonly IDateListUndoService _dateListUndoService;
    private readonly IMoveItSessionService _sessionService;
    private readonly UiOptions _uiOptions;
    private readonly GridBehaviorOptions _gridBehaviorOptions;
    private readonly MetadataUiOptions _metadataOptions;
    private readonly ScriptBehaviorOptions _scriptBehaviorOptions;
    private readonly DateListBehaviorOptions _dateListBehaviorOptions;
    private readonly GlobalParameterBehaviorOptions _globalParameterBehaviorOptions;
    private readonly KeyCertificateBehaviorOptions _keyCertificateBehaviorOptions;
    private readonly ILogger<ResourcesController> _logger;

    // Purpose: Initializes resources controller with its required dependencies.
    public ResourcesController(
        IAutomationScriptService scriptService,
        IAutomationDateListService dateListService,
        IAutomationGlobalParameterService globalParameterService,
        IAutomationKeyCertificateService keyCertificateService,
        IMetadataRepository metadataRepository,
        IMetadataValueValidationService metadataValueValidation,
        IScriptUndoService scriptUndoService,
        IRecycleBinService recycleBinService,
        IResourcePortabilityService resourcePortabilityService,
        IDateListUndoService dateListUndoService,
        IMoveItSessionService sessionService,
        IOptionsSnapshot<UiOptions> uiOptions,
        IOptionsSnapshot<GridBehaviorOptions> gridBehaviorOptions,
        IOptionsSnapshot<MetadataUiOptions> metadataOptions,
        IOptionsSnapshot<ScriptBehaviorOptions> scriptBehaviorOptions,
        IOptionsSnapshot<DateListBehaviorOptions> dateListBehaviorOptions,
        IOptionsSnapshot<GlobalParameterBehaviorOptions> globalParameterBehaviorOptions,
        IOptionsSnapshot<KeyCertificateBehaviorOptions> keyCertificateBehaviorOptions,
        ILogger<ResourcesController> logger,
        ScriptMetadataSaveService scriptMetadataSaves)
    {
        _scriptService = scriptService;
        _scriptMetadataSaves = scriptMetadataSaves;
        _dateListService = dateListService;
        _globalParameterService = globalParameterService;
        _keyCertificateService = keyCertificateService;
        _metadataRepository = metadataRepository;
        _metadataValueValidation = metadataValueValidation;
        _scriptUndoService = scriptUndoService;
        _recycleBinService = recycleBinService;
        _resourcePortabilityService = resourcePortabilityService;
        _dateListUndoService = dateListUndoService;
        _sessionService = sessionService;
        _uiOptions = uiOptions.Value;
        _gridBehaviorOptions = gridBehaviorOptions.Value;
        _metadataOptions = metadataOptions.Value;
        _scriptBehaviorOptions = scriptBehaviorOptions.Value;
        _dateListBehaviorOptions = dateListBehaviorOptions.Value;
        _globalParameterBehaviorOptions = globalParameterBehaviorOptions.Value;
        _keyCertificateBehaviorOptions = keyCertificateBehaviorOptions.Value;
        _logger = logger;
    }

    // Renders the Scripts workspace shell immediately. The initial Script inventory is loaded asynchronously
    // after the shared grid shell is visible, matching the proven Tasks/Hosts progressive-loading pattern.
    public IActionResult Scripts()
    {
        try
        {
            var session = RequireSession();
            var capabilities = MoveItLicensePolicy.GetCapabilities(session);
            return View("Scripts", new ScriptsPageViewModel
            {
                CustomScriptsLicensed = capabilities.CustomScriptsLicensed,
                UiScriptRefreshIntervalSeconds = Math.Max(0, _scriptBehaviorOptions.UiRefreshIntervalSeconds.Value),
                LastRefreshUtc = null,
                Grid = BuildEmptyScriptGrid()
            });
        }
        catch (MoveItAuthenticationException ex)
        {
            TempData["LoginError"] = ex.Message;
            return RedirectToAction("Login", "Account");
        }
    }

    // Performs the initial asynchronous Script inventory load without forcing a new MOVEit read.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> InitialScriptsGrid(bool preferCached = false, CancellationToken cancellationToken = default)
    {
        try
        {
            var initial = preferCached
                ? await _scriptService.GetInitialScriptSnapshotAsync(cancellationToken)
                : new InitialCacheSnapshot<ScriptCacheSnapshot>(await _scriptService.GetScriptSnapshotAsync(false, cancellationToken), RefreshRecommended: false);
            return await BuildScriptsGridJsonAsync(initial.Snapshot, cancellationToken, initial.RefreshRecommended);
        }
        catch (MoveItApiException ex)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Refreshes Script grid data through the shared cache contract; forceRefresh bypasses cache age.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> RefreshScriptsGrid(CancellationToken cancellationToken = default, bool forceRefresh = false)
    {
        try
        {
            var snapshot = await _scriptService.GetScriptSnapshotAsync(forceRefresh, cancellationToken);
            return await BuildScriptsGridJsonAsync(snapshot, cancellationToken);
        }
        catch (MoveItApiException ex)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Builds the shared Script-grid client payload from one already-resolved inventory snapshot.
    private async Task<JsonResult> BuildScriptsGridJsonAsync(ScriptCacheSnapshot snapshot, CancellationToken cancellationToken, bool refreshRecommended = false)
    {
        var session = RequireSession();
        var environmentKey = session.GetEnvironmentKey();
        var capabilities = MoveItLicensePolicy.GetCapabilities(session);
        var scripts = capabilities.CustomScriptsLicensed
            ? snapshot.Scripts
            : snapshot.Scripts.Where(script => script.IsBuiltIn).ToList();
        var ids = scripts.Select(script => script.Id).ToList();
        await _metadataRepository.EnsureObjectsAsync(environmentKey, ScriptObjectType, ids, cancellationToken);
        var metadataByScript = await _metadataRepository.GetObjectMetadataBatchAsync(environmentKey, ScriptObjectType, ids, cancellationToken);
        var undoScriptIds = await _scriptUndoService.GetAvailableScriptIdsAsync(session.Username, ids, cancellationToken);
        var rows = scripts
            .Select(script => BuildScriptGridRow(script, metadataByScript.TryGetValue(script.Id, out var metadata) ? metadata : null, undoScriptIds.Contains(script.Id)))
            .Select(GridClientContract.FromRow)
            .ToList();

        return Json(new { success = true, refreshedUtc = snapshot.RefreshedUtc, rows, refreshRecommended });
    }

    // Creates the minimal persistent custom Script shell that is completed in MOVEit's native Script editor.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Script", "Create", "")]
    public async Task<IActionResult> CreateScript([FromBody] ScriptCreateRequest? request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequest(new { message = "Script creation request is required." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();
        var licenseBlock = MoveItLicensePolicy.GetCustomScriptBlock(session);
        if (!string.IsNullOrWhiteSpace(licenseBlock)) return StatusCode(StatusCodes.Status403Forbidden, new { message = licenseBlock });

        try
        {
            var created = await _scriptService.CreateScriptAsync(request.Name, request.Language, cancellationToken, request.ResourceGroup);
            try
            {
                await _metadataRepository.RecordObjectCreatedAsync(session.GetEnvironmentKey(), ScriptObjectType, created.Id, session.Username, cancellationToken);
            }
            catch (Exception metadataException)
            {
                _logger.LogError(metadataException, "MOVEit Script {ScriptId} was created, but MAUI could not record its creation audit metadata.", created.Id);
            }

            return Json(new
            {
                success = true,
                script = new { id = created.Id, name = created.Name, language = created.LanguageCode }
            });
        }
        catch (Services.MoveIt.Resources.Scripts.ScriptWriteUncertainException ex)
        {
            _logger.LogWarning("Script creation could not be confirmed; known Script ID {ScriptId}; error type {ErrorType}.", ex.ScriptId, ex.InnerException?.GetType().Name);
            return Conflict(new { message = ex.Message, scriptId = ex.ScriptId, moveItSaved = true });
        }
        catch (ScriptNameAlreadyExistsException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Returns native Script information, MAUI metadata/audit values, and visible Task dependencies.
    // Exports one custom Script configuration in the shared MAUI portable package.
    [HttpGet]
    [MoveItObjectPermission("Script", "Edit", "scriptId")]
    public async Task<IActionResult> ExportScript(string scriptId, CancellationToken cancellationToken = default)
    {
        var licenseBlock = MoveItLicensePolicy.GetCustomScriptBlock(_sessionService.Get());
        if (!string.IsNullOrWhiteSpace(licenseBlock)) return StatusCode(StatusCodes.Status403Forbidden, new { message = licenseBlock });
        try { _ = RequireSession(); var export = await _resourcePortabilityService.ExportScriptAsync(scriptId, cancellationToken); return File(export.Content, "application/zip", export.FileName); }
        catch (Exception ex) when (ex is InvalidOperationException or MoveItApiException or MoveItComException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Exports selected custom Scripts in one same-type MAUI portable package.
    [HttpPost, ValidateAntiForgeryToken]
    [MoveItObjectPermission("Script", "Edit", "ScriptIds")]
    public async Task<IActionResult> ExportScripts([FromBody] BulkScriptRequest? request, CancellationToken cancellationToken = default)
    {
        var licenseBlock = MoveItLicensePolicy.GetCustomScriptBlock(_sessionService.Get());
        if (!string.IsNullOrWhiteSpace(licenseBlock)) return StatusCode(StatusCodes.Status403Forbidden, new { message = licenseBlock });
        if (request?.ScriptIds is null || request.ScriptIds.Count == 0) return BadRequest(new { message = "Select at least one Script to export." });
        try { _ = RequireSession(); var export = await _resourcePortabilityService.ExportScriptsAsync(request.ScriptIds, cancellationToken); return File(export.Content, "application/zip", export.FileName); }
        catch (Exception ex) when (ex is InvalidOperationException or MoveItApiException or MoveItComException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Validates import script and returns the information required before execution.
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(MaxResourceImportBytes + (1024 * 1024))]
    [MoveItObjectPermission("Script", "Import", "")]
    public async Task<IActionResult> PreflightImportScript([FromForm] ResourceImportPreflightRequest? request, CancellationToken cancellationToken = default)
    {
        var licenseBlock = MoveItLicensePolicy.GetCustomScriptBlock(_sessionService.Get());
        if (!string.IsNullOrWhiteSpace(licenseBlock)) return StatusCode(StatusCodes.Status403Forbidden, new { message = licenseBlock });
        var validation = ValidateResourceImportFile(request?.File, "Script"); if (validation is not null) return validation;
        try { _ = RequireSession(); using var memory = new MemoryStream(); await request!.File!.CopyToAsync(memory, cancellationToken); var result = await _resourcePortabilityService.PreflightScriptImportAsync(memory.ToArray(), request.Mode, cancellationToken); return Json(new { success = true, result }); }
        catch (Exception ex) when (ex is InvalidOperationException or MoveItApiException or MoveItComException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Imports script after validating the package and target.
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(MaxResourceImportBytes + (1024 * 1024))]
    [MoveItObjectPermission("Script", "Import", "")]
    public async Task<IActionResult> ImportScript([FromForm] ResourceImportRequest? request, CancellationToken cancellationToken = default)
    {
        var licenseBlock = MoveItLicensePolicy.GetCustomScriptBlock(_sessionService.Get());
        if (!string.IsNullOrWhiteSpace(licenseBlock)) return StatusCode(StatusCodes.Status403Forbidden, new { message = licenseBlock });
        var validation = ValidateResourceImportFile(request?.File, "Script"); if (validation is not null) return validation; var session = _sessionService.Get(); if (session is null) return MauiControllerExceptionMapper.SessionExpired();
        try { using var memory = new MemoryStream(); await request!.File!.CopyToAsync(memory, cancellationToken); var result = await _resourcePortabilityService.ImportScriptAsync(memory.ToArray(), request.Mode, request.AcknowledgeUnsupportedMetadata, session.Username, cancellationToken); return Json(new { success = true, id = result.Id, name = result.Name, created = result.Created, warning = result.Warning, itemCount = result.ItemCount, importedCount = result.ImportedCount, createdCount = result.CreatedCount, overwrittenCount = result.OverwrittenCount, failedCount = result.FailedCount, items = result.Items }); }
        catch (ScriptNameAlreadyExistsException ex) { return Conflict(new { message = ex.Message }); }
        catch (Exception ex) when (ex is InvalidOperationException or MoveItApiException or MoveItComException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Returns Script details, metadata, and dependency information.
    [HttpGet]
    public async Task<IActionResult> ScriptDetails(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { message = "Script ID is required." });
        try
        {
            var session = RequireSession();
            // Reopening after a conflict must refresh the editable fields, not reuse the grid cache.
            await _scriptService.GetScriptSnapshotAsync(true, cancellationToken);
            var scriptTask = _scriptService.GetScriptAsync(id, cancellationToken);
            var dependenciesTask = _scriptService.GetAssignedTasksAsync(id, cancellationToken);
            var metadataTask = _metadataRepository.GetObjectMetadataAsync(session.GetEnvironmentKey(), ScriptObjectType, id.Trim(), cancellationToken);
            await Task.WhenAll(scriptTask, dependenciesTask, metadataTask);
            var script = await scriptTask;
            var metadata = await metadataTask;
            if (!MoveItLicensePolicy.GetCapabilities(session).CustomScriptsLicensed && !script.IsBuiltIn)
            {
                var licenseBlock = MoveItLicensePolicy.GetCustomScriptBlock(session);
                return StatusCode(StatusCodes.Status403Forbidden, new { message = licenseBlock });
            }

            return Json(new
            {
                editVersion = ObjectEditVersion.Create("Script", script.Id, ObjectEditVersion.Named(script.Name, script.Description)),
                script = new
                {
                    id = script.Id,
                    name = script.Name,
                    description = script.Description,
                    language = script.Language,
                    languageCode = script.LanguageCode,
                    type = script.Type,
                    resourceGroups = script.Group,
                    isBuiltIn = script.IsBuiltIn
                },
                metadata = metadata.Values,
                fields = _metadataOptions.Scripts.Fields,
                audit = new
                {
                    createdUtc = metadata.CreatedUtc,
                    createdBy = metadata.CreatedBy,
                    modifiedUtc = metadata.ModifiedUtc,
                    modifiedBy = metadata.ModifiedBy
                },
                tasks = (await dependenciesTask).Select(task => new { id = task.Id, name = task.Name })
            });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Saves editable native Script Name/Description together with MAUI-owned metadata.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Script", "Edit", "id")]
    public async Task<IActionResult> SaveScriptDetails(string id, [FromBody] ScriptDetailsUpdateRequest? request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { message = "Script ID is required." });
        if (request is null) return BadRequest(new { message = "Script metadata request is required." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();

        try
        {
            var script = await _scriptService.GetScriptAsync(id.Trim(), cancellationToken);
            if (script.IsBuiltIn)
            {
                await _scriptMetadataSaves.SaveBuiltInAsync(id, request.Metadata, cancellationToken);
                return Json(new { success = true });
            }
            var licenseBlock = MoveItLicensePolicy.GetCustomScriptBlock(session);
            if (!string.IsNullOrWhiteSpace(licenseBlock)) return StatusCode(StatusCodes.Status403Forbidden, new { message = licenseBlock });
            using var edit = ObjectEditVersion.Require("Script", id.Trim(), request.EditVersion);
            var values = await _metadataValueValidation.FilterAndValidateAsync(session.GetEnvironmentKey(), _metadataOptions.Scripts, request.Metadata, cancellationToken);

            await UpdateScriptBasicDetailsAsync(id.Trim(), request.Name, request.Description, cancellationToken);
            await _metadataRepository.SaveObjectMetadataAsync(session.GetEnvironmentKey(), ScriptObjectType, id.Trim(), values, session.Username, cancellationToken);
            await _metadataRepository.RecordObjectConfigurationModifiedAsync(session.GetEnvironmentKey(), ScriptObjectType, id.Trim(), session.Username, cancellationToken);
            return Json(new { success = true });
        }
        catch (Services.MoveIt.Resources.Scripts.ScriptWriteUncertainException ex)
        { return Conflict(new { message = ex.Message, moveItSaved = true }); }
        catch (ObjectEditConflictException ex)
        { return Conflict(new { message = ex.Message, editConflict = true, moveItSaved = false }); }
        catch (ObjectCheckoutReleaseException ex)
        { return Conflict(new { message = ex.Message, moveItSaved = true }); }
        catch (ScriptNameAlreadyExistsException ex) { return Conflict(new { message = ex.Message }); }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Captures the current complete Script XML before opening the native MOVEit Script editor.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Script", "Edit", "ScriptId")]
    public async Task<IActionResult> PrepareScriptEdit([FromBody] ScriptActionRequest? request, CancellationToken cancellationToken)
    {
        var licenseBlock = MoveItLicensePolicy.GetCustomScriptBlock(_sessionService.Get());
        if (!string.IsNullOrWhiteSpace(licenseBlock)) return StatusCode(StatusCodes.Status403Forbidden, new { message = licenseBlock });
        var scriptId = (request?.ScriptId ?? string.Empty).Trim();
        if (scriptId.Length == 0) return BadRequest(new { message = "Script ID is required." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();

        try
        {
            var inventory = await _scriptService.GetScriptSnapshotAsync(false, cancellationToken);
            var script = inventory.Scripts.FirstOrDefault(item => item.Id.Equals(scriptId, StringComparison.OrdinalIgnoreCase));
            if (script?.IsBuiltIn == true)
                return Json(new { success = true, readOnly = true, scriptId = script.Id, scriptName = script.Name, snapshotCreatedUtc = (DateTimeOffset?)null });

            var snapshot = await NativeEditorSnapshotRetry.CaptureAsync(
                token => _scriptUndoService.CaptureAsync(scriptId, session.Username, token),
                "Script", scriptId, _logger, cancellationToken);
            return Json(new { success = true, scriptId = snapshot.ScriptId, scriptName = snapshot.ScriptName, snapshotCreatedUtc = snapshot.CreatedUtc });
        }
        catch (Exception ex) when (ex is MoveItAuthenticationException || (ex is MoveItComException or InvalidOperationException or ArgumentException))
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to capture an undo snapshot before opening MOVEit Script {ScriptId}.", scriptId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "MAUI could not save the Script undo snapshot. The editor was not opened." });
        }
    }

    // Purpose: Validates undo script and returns the information required before execution.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Script", "Edit", "ScriptId")]
    public async Task<IActionResult> PreflightUndoScript([FromBody] ScriptActionRequest? request, CancellationToken cancellationToken)
    {
        var licenseBlock = MoveItLicensePolicy.GetCustomScriptBlock(_sessionService.Get());
        if (!string.IsNullOrWhiteSpace(licenseBlock)) return StatusCode(StatusCodes.Status403Forbidden, new { message = licenseBlock });
        var scriptId = (request?.ScriptId ?? string.Empty).Trim();
        if (scriptId.Length == 0) return BadRequest(new { message = "Script ID is required." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();

        try
        {
            var script = await _scriptService.GetScriptAsync(scriptId, cancellationToken);
            if (script.IsBuiltIn)
                return Json(new { success = false, scriptId = script.Id, scriptName = script.Name, message = "Undo is not available for built-in MOVEit Scripts." });

            var result = await _scriptUndoService.GetHistoryAsync(scriptId, session.Username, cancellationToken);
            return Json(new
            {
                success = result.CanUndo,
                scriptId = result.ObjectId,
                scriptName = result.ObjectName,
                snapshots = result.Snapshots.Select(item => new { snapshotId = item.SnapshotId, snapshotCreatedUtc = item.CreatedUtc, snapshotCreatedBy = item.CreatedBy, canRestore = item.CanRestore, message = item.Message }),
                message = result.Message
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to validate undo data for MOVEit Script {ScriptId}.", scriptId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "MAUI could not validate the Script undo data." });
        }
    }

    // Purpose: Restores the selected Script undo snapshot.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Script", "Edit", "ScriptId")]
    public async Task<IActionResult> UndoScript([FromBody] ScriptActionRequest? request, CancellationToken cancellationToken)
    {
        var licenseBlock = MoveItLicensePolicy.GetCustomScriptBlock(_sessionService.Get());
        if (!string.IsNullOrWhiteSpace(licenseBlock)) return StatusCode(StatusCodes.Status403Forbidden, new { message = licenseBlock });
        var scriptId = (request?.ScriptId ?? string.Empty).Trim();
        if (scriptId.Length == 0) return BadRequest(new { message = "Script ID is required." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();

        try
        {
            var script = await _scriptService.GetScriptAsync(scriptId, cancellationToken);
            if (script.IsBuiltIn)
                return BadRequest(new { message = "Undo is not available for built-in MOVEit Scripts." });

            var snapshotId = (request?.SnapshotId ?? string.Empty).Trim();
            if (snapshotId.Length == 0) return BadRequest(new { message = "Undo snapshot is required." });
            var result = await _scriptUndoService.RestoreAsync(scriptId, session.Username, snapshotId, cancellationToken);
            return Json(new { success = true, scriptId = result.ScriptId, scriptName = result.ScriptName, snapshotCreatedUtc = result.SnapshotCreatedUtc, warning = result.Warning });
        }
        catch (Exception ex) when (ex is MoveItAuthenticationException || (ex is MoveItComException or InvalidOperationException or ArgumentException))
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to restore the undo snapshot for MOVEit Script {ScriptId}.", scriptId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "MAUI could not restore the Script undo snapshot." });
        }
    }

    // Records that MAUI opened the native Script editor; used by the shared Undo concurrency guardrail.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Script", "Edit", "ScriptId")]
    public async Task<IActionResult> MarkScriptEdited([FromBody] ScriptActionRequest? request, CancellationToken cancellationToken)
    {
        var licenseBlock = MoveItLicensePolicy.GetCustomScriptBlock(_sessionService.Get());
        if (!string.IsNullOrWhiteSpace(licenseBlock)) return StatusCode(StatusCodes.Status403Forbidden, new { message = licenseBlock });
        var scriptId = (request?.ScriptId ?? string.Empty).Trim();
        if (scriptId.Length == 0) return BadRequest(new { message = "Script ID is required." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();

        try
        {
            var script = await _scriptService.GetScriptAsync(scriptId, cancellationToken);
            if (script.IsBuiltIn) return Json(new { success = true, readOnly = true });

            await _metadataRepository.RecordObjectConfigurationModifiedAsync(session.GetEnvironmentKey(), ScriptObjectType, scriptId, session.Username, cancellationToken);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to update MAUI modification audit metadata for Script {ScriptId}.", scriptId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "MAUI could not update the Script modification audit information." });
        }
    }

    // Performs one live permission/dependency check for the complete selected Script set before
    // showing the destructive bulk confirmation. Built-in Scripts are always returned as blockers.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Script", "Delete", "ScriptIds")]
    public async Task<IActionResult> PreflightDeleteScripts([FromBody] BulkScriptRequest? request, CancellationToken cancellationToken)
    {
        var licenseBlock = MoveItLicensePolicy.GetCustomScriptBlock(_sessionService.Get());
        if (!string.IsNullOrWhiteSpace(licenseBlock)) return StatusCode(StatusCodes.Status403Forbidden, new { message = licenseBlock });
        var scriptIds = request?.ScriptIds;
        if (scriptIds is null || scriptIds.Count == 0) return BadRequest(new { message = "Select at least one Script." });
        if (_sessionService.Get() is null) return MauiControllerExceptionMapper.SessionExpired();

        try
        {
            var results = await _scriptService.PreflightDeleteScriptsAsync(scriptIds, cancellationToken);
            var blockers = results.Where(item => !item.CanDelete).ToList();
            return Json(new
            {
                success = blockers.Count == 0,
                count = results.Count,
                message = blockers.Count == 0
                    ? string.Empty
                    : $"{blockers.Count} selected Script{(blockers.Count == 1 ? string.Empty : "s")} cannot be deleted.",
                targets = results.Select(item => new { scriptId = item.ScriptId, scriptName = item.ScriptName }),
                blockers = blockers.Select(item => new
                {
                    scriptId = item.ScriptId,
                    scriptName = item.ScriptName,
                    message = item.Message,
                    taskReferences = item.TaskReferences.Select(task => new { id = task.Id, name = task.Name })
                })
            });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Revalidates the complete selected Script set and deletes only items that are still safe.
    // MAUI captures Recycle Bin snapshots before submitting the destructive COM operations.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Script", "Delete", "ScriptIds")]
    public async Task<IActionResult> DeleteScripts([FromBody] BulkScriptRequest? request, CancellationToken cancellationToken)
    {
        var licenseBlock = MoveItLicensePolicy.GetCustomScriptBlock(_sessionService.Get());
        if (!string.IsNullOrWhiteSpace(licenseBlock)) return StatusCode(StatusCodes.Status403Forbidden, new { message = licenseBlock });
        var scriptIds = request?.ScriptIds;
        if (scriptIds is null || scriptIds.Count == 0) return BadRequest(new { message = "Select at least one Script." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();
        try
        {
            var recycleCapture = await _recycleBinService.CaptureForDeletionAsync(ScriptObjectType, scriptIds, session.Username, request?.DeleteWithoutRecycle == true, cancellationToken);
            if (recycleCapture.NeedsConfirmation && request?.DeleteWithoutRecycle != true)
            {
                return StatusCode(StatusCodes.Status409Conflict, new { code = "RecycleCaptureFailed", message = "MAUI could not create a recovery copy for one or more selected Scripts. Delete anyway? Items deleted without a recovery copy cannot be restored from the Recycle Bin.", failures = recycleCapture.Failures.Select(item => new { id = item.ObjectId, message = item.Message }) });
            }

            var result = await _scriptService.DeleteScriptsAsync(scriptIds, cancellationToken);
            var deletedIds = result.Items.Where(item => item.Success).Select(item => item.Id).ToList();
            var deletedIdSet = deletedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unusedRecycleIds = recycleCapture.RecycleIdsByObjectId.Where(pair => !deletedIdSet.Contains(pair.Key)).Select(pair => pair.Value).ToList();
            if (unusedRecycleIds.Count > 0)
            {
                try { await _recycleBinService.DiscardCapturedAsync(unusedRecycleIds, cancellationToken); }
                catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogWarning(ex, "MAUI could not remove unused Recycle Bin snapshots after one or more Script deletes failed."); }
            }

            var cleanupWarnings = new List<string>();
            if (deletedIds.Count > 0)
            {
                try { await _metadataRepository.DeleteObjectsAsync(session.GetEnvironmentKey(), ScriptObjectType, deletedIds, cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { _logger.LogError(ex, "MOVEit Script deletion succeeded, but MAUI metadata cleanup failed for Script IDs {ScriptIds}.", string.Join(",", deletedIds)); cleanupWarnings.Add("MAUI could not remove all local Script metadata."); }
                try { await _scriptUndoService.DeleteSnapshotsAsync(deletedIds, cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { _logger.LogError(ex, "MOVEit Script deletion succeeded, but MAUI undo cleanup failed for Script IDs {ScriptIds}.", string.Join(",", deletedIds)); cleanupWarnings.Add("MAUI could not remove all local Script undo snapshots."); }
            }

            var warning = cleanupWarnings.Count == 0 ? string.Empty : $"The MOVEit Script deletion succeeded for one or more Scripts, but {string.Join(" ", cleanupWarnings)} Review the MAUI application log if stale local records remain.";
            return Json(new { success = result.Failed == 0 && result.Skipped == 0, succeeded = result.Succeeded, skipped = result.Skipped, failed = result.Failed, warning, items = result.Items.Select(item => new { id = item.Id, name = item.Name, success = item.Success, skipped = item.Skipped, message = item.Message }) });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Performs a live permission/dependency check before showing the destructive Script confirmation.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Script", "Delete", "ScriptId")]
    public async Task<IActionResult> PreflightDeleteScript([FromBody] ScriptActionRequest? request, CancellationToken cancellationToken)
    {
        var scriptId = (request?.ScriptId ?? string.Empty).Trim();
        if (scriptId.Length == 0) return BadRequest(new { message = "Script ID is required." });
        if (_sessionService.Get() is null) return MauiControllerExceptionMapper.SessionExpired();

        try
        {
            var result = await _scriptService.PreflightDeleteScriptAsync(scriptId, cancellationToken);
            return Json(new
            {
                success = result.CanDelete,
                scriptId = result.ScriptId,
                scriptName = result.ScriptName,
                message = result.Message,
                taskReferences = result.TaskReferences.Select(item => new { id = item.Id, name = item.Name })
            });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Revalidates Script guardrails and permanently deletes the custom Script through COM.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Script", "Delete", "ScriptId")]
    public async Task<IActionResult> DeleteScript([FromBody] ScriptActionRequest? request, CancellationToken cancellationToken)
    {
        var scriptId = (request?.ScriptId ?? string.Empty).Trim();
        if (scriptId.Length == 0) return BadRequest(new { message = "Script ID is required." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();
        try
        {
            var recycleCapture = await _recycleBinService.CaptureForDeletionAsync(ScriptObjectType, new[] { scriptId }, session.Username, request?.DeleteWithoutRecycle == true, cancellationToken);
            if (recycleCapture.NeedsConfirmation && request?.DeleteWithoutRecycle != true)
                return StatusCode(StatusCodes.Status409Conflict, new { code = "RecycleCaptureFailed", message = "MAUI could not create a recovery copy for this Script. Delete anyway? A Script deleted without a recovery copy cannot be restored from the Recycle Bin.", failures = recycleCapture.Failures.Select(item => new { id = item.ObjectId, message = item.Message }) });

            ScriptDeleteResult deleted;
            try { deleted = await _scriptService.DeleteScriptAsync(scriptId, cancellationToken); }
            catch
            {
                if (recycleCapture.RecycleIdsByObjectId.TryGetValue(scriptId, out var unusedId))
                {
                    try
                    {
                        await _recycleBinService.DiscardCapturedAsync(new[] { unusedId }, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception cleanupEx)
                    {
                        _logger.LogWarning(
                            cleanupEx,
                            "Unable to discard unused Recycle Bin capture {RecycleItemId} after Script {ScriptId} deletion failed.",
                            unusedId,
                            scriptId);
                    }
                }
                throw;
            }

            var cleanupWarnings = new List<string>();
            try { await _metadataRepository.DeleteObjectsAsync(session.GetEnvironmentKey(), ScriptObjectType, new[] { deleted.Id }, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) { _logger.LogError(ex, "MOVEit Script {ScriptId} was deleted, but MAUI metadata cleanup failed.", deleted.Id); cleanupWarnings.Add("MAUI could not remove all local Script metadata."); }
            try { await _scriptUndoService.DeleteSnapshotsAsync(new[] { deleted.Id }, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) { _logger.LogError(ex, "MOVEit Script {ScriptId} was deleted, but MAUI undo cleanup failed.", deleted.Id); cleanupWarnings.Add("MAUI could not remove all local Script undo snapshots."); }

            var warning = cleanupWarnings.Count == 0 ? string.Empty : $"The MOVEit Script deletion succeeded, but {string.Join(" ", cleanupWarnings)} Review the MAUI application log if stale local records remain.";
            return Json(new { success = true, scriptId = deleted.Id, scriptName = deleted.Name, warning });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
        catch (Exception ex) { _logger.LogError(ex, "Unable to delete MOVEit Script {ScriptId}.", scriptId); return StatusCode(StatusCodes.Status500InternalServerError, new { message = "MAUI could not delete the Script." }); }
    }



    // Purpose: Builds script grid.
    private GridViewModel BuildScriptGrid(GridOptions options, IReadOnlyList<GridRow> rows) => new()
    {
        GridId = "scripts",
        EntityName = "Scripts",
        SearchPlaceholder = "Search script names, descriptions, types, languages, and metadata",
        Columns = BuildScriptColumns(),
        Rows = rows,
        Options = options,
        SearchColumns = options.SearchColumns,
        FitColumnsToGrid = _gridBehaviorOptions.FitColumnsToGrid.Value,
        SelectRowsBeyondGridSize = _gridBehaviorOptions.SelectRowsBeyondGridSize.Value,
        DefaultPageSize = _gridBehaviorOptions.DefaultRowSize.Value,
        PageSizeOptions = _gridBehaviorOptions.DefaultRowSize.PossibleValues,
        ShowBulkActions = options.BulkActions.Count > 0
    };

    // Purpose: Builds script grid options.
    private GridOptions BuildScriptGridOptions(GridOptions source)
    {
        var metadataKeys = MetadataGridSupport.GetFieldColumnKeys(_metadataOptions.Scripts).ToList();
        var defaultNativeColumns = new[] { "Name", "Description", "Type", "Language" };
        var availableNativeColumns = new[] { "Id", "Name", "Description", "Type", "Language", "Group" };
        var defaultSearchColumns = new[] { "Name", "Description", "Type", "Language", "Group" };
        IEnumerable<string> availableColumns = source.AvailableColumns.Count > 0 ? source.AvailableColumns : availableNativeColumns;
        var customScriptsLicensed = MoveItLicensePolicy.GetCapabilities(_sessionService.Get()).CustomScriptsLicensed;
        IEnumerable<string> addActions = source.AddActions.Count > 0 ? source.AddActions : new[] { "New Script", "Import Scripts" };
        if (!customScriptsLicensed) addActions = Array.Empty<string>();
        IEnumerable<string> searchColumns = source.SearchColumns.Count > 0 ? source.SearchColumns : defaultSearchColumns;

        return new GridOptions
        {
            // Keep the Scripts workspace usable when optional grid definitions are absent.
            // The packaged configuration supplies the normal defaults.
            DefaultColumns = source.DefaultColumns.Count > 0 ? source.DefaultColumns.ToList() : defaultNativeColumns.ToList(),
            AvailableColumns = availableColumns
                .Concat(new[] { "Group" })
                .Concat(MetadataGridSupport.GetAvailableColumnKeys(_metadataOptions.Scripts))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            BulkActions = customScriptsLicensed ? source.BulkActions.ToList() : new List<string>(),
            AddActions = addActions
                .Where(action => !string.IsNullOrWhiteSpace(action))
                .Select(action => action.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            SearchColumns = searchColumns
                .Concat(new[] { "Group" })
                .Concat(metadataKeys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    // Purpose: Builds script columns.
    private IReadOnlyList<GridColumn> BuildScriptColumns()
    {
        var columns = new List<GridColumn>
        {
            new() { Key = "Id", Label = "Script ID" },
            new() { Key = "Name", Label = "Script Name", DefaultWidth = 350 },
            new() { Key = "Description", Label = "Description", DefaultWidth = 300 },
            new() { Key = "Type", Label = "Type", DefaultWidth = 150 },
            new() { Key = "Language", Label = "Language" },
            new() { Key = "Group", Label = "Resource Group" }
        };

        MetadataGridSupport.AddColumns(columns, _metadataOptions.Scripts);
        return columns;
    }

    // Purpose: Builds script grid row.
    private GridRow BuildScriptGridRow(AutomationScript script, ObjectMetadataSnapshot? metadata, bool hasUndo)
    {
        var values = new Dictionary<string, string>
        {
            ["Id"] = script.Id,
            ["Name"] = script.Name,
            ["Description"] = script.Description,
            ["Type"] = script.Type,
            ["Language"] = script.Language,
            ["Group"] = script.Group
        };
        var raw = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase)
        {
            ["LanguageCode"] = script.LanguageCode,
            ["CanEditDelete"] = script.CanEditDelete && !script.IsBuiltIn ? "1" : "0",
            ["IsBuiltIn"] = script.IsBuiltIn ? "1" : "0",
            ["HasUndo"] = hasUndo && !script.IsBuiltIn ? "1" : "0"
        };

        MetadataGridSupport.AddValues(values, raw, _metadataOptions.Scripts, metadata, FormatDate);

        return new GridRow
        {
            Id = script.Id,
            InfoActionUrl = Url.Action(nameof(ScriptDetails), new { id = script.Id }) ?? string.Empty,
            Values = values,
            RawValues = raw
        };
    }


    // Purpose: Formats date.
    private static string FormatDate(DateTimeOffset? value)
        => value.HasValue ? value.Value.ToLocalTime().ToString("MMM d, yyyy  h:mm tt", CultureInfo.CurrentCulture) : string.Empty;

    // Purpose: Builds empty script grid.
    private GridViewModel BuildEmptyScriptGrid()
    {
        var options = BuildScriptGridOptions(_uiOptions.Grids.TryGetValue("scripts", out var gridOptions) ? gridOptions : new GridOptions());
        return BuildScriptGrid(options, Array.Empty<GridRow>());
    }

    // Information and source edits share the authoritative Script CRUD save lifecycle.
    private Task UpdateScriptBasicDetailsAsync(string id, string name, string description, CancellationToken cancellationToken)
        => _scriptService.UpdateBasicDetailsAsync(id, name, description, cancellationToken);

    // Purpose: Validates resource import file.
    private IActionResult? ValidateResourceImportFile(IFormFile? file, string label)
    {
        if (file is null || file.Length <= 0) return BadRequest(new { message = $"Select a MAUI {label} export file." });
        if (file.Length > MaxResourceImportBytes) return BadRequest(new { message = $"The MAUI {label} export exceeds the 50 MB import limit." });
        if (!string.Equals(Path.GetExtension(file.FileName), ".maui", StringComparison.OrdinalIgnoreCase)) return BadRequest(new { message = $"{label} imports must use a .maui file." });
        return null;
    }

    // Purpose: Returns the required session.
    private MoveItSessionState RequireSession()
        => _sessionService.Get() ?? throw new MoveItAuthenticationException(MauiControllerExceptionMapper.SessionExpiredMessage);
}
