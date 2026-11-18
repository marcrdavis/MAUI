using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Features.LocalGroups;

[Authorize(Policy = MauiSecurityPolicies.Admin)]
[Route("Configuration/LocalGroups")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LocalGroupsController(LocalGroupsService service, ILogger<LocalGroupsController> logger) : Controller
{
    [HttpGet("State")]
    public IActionResult State()
    {
        try { return Json(service.State()); }
        catch (MoveItAuthenticationException ex) { return Unauthorized(new { message = ex.Message }); }
    }
    [HttpPost("Execute"), ValidateAntiForgeryToken, RequestSizeLimit(64 * 1024)]
    public async Task<IActionResult> Execute([FromBody] LocalGroupRequest request, CancellationToken cancellationToken)
    {
        try { return Json(await service.ExecuteAsync(request, User, cancellationToken)); }
        catch (MoveItAuthenticationException ex) { return Unauthorized(new { message = ex.Message }); }
        catch (UnauthorizedAccessException ex) { return StatusCode(403, new { message = ex.Message }); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or MoveItApiException or HttpRequestException or IOException)
        {
            logger.LogWarning(ex, "Local group {Operation} failed for {User}.", request?.Operation, User.Identity?.Name);
            return BadRequest(new { message = ex is IOException ? "The local group operation record could not be written. Check MAUI Data permissions before retrying." : ex.Message,
                outcomeUncertain = ex is LocalGroupsOperationException { OutcomeUncertain: true },
                retryable = ex is LocalGroupsOperationException { Retryable: true } });
        }
    }
}

[ApiController, Route(LocalGroupsProtocol.AgentRoute)]
[Authorize(AuthenticationSchemes = LocalGroupsProtocol.AgentScheme)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(2 * 1024 * 1024)]
public sealed class LocalGroupsAgentController(LocalGroupsConfiguration configuration, LocalGroupsBroker broker) : ControllerBase
{
    // Static reachability information only. Does not authenticate the service or expose registrations.
    [HttpGet, AllowAnonymous]
    public IActionResult Information() => Ok(new
    {
        message = "MAUI User Management service endpoint. The Windows Service uses POST poll and POST result with a registered client certificate.",
        protocolVersion = LocalGroupsProtocol.Version
    });

    private LocalGroupsAgent Agent()
    {
        var agent = configuration.RequireAgent(User.FindFirst(AgentAuthenticationHandler.ServerClaim)?.Value ?? "");
        if (!agent.CertificateSha256.Equals(User.FindFirst(AgentAuthenticationHandler.CertificateClaim)?.Value, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The service registration changed.");
        return agent;
    }
    [HttpPost("poll")]
    public async Task<IActionResult> Poll([FromBody] AgentPoll poll, CancellationToken ct)
    {
        try
        {
            var agent = Agent();
            if (poll.ProtocolVersion != LocalGroupsProtocol.Version || !LocalGroupsProtocol.IsComputerName(poll.ComputerName)) return BadRequest(new { message = "Invalid service computer name or protocol version." });
            var command = await broker.PollAsync(agent, poll.ComputerName, ct);
            return command is null ? NoContent() : Ok(command);
        }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
    }
    [HttpPost("result")]
    public IActionResult Result([FromBody] AgentResult result)
    {
        try { broker.Complete(Agent(), result); return Ok(new { acceptedId = result.Id }); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return Conflict(new { message = ex.Message }); }
    }
}
