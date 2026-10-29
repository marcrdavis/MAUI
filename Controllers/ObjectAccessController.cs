using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Controllers;

[Authorize, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ObjectAccessController(IMoveItObjectAccessService access) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Creation(CancellationToken ct)
    {
        try { return Json(await access.GetCreationAsync(true, ct)); }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException) { return MauiControllerExceptionMapper.Map(ex); }
    }
    [HttpGet]
    public async Task<IActionResult> Object(string type, string id, CancellationToken ct)
    {
        try { return Json(await access.GetObjectAsync(type, id, ct)); }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException) { return MauiControllerExceptionMapper.Map(ex); }
    }
    // Bound parallel detail reads so opening a bulk menu cannot flood MOVEit.
    [HttpGet]
    public async Task<IActionResult> Objects(string type, [FromQuery] string[] ids, CancellationToken ct)
    {
        if (ids.Length > 100) return BadRequest(new { message = "Select at most 100 objects per permission request." });
        try
        {
            return Json(await access.GetObjectsAsync(type, ids, ct));
        }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException)
        { return MauiControllerExceptionMapper.Map(ex); }
    }

}
