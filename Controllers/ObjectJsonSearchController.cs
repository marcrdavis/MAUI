using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ObjectJsonSearchController(IObjectJsonSearchService search,
    ILogger<ObjectJsonSearchController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string type, string id, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || type is not ("Task" or "Host") || string.IsNullOrEmpty(id)
            || id.Length > 32 || !id.All(char.IsAsciiDigit))
            return BadRequest(new { message = "Select a valid Task or Host." });
        try { return Json(new { success = true, document = await search.ReadAsync(type, id, cancellationToken) }); }
        catch (MoveItAuthenticationException) { return MauiControllerExceptionMapper.SessionExpired(); }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Neither native response bodies nor searchable values belong in diagnostics.
            logger.LogWarning("JSON search could not read {ObjectType} {ObjectId} ({ErrorType}).", type, id, ex.GetType().Name);
            return StatusCode(502, new { message = "JSON search could not be completed. Select Refresh Now to retry." });
        }
    }
}
