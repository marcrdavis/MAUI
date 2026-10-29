using System.Globalization;
using System.Text.Json.Nodes;

namespace MoveItAutomationUI.Services.MoveIt.Hosts;

// Shared native Host JSON interpretation. UI and save policy use the same helpers.
internal static class HostDefinitionJson
{
    internal static (string TypeCode, JsonObject Host) UnwrapHost(JsonObject wrapper)
    {
        foreach (var key in new[] { "FileSystem", "Share", "siLock", "SiLock", "FTP", "SSHFTP", "POP3", "SMTP", "S3", "AzureBlob", "SharePoint", "GoogleStorage", "OneDrive", "AS1", "AS2", "AS3" })
        {
            if (GetNode(wrapper, key) is JsonObject typed) return (NormalizeTypeCode(key), typed);
        }
        return (NormalizeTypeCode(ReadNode(GetNode(wrapper, "Type"))), wrapper);
    }

    internal static JsonNode? GetNode(JsonObject? obj, string name)
    {
        if (obj is null) return null;
        foreach (var pair in obj)
            if (pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)) return pair.Value;
        return null;
    }

    internal static string ReadNode(JsonNode? node)
    {
        if (node is null) return string.Empty;
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text)) return text ?? string.Empty;
            if (value.TryGetValue<int>(out var intValue)) return intValue.ToString(CultureInfo.InvariantCulture);
            if (value.TryGetValue<long>(out var longValue)) return longValue.ToString(CultureInfo.InvariantCulture);
            if (value.TryGetValue<decimal>(out var decimalValue)) return decimalValue.ToString(CultureInfo.InvariantCulture);
            if (value.TryGetValue<double>(out var doubleValue)) return doubleValue.ToString(CultureInfo.InvariantCulture);
            if (value.TryGetValue<bool>(out var boolValue)) return boolValue ? "true" : "false";
        }
        if (node is JsonArray array) return string.Join(", ", array.Select(ReadNode).Where(item => item.Length > 0));
        return node.ToJsonString();
    }

    private static string NormalizeTypeCode(string? typeCode)
    {
        var value = (typeCode ?? string.Empty).Trim();
        if (value.Equals("SiLock", StringComparison.OrdinalIgnoreCase)) return "siLock";
        return value;
    }
}
