using System.Globalization;
using System.Text.Json.Nodes;

namespace MoveItAutomationUI.Services.MoveIt.Hosts;

// Native state-cache ownership shared by all verified Host write profiles.
internal static class HostStateCacheSettings
{
    internal static readonly IReadOnlyList<HostProfileField> Fields = new HostProfileField[]
    {
        new("UseDefStateCaching", "Use default state cache settings", "advanced", "Boolean"),
        new("StateCacheTime", "State Cache Time", "advanced", "Number"),
        new("StateCacheUnit", "State Cache Unit", "advanced", "Select", Choices: new HostProfileChoice[]
        {
            new("Minutes", "Minutes"), new("Hours", "Hours")
        })
    };

    internal static bool IsField(string key) => Fields.Any(field => field.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
    internal static bool IsRequested(JsonObject patch) => patch.Any(pair => IsField(pair.Key));

    internal static void Apply(JsonObject host, JsonObject patch)
    {
        if (!IsRequested(patch)) return;
        var defaultNode = HostDefinitionJson.GetNode(patch, "UseDefStateCaching")
            ?? HostDefinitionJson.GetNode(host, "UseDefStateCaching");
        var useDefault = defaultNode is null ? true : ReadFlag(defaultNode);
        if (useDefault)
        {
            Set(host, "UseDefStateCaching", JsonValue.Create(1));
            Remove(host, "StateCacheTime");
            Remove(host, "StateCacheUnit");
            return;
        }

        var timeText = HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(patch, "StateCacheTime")
            ?? HostDefinitionJson.GetNode(host, "StateCacheTime")).Trim();
        if (!int.TryParse(timeText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var time) || time < -1)
            throw new InvalidOperationException("State Cache Time must be -1 (Always), 0 (Until end of task run), or a positive whole number.");
        string? unit = null;
        if (time > 0)
        {
            var requestedUnit = HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(patch, "StateCacheUnit")
                ?? HostDefinitionJson.GetNode(host, "StateCacheUnit")).Trim();
            unit = Fields[2].Choices!.FirstOrDefault(choice => choice.Value.Equals(requestedUnit, StringComparison.OrdinalIgnoreCase))?.Value
                ?? throw new InvalidOperationException("State Cache Unit must be Minutes or Hours for a timed cache.");
        }
        Set(host, "UseDefStateCaching", JsonValue.Create(0));
        Set(host, "StateCacheTime", JsonValue.Create(time));
        if (unit is null) Remove(host, "StateCacheUnit");
        else Set(host, "StateCacheUnit", JsonValue.Create(unit));
    }

    internal static bool HasChanges(JsonObject before, JsonObject after, JsonObject patch)
        => IsRequested(patch) && Fields.Any(field => Text(before, field.Key) != Text(after, field.Key));

    internal static void Verify(JsonObject expected, JsonObject actual, JsonObject patch)
    {
        if (!IsRequested(patch)) return;
        if (ReadFlag(HostDefinitionJson.GetNode(expected, "UseDefStateCaching")) != ReadFlag(HostDefinitionJson.GetNode(actual, "UseDefStateCaching")))
            throw new InvalidOperationException("MOVEit did not persist the requested Host state-cache inheritance setting.");
        if (ReadFlag(HostDefinitionJson.GetNode(expected, "UseDefStateCaching"))) return;
        if (Text(expected, "StateCacheTime") != Text(actual, "StateCacheTime"))
            throw new InvalidOperationException("MOVEit did not persist the requested Host State Cache Time.");
        if (int.Parse(Text(expected, "StateCacheTime"), CultureInfo.InvariantCulture) > 0 && Text(expected, "StateCacheUnit") != Text(actual, "StateCacheUnit"))
            throw new InvalidOperationException("MOVEit did not persist the requested Host State Cache Unit.");
    }

    private static bool ReadFlag(JsonNode? node)
    {
        var text = HostDefinitionJson.ReadNode(node).Trim();
        if (text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (text == "0" || text.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        throw new InvalidOperationException("Use default state cache settings must be Yes or No.");
    }

    private static string Text(JsonObject host, string key) => HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(host, key)).Trim();
    private static void Remove(JsonObject host, string key)
    {
        foreach (var actualKey in host.Select(pair => pair.Key).Where(name => name.Equals(key, StringComparison.OrdinalIgnoreCase)).ToArray())
            host.Remove(actualKey);
    }
    private static void Set(JsonObject host, string key, JsonNode? value)
    {
        var actualKey = host.Select(pair => pair.Key).FirstOrDefault(name => name.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? key;
        Remove(host, key);
        host[actualKey] = value;
    }
}
