// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// First-class MAUI Report management workspace. "Reports" here means MAUI-created/scheduled Reports;
// MOVEit Automation's native report/history data remains under the MAUI Records workspace. Admins can
// manage all Report owners; normal MAUI Users are restricted to definitions they own.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Reporting;
using MoveItAutomationUI.Services.Lifecycle;
using MoveItAutomationUI.Services.Security;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Controllers;

[Authorize]
public sealed class ReportsController : Controller
{
    private const long MaxReportImportBytes = MauiPortablePackage.MaxPackageBytes;

    private readonly IOutboundReportService _reports;
    private readonly IOutboundReportPortabilityService _portability;
    private readonly IOutboundReportEmailService _email;
    private readonly IDataAccessCredentialService _dataAccessCredentials;
    private readonly IMauiSecurityService _security;
    private readonly UiOptions _uiOptions;
    private readonly GridBehaviorOptions _gridBehavior;

    public ReportsController(
        IOutboundReportService reports,
        IOutboundReportPortabilityService portability,
        IOutboundReportEmailService email,
        IDataAccessCredentialService dataAccessCredentials,
        IMauiSecurityService security,
        IOptionsSnapshot<UiOptions> uiOptions,
        IOptionsSnapshot<GridBehaviorOptions> gridBehavior)
    {
        _reports = reports;
        _portability = portability;
        _email = email;
        _dataAccessCredentials = dataAccessCredentials;
        _security = security;
        _uiOptions = uiOptions.Value;
        _gridBehavior = gridBehavior.Value;
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Index()
    {
        var actor = CurrentActor();
        if (!actor.IsAdmin) return RedirectToAction(nameof(My));
        return BuildReportsView("all", "All Reports", "Manage all saved MAUI Reports, schedules, delivery, and execution history.");
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult My()
        => BuildReportsView("mine", "My Reports", "Manage saved MAUI Reports that you created, including schedules, delivery, and execution history.");

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> List(string? scope, CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        var mineOnly = string.Equals(scope, "mine", StringComparison.OrdinalIgnoreCase);
        var reports = await _reports.ListAsync(actor.Username, actor.IsAdmin && !mineOnly, cancellationToken);
        var emailStatus = _email.GetConfigurationStatus();
        var missingDataAccessEnvironments = reports
            .Select(report => MoveItEnvironmentKey.Normalize(report.EnvironmentKey))
            .Where(environment => !string.IsNullOrWhiteSpace(environment))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(environment => !_dataAccessCredentials.IsEnvironmentConfigured(environment))
            .OrderBy(environment => environment, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Json(new
        {
            reports,
            readiness = new
            {
                smtpConfigured = emailStatus.IsConfigured,
                smtpMessage = emailStatus.Message,
                missingDataAccessEnvironments
            }
        });
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> History(string id, CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        try { return Json(new { history = await _reports.HistoryAsync(id, actor.Username, actor.IsAdmin, cancellationToken) }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update([FromBody] UpdateOutboundReportRequest? request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequest(new { message = "Report details are required." });
        var actor = CurrentActor();
        try { return Json(new { success = true, report = await _reports.UpdateAsync(request, actor.Username, actor.IsAdmin, cancellationToken) }); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> EditView(string id, string? returnScope, string? returnUrl, CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        try
        {
            var report = await _reports.GetEditDefinitionAsync(id, actor.Username, actor.IsAdmin, cancellationToken);
            if (report is null) return NotFound("The Report no longer exists or you do not have access to it.");

            // Existing Reports may contain a PathBase from their original IIS deployment.
            // Normalize against the Report grid, then resolve the route within THIS application.
            var source = OutboundReportSourceRoutes.Normalize(report.GridKey, report.SourceContext);
            var target = Url.Content("~" + source.Path);
            foreach (var pair in source.Query)
            {
                if (pair.Key.Equals("reportEdit", StringComparison.OrdinalIgnoreCase)
                    || pair.Key.Equals("savedView", StringComparison.OrdinalIgnoreCase)
                    || pair.Key.Equals("reportReturn", StringComparison.OrdinalIgnoreCase)
                    || pair.Key.Equals("reportReturnUrl", StringComparison.OrdinalIgnoreCase)
                    || pair.Key.Equals("gridReturnState", StringComparison.OrdinalIgnoreCase)) continue;
                target = QueryHelpers.AddQueryString(target, pair.Key, pair.Value ?? string.Empty);
            }
            target = QueryHelpers.AddQueryString(target, "reportEdit", report.Id);
            if (string.Equals(returnScope, "mine", StringComparison.OrdinalIgnoreCase))
                target = QueryHelpers.AddQueryString(target, "reportReturn", "mine");
            // A URL local to the website can still point outside this IIS application.
            // Keep the Reports return destination within the active application PathBase.
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
                && MauiApplicationReturnUrls.IsWithinApplication(returnUrl, Request.PathBase))
                target = QueryHelpers.AddQueryString(target, "reportReturnUrl", returnUrl);
            return LocalRedirect(target);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> EditDefinition(string id, CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        try
        {
            var report = await _reports.GetEditDefinitionAsync(id, actor.Username, actor.IsAdmin, cancellationToken);
            return report is null
                ? NotFound(new { message = "The Report no longer exists or you do not have access to it." })
                : Json(new { report });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateView([FromBody] UpdateOutboundReportViewRequest? request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequest(new { message = "Report view details are required." });
        var actor = CurrentActor();
        try
        {
            return Json(new { success = true, report = await _reports.UpdateViewAsync(request, actor.Username, actor.IsAdmin, cancellationToken) });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetEnabled([FromBody] SetOutboundReportEnabledRequest? request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequest(new { message = "Report identifier is required." });
        var actor = CurrentActor();
        try
        {
            var report = await _reports.SetEnabledAsync(request.Id, request.Enabled, actor.Username, actor.IsAdmin, cancellationToken);
            return report is null ? NotFound(new { message = "The Report no longer exists." }) : Json(new { success = true, report });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunNow([FromBody] OutboundReportIdRequest? request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequest(new { message = "Report identifier is required." });
        var actor = CurrentActor();
        try
        {
            var result = await _reports.RunSavedNowAsync(request.Id, actor.Username, actor.IsAdmin, cancellationToken);
            return result.Success ? Json(new { success = true, result }) : BadRequest(new { message = result.Message, result });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return BadRequest(new { message = ex.Message }); }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportReports([FromBody] BulkOutboundReportRequest? request, CancellationToken cancellationToken = default)
    {
        if (request is null) return BadRequest(new { message = "Select at least one Report to export." });
        var actor = CurrentActor();
        try
        {
            var export = await _portability.ExportAsync(request.ReportIds, actor.Username, actor.IsAdmin, cancellationToken);
            return File(export.Content, "application/zip", export.FileName);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxReportImportBytes + (1024 * 1024))]
    public async Task<IActionResult> PreflightImportReports([FromForm] OutboundReportImportPreflightRequest? request, CancellationToken cancellationToken = default)
    {
        var file = request?.File;
        var validation = ValidateReportImportFile(file);
        if (validation is not null) return validation;
        var actor = CurrentActor();
        try
        {
            return Json(new { result = await _portability.PreflightImportAsync(file!, actor.Username, cancellationToken) });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxReportImportBytes + (1024 * 1024))]
    public async Task<IActionResult> ImportReports([FromForm] OutboundReportImportRequest? request, CancellationToken cancellationToken = default)
    {
        var file = request?.File;
        var validation = ValidateReportImportFile(file);
        if (validation is not null) return validation;
        var actor = CurrentActor();
        try
        {
            return Json(new { success = true, result = await _portability.ImportAsync(file!, actor.Username, cancellationToken) });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteReports([FromBody] BulkOutboundReportRequest? request, CancellationToken cancellationToken = default)
    {
        var ids = (request?.ReportIds ?? new List<string>())
            .Select(value => (value ?? string.Empty).Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (ids.Length == 0) return BadRequest(new { message = "Select at least one Report to delete." });
        if (ids.Length > 500) return BadRequest(new { message = "Delete no more than 500 Reports at a time." });

        if (ids.Any(id => !Guid.TryParse(id, out _)))
            return BadRequest(new { message = "One or more Report identifiers are invalid." });

        var actor = CurrentActor();
        var visibleIds = (await _reports.ListAsync(actor.Username, actor.IsAdmin, cancellationToken))
            .Select(report => report.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (ids.Any(id => !visibleIds.Contains(id)))
            return NotFound(new { message = "One or more selected Reports no longer exist or you do not have access to them. Refresh the grid and try again." });

        var deleted = 0;
        foreach (var id in ids)
        {
            if (await _reports.DeleteAsync(id, actor.Username, actor.IsAdmin, cancellationToken)) deleted++;
        }
        return Json(new { success = true, deletedCount = deleted });
    }


    // Purpose: Applies the same .maui package boundary used by the other portability import endpoints.
    private IActionResult? ValidateReportImportFile(IFormFile? file)
    {
        if (file is null || file.Length <= 0) return BadRequest(new { message = "Select a MAUI Report export file." });
        if (file.Length > MaxReportImportBytes) return BadRequest(new { message = "The MAUI Report export exceeds the 50 MB import limit." });
        if (!string.Equals(Path.GetExtension(file.FileName), ".maui", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Report imports must use a .maui file." });
        return null;
    }


    private IActionResult BuildReportsView(string scope, string title, string description)
    {
        ViewData["ReportsScope"] = scope;
        ViewData["ReportsTitle"] = title;
        ViewData["ReportsDescription"] = description;

        // The browser receives an empty grid so the shared Loading shell is visible from first paint.
        // reports.js retrieves the authoritative database rows and releases the shell only after replacement.
        return View("Index", new ReportsPageViewModel { Grid = BuildEmptyGrid() });
    }

    // Purpose: Resolves the current MAUI user once per request. Report ownership is based on the
    // authenticated MOVEit username stored on the Report definition; MAUI Admins are allowed to
    // cross ownership boundaries while normal Users remain restricted to their own definitions.
    private (string Username, bool IsAdmin) CurrentActor()
    {
        var username = (User.Identity?.Name ?? string.Empty).Trim();
        if (username.Length == 0) throw new InvalidOperationException("The current MAUI user could not be determined.");
        return (username, _security.IsAdmin(username));
    }

    private GridViewModel BuildEmptyGrid()
    {
        var configured = _uiOptions.Grids.TryGetValue(ReportsGridDefaults.GridKey, out var value) ? value : ReportsGridDefaults.Create();
        var available = NormalizeColumns(configured.AvailableColumns, ReportsGridDefaults.AvailableColumns);
        var defaults = NormalizeColumns(configured.DefaultColumns, ReportsGridDefaults.DefaultColumns).Where(column => available.Contains(column, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (defaults.Length == 0) defaults = ReportsGridDefaults.DefaultColumns.Where(column => available.Contains(column, StringComparer.OrdinalIgnoreCase)).ToArray();
        var search = NormalizeColumns(configured.SearchColumns, ReportsGridDefaults.SearchColumns).Where(column => available.Contains(column, StringComparer.OrdinalIgnoreCase)).ToArray();

        var options = new GridOptions
        {
            AvailableColumns = available.ToList(),
            DefaultColumns = defaults.ToList(),
            SearchColumns = search.ToList(),
            AddActions = new List<string> { "Import Reports" },
            BulkActions = new List<string> { "Run Reports", "Export Reports", "Delete Reports" }
        };

        return new GridViewModel
        {
            GridId = ReportsGridDefaults.GridKey,
            EntityName = "Reports",
            SearchPlaceholder = "Search report names, sources, recipients, schedules, and status",
            Columns = new[]
            {
                new GridColumn { Key = "Id", Label = "ID" },
                new GridColumn { Key = "ReportName", Label = "Report Name" },
                new GridColumn { Key = "Description", Label = "Description", DefaultWidth = 300 },
                new GridColumn { Key = "Source", Label = "Source", DefaultWidth = 150 },
                new GridColumn { Key = "Environment", Label = "Environment" },
                new GridColumn { Key = "Recipients", Label = "Recipients" },
                new GridColumn { Key = "Schedule", Label = "Schedule" },
                new GridColumn { Key = "Enabled", Label = "Enabled", Type = "boolean", DefaultWidth = 120 },
                new GridColumn { Key = "NextRun", Label = "Next Run", Type = "datetime" },
                new GridColumn { Key = "LastRun", Label = "Last Run", Type = "datetime" },
                new GridColumn { Key = "LastStatus", Label = "Last Status", Type = "status" },
                new GridColumn { Key = "LastMessage", Label = "Last Message" },
                new GridColumn { Key = "Owner", Label = "Owner" },
                new GridColumn { Key = "Created", Label = "Created", Type = "datetime" },
                new GridColumn { Key = "Modified", Label = "Modified", Type = "datetime" }
            },
            Rows = Array.Empty<GridRow>(),
            Options = options,
            SearchColumns = options.SearchColumns,
            FitColumnsToGrid = _gridBehavior.FitColumnsToGrid.Value,
            SelectRowsBeyondGridSize = _gridBehavior.SelectRowsBeyondGridSize.Value,
            DefaultPageSize = _gridBehavior.DefaultRowSize.Value,
            PageSizeOptions = _gridBehavior.DefaultRowSize.PossibleValues,
            ShowBulkActions = true,
            ShowSavedViewActions = false,
            ShowCreateReportAction = false,
            DefaultSortColumn = "ReportName",
            DefaultSortDirection = "asc"
        };
    }

    private static IReadOnlyList<string> NormalizeColumns(IReadOnlyList<string>? configured, IReadOnlyList<string> fallback)
    {
        var source = configured is { Count: > 0 } ? configured : fallback;
        return source
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
