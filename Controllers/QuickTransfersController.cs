// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Controllers;

[Authorize]
public sealed class QuickTransfersController : Controller
{
    private readonly IQuickTransferService _quickTransfers;
    private readonly IAutomationHostBrowseService _browse;
    private readonly IMoveItSessionService _session;
    private readonly UiOptions _ui;
    private readonly ILogger<QuickTransfersController> _logger;

    public QuickTransfersController(
        IQuickTransferService quickTransfers,
        IAutomationHostBrowseService browse,
        IMoveItSessionService session,
        IOptionsSnapshot<UiOptions> ui,
        ILogger<QuickTransfersController> logger)
    {
        _quickTransfers = quickTransfers;
        _browse = browse;
        _session = session;
        _ui = ui.Value;
        _logger = logger;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        try
        {
            if (RequireSession().Info?.IsAdmin != true || !_ui.EnableQuickTransfer.Value)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Quick Transfer is disabled or unavailable to this MOVEit user." });
            var model = await _quickTransfers.GetPageModelAsync(_ui.AutoFormatNewTaskHostNames.Value, cancellationToken);
            return View(model);
        }
        catch (MoveItAuthenticationException ex)
        {
            TempData["LoginError"] = ex.Message;
            return RedirectToAction("Login", "Account");
        }
        catch (MoveItApiException ex)
        {
            _logger.LogError(ex, "Quick Transfer reference data could not be loaded.");
            return View(new QuickTransferPageViewModel
            {
                AutoFormatNewTaskHostNames = _ui.AutoFormatNewTaskHostNames.Value,
                LoadError = "Quick Transfer reference data could not be loaded from MOVEit. Refresh the page after the server connection is available. See Configuration > Logs for details."
            });
        }
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> References(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        try
        {
            if (RequireSession().Info?.IsAdmin != true || !_ui.EnableQuickTransfer.Value)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Quick Transfer is disabled or unavailable to this MOVEit user." });
            var model = forceRefresh
                ? await _quickTransfers.RefreshReferencesAsync(_ui.AutoFormatNewTaskHostNames.Value, cancellationToken)
                : await _quickTransfers.GetPageModelAsync(_ui.AutoFormatNewTaskHostNames.Value, cancellationToken);
            return Json(new
            {
                success = true,
                pgpLicensed = model.PgpLicensed,
                sourceHosts = model.SourceHosts.Select(ToHostJson),
                destinationHosts = model.DestinationHosts.Select(ToHostJson),
                pgpKeys = model.PgpKeys.Select(key => new { id = key.Id, name = key.Name, uid = key.Uid, publicPrivate = key.PublicPrivate })
            });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItAuthenticationException or InvalidOperationException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Browse([FromBody] AutomationHostBrowseRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.HostId)) return BadRequest(new { message = "Host ID is required." });
        try
        {
            if (RequireSession().Info?.IsAdmin != true || !_ui.EnableQuickTransfer.Value)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Quick Transfer is disabled or unavailable to this MOVEit user." });
            var result = await _browse.BrowseAsync(
                request.HostId,
                request.Directory,
                request.FileMask,
                request.RootName,
                request.RootId,
                request.TaskId,
                request.StepId,
                cancellationToken);
            return Json(new
            {
                success = true,
                hostId = result.HostId,
                hostName = result.HostName,
                hostTypeCode = result.HostTypeCode,
                currentDirectory = result.CurrentDirectory,
                parentDirectory = result.ParentDirectory,
                rootName = result.RootName,
                rootId = result.RootId,
                rootKind = result.RootKind,
                items = result.Items.Select(item => new
                {
                    name = item.Name,
                    path = item.Path,
                    isDirectory = item.IsDirectory,
                    nativeId = item.NativeId,
                    nativeType = item.NativeType,
                    size = item.Size,
                    lastModified = item.LastModified
                })
            });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItAuthenticationException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Prepare([FromBody] QuickTransferRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null) return BadRequest(new { message = "Quick Transfer request is required." });
        try
        {
            if (RequireSession().Info?.IsAdmin != true || !_ui.EnableQuickTransfer.Value)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Quick Transfer is disabled or unavailable to this MOVEit user." });
            var result = await _quickTransfers.PrepareAsync(request, cancellationToken);
            return Json(new
            {
                success = true,
                transferToken = result.TransferToken,
                taskId = result.TaskId,
                taskName = result.TaskName,
                persistentTask = result.PersistentTask,
                deleteTemporaryTaskAfterSuccess = result.DeleteTemporaryTaskAfterSuccess,
                reusedPreparedTask = result.ReusedPreparedTask
            });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItAuthenticationException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Transfer([FromBody] QuickTransferRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null) return BadRequest(new { message = "Quick Transfer request is required." });
        try
        {
            if (RequireSession().Info?.IsAdmin != true || !_ui.EnableQuickTransfer.Value)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Quick Transfer is disabled or unavailable to this MOVEit user." });
            var result = await _quickTransfers.StartAsync(request, cancellationToken);
            return Json(new
            {
                success = true,
                transferToken = result.TransferToken,
                taskId = result.TaskId,
                taskName = result.TaskName,
                nominalStart = result.NominalStart,
                persistentTask = result.PersistentTask,
                deleteTemporaryTaskAfterSuccess = result.DeleteTemporaryTaskAfterSuccess
            });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItAuthenticationException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Quick Transfer could not be started.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Quick Transfer could not be started. Review Configuration > Logs for details." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel([FromBody] QuickTransferCancelRequest? request, CancellationToken cancellationToken = default)
    {
        var transferToken = (request?.TransferToken ?? string.Empty).Trim();
        if (transferToken.Length == 0) return BadRequest(new { message = "Transfer token is required." });
        try
        {
            if (RequireSession().Info?.IsAdmin != true || !_ui.EnableQuickTransfer.Value)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Quick Transfer is disabled or unavailable to this MOVEit user." });
            var result = await _quickTransfers.CancelAsync(transferToken, cancellationToken);
            return Json(ToStatusJson(result));
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItAuthenticationException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Status(string transferToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transferToken)) return BadRequest(new { message = "Transfer token is required." });
        try
        {
            if (RequireSession().Info?.IsAdmin != true || !_ui.EnableQuickTransfer.Value)
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Quick Transfer is disabled or unavailable to this MOVEit user." });
            var result = await _quickTransfers.GetStatusAsync(transferToken, cancellationToken);
            return Json(ToStatusJson(result));
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItAuthenticationException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    private static object ToStatusJson(QuickTransferStatusResult result) => new
    {
        success = true,
        transferToken = result.TransferToken,
        taskId = result.TaskId,
        taskName = result.TaskName,
        state = result.State,
        message = result.Message,
        percentComplete = result.PercentComplete,
        currentFileBytes = result.CurrentFileBytes,
        totalFileBytes = result.TotalFileBytes,
        filesTransferred = result.FilesTransferred,
        totalBytesTransferred = result.TotalBytesTransferred,
        completed = result.Completed,
        transferSucceeded = result.Success
    };

    private MoveItSessionState RequireSession()
        => _session.Get() ?? throw new MoveItAuthenticationException("Your MOVEit session has expired. Sign in again.");

    private static object ToHostJson(QuickTransferHostOption host) => new
    {
        id = host.Id,
        name = host.Name,
        typeCode = host.TypeCode,
        type = host.Type,
        browseSupported = host.BrowseSupported,
        rootKind = host.RootKind
    };
}
