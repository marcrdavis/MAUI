// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Controllers;

[Authorize]
public sealed partial class HostsController : Controller
{
    private const string ObjectType = "Host";
    private const string TaskObjectType = "Task";
    private const long MaxHostImportBytes = MauiNativePortablePackage.MaxPackageBytes;
    private readonly IAutomationHostService _hostService;
    private readonly IAutomationTaskService _taskService;
    private readonly IMauiNativePortabilityService _nativePortabilityService;
    private readonly IHostConnectionTestService _hostConnectionTestService;
    private readonly IHostHealthService _hostHealthService;
    private readonly IMetadataRepository _metadataRepository;
    private readonly IMetadataValueValidationService _metadataValueValidation;
    private readonly IHostUndoService _hostUndoService;
    private readonly IRecycleBinService _recycleBinService;
    private readonly IMoveItSessionService _sessionService;
    private readonly UiOptions _uiOptions;
    private readonly GridBehaviorOptions _gridBehaviorOptions;
    private readonly MetadataUiOptions _metadataOptions;
    private readonly HostBehaviorOptions _hostBehaviorOptions;
    private readonly ILogger<HostsController> _logger;

    // Initializes the Hosts controller with live MOVEit Host access, metadata, and grid configuration.
    public HostsController(
        IAutomationHostService hostService,
        IAutomationTaskService taskService,
        IMauiNativePortabilityService nativePortabilityService,
        IHostConnectionTestService hostConnectionTestService,
        IHostHealthService hostHealthService,
        IMetadataRepository metadataRepository,
        IMetadataValueValidationService metadataValueValidation,
        IHostUndoService hostUndoService,
        IRecycleBinService recycleBinService,
        IMoveItSessionService sessionService,
        IOptionsSnapshot<UiOptions> uiOptions,
        IOptionsSnapshot<GridBehaviorOptions> gridBehaviorOptions,
        IOptionsSnapshot<MetadataUiOptions> metadataOptions,
        IOptionsSnapshot<HostBehaviorOptions> hostBehaviorOptions,
        ILogger<HostsController> logger)
    {
        _hostService = hostService;
        _taskService = taskService;
        _nativePortabilityService = nativePortabilityService;
        _hostConnectionTestService = hostConnectionTestService;
        _hostHealthService = hostHealthService;
        _metadataRepository = metadataRepository;
        _metadataValueValidation = metadataValueValidation;
        _hostUndoService = hostUndoService;
        _recycleBinService = recycleBinService;
        _sessionService = sessionService;
        _uiOptions = uiOptions.Value;
        _gridBehaviorOptions = gridBehaviorOptions.Value;
        _metadataOptions = metadataOptions.Value;
        _hostBehaviorOptions = hostBehaviorOptions.Value;
        _logger = logger;
    }

    // Renders the Hosts workspace immediately. The authoritative MOVEit inventory and task-assignment data
    // are loaded asynchronously after the page shell is visible so first navigation is not blocked by REST work.
    public IActionResult Index(string filter = "all")
    {
        try
        {
            _ = RequireSession();
            return View(new HostsPageViewModel
            {
                ActiveFilter = NormalizeHostFilter(filter),
                UiHostRefreshIntervalSeconds = Math.Max(0, _hostBehaviorOptions.UiRefreshIntervalSeconds.Value),
                ImportRowLimit = Math.Clamp(_uiOptions.ImportRowLimit.Value, 1, 1000),
                AutoFormatNewTaskHostNames = _uiOptions.AutoFormatNewTaskHostNames.Value,
                LastRefreshUtc = null,
                Grid = BuildEmptyGrid(NormalizeHostFilter(filter))
            });
        }
        catch (MoveItAuthenticationException ex)
        {
            TempData["LoginError"] = ex.Message;
            return RedirectToAction("Login", "Account");
        }
    }

    // Performs the initial asynchronous Host inventory load after the page shell is already visible.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> InitialGrid(string filter = "all", bool preferCached = false, CancellationToken cancellationToken = default)
    {
        try
        {
            var initial = preferCached
                ? await _hostService.GetInitialHostSnapshotAsync(cancellationToken)
                : new InitialCacheSnapshot<HostCacheSnapshot>(await _hostService.GetHostSnapshotAsync(false, cancellationToken), RefreshRecommended: false);
            return await BuildHostGridJsonAsync(initial.Snapshot, filter, ensureObjects: true, cancellationToken: cancellationToken, refreshRecommended: initial.RefreshRecommended);
        }
        catch (MoveItApiException ex)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Refreshes Host grid data through the shared cache contract; forceRefresh bypasses cache age.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> RefreshGrid(string filter = "all", CancellationToken cancellationToken = default, bool forceRefresh = false)
    {
        try
        {
            var snapshot = await _hostService.GetHostSnapshotAsync(forceRefresh, cancellationToken);
            return await BuildHostGridJsonAsync(snapshot, filter, ensureObjects: true, cancellationToken: cancellationToken);
        }
        catch (MoveItApiException ex)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Builds the unchanged reusable Host-grid payload from one already-resolved Host snapshot.
    private async Task<JsonResult> BuildHostGridJsonAsync(
        HostCacheSnapshot snapshot,
        string filter,
        bool ensureObjects,
        CancellationToken cancellationToken,
        bool refreshRecommended = false)
    {
        var session = RequireSession();
        var environmentKey = session.GetEnvironmentKey();
        var hosts = snapshot.Hosts;

        if (ensureObjects)
            await _metadataRepository.EnsureObjectsAsync(environmentKey, ObjectType, hosts.Select(host => host.Id), cancellationToken);

        var metadataByHost = await _metadataRepository.GetObjectMetadataBatchAsync(environmentKey, ObjectType, hosts.Select(host => host.Id), cancellationToken);
        var undoHostIds = await _hostUndoService.GetAvailableHostIdsAsync(session.Username, hosts.Select(host => host.Id), cancellationToken);
        var scopedHosts = ApplyHostFilter(hosts, filter);
        var rows = scopedHosts
            .Select(host => BuildGridRow(host, metadataByHost.TryGetValue(host.Id, out var metadata) ? metadata : null, undoHostIds.Contains(host.Id)))
            .Select(GridClientContract.FromRow)
            .ToList();

        return Json(new
        {
            success = true,
            refreshedUtc = snapshot.RefreshedUtc,
            rows,
            refreshRecommended,
            licenseCapacity = MoveItLicenseCapacity.ForHosts(session, hosts.Count)
        });
    }

    // Creates a minimal persistent MOVEit Host and records its MAUI creation audit.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Host", "Create", "")]
    public async Task<IActionResult> CreateHost([FromBody] HostCreateRequest? request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequest(new { message = "Host creation request is required." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();

        try
        {
            using var creationLease = await MoveItLicensePolicy.AcquireHostCreationAsync(session, cancellationToken);
            var currentHosts = (await _hostService.GetHostSnapshotAsync(true, cancellationToken)).Hosts;
            var capacityBlock = MoveItLicensePolicy.GetHostCapacityBlock(session, currentHosts.Count, 1);
            if (!string.IsNullOrWhiteSpace(capacityBlock)) return Conflict(new { code = "LicenseLimitReached", message = capacityBlock });

            var created = await _hostService.CreateHostAsync(request.Name, request.HostType, cancellationToken, request.Unc, request.ResourceGroup);
            try
            {
                await _metadataRepository.RecordObjectCreatedAsync(session.GetEnvironmentKey(), ObjectType, created.Id, session.Username, cancellationToken);
            }
            catch (Exception metadataException)
            {
                _logger.LogError(metadataException,
                    "MOVEit Host {HostId} was created, but MAUI could not record its creation audit metadata.",
                    created.Id);
            }

            return Json(new
            {
                success = true,
                host = new { id = created.Id, name = created.Name, type = created.HostType, typeCode = created.HostTypeCode }
            });
        }
        catch (HostV0CreationOutcomeException ex)
        {
            _logger.LogWarning(ex, "New Host {HostName} creation could not be verified; known Host ID {HostId}; stage {VerificationStage}; reason {VerificationReason}.",
                ex.HostName, ex.HostId, ex.VerificationStage, ex.VerificationReason);
            return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message, outcomeUnknown = true, hostId = ex.HostId });
        }
        catch (HostNameAlreadyExistsException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Tests one saved Host through the verified native MOVEit connection-test pattern for its Host type.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestHost([FromBody] HostTestRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.HostId))
            return BadRequest(new { message = "Host ID is required." });

        try
        {
            var session = RequireSession();
            var result = await _hostConnectionTestService.TestHostAsync(request.HostId, cancellationToken);
            await _hostHealthService.SaveConnectionTestResultAsync(
                session.GetEnvironmentKey(),
                result,
                DateTimeOffset.UtcNow,
                cancellationToken);

            return Json(new
            {
                success = true,
                hostId = result.HostId,
                hostName = result.HostName,
                hostTypeCode = result.HostTypeCode,
                status = result.Status,
                message = result.Message,
                currentDirectory = result.CurrentDirectory,
                itemCount = result.ItemCount
            });
        }
        catch (Exception ex) when (ex is MoveItAuthenticationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Exports one or more Hosts through a temporary disabled carrier Task so MOVEit performs native dependency
    // discovery and Server-Key/passphrase credential protection. The carrier Task is never included in the package.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [MoveItObjectPermission("Host", "Edit", "ItemIds")]
    public async Task<IActionResult> ExportHost([FromBody] NativePortabilityExportRequest? request, CancellationToken cancellationToken = default)
    {
        if (request?.ItemIds is null || request.ItemIds.Count == 0) return BadRequest(new { message = "Select at least one Host to export." });
        try
        {
            var session = RequireSession();
            var export = await _nativePortabilityService.ExportHostsAsync(request.ItemIds, request.ProtectionMode, request.Passphrase, request.IncludeKeys, cancellationToken);
            _logger.LogInformation(
                "Host portability export completed for {UserName}. SelectedCount={SelectedCount}; ProtectionMode={ProtectionMode}; IncludeKeys={IncludeKeys}; PackageFile={PackageFile}; PackageBytes={PackageBytes}.",
                session.Username, request.ItemIds.Count, request.ProtectionMode, request.IncludeKeys, export.FileName, export.Content.LongLength);
            return File(export.Content, "application/zip", export.FileName);
        }
        catch (Exception ex) when (ex is InvalidOperationException or MoveItApiException or MoveItComException or ArgumentException)
        {
            _logger.LogWarning(
                "Host portability export failed for {UserName}. SelectedCount={SelectedCount}; ProtectionMode={ProtectionMode}; IncludeKeys={IncludeKeys}; Error={Error}.",
                User.Identity?.Name ?? "unknown", request.ItemIds.Count, request.ProtectionMode, request.IncludeKeys, ex.Message);
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Decodes a native Host package and returns MOVEit's duplicate model plus MAUI metadata compatibility warnings.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [RequestSizeLimit(MaxHostImportBytes + (1024 * 1024))]
    [MoveItObjectPermission("Host", "Import", "")]
    public async Task<IActionResult> PreflightImportHost([FromForm] NativePortabilityImportPreflightRequest? request, CancellationToken cancellationToken = default)
    {
        if (request?.File is null || request.File.Length <= 0) return BadRequest(new { message = "Select a MAUI Host portability package." });
        if (request.File.Length > MaxHostImportBytes) return BadRequest(new { message = "The MAUI Host package exceeds the 50 MB import limit." });
        if (!string.Equals(Path.GetExtension(request.File.FileName), ".maui", StringComparison.OrdinalIgnoreCase)) return BadRequest(new { message = "Host imports must use a .maui file." });
        try
        {
            var session = RequireSession();
            using var memory = new MemoryStream();
            await request.File.CopyToAsync(memory, cancellationToken);
            var result = await _nativePortabilityService.PreflightImportAsync(memory.ToArray(), "Host", request.Passphrase, cancellationToken);
            _logger.LogInformation(
                "Host portability package analyzed for {UserName}. File={FileName}; PackageBytes={PackageBytes}; ProtectionMode={ProtectionMode}; ItemCount={ItemCount}; ConflictCount={ConflictCount}; MetadataAcknowledgementRequired={MetadataAcknowledgementRequired}.",
                session.Username, Path.GetFileName(request.File.FileName), request.File.Length, result.ProtectionMode, result.ItemCount, result.ConflictCount, result.RequiresMetadataAcknowledgement);
            return Json(new { success = true, result });
        }
        catch (Exception ex) when (ex is InvalidOperationException or MoveItApiException or MoveItComException or ArgumentException)
        {
            _logger.LogWarning(
                "Host portability package analysis failed for {UserName}. File={FileName}; PackageBytes={PackageBytes}; Error={Error}.",
                User.Identity?.Name ?? "unknown", Path.GetFileName(request.File.FileName), request.File.Length, ex.Message);
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Imports a native Host package after applying per-object Overwrite or Rename conflict resolutions.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [RequestSizeLimit(MaxHostImportBytes + (1024 * 1024))]
    [MoveItObjectPermission("Host", "Import", "")]
    public async Task<IActionResult> ImportHost([FromForm] NativePortabilityImportRequest? request, CancellationToken cancellationToken = default)
    {
        if (request?.File is null || request.File.Length <= 0) return BadRequest(new { message = "Select a MAUI Host portability package." });
        if (request.File.Length > MaxHostImportBytes) return BadRequest(new { message = "The MAUI Host package exceeds the 50 MB import limit." });
        if (!string.Equals(Path.GetExtension(request.File.FileName), ".maui", StringComparison.OrdinalIgnoreCase)) return BadRequest(new { message = "Host imports must use a .maui file." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();
        try
        {
            var resolutions = JsonSerializer.Deserialize<List<NativePortabilityConflictResolution>>(request.ResolutionsJson ?? "[]", new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new List<NativePortabilityConflictResolution>();
            using var memory = new MemoryStream();
            await request.File.CopyToAsync(memory, cancellationToken);
            var result = await _nativePortabilityService.ImportAsync(memory.ToArray(), "Host", request.Passphrase, resolutions, leaveTasksDisabled: true, acknowledgeUnsupportedMetadata: request.AcknowledgeUnsupportedMetadata, username: session.Username, cancellationToken: cancellationToken);
            _logger.LogInformation(
                "Host portability import completed for {UserName}. File={FileName}; PackageBytes={PackageBytes}; ResolutionCount={ResolutionCount}; ImportedCount={ImportedCount}; OverwrittenCount={OverwrittenCount}; RenamedCount={RenamedCount}; HasWarning={HasWarning}.",
                session.Username, Path.GetFileName(request.File.FileName), request.File.Length, resolutions.Count, result.ImportedCount, result.OverwrittenCount, result.RenamedCount, !string.IsNullOrWhiteSpace(result.Warning));
            return Json(new
            {
                success = result.Success,
                packageType = result.PackageType,
                importedCount = result.ImportedCount,
                overwrittenCount = result.OverwrittenCount,
                renamedCount = result.RenamedCount,
                items = result.Items,
                warning = result.Warning
            });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                "Host portability import rejected an invalid conflict-resolution payload for {UserName}. File={FileName}; PackageBytes={PackageBytes}; Error={Error}.",
                session.Username, Path.GetFileName(request.File.FileName), request.File.Length, ex.Message);
            return BadRequest(new { message = "The import conflict-resolution request is invalid." });
        }
        catch (NativePortabilityImportOutcomeUnknownException ex)
        {
            // MOVEit's native import request was sent but MAUI did not receive a definitive upstream result.
            // Distinguish this from a validated failure so the browser does not encourage an unsafe retry.
            _logger.LogError(
                "Host portability import outcome is unknown for {UserName}. File={FileName}; PackageBytes={PackageBytes}; Error={Error}. Verify MOVEit destination state before retrying.",
                session.Username, Path.GetFileName(request.File.FileName), request.File.Length, ex.Message);
            return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message, outcomeUnknown = true });
        }
        catch (Exception ex) when (ex is InvalidOperationException or MoveItApiException or MoveItComException or ArgumentException)
        {
            _logger.LogWarning(
                "Host portability import failed before a confirmed native commit for {UserName}. File={FileName}; PackageBytes={PackageBytes}; Error={Error}.",
                session.Username, Path.GetFileName(request.File.FileName), request.File.Length, ex.Message);
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Returns common MOVEit Host information, MAUI metadata, audit data, and visible task dependencies.
    [HttpGet]
    public async Task<IActionResult> Details(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { message = "Host ID is required." });

        try
        {
            var session = RequireSession();
            var host = await _hostService.GetHostAsync(id, cancellationToken);
            var (_, definition) = HostDefinitionJson.UnwrapHost(await _hostService.GetHostDefinitionAsync(id, cancellationToken));
            var editorName = HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(definition, "Name"));
            var editorDescription = HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(definition, "Desc"));
            var metadata = await _metadataRepository.GetObjectMetadataAsync(session.GetEnvironmentKey(), ObjectType, id, cancellationToken);
            var isDefaultLocalFileSystemHost = IsDefaultLocalFileSystemHost(host.Id);

            return Json(new
            {
                editVersion = ObjectEditVersion.Create("Host", host.Id, ObjectEditVersion.Named(editorName, editorDescription)),
                host = new
                {
                    id = host.Id,
                    name = editorName,
                    description = editorDescription,
                    type = host.Type,
                    resourceGroups = host.Group,
                    canEditMoveItFields = host.CanEditDelete,
                    canEditName = host.CanEditDelete && !isDefaultLocalFileSystemHost,
                    moveItFieldsReadOnlyReason = host.CanEditDelete
                            ? string.Empty
                            : "Your MOVEit permissions do not allow this Host configuration to be edited. MAUI metadata can still be changed."
                },
                metadata = metadata.Values,
                fields = _metadataOptions.Hosts.Fields,
                audit = new
                {
                    createdUtc = metadata.CreatedUtc,
                    createdBy = metadata.CreatedBy,
                    modifiedUtc = metadata.ModifiedUtc,
                    modifiedBy = metadata.ModifiedBy
                },
                tasks = host.AssignedTasks.Select(task => new { id = task.Id, name = task.Name })
            });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Previews the visible Tasks assigned to a Host before enabling or disabling them.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PreflightAssignedTaskState([FromBody] HostAssignedTaskStateRequest? request, CancellationToken cancellationToken)
    {
        var hostId = (request?.HostId ?? string.Empty).Trim();
        if (hostId.Length == 0) return BadRequest(new { message = "Host ID is required." });
        if (_sessionService.Get() is null) return MauiControllerExceptionMapper.SessionExpired();

        try
        {
            var preview = await BuildAssignedTaskStatePreviewAsync(hostId, request!.Enabled, cancellationToken);
            return Json(new
            {
                success = true,
                hostId = preview.HostId,
                hostName = preview.HostName,
                enabled = preview.Enabled,
                assigned = preview.Items.Count,
                willChange = preview.Items.Count(item => item.WillChange),
                alreadyInState = preview.Items.Count(item => item.AlreadyInState),
                blocked = preview.Items.Count(item => item.Blocked),
                tasks = preview.Items.Select(item => new
                {
                    id = item.Id,
                    name = item.Name,
                    description = item.Description,
                    status = item.Status,
                    willChange = item.WillChange,
                    alreadyInState = item.AlreadyInState,
                    blocked = item.Blocked,
                    message = item.Message
                })
            });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Re-resolves a Host's assigned Tasks at execution time and changes only those Tasks through the existing Task state service.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetAssignedTasksEnabled([FromBody] HostAssignedTaskStateRequest? request, [FromServices] IMoveItObjectAccessService access, CancellationToken cancellationToken)
    {
        var hostId = (request?.HostId ?? string.Empty).Trim();
        if (hostId.Length == 0) return BadRequest(new { message = "Host ID is required." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();

        try
        {
            var preview = await BuildAssignedTaskStatePreviewAsync(hostId, request!.Enabled, cancellationToken);
            var taskIds = preview.Items
                .Where(item => item.WillChange)
                .Select(item => item.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (taskIds.Count == 0)
            {
                return Json(new
                {
                    success = true,
                    hostId = preview.HostId,
                    hostName = preview.HostName,
                    succeeded = 0,
                    skipped = 0,
                    failed = 0,
                    alreadyInState = preview.Items.Count(item => item.AlreadyInState),
                    blocked = preview.Items.Count(item => item.Blocked),
                    items = Array.Empty<object>()
                });
            }

            foreach (var taskId in taskIds) await access.RequireAsync("Task", taskId, "Edit", cancellationToken);
            var result = await _taskService.SetTasksEnabledAsync(taskIds, request.Enabled, cancellationToken);
            var changedTaskIds = result.Items
                .Where(item => item.Success && !item.Skipped)
                .Select(item => item.Id)
                .ToList();

            if (changedTaskIds.Count > 0)
            {
                try
                {
                    await _metadataRepository.RecordObjectsConfigurationModifiedAsync(
                        session.GetEnvironmentKey(),
                        TaskObjectType,
                        changedTaskIds,
                        session.Username,
                        cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "MOVEit assigned Task state changed for Host {HostId}, but MAUI could not update Modified audit metadata for Task IDs {TaskIds}.",
                        preview.HostId,
                        string.Join(",", changedTaskIds));
                }
            }

            _logger.LogInformation(
                "{Action} assigned Tasks for Host {HostId} ({HostName}) requested by {Username}. {Succeeded} changed, {Skipped} skipped, {Failed} failed.",
                request.Enabled ? "Enable" : "Disable",
                preview.HostId,
                preview.HostName,
                session.Username,
                result.Succeeded,
                result.Skipped,
                result.Failed);

            return Json(new
            {
                success = result.Failed == 0,
                hostId = preview.HostId,
                hostName = preview.HostName,
                succeeded = result.Succeeded,
                skipped = result.Skipped,
                failed = result.Failed,
                alreadyInState = preview.Items.Count(item => item.AlreadyInState),
                blocked = preview.Items.Count(item => item.Blocked),
                items = result.Items.Select(item => new
                {
                    id = item.Id,
                    name = item.Name,
                    success = item.Success,
                    skipped = item.Skipped,
                    message = item.Message
                })
            });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Saves editable common MOVEit Host information plus MAUI-owned metadata.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Host", "Edit", "id")]
    public async Task<IActionResult> SaveDetails(string id, [FromBody] HostDetailsUpdateRequest? request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { message = "Host ID is required." });
        if (request is null) return BadRequest(new { message = "Host information request is required." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();

        var moveItSaved = false;
        try
        {
            var hostId = id.Trim();
            var values = await _metadataValueValidation.FilterAndValidateAsync(session.GetEnvironmentKey(), _metadataOptions.Hosts, request.Metadata, cancellationToken);
            // Shared Host policy preserves the built-in Local File System name while allowing Description edits.
            var updateMoveItFields = request.UpdateMoveItFields;
            if (updateMoveItFields)
            {
                using var edit = ObjectEditVersion.Require("Host", hostId, request.EditVersion);
                await _hostService.UpdateBasicDetailsAsync(hostId, request.Name, request.Description, cancellationToken);
                moveItSaved = true;
                await _metadataRepository.RecordObjectConfigurationModifiedAsync(session.GetEnvironmentKey(), ObjectType, hostId, session.Username, cancellationToken);
            }

            await _metadataRepository.SaveObjectMetadataAsync(session.GetEnvironmentKey(), ObjectType, hostId, values, session.Username, cancellationToken);
            return Json(new { success = true });
        }
        catch (HostPartialSaveException ex)
        {
            _logger.LogWarning(ex, "Host {HostId} information has an unverified save outcome; stage {VerificationStage}; reason {VerificationReason}.",
                id, ex.VerificationStage, ex.VerificationReason);
            return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message, moveItSaved = true, outcomeUnknown = true });
        }
        catch (Exception ex) when (ex is ObjectEditConflictException or ObjectCheckoutReleaseException)
        {
            return Conflict(new { message = ex.Message, editConflict = true, moveItSaved = false });
        }
        catch (Exception ex) when (moveItSaved && ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Host {HostId} information was saved but MAUI metadata/audit updates did not complete.", id);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "Host information was saved and verified in MOVEit, but MAUI could not finish its metadata/audit updates. Reopen Host Information before retrying remaining changes.",
                moveItSaved = true
            });
        }
        catch (HostNameAlreadyExistsException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Performs one live safety/dependency check for every selected Host before bulk deletion is offered.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Host", "Delete", "HostIds")]
    public async Task<IActionResult> PreflightDeleteHosts([FromBody] BulkHostRequest? request, CancellationToken cancellationToken)
    {
        var hostIds = NormalizeHostIds(request?.HostIds);
        if (hostIds.Count == 0) return BadRequest(new { message = "Select at least one Host." });
        if (_sessionService.Get() is null) return MauiControllerExceptionMapper.SessionExpired();

        try
        {
            var results = await _hostService.PreflightDeleteHostsAsync(hostIds, cancellationToken);
            var blockers = results.Where(item => !item.CanDelete).ToList();
            return Json(new
            {
                success = blockers.Count == 0,
                message = blockers.Count == 0 ? string.Empty : "One or more selected Hosts cannot be deleted. Resolve the listed dependencies or permissions and try again.",
                targets = results.Select(item => new { hostId = item.HostId, hostName = item.HostName }),
                blockers = blockers.Select(item => new
                {
                    hostId = item.HostId,
                    hostName = item.HostName,
                    message = item.Message,
                    taskReferences = item.TaskReferences.Select(reference => new { id = reference.Id, name = reference.Name }),
                    alternateHostReferences = item.AlternateHostReferences.Select(reference => new { id = reference.Id, name = reference.Name })
                })
            });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Deletes a selected Host set using the established per-Host MOVEit guardrails and one Recycle Bin capture operation.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Host", "Delete", "HostIds")]
    public async Task<IActionResult> DeleteHosts([FromBody] BulkHostRequest? request, CancellationToken cancellationToken)
    {
        var hostIds = NormalizeHostIds(request?.HostIds);
        if (hostIds.Count == 0) return BadRequest(new { message = "Select at least one Host." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();

        try
        {
            var recycleCapture = await _recycleBinService.CaptureForDeletionAsync(ObjectType, hostIds, session.Username, request?.DeleteWithoutRecycle == true, cancellationToken);
            if (recycleCapture.NeedsConfirmation && request?.DeleteWithoutRecycle != true)
            {
                return StatusCode(StatusCodes.Status409Conflict, new
                {
                    code = "RecycleCaptureFailed",
                    message = "MAUI could not create a recovery copy for one or more selected Hosts. Delete anyway? Hosts deleted without a recovery copy cannot be restored from the Recycle Bin.",
                    failures = recycleCapture.Failures.Select(item => new { id = item.ObjectId, message = item.Message })
                });
            }

            var results = new List<HostDeleteResultItem>();
            var deletedIds = new List<string>();
            foreach (var hostId in hostIds)
            {
                try
                {
                    var deleted = await _hostService.DeleteHostAsync(hostId, cancellationToken);
                    deletedIds.Add(deleted.Id);
                    results.Add(new HostDeleteResultItem(deleted.Id, deleted.Name, true, string.Empty));
                }
                catch (MoveItAuthenticationException)
                {
                    throw;
                }
                catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
                {
                    results.Add(new HostDeleteResultItem(hostId, hostId, false, ex.Message));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unable to delete MOVEit Host {HostId} during bulk deletion.", hostId);
                    results.Add(new HostDeleteResultItem(hostId, hostId, false, "MAUI could not delete the Host."));
                }
            }

            var deletedIdSet = deletedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unusedRecycleIds = recycleCapture.RecycleIdsByObjectId
                .Where(pair => !deletedIdSet.Contains(pair.Key))
                .Select(pair => pair.Value)
                .ToList();
            if (unusedRecycleIds.Count > 0)
            {
                try { await _recycleBinService.DiscardCapturedAsync(unusedRecycleIds, cancellationToken); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { _logger.LogWarning(ex, "MAUI could not remove unused Recycle Bin snapshots after one or more Host deletes failed."); }
            }

            var warning = await CleanupDeletedHostsAsync(session, deletedIds, cancellationToken);
            var failed = results.Count(item => !item.Success);
            return Json(new
            {
                success = failed == 0,
                succeeded = deletedIds.Count,
                failed,
                warning,
                items = results.Select(item => new { id = item.Id, name = item.Name, success = item.Success, message = item.Message })
            });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Performs a live safety/dependency check before displaying the destructive Delete Host confirmation.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Host", "Delete", "HostId")]
    public async Task<IActionResult> PreflightDeleteHost([FromBody] HostActionRequest? request, CancellationToken cancellationToken)
    {
        var hostId = (request?.HostId ?? string.Empty).Trim();
        if (hostId.Length == 0) return BadRequest(new { message = "Host ID is required." });
        if (_sessionService.Get() is null) return MauiControllerExceptionMapper.SessionExpired();

        try
        {
            var result = await _hostService.PreflightDeleteHostAsync(hostId, cancellationToken);
            return Json(new
            {
                success = result.CanDelete,
                hostId = result.HostId,
                hostName = result.HostName,
                message = result.Message,
                taskReferences = result.TaskReferences.Select(item => new { id = item.Id, name = item.Name }),
                alternateHostReferences = result.AlternateHostReferences.Select(item => new { id = item.Id, name = item.Name })
            });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Revalidates all guardrails and permanently deletes one Host through the MOVEit Windows API.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Host", "Delete", "HostId")]
    public async Task<IActionResult> DeleteHost([FromBody] HostActionRequest? request, CancellationToken cancellationToken)
    {
        var hostId = (request?.HostId ?? string.Empty).Trim();
        if (hostId.Length == 0) return BadRequest(new { message = "Host ID is required." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();

        try
        {
            var recycleCapture = await _recycleBinService.CaptureForDeletionAsync(ObjectType, new[] { hostId }, session.Username, request?.DeleteWithoutRecycle == true, cancellationToken);
            if (recycleCapture.NeedsConfirmation && request?.DeleteWithoutRecycle != true)
            {
                return StatusCode(StatusCodes.Status409Conflict, new
                {
                    code = "RecycleCaptureFailed",
                    message = "MAUI could not create a recovery copy for this Host. Delete anyway? A Host deleted without a recovery copy cannot be restored from the Recycle Bin.",
                    failures = recycleCapture.Failures.Select(item => new { id = item.ObjectId, message = item.Message })
                });
            }

            HostDeleteResult deleted;
            try { deleted = await _hostService.DeleteHostAsync(hostId, cancellationToken); }
            catch
            {
                if (recycleCapture.RecycleIdsByObjectId.TryGetValue(hostId, out var unusedId))
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
                            "Unable to discard unused Recycle Bin capture {RecycleItemId} after Host {HostId} deletion failed.",
                            unusedId,
                            hostId);
                    }
                }
                throw;
            }

            var warning = await CleanupDeletedHostsAsync(session, new[] { deleted.Id }, cancellationToken);
            return Json(new { success = true, hostId = deleted.Id, hostName = deleted.Name, warning });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to delete MOVEit Host {HostId}.", hostId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "MAUI could not delete the Host." });
        }
    }

    // Captures the current Host configuration before the native MOVEit editor is allowed to open.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Host", "Edit", "HostId")]
    public async Task<IActionResult> PrepareHostEdit([FromBody] HostActionRequest? request, CancellationToken cancellationToken)
    {
        var hostId = (request?.HostId ?? string.Empty).Trim();
        if (hostId.Length == 0) return BadRequest(new { message = "Host ID is required." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();

        try
        {
            var snapshot = await NativeEditorSnapshotRetry.CaptureAsync(
                token => _hostUndoService.CaptureAsync(hostId, session.Username, token),
                "Host", hostId, _logger, cancellationToken);
            return Json(new { success = true, hostId = snapshot.HostId, hostName = snapshot.HostName, snapshotCreatedUtc = snapshot.CreatedUtc });
        }
        catch (Exception ex) when (ex is MoveItAuthenticationException || (ex is MoveItComException or InvalidOperationException or ArgumentException))
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to capture an undo snapshot before opening MOVEit Host {HostId}.", hostId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "MAUI could not save the Host undo snapshot. The editor was not opened." });
        }
    }

    // Purpose: Validates undo host and returns the information required before execution.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Host", "Edit", "HostId")]
    public async Task<IActionResult> PreflightUndoHost([FromBody] HostActionRequest? request, CancellationToken cancellationToken)
    {
        var hostId = (request?.HostId ?? string.Empty).Trim();
        if (hostId.Length == 0) return BadRequest(new { message = "Host ID is required." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();

        try
        {
            var result = await _hostUndoService.GetHistoryAsync(hostId, session.Username, cancellationToken);
            return Json(new
            {
                success = result.CanUndo,
                hostId = result.ObjectId,
                hostName = result.ObjectName,
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
            _logger.LogError(ex, "Unable to validate undo data for MOVEit Host {HostId}.", hostId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "MAUI could not validate the Host undo data." });
        }
    }

    // Purpose: Restores the selected Host undo snapshot.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Host", "Edit", "HostId")]
    public async Task<IActionResult> UndoHost([FromBody] HostActionRequest? request, CancellationToken cancellationToken)
    {
        var hostId = (request?.HostId ?? string.Empty).Trim();
        if (hostId.Length == 0) return BadRequest(new { message = "Host ID is required." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();

        try
        {
            var snapshotId = (request?.SnapshotId ?? string.Empty).Trim();
            if (snapshotId.Length == 0) return BadRequest(new { message = "Undo snapshot is required." });
            var result = await _hostUndoService.RestoreAsync(hostId, session.Username, snapshotId, cancellationToken);
            return Json(new { success = true, hostId = result.HostId, hostName = result.HostName, snapshotCreatedUtc = result.SnapshotCreatedUtc, warning = result.Warning });
        }
        catch (Exception ex) when (ex is MoveItAuthenticationException || (ex is MoveItComException or InvalidOperationException or ArgumentException))
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to restore the undo snapshot for MOVEit Host {HostId}.", hostId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "MAUI could not restore the Host undo snapshot." });
        }
    }

    // Records that MAUI started a native Host editor session for the current user.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Host", "Edit", "HostId")]
    public async Task<IActionResult> MarkHostEdited([FromBody] HostActionRequest? request, CancellationToken cancellationToken)
    {
        var hostId = (request?.HostId ?? string.Empty).Trim();
        if (hostId.Length == 0) return BadRequest(new { message = "Host ID is required." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();

        try
        {
            await _metadataRepository.RecordObjectConfigurationModifiedAsync(session.GetEnvironmentKey(), ObjectType, hostId, session.Username, cancellationToken);
            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to update MAUI modification audit metadata for Host {HostId}.", hostId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "MAUI could not update the Host modification audit information." });
        }
    }

    // MOVEit reserves Host ID 0 for the built-in Local File System Host. Its native fields are
    // read-only through the Automation API, but MAUI metadata for the Host remains editable.
    private static bool IsDefaultLocalFileSystemHost(string? hostId) =>
        string.Equals((hostId ?? string.Empty).Trim(), "0", StringComparison.OrdinalIgnoreCase);

    // Filters Hosts by the normalized MOVEit Host type code represented by the selected navigation filter.
    private static IReadOnlyList<AutomationHost> ApplyHostFilter(IReadOnlyList<AutomationHost> hosts, string filter)
    {
        var typeCode = NormalizeHostFilter(filter) switch
        {
            "local-file-system" => "FileSystem",
            "unc-share" => "Share",
            "moveit-transfer" => "siLock",
            "ftp-ftps" => "FTP",
            "ssh-sftp" => "SSHFTP",
            "pop3" => "POP3",
            "smtp" => "SMTP",
            "amazon-s3" => "S3",
            "onedrive" => "OneDrive",
            "azure-blob" => "AzureBlob",
            "sharepoint" => "SharePoint",
            "google-storage" => "GoogleStorage",
            _ => string.Empty
        };

        return typeCode.Length == 0
            ? hosts
            : hosts.Where(host => host.TypeCode.Equals(typeCode, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    // Normalizes Host navigation filters so an unsupported value safely falls back to All Hosts.
    private static string NormalizeHostFilter(string filter)
    {
        var value = (filter ?? string.Empty).Trim().ToLowerInvariant();
        return value is
            "local-file-system" or
            "unc-share" or
            "moveit-transfer" or
            "ftp-ftps" or
            "ssh-sftp" or
            "pop3" or
            "smtp" or
            "amazon-s3" or
            "azure-blob" or
            "sharepoint" or
            "google-storage"
            ? value
            : "all";
    }

    // Builds the reusable Hosts grid with configured native and MAUI metadata columns.
    private GridViewModel BuildGrid(GridOptions options, IReadOnlyList<GridRow> rows, string filter = "all") => new()
    {
        GridId = "hosts",
        PreferenceKey = $"hosts:{NormalizeHostFilter(filter)}",
        EntityName = "Hosts",
        SearchPlaceholder = "Search host names, descriptions, destinations, and metadata",
        Columns = BuildColumns(),
        Rows = rows,
        Options = options,
        SearchColumns = options.SearchColumns,
        FitColumnsToGrid = _gridBehaviorOptions.FitColumnsToGrid.Value,
        SelectRowsBeyondGridSize = _gridBehaviorOptions.SelectRowsBeyondGridSize.Value,
        DefaultPageSize = _gridBehaviorOptions.DefaultRowSize.Value,
        PageSizeOptions = _gridBehaviorOptions.DefaultRowSize.PossibleValues
    };

    // Adds configured Host metadata and audit fields as optional grid columns and searchable metadata.
    private GridOptions BuildHostGridOptions(GridOptions source)
    {
        var metadataKeys = MetadataGridSupport.GetFieldColumnKeys(_metadataOptions.Hosts).ToList();

        var addActions = source.AddActions
            .Where(action => !string.IsNullOrWhiteSpace(action))
            .Select(action => action.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var bulkActions = source.BulkActions
            .Where(action => !string.IsNullOrWhiteSpace(action))
            .Select(action => action.Trim())
            .Where(action => !action.Equals("Test Hosts", StringComparison.OrdinalIgnoreCase))
            .ToList();
        bulkActions.Insert(0, "Test Hosts");

        var defaultColumns = source.DefaultColumns
            .Where(column => !column.Equals("Health", StringComparison.OrdinalIgnoreCase))
            .ToList();
        defaultColumns.Add("Health");

        return new GridOptions
        {
            DefaultColumns = defaultColumns,
            AvailableColumns = source.AvailableColumns
                .Concat(new[] { "Group", "Health", "LastHealthCheck", "TestResults" })
                .Concat(MetadataGridSupport.GetAvailableColumnKeys(_metadataOptions.Hosts))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            BulkActions = bulkActions,
            AddActions = addActions,
            SearchColumns = source.SearchColumns.Concat(new[] { "Group", "Health", "TestResults" }).Concat(metadataKeys).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    // Defines common Host columns and optional native properties that can be enabled in Grid Settings.
    private IReadOnlyList<GridColumn> BuildColumns()
    {
        var columns = new List<GridColumn>
        {
            new() { Key = "Id", Label = "Host ID" },
            new() { Key = "Name", Label = "Host Name", DefaultWidth = 300 },
            new() { Key = "Description", Label = "Description", DefaultWidth = 300 },
            new() { Key = "Destination", Label = "Destination", DefaultWidth = 300 },
            new() { Key = "Type", Label = "Type", DefaultWidth = 150 },
            new() { Key = "Secure", Label = "Secure", DefaultWidth = 150 },
            new() { Key = "Assigned", Label = "# Assigned", Type = "number", DefaultWidth = 110 },
            new() { Key = "Health", Label = "Health", Type = "status", DefaultWidth = 120 },
            new() { Key = "LastHealthCheck", Label = "Last Health Check", Type = "datetime", DefaultWidth = 180 },
            new() { Key = "TestResults", Label = "Test Results", DefaultWidth = 350 },
            new() { Key = "Username", Label = "Username", DefaultWidth = 200 },
            new() { Key = "Port", Label = "Port", Type = "number" },
            new() { Key = "Group", Label = "Resource Group" }
        };

        MetadataGridSupport.AddColumns(columns, _metadataOptions.Hosts);
        return columns;
    }

    // Maps one live Host and its local metadata into the reusable grid row contract.
    private GridRow BuildGridRow(AutomationHost host, ObjectMetadataSnapshot? metadata, bool hasUndo)
    {
        var values = new Dictionary<string, string>
        {
            ["Id"] = host.Id,
            ["Name"] = host.Name,
            ["Description"] = host.Description,
            ["Destination"] = host.Destination,
            ["Type"] = host.Type,
            ["Secure"] = host.Secure,
            ["Assigned"] = host.AssignedCount.ToString(CultureInfo.InvariantCulture),
            ["Username"] = host.Username,
            ["Port"] = host.Port?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["Group"] = host.Group,
            ["Health"] = GetHostHealth(metadata),
            ["LastHealthCheck"] = FormatHostHealthDate(metadata),
            ["HealthDetail"] = GetHostHealthDetail(metadata),
            ["TestResults"] = GetHostHealthDetail(metadata)
        };
        var raw = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase)
        {
            ["Assigned"] = host.AssignedCount.ToString(CultureInfo.InvariantCulture),
            ["Port"] = host.Port?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["HasUndo"] = hasUndo ? "1" : "0",
            ["CanEdit"] = host.CanEditDelete ? "1" : "0",
            ["CanDelete"] = host.CanEditDelete ? "1" : "0",
            ["LastHealthCheck"] = GetHostHealthCheckedUtc(metadata)?.ToString("O") ?? string.Empty,
            ["HealthDetail"] = GetHostHealthDetail(metadata),
            ["TestResults"] = GetHostHealthDetail(metadata),
            ["TypeCode"] = host.TypeCode,
            ["TestSupported"] = _hostConnectionTestService.SupportsHostType(host.TypeCode) ? "1" : "0",
            ["Selectable"] = host.TypeCode.Equals("FileSystem", StringComparison.OrdinalIgnoreCase) ? "0" : "1"
        };

        MetadataGridSupport.AddValues(values, raw, _metadataOptions.Hosts, metadata, FormatDate);

        return new GridRow
        {
            Id = host.Id,
            InfoActionUrl = Url.Action(nameof(Details), new { id = host.Id }) ?? string.Empty,
            Values = values,
            RawValues = raw
        };
    }


    // Builds the confirmation model from the same Host assignment inventory used by Host Information and the live Task inventory.
    private async Task<HostAssignedTaskStatePreview> BuildAssignedTaskStatePreviewAsync(string hostId, bool enabled, CancellationToken cancellationToken)
    {
        var normalizedHostId = hostId.Trim();
        var hostSnapshotTask = _hostService.GetHostSnapshotAsync(true, cancellationToken);
        var taskSnapshotTask = _taskService.GetTaskSnapshotAsync(true, cancellationToken);
        await Task.WhenAll(hostSnapshotTask, taskSnapshotTask);

        var host = (await hostSnapshotTask).Hosts
            .FirstOrDefault(item => string.Equals(item.Id, normalizedHostId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Host '{normalizedHostId}' is no longer available to the current MOVEit user.");
        var tasksById = (await taskSnapshotTask).Tasks
            .ToDictionary(task => task.Id, StringComparer.OrdinalIgnoreCase);

        var items = new List<HostAssignedTaskStatePreviewItem>();
        foreach (var reference in host.AssignedTasks
                     .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                     .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            if (!tasksById.TryGetValue(reference.Id, out var task))
            {
                items.Add(new HostAssignedTaskStatePreviewItem(
                    reference.Id,
                    reference.Name,
                    string.Empty,
                    "Unavailable",
                    false,
                    false,
                    true,
                    "Task is no longer available to the current MOVEit user."));
                continue;
            }

            var alreadyInState = task.Enabled == enabled;
            var blocked = false;
            var message = alreadyInState
                ? enabled ? "Already enabled." : "Already disabled."
                : enabled ? "Will be enabled." : "Will be disabled.";

            if (!alreadyInState && enabled && task.Scheduled.Equals("Incomplete", StringComparison.OrdinalIgnoreCase))
            {
                blocked = true;
                message = "Cannot be enabled because the Task is incomplete.";
            }
            else if (!alreadyInState && enabled && task.Scheduled.Equals("Unscheduled", StringComparison.OrdinalIgnoreCase))
            {
                blocked = true;
                message = "Cannot be enabled because the Task has no schedule.";
            }

            items.Add(new HostAssignedTaskStatePreviewItem(
                task.Id,
                task.Name,
                task.Description,
                task.Enabled ? "Enabled" : "Disabled",
                !alreadyInState && !blocked,
                alreadyInState,
                blocked,
                message));
        }

        return new HostAssignedTaskStatePreview(host.Id, host.Name, enabled, items);
    }

    private sealed record HostAssignedTaskStatePreview(
        string HostId,
        string HostName,
        bool Enabled,
        IReadOnlyList<HostAssignedTaskStatePreviewItem> Items);

    private sealed record HostAssignedTaskStatePreviewItem(
        string Id,
        string Name,
        string Description,
        string Status,
        bool WillChange,
        bool AlreadyInState,
        bool Blocked,
        string Message);


    // Reads system-managed Host Health metadata without exposing it through the editable metadata field catalog.
    private static string GetHostHealth(ObjectMetadataSnapshot? metadata)
        => metadata is not null && metadata.Values.TryGetValue(HostHealthMetadata.HealthField, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : HostHealthStates.Unknown;

    private static string GetHostHealthDetail(ObjectMetadataSnapshot? metadata)
        => metadata is not null && metadata.Values.TryGetValue(HostHealthMetadata.DetailField, out var value)
            ? value
            : string.Empty;

    private static DateTimeOffset? GetHostHealthCheckedUtc(ObjectMetadataSnapshot? metadata)
        => metadata is not null
           && metadata.Values.TryGetValue(HostHealthMetadata.LastCheckUtcField, out var value)
           && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static string FormatHostHealthDate(ObjectMetadataSnapshot? metadata)
        => FormatDate(GetHostHealthCheckedUtc(metadata));

    // Formats local audit timestamps consistently with the Tasks grid.
    private static string FormatDate(DateTimeOffset? value)
        => value.HasValue ? value.Value.ToLocalTime().ToString("MMM d, yyyy  h:mm tt", CultureInfo.CurrentCulture) : string.Empty;

    // Builds an empty Hosts grid while preserving configured columns/settings after an API failure.
    private GridViewModel BuildEmptyGrid(string filter)
    {
        var options = BuildHostGridOptions(_uiOptions.Grids.TryGetValue("hosts", out var gridOptions) ? gridOptions : new GridOptions());
        return BuildGrid(options, Array.Empty<GridRow>(), filter);
    }

    // Normalizes a bulk Host selection once so controller actions never process blank or duplicate IDs.
    private static List<string> NormalizeHostIds(IEnumerable<string>? hostIds)
        => (hostIds ?? Array.Empty<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    // Removes local Host metadata and Undo history after MOVEit confirms deletion.
    private async Task<string> CleanupDeletedHostsAsync(MoveItSessionState session, IReadOnlyCollection<string> deletedIds, CancellationToken cancellationToken)
    {
        if (deletedIds.Count == 0) return string.Empty;
        var cleanupWarnings = new List<string>();
        try
        {
            await _metadataRepository.DeleteObjectsAsync(session.GetEnvironmentKey(), ObjectType, deletedIds, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MOVEit Host deletion succeeded, but MAUI metadata cleanup failed for Host IDs {HostIds}.", string.Join(",", deletedIds));
            cleanupWarnings.Add("MAUI could not remove all local Host metadata.");
        }

        try
        {
            await _hostUndoService.DeleteSnapshotsAsync(deletedIds, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MOVEit Host deletion succeeded, but MAUI undo cleanup failed for Host IDs {HostIds}.", string.Join(",", deletedIds));
            cleanupWarnings.Add("MAUI could not remove all local Host undo snapshots.");
        }

        return cleanupWarnings.Count == 0
            ? string.Empty
            : $"The MOVEit Host deletion succeeded, but {string.Join(" ", cleanupWarnings)} Review the MAUI application log if stale local records remain.";
    }

    private sealed record HostDeleteResultItem(string Id, string Name, bool Success, string Message);

    public sealed record HostTestRequest(string HostId);

    // Returns the authenticated MOVEit session or redirects callers through the standard authentication exception path.
    private MoveItSessionState RequireSession()
        => _sessionService.Get() ?? throw new MoveItAuthenticationException(MauiControllerExceptionMapper.SessionExpiredMessage);
}
