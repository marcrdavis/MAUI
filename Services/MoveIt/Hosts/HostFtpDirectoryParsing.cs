using System.Globalization;
using System.Text.Json.Nodes;

namespace MoveItAutomationUI.Services.MoveIt.Hosts;

// The editor mode is a MAUI command; only native parsing fields enter the v0 PUT.
// Inactive parsing settings are omitted, matching the captured MOVEit definitions.
internal static class HostFtpDirectoryParsing
{
    public const string ModeKey = "DirectoryParsingMode";
    public static readonly string[] Columns = { "CustomDirSkipTop", "CustomDirSkipBottom", "CustomDirColFilename", "CustomDirColDate" };
    public static bool IsField(string key) => key is ModeKey or "DirScriptID" || Columns.Contains(key, StringComparer.Ordinal);
    private static string Text(JsonObject host, string key) => HostResourceContracts.Text(host, key);
    public static string Mode(JsonObject host) => Text(host, "CustomDir") is "1" or "true" ? "custom"
        : Text(host, "DirScriptID") is not ("" or "0") ? "script" : "automatic";
    private static bool Requested(JsonObject patch) => patch.Any(pair => IsField(pair.Key));

    public static void Apply(JsonObject host, JsonObject patch)
    {
        if (!Requested(patch)) return;
        var mode = patch.ContainsKey(ModeKey) ? Text(patch, ModeKey) : Mode(host);
        if (mode is not ("automatic" or "custom" or "script"))
            throw new InvalidOperationException("Select a supported FTP directory parsing mode.");
        if (mode != "custom" && Columns.Any(patch.ContainsKey))
            throw new InvalidOperationException("Directory parsing columns apply only to custom parsing.");
        if (mode != "script" && patch.ContainsKey("DirScriptID"))
            throw new InvalidOperationException("A directory parsing script applies only to script parsing.");
        var script = patch.ContainsKey("DirScriptID") ? Text(patch, "DirScriptID") : Text(host, "DirScriptID");
        if (mode == "script" && (!long.TryParse(script, NumberStyles.None, CultureInfo.InvariantCulture, out var scriptId) || scriptId <= 0))
            throw new InvalidOperationException("Select a directory parsing Script before saving.");
        var columns = new Dictionary<string, long>();
        if (mode == "custom") foreach (var key in Columns)
        {
            var text = patch.ContainsKey(key) ? Text(patch, key) : Text(host, key);
            if (text.Length == 0) text = "0";
            if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0)
                throw new InvalidOperationException("Directory parsing line and column values must be nonnegative whole numbers.");
            columns[key] = value;
        }
        foreach (var key in Columns.Concat(new[] { "CustomDir", "DirScriptID" })) Remove(host, key);
        if (mode == "custom")
        {
            host["CustomDir"] = 1;
            foreach (var column in columns) host[column.Key] = column.Value;
        }
        else if (mode == "script") host["DirScriptID"] = script;
    }

    public static bool HasChanges(JsonObject before, JsonObject after, JsonObject patch)
        => Requested(patch) && !Same(before, after);
    public static void Verify(JsonObject expected, JsonObject saved, JsonObject patch)
    {
        if (Requested(patch) && !Same(expected, saved))
            throw new InvalidOperationException("MOVEit did not persist the requested FTP directory parsing settings.");
    }
    private static bool Same(JsonObject left, JsonObject right)
    {
        var mode = Mode(left);
        if (mode != Mode(right)) return false;
        if (mode == "script") return Text(left, "DirScriptID") == Text(right, "DirScriptID");
        return mode != "custom" || Columns.All(key => long.TryParse(Text(left, key) is { Length: > 0 } aText ? aText : "0", out var a)
            && long.TryParse(Text(right, key) is { Length: > 0 } bText ? bText : "0", out var b) && a == b);
    }
    private static void Remove(JsonObject host, string key)
    {
        foreach (var name in host.Select(pair => pair.Key).Where(name => name.Equals(key, StringComparison.OrdinalIgnoreCase)).ToArray()) host.Remove(name);
    }
}
