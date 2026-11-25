using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MoveItAutomationUI.Services.Reporting;

namespace MoveItAutomationUI.Controllers;

[AllowAnonymous]
public sealed class ReportApiController(ReportApiSharingService sharing, ILogger<ReportApiController> logger) : Controller
{
    [HttpGet("/api/reports/{reportGuid}/csv")]
    [HttpHead("/api/reports/{reportGuid}/csv")]
    [EnableRateLimiting("ReportCsv")]
    public async Task<IActionResult> Csv(string reportGuid, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        if (!Guid.TryParse(reportGuid, out var guid) || guid == Guid.Empty) return NotFound();
        if (Request.Query.Keys.Any(key => !key.Equals("download", StringComparison.OrdinalIgnoreCase))
            || Request.Query["download"].Count > 1)
            return Problem(statusCode: 400, title: "Only the optional download flag is supported.");
        var download = false;
        if (Request.Query.ContainsKey("download") && !bool.TryParse(Request.Query["download"], out download))
            return Problem(statusCode: 400, title: "The download flag must be true or false.");
        try
        {
            var csv = await sharing.CsvAsync(guid, HttpMethods.IsHead(Request.Method), cancellationToken);
            if (csv is null) return NotFound();
            if (HttpMethods.IsHead(Request.Method)) return Ok();
            Response.Headers["X-MAUI-Generated-Utc"] = csv.GeneratedUtc.ToString("O");
            Response.Headers["X-MAUI-Row-Count"] = csv.RowCount.ToString(CultureInfo.InvariantCulture);
            Response.Headers["X-MAUI-Report-Revision"] = csv.DefinitionRevision;
            if (csv.RetrievalLimit is { } limit)
            {
                Response.Headers["X-MAUI-Source-Limit"] = limit.ToString(CultureInfo.InvariantCulture);
                Response.Headers["X-MAUI-Source-Limit-Reached"] = csv.SourceLimitReached ? "true" : "false";
            }
            if (!download) Response.Headers["Content-Disposition"] = "inline; filename=\"report.csv\"";
            return download
                ? File(csv.Content, "text/csv; charset=utf-8", "report.csv")
                : File(csv.Content, "text/csv; charset=utf-8");
        }
        catch (ReportApiBusyException)
        {
            Response.Headers.RetryAfter = "5";
            return Problem(statusCode: 429, title: "Report generation is busy. Try again shortly.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return StatusCode(499); }
        catch (ReportDataUnavailableException)
        {
            return Problem(statusCode: 503, title: "Report data access is not configured.");
        }
        catch (Exception ex)
        {
            logger.LogWarning("CSV request failed with {ErrorType}. Reference {TraceId}.",
                ex.GetType().Name, HttpContext.TraceIdentifier);
            return Problem(statusCode: 502, title: "Report data could not be generated.",
                detail: $"Reference {HttpContext.TraceIdentifier}.");
        }
    }
}
