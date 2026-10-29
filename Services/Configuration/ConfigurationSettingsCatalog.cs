// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Shared labels, keys, ordering, and supported values for Configuration > Settings.

using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.Configuration;

internal static class ConfigurationSettingsCatalog
{
    private static readonly (string Key, string Label)[] RefreshSections =
    {
        (TaskBehaviorOptions.SectionName, "Tasks"),
        (HostBehaviorOptions.SectionName, "Hosts"),
        (ScriptBehaviorOptions.SectionName, "Scripts"),
        (DateListBehaviorOptions.SectionName, "Date Lists"),
        (GlobalParameterBehaviorOptions.SectionName, "Global Parameters"),
        (KeyCertificateBehaviorOptions.SectionName, "Keys & Certificates")
    };

    private static readonly (string Key, string Label)[] ReportSections =
    {
        ("TaskRun", "Task Run"),
        ("FileActivity", "File Activity"),
        ("Audit", "Audit")
    };

    private static readonly (string Key, string Label)[] GridSections =
    {
        ("tasks", "Tasks"),
        ("upcomingruns", "Upcoming Runs"),
        ("hosts", "Hosts"),
        ("scripts", "Scripts"),
        ("datelists", "Date Lists"),
        ("globalparameters", "Global Parameters"),
        ("keysandcertificates", "Keys & Certificates"),
        ("taskruns", "Task Run"),
        ("fileactivity", "File Activity"),
        ("audit", "Audit"),
        ("logs", "Logs"),
        ("reports", "Reports")
    };

    private static readonly (string Key, string Label)[] SettingsNavigationSections =
    {
        ("general", "General"),
        ("editors", "Editors"),
        ("backup", "Backup"),
        ("scheduler", "Scheduler"),
        ("database", "Database"),
        ("moveit", "MOVEit Connections"),
        ("moveit-configuration", "MOVEit Configuration"),
        ("ssh-algorithms", "SSH/KEX Algorithms"),
        ("metadata", "Metadata"),
        ("grids", "Grid Definitions"),
        ("grid", "Grid Defaults"),
        ("refresh", "Refresh & Caching"),
        ("smtp", "SMTP Configuration"),
        ("security", "Security"),
        ("tags", "Tags"),
        ("experimental", "Experimental"),
        ("advanced", "Advanced")
    };

    public static readonly string[] RefreshSectionKeys = RefreshSections.Select(item => item.Key).ToArray();
    public static readonly IReadOnlyDictionary<string, string> RefreshLabels = ToLabelMap(RefreshSections);

    public static readonly string[] ReportSectionKeys = ReportSections.Select(item => item.Key).ToArray();
    public static readonly IReadOnlyDictionary<string, string> ReportLabels = ToLabelMap(ReportSections);

    public static readonly HashSet<int> AllowedReportBatchSizes = new() { 100, 200, 500, 1000, 2000, 5000, 10000 };

    public static readonly string[] GridSectionKeys = GridSections.Select(item => item.Key).ToArray();
    public static readonly IReadOnlyDictionary<string, string> GridLabels = ToLabelMap(GridSections);

    public static readonly string[] DefaultSettingsNavigationOrder = SettingsNavigationSections.Select(item => item.Key).ToArray();
    public static readonly IReadOnlyDictionary<string, string> SettingsNavigationLabels = ToLabelMap(SettingsNavigationSections);

    private static IReadOnlyDictionary<string, string> ToLabelMap(IEnumerable<(string Key, string Label)> sections)
        => sections.ToDictionary(item => item.Key, item => item.Label, StringComparer.OrdinalIgnoreCase);
}
