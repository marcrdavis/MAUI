using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Controllers;

public sealed partial class ConfigurationController
{
    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateResourceGroupFolders(
        [FromServices] IResourceGroupLookupService groups,
        [FromServices] IMetadataRepository metadata,
        CancellationToken cancellationToken)
    {
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();
        if (session.Info?.IsAdmin != true)
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only MOVEit administrators can manage Task folders." });

        try
        {
            // Read the current MOVEit inventory before making any local changes.
            var names = await groups.GetVisibleNamesAsync(cancellationToken);
            var result = await metadata.CreateMissingRootFoldersAsync(session.GetEnvironmentKey(), "Task", names, cancellationToken);
            return Json(new { success = true, created = result.Created, existing = result.Existing,
                message = $"Created {result.Created} Task folders. {result.Existing} Resource Group names already have matching folders." });
        }
        catch (Exception ex) when (ex is MoveItAuthenticationException or MoveItApiException or InvalidOperationException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
        catch (Exception ex) when (ex is DbUpdateException or System.Data.Common.DbException or HttpRequestException)
        {
            _logger.LogError(ex, "Unable to create Task folders from Resource Groups for {Environment}.", session.GetEnvironmentKey());
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Task folders could not be created. Review Configuration > Logs and try again." });
        }
    }
}
