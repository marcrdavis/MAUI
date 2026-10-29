// Author: Marc R. Davis (marcdavis@comcast.net)
// Scalar-only Task definition editing. Shared by validation and persistence; no independent REST/COM transport.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;
using MoveItAutomationUI.Services.MoveIt.Tasks.AdvancedTasks;

namespace MoveItAutomationUI.Services.MoveIt.Tasks;

public sealed record TaskJsonValueChange(string[] Path, JsonNode? Value);
public sealed record TaskJsonEditorDocument(string Id, string Name, string Kind, string EditVersion,
    JsonObject Definition, IReadOnlyList<string[]> ReadOnlyPaths, bool CheckoutObjectsForDurationOfEdit);
public sealed record TaskJsonEditRequest(string EditVersion, List<TaskJsonValueChange> Changes);

/// <summary>Existing scalar paths only. Object/array topology, native identities and masked credentials never round-trip as edits.</summary>
public static class TaskJsonValues
{
    public const int MaximumDefinitionBytes = 1_048_576;
    public const int MaximumChanges = 2000;
    public const int MaximumNodes = 20000;
    public const int MaximumDepth = 48;
    private static readonly HashSet<string> Managed = new(StringComparer.OrdinalIgnoreCase)
    { "ID", "TT", "Type", "NextEID", "Group", "Secure", "ClonedFromTaskID", "Scheduled", "Renamed", "xAlter", "xAddEditDelete", "xRun",
      "InvalidOverride", "HostID", "TaskID", "ScriptID", "ClientKeyID", "UseDefUser", "UseOAuth", "SyF", "SyT", "SyDel", "UseLCaseComp", "Multi" };
    private static readonly HashSet<string> Binary = new(StringComparer.OrdinalIgnoreCase)
    { "Active", "AR", "Disabled", "OnlyUntilFirstSuccess", "FailIfNoSuccessInSched", "RunEvenIfNotif", "Secure", "Watched",
      "NewFilesOnly", "DeleteOrig", "Unzip", "Zip", "IsHTML", "IsDest", "SearchSubdirs", "UseOrigName", "ForceDir", "OverwriteOrig",
      "UseRelativeSubdirs", "Wait", "DoIfSuccess", "DoIfFailure", "DoIfNoAction", "UseDefStateCaching" };

    public static MoveItTaskKind Kind(JsonObject task)
    {
        var type = MoveItRestClient.GetString(task, "TT").Trim();
        return MoveItTaskKinds.Parse(type.Length == 0 ? "Tr" : type);
    }
    public static JsonObject Project(JsonObject task, string nativeXml)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(task.ToJsonString()) > MaximumDefinitionBytes)
            throw new InvalidOperationException("This Task definition is too large for the JSON Editor.");
        var result = task.DeepClone().AsObject();
        var schedules = TraditionalTaskScheduleXml.ReadSchedules(nativeXml);
        // REST can collapse an overnight pair. Expose the authoritative native pair and preserve its XML on Save.
        if (schedules.Any(s => s["Multi"]?.ToString() is "1" or "2"))
            result["Schedules"] = new JsonObject { ["Schedule"] = new JsonArray(schedules.Select(s => (JsonNode?)s.DeepClone()).ToArray()) };
        _ = ReadOnlyPaths(result, redact: false);
        return result;
    }
    public static IReadOnlyList<string[]> ReadOnlyPaths(JsonObject document, bool redact = true)
    {
        var paths = new List<string[]>(); var count = 0;
        void Visit(JsonNode? node, string[] path, bool inherited, bool hidden)
        {
            if (++count > MaximumNodes || path.Length > MaximumDepth) throw new InvalidOperationException("This Task definition is too large for the JSON Editor.");
            if (node is JsonObject obj)
            {
                foreach (var pair in obj.ToArray())
                {
                    var secret = hidden || Secret(pair.Key) || (obj.Any(p => p.Key.Equals("Secure", StringComparison.OrdinalIgnoreCase) && (p.Value?.ToString() is "1" or "true" or "True"))
                        && (pair.Key.Equals("value", StringComparison.OrdinalIgnoreCase) || pair.Key.Equals("DisplayValue", StringComparison.OrdinalIgnoreCase)));
                    Visit(pair.Value, [.. path, pair.Key], inherited || Managed.Contains(pair.Key) || pair.Key.StartsWith("__", StringComparison.Ordinal), secret);
                }
            }
            else if (node is JsonArray array)
                for (var i = 0; i < array.Count; i++) Visit(array[i], [.. path, i.ToString(CultureInfo.InvariantCulture)], inherited, hidden);
            else if (inherited || hidden || node is null || UnsafeNumber(node))
            {
                paths.Add(path);
                if (hidden && redact) Set(document, path, null);
            }
        }
        Visit(document, [], false, false); return paths;
    }
    private static bool Secret(string key) => AdvancedTaskDefinition.IsSecret(key) || key.Equals("PrivateKey", StringComparison.OrdinalIgnoreCase)
        || key.Equals("AccessToken", StringComparison.OrdinalIgnoreCase) || key.Equals("RefreshToken", StringComparison.OrdinalIgnoreCase)
        || key.Equals("Token", StringComparison.OrdinalIgnoreCase);
    private static bool UnsafeNumber(JsonNode node)
        => KindOf(node) == JsonValueKind.Number && (!decimal.TryParse(node.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)
            || n > 9007199254740991m || n < -9007199254740991m);
    public static JsonValueKind KindOf(JsonNode? node)
    { using var doc = JsonDocument.Parse(node?.ToJsonString() ?? "null"); return doc.RootElement.ValueKind; }
    public static string Key(IEnumerable<string> path) => JsonSerializer.Serialize(path);
    public static JsonNode? Get(JsonNode document, IReadOnlyList<string> path)
    {
        JsonNode? value = document;
        foreach (var segment in path)
        {
            if (value is JsonObject obj && obj.TryGetPropertyValue(segment, out var child)) value = child;
            else if (value is JsonArray array && int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                && index >= 0 && index < array.Count && segment == index.ToString(CultureInfo.InvariantCulture)) value = array[index];
            else throw new InvalidOperationException("An edited element no longer exists. Reload this Task.");
        }
        return value;
    }
    private static void Set(JsonNode document, string[] path, JsonNode? value)
    {
        if (path.Length == 0) throw new InvalidOperationException("Select an existing Task value.");
        var parent = Get(document, path[..^1]);
        if (parent is JsonObject obj && obj.ContainsKey(path[^1])) obj[path[^1]] = value?.DeepClone();
        else if (parent is JsonArray array && int.TryParse(path[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var i) && i >= 0 && i < array.Count)
            array[i] = value?.DeepClone();
        else throw new InvalidOperationException("An edited element no longer exists. Reload this Task.");
    }
    public static JsonObject Apply(JsonObject document, IReadOnlyList<TaskJsonValueChange> changes)
    {
        if (changes.Count is 0 or > MaximumChanges) throw new InvalidOperationException($"Select between 1 and {MaximumChanges} changed values per Task.");
        var proposed = document.DeepClone().AsObject();
        var readOnly = ReadOnlyPaths(proposed, redact: false).Select(Key).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in changes)
        {
            if (change is null) throw new InvalidOperationException("A changed Task value is missing.");
            var path = change.Path;
            if (path is null || path.Length is 0 or > MaximumDepth || path.Any(p => p is null || p.Length > 256))
                throw new InvalidOperationException("Select a valid existing Task value.");
            if (!seen.Add(Key(path))) throw new InvalidOperationException("An element was included more than once.");
            var old = Get(document, path);
            if (old is JsonObject or JsonArray || readOnly.Contains(Key(path)))
                throw new InvalidOperationException("Identity, reference, protected and runtime fields are read-only.");
            var kind = KindOf(old); var nextKind = KindOf(change.Value);
            var boolean = kind is JsonValueKind.True or JsonValueKind.False;
            if (boolean ? nextKind is not (JsonValueKind.True or JsonValueKind.False)
                : kind != nextKind || kind is not (JsonValueKind.String or JsonValueKind.Number))
                throw new InvalidOperationException("The replacement must keep the existing JSON value type.");
            if (change.Value is null || UnsafeNumber(change.Value) || change.Value.ToJsonString().Length > 65536)
                throw new InvalidOperationException("The replacement value is too large or unsupported.");
            var name = path[^1];
            if (kind == JsonValueKind.Number && long.TryParse(old!.ToJsonString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                && !long.TryParse(change.Value.ToJsonString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                throw new InvalidOperationException("This element requires a whole number.");
            if ((Binary.Contains(name) || name.StartsWith("UseDef", StringComparison.OrdinalIgnoreCase))
                && (kind is JsonValueKind.Number or JsonValueKind.String) && change.Value.ToString() is not ("0" or "1"))
                throw new InvalidOperationException("This element must be 0 or 1.");
            if (name is "Name" or "FileMask" && string.IsNullOrWhiteSpace(change.Value.ToString()))
                throw new InvalidOperationException("Task Name and Source File(s) cannot be empty.");
            if (name.Equals("CacheNames", StringComparison.OrdinalIgnoreCase))
            {
                if (change.Value.ToString() is not ("random" or "orig"))
                    throw new InvalidOperationException("Cache File Names must be random or orig.");
                _ = TraditionalTaskDefinitionEditorOperations.ResolveCacheNames(change.Value.ToString());
            }
            Set(proposed, path, change.Value);
        }
        // Validate only changed Source/Destination profiles; unchanged and future fields remain intact.
        var checkedSteps = new HashSet<string>(StringComparer.Ordinal);
        foreach (var change in changes)
        {
            for (var i = 0; i < change.Path.Length - 1; i++)
            {
                var kind = change.Path[i];
                if (kind is not ("Source" or "Destination")) continue;
                var stepPath = change.Path[..(i + 1)];
                if (!checkedSteps.Add(Key(stepPath))) continue;
                if (Get(proposed, stepPath) is not JsonObject step) continue;
                var patch = step.DeepClone().AsObject();
                foreach (var key in patch.Select(pair => pair.Key).Where(Secret).ToArray()) patch.Remove(key);
                var type = MoveItRestClient.GetString(step, "Type");
                TraditionalTaskDefinitionEditorOperations.ValidateHostStepPatch(kind, type, patch, false);
            }
        }
        if (proposed.ContainsKey("CacheNames")) proposed["CacheNames"] = TraditionalTaskDefinitionEditorOperations.ResolveCacheNames(proposed["CacheNames"]?.ToString() ?? "");
        return proposed;
    }
    public static bool Equivalent(JsonNode? expected, JsonNode? actual, string field = "")
    {
        if (JsonNode.DeepEquals(expected, actual)) return true;
        if (expected is null) return actual is null;
        var expectedKind = KindOf(expected); var actualKind = KindOf(actual);
        if (expectedKind is JsonValueKind.True or JsonValueKind.False || actualKind is JsonValueKind.True or JsonValueKind.False)
        {
            static bool FlagText(JsonNode? value) => (value?.ToString() ?? "").ToLowerInvariant() is "" or "0" or "1" or "true" or "false" or "yes" or "no";
            return FlagText(expected) && FlagText(actual)
                && TraditionalTaskDefinitionEditorOperations.ParseBool(expected) == TraditionalTaskDefinitionEditorOperations.ParseBool(actual);
        }
        if (TraditionalTaskDefinitionEditorOperations.EquivalentScalar(field, expected, actual)) return true;
        if (expectedKind == JsonValueKind.String && actualKind is JsonValueKind.String or JsonValueKind.Null)
        {
            try { TraditionalTaskDefinitionEditorOperations.VerifyTextValue("the edited value", expected.ToString(), actual?.ToString() ?? ""); return true; }
            catch (InvalidOperationException) { return false; }
        }
        return false;
    }
    public static void Verify(JsonObject saved, IReadOnlyList<TaskJsonValueChange> changes)
    {
        foreach (var change in changes)
        {
            // MOVEit may omit a scalar default; its containing object/array must still exist.
            var parent = Get(saved, change.Path[..^1]);
            var actual = parent is JsonObject obj ? obj[change.Path[^1]] : Get(saved, change.Path);
            if (!Equivalent(change.Value, actual, change.Path[^1]))
                throw new InvalidOperationException("MOVEit did not retain an edited Task value. Reload this Task before retrying.");
        }
    }
    internal static string NativeSchedules(string originalXml, IReadOnlyList<TaskJsonValueChange> changes)
    {
        var root = XElement.Parse(originalXml); var schedules = root.Element("Schedules")
            ?? throw new InvalidOperationException("The native Task schedules are unavailable.");
        foreach (var change in changes.Where(c => c.Path[0] == "Schedules"))
        {
            XElement node = schedules; var path = change.Path;
            for (var i = 1; i < path.Length; i++)
            {
                var name = path[i];
                if (i == path.Length - 1)
                {
                    var attribute = node.Attribute(name);
                    // ToJsonSchedule supplies native default flag attributes even when omitted in XML.
                    if (attribute is not null || node.Element(name) is null) node.SetAttributeValue(name, change.Value?.ToString());
                    else node.Element(name)!.Value = change.Value?.ToString() ?? "";
                }
                else if (int.TryParse(path[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                {
                    node = node.Elements(name).ElementAtOrDefault(index) ?? throw new InvalidOperationException("The native Schedule element is unavailable."); i++;
                    if (i == path.Length - 1) node.Value = change.Value?.ToString() ?? "";
                }
                else node = node.Element(name) ?? throw new InvalidOperationException("The native Schedule element is unavailable.");
            }
        }
        return schedules.ToString(SaveOptions.DisableFormatting);
    }
}
