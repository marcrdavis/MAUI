// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Controllers;

[Authorize]
[Route("Server/[action]")]
public sealed class MoveItServerController : Controller
{
    private readonly IMoveItRestClient _restClient;
    private readonly ILogger<MoveItServerController> _logger;

    public MoveItServerController(IMoveItRestClient restClient, ILogger<MoveItServerController> logger)
    {
        _restClient = restClient;
        _logger = logger;
    }

    // Returns MOVEit's live failover/resiliency state. This is read-only and intentionally uncached so the
    // footer capability probe and modal always reflect the current native /api/v1/failoverstatus response.
    [HttpGet]
    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> FailoverStatus(CancellationToken cancellationToken)
    {
        try
        {
            var status = await _restClient.GetFailoverStatusAsync(cancellationToken);
            if (status.ResilEnabled && status.Nodes.Count != 2)
            {
                _logger.LogWarning(
                    "MOVEit failover status for {Username} reports resiliency enabled but returned {NodeCount} node definitions (expected 2).",
                    User.Identity?.Name ?? "unknown",
                    status.Nodes.Count);
            }
            else
            {
                _logger.LogDebug(
                    "MOVEit failover status retrieved for {Username}: enabled={ResilEnabled}, thisNode={ThisNode}, nodes={NodeCount}.",
                    User.Identity?.Name ?? "unknown",
                    status.ResilEnabled,
                    status.ThisNode,
                    status.Nodes.Count);
            }

            var nodes = status.Nodes.Select(node =>
            {
                var isPrimary = node.ResilRole.Equals("primary", StringComparison.OrdinalIgnoreCase);
                var isSecondary = node.ResilRole.Equals("secondary", StringComparison.OrdinalIgnoreCase);
                return new
                {
                    nodeNum = node.NodeNum,
                    hostname = node.Hostname,
                    role = node.ResilRole,
                    nodeHasError = node.NodeHasError,
                    status = isPrimary ? status.PrimaryStatus : isSecondary ? status.SecondaryStatus : string.Empty,
                    statusStamp = isPrimary ? status.PrimaryStatusStamp : isSecondary ? status.SecondaryStatusStamp : string.Empty
                };
            }).ToArray();

            return Json(new
            {
                resilEnabled = status.ResilEnabled,
                thisNode = status.ThisNode,
                nodes
            });
        }
        catch (MoveItAuthenticationException)
        {
            return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        }
        catch (MoveItApiException ex)
        {
            _logger.LogWarning(ex, "Unable to retrieve MOVEit failover status for {Username}.", User.Identity?.Name ?? "unknown");
            var statusCode = ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : StatusCodes.Status502BadGateway;
            return StatusCode(statusCode, new
            {
                message = string.IsNullOrWhiteSpace(ex.Message)
                    ? "MOVEit failover status could not be retrieved."
                    : ex.Message
            });
        }
    }

    // Resets MOVEit's tamper-detection baseline. This destructive server action is restricted to MAUI Admins.
    [HttpPost]
    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetTamperDetection(CancellationToken cancellationToken)
    {
        try
        {
            await _restClient.ResetTamperDetectionAsync(cancellationToken);
            _logger.LogWarning("MOVEit tamper detection reset by {Username}.", User.Identity?.Name ?? "unknown");
            return Json(new { success = true });
        }
        catch (MoveItAuthenticationException)
        {
            return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        }
        catch (MoveItApiException ex)
        {
            _logger.LogWarning(ex, "MOVEit tamper detection reset failed for {Username}.", User.Identity?.Name ?? "unknown");
            if (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    message = "Reset Tamper Detection requires MOVEit Administrator access."
                });
            }

            var statusCode = ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : StatusCodes.Status502BadGateway;
            return StatusCode(statusCode, new
            {
                message = string.IsNullOrWhiteSpace(ex.Message)
                    ? "MOVEit tamper detection could not be reset."
                    : ex.Message
            });
        }
    }
}
