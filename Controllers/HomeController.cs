// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Controllers;

[Authorize]
public sealed class HomeController : Controller
{
    private readonly IAutomationDashboardService _dashboardService;
    private readonly IAutomationKeyCertificateService _keyCertificateService;
    private readonly IMoveItSessionService _sessionService;

    // Purpose: Initializes home controller with its required dependencies.
    public HomeController(
        IAutomationDashboardService dashboardService,
        IAutomationKeyCertificateService keyCertificateService,
        IMoveItSessionService sessionService)
    {
        _dashboardService = dashboardService;
        _keyCertificateService = keyCertificateService;
        _sessionService = sessionService;
    }

    // The dashboard shell renders immediately. Data sources load independently through dashboard.js.
    public IActionResult Index() => View();

    // Purpose: Returns the current Dashboard summary data.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        try
        {
            var summary = await _dashboardService.GetSummaryAsync(cancellationToken);
            var session = _sessionService.Get();
            var taskLicense = MoveItLicenseCapacity.ForTasks(session, summary.TotalTasks);
            var hostLicense = MoveItLicenseCapacity.ForHosts(session, summary.Hosts);
            return Json(new { ok = true, summary, licenseCapacity = new { tasks = taskLicense, hosts = hostLicense } });
        }
        catch (MoveItAuthenticationException ex)
        {
            return Unauthorized(new { ok = false, message = ex.Message });
        }
        catch (MoveItApiException ex)
        {
            return StatusCode((int)(ex.StatusCode ?? System.Net.HttpStatusCode.BadGateway), new { ok = false, message = ex.Message });
        }
    }

    // Purpose: Returns the Dashboard File Activity summary data.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> FileActivitySummary(CancellationToken cancellationToken)
    {
        try
        {
            var summary = await _dashboardService.GetFileActivitySummaryAsync(cancellationToken);
            return Json(new { ok = true, summary });
        }
        catch (MoveItAuthenticationException ex)
        {
            return Unauthorized(new { ok = false, message = ex.Message });
        }
        catch (MoveItApiException ex)
        {
            return StatusCode((int)(ex.StatusCode ?? System.Net.HttpStatusCode.BadGateway), new { ok = false, message = ex.Message });
        }
    }

    // Purpose: Returns the Dashboard key and certificate expiration summary.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ExpirationSummary(CancellationToken cancellationToken)
    {
        try
        {
            // Dashboard checks key/certificate expiration once per page load. Reuse the existing
            // per-user MOVEit inventory snapshot and the exact same expiration rules as the grid.
            var snapshot = await _keyCertificateService.GetSnapshotAsync(false, cancellationToken);
            var expiredCount = snapshot.Items.Count(KeyCertificateExpiration.IsExpired);
            return Json(new { ok = true, expiredCount });
        }
        catch (MoveItAuthenticationException ex)
        {
            return Unauthorized(new { ok = false, message = ex.Message });
        }
        catch (MoveItApiException ex)
        {
            return StatusCode((int)(ex.StatusCode ?? System.Net.HttpStatusCode.BadGateway), new { ok = false, message = ex.Message });
        }
    }

    // Purpose: Returns the Dashboard running-task snapshot.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Running(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _dashboardService.GetRunningSnapshotAsync(cancellationToken);
            return Json(new { ok = true, snapshot });
        }
        catch (MoveItAuthenticationException ex)
        {
            return Unauthorized(new { ok = false, message = ex.Message });
        }
        catch (MoveItApiException ex)
        {
            return StatusCode((int)(ex.StatusCode ?? System.Net.HttpStatusCode.BadGateway), new { ok = false, message = ex.Message });
        }
    }

    // Purpose: Renders the application error page.
    [AllowAnonymous]
    public IActionResult Error() => View();
}
