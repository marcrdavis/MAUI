// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Controller boundary for the experimental MAUI Host Editor.

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Features.Editors.Hosts;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Controllers;

[Authorize]
public sealed class HostEditorController : Controller
{
    private const string ObjectType = "Host";
    private readonly IHostEditorService _editorService;
    private readonly IAutomationHostService _hosts;
    private readonly IHostEditorSaveService _saveService;
    private readonly IMetadataRepository _metadataRepository;
    private readonly IHostUndoService _undo;
    private readonly IMetadataValueValidationService _metadataValueValidation;
    private readonly IMoveItSessionService _sessionService;
    private readonly MetadataUiOptions _metadataOptions;
    private readonly ILogger<HostEditorController> _logger;

    public HostEditorController(
        IHostEditorService editorService,
        IAutomationHostService hosts,
        IHostEditorSaveService saveService,
        IMetadataRepository metadataRepository,
        IHostUndoService undo,
        IMetadataValueValidationService metadataValueValidation,
        IMoveItSessionService sessionService,
        IOptionsSnapshot<MetadataUiOptions> metadataOptions,
        ILogger<HostEditorController> logger)
    {
        _editorService = editorService;
        _hosts = hosts;
        _saveService = saveService;
        _metadataRepository = metadataRepository;
        _undo = undo;
        _metadataValueValidation = metadataValueValidation;
        _sessionService = sessionService;
        _metadataOptions = metadataOptions.Value;
        _logger = logger;
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [MoveItObjectPermission("Host", "Read", "id")]
    public async Task<IActionResult> Shell(string id, CancellationToken cancellationToken)
    {
        var hostId = (id ?? string.Empty).Trim();
        if (hostId.Length == 0) return BadRequest(new { message = "Host ID is required." });
        var session = _sessionService.Get();
        if (session is null) return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });

        try
        {
            var environmentKey = session.GetEnvironmentKey();
            var hostTask = _editorService.GetHostAsync(hostId, cancellationToken);
            var metadataTask = _metadataRepository.GetObjectMetadataAsync(environmentKey, ObjectType, hostId, cancellationToken);
            await Task.WhenAll(hostTask, metadataTask);

            var host = await hostTask;
            var metadata = await metadataTask;
            var readOnly = HttpContext.RequestServices.GetService<IMoveItObjectAccessService>() is { } access
                ? !(await access.GetObjectAsync("Host", hostId, cancellationToken)).CanEdit : !host.CanEditDelete;
            readOnly |= !host.CanEditConfiguration;
            if (readOnly) host = host with
            {
                CanEditDelete = false, CanEditMoveItFields = false, CanEditConfiguration = false,
                ReadOnlyReason = "This Host is read-only for your MOVEit account.",
                ConfigurationSections = host.ConfigurationSections.Select(section => section with
                { Fields = section.Fields.Select(field => field with { Editable = false }).ToArray() }).ToArray()
            };
            return Json(new HostEditorShellViewModel
            {
                CheckoutObjectsForDurationOfEdit = !readOnly && HttpContext.RequestServices.GetRequiredService<IOptionsSnapshot<UiOptions>>().Value.CheckoutObjectsForDurationOfEdit.Value,
                Preferences = await HttpContext.RequestServices.GetRequiredService<IUserPreferencesService>().GetCurrentHostEditorPreferencesAsync(cancellationToken),
                Host = host,
                ReadOnly = readOnly,
                CanUndo = false, // Editor Undo starts empty; the grid retains its separate saved history.
                Metadata = metadata.Values,
                Fields = _metadataOptions.Hosts.Fields,
                Audit = new HostEditorAudit(metadata.CreatedUtc, metadata.CreatedBy, metadata.ModifiedUtc, metadata.ModifiedBy),
                Tasks = host.Tasks
            });
        }
        catch (MoveItAuthenticationException)
        {
            return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning(ex, "Host Editor shell could not load Host {HostId}.", hostId);
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Fingerprint([FromBody] HostFingerprintRequest? request, CancellationToken cancellationToken)
    {
        if (_sessionService.Get() is null) return Unauthorized(new { message = "MOVEit session expired." });
        if (request is null) return BadRequest(new { message = "SFTP Host settings are required." });
        try { return Json(new { fingerprint = await _hosts.RetrieveSshHostFingerprintAsync(request, cancellationToken) }); }
        catch (MoveItAuthenticationException) { return Unauthorized(new { message = "MOVEit session expired." }); }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException)
        { return BadRequest(new { message = "Unable to retrieve the SSH Host key. Verify the hostname, port, proxy and key-exchange settings." }); }
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Certificates(string id, CancellationToken cancellationToken)
    {
        if (_sessionService.Get() is null) return Unauthorized(new { message = "MOVEit session expired." });
        try { return Json(new { items = await _hosts.GetHostTlsCertificatesAsync(id, cancellationToken) }); }
        catch (MoveItAuthenticationException) { return Unauthorized(new { message = "MOVEit session expired." }); }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException)
        { return BadRequest(new { message = "Unable to load the available client certificates. Refresh the list or check your MOVEit permissions." }); }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> DocumentLibraries([FromBody] HostDocumentLibraryRequest? request, CancellationToken cancellationToken)
    {
        if (_sessionService.Get() is null) return Unauthorized(new { message = "MOVEit session expired." });
        if (request is null) return BadRequest(new { message = "SharePoint Host settings are required." });
        try { return Json(new { items = await _hosts.BrowseDocumentLibrariesAsync(request, cancellationToken) }); }
        catch (MoveItAuthenticationException) { return Unauthorized(new { message = "MOVEit session expired." }); }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException)
        { return BadRequest(new { message = "Unable to browse document libraries. Verify the site, Certificate Access settings and direct connection." }); }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Browse(string id, [FromBody] AutomationHostBrowseRequest? request, CancellationToken cancellationToken)
    {
        var session = _sessionService.Get();
        if (session is null) return Unauthorized(new { message = "MOVEit session expired." });
        if (session.Info?.IsAdmin != true) return StatusCode(StatusCodes.Status403Forbidden, new { message = "Host browsing requires a MOVEit administrator." });
        if (string.IsNullOrWhiteSpace(id) || request is null) return BadRequest(new { message = "Host ID and browse location are required." });
        try
        {
            // Inspection uses saved Host settings and never writes editor values or creates a Task.
            if (id.Trim() == "0")
            {
                var local = await HttpContext.RequestServices.GetRequiredService<ITraditionalTaskEditorFolderBrowseService>()
                    .BrowseLocalAsync(request.Directory, cancellationToken);
                return Json(new AutomationHostBrowseResult("0", local.HostName, "FileSystem", local.CurrentPath, local.ParentPath,
                    local.Items.Select(item => new AutomationHostBrowseItem(item.Name, item.Path, item.IsFolder, item.Id, "", item.Size, item.LastModified)).ToArray()));
            }
            var browse = HttpContext.RequestServices.GetRequiredService<IAutomationHostBrowseService>();
            return Json(await browse.BrowseAsync(id.Trim(), request.Directory, rootName: request.RootName,
                rootId: request.RootId, cancellationToken: cancellationToken));
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItAuthenticationException or InvalidOperationException or ArgumentException)
        { return MauiControllerExceptionMapper.Map(ex); }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Host", "Edit", "id")]
    public async Task<IActionResult> Save(string id, [FromBody] HostEditorSaveRequest? request, CancellationToken cancellationToken)
    {
        var hostId = (id ?? string.Empty).Trim();
        if (hostId.Length == 0) return BadRequest(new { message = "Host ID is required." });
        if (request is null) return BadRequest(new { message = "Host Editor save request is required." });
        var session = _sessionService.Get();
        if (session is null) return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });

        try
        {
            var environmentKey = session.GetEnvironmentKey();

            using var edit = ObjectEditVersion.Require("Host", hostId, request.EditVersion);
            // Validate all MAUI-owned values before the MOVEit write so a bad metadata value cannot
            // turn an otherwise preventable validation error into a partial Host save.
            var metadata = await _metadataValueValidation.FilterAndValidateAsync(
                environmentKey,
                _metadataOptions.Hosts,
                request.Metadata,
                cancellationToken);

            var moveItResult = await _saveService.SaveMoveItAsync(
                hostId,
                request,
                session.Username,
                cancellationToken);

            try
            {
                await _metadataRepository.SaveObjectMetadataAsync(environmentKey, ObjectType, hostId, metadata, session.Username, cancellationToken);
                if (moveItResult.Changed)
                    await _metadataRepository.RecordObjectConfigurationModifiedAsync(environmentKey, ObjectType, hostId, session.Username, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Host Editor saved MOVEit Host {HostId}={MoveItSaved}, but MAUI metadata/audit persistence did not complete.",
                    hostId,
                    moveItResult.Changed);
                return BadRequest(new
                {
                    message = moveItResult.Changed
                        ? "The Host was saved in MOVEit, but MAUI could not fully save its metadata/audit state. Close and reopen the Host Editor before retrying."
                        : "MAUI could not save the Host metadata.",
                    moveItSaved = moveItResult.Changed
                });
            }

            return Json(new
            {
                success = true,
                moveItChanged = moveItResult.Changed,
                undoSnapshotId = moveItResult.UndoSnapshotId,
                hostId,
                hostName = moveItResult.HostName,
                typeCode = moveItResult.TypeCode
            });
        }
        catch (ObjectCheckoutRejectedException ex) { return Conflict(ex.Feedback()); }
        catch (ObjectEditLeaseException ex)
        {
            return Conflict(new { message = ex.Message, leaseLost = true, editConflict = true, moveItSaved = ex.MayHaveSaved });
        }
        catch (Exception ex) when (ex is ObjectEditConflictException or ObjectCheckoutReleaseException)
        {
            return Conflict(new { message = ex.Message, moveItSaved = false, editConflict = true });
        }
        catch (HostNameAlreadyExistsException ex)
        {
            return Conflict(new { message = ex.Message, moveItSaved = false });
        }
        catch (HostPartialSaveException ex)
        {
            _logger.LogError(ex, "Host Editor encountered a partial-save condition for Host {HostId}; stage {VerificationStage}; reason {VerificationReason}.",
                hostId, ex.VerificationStage, ex.VerificationReason);
            return BadRequest(new { message = ex.Message, moveItSaved = true });
        }
        catch (MoveItAuthenticationException)
        {
            return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning(ex, "Host Editor could not save Host {HostId}.", hostId);
            return BadRequest(new { message = ex.Message, moveItSaved = false });
        }
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePreferences([FromBody] HostEditorPreferences request,
        [FromServices] IUserPreferencesService preferences, CancellationToken cancellationToken)
    {
        if (_sessionService.Get() is null) return Unauthorized();
        try { return Json(await preferences.SaveCurrentHostEditorPreferencesAsync(request, cancellationToken)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

}

