using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.Reporting;

public static class ReportCsvWriter
{
    public static string Write(OutboundReportData data)
    {
        var lines = new List<string>
        {
            string.Join(',', data.Columns.Select(column => Escape(Protect(column.Label, "text"))))
        };
        foreach (var row in data.Rows)
            lines.Add(string.Join(',', data.Columns.Select(column =>
                Escape(Protect(row.Values.GetValueOrDefault(column.Key) ?? string.Empty, column.Type)))));
        return string.Join("\r\n", lines);
    }

    public static string WriteEmail(string title, string description, OutboundReportData data)
        => string.Join("\r\n", Escape(Protect(title, "text")), Escape(Protect(description, "text")),
            Escape($"Generated: {DateTimeOffset.Now:O}"), string.Empty, Write(data));

    private static string Protect(string value, string type)
        => !type.Equals("number", StringComparison.OrdinalIgnoreCase) && value.Length > 0
            && "=+-@".Contains(value[0]) ? $"'{value}" : value;

    private static string Escape(string value)
        => value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
}
