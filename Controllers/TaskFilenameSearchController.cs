using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Tasks;

namespace MoveItAutomationUI.Controllers;

[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TaskFilenameSearchController(ITaskFilenameSearchService filenames,
    ILogger<TaskFilenameSearchController> logger) : Controller
{
    // Separate on-demand read: initial Task grids and regular refreshes never request step data.
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try { return Json(new { success = true, items = await filenames.ReadAsync(cancellationToken) }); }
        catch (MoveItAuthenticationException) { return MauiControllerExceptionMapper.SessionExpired(); }
        catch (MoveItApiException ex) { return MauiControllerExceptionMapper.Map(ex); }
        // An upstream timeout can throw OperationCanceledException without the browser cancelling.
        // Keep that failure structured; propagate only cancellation of this request.
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Task filename search could not be loaded.");
            return StatusCode(500, new { message = "Task filenames could not be loaded. Retry filename search." });
        }
    }
}
