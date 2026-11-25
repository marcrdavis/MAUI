using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace MoveItAutomationUI.Services.Reporting;

public static class ReportApiRoutes
{
    private static readonly Regex CsvPath = new(@"/api/reports/[^/\s?""'<>]+/csv\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    public static bool IsCsvRequest(PathString path)
        => path.Value is { } value && Regex.IsMatch(value, @"^/api/reports/[^/]+/csv/?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static string Redact(string value)
        => CsvPath.Replace(value, "/api/reports/[redacted]/csv");

    public static Task UnavailableAsync(HttpContext context)
        => Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Report data is temporarily unavailable.").ExecuteAsync(context);
}
