// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;
using MoveItAutomationUI.Services.MoveIt.Tasks;

namespace MoveItAutomationUI.Controllers;

[Authorize]
public sealed class TasksController : Controller
{
    private const string ObjectType = "Task";
    private readonly IAutomationTaskService _taskService;
    private readonly ITaskCrudCoordinator _taskCrud;
    private readonly IMoveItRestClient _moveItRestClient;
    private readonly ITaskPreflightService _taskPreflightService;
    private readonly IResourceGroupManagementService _resourceGroupManagementService;
    private readonly IResourceGroupLookupService _resourceGroupLookup;
    private readonly IAutomationHostService _hostService;
    private readonly IAutomationScheduleForecastService _scheduleForecastService;
    private readonly IAutomationTaskCalendarService _taskCalendarService;
    private readonly IMetadataRepository _metadataRepository;
    private readonly IMetadataValueValidationService _metadataValueValidation;
    private readonly ITaskNotificationTemplateService _notificationTemplateService;
    private readonly ITaskUndoService _taskUndoService;
    private readonly ITaskPortabilityService _taskPortabilityService;
    private readonly ITaskSpreadsheetImportService _taskSpreadsheetImportService;
    private readonly IMauiNativePortabilityService _nativePortabilityService;
    private readonly IRecycleBinService _recycleBinService;
    private readonly IMoveItSessionService _sessionService;
    private readonly UiOptions _uiOptions;
    private readonly GridBehaviorOptions _gridBehaviorOptions;
    private readonly MetadataUiOptions _metadataOptions;
    private readonly TaskBehaviorOptions _taskBehaviorOptions;
    private readonly ILogger<TasksController> _logger;

    // Initializes the Tasks controller with task, metadata, grid, and UI dependencies.
    public TasksController(
        IAutomationTaskService taskService,
        ITaskCrudCoordinator taskCrud,
        IMoveItRestClient moveItRestClient,
        ITaskPreflightService taskPreflightService,
        IResourceGroupManagementService resourceGroupManagementService,
        IResourceGroupLookupService resourceGroupLookup,
        IAutomationHostService hostService,
        IAutomationScheduleForecastService scheduleForecastService,
        IAutomationTaskCalendarService taskCalendarService,
        IMetadataRepository metadataRepository,
        IMetadataValueValidationService metadataValueValidation,
        ITaskNotificationTemplateService notificationTemplateService,
        ITaskUndoService taskUndoService,
        ITaskPortabilityService taskPortabilityService,
        ITaskSpreadsheetImportService taskSpreadsheetImportService,
        IMauiNativePortabilityService nativePortabilityService,
        IRecycleBinService recycleBinService,
        IMoveItSessionService sessionService,
        IOptionsSnapshot<UiOptions> uiOptions,
        IOptionsSnapshot<GridBehaviorOptions> gridBehaviorOptions,
        IOptionsSnapshot<MetadataUiOptions> metadataOptions,
        IOptionsSnapshot<TaskBehaviorOptions> taskBehaviorOptions,
        ILogger<TasksController> logger)
    {
        _taskService = taskService;
        _taskCrud = taskCrud;
        _moveItRestClient = moveItRestClient;
        _taskPreflightService = taskPreflightService;
        _resourceGroupManagementService = resourceGroupManagementService;
        _resourceGroupLookup = resourceGroupLookup;
        _hostService = hostService;
        _scheduleForecastService = scheduleForecastService;
        _taskCalendarService = taskCalendarService;
        _metadataRepository = metadataRepository;
        _metadataValueValidation = metadataValueValidation;
        _notificationTemplateService = notificationTemplateService;
        _taskUndoService = taskUndoService;
        _taskPortabilityService = taskPortabilityService;
        _taskSpreadsheetImportService = taskSpreadsheetImportService;
        _nativePortabilityService = nativePortabilityService;
        _recycleBinService = recycleBinService;
        _sessionService = sessionService;
        _uiOptions = uiOptions.Value;
        _gridBehaviorOptions = gridBehaviorOptions.Value;
        _metadataOptions = metadataOptions.Value;
        _taskBehaviorOptions = taskBehaviorOptions.Value;
        _logger = logger;
    }

    // Renders the Tasks workspace immediately. The authoritative MOVEit inventory is loaded asynchronously
    // after the page shell is visible so slow first-time reads do not block navigation or page rendering.
    public async Task<IActionResult> Index(string filter = "all", int? folderId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var session = RequireSession();
            var capabilities = MoveItLicensePolicy.GetCapabilities(session);
            var environmentKey = session.GetEnvironmentKey();
            var folders = FlattenFolders(await _metadataRepository.GetFoldersAsync(environmentKey, ObjectType, cancellationToken));
            var normalizedFilter = NormalizeFilter(filter);
            var options = BuildTaskGridOptions(
                _uiOptions.Grids.TryGetValue("tasks", out var gridOptions) ? gridOptions : new GridOptions(),
                normalizedFilter);

            return View(new TasksPageViewModel
            {
                ActiveFilter = normalizedFilter,
                SelectedFolderId = folderId,
                Folders = folders,
                UiTaskRefreshIntervalSeconds = Math.Max(0, _taskBehaviorOptions.UiRefreshIntervalSeconds.Value),
                AutoFormatNewTaskHostNames = _uiOptions.AutoFormatNewTaskHostNames.Value,
                ImportRowLimit = _uiOptions.ImportRowLimit.Value is >= 1 and <= 1000 ? _uiOptions.ImportRowLimit.Value : 500,
                AdvancedTasksLicensed = capabilities.AdvancedTasksLicensed,
                IsMoveItAdmin = session.Info?.IsAdmin == true,
                LastRefreshUtc = null,
                FolderTreeTasks = Array.Empty<TaskTreeItem>(),
                Grid = BuildGrid(options, Array.Empty<GridRow>(), normalizedFilter)
            });
        }
        catch (MoveItAuthenticationException ex)
        {
            TempData["LoginError"] = ex.Message;
            return RedirectToAction("Login", "Account");
        }
    }

    // Displays the Upcoming Runs page shell. The forecast is loaded asynchronously after the reusable grid
    // has initialized, matching the Tasks and Hosts progressive-loading behavior and preventing a full-height
    // server-rendered table from flashing before the fixed grid viewport is established.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult UpcomingRuns()
    {
        var options = _uiOptions.Grids.TryGetValue("upcomingruns", out var gridOptions)
            ? gridOptions
            : new GridOptions
            {
                DefaultColumns = new List<string> { "ScheduledTime", "TaskName", "Description", "Schedule", "Notes" },
                AvailableColumns = new List<string> { "ScheduledTime", "TaskName", "Description", "Schedule", "Notes", "TaskID", "ScheduleID" },
                SearchColumns = new List<string> { "TaskName", "Description", "Schedule", "Notes" }
            };

        try
        {
            _ = RequireSession();
            return View(new UpcomingRunsPageViewModel
            {
                Grid = BuildUpcomingRunsGrid(options, Array.Empty<GridRow>())
            });
        }
        catch (MoveItAuthenticationException ex)
        {
            TempData["LoginError"] = ex.Message;
            return RedirectToAction("Login", "Account");
        }
    }

    // Refreshes the calculated Upcoming Runs rows without reloading the page.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> UpcomingRunsGrid(CancellationToken cancellationToken = default)
    {
        try
        {
            _ = RequireSession();
            var forecast = await _scheduleForecastService.GetUpcomingRunsAsync(cancellationToken);
            var rows = forecast.Runs.Select(BuildUpcomingRunRow).ToList();
            return Json(new
            {
                rows,
                unsupportedScheduleCount = forecast.UnsupportedScheduleCount,
                refreshedUtc = DateTimeOffset.UtcNow
            });
        }
        catch (MoveItAuthenticationException)
        {
            return MauiControllerExceptionMapper.SessionExpired();
        }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Upcoming Runs refresh could not be calculated.");
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Displays the Task Calendar shell. Calendar event data is loaded asynchronously for the visible range.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Calendar()
    {
        try
        {
            _ = RequireSession();
            return View(new TaskCalendarPageViewModel
            {
                InitialDate = _taskCalendarService.GetServerToday().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            });
        }
        catch (MoveItAuthenticationException ex)
        {
            TempData["LoginError"] = ex.Message;
            return RedirectToAction("Login", "Account");
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Task Calendar could not determine the MOVEit server date.");
            TempData["LoginError"] = ex.Message;
            return RedirectToAction("Login", "Account");
        }
    }

    // Returns actual Task Run history plus projected scheduled runs for the requested server-local date range.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> CalendarData(string start, string end, CancellationToken cancellationToken = default)
    {
        if (!DateTime.TryParseExact(start, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDate)
            || !DateTime.TryParseExact(end, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDate))
            return BadRequest(new { message = "Calendar start and end dates must use yyyy-MM-dd format." });

        try
        {
            var session = RequireSession();
            var snapshot = await _taskCalendarService.GetCalendarAsync(
                startDate,
                endDate,
                session.GetEnvironmentKey(),
                cancellationToken);
            return Json(new
            {
                events = snapshot.Events,
                unsupportedScheduleCount = snapshot.UnsupportedScheduleCount,
                historicalLimitReached = snapshot.HistoricalLimitReached,
                generatedUtc = snapshot.GeneratedUtc
            });
        }
        catch (MoveItAuthenticationException)
        {
            return MauiControllerExceptionMapper.SessionExpired();
        }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning(ex, "Task Calendar data could not be loaded for {StartDate} through {EndDate}.", start, end);
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Performs the initial asynchronous Task inventory load after the page shell is already visible.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> InitialGrid(string filter = "all", int? folderId = null, bool preferCached = false, CancellationToken cancellationToken = default)
    {
        try
        {
            var initial = preferCached
                ? await _taskService.GetInitialTaskSnapshotAsync(cancellationToken)
                : new InitialCacheSnapshot<TaskCacheSnapshot>(await _taskService.GetTaskSnapshotAsync(false, cancellationToken), RefreshRecommended: false);
            return await BuildTaskGridJsonAsync(initial.Snapshot, filter, folderId, ensureObjects: true, cancellationToken: cancellationToken, refreshRecommended: initial.RefreshRecommended);
        }
        catch (MoveItApiException ex)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Refreshes Task grid data through the shared cache contract; forceRefresh is reserved for explicit Refresh Now/live reads.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> RefreshGrid(string filter = "all", int? folderId = null, CancellationToken cancellationToken = default, bool forceRefresh = false)
    {
        try
        {
            var snapshot = await _taskService.GetTaskSnapshotAsync(forceRefresh, cancellationToken);
            return await BuildTaskGridJsonAsync(snapshot, filter, folderId, ensureObjects: true, cancellationToken: cancellationToken);
        }
        catch (MoveItApiException ex)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Builds the unchanged reusable grid/tree payload from one already-resolved Task snapshot.
    private async Task<JsonResult> BuildTaskGridJsonAsync(
        TaskCacheSnapshot snapshot,
        string filter,
        int? folderId,
        bool ensureObjects,
        CancellationToken cancellationToken,
        bool refreshRecommended = false)
    {
        var session = RequireSession();
        var environmentKey = session.GetEnvironmentKey();
        var tasks = snapshot.Tasks;

        if (ensureObjects)
            await _metadataRepository.EnsureObjectsAsync(environmentKey, ObjectType, tasks.Select(task => task.Id), cancellationToken);

        var metadataByTask = await _metadataRepository.GetObjectMetadataBatchAsync(environmentKey, ObjectType, tasks.Select(task => task.Id), cancellationToken);
        var assignments = metadataByTask.ToDictionary(pair => pair.Key, pair => pair.Value.FolderId, StringComparer.OrdinalIgnoreCase);
        var undoTaskIds = await _taskUndoService.GetAvailableTaskIdsAsync(session.Username, tasks.Select(task => task.Id), cancellationToken);

        var scopedTasks = folderId.HasValue
            ? tasks.Where(task => assignments.TryGetValue(task.Id, out var assignedFolder) && assignedFolder == folderId).ToList()
            : tasks.ToList();
        scopedTasks = ApplyTaskFilter(scopedTasks, filter).ToList();

        var rows = scopedTasks
            .Select(task => BuildGridRow(task, metadataByTask.TryGetValue(task.Id, out var metadata) ? metadata : null, undoTaskIds.Contains(task.Id)))
            .Select(GridClientContract.FromRow)
            .ToList();
        var treeTasks = tasks
            .OrderBy(task => task.Name, StringComparer.OrdinalIgnoreCase)
            .Select(task => new
            {
                id = task.Id,
                name = task.Name,
                folderId = assignments.TryGetValue(task.Id, out var assignedFolder) ? assignedFolder : null
            })
            .ToList();

        return Json(new
        {
            success = true,
            refreshedUtc = snapshot.RefreshedUtc,
            rows,
            treeTasks,
            refreshRecommended,
            licenseCapacity = MoveItLicenseCapacity.ForTasks(session, tasks.Count)
        });
    }

    private const long MaxTaskImportBytes = MauiNativePortablePackage.MaxPackageBytes;

    // Exports one or more Tasks using MOVEit's native configuration portability pipeline plus MAUI metadata.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [MoveItObjectPermission("Task", "Edit", "ItemIds")]
    public async Task<IActionResult> ExportTask([FromBody] NativePortabilityExportRequest? request, CancellationToken cancellationToken = default)
    {
        if (request?.ItemIds is null || request.ItemIds.Count == 0) return BadRequest(new { message = "Select at least one Task to export." });
        try
        {
            var session = RequireSession();
            var export = await _nativePortabilityService.ExportTasksAsync(request.ItemIds, request.ProtectionMode, request.Passphrase, request.IncludeKeys, cancellationToken);
            _logger.LogInformation(
                "Task portability export completed for {UserName}. SelectedCount={SelectedCount}; ProtectionMode={ProtectionMode}; IncludeKeys={IncludeKeys}; PackageFile={PackageFile}; PackageBytes={PackageBytes}.",
                session.Username, request.ItemIds.Count, request.ProtectionMode, request.IncludeKeys, export.FileName, export.Content.LongLength);
            return File(export.Content, "application/zip", export.FileName);
        }
        catch (Exception ex) when (ex is InvalidOperationException or MoveItApiException or MoveItComException or ArgumentException)
        {
            _logger.LogWarning(
                "Task portability export failed for {UserName}. SelectedCount={SelectedCount}; ProtectionMode={ProtectionMode}; IncludeKeys={IncludeKeys}; Error={Error}.",
                User.Identity?.Name ?? "unknown", request.ItemIds.Count, request.ProtectionMode, request.IncludeKeys, ex.Message);
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Decodes a native Task package and returns MOVEit's duplicate model plus MAUI metadata compatibility warnings.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [RequestSizeLimit(MaxTaskImportBytes + (1024 * 1024))]
    [MoveItObjectPermission("Task", "Import", "")]
    public async Task<IActionResult> PreflightImportTask([FromForm] NativePortabilityImportPreflightRequest? request, CancellationToken cancellationToken = default)
    {
        if (request?.File is null || request.File.Length <= 0) return BadRequest(new { message = "Select a MAUI Task portability package." });
        if (request.File.Length > MaxTaskImportBytes) return BadRequest(new { message = "The MAUI Task package exceeds the 50 MB import limit." });
        if (!string.Equals(Path.GetExtension(request.File.FileName), ".maui", StringComparison.OrdinalIgnoreCase)) return BadRequest(new { message = "Task imports must use a .maui file." });
        try
        {
            var session = RequireSession();
            using var memory = new MemoryStream();
            await request.File.CopyToAsync(memory, cancellationToken);
            var result = await _nativePortabilityService.PreflightImportAsync(memory.ToArray(), "Task", request.Passphrase, cancellationToken);
            _logger.LogInformation(
                "Task portability package analyzed for {UserName}. File={FileName}; PackageBytes={PackageBytes}; ProtectionMode={ProtectionMode}; ItemCount={ItemCount}; ConflictCount={ConflictCount}; MetadataAcknowledgementRequired={MetadataAcknowledgementRequired}.",
                session.Username, Path.GetFileName(request.File.FileName), request.File.Length, result.ProtectionMode, result.ItemCount, result.ConflictCount, result.RequiresMetadataAcknowledgement);
            return Json(new { success = true, result });
        }
        catch (Exception ex) when (ex is InvalidOperationException or MoveItApiException or MoveItComException or ArgumentException)
        {
            _logger.LogWarning(
                "Task portability package analysis failed for {UserName}. File={FileName}; PackageBytes={PackageBytes}; Error={Error}.",
                User.Identity?.Name ?? "unknown", Path.GetFileName(request.File.FileName), request.File.Length, ex.Message);
            return MauiControllerExceptionMapper.Map(ex);
        }
    }



    // Loads CSV/XLSX rows for the client preview without running MOVEit or MAUI validation.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [RequestSizeLimit(MaxTaskImportBytes + (1024 * 1024))]
    [MoveItObjectPermission("Task", "Import", "")]
    public async Task<IActionResult> PreviewTaskSpreadsheet([FromForm] TaskSpreadsheetPreviewForm? request, CancellationToken cancellationToken = default)
    {
        if (request?.File is null || request.File.Length <= 0) return BadRequest(new { message = "Select a CSV or XLSX Task import file." });
        if (request.File.Length > MaxTaskImportBytes) return BadRequest(new { message = "The Task spreadsheet exceeds the 50 MB import limit." });
        var extension = Path.GetExtension(request.File.FileName);
        if (!extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Task spreadsheet imports must use a .csv or .xlsx file." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();
        try
        {
            using var memory = new MemoryStream();
            await request.File.CopyToAsync(memory, cancellationToken);
            var data = TaskSpreadsheetImportFileReader.Read(memory.ToArray(), Path.GetFileName(request.File.FileName));
            var result = new TaskSpreadsheetFilePreviewResult(
                Path.GetFileName(request.File.FileName),
                data.Headers,
                data.Rows.Select(row => new TaskSpreadsheetFilePreviewRow(row.RowNumber, row.Values)).ToArray(),
                data.SkippedBlankRows);
            _logger.LogInformation("Task spreadsheet preview loaded for {UserName}. File={FileName}; Rows={Rows}; Columns={Columns}; SkippedBlankRows={SkippedBlankRows}.",
                session.Username, result.FileName, result.Rows.Count, result.Columns.Count, result.SkippedBlankRows);
            return Json(new { success = true, result });
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning(ex, "Task spreadsheet preview failed for {UserName}. File={FileName}.", session.Username, Path.GetFileName(request.File.FileName));
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Analyzes a CSV/XLSX Task spreadsheet without invoking the native .maui portability workflow.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [RequestSizeLimit(MaxTaskImportBytes + (1024 * 1024))]
    [MoveItObjectPermission("Task", "Import", "")]
    public async Task<IActionResult> AnalyzeTaskSpreadsheet([FromForm] TaskSpreadsheetAnalyzeForm? request, CancellationToken cancellationToken = default)
    {
        if (request?.File is null || request.File.Length <= 0) return BadRequest(new { message = "Select a CSV or XLSX Task import file." });
        if (request.File.Length > MaxTaskImportBytes) return BadRequest(new { message = "The Task spreadsheet exceeds the 50 MB import limit." });
        var extension = Path.GetExtension(request.File.FileName);
        if (!extension.Equals(".csv", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Task spreadsheet imports must use a .csv or .xlsx file." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();
        try
        {
            using var memory = new MemoryStream();
            await request.File.CopyToAsync(memory, cancellationToken);
            var result = await _taskSpreadsheetImportService.AnalyzeAsync(memory.ToArray(), Path.GetFileName(request.File.FileName), request.TaskNamePrefix, request.UpdateExisting, request.LeaveTasksDisabled, session.Username, cancellationToken);
            _logger.LogInformation("Task spreadsheet analyzed for {UserName}. File={FileName}; Rows={Rows}; Valid={Valid}; Warnings={Warnings}; Errors={Errors}; IgnoredColumns={IgnoredColumns}.",
                session.Username, Path.GetFileName(request.File.FileName), result.Rows.Count, result.ValidRows, result.WarningRows, result.ErrorRows, result.IgnoredColumns);
            return Json(new { success = true, result });
        }
        catch (Exception ex) when (ex is InvalidOperationException or MoveItApiException or ArgumentException)
        {
            _logger.LogWarning(ex, "Task spreadsheet analysis failed for {UserName}. File={FileName}.", session.Username, Path.GetFileName(request.File.FileName));
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Imports one previously analyzed spreadsheet row so browser progress reflects committed Task operations.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [MoveItObjectPermission("Task", "Import", "")]
    public async Task<IActionResult> ImportTaskSpreadsheetRow([FromBody] TaskSpreadsheetImportRowRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.AnalysisId) || request.RowNumber <= 0)
            return BadRequest(new { message = "The analyzed Task spreadsheet row request is invalid." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();
        try
        {
            var result = await _taskSpreadsheetImportService.ImportRowAsync(request.AnalysisId, request.RowNumber, session.Username, cancellationToken);
            _logger.LogInformation("Task spreadsheet row imported for {UserName}. Row={Row}; Task={TaskName}; Outcome={Outcome}; TaskId={TaskId}.",
                session.Username, result.RowNumber, result.TaskName, result.Outcome, result.TaskId);
            return Json(new { success = true, result });
        }
        catch (Exception ex) when (ex is InvalidOperationException or MoveItApiException or MoveItComException or ArgumentException)
        {
            _logger.LogWarning(ex, "Task spreadsheet row import failed for {UserName}. AnalysisId={AnalysisId}; Row={Row}.", session.Username, request.AnalysisId, request.RowNumber);
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Imports a native Task package after applying per-object Overwrite or Rename conflict resolutions.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [RequestSizeLimit(MaxTaskImportBytes + (1024 * 1024))]
    [MoveItObjectPermission("Task", "Import", "")]
    public async Task<IActionResult> ImportTask([FromForm] NativePortabilityImportRequest? request, CancellationToken cancellationToken = default)
    {
        if (request?.File is null || request.File.Length <= 0) return BadRequest(new { message = "Select a MAUI Task portability package." });
        if (request.File.Length > MaxTaskImportBytes) return BadRequest(new { message = "The MAUI Task package exceeds the 50 MB import limit." });
        if (!string.Equals(Path.GetExtension(request.File.FileName), ".maui", StringComparison.OrdinalIgnoreCase)) return BadRequest(new { message = "Task imports must use a .maui file." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();
        try
        {
            var resolutions = JsonSerializer.Deserialize<List<NativePortabilityConflictResolution>>(request.ResolutionsJson ?? "[]", new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new List<NativePortabilityConflictResolution>();
            using var memory = new MemoryStream();
            await request.File.CopyToAsync(memory, cancellationToken);
            var result = await _nativePortabilityService.ImportAsync(memory.ToArray(), "Task", request.Passphrase, resolutions, request.LeaveTasksDisabled, request.AcknowledgeUnsupportedMetadata, session.Username, cancellationToken);
            _logger.LogInformation(
                "Task portability import completed for {UserName}. File={FileName}; PackageBytes={PackageBytes}; ResolutionCount={ResolutionCount}; ImportedCount={ImportedCount}; OverwrittenCount={OverwrittenCount}; RenamedCount={RenamedCount}; HasWarning={HasWarning}.",
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
                "Task portability import rejected an invalid conflict-resolution payload for {UserName}. File={FileName}; PackageBytes={PackageBytes}; Error={Error}.",
                session.Username, Path.GetFileName(request.File.FileName), request.File.Length, ex.Message);
            return BadRequest(new { message = "The import conflict-resolution request is invalid." });
        }
        catch (NativePortabilityImportOutcomeUnknownException ex)
        {
            // MOVEit's native import request was sent but MAUI did not receive a definitive upstream result.
            // Distinguish this from a validated failure so the browser does not encourage an unsafe retry.
            _logger.LogError(
                "Task portability import outcome is unknown for {UserName}. File={FileName}; PackageBytes={PackageBytes}; Error={Error}. Verify MOVEit destination state before retrying.",
                session.Username, Path.GetFileName(request.File.FileName), request.File.Length, ex.Message);
            return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message, outcomeUnknown = true });
        }
        catch (Exception ex) when (ex is InvalidOperationException or MoveItApiException or MoveItComException or ArgumentException)
        {
            _logger.LogWarning(
                "Task portability import failed before a confirmed native commit for {UserName}. File={FileName}; PackageBytes={PackageBytes}; Error={Error}.",
                session.Username, Path.GetFileName(request.File.FileName), request.File.Length, ex.Message);
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Clones one Task by exporting it to memory, renaming the portable package, and importing it as a new disabled Task.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Task", "Clone", "TaskId")]
    public async Task<IActionResult> CloneTask([FromBody] TaskCloneRequest? request, CancellationToken cancellationToken = default)
    {
        var taskId = (request?.TaskId ?? string.Empty).Trim();
        var newTaskName = (request?.NewTaskName ?? string.Empty).Trim();
        if (taskId.Length == 0) return BadRequest(new { message = "Task ID is required." });
        if (newTaskName.Length == 0) return BadRequest(new { message = "Task Name is required." });
        if (newTaskName.Length > 254) return BadRequest(new { message = "Task Name cannot exceed 254 characters." });

        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();

        try
        {
            var result = await _taskPortabilityService.CloneAsync(taskId, newTaskName, session.Username, cancellationToken);
            if (result.FailedCount > 0 || string.IsNullOrWhiteSpace(result.TaskId))
                return BadRequest(new { message = result.Items.FirstOrDefault(item => !item.Success)?.Error ?? "MOVEit did not return the cloned Task ID." });

            return Json(new
            {
                success = true,
                taskId = result.TaskId,
                taskName = result.TaskName,
                warning = result.Warning
            });
        }
        catch (TaskNameAlreadyExistsException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (Exception ex) when (ex is InvalidOperationException or MoveItApiException or MoveItComException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Task", "Clone", "TaskId")]
    public async Task<IActionResult> CreateAdvancedFromTraditional([FromBody] TaskAdvancedConversionRequest? request, CancellationToken cancellationToken = default)
    {
        var id = (request?.TaskId ?? string.Empty).Trim();
        if (id.Length == 0) return BadRequest(new { message = "Task ID is required." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();
        try
        {
            var featureBlock = MoveItLicensePolicy.GetAdvancedTaskCreationBlock(session, "Advanced");
            if (!string.IsNullOrWhiteSpace(featureBlock)) return BadRequest(new { message = featureBlock });
            using var creationLease = await MoveItLicensePolicy.AcquireTaskCreationAsync(session, cancellationToken);
            var currentTasks = (await _taskService.GetTaskSnapshotAsync(true, cancellationToken)).Tasks;
            var capacityBlock = MoveItLicensePolicy.GetTaskCapacityBlock(session, currentTasks.Count, 1);
            if (!string.IsNullOrWhiteSpace(capacityBlock)) return Conflict(new { code = "LicenseLimitReached", message = capacityBlock });
            var created = await _taskCrud.CreateAdvancedFromTraditionalAsync(id, cancellationToken);
            _taskService.InvalidateCache();
            var warning = "";
            try
            {
                await _metadataRepository.RecordObjectCreatedAsync(session.GetEnvironmentKey(), ObjectType, created.Id, session.Username, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Advanced Task {TaskId} created, but its MAUI audit metadata could not be recorded.", created.Id);
                warning = "The Advanced Task was created, but its MAUI creation audit could not be recorded.";
            }
            return Json(new { success = true, taskId = created.Id, taskName = created.Name, taskType = created.TaskType, warning });
        }
        catch (TaskAdvancedConversionOutcomeException ex)
        {
            _taskService.InvalidateCache();
            _logger.LogError(ex, "Traditional-to-Advanced creation needs review for source Task {SourceId}; created Task {TaskId}.", id, ex.TaskId);
            return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message, outcomeUnknown = true, taskId = ex.TaskId, taskName = ex.TaskName });
        }
        catch (Exception ex) when (ex is InvalidOperationException or MoveItApiException or MoveItComException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Non-admin MOVEit accounts only see the Resource Groups returned by their own authenticated REST session.
    // This separate lightweight read avoids blocking the Tasks workspace while the modal is closed.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> AvailableCreationResourceGroups([FromServices] IMoveItObjectAccessService access, CancellationToken cancellationToken = default)
    {
        try
        {
            var session = RequireSession();
            if (session.Info?.IsAdmin == true) return Json(new { groups = Array.Empty<string>() });
            var names = (await access.GetCreationAsync(true, cancellationToken)).Groups.GetValueOrDefault("Task", []);
            return Json(new { groups = names });
        }
        catch (Exception ex) when (ex is MoveItAuthenticationException or MoveItApiException or InvalidOperationException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Creates a new disabled/unscheduled MOVEit task, after a live REST duplicate-name check,
    // and returns the persistent ID needed by the selected MAUI/native Task editor.
    [HttpPost]
    [ValidateAntiForgeryToken]
    // Purpose: Validate, create, audit, and return a new MOVEit task for native editing.
    [MoveItObjectPermission("Task", "Create", "")]
    public async Task<IActionResult> CreateTask([FromBody] TaskCreateRequest? request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequest(new { message = "Task creation request is required." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();

        try
        {
            var featureBlock = MoveItLicensePolicy.GetAdvancedTaskCreationBlock(session, request.TaskType);
            if (!string.IsNullOrWhiteSpace(featureBlock)) return BadRequest(new { message = featureBlock });

            using var creationLease = await MoveItLicensePolicy.AcquireTaskCreationAsync(session, cancellationToken);
            var currentTasks = (await _taskService.GetTaskSnapshotAsync(true, cancellationToken)).Tasks;
            var capacityBlock = MoveItLicensePolicy.GetTaskCapacityBlock(session, currentTasks.Count, 1);
            if (!string.IsNullOrWhiteSpace(capacityBlock)) return Conflict(new { code = "LicenseLimitReached", message = capacityBlock });

            var requestedName = (request.Name ?? string.Empty).Trim();
            if (requestedName.Length == 0 || requestedName.Length > 254)
                return BadRequest(new { message = "Task Name must contain 1–254 characters." });
            var requestedGroup = session.Info?.IsAdmin == true ? string.Empty : (request.ResourceGroup ?? string.Empty).Trim();
            if (session.Info?.IsAdmin != true && requestedGroup.Length == 0)
                return BadRequest(new { message = "Select a Resource Group to create a Task." });
            if (currentTasks.Any(task => task.Name.Equals(requestedName, StringComparison.OrdinalIgnoreCase)))
                return Conflict(new { message = $"A Task named '{requestedName}' already exists." });
            // One shared creation entry point carries the selected group into either v1 or v0.
            var created = await _taskCrud.CreateEmptyAsync(requestedName, request.TaskType, requestedGroup, cancellationToken);
            _taskService.InvalidateCache();

            // MOVEit creation and the separate MAUI metadata store cannot be rolled back transactionally.
            // Record the audit immediately, but never report the already-created MOVEit task as failed if
            // the local audit write encounters a database problem.
            try
            {
                await _metadataRepository.RecordObjectCreatedAsync(session.GetEnvironmentKey(), ObjectType, created.Id, session.Username, cancellationToken);
            }
            catch (Exception metadataException)
            {
                _logger.LogError(metadataException,
                    "MOVEit Task {TaskId} was created, but MAUI could not record its creation audit metadata.",
                    created.Id);
            }

            return Json(new
            {
                success = true,
                task = new
                {
                    id = created.Id,
                    name = created.Name,
                    type = created.TaskType
                }
            });
        }
        catch (TaskV0CreationOutcomeException ex)
        {
            _taskService.InvalidateCache();
            _logger.LogWarning(ex, "Advanced/Sync Task creation has an unverified outcome for {TaskName}.", ex.TaskName);
            return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message, outcomeUnknown = true, taskId = ex.TaskId, taskName = ex.TaskName });
        }
        catch (TaskNameAlreadyExistsException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    // Purpose: Run task now.
    [MoveItObjectPermission("Task", "Run", "TaskId")]
    public async Task<IActionResult> RunTaskNow([FromBody] TaskActionRequest? request, CancellationToken cancellationToken)
    {
        var taskId = (request?.TaskId ?? string.Empty).Trim();
        if (taskId.Length == 0) return BadRequest(new { message = "Task ID is required." });

        try
        {
            var result = await _taskService.RunTaskNowAsync(taskId, cancellationToken);
            return Json(new
            {
                success = true,
                taskId = result.TaskId,
                taskName = result.TaskName,
                nominalStart = result.NominalStart
            });
        }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    // Purpose: Validate and snapshot the running task instances before Kill confirmation.
    public async Task<IActionResult> PreflightKillTask([FromBody] TaskActionRequest? request, CancellationToken cancellationToken)
    {
        var taskId = (request?.TaskId ?? string.Empty).Trim();
        if (taskId.Length == 0) return BadRequest(new { message = "Task ID is required." });

        try
        {
            var result = await _taskService.PreflightKillTaskAsync(taskId, cancellationToken);
            return Json(new
            {
                success = result.NominalStarts.Count > 0,
                taskId = result.TaskId,
                taskName = result.TaskName,
                runningCount = result.NominalStarts.Count,
                nominalStarts = result.NominalStarts,
                message = result.Message
            });
        }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    // Purpose: Kill task.
    [MoveItObjectPermission("Task", "Run", "TaskId")]
    public async Task<IActionResult> KillTask([FromBody] TaskKillRequest? request, CancellationToken cancellationToken)
    {
        var taskId = (request?.TaskId ?? string.Empty).Trim();
        var nominalStarts = request?.NominalStarts;
        if (taskId.Length == 0) return BadRequest(new { message = "Task ID is required." });
        if (nominalStarts is null || nominalStarts.Count == 0) return BadRequest(new { message = "No running task instances were selected to kill." });

        try
        {
            var result = await _taskService.KillTaskAsync(taskId, nominalStarts, cancellationToken);
            return Json(new
            {
                success = result.Failed == 0,
                taskId = result.TaskId,
                taskName = result.TaskName,
                killed = result.Killed,
                skipped = result.Skipped,
                failed = result.Failed,
                items = result.Items.Select(item => new
                {
                    nominalStart = item.NominalStart,
                    success = item.Success,
                    skipped = item.Skipped,
                    message = item.Message
                })
            });
        }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Runs one read-only Task Preflight stage so the browser can show real progress across the diagnostic pipeline.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PreflightStage([FromBody] TaskPreflightStageRequest? request, CancellationToken cancellationToken = default)
    {
        var taskId = (request?.TaskId ?? string.Empty).Trim();
        var stage = (request?.Stage ?? string.Empty).Trim();
        if (taskId.Length == 0) return BadRequest(new { message = "Task ID is required." });
        if (stage.Length == 0) return BadRequest(new { message = "Preflight stage is required." });
        try
        {
            _ = RequireSession();
            var result = await _taskPreflightService.RunStageAsync(taskId, stage, cancellationToken);
            return Json(new
            {
                success = true,
                taskId = result.TaskId,
                taskName = result.TaskName,
                stage = result.Stage,
                status = result.Status,
                checks = result.Checks.Select(check => new { status = check.Status, title = check.Title, message = check.Message })
            });
        }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Task Preflight stage {Stage} failed for Task {TaskId}.", stage, taskId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Task Preflight could not complete this check. Review Configuration > Logs for details." });
        }
    }

    // Returns the selected Task/dependency inventory and current Resource Group names without modifying MOVEit.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResourceGroupSetup([FromBody] ResourceGroupSetupRequest? request, CancellationToken cancellationToken = default)
    {
        try
        {
            if (RequireSession().Info?.IsAdmin != true) return StatusCode(403, new { message = "Only MOVEit administrators can manage Resource Groups." });
            var result = await _resourceGroupManagementService.PrepareForTasksAsync((IEnumerable<string>?)request?.TaskIds ?? Array.Empty<string>(), cancellationToken);
            return Json(new
            {
                success = true,
                groups = result.Groups.Select(group => new { name = group.Name, description = group.Description }),
                items = result.Items.Select(item => new { memberType = item.MemberType, type = item.Type, id = item.Id, name = item.Name })
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or MoveItApiException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Creates one new Resource Group containing all selected Tasks and their required Host/Script/Key/Certificate dependencies.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateResourceGroup([FromBody] ResourceGroupCreateRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null) return BadRequest(new { message = "Resource Group request is required." });
        try
        {
            if (RequireSession().Info?.IsAdmin != true) return StatusCode(403, new { message = "Only MOVEit administrators can manage Resource Groups." });
            var result = await _resourceGroupManagementService.CreateForTasksAsync(request.TaskIds, request.Name, request.Description, cancellationToken);
            return Json(new { success = true, groupName = result.GroupName, addedCount = result.AddedCount, existingCount = result.ExistingCount, totalMembers = result.TotalMembers });
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or MoveItApiException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Adds selected Tasks and all required dependencies to an existing Resource Group, silently suppressing duplicate memberships.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddToResourceGroup([FromBody] ResourceGroupAddRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null) return BadRequest(new { message = "Resource Group request is required." });
        try
        {
            if (RequireSession().Info?.IsAdmin != true) return StatusCode(403, new { message = "Only MOVEit administrators can manage Resource Groups." });
            var result = await _resourceGroupManagementService.AddTasksToGroupAsync(request.TaskIds, request.GroupName, cancellationToken);
            return Json(new { success = true, groupName = result.GroupName, addedCount = result.AddedCount, existingCount = result.ExistingCount, totalMembers = result.TotalMembers });
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or MoveItApiException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Returns Notifications modal setup for one or more selected Tasks without modifying MOVEit.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Task", "Edit", "TaskId,TaskIds")]
    public async Task<IActionResult> NotificationSetup([FromBody] BulkTaskRequest? request, CancellationToken cancellationToken = default)
    {
        var ids = (request?.TaskIds ?? new List<string>()).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ids.Count == 0) return BadRequest(new { message = "Select at least one Task." });
        try
        {
            _ = RequireSession();
            var tasksTask = _taskService.GetTasksAsync(cancellationToken);
            var hostsTask = _hostService.GetHostsAsync(cancellationToken);
            var templatesTask = _notificationTemplateService.GetCurrentAsync(cancellationToken);
            await Task.WhenAll(tasksTask, hostsTask, templatesTask);
            var byId = (await tasksTask).ToDictionary(task => task.Id, StringComparer.OrdinalIgnoreCase);
            var selected = ids.Select(id => byId.TryGetValue(id, out var task) ? task : null).ToList();
            var eligible = selected.Where(task => task is not null && task.Type.Equals("Traditional", StringComparison.OrdinalIgnoreCase)).Select(task => new { id = task!.Id, name = task.Name }).ToList();
            var skipped = selected.Where(task => task is not null && !task.Type.Equals("Traditional", StringComparison.OrdinalIgnoreCase)).Select(task => new { id = task!.Id, name = task.Name, reason = "Notifications are supported only for Traditional Tasks." }).ToList();
            var missing = ids.Where(id => !byId.ContainsKey(id)).Select(id => new { id, name = id, reason = "Task is no longer available. Refresh the grid." }).ToList();
            var smtpHosts = (await hostsTask).Where(host => host.Type.StartsWith("SMTP", StringComparison.OrdinalIgnoreCase)).OrderBy(host => host.Name, StringComparer.OrdinalIgnoreCase).Select(host => new { id = host.Id, name = host.Name }).ToList();

            TaskNotificationSnapshot? existing = null;
            if (ids.Count == 1 && eligible.Count == 1) existing = await _taskService.GetNotificationSnapshotAsync(ids[0], cancellationToken);
            var templates = (await templatesTask).Select(template => new
            {
                id = template.Id,
                name = template.Name,
                subject = template.Subject,
                message = template.Message,
                format = template.Format
            }).ToList();

            return Json(new
            {
                success = true,
                eligible,
                skipped = skipped.Concat(missing).ToList(),
                smtpHosts,
                templates,
                existing = existing is null ? null : new
                {
                    taskId = existing.TaskId,
                    taskName = existing.TaskName,
                    notifications = existing.Notifications
                }
            });
        }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unable to load Task notification setup for {UserName}.", User.Identity?.Name ?? "unknown");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Task notification settings could not be loaded. Review Configuration > Logs for details." });
        }
    }

    // Saves one reusable task-notification Subject and Message Body template in MAUI metadata for the current MOVEit environment.
    [Authorize(Policy = MauiSecurityPolicies.TemplateAdmin)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveNotificationTemplate([FromBody] TaskNotificationTemplateSaveRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null) return BadRequest(new { message = "Template data is required." });
        try
        {
            _ = RequireSession();
            var saved = await _notificationTemplateService.SaveCurrentAsync(request, cancellationToken);
            return Json(new
            {
                success = true,
                template = new
                {
                    id = saved.Id,
                    name = saved.Name,
                    subject = saved.Subject,
                    message = saved.Message,
                    format = saved.Format
                }
            });
        }
        catch (Exception ex) when (ex is MoveItAuthenticationException || (ex is InvalidOperationException or ArgumentException))
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unable to save a Task notification template for {UserName}.", User.Identity?.Name ?? "unknown");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "The notification template could not be saved. Review Configuration > Logs for details." });
        }
    }

    // Applies one Notifications change. Bulk UI calls this endpoint sequentially so progress is real and each Task gets its own Undo snapshot.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Task", "Edit", "TaskId,TaskIds")]
    public async Task<IActionResult> UpdateNotification([FromBody] TaskNotificationRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.TaskId)) return BadRequest(new { message = "Task ID is required." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();
        try
        {
            var snapshot = await _taskService.GetNotificationSnapshotAsync(request.TaskId, cancellationToken);
            if (!snapshot.TaskType.Equals("Traditional", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "Notifications can be managed only for Traditional Tasks." });
            var mode = (request.Mode ?? string.Empty).Trim();
            if (!mode.Equals("upsert", StringComparison.OrdinalIgnoreCase) && !mode.Equals("remove", StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { message = "Notification action must be Add/Update or Remove." });

            if (request.ActionIndex.HasValue && !snapshot.Notifications.Any(item => item.ActionIndex == request.ActionIndex.Value))
                return BadRequest(new { message = "The selected notification no longer exists. Close and reopen Edit Notifications, then try again." });

            if (mode.Equals("remove", StringComparison.OrdinalIgnoreCase))
            {
                if (snapshot.Notifications.Count == 0)
                    return Json(new { success = true, skipped = true, taskId = snapshot.TaskId, taskName = snapshot.TaskName, message = "No task-level Email notification exists." });

                if (!request.ActionIndex.HasValue)
                {
                    var hasSelectedResult = request.DoIfFailure || request.DoIfNoAction || request.DoIfSuccess;
                    if (!hasSelectedResult) return BadRequest(new { message = "Select at least one notification result: Failure, No Transfer, or Success." });
                    var hasExactMatch = snapshot.Notifications.Any(item =>
                        item.DoIfFailure == request.DoIfFailure &&
                        item.DoIfNoAction == request.DoIfNoAction &&
                        item.DoIfSuccess == request.DoIfSuccess);
                    if (!hasExactMatch)
                        return Json(new { success = true, skipped = true, taskId = snapshot.TaskId, taskName = snapshot.TaskName, message = "No matching task-level Email notification exists." });
                }
            }

            var hostId = (request.HostId ?? string.Empty).Trim();
            var addressTo = request.AddressTo ?? string.Empty;

            if (mode.Equals("upsert", StringComparison.OrdinalIgnoreCase))
            {
                if (!request.DoIfFailure && !request.DoIfNoAction && !request.DoIfSuccess)
                    return BadRequest(new { message = "Select at least one notification result: Failure, No Transfer, or Success." });

                var hasRecipient = addressTo
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Any(item => item.Length > 0);
                var updatesExistingNotification = request.ActionIndex.HasValue ||
                    (request.MatchExactResults && snapshot.Notifications.Any(item =>
                        item.DoIfFailure == request.DoIfFailure &&
                        item.DoIfNoAction == request.DoIfNoAction &&
                        item.DoIfSuccess == request.DoIfSuccess));
                var requiresRecipient = !request.PreserveExistingRecipients || !updatesExistingNotification;
                if (requiresRecipient && !hasRecipient) return BadRequest(new { message = "Enter at least one recipient." });
                if (string.IsNullOrWhiteSpace(request.Subject)) return BadRequest(new { message = "Subject is required." });
                if (string.IsNullOrWhiteSpace(request.Message)) return BadRequest(new { message = "Message Body is required." });

                var smtpHostExists = (await _hostService.GetHostsAsync(cancellationToken)).Any(host =>
                    string.Equals(host.Id, hostId, StringComparison.OrdinalIgnoreCase) &&
                    host.Type.StartsWith("SMTP", StringComparison.OrdinalIgnoreCase));
                if (!smtpHostExists) return BadRequest(new { message = "Select an available SMTP Host." });
            }

            await _taskUndoService.CaptureAsync(request.TaskId, session.Username, cancellationToken);
            var result = await _taskService.UpdateNotificationAsync(request.TaskId, new TaskNotificationUpdateRequest(
                mode, request.ActionIndex, request.MatchExactResults, request.DoIfFailure, request.DoIfNoAction, request.DoIfSuccess,
                request.IsHtml, hostId, addressTo, request.PreserveExistingRecipients, request.Subject, request.Message), cancellationToken);
            var warning = string.Empty;
            try
            {
                await _metadataRepository.RecordObjectConfigurationModifiedAsync(session.GetEnvironmentKey(), ObjectType, request.TaskId, session.Username, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Task notification update succeeded, but MAUI could not record modification metadata for Task {TaskId}.", request.TaskId);
                warning = "The MOVEit notification was updated, but MAUI could not fully update its local audit metadata.";
            }
            return Json(new { success = true, skipped = !result.Changed, taskId = result.TaskId, taskName = result.TaskName, message = result.Message, warning });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unable to update Task notification for Task {TaskId}.", request.TaskId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "The Task notification could not be updated. Review Configuration > Logs for details." });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    // Purpose: Enable the selected tasks through the shared bulk state-change path.
    [MoveItObjectPermission("Task", "Edit", "TaskIds")]
    public async Task<IActionResult> EnableTasks([FromBody] BulkTaskRequest? request, CancellationToken cancellationToken)
        => await SetTasksEnabled(request, true, cancellationToken);

    [HttpPost]
    [ValidateAntiForgeryToken]
    // Purpose: Disable the selected tasks through the shared bulk state-change path.
    [MoveItObjectPermission("Task", "Edit", "TaskIds")]
    public async Task<IActionResult> DisableTasks([FromBody] BulkTaskRequest? request, CancellationToken cancellationToken)
        => await SetTasksEnabled(request, false, cancellationToken);

    [HttpPost]
    [ValidateAntiForgeryToken]
    // Purpose: Validate task deletion dependencies before confirmation.
    [MoveItObjectPermission("Task", "Delete", "TaskIds")]
    public async Task<IActionResult> PreflightDeleteTasks([FromBody] BulkTaskRequest? request, CancellationToken cancellationToken)
    {
        var taskIds = request?.TaskIds;
        if (taskIds is null || taskIds.Count == 0) return BadRequest(new { message = "Select at least one task." });
        try
        {
            var result = await _taskService.PreflightDeleteTasksAsync(taskIds, cancellationToken);
            return Json(new
            {
                success = result.Blockers.Count == 0 && !result.HasCycle && string.IsNullOrWhiteSpace(result.Message),
                message = result.Message,
                hasCycle = result.HasCycle,
                targets = result.Targets.Select(item => new { taskId = item.TaskId, taskName = item.TaskName }),
                blockers = result.Blockers.Select(item => new
                {
                    taskId = item.TaskId,
                    taskName = item.TaskName,
                    referencedByTaskId = item.ReferencedByTaskId,
                    referencedByTaskName = item.ReferencedByTaskName
                })
            });
        }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    // Purpose: Delete tasks, capturing a MAUI Recycle Bin snapshot first when the feature is enabled.
    [MoveItObjectPermission("Task", "Delete", "TaskIds")]
    public async Task<IActionResult> DeleteTasks([FromBody] BulkTaskRequest? request, CancellationToken cancellationToken)
    {
        var taskIds = request?.TaskIds;
        if (taskIds is null || taskIds.Count == 0) return BadRequest(new { message = "Select at least one task." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();
        try
        {
            var recycleCapture = await _recycleBinService.CaptureForDeletionAsync(ObjectType, taskIds, session.Username, request?.DeleteWithoutRecycle == true, cancellationToken);
            if (recycleCapture.NeedsConfirmation && request?.DeleteWithoutRecycle != true)
            {
                return StatusCode(StatusCodes.Status409Conflict, new
                {
                    code = "RecycleCaptureFailed",
                    message = "MAUI could not create a recovery copy for one or more selected tasks. Delete anyway? Items deleted without a recovery copy cannot be restored from the Recycle Bin.",
                    failures = recycleCapture.Failures.Select(item => new { id = item.ObjectId, message = item.Message })
                });
            }

            var result = await _taskService.DeleteTasksAsync(taskIds, cancellationToken);
            var deletedIds = result.Items.Where(item => item.Success).Select(item => item.Id).ToList();
            var deletedIdSet = deletedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unusedRecycleIds = recycleCapture.RecycleIdsByObjectId
                .Where(pair => !deletedIdSet.Contains(pair.Key))
                .Select(pair => pair.Value)
                .ToList();
            if (unusedRecycleIds.Count > 0)
            {
                try { await _recycleBinService.DiscardCapturedAsync(unusedRecycleIds, cancellationToken); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { _logger.LogWarning(ex, "MAUI could not remove unused Recycle Bin snapshots after one or more Task deletes failed."); }
            }

            string metadataWarning = string.Empty;
            if (deletedIds.Count > 0)
            {
                var cleanupWarnings = new List<string>();
                try
                {
                    await _metadataRepository.DeleteObjectsAsync(session.GetEnvironmentKey(), ObjectType, deletedIds, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "MOVEit task deletion succeeded, but MAUI metadata cleanup failed for task IDs {TaskIds}.", string.Join(",", deletedIds));
                    cleanupWarnings.Add("MAUI could not remove all local folder/metadata records.");
                }

                try { await _taskUndoService.DeleteSnapshotsAsync(deletedIds, cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "MOVEit task deletion succeeded, but MAUI undo cleanup failed for task IDs {TaskIds}.", string.Join(",", deletedIds));
                    cleanupWarnings.Add("MAUI could not remove all local undo snapshots.");
                }

                if (cleanupWarnings.Count > 0)
                    metadataWarning = $"The MOVEit task deletion succeeded, but {string.Join(" ", cleanupWarnings)} Review the MAUI application log if stale local records remain.";
            }

            if (result.AuthenticationFailed) return MauiControllerExceptionMapper.SessionExpired();

            return Json(new
            {
                success = result.Failed == 0,
                succeeded = result.Succeeded,
                failed = result.Failed,
                warning = metadataWarning,
                items = result.Items.Select(item => new { id = item.Id, name = item.Name, success = item.Success, message = item.Message })
            });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Set tasks enabled.
    private async Task<IActionResult> SetTasksEnabled(BulkTaskRequest? request, bool enabled, CancellationToken cancellationToken)
    {
        var taskIds = request?.TaskIds;
        if (taskIds is null || taskIds.Count == 0) return BadRequest(new { message = "Select at least one task." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();

        try
        {
            var result = await _taskService.SetTasksEnabledAsync(taskIds, enabled, cancellationToken);
            var changedTaskIds = result.Items
                .Where(item => item.Success && !item.Skipped)
                .Select(item => item.Id)
                .ToList();
            if (changedTaskIds.Count > 0)
            {
                try
                {
                    await _metadataRepository.RecordObjectsConfigurationModifiedAsync(session.GetEnvironmentKey(), ObjectType, changedTaskIds, session.Username, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "MOVEit task state changed, but MAUI could not update Modified audit metadata for task IDs {TaskIds}.", string.Join(",", changedTaskIds));
                }
            }

            return Json(new
            {
                success = result.Failed == 0,
                succeeded = result.Succeeded,
                skipped = result.Skipped,
                failed = result.Failed,
                items = result.Items.Select(item => new { id = item.Id, name = item.Name, success = item.Success, skipped = item.Skipped, message = item.Message })
            });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Returns the current Task action state used by secondary Task views.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ActionState(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { message = "Task ID is required." });

        try
        {
            var task = await _taskService.GetActionStateAsync(id, cancellationToken);
            return Json(new
            {
                task = new
                {
                    id = task.Id,
                    name = task.Name,
                    enabled = task.Enabled,
                    runnable = task.Runnable,
                    // Compatibility alias for any already-loaded RC1 browser assets that still read canRun.
                    canRun = task.Runnable
                }
            });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Returns common MOVEit Task Information plus configurable local metadata and folder assignment.
    [HttpGet]
    public async Task<IActionResult> Details(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { message = "Task ID is required." });

        try
        {
            var task = await _taskService.GetBasicDetailsAsync(id, cancellationToken);
            var metadata = await _metadataRepository.GetObjectMetadataAsync(GetCurrentEnvironmentKey(), ObjectType, id, cancellationToken);
            var folders = FlattenFolders(await _metadataRepository.GetFoldersAsync(GetCurrentEnvironmentKey(), ObjectType, cancellationToken));

            return Json(new
            {
                editVersion = ObjectEditVersion.Create("TaskInformation", task.Id, ObjectEditVersion.TaskInformation(task.Name, task.Description, task.OperatorNotes)),
                task = new
                {
                    id = task.Id,
                    name = task.Name,
                    description = task.Description,
                    operatorNotes = task.OperatorNotes,
                    type = task.Type,
                    resourceGroups = task.ResourceGroups,
                    enabled = task.Enabled,
                    // COM is used as the authoritative permission check when Save is attempted.
                    canEditMoveItFields = true
                },
                metadata = metadata.Values,
                folderId = metadata.FolderId,
                fields = _metadataOptions.Tasks.Fields,
                folders,
                audit = new
                {
                    createdUtc = metadata.CreatedUtc,
                    createdBy = metadata.CreatedBy,
                    modifiedUtc = metadata.ModifiedUtc,
                    modifiedBy = metadata.ModifiedBy
                }
            });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }


    // Lazy-loads Task-to-Task relationships for the Task Information Tasks tab.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Dependencies(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { message = "Task ID is required." });

        try
        {
            var relationships = await _taskService.GetTaskDependenciesAsync(id, cancellationToken);
            return Json(new
            {
                calledBy = relationships.CalledBy.Select(task => new { id = task.Id, name = task.Name }),
                calls = relationships.Calls.Select(task => new { id = task.Id, name = task.Name })
            });
        }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Lazy-loads the recent Task Logs tab so no task-log REST traffic occurs until this endpoint is requested.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Logs(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { message = "Task ID is required." });

        try
        {
            var logs = await _taskService.GetTaskLogsAsync(id, cancellationToken);
            return Json(new
            {
                logs = logs.Select(log => new
                {
                    id = log.Id,
                    exitStatus = log.ExitStatus,
                    fallbackName = string.IsNullOrWhiteSpace(log.LogPath) ? string.Empty : Path.GetFileName(log.LogPath),
                    startTime = log.StartTime
                })
            });
        }
        catch (MoveItApiException ex)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Renders one retained MOVEit Task Log as escaped, read-only text in a MAUI-branded popup window.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> LogViewer(string id, long logId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id) || logId <= 0)
        {
            return View(new TaskLogViewerViewModel
            {
                TaskId = id ?? string.Empty,
                LogId = logId,
                ErrorMessage = "A valid Task ID and Task Log ID are required."
            });
        }

        try
        {
            var log = await _taskService.GetTaskLogAsync(id, logId, cancellationToken);
            return View(new TaskLogViewerViewModel
            {
                TaskId = log.TaskId,
                TaskName = log.TaskName,
                LogId = log.LogId,
                ExitStatus = log.ExitStatus,
                StartTime = log.StartTime,
                LogPath = log.LogPath,
                Content = log.Content
            });
        }
        catch (MoveItAuthenticationException ex)
        {
            return View(new TaskLogViewerViewModel
            {
                TaskId = id,
                LogId = logId,
                ErrorMessage = ex.Message
            });
        }
        catch (MoveItApiException ex)
        {
            return View(new TaskLogViewerViewModel
            {
                TaskId = id,
                LogId = logId,
                ErrorMessage = ex.Message
            });
        }
    }

    // Captures the current task configuration before the native MOVEit editor is allowed to open.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Task", "Edit", "TaskId")]
    public async Task<IActionResult> PrepareTaskEdit([FromBody] TaskActionRequest? request, CancellationToken cancellationToken)
    {
        var taskId = (request?.TaskId ?? string.Empty).Trim();
        if (taskId.Length == 0) return BadRequest(new { message = "Task ID is required." });

        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();

        try
        {
            var snapshot = await NativeEditorSnapshotRetry.CaptureAsync(
                token => _taskUndoService.CaptureAsync(taskId, session.Username, token),
                "Task", taskId, _logger, cancellationToken);
            return Json(new
            {
                success = true,
                taskId = snapshot.TaskId,
                taskName = snapshot.TaskName,
                snapshotCreatedUtc = snapshot.CreatedUtc
            });
        }
        catch (Exception ex) when (ex is MoveItAuthenticationException || (ex is MoveItComException or InvalidOperationException or ArgumentException))
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to capture an undo snapshot before opening MOVEit task {TaskId}.", taskId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "MAUI could not save the task undo snapshot. The editor was not opened." });
        }
    }

    // Checks whether the current user's saved snapshot can still be restored without overwriting a later MAUI user's work.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Task", "Edit", "TaskId")]
    public async Task<IActionResult> PreflightUndoTask([FromBody] TaskActionRequest? request, CancellationToken cancellationToken)
    {
        var taskId = (request?.TaskId ?? string.Empty).Trim();
        if (taskId.Length == 0) return BadRequest(new { message = "Task ID is required." });

        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();

        try
        {
            var result = await _taskUndoService.GetHistoryAsync(taskId, session.Username, cancellationToken);
            return Json(new
            {
                success = result.CanUndo,
                taskId = result.ObjectId,
                taskName = result.ObjectName,
                snapshots = result.Snapshots.Select(item => new
                {
                    snapshotId = item.SnapshotId,
                    snapshotCreatedUtc = item.CreatedUtc,
                    snapshotCreatedBy = item.CreatedBy,
                    canRestore = item.CanRestore,
                    message = item.Message
                }),
                message = result.Message
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to validate undo data for MOVEit task {TaskId}.", taskId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "MAUI could not validate the task undo data." });
        }
    }

    // Restores the current user's one-level snapshot after repeating the cross-user safety check.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Task", "Edit", "TaskId")]
    public async Task<IActionResult> UndoTask([FromBody] TaskActionRequest? request, CancellationToken cancellationToken)
    {
        var taskId = (request?.TaskId ?? string.Empty).Trim();
        if (taskId.Length == 0) return BadRequest(new { message = "Task ID is required." });

        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();

        var snapshotId = (request?.SnapshotId ?? string.Empty).Trim();
        if (snapshotId.Length == 0) return BadRequest(new { message = "Undo snapshot is required." });

        try
        {
            var result = await _taskUndoService.RestoreAsync(taskId, session.Username, snapshotId, cancellationToken);
            return Json(new
            {
                success = true,
                taskId = result.TaskId,
                taskName = result.TaskName,
                snapshotCreatedUtc = result.SnapshotCreatedUtc,
                warning = result.Warning
            });
        }
        catch (Exception ex) when (ex is MoveItAuthenticationException || (ex is MoveItComException or InvalidOperationException or ArgumentException))
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to restore the undo snapshot for MOVEit task {TaskId}.", taskId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "MAUI could not restore the task undo snapshot." });
        }
    }

    // Records that the native MOVEit editor was opened from MAUI. This updates only the MAUI
    // Modified audit fields; it does not imply that MAUI created the task or that MOVEit saved changes.
    [HttpPost]
    [ValidateAntiForgeryToken]
    // Purpose: Record local Modified audit values when MAUI opens the native MOVEit task editor.
    [MoveItObjectPermission("Task", "Edit", "TaskId")]
    public async Task<IActionResult> MarkTaskEdited([FromBody] TaskActionRequest? request, CancellationToken cancellationToken)
    {
        var taskId = (request?.TaskId ?? string.Empty).Trim();
        if (taskId.Length == 0) return BadRequest(new { message = "Task ID is required." });

        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();

        try
        {
            await _metadataRepository.RecordObjectConfigurationModifiedAsync(session.GetEnvironmentKey(), ObjectType, taskId, session.Username, cancellationToken);
            return Json(new { success = true });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to update MAUI modification audit metadata for Task {TaskId}.", taskId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "MAUI could not update the task modification audit information." });
        }
    }

    // Saves common MOVEit Task Information through the shared type-selected REST lifecycle, then saves local metadata/folder state.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Task", "Edit", "id")]
    public async Task<IActionResult> SaveDetails(string id, [FromBody] TaskDetailsUpdateRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { message = "Task ID is required." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();

        var moveItSaved = false;
        try
        {
            // Validate local values before committing the MOVEit definition.
            var values = await _metadataValueValidation.FilterAndValidateAsync(session.GetEnvironmentKey(), _metadataOptions.Tasks, request.Metadata, cancellationToken);
            if (request.UpdateMoveItFields)
            {
                using var edit = ObjectEditVersion.Require("TaskInformation", id.Trim(), request.EditVersion);
                await _taskService.UpdateBasicDetailsAsync(id, request.Name, request.Description, request.OperatorNotes, cancellationToken);
                moveItSaved = true;
                await _metadataRepository.RecordObjectConfigurationModifiedAsync(session.GetEnvironmentKey(), ObjectType, id, session.Username, cancellationToken);
            }

            await _metadataRepository.SaveObjectMetadataAsync(session.GetEnvironmentKey(), ObjectType, id, values, session.Username, cancellationToken);
            // Non-admin edits must preserve the current assignment, including when
            // an administrator moved the Task after this Information form loaded.
            if (session.Info?.IsAdmin == true)
                await _metadataRepository.AssignObjectToFolderAsync(session.GetEnvironmentKey(), ObjectType, id, request.FolderId, session.Username, cancellationToken);
            return Json(new { success = true });
        }
        catch (TraditionalTaskPartialSaveException ex)
        {
            _taskService.InvalidateCache();
            _logger.LogWarning(ex, "Task {TaskId} information has an unverified REST save outcome.", id);
            return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message, moveItSaved = true, outcomeUnknown = true });
        }
        catch (Exception ex) when (ex is ObjectEditConflictException or ObjectCheckoutReleaseException)
        {
            return Conflict(new { message = ex.Message, editConflict = true, moveItSaved = false });
        }
        catch (Exception ex) when (moveItSaved && ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Task {TaskId} information was saved but MAUI metadata/folder/audit updates did not complete.", id);
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "Task information was saved and verified in MOVEit, but MAUI could not finish its metadata/folder/audit updates. Reopen Task Information before retrying remaining changes.",
                moveItSaved = true
            });
        }
        catch (TaskNameAlreadyExistsException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Moves a task inside the local hierarchy. A null FolderId places it directly beneath All Tasks.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Task", "Edit", "TaskId")]
    public async Task<IActionResult> MoveTask([FromBody] TaskMoveRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TaskId)) return BadRequest(new { message = "Task ID is required." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();
        if (session.Info?.IsAdmin != true) return FolderAccessDenied();

        try
        {
            await _metadataRepository.AssignObjectToFolderAsync(session.GetEnvironmentKey(), ObjectType, request.TaskId, request.FolderId, session.Username, cancellationToken);
            return Json(new { success = true });
        }
        catch (InvalidOperationException ex)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Moves multiple selected tasks in one hierarchy operation. Used by Shift/Ctrl multi-select in the tree manager.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Task", "Edit", "TaskIds")]
    public async Task<IActionResult> MoveTasks([FromBody] TasksMoveRequest request, CancellationToken cancellationToken)
    {
        var taskIds = (request.TaskIds ?? new List<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (taskIds.Count == 0) return BadRequest(new { message = "At least one Task ID is required." });

        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();
        if (session.Info?.IsAdmin != true) return FolderAccessDenied();

        try
        {
            await _metadataRepository.AssignObjectsToFolderAsync(session.GetEnvironmentKey(), ObjectType, taskIds, request.FolderId, session.Username, cancellationToken);
            return Json(new { success = true, moved = taskIds.Count });
        }
        catch (InvalidOperationException ex)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    // Purpose: Create folder.
    public async Task<IActionResult> CreateFolder([FromBody] FolderCreateRequest request, CancellationToken cancellationToken)
        => await ExecuteFolderActionAsync(async () => await _metadataRepository.CreateFolderAsync(GetCurrentEnvironmentKey(), ObjectType, request.Name, request.ParentId, cancellationToken));

    [HttpPost]
    [ValidateAntiForgeryToken]
    // Purpose: Rename folder.
    public async Task<IActionResult> RenameFolder([FromBody] FolderRenameRequest request, CancellationToken cancellationToken)
        => await ExecuteFolderActionAsync(async () => { await _metadataRepository.RenameFolderAsync(GetCurrentEnvironmentKey(), ObjectType, request.FolderId, request.Name, cancellationToken); return true; });

    [HttpPost]
    [ValidateAntiForgeryToken]
    // Purpose: Move folder.
    public async Task<IActionResult> MoveFolder([FromBody] FolderMoveRequest request, CancellationToken cancellationToken)
        => await ExecuteFolderActionAsync(async () => { await _metadataRepository.MoveFolderAsync(GetCurrentEnvironmentKey(), ObjectType, request.FolderId, request.ParentId, cancellationToken); return true; });

    [HttpPost]
    [ValidateAntiForgeryToken]
    // Deletes a MAUI folder only after the repository confirms that it has no child folders or task assignments.
    public async Task<IActionResult> DeleteFolder([FromBody] FolderDeleteRequest request, CancellationToken cancellationToken)
    {
        // Treats All Tasks as a virtual root and has no database folder ID. Reject non-positive IDs explicitly as a second safety layer.
        if (request.FolderId <= 0) return BadRequest(new { message = "All Tasks cannot be deleted." });
        return await ExecuteFolderActionAsync(async () =>
        {
            await _metadataRepository.DeleteFolderAsync(GetCurrentEnvironmentKey(), ObjectType, request.FolderId, cancellationToken);
            return true;
        });
    }

    // Builds the read-only Upcoming Runs forecast grid.
    private GridViewModel BuildUpcomingRunsGrid(GridOptions options, IReadOnlyList<GridRow> rows) => new()
    {
        GridId = "upcomingruns",
        EntityName = "Runs",
        SearchPlaceholder = "Search task names, descriptions, schedules, or notes",
        Columns = new List<GridColumn>
        {
            new() { Key = "ScheduledTime", Label = "Scheduled Time", Type = "datetime", DefaultWidth = 200 },
            new() { Key = "TaskName", Label = "Task Name", DefaultWidth = 350 },
            new() { Key = "Description", Label = "Description", DefaultWidth = 300 },
            new() { Key = "Schedule", Label = "Schedule" },
            new() { Key = "Notes", Label = "Notes" },
            new() { Key = "TaskID", Label = "Task ID" },
            new() { Key = "ScheduleID", Label = "Schedule ID" }
        },
        Rows = rows,
        Options = options,
        SearchColumns = options.SearchColumns,
        FitColumnsToGrid = _gridBehaviorOptions.FitColumnsToGrid.Value,
        HighlightFailedTasksInGrid = false,
        SelectRowsBeyondGridSize = false,
        DefaultPageSize = _gridBehaviorOptions.DefaultRowSize.Value,
        PageSizeOptions = _gridBehaviorOptions.DefaultRowSize.PossibleValues,
        EnableRowSelection = false,
        ShowBulkActions = false,
        DefaultSortColumn = "ScheduledTime",
        DefaultSortDirection = "asc"
    };

    // Converts one calculated schedule occurrence into a stable read-only grid row.
    private GridRow BuildUpcomingRunRow(UpcomingRunOccurrence run)
    {
        var utc = run.ScheduledTime.ToUniversalTime();
        return new GridRow
        {
            Id = $"{run.TaskId}:{utc:yyyyMMddHHmm}",
            Values = new Dictionary<string, string>
            {
                ["ScheduledTime"] = run.ScheduledTime.ToString("MMM d, yyyy  h:mm tt", CultureInfo.InvariantCulture),
                ["TaskName"] = run.TaskName,
                ["Description"] = run.Description,
                ["Schedule"] = run.Schedule,
                ["Notes"] = run.Notes,
                ["TaskID"] = run.TaskId,
                ["ScheduleID"] = run.ScheduleId
            },
            RawValues = new Dictionary<string, string>
            {
                ["ScheduledTime"] = run.ScheduledTime.ToString("O", CultureInfo.InvariantCulture),
                ["TaskID"] = run.TaskId
            },
            InfoActionUrl = Url.Action(nameof(Details), "Tasks", new { id = run.TaskId }) ?? string.Empty
        };
    }

    // Purpose: Build grid.
    private GridViewModel BuildGrid(GridOptions options, IReadOnlyList<GridRow> rows, string filter = "all") => new()
    {
        GridId = "tasks",
        PreferenceKey = $"tasks:{NormalizeFilter(filter)}",
        EntityName = "Tasks",
        SearchPlaceholder = "Search task names, descriptions, filenames, resource groups, and metadata",
        Columns = BuildColumns(),
        Rows = rows,
        Options = options,
        SearchColumns = options.SearchColumns,
        FitColumnsToGrid = _gridBehaviorOptions.FitColumnsToGrid.Value,
        HighlightFailedTasksInGrid = _gridBehaviorOptions.HighlightFailedTasksInGrid.Value,
        SelectRowsBeyondGridSize = _gridBehaviorOptions.SelectRowsBeyondGridSize.Value,
        DefaultPageSize = _gridBehaviorOptions.DefaultRowSize.Value,
        PageSizeOptions = _gridBehaviorOptions.DefaultRowSize.PossibleValues,
        DefaultSortColumn = NormalizeFilter(filter) == "all" ? "Name" : "LastRun",
        DefaultSortDirection = NormalizeFilter(filter) == "all" ? "asc" : "desc"
    };

    // Adds configured task metadata fields and audit fields to the reusable grid without making them visible by default.
    private GridOptions BuildTaskGridOptions(GridOptions source, string filter)
    {
        var metadataColumnKeys = MetadataGridSupport.GetFieldColumnKeys(_metadataOptions.Tasks).ToList();

        var availableColumns = source.AvailableColumns
            .Concat(new[] { "Group" })
            .Concat(MetadataGridSupport.GetAvailableColumnKeys(_metadataOptions.Tasks))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var searchColumns = source.SearchColumns
            .Concat(new[] { "Group" })
            .Concat(metadataColumnKeys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var defaultColumns = source.DefaultColumns.ToList();
        if (NormalizeFilter(filter) != "all")
        {
            defaultColumns.RemoveAll(column => column.Equals("Schedule", StringComparison.OrdinalIgnoreCase));
        }

        return new GridOptions
        {
            DefaultColumns = defaultColumns,
            AvailableColumns = availableColumns,
            BulkActions = BuildTaskBulkActions(source.BulkActions),
            AddActions = source.AddActions.ToList(),
            SearchColumns = searchColumns
        };
    }

    // Adds core Task bulk actions even when an installed appsettings.json predates the feature.
    private List<string> BuildTaskBulkActions(IEnumerable<string> configuredActions)
    {
        var actions = configuredActions
            .Select(action => action.Equals("Notifications", StringComparison.OrdinalIgnoreCase) ? "Edit Notifications" : action)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        static void InsertBeforeExportOrDelete(List<string> target, string action)
        {
            if (target.Contains(action, StringComparer.OrdinalIgnoreCase)) return;
            var index = target.FindIndex(value => value.Equals("Export Tasks", StringComparison.OrdinalIgnoreCase) || value.Equals("Delete Tasks", StringComparison.OrdinalIgnoreCase));
            if (index < 0) target.Add(action);
            else target.Insert(index, action);
        }

        actions.RemoveAll(action => action.Equals("JSON Editor", StringComparison.OrdinalIgnoreCase));
        if (_uiOptions.TaskJsonEditor.Value) InsertBeforeExportOrDelete(actions, "JSON Editor");
        InsertBeforeExportOrDelete(actions, "Create Resource Group");
        InsertBeforeExportOrDelete(actions, "Add to Resource Group");
        return actions;
    }

    // Purpose: Apply task filter.
    private static IReadOnlyList<AutomationTask> ApplyTaskFilter(IReadOnlyList<AutomationTask> tasks, string filter) => NormalizeFilter(filter) switch
    {
        "running" => tasks.Where(task => task.Status == "Running").ToList(),
        "success" => tasks.Where(task => task.Status == "Success").ToList(),
        "failed" => tasks.Where(task => task.Status == "Failed").ToList(),
        "no-transfers" => tasks.Where(task => task.Status == "No Transfers").ToList(),
        _ => tasks
    };

    // Purpose: Normalize filter.
    private static string NormalizeFilter(string filter)
    {
        var value = (filter ?? string.Empty).Trim().ToLowerInvariant();
        return value is "running" or "success" or "failed" or "no-transfers" ? value : "all";
    }

    // Purpose: Build columns.
    private IReadOnlyList<GridColumn> BuildColumns()
    {
        var columns = new List<GridColumn>
        {
            new() { Key = "Id", Label = "Task ID" },
            new() { Key = "Name", Label = "Task Name", DefaultWidth = 350 },
            new() { Key = "Description", Label = "Description", DefaultWidth = 300 },
            new() { Key = "Type", Label = "Type", DefaultWidth = 115 },
            new() { Key = "Enabled", Label = "Enabled", Type = "boolean", DefaultWidth = 100 },
            new() { Key = "Status", Label = "Status", Type = "status", DefaultWidth = 130 },
            new() { Key = "Scheduled", Label = "Schedule Status", DefaultWidth = 115 },
            new() { Key = "Schedule", Label = "Schedule" },
            new() { Key = "LastRun", Label = "Last Run", Type = "datetime" },
            new() { Key = "LastResult", Label = "Last Result" },
            new() { Key = "Files", Label = "Files", Type = "number" },
            new() { Key = "Size", Label = "Size", Type = "number", DefaultWidth = 120 },
            new() { Key = "Group", Label = "Resource Group" }
        };

        MetadataGridSupport.AddColumns(columns, _metadataOptions.Tasks);
        return columns;
    }

    // Purpose: Build grid row.
    private GridRow BuildGridRow(AutomationTask task, ObjectMetadataSnapshot? metadata, bool hasUndo)
    {
        var values = new Dictionary<string, string>
        {
            ["Id"] = task.Id,
            ["Name"] = task.Name,
            ["Description"] = task.Description,
            ["Type"] = task.Type,
            ["Enabled"] = task.Enabled ? "Yes" : "No",
            ["Status"] = task.Status,
            ["Scheduled"] = task.Scheduled,
            ["Schedule"] = task.Schedule,
            ["LastRun"] = FormatDate(task.LastRun),
            ["LastResult"] = task.LastResult,
            ["Files"] = task.FilesSent?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["Size"] = FormatByteSize(task.TotalBytesSent),
            ["Group"] = task.Group
        };

        var rawValues = new Dictionary<string, string>
        {
            ["LastRun"] = task.LastRun?.ToString("O") ?? string.Empty,
            ["Files"] = task.FilesSent?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["Size"] = task.TotalBytesSent?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty,
            ["Enabled"] = task.Enabled ? "1" : "0",
            ["CanRun"] = task.CanRun ? "1" : "0",
            ["CanEdit"] = task.CanEditMoveItFields ? "1" : "0",
            ["CanDelete"] = task.CanAddEditDelete ? "1" : "0",
            ["Runnable"] = !task.Scheduled.Equals("Incomplete", StringComparison.OrdinalIgnoreCase) ? "1" : "0",
            ["Running"] = task.Status.Equals("Running", StringComparison.OrdinalIgnoreCase) ? "1" : "0",
            ["HasUndo"] = hasUndo ? "1" : "0",
            ["TaskType"] = task.Type
        };

        MetadataGridSupport.AddValues(values, rawValues, _metadataOptions.Tasks, metadata, FormatDate);

        return new GridRow
        {
            Id = task.Id,
            InfoActionUrl = Url.Action(nameof(Details), new { id = task.Id }) ?? string.Empty,
            Values = values,
            RawValues = rawValues
        };
    }


    // Purpose: Format byte size.
    private static string FormatByteSize(double? bytes)
    {
        if (!bytes.HasValue || bytes.Value < 0) return string.Empty;

        var value = bytes.Value;
        var units = new[] { "B", "KB", "MB", "GB", "TB", "PB" };
        var unitIndex = 0;
        while (value >= 1024d && unitIndex < units.Length - 1)
        {
            value /= 1024d;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{value:0} {units[unitIndex]}"
            : $"{value:0.##} {units[unitIndex]}";
    }

    // Purpose: Flatten the metadata folder hierarchy for UI selection controls.
    private static IReadOnlyList<TaskFolderOption> FlattenFolders(IReadOnlyList<MetadataFolderRecord> folders)
    {
        var rootFolders = folders
            .Where(folder => !folder.ParentId.HasValue)
            .OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var byParent = folders
            .Where(folder => folder.ParentId.HasValue)
            .GroupBy(folder => folder.ParentId!.Value)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase).ToList());

        var result = new List<TaskFolderOption>();
        var visited = new HashSet<int>();

        // Purpose: Append one folder and recursively flatten its descendants.
        void AppendFolder(MetadataFolderRecord folder, int depth, string parentPath)
        {
            if (!visited.Add(folder.Id)) return;

            var path = string.IsNullOrWhiteSpace(parentPath) ? folder.Name : $"{parentPath} / {folder.Name}";
            result.Add(new TaskFolderOption
            {
                Id = folder.Id,
                ParentId = folder.ParentId,
                Name = folder.Name,
                DisplayName = path,
                Depth = depth
            });

            if (!byParent.TryGetValue(folder.Id, out var children)) return;
            foreach (var child in children) AppendFolder(child, depth + 1, path);
        }

        foreach (var rootFolder in rootFolders) AppendFolder(rootFolder, 0, string.Empty);
        return result;
    }

    // Returns the current authenticated MOVEit session or raises the same authentication exception used by the API services.
    private MoveItSessionState RequireSession()
        => _sessionService.Get() ?? throw new MoveItAuthenticationException(MauiControllerExceptionMapper.SessionExpiredMessage);

    // Returns the normalized MAUI metadata scope for the current Automation environment.
    private string GetCurrentEnvironmentKey() => RequireSession().GetEnvironmentKey();

    // Purpose: Execute a folder operation and translate expected database errors into MVC responses.
    private ObjectResult FolderAccessDenied()
        => StatusCode(StatusCodes.Status403Forbidden, new { message = "Only MOVEit administrators can manage Task folders or change Task folder assignments." });

    private async Task<IActionResult> ExecuteFolderActionAsync<T>(Func<Task<T>> action)
    {
        try
        {
            if (RequireSession().Info?.IsAdmin != true) return FolderAccessDenied();
            var result = await action();
            return new JsonResult(new { success = true, result });
        }
        catch (Exception ex) when (ex is MoveItAuthenticationException or InvalidOperationException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
        catch (DbUpdateException ex)
        {
            return new BadRequestObjectResult(new { message = "The folder operation conflicts with an existing hierarchy item.", detail = ex.InnerException?.Message ?? ex.Message });
        }
    }

    // Purpose: Format date.
    private static string FormatDate(DateTimeOffset? value) => value?.ToLocalTime().ToString("MMM d, yyyy  h:mm tt") ?? "—";
}
