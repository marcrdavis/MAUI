using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Controllers;

public sealed partial class HostsController
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    [MoveItObjectPermission("Host", "Clone", "HostId")]
    public async Task<IActionResult> CloneHost([FromBody] HostCloneRequest? request, CancellationToken cancellationToken)
    {
        var id = (request?.HostId ?? "").Trim();
        var name = (request?.NewHostName ?? "").Trim();
        if (id.Length == 0 || name.Length == 0 || name.Length > 254) return BadRequest(new { message = "Select a Host and enter a name from 1 to 254 characters." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();
        try
        {
            using var lease = await MoveItLicensePolicy.AcquireHostCreationAsync(session, cancellationToken);
            var count = (await _hostService.GetHostSnapshotAsync(true, cancellationToken)).Hosts.Count;
            var block = MoveItLicensePolicy.GetHostCapacityBlock(session, count, 1);
            if (!string.IsNullOrWhiteSpace(block)) return Conflict(new { code = "LicenseLimitReached", message = block });
            var metadata = await _metadataRepository.GetObjectMetadataAsync(session.GetEnvironmentKey(), ObjectType, id, cancellationToken);
            var created = await _hostService.CloneHostAsync(id, name, cancellationToken);
            var warning = "";
            try
            {
                await _metadataRepository.RecordObjectCreatedAsync(session.GetEnvironmentKey(), ObjectType, created.Id, session.Username, cancellationToken);
                await _metadataRepository.SaveObjectMetadataAsync(session.GetEnvironmentKey(), ObjectType, created.Id, metadata.Values, session.Username, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Host {HostId} was cloned but MAUI metadata could not be fully copied.", created.Id);
                warning = "The Host was cloned, but MAUI could not fully copy its metadata. Review the new Host.";
            }
            return Json(new { success = true, host = new { id = created.Id, name = created.Name, type = created.HostType, typeCode = created.HostTypeCode }, warning });
        }
        catch (HostNameAlreadyExistsException ex) { return Conflict(new { message = ex.Message, moveItSaved = false }); }
        catch (HostPartialSaveException ex) { return Conflict(new { message = ex.Message, moveItSaved = true, hostId = ex.HostId }); }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        { return MauiControllerExceptionMapper.Map(ex); }
    }
    public sealed record HostCloneRequest(string HostId, string NewHostName);
}
