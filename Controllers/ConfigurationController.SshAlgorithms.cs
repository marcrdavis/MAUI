using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Controllers;

public sealed partial class ConfigurationController
{
    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> SshAlgorithms([FromServices] IHostCrudCoordinator hosts, CancellationToken cancellationToken)
    {
        try { return Json(await hosts.GetSshAlgorithmsAsync(cancellationToken)); }
        catch (MoveItAuthenticationException) { throw; }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or HttpRequestException)
        {
            return BadRequest(new { message = "Unable to load supported SSH/KEX algorithms from MOVEit. Saved defaults have been preserved. Reload the lists to try again." });
        }
    }

    private static void ValidateSshSelection(string? selection, IReadOnlyList<HostProfileChoice> choices)
    {
        if (selection is null) return;
        var normalized = HostSshAlgorithms.NormalizeSelection(selection);
        var supported = choices.Select(choice => choice.Value).ToHashSet(StringComparer.Ordinal);
        if (normalized.Split(',').Any(value => !supported.Contains(value)))
            throw new ArgumentException("The selected SSH/KEX defaults include algorithms unavailable on the connected MOVEit server. Reload the lists and review the selections.");
    }
}
