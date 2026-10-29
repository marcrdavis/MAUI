using System.Text.Json.Nodes;
using System.Xml.Linq;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Features.EnvironmentCopy;

// Pure planning rules are shared by Analyze/Resolve/Review and rechecked before execution.
public static class EnvironmentCopyRules
{
    internal static JsonValue RevisionXml(string xml)
    {
        var root = XElement.Parse(xml);
        root.Attributes().Where(a => a.Name.LocalName.StartsWith('x') || a.Name.LocalName is "Renamed" or "UsedByCount" or "OverridesTotal" or "OverridesNoPerm" or "Scheduled").Remove();
        return ObjectEditVersion.Xml(root);
    }
    public static string Type(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "task" => "Task", "host" => "Host", "script" => "Script", "datelist" => "DateList", "keycertificate" => "KeyCertificate",
        _ => throw new ArgumentException("Unsupported object type for Copy Between Environments.")
    };
    public static string Name(string? value)
    {
        var name = value?.Trim() ?? "";
        if (name.Length is 0 or > 255 || name.Any(char.IsControl)) throw new ArgumentException("Destination names must contain 1–255 characters and no control characters.");
        return name;
    }
    public static string NewName(string original, string type, string prefix, string suffix, string scope, bool unique, ISet<string> occupied)
    {
        if (scope is not ("tasks" or "tasks-hosts" or "all")) throw new ArgumentException("Choose a valid naming scope.");
        var applies = type == "Task" || type == "Host" && scope != "tasks" || scope == "all";
        var desired = Name(applies ? prefix + original + suffix : original);
        if (!unique || !occupied.Contains(desired)) return desired;
        for (var index = 2; index < 100000; index++)
        {
            var end = $" ({index})";
            var candidate = desired[..Math.Min(desired.Length, 255 - end.Length)] + end;
            if (!occupied.Contains(candidate)) return candidate;
        }
        throw new InvalidOperationException("Unable to allocate a unique destination name.");
    }
    public static string? ReferenceType(string name) => name.ToLowerInvariant() switch
    {
        "hostid" => "Host", "taskid" => "Task", "datelistid" or "scheddatelistid" => "DateList",
        "scriptid" or "customscriptid" or "dirscriptid" => "Script", _ => null
    };
    // Only structured native attributes are rewritten. Script source, macro text and descriptions remain literal.
    private static string? ReferenceType(XAttribute attribute) => attribute.Name.LocalName == "ID" && attribute.Parent?.Name.LocalName == "DateListRef"
        ? "DateList" : ReferenceType(attribute.Name.LocalName);
    public static IReadOnlyList<CopySelection> References(string xml)
        => XElement.Parse(xml).DescendantsAndSelf().Attributes()
            .Select(a => new { Type = ReferenceType(a), Value = a.Value.Trim() })
            .Where(a => a.Type is not null && a.Value.Length > 0 && (!long.TryParse(a.Value, out var number) || number > 0 || number == 0 && a.Type == "Host"))
            .Select(a => new CopySelection(a.Type!, a.Value)).Distinct().ToArray();
    public static string Rewrite(string xml, string id, string name, IReadOnlyDictionary<string, string> mappings, IReadOnlyList<string>? groups = null, bool disableTask = false)
    {
        var root = XElement.Parse(xml);
        foreach (var attribute in root.DescendantsAndSelf().Attributes())
        {
            var type = ReferenceType(attribute);
            if (type is not null && mappings.TryGetValue(type + "|" + attribute.Value.Trim(), out var mapped)) attribute.Value = mapped;
            var keyPrefix = attribute.Name.LocalName switch { "ClientKeyID" or "DefClientKeyID" => "ssh|", "ClientCertSHA1" or "DefClientCertSHA1" => "tls|", _ => "" };
            if (keyPrefix.Length > 0 && mappings.TryGetValue("KeyCertificate|" + keyPrefix + attribute.Value.Trim(), out var key)) attribute.Value = key[(key.IndexOf('|') + 1)..];
        }
        foreach (var parameter in root.Descendants("Parameter"))
        {
            var parameterName = (string?)parameter.Attribute("Name") ?? "";
            if (!parameterName.StartsWith("PGP", StringComparison.OrdinalIgnoreCase) || !parameterName.Contains("Key", StringComparison.OrdinalIgnoreCase)) continue;
            var value = (string?)parameter.Attribute("value") ?? parameter.Value;
            if (mappings.TryGetValue("KeyCertificate|pgp|" + value.Trim(), out var key))
            { var mapped = key[(key.IndexOf('|') + 1)..]; if (parameter.Attribute("value") is { } attr) attr.Value = mapped; else parameter.Value = mapped; }
        }
        root.SetAttributeValue("ID", id); root.SetAttributeValue("Name", Name(name));
        if (disableTask && root.Name.LocalName == "Task") root.SetAttributeValue("Active", "0");
        if (groups is not null)
        {
            root.Elements().Where(e => e.Name.LocalName == "Group").Remove();
            foreach (var group in groups) root.Add(new XElement("Group", group));
        }
        return root.ToString(SaveOptions.DisableFormatting);
    }
    public static IReadOnlyList<string> Order(IReadOnlyDictionary<string, IReadOnlyList<string>> graph)
    {
        var result = new List<string>(); var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string key)
        {
            if (visited.Contains(key)) return;
            if (!active.Add(key)) throw new InvalidOperationException("Referenced Tasks form a cycle. Map at least one Task in the cycle to an existing destination Task before copying.");
            if (!graph.TryGetValue(key, out var edges)) throw new InvalidOperationException("A required dependency has no destination mapping.");
            foreach (var dependency in edges) Visit(dependency);
            active.Remove(key); visited.Add(key); result.Add(key);
        }
        foreach (var key in graph.Keys.Order(StringComparer.OrdinalIgnoreCase)) Visit(key);
        return result;
    }
    public static IReadOnlyDictionary<string, string> RedactedProperties(JsonObject node)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        void Walk(JsonNode? value, string path, bool secret = false)
        {
            if (value is JsonObject obj)
            {
                var secure = obj.FirstOrDefault(pair => pair.Key.Equals("Secure", StringComparison.OrdinalIgnoreCase)).Value?.ToString() is "1" or "true";
                foreach (var pair in obj)
                {
                    if (pair.Key is "EditVersion" or "UsedByCount" || pair.Key.StartsWith('x')) continue;
                    var sensitive = secret || IsSecret(pair.Key) || secure && (pair.Key.Equals("value", StringComparison.OrdinalIgnoreCase) || pair.Key.Equals("DisplayValue", StringComparison.OrdinalIgnoreCase));
                    if (sensitive) result[path + pair.Key] = "Not compared (protected value)";
                    else Walk(pair.Value, path + pair.Key + ".");
                }
            }
            else if (value is JsonArray array)
                for (var i = 0; i < array.Count; i++) Walk(array[i], path + i + ".", secret);
            else result[path.TrimEnd('.')] = value?.ToString() ?? "";
        }
        Walk(node, ""); return result;
    }
    public static bool IsSecret(string name) => name.Contains("password", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Account", StringComparison.OrdinalIgnoreCase) || name.Equals("DefAccount", StringComparison.OrdinalIgnoreCase)
        || name.Contains("secret", StringComparison.OrdinalIgnoreCase) || name.Contains("token", StringComparison.OrdinalIgnoreCase)
        || name.Contains("passphrase", StringComparison.OrdinalIgnoreCase) || name.Contains("keymaterial", StringComparison.OrdinalIgnoreCase)
        || name.Contains("private", StringComparison.OrdinalIgnoreCase);
}
