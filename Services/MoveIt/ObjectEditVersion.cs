// Opaque process-local revisions: no native configuration or credentials are sent to the browser.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace MoveItAutomationUI.Services.MoveIt;

public static class ObjectEditVersion
{
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);
    private static readonly AsyncLocal<Requirement?> Current = new();

    public static string Create(string type, string id, JsonNode definition)
        => Convert.ToHexString(HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(type + "\n" + id.Trim() + "\n" + Canonical(definition))));

    // Existing information models normalize surrounding whitespace; compare those same field semantics.
    public static JsonObject Named(string name, string description)
        => new() { ["Name"] = name.Trim(), ["Description"] = description.Trim() };

    public static JsonObject TaskInformation(string name, string description, string notes)
        => new() { ["Name"] = name.Trim(), ["Description"] = description.Trim(), ["Notes"] = notes.Trim() };

    public static string InformationValue(XElement root, params string[] names)
    {
        foreach (var parent in new[] { root.Element("Info"), root }.OfType<XElement>())
        {
            var attribute = parent.Attributes().FirstOrDefault(a => names.Contains(a.Name.LocalName, StringComparer.OrdinalIgnoreCase));
            if (attribute is not null) return attribute.Value;
            var element = parent.Elements().FirstOrDefault(e => names.Contains(e.Name.LocalName, StringComparer.OrdinalIgnoreCase));
            if (element is not null) return element.Value;
        }
        return "";
    }

    // REST permission/schedule status and COS branch UI annotations are not saved
    // configuration. Permission flags describe access, not Task configuration.
    private static readonly HashSet<string> TaskReadOnlyFields = new(
        ["xAddEditDelete", "xAlter", "xRun", "Renamed", "Scheduled"], StringComparer.OrdinalIgnoreCase);

    public static JsonObject Definition(JsonNode json, string xml)
    {
        var root = XElement.Parse(xml);
        var configuration = json.DeepClone();
        if (root.Name.LocalName == "Task")
        {
            configuration = TaskConfigurationJson(configuration);
            root.Attributes().Where(attribute => TaskReadOnlyFields.Contains(attribute.Name.LocalName)).Remove();
        }
        return new() { ["json"] = configuration, ["xml"] = Normalize(root).ToString(SaveOptions.DisableFormatting) };
    }

    private static JsonNode TaskConfigurationJson(JsonNode json)
    {
        var copy = json.DeepClone();
        if (copy is JsonObject task)
            foreach (var key in task.Select(pair => pair.Key).Where(TaskReadOnlyFields.Contains).ToArray()) task.Remove(key);
        RemoveBranchAnnotations(copy);
        return copy;
    }

    private static void RemoveBranchAnnotations(JsonNode? node)
    {
        if (node is not JsonObject task) return;
        void Visit(JsonNode? steps)
        {
            if (steps is not JsonArray array) return;
            foreach (var wrapper in array.OfType<JsonObject>())
            {
                if (wrapper["For"] is JsonObject loop) Visit(loop["steps"]);
                if (wrapper["If"] is not JsonObject conditional) continue;
                var branches = (conditional["When"] as JsonArray ?? []).OfType<JsonObject>().ToList();
                if (conditional["Otherwise"] is JsonObject otherwise) branches.Add(otherwise);
                foreach (var branch in branches)
                {
                    branch.Remove("__parentStepId"); branch.Remove("__branchIndex");
                    Visit(branch["steps"]);
                }
            }
        }
        Visit(task["steps"]);
    }

    public static bool IsRequired(string type) => Current.Value?.Type == type;

    public static JsonObject TaskInformation(JsonObject task)
    {
        var info = task["Info"] as JsonObject;
        return TaskInformation(MoveItRestClient.GetString(task, "Name"), MoveItRestClient.GetString(info, "Description"), MoveItRestClient.GetString(info, "Notes"));
    }

    public static bool Pending => Current.Value is { Validated: false };
    public static bool Active => Current.Value is not null;
    public static JsonValue Xml(XElement element) => JsonValue.Create(Normalize(element).ToString(SaveOptions.DisableFormatting))!;

    private static XElement Normalize(XElement element) => new(element.Name,
        element.Attributes().OrderBy(a => a.Name.ToString(), StringComparer.Ordinal).Select(a => new XAttribute(a)),
        element.Nodes().Where(n => n is not XText t || !element.HasElements || !string.IsNullOrWhiteSpace(t.Value))
            .Select(n => n is XElement child ? Normalize(child) : n));

    public static IDisposable Require(string type, string id, string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) throw new ObjectEditConflictException(type);
        var previous = Current.Value;
        Current.Value = new Requirement(type, id.Trim(), version);
        return new Restore(() => Current.Value = previous);
    }

    public static void Verify(string type, string id, JsonNode current)
    {
        var requirement = Current.Value;
        if (requirement is null || requirement.Validated) return; // Non-editor workflows already patch a fresh definition.
        if (requirement.Type != type || !requirement.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase)
            || requirement.Version != Create(type, id, current)) throw new ObjectEditConflictException(type);
        // Only mark after the comparison under checkout, not during preflight reads.
        requirement.Validated = true;
    }

    public static void Persisted(string type, string id, JsonNode current)
    {
        if (Current.Value is { } requirement && requirement.Type == type && requirement.Id == id.Trim())
        {
            requirement.SavedVersion = Create(type, id, current);
            requirement.SavedDefinition = current.DeepClone();
        }
    }

    public static void VerifyNativeSchedules(string xml)
    {
        if (Current.Value?.SavedDefinition is not JsonObject saved || saved["xml"] is null) return;
        static string Schedules(string value) => Normalize(XElement.Parse(value).Element("Schedules") ?? new XElement("Schedules")).ToString(SaveOptions.DisableFormatting);
        if (Schedules(saved["xml"]!.GetValue<string>()) != Schedules(xml)) throw new ObjectEditConflictException("Task Schedule");
    }

    public static void CompleteNativeSchedules(string id, JsonObject current, string xml)
    {
        if (Current.Value?.SavedDefinition is not JsonObject saved) return;
        static JsonObject WithoutSchedules(JsonObject value)
        {
            var copy = TaskConfigurationJson(value).AsObject(); copy.Remove("Schedules"); copy.Remove("NextEID"); return copy;
        }
        if (Canonical(WithoutSchedules(saved["json"]!.AsObject())) != Canonical(WithoutSchedules(current)))
            throw new ObjectEditConflictException("Task");
        static string WithoutNativeSchedules(string value)
        {
            var root = XElement.Parse(value);
            root.Elements().Where(e => e.Name.LocalName == "Schedules").Remove();
            root.Attribute("NextEID")?.Remove();
            root.Attributes().Where(attribute => TaskReadOnlyFields.Contains(attribute.Name.LocalName)).Remove();
            return Normalize(root).ToString(SaveOptions.DisableFormatting);
        }
        if (WithoutNativeSchedules(saved["xml"]!.GetValue<string>()) != WithoutNativeSchedules(xml))
            throw new ObjectEditConflictException("Task");
        Persisted("Task", id, Definition(current, xml));
    }

    public static string? SavedVersion => Current.Value?.SavedVersion;

    private static string Canonical(JsonNode? node) => node switch
    {
        JsonObject obj => "{" + string.Join(",", obj.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => System.Text.Json.JsonSerializer.Serialize(p.Key) + ":" + Canonical(p.Value))) + "}",
        JsonArray array => "[" + string.Join(",", array.Select(Canonical)) + "]",
        _ => node?.ToJsonString() ?? "null"
    };
    private sealed class Requirement(string type, string id, string version)
    {
        public string Type { get; } = type;
        public string Id { get; } = id;
        public string Version { get; } = version;
        public bool Validated { get; set; }
        public string? SavedVersion { get; set; }
        public JsonNode? SavedDefinition { get; set; }
    }
    private sealed class Restore(Action restore) : IDisposable { public void Dispose() => restore(); }
}

public sealed class ObjectEditConflictException(string type)
    : InvalidOperationException($"This {type} changed in MOVEit or its editing session expired. Saving is blocked to prevent overwriting changes without review.");
