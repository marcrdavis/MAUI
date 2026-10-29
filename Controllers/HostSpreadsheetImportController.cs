using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Controllers;

// Separate feature controller: keeps the established Host inventory/editor APIs unchanged.
[Authorize]
public sealed class HostSpreadsheetImportController : Controller
{
    private const long MaxUploadBytes = 50L * 1024 * 1024;
    private readonly IHostSpreadsheetImportService _import;
    private readonly IMoveItSessionService _sessions;
    private readonly IWebHostEnvironment _environment;

    public HostSpreadsheetImportController(
        IHostSpreadsheetImportService import, IMoveItSessionService sessions, IWebHostEnvironment environment)
    {
        _import = import;
        _sessions = sessions;
        _environment = environment;
    }

    // Host templates are installation-independent release assets under /Templates,
    // just like TaskImport; they must not be served directly from wwwroot.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult DownloadTemplate([FromQuery] string? format)
    {
        if (_sessions.Get() is null) return Unauthorized();
        var fileName = format?.ToLowerInvariant() switch
        {
            "csv" => "MAUI_HostImportTemplate.csv",
            "xlsx" => "MAUI_HostImportTemplate.xlsx",
            _ => null
        };
        if (fileName is null) return BadRequest();
        var fullPath = Path.Combine(_environment.ContentRootPath, "Templates", "HostImport", fileName);
        if (!System.IO.File.Exists(fullPath)) return NotFound();
        var contentType = format!.Equals("csv", StringComparison.OrdinalIgnoreCase)
            ? "text/csv" : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        return PhysicalFile(fullPath, contentType, fileName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [RequestSizeLimit(MaxUploadBytes + 1048576)]
    [MoveItObjectPermission("Host", "Import")]
    public async Task<IActionResult> Preview([FromForm] IFormFile? file, CancellationToken cancellationToken)
    {
        if (_sessions.Get() is null) return Unauthorized(new { message = "MOVEit session expired." });
        var bytes = await ReadFileAsync(file, cancellationToken);
        if (bytes is null) return BadRequest(new { message = "Select a CSV or XLSX file (maximum 50 MB)." });
        try { return Json(new { success = true, result = _import.Preview(bytes, Path.GetFileName(file!.FileName)) }); }
        catch (InvalidOperationException) { return BadRequest(new { message = "Unable to read this spreadsheet. Check its format, column names, and row limits." }); }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [RequestSizeLimit(MaxUploadBytes + 1048576)]
    [MoveItObjectPermission("Host", "Import")]
    public async Task<IActionResult> Analyze([FromForm] IFormFile? file, [FromForm] string? prefix, [FromForm] bool updateExisting, [FromForm] bool testAfterCreation, CancellationToken cancellationToken)
    {
        if (_sessions.Get() is null) return Unauthorized(new { message = "MOVEit session expired." });
        var bytes = await ReadFileAsync(file, cancellationToken);
        if (bytes is null) return BadRequest(new { message = "Select a CSV or XLSX file (maximum 50 MB)." });
        try { return Json(new { success = true, result = await _import.AnalyzeAsync(bytes, Path.GetFileName(file!.FileName), prefix ?? string.Empty, updateExisting, testAfterCreation, cancellationToken) }); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // Parsing/metadata failures must never echo spreadsheet contents, including credentials.
            return BadRequest(new { message = "Unable to analyze this Host spreadsheet. Verify the column headers and required fields." });
        }
    }

    public sealed record RowRequest(string AnalysisId, int RowNumber);
    [HttpPost]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [MoveItObjectPermission("Host", "Import")]
    public async Task<IActionResult> ImportRow([FromBody] RowRequest? request, CancellationToken cancellationToken)
    {
        if (_sessions.Get() is null) return Unauthorized(new { message = "MOVEit session expired." });
        if (request is null || request.RowNumber < 2 || string.IsNullOrWhiteSpace(request.AnalysisId))
            return BadRequest(new { message = "A valid analyzed Host row is required." });
        try { return Json(new { success = true, result = await _import.ImportRowAsync(request.AnalysisId, request.RowNumber, cancellationToken) }); }
        catch (InvalidOperationException) { return Conflict(new { message = "This import row is not available. Reanalyze the file." }); }
    }

    public sealed record FinishRequest(string AnalysisId);
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Finish([FromBody] FinishRequest? request)
    {
        if (_sessions.Get() is null) return Unauthorized();
        if (request is not null && !string.IsNullOrWhiteSpace(request.AnalysisId)) _import.Finish(request.AnalysisId);
        return Json(new { success = true });
    }

    private static async Task<byte[]?> ReadFileAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length <= 0 || file.Length > MaxUploadBytes) return null;
        var ext = Path.GetExtension(file.FileName);
        if (!ext.Equals(".csv", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)) return null;
        using var memory = new MemoryStream();
        await file.CopyToAsync(memory, cancellationToken);
        return memory.ToArray();
    }
}
