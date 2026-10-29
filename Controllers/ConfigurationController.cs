// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.Backup;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Hosts;
using System.Net;
using System.Text;
using MoveItAutomationUI.Services.Metadata;

namespace MoveItAutomationUI.Controllers;

[Authorize]
public sealed partial class ConfigurationController : Controller
{
    private const long MaxConfigurationImportBytes = 50L * 1024L * 1024L;
    private const long MaxMetadataImportBytes = 10L * 1024L * 1024L;

    private readonly IAppSettingsConfigurationService _settingsService;
    private readonly IAutomationConfigurationService _automationConfigurationService;
    private readonly IMoveItSessionService _sessionService;
    private readonly IDatabaseConnectionTester _databaseTester;
    private readonly IApplicationLogRepository _applicationLogRepository;
    private readonly IMauiBackupService _backupService;
    private readonly IDataAccessCredentialService _dataAccessCredentials;
    private readonly IMetadataWorkbookService _metadataWorkbookService;
    private readonly ITaskNotificationTemplateService _notificationTemplateService;
    private readonly IMoveItServerCatalog _servers;
    private readonly UiOptions _uiOptions;
    private readonly GridBehaviorOptions _gridBehavior;
    private readonly ILogger<ConfigurationController> _logger;

    // Purpose: Initializes configuration controller with its required dependencies.
    public ConfigurationController(
        IAppSettingsConfigurationService settingsService,
        IAutomationConfigurationService automationConfigurationService,
        IMoveItSessionService sessionService,
        IDatabaseConnectionTester databaseTester,
        IApplicationLogRepository applicationLogRepository,
        IMauiBackupService backupService,
        IDataAccessCredentialService dataAccessCredentials,
        IMetadataWorkbookService metadataWorkbookService,
        ITaskNotificationTemplateService notificationTemplateService,
        IMoveItServerCatalog servers,
        IOptionsSnapshot<UiOptions> uiOptions,
        IOptionsSnapshot<GridBehaviorOptions> gridBehavior,
        ILogger<ConfigurationController> logger)
    {
        _settingsService = settingsService;
        _automationConfigurationService = automationConfigurationService;
        _sessionService = sessionService;
        _databaseTester = databaseTester;
        _applicationLogRepository = applicationLogRepository;
        _backupService = backupService;
        _dataAccessCredentials = dataAccessCredentials;
        _metadataWorkbookService = metadataWorkbookService;
        _notificationTemplateService = notificationTemplateService;
        _servers = servers;
        _uiOptions = uiOptions.Value;
        _gridBehavior = gridBehavior.Value;
        _logger = logger;
    }

    // Displays the MAUI application configuration workspace. Categories inside the page keep the main sidebar compact.
    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Settings()
    {
        ViewData["IsMoveItAdmin"] = IsMoveItAdmin();
        return View(_settingsService.GetSettings());
    }


    // Displays MAUI-owned ILogger events persisted in the metadata database.
    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Logs(CancellationToken cancellationToken = default)
    {
        var items = await _applicationLogRepository.GetAsync(cancellationToken);
        var options = _uiOptions.Grids.TryGetValue(ApplicationLogGridDefaults.GridKey, out var configured)
            ? configured
            : ApplicationLogGridDefaults.Create();

        var columns = new[]
        {
            new GridColumn { Key = "Id", Label = "ID", Type = "number" },
            new GridColumn { Key = "Timestamp", Label = "Timestamp", Type = "datetime", DefaultWidth = 150 },
            new GridColumn { Key = "Level", Label = "Level", DefaultWidth = 150 },
            new GridColumn { Key = "User", Label = "User", DefaultWidth = 200 },
            new GridColumn { Key = "Environment", Label = "Environment" },
            new GridColumn { Key = "Category", Label = "Category" },
            new GridColumn { Key = "EventId", Label = "Event ID", Type = "number" },
            new GridColumn { Key = "EventName", Label = "Event Name" },
            new GridColumn { Key = "RequestPath", Label = "Request Path" },
            new GridColumn { Key = "TraceId", Label = "Trace ID" },
            new GridColumn { Key = "Message", Label = "Message" },
            new GridColumn { Key = "Exception", Label = "Exception" },
            new GridColumn { Key = "Properties", Label = "Properties" }
        };

        var rows = items.Select(item =>
        {
            var timestampUtc = item.TimestampUtc.Kind == DateTimeKind.Utc
                ? item.TimestampUtc
                : DateTime.SpecifyKind(item.TimestampUtc, DateTimeKind.Utc);
            return new GridRow
            {
                Id = item.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Id"] = item.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["Timestamp"] = timestampUtc.ToLocalTime().ToString("M/d/yyyy h:mm:ss tt", System.Globalization.CultureInfo.CurrentCulture),
                    ["Level"] = item.Level,
                    ["User"] = item.Username,
                    ["Environment"] = item.EnvironmentKey,
                    ["Category"] = item.Category,
                    ["EventId"] = item.EventId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["EventName"] = item.EventName,
                    ["RequestPath"] = item.RequestPath,
                    ["TraceId"] = item.TraceId,
                    ["Message"] = item.Message,
                    ["Exception"] = item.Exception,
                    ["Properties"] = item.PropertiesJson
                },
                RawValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Id"] = item.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["Timestamp"] = timestampUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
                }
            };
        }).ToArray();

        var pageSize = _gridBehavior.DefaultRowSize.Value;
        if (!GridBehaviorOptions.SupportedDefaultRowSizes.Contains(pageSize)) pageSize = 50;

        return View(new GridViewModel
        {
            GridId = ApplicationLogGridDefaults.GridKey,
            EntityName = "Log Entries",
            SearchPlaceholder = "Search logs",
            Columns = columns,
            Rows = rows,
            Options = options,
            SearchColumns = options.SearchColumns,
            FitColumnsToGrid = _gridBehavior.FitColumnsToGrid.Value,
            SelectRowsBeyondGridSize = false,
            DefaultPageSize = pageSize,
            EnableRowSelection = false,
            ShowBulkActions = false,
            DefaultSortColumn = "Timestamp",
            DefaultSortDirection = "desc"
        });
    }

    // Persists administrator-editable settings. Application configuration is stored in appsettings.json; MAUI Security users are stored in the metadata database. Startup-sensitive changes remain pending until restart.
    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSettings([FromBody] ConfigurationSettingsSaveRequest? request, CancellationToken cancellationToken,
        [FromServices] IHostCrudCoordinator? hostCrud = null)
    {
        if (request is null) return BadRequest(new { message = "Configuration values are required." });
        try
        {
            if (request.SshAlgorithms is { } ssh && (ssh.Encryption is not null || ssh.KeyExchange is not null))
            {
                if (hostCrud is null) throw new InvalidOperationException("SSH algorithm discovery is unavailable.");
                var catalog = await hostCrud.GetSshAlgorithmsAsync(cancellationToken);
                ValidateSshSelection(ssh.Encryption, catalog.Encryption);
                ValidateSshSelection(ssh.KeyExchange, catalog.KeyExchange);
            }
            var userName = User.Identity?.Name ?? "unknown";
            var result = await _settingsService.SaveAsync(request, userName, cancellationToken);
            _logger.LogInformation("MAUI configuration saved by {UserName}. Restart required: {RestartRequired}.", userName, result.RestartRequired);
            return Json(new
            {
                success = true,
                restartRequired = result.RestartRequired,
                restartReasons = result.RestartReasons,
                smtpPasswordConfigured = result.SmtpPasswordConfigured,
                configurationRevision = result.ConfigurationRevision,
                clearedDataAccessServerKeys = result.ClearedDataAccessServerKeys,
                message = result.RestartRequired
                    ? "Configuration saved. One or more changes require an application restart."
                    : "Configuration saved."
            });
        }
        catch (MoveItAuthenticationException) { throw; }
        catch (MoveItApiException)
        {
            return BadRequest(new { message = "Unable to verify supported SSH/KEX algorithms. Configuration was not saved. Reload Settings and try again." });
        }
        catch (ConfigurationRevisionConflictException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or Microsoft.Data.Sqlite.SqliteException)
        {
            return BadRequest(new { message = $"Database operation failed: {ex.Message}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to save MAUI application configuration.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "The configuration could not be saved. Review the application log for details." });
        }
    }


    // Tests staged database values without changing the active DbContext or appsettings.json.
    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestDatabase([FromBody] DatabaseConnectionTestRequest? request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequest(new { message = "Database configuration is required." });
        try
        {
            var result = await _databaseTester.TestAsync(request.Provider, request.ConnectionString, cancellationToken);
            return Json(new
            {
                success = true,
                message = result.Message,
                result.Provider,
                result.Database,
                result.DataSource,
                result.SafeForMauiInitialization,
                result.IsEmpty,
                result.IsMauiDatabase,
                result.UserTableCount
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MAUI database connection test failed for {UserName}.", User.Identity?.Name ?? "unknown");
            return BadRequest(new { message = ex.Message });
        }
    }

    // Saves or updates the protected unattended Data Access Credentials for one configured MOVEit server.
    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveDataAccessCredentials([FromBody] DataAccessCredentialSaveRequest? request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequest(new { message = "Data Access credential values are required." });
        try
        {
            var configurationRevision = await _dataAccessCredentials.SaveAsync(request.ServerKey, request.Username, request.Password, cancellationToken);
            return Json(new
            {
                success = true,
                message = "Data Access Credentials saved.",
                configurationRevision,
                serverKey = request.ServerKey?.Trim() ?? string.Empty,
                username = request.Username?.Trim() ?? string.Empty,
                configured = true
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to save Data Access Credentials for MOVEit server {ServerKey}.", request.ServerKey);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Data Access Credentials could not be saved. Review Configuration > Logs for details." });
        }
        finally
        {
            request.Password = string.Empty;
        }
    }

    // Validates the exact server-specific Data Access Credentials and confirms MOVEit Administrator access.
    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestDataAccessCredentials([FromBody] DataAccessServerRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ServerKey))
            return BadRequest(new { message = "MOVEit Server is required." });

        try
        {
            var result = await _dataAccessCredentials.TestAsync(request.ServerKey, cancellationToken);
            if (!result.Success) return BadRequest(new { message = result.Message });
            return Json(new { success = true, message = result.Message });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or MoveItApiException)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // Clears the protected Data Access Credentials for one configured MOVEit server.
    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearDataAccessCredentials([FromBody] DataAccessServerRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ServerKey))
            return BadRequest(new { message = "MOVEit Server is required." });
        try
        {
            var serverKey = request.ServerKey.Trim();
            var configurationRevision = await _dataAccessCredentials.ClearAsync(serverKey, cancellationToken);
            return Json(new { success = true, message = "Data Access Credentials cleared.", configurationRevision, serverKey, configured = false });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to clear Data Access Credentials for MOVEit server {ServerKey}.", request.ServerKey);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Data Access Credentials could not be cleared. Review Configuration > Logs for details." });
        }
    }

    // Returns unattended Data Access readiness for all configured MOVEit environments.
    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult DataAccessReadiness()
    {
        var servers = _servers.GetServers();
        var missingDataAccessEnvironments = servers
            .Select(MoveItEnvironmentKey.Resolve)
            .Where(environment => !string.IsNullOrWhiteSpace(environment))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(environment => !_dataAccessCredentials.IsEnvironmentConfigured(environment))
            .OrderBy(environment => environment, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var missingDataAccessServers = servers
            .Where(server => !_dataAccessCredentials.IsConfigured(server.Key, out _))
            .Select(server => string.IsNullOrWhiteSpace(server.DisplayName) ? server.Key : server.DisplayName.Trim())
            .Where(serverName => !string.IsNullOrWhiteSpace(serverName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(serverName => serverName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Json(new { missingDataAccessEnvironments, missingDataAccessServers });
    }

    // Runs the same backup pipeline used by the hidden Backup System Job immediately.
    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BackupNow(CancellationToken cancellationToken)
    {
        var user = User.Identity?.Name ?? "unknown";
        var currentSession = _sessionService.Get();
        if (currentSession is null)
            return Unauthorized(new { message = "Your MOVEit session has expired. Please sign in again before running a manual backup." });

        var result = await _backupService.RunManualAsync(user, currentSession, cancellationToken);
        if (!result.Success)
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = result.Messages.LastOrDefault() ?? "Backup failed. Review Configuration > Logs for details.",
                folderPath = result.FolderPath,
                messages = result.Messages
            });

        return Json(new
        {
            success = true,
            message = "Backup completed successfully.",
            folderPath = result.FolderPath,
            messages = result.Messages
        });
    }

    // Lists MAUI backup folders available for download, removal, or guarded restore.
    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Backups()
    {
        var backups = _backupService.ListBackups();
        return Json(new { backups });
    }

    // Packages one backup folder as a ZIP download without exposing filesystem paths to the browser.
    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> DownloadBackup(string backupId, CancellationToken cancellationToken)
    {
        try
        {
            var archivePath = await _backupService.CreateDownloadArchiveAsync(backupId, cancellationToken);
            var fileName = $"MAUI_Backup_{Path.GetFileName(backupId)}.zip";
            var stream = new FileStream(
                archivePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 128,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            return File(stream, "application/zip", fileName);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to create MAUI backup download {BackupId} for {UserName}.", backupId, User.Identity?.Name ?? "unknown");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "The backup could not be packaged for download. Review Configuration > Logs for details." });
        }
    }

    // Removes one selected MAUI backup folder.
    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveBackup([FromBody] MauiBackupActionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            request ??= new MauiBackupActionRequest();
            await _backupService.RemoveBackupAsync(request.BackupId, User.Identity?.Name ?? "unknown", cancellationToken);
            return Json(new { success = true, message = "Backup removed." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "MAUI backup {BackupId} could not be removed for {UserName}.", request?.BackupId, User.Identity?.Name ?? "unknown");
            return Conflict(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to remove MAUI backup {BackupId} for {UserName}.", request?.BackupId, User.Identity?.Name ?? "unknown");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "The backup could not be removed. Review Configuration > Logs for details." });
        }
    }

    // Restores MAUI configuration/SQLite state and explicitly selected Server-Key-protected MOVEit exports.
    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreBackup([FromBody] MauiBackupRestoreRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) return BadRequest(new { message = "The restore options are invalid. Reopen Restore Backup and select again." });
        request ??= new MauiBackupRestoreRequest();
        if (!request.Acknowledged)
            return BadRequest(new { message = "Acknowledge that the restore replaces the current MAUI configuration and supported metadata database state." });
        if (!string.Equals(request.ConfirmationText?.Trim(), "RESTORE", StringComparison.Ordinal))
            return BadRequest(new { message = "Type RESTORE to confirm this operation." });

        var currentSession = _sessionService.Get();

        try
        {
            var result = await _backupService.RestoreBackupAsync(
                request.BackupId,
                request.MoveItEnvironments,
                User.Identity?.Name ?? "unknown",
                currentSession,
                cancellationToken);
            return Json(new
            {
                success = result.Success,
                restartScheduled = result.RestartScheduled,
                message = result.RestartScheduled
                    ? "Backup restored successfully. MAUI is restarting; sign in again after the application returns."
                    : "Backup restored successfully. Restart MAUI manually before continuing.",
                messages = result.Messages
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (MauiBackupRestoreOutcomeUnknownException ex)
        {
            _logger.LogError(ex,
                "MAUI backup {BackupId} has an unknown MOVEit configuration-restore outcome for {UserName}. Verify MOVEit before retrying.",
                request.BackupId, User.Identity?.Name ?? "unknown");
            return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message, outcomeUnknown = true });
        }
        catch (MoveItApiException ex)
        {
            _logger.LogWarning(ex, "MOVEit configuration restore failed from MAUI backup {BackupId} for {UserName}.", request.BackupId, User.Identity?.Name ?? "unknown");
            return StatusCode(ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : StatusCodes.Status502BadGateway, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "MAUI backup restore was rejected for {BackupId} by {UserName}.", request.BackupId, User.Identity?.Name ?? "unknown");
            return Conflict(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected MAUI backup restore failure for {BackupId} by {UserName}.", request.BackupId, User.Identity?.Name ?? "unknown");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "The backup restore did not complete. Review Configuration > Logs before retrying." });
        }
    }

    // Exports the complete MOVEit Automation configuration through the documented v1 Config API.
    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportMoveItConfiguration([FromBody] MoveItConfigurationExportRequest request, CancellationToken cancellationToken)
    {
        if (!IsMoveItAdmin()) return StatusCode(StatusCodes.Status403Forbidden, new { message = "MOVEit Administrator access is required to export the Automation configuration." });

        try
        {
            request ??= new MoveItConfigurationExportRequest();
            if (string.Equals(request.Mode, MoveItConfigurationExportModes.Passphrase, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(request.Passphrase, request.ConfirmPassphrase, StringComparison.Ordinal))
                return BadRequest(new { message = "The export passphrase and confirmation do not match." });

            var xml = await _automationConfigurationService.ExportAsync(request.Mode, request.Passphrase, cancellationToken);
            var state = _sessionService.Get();
            var environment = SanitizeFileNamePart(state?.GetEnvironmentKey() ?? "MOVEit");
            var suffix = string.Equals(request.Mode, MoveItConfigurationExportModes.Sanitized, StringComparison.OrdinalIgnoreCase) ? "_Sanitized" : string.Empty;
            var fileName = $"MOVEitAutomation_Config_{environment}_{DateTime.Now:yyyyMMdd_HHmmss}{suffix}.xml";

            _logger.LogInformation("MOVEit configuration exported by {UserName} using mode {Mode}.", User.Identity?.Name ?? "unknown", request.Mode);
            return File(Encoding.UTF8.GetBytes(xml), "application/xml; charset=utf-8", fileName);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(
                "MOVEit configuration export validation failed for {UserName}. Mode={Mode}; Error={Error}.",
                User.Identity?.Name ?? "unknown", request?.Mode ?? string.Empty, ex.Message);
            return BadRequest(new { message = ex.Message });
        }
        catch (MoveItApiException ex)
        {
            _logger.LogWarning(ex, "MOVEit configuration export failed for {UserName}.", User.Identity?.Name ?? "unknown");
            return StatusCode(ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : StatusCodes.Status502BadGateway, new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected MOVEit configuration export failure for {UserName}.", User.Identity?.Name ?? "unknown");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "The MOVEit configuration could not be exported. Review the application log for details." });
        }
    }

    // Imports a complete MOVEit Automation configuration. This is intentionally guarded by both MAUI and MOVEit Admin checks.
    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxConfigurationImportBytes + (1024 * 1024))]
    public async Task<IActionResult> ImportMoveItConfiguration(
        [FromForm] IFormFile? file,
        [FromForm] string mode,
        [FromForm] string passphrase,
        [FromForm] bool acknowledged,
        CancellationToken cancellationToken)
    {
        if (!IsMoveItAdmin()) return StatusCode(StatusCodes.Status403Forbidden, new { message = "MOVEit Administrator access is required to import the Automation configuration." });
        if (!acknowledged) return BadRequest(new { message = "Confirm that you understand the current MOVEit Automation configuration will be overwritten." });
        if (file is null || file.Length <= 0) return BadRequest(new { message = "Select a MOVEit Automation configuration XML file to import." });
        if (file.Length > MaxConfigurationImportBytes) return BadRequest(new { message = "The configuration file exceeds the 50 MB import limit." });
        if (!string.Equals(Path.GetExtension(file.FileName), ".xml", StringComparison.OrdinalIgnoreCase)) return BadRequest(new { message = "MOVEit configuration imports must use an .xml file." });

        var importCommitStarted = false;
        try
        {
            string configXml;
            await using (var stream = file.OpenReadStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                configXml = await reader.ReadToEndAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(configXml) || !configXml.TrimStart().StartsWith('<'))
                return BadRequest(new { message = "The selected file does not appear to contain MOVEit configuration XML." });

            // The native MOVEit import is destructive. Honor browser/request cancellation while reading and validating
            // the upload, but once the commit begins do not let a disconnected browser cancel the upstream operation
            // and create an ambiguous "did MOVEit import it?" state.
            cancellationToken.ThrowIfCancellationRequested();
            importCommitStarted = true;
            await _automationConfigurationService.ImportAsync(mode, configXml, passphrase, CancellationToken.None);
            _logger.LogWarning("MOVEit configuration imported by {UserName} from file {FileName} using mode {Mode}. Resource caches were cleared.",
                User.Identity?.Name ?? "unknown", Path.GetFileName(file.FileName), mode);

            return Json(new
            {
                success = true,
                message = "MOVEit configuration imported successfully. MAUI resource caches were cleared; refresh open resource pages before continuing."
            });
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(
                "MOVEit configuration import validation failed for {UserName}. File={FileName}; PackageBytes={PackageBytes}; Mode={Mode}; Error={Error}.",
                User.Identity?.Name ?? "unknown", Path.GetFileName(file.FileName), file.Length, mode, ex.Message);
            return BadRequest(new { message = ex.Message });
        }
        catch (MoveItAuthenticationException ex)
        {
            _logger.LogWarning(ex,
                "MOVEit configuration import authentication failed for {UserName}. File={FileName}; PackageBytes={PackageBytes}; Mode={Mode}.",
                User.Identity?.Name ?? "unknown", Path.GetFileName(file.FileName), file.Length, mode);
            return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        }
        catch (MoveItApiException ex) when (importCommitStarted
            && (!ex.StatusCode.HasValue
                || ex.StatusCode == HttpStatusCode.RequestTimeout
                || (int)ex.StatusCode.Value >= 500))
        {
            _logger.LogError(ex,
                "MOVEit configuration import returned an ambiguous upstream result for {UserName}. File={FileName}; PackageBytes={PackageBytes}; Mode={Mode}. The native import may have completed; verify MOVEit before retrying.",
                User.Identity?.Name ?? "unknown", Path.GetFileName(file.FileName), file.Length, mode);
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                outcomeUnknown = true,
                message = "MOVEit did not return a definitive configuration-import result. The import may have completed. Verify the active MOVEit Automation configuration before attempting another import."
            });
        }
        catch (OperationCanceledException ex) when (importCommitStarted)
        {
            _logger.LogError(ex,
                "MOVEit configuration import timed out after the native commit began for {UserName}. File={FileName}; PackageBytes={PackageBytes}; Mode={Mode}. The native import outcome is unknown.",
                User.Identity?.Name ?? "unknown", Path.GetFileName(file.FileName), file.Length, mode);
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                outcomeUnknown = true,
                message = "MOVEit did not return a definitive configuration-import result. The import may have completed. Verify the active MOVEit Automation configuration before attempting another import."
            });
        }
        catch (HttpRequestException ex) when (importCommitStarted)
        {
            _logger.LogError(ex,
                "MOVEit configuration import lost the upstream connection after the native commit began for {UserName}. File={FileName}; PackageBytes={PackageBytes}; Mode={Mode}. The native import outcome is unknown.",
                User.Identity?.Name ?? "unknown", Path.GetFileName(file.FileName), file.Length, mode);
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                outcomeUnknown = true,
                message = "MOVEit did not return a definitive configuration-import result. The import may have completed. Verify the active MOVEit Automation configuration before attempting another import."
            });
        }
        catch (MoveItApiException ex)
        {
            _logger.LogWarning(ex,
                "MOVEit configuration import failed for {UserName}. File={FileName}; PackageBytes={PackageBytes}; Mode={Mode}.",
                User.Identity?.Name ?? "unknown", Path.GetFileName(file.FileName), file.Length, mode);
            return StatusCode(ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : StatusCodes.Status502BadGateway, new { message = ex.Message });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && !importCommitStarted)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected MOVEit configuration import failure for {UserName}.", User.Identity?.Name ?? "unknown");
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "The MOVEit configuration could not be imported. Review the application log for details." });
        }
    }

    // Purpose: Returns whether move it admin.
    private bool IsMoveItAdmin() => _sessionService.Get()?.Info?.IsAdmin == true;

    // Purpose: Sanitizes file name part.
    private static string SanitizeFileNamePart(string value)
    {
        var safe = new string((value ?? string.Empty).Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_').ToArray()).Trim('_');
        return safe.Length == 0 ? "MOVEit" : safe;
    }

}
