// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Reuses the existing grid controller paths to regenerate scheduled report data, then reapplies the saved current-view state.
// For Task Run, File Activity, and Audit, MAUI Reports call RecordsController; "Records" is MAUI's name for MOVEit Automation's native Reports area.

using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.Security;

namespace MoveItAutomationUI.Services.Reporting;

public interface IOutboundReportDataSource
{
    Task<OutboundReportData> LoadAsync(OutboundReportEntity report, CancellationToken cancellationToken = default);
    OutboundReportData NormalizeSnapshot(ReportGridSnapshot snapshot);
}

public sealed class OutboundReportDataSource : IOutboundReportDataSource
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private static readonly Regex FilterAlternativeSeparatorRegex = new(
        @",|\s+or\s+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly IServiceProvider _services;
    private readonly IMauiSecurityService _security;

    public OutboundReportDataSource(IServiceProvider services, IMauiSecurityService security)
    {
        _services = services;
        _security = security;
    }

    public async Task<OutboundReportData> LoadAsync(OutboundReportEntity report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        OutboundReportSourcePolicy.EnsureAllowed(report.GridKey, _security.IsAdmin(report.OwnerUsername));
        var state = Deserialize<ReportGridViewState>(report.ViewStateJson, "report view state");
        var context = Deserialize<ReportSourceContext>(report.SourceContextJson, "report source context");
        var rows = await LoadSourceRowsAsync(report.GridKey, context, cancellationToken);
        return ApplyViewState(state, rows);
    }

    public OutboundReportData NormalizeSnapshot(ReportGridSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var columns = (snapshot.Columns ?? new()).OrderBy(column => column.Order).ToArray();
        if (columns.Length == 0) throw new ArgumentException("The current grid view does not contain any visible columns.");
        return new OutboundReportData(columns, snapshot.Rows ?? new List<ReportGridSnapshotRow>());
    }

    private async Task<IReadOnlyList<ReportGridSnapshotRow>> LoadSourceRowsAsync(string gridKey, ReportSourceContext source, CancellationToken cancellationToken)
    {
        gridKey = (gridKey ?? string.Empty).Trim().ToLowerInvariant();
        if (gridKey == "taskruns")
        {
            return await LoadAllReportBatchesAsync(
                (cursor, offset) => WithController<RecordsController>("Records", controller => controller.TaskRunBatch(Query(source, "taskId"), Query(source, "taskName"), Query(source, "dashboardView"), cursor, offset, cancellationToken)),
                cancellationToken);
        }
        if (gridKey == "fileactivity")
        {
            return await LoadAllReportBatchesAsync(
                (cursor, offset) => WithController<RecordsController>("Records", controller => controller.FileActivityBatch(Query(source, "taskId"), Query(source, "taskName"), Query(source, "runId"), Query(source, "nominalStart"), Query(source, "dashboardView"), cursor, offset, cancellationToken)),
                cancellationToken);
        }
        if (gridKey == "audit")
        {
            return await LoadAllReportBatchesAsync(
                (cursor, offset) => WithController<RecordsController>("Records", controller => controller.AuditBatch(Query(source, "targetType"), Query(source, "targetId"), Query(source, "targetName"), cursor, offset, cancellationToken)),
                cancellationToken);
        }

        IActionResult result = gridKey switch
        {
            "tasks" => await WithController<TasksController>("Tasks", controller => controller.InitialGrid(Query(source, "filter", "all"), QueryInt(source, "folderId"), preferCached: false, cancellationToken: cancellationToken)),
            "upcomingruns" => await WithController<TasksController>("Tasks", controller => controller.UpcomingRunsGrid(cancellationToken)),
            "hosts" => await WithController<HostsController>("Hosts", controller => controller.RefreshGrid(Query(source, "filter", "all"), cancellationToken, forceRefresh: true)),
            "scripts" => await WithController<ResourcesController>("Resources", controller => controller.RefreshScriptsGrid(cancellationToken, forceRefresh: true)),
            "datelists" => await WithController<ResourcesController>("Resources", controller => controller.RefreshDateListsGrid(cancellationToken, forceRefresh: true)),
            "globalparameters" => await WithController<ResourcesController>("Resources", controller => controller.RefreshGlobalParametersGrid(cancellationToken, forceRefresh: true)),
            "keysandcertificates" => await WithController<ResourcesController>("Resources", controller => controller.RefreshKeysAndCertificatesGrid(cancellationToken, forceRefresh: true)),
            "logs" => await WithController<ConfigurationController>("Configuration", controller => controller.Logs(cancellationToken)),
            _ => throw new InvalidOperationException($"Grid '{gridKey}' does not support scheduled report generation.")
        };
        return ExtractRows(result);
    }

    private async Task<IReadOnlyList<ReportGridSnapshotRow>> LoadAllReportBatchesAsync(
        Func<string?, int, Task<IActionResult>> loadBatch,
        CancellationToken cancellationToken)
    {
        var rows = new List<ReportGridSnapshotRow>();
        string? cursor = null;
        var offset = 0;

        while (offset < ReportBehaviorOptions.MaximumEntriesPerReport)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await loadBatch(cursor, offset);
            var page = ExtractBatchPage(result);
            if (page.Rows.Count > 0)
            {
                var remaining = ReportBehaviorOptions.MaximumEntriesPerReport - rows.Count;
                rows.AddRange(page.Rows.Take(remaining));
            }

            if (!page.HasMore || rows.Count >= ReportBehaviorOptions.MaximumEntriesPerReport) break;
            if (string.IsNullOrWhiteSpace(page.NextCursor))
                throw new InvalidOperationException("MOVEit report paging indicated more rows but did not return a continuation cursor.");

            var nextOffset = page.End > offset ? page.End : offset + page.Rows.Count;
            if (nextOffset <= offset)
                throw new InvalidOperationException("MOVEit report paging did not advance to the next batch.");

            cursor = page.NextCursor;
            offset = nextOffset;
        }

        return rows;
    }

    private (IReadOnlyList<ReportGridSnapshotRow> Rows, bool HasMore, string NextCursor, int End) ExtractBatchPage(IActionResult result)
    {
        var rows = ExtractRows(result);
        object? value = result switch
        {
            JsonResult json => json.Value,
            ObjectResult objectResult => objectResult.Value,
            _ => null
        };
        if (value is null) throw new InvalidOperationException("The report batch returned no paging metadata.");

        var node = JsonSerializer.SerializeToNode(value, JsonOptions) as JsonObject
            ?? throw new InvalidOperationException("The report batch returned invalid paging metadata.");
        var hasMore = node["hasMore"]?.GetValue<bool>() ?? false;
        var nextCursor = node["nextCursor"]?.GetValue<string>() ?? string.Empty;
        var end = node["end"]?.GetValue<int>() ?? rows.Count;
        return (rows, hasMore, nextCursor, end);
    }

    private async Task<IActionResult> WithController<TController>(string controllerName, Func<TController, Task<IActionResult>> action)
        where TController : Controller
    {
        var controller = ActivatorUtilities.CreateInstance<TController>(_services);
        PrepareController(controller, controllerName);
        return await action(controller);
    }

    private void PrepareController(Controller controller, string controllerName)
    {
        var httpContext = new DefaultHttpContext
        {
            RequestServices = _services
        };
        PrepareControllerContext(controller, controllerName, httpContext);
    }

    private static void PrepareControllerContext(Controller controller, string controllerName, HttpContext? existing = null)
    {
        var httpContext = existing ?? new DefaultHttpContext();
        if (httpContext.RequestServices is null) throw new InvalidOperationException("Background controller services are unavailable.");
        httpContext.Request.Scheme = "https";
        httpContext.Request.Host = new HostString("localhost");
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "MAUI Reports System Job") }, "MAUI-Report"));
        var routeData = new RouteData();
        routeData.Values["controller"] = controllerName;

        // The report engine executes existing grid-controller actions outside the normal MVC
        // endpoint pipeline. Some grid row builders call Url.Action() only to populate their
        // interactive InfoActionUrl value. Without a router in this synthetic ActionContext,
        // ASP.NET Core's UrlHelper throws before the report rows can be returned. The report
        // output does not consume those interactive URLs, but supplying a lightweight router
        // keeps the existing controller/grid paths usable without duplicating their data logic.
        routeData.Routers.Add(BackgroundReportRouter.Instance);

        var actionDescriptor = new ControllerActionDescriptor { ControllerName = controllerName };
        controller.ControllerContext = new ControllerContext(new ActionContext(httpContext, routeData, actionDescriptor));
    }

    private sealed class BackgroundReportRouter : IRouter
    {
        public static BackgroundReportRouter Instance { get; } = new();

        private BackgroundReportRouter()
        {
        }

        public Task RouteAsync(RouteContext context) => Task.CompletedTask;

        public VirtualPathData? GetVirtualPath(VirtualPathContext context)
        {
            // These URLs are not included in outbound report data. Return a stable local path
            // so Url.Action() can complete normally while the grid controller builds its rows.
            var controller = context.Values.TryGetValue("controller", out var controllerValue)
                ? controllerValue?.ToString()
                : null;
            var action = context.Values.TryGetValue("action", out var actionValue)
                ? actionValue?.ToString()
                : null;

            var path = string.IsNullOrWhiteSpace(controller)
                ? "/"
                : string.IsNullOrWhiteSpace(action)
                    ? $"/{controller}"
                    : $"/{controller}/{action}";

            return new VirtualPathData(this, path);
        }
    }

    private IReadOnlyList<ReportGridSnapshotRow> ExtractRows(IActionResult result)
    {
        if (result is ViewResult view && view.Model is GridViewModel grid)
            return grid.Rows.Select(ToSnapshotRow).ToArray();

        if (result is ObjectResult errorResult && errorResult.StatusCode is >= 400)
            throw new InvalidOperationException(ExtractMessage(errorResult.Value) ?? $"Grid data request failed ({errorResult.StatusCode}).");
        if (result is StatusCodeResult statusCodeResult && statusCodeResult.StatusCode >= 400)
            throw new InvalidOperationException($"Grid data request failed ({statusCodeResult.StatusCode}).");

        object? value = result switch
        {
            JsonResult json => json.Value,
            ObjectResult objectResult => objectResult.Value,
            _ => null
        };
        if (value is null) throw new InvalidOperationException("The grid data source returned no data.");

        var node = JsonSerializer.SerializeToNode(value, JsonOptions) as JsonObject
            ?? throw new InvalidOperationException("The grid data source returned an invalid response.");
        if (node["rows"] is not JsonArray rowsNode) throw new InvalidOperationException("The grid data source response did not contain rows.");
        var rows = new List<ReportGridSnapshotRow>(rowsNode.Count);
        foreach (var rowNode in rowsNode.OfType<JsonObject>())
        {
            rows.Add(new ReportGridSnapshotRow
            {
                Id = rowNode["id"]?.GetValue<string>() ?? string.Empty,
                Values = ReadStringDictionary(rowNode["values"] as JsonObject),
                RawValues = ReadStringDictionary(rowNode["rawValues"] as JsonObject)
            });
        }
        return rows;
    }

    private static OutboundReportData ApplyViewState(ReportGridViewState state, IReadOnlyList<ReportGridSnapshotRow> sourceRows)
    {
        if (state.SchemaVersion != 1) throw new InvalidOperationException("The report view uses an unsupported schema.");
        var columns = (state.Columns ?? new List<ReportGridColumnState>()).OrderBy(column => column.Order).ToArray();
        if (columns.Length == 0) throw new InvalidOperationException("The report view contains no visible columns.");

        var today = DateOnly.FromDateTime(DateTime.Today);
        var filtered = sourceRows.Where(row => MatchesScopeFilters(row, state.ScopeFilters)
            && MatchesGlobalSearch(row, state.SearchText, state.SearchColumns, columns)
            && MatchesColumnFilters(row, state.Filters, state.FilterTypes, columns, today)).ToList();

        if (state.Sort is { Column.Length: > 0 } sort && (sort.Direction == "asc" || sort.Direction == "desc"))
        {
            var multiplier = sort.Direction == "asc" ? 1 : -1;
            filtered = filtered.Select((row, index) => (row, index)).OrderBy(item => item, Comparer<(ReportGridSnapshotRow row, int index)>.Create((left, right) =>
            {
                var comparison = CompareValues(ValueForSort(left.row, sort.Column), ValueForSort(right.row, sort.Column), sort.Type);
                return comparison != 0 ? comparison * multiplier : left.index.CompareTo(right.index);
            })).Select(item => item.row).ToList();
        }

        return new OutboundReportData(columns, filtered);
    }

    private static bool MatchesScopeFilters(ReportGridSnapshotRow row, IReadOnlyDictionary<string, string>? filters)
    {
        if (filters is null) return true;
        foreach (var pair in filters)
        {
            var expected = (pair.Value ?? string.Empty).Trim();
            if (expected.Length == 0) continue;
            var actual = Value(row, pair.Key, preferRaw: true).Trim();
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private static bool MatchesGlobalSearch(ReportGridSnapshotRow row, string searchText, IReadOnlyList<string>? searchColumns, IReadOnlyList<ReportGridColumnState> outputColumns)
    {
        var keys = searchColumns is { Count: > 0 } ? searchColumns : outputColumns.Select(column => column.Key).ToArray();
        var text = string.Join(' ', keys.Select(key => Value(row, key, preferRaw: false))).ToLowerInvariant();
        return MatchesTextFilterExpression(text, searchText);
    }

    private static bool MatchesColumnFilters(
        ReportGridSnapshotRow row,
        IReadOnlyDictionary<string, string>? filters,
        IReadOnlyDictionary<string, string>? filterTypes,
        IReadOnlyList<ReportGridColumnState> outputColumns,
        DateOnly today)
    {
        if (filters is null) return true;
        foreach (var pair in filters)
        {
            var type = FilterType(pair.Key, filterTypes, outputColumns);
            if (!MatchesColumnFilterExpression(row, pair.Key, pair.Value, type, today)) return false;
        }
        return true;
    }

    private static bool MatchesColumnFilterExpression(
        ReportGridSnapshotRow row,
        string key,
        string? rawValue,
        string type,
        DateOnly today)
    {
        var alternatives = type.Equals("datetime", StringComparison.OrdinalIgnoreCase)
            ? GridDateFilterMacros.SplitAlternatives(rawValue)
            : SplitFilterAlternatives(rawValue);
        if (alternatives.Count == 0) return true;
        if (alternatives.Count == 1) return MatchesSingleColumnFilter(row, key, alternatives[0], type, today, out var single) ? single : true;

        var anyMatch = false;
        foreach (var alternative in alternatives)
        {
            if (!MatchesSingleColumnFilter(row, key, alternative, type, today, out var matches))
            {
                // Preserve literal-filter behavior for partial/malformed OR expressions.
                return MatchesSingleColumnFilter(row, key, rawValue ?? string.Empty, type, today, out var literal) ? literal : true;
            }
            anyMatch |= matches;
        }
        return anyMatch;
    }

    private static bool MatchesSingleColumnFilter(
        ReportGridSnapshotRow row,
        string key,
        string rawValue,
        string type,
        DateOnly today,
        out bool matches)
    {
        if (type.Equals("datetime", StringComparison.OrdinalIgnoreCase)
            && GridDateFilterMacros.TryParseFilter(rawValue, today, out var dateFilter))
        {
            var hasDate = GridDateFilterMacros.TryReadGridDateTime(
                Value(row, key, preferRaw: true),
                Value(row, key, preferRaw: false),
                out var rowDate);
            matches = dateFilter.Matches(hasDate ? rowDate : null);
            return true;
        }

        if (type.Equals("number", StringComparison.OrdinalIgnoreCase)
            && TryParseNumericComparisonFilter(rawValue, out var comparison))
        {
            matches = TryReadNumericGridValue(Value(row, key, preferRaw: true), out var rowNumber)
                && MatchesNumericComparison(rowNumber, comparison);
            return true;
        }

        var filter = ParseTextFilter(rawValue);
        if (filter is null)
        {
            matches = true;
            return false;
        }

        matches = MatchesTextFilter(Value(row, key, preferRaw: false), filter.Value.term, filter.Value.negate);
        return true;
    }

    private static string FilterType(
        string key,
        IReadOnlyDictionary<string, string>? filterTypes,
        IReadOnlyList<ReportGridColumnState> outputColumns)
    {
        if (filterTypes is not null && filterTypes.TryGetValue(key, out var configured) && !string.IsNullOrWhiteSpace(configured))
            return configured.Trim();

        return outputColumns.FirstOrDefault(column => column.Key.Equals(key, StringComparison.OrdinalIgnoreCase))?.Type?.Trim()
            ?? "text";
    }

    private readonly record struct NumericComparisonFilter(string Operator, double Target);

    private static bool TryParseNumericComparisonFilter(string? value, out NumericComparisonFilter filter)
    {
        filter = default;
        var text = (value ?? string.Empty).Trim();
        if (text.Length < 2) return false;

        string operation;
        string numberText;
        if (text.StartsWith("<=", StringComparison.Ordinal) || text.StartsWith(">=", StringComparison.Ordinal))
        {
            operation = text[..2];
            numberText = text[2..].Trim();
        }
        else if (text[0] is '<' or '>')
        {
            operation = text[..1];
            numberText = text[1..].Trim();
        }
        else return false;

        if (!double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var target)
            || double.IsNaN(target) || double.IsInfinity(target)) return false;

        filter = new NumericComparisonFilter(operation, target);
        return true;
    }

    private static bool TryReadNumericGridValue(string? value, out double number)
    {
        return double.TryParse((value ?? string.Empty).Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out number)
            && !double.IsNaN(number)
            && !double.IsInfinity(number);
    }

    private static bool MatchesNumericComparison(double value, NumericComparisonFilter filter)
    {
        return filter.Operator switch
        {
            "<" => value < filter.Target,
            "<=" => value <= filter.Target,
            ">" => value > filter.Target,
            ">=" => value >= filter.Target,
            _ => false
        };
    }

    private static IReadOnlyList<string> SplitFilterAlternatives(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0) return Array.Empty<string>();

        var parts = FilterAlternativeSeparatorRegex.Split(text).Select(part => part.Trim()).ToArray();
        return parts.Length > 1 && parts.All(part => part.Length > 0)
            ? parts
            : new[] { text };
    }

    private static bool MatchesTextFilterExpression(string text, string? rawValue)
    {
        var alternatives = SplitFilterAlternatives(rawValue);
        if (alternatives.Count == 0) return true;
        if (alternatives.Count == 1)
        {
            var filter = ParseTextFilter(alternatives[0]);
            return filter is null || MatchesTextFilter(text, filter.Value.term, filter.Value.negate);
        }

        var filters = alternatives.Select(ParseTextFilter).ToArray();
        if (filters.Any(filter => filter is null))
        {
            var literal = ParseTextFilter(rawValue);
            return literal is null || MatchesTextFilter(text, literal.Value.term, literal.Value.negate);
        }

        return filters.Any(filter => filter is { } parsed && MatchesTextFilter(text, parsed.term, parsed.negate));
    }

    private static (string term, bool negate)? ParseTextFilter(string? value)
    {
        var text = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (text.Length == 0) return null;
        if (text.StartsWith("not:", StringComparison.Ordinal))
        {
            var term = text[4..].Trim();
            return term.Length == 0 ? null : (term, true);
        }
        if (text.StartsWith("!", StringComparison.Ordinal))
        {
            var term = text[1..].Trim();
            return term.Length == 0 ? null : (term, true);
        }
        return (text, false);
    }

    private static bool MatchesTextFilter(string text, string term, bool negate)
    {
        var contains = text.Contains(term, StringComparison.OrdinalIgnoreCase);
        return negate ? !contains : contains;
    }

    private static int CompareValues(string left, string right, string type)
    {
        if (type.Equals("number", StringComparison.OrdinalIgnoreCase))
        {
            var leftOk = double.TryParse(left, NumberStyles.Any, CultureInfo.InvariantCulture, out var leftNumber);
            var rightOk = double.TryParse(right, NumberStyles.Any, CultureInfo.InvariantCulture, out var rightNumber);
            return (leftOk ? leftNumber : double.NegativeInfinity).CompareTo(rightOk ? rightNumber : double.NegativeInfinity);
        }
        if (type.Equals("datetime", StringComparison.OrdinalIgnoreCase))
        {
            var leftOk = DateTimeOffset.TryParse(left, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var leftDate);
            var rightOk = DateTimeOffset.TryParse(right, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var rightDate);
            return (leftOk ? leftDate : DateTimeOffset.MinValue).CompareTo(rightOk ? rightDate : DateTimeOffset.MinValue);
        }
        return StringComparer.CurrentCultureIgnoreCase.Compare(left, right);
    }

    private static string ValueForSort(ReportGridSnapshotRow row, string key)
        => Value(row, key, preferRaw: true);

    private static string Value(ReportGridSnapshotRow row, string key, bool preferRaw)
    {
        if (preferRaw && row.RawValues.TryGetValue(key, out var raw) && !string.IsNullOrEmpty(raw)) return raw;
        return row.Values.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;
    }

    private static Dictionary<string, string> ReadStringDictionary(JsonObject? node)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (node is null) return result;
        foreach (var pair in node)
            result[pair.Key] = pair.Value?.ToString() ?? string.Empty;
        return result;
    }

    private static ReportGridSnapshotRow ToSnapshotRow(GridRow row) => new()
    {
        Id = row.Id,
        Values = new Dictionary<string, string>(row.Values, StringComparer.OrdinalIgnoreCase),
        RawValues = new Dictionary<string, string>(row.RawValues, StringComparer.OrdinalIgnoreCase)
    };

    private static T Deserialize<T>(string json, string label) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw new InvalidOperationException($"The {label} is empty."); }
        catch (JsonException ex) { throw new InvalidOperationException($"The {label} is invalid.", ex); }
    }

    private static string Query(ReportSourceContext context, string key, string fallback = "")
        => context.Query != null && context.Query.TryGetValue(key, out var value) ? value ?? fallback : fallback;

    private static int? QueryInt(ReportSourceContext context, string key)
        => int.TryParse(Query(context, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static string? ExtractMessage(object? value)
    {
        if (value is null) return null;
        var node = JsonSerializer.SerializeToNode(value, JsonOptions) as JsonObject;
        return node?["message"]?.GetValue<string>();
    }
}
