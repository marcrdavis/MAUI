// Author: MAUI project
// Normalizes saved Report edit-navigation paths independently of the IIS application PathBase.
// Report execution uses GridKey and Query; Path is only for returning to the grid editor.

using System.Text.Json;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.Reporting;

public static class OutboundReportSourceRoutes
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // These are the actual MAUI grid page routes, not MOVEit API routes. The aliases support
    // saved Reports originally captured from a controller's default Index action.
    private static readonly IReadOnlyDictionary<string, string[]> Routes =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["tasks"] = ["/Tasks/Index", "/Tasks"],
            ["upcomingruns"] = ["/Tasks/UpcomingRuns"],
            ["hosts"] = ["/Hosts/Index", "/Hosts"],
            ["scripts"] = ["/Resources/Scripts"],
            ["datelists"] = ["/Resources/DateLists"],
            ["globalparameters"] = ["/Resources/GlobalParameters"],
            ["keysandcertificates"] = ["/Resources/KeysAndCerts"],
            ["taskruns"] = ["/Records/TaskRun"],
            ["fileactivity"] = ["/Records/FileActivity"],
            ["audit"] = ["/Records/Audit"],
            ["logs"] = ["/Configuration/Logs"]
        };

    public static ReportSourceContext Normalize(string? gridKey, ReportSourceContext? context)
    {
        if (context is null) throw new ArgumentException("The Report source context is required.");
        var path = (context.Path ?? string.Empty).Trim().TrimEnd('/');
        if (path.Length == 0 || path.Length > 512 || !path.StartsWith("/", StringComparison.Ordinal)
            || path.StartsWith("//", StringComparison.Ordinal) || path.Contains('\\')
            || path.Contains('?') || path.Contains('#')
            || path.Split('/').Any(part => part == "." || part == ".."))
            throw new ArgumentException("The Report source path is invalid.");

        if (!Routes.TryGetValue((gridKey ?? string.Empty).Trim(), out var allowedRoutes))
            throw new ArgumentException("The Report source grid is invalid.");

        // Match only known MAUI grid routes at a segment boundary. Any preceding segments are
        // an obsolete IIS PathBase and must never be persisted or reused for navigation.
        foreach (var route in allowedRoutes.OrderByDescending(item => item.Length))
        {
            if (path.Equals(route, StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(route, StringComparison.OrdinalIgnoreCase))
            {
                var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (context.Query is not null)
                {
                    // Preserve supplied filter parameters, even if a legacy definition contains
                    // case-variant duplicate keys. The latest value wins, as in browser query parsing.
                    foreach (var pair in context.Query)
                    {
                        // ReturnUrl/ReturnLabel are temporary grid-navigation context, not
                        // report filters. A legacy value may contain another IIS PathBase.
                        if (pair.Key.Equals("returnUrl", StringComparison.OrdinalIgnoreCase)
                            || pair.Key.Equals("returnLabel", StringComparison.OrdinalIgnoreCase)) continue;
                        query[pair.Key] = pair.Value;
                    }
                }
                return new ReportSourceContext { Path = allowedRoutes[0], Query = query };
            }
        }

        throw new ArgumentException("The Report source path does not match its grid.");
    }

    public static string NormalizeJson(string? json, string? gridKey)
    {
        var context = JsonSerializer.Deserialize<ReportSourceContext>(json ?? string.Empty, JsonOptions)
            ?? throw new ArgumentException("The Report source context is empty.");
        return JsonSerializer.Serialize(Normalize(gridKey, context), JsonOptions);
    }
}
