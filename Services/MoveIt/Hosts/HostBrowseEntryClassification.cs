using System.Text.Json.Nodes;

namespace MoveItAutomationUI.Services.MoveIt.Hosts;

// Files.File can contain directories. Use the native entry flag before the collection default.
internal static class HostBrowseEntryClassification
{
    internal static bool IsDirectory(JsonObject? entry, bool defaultDirectory)
    {
        var flag = HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(entry, "IsDir")).Trim();
        if (flag.Length == 0) return defaultDirectory;
        return flag == "1" || flag.Equals("true", StringComparison.OrdinalIgnoreCase)
            || flag.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}
