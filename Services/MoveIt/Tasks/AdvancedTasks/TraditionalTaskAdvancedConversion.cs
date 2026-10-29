using System.Globalization;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using static MoveItAutomationUI.Services.MoveIt.TraditionalTasks.TraditionalTaskDefinitionEditorOperations;

namespace MoveItAutomationUI.Services.MoveIt.Tasks.AdvancedTasks;

// One conversion plan owns topology for both native creation and captured JSON verification.
// Native XML templates retain credentials, secure parameters and version-specific properties.
public sealed class TraditionalTaskAdvancedConversion
{
    public JsonObject Definition { get; }
    private readonly JsonObject _source;
    private readonly Dictionary<string, string> _stepTemplates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _actionTemplates = new(StringComparer.Ordinal);
    private static readonly string[] Outcomes = ["Success", "Failure", "No xfers"];
    private static readonly string[] Conditions = ["DoIfSuccess", "DoIfFailure", "DoIfNoAction"];

    private TraditionalTaskAdvancedConversion(JsonObject source, string name)
    {
        _source = source.DeepClone().AsObject();
        var type = GetString(source, "TT").Trim();
        if (type.Length > 0 && !type.Equals("Tr", StringComparison.OrdinalIgnoreCase)
            && !type.Equals("Traditional", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Create Advanced Task requires a Traditional Task.");
        if (name.Trim().Length is 0 or > 254) throw new InvalidOperationException("Task Name must contain 1–254 characters.");
        var steps = (source["steps"] as JsonArray ?? []).OfType<JsonObject>().ToList();
        if (steps.Any(step => !new[] { "Source", "Process", "Destination" }.Contains(AdvancedTaskDefinition.Kind(step))))
            throw new InvalidOperationException("This Traditional Task contains a step without a verified Advanced conversion pattern.");
        var ids = steps.Select(step => GetString(step[AdvancedTaskDefinition.Kind(step)]!.AsObject(), "ID")).ToList();
        if (ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
            throw new InvalidOperationException("The Traditional Task has missing or duplicate step IDs.");
        foreach (var process in steps.Where(step => step["Process"] is JsonObject).Select(step => step["Process"]!.AsObject()))
            if (GetString(process, "Run") is { Length: > 0 } run && run is not ("PerFile" or "Once"))
                throw new InvalidOperationException("The Process run mode has no verified Advanced conversion pattern.");

        var reserved = new HashSet<long>();
        void Reserve(JsonNode? node)
        {
            if (node is JsonObject obj)
                foreach (var field in obj)
                {
                    if (field.Key == "ID" && long.TryParse(field.Value?.ToString(), out var id)) reserved.Add(id);
                    else Reserve(field.Value);
                }
            else if (node is JsonArray array) foreach (var item in array) Reserve(item);
        }
        foreach (var field in source.Where(field => field.Key is not ("ID" or "steps" or "NextActions"))) Reserve(field.Value);
        long next = ids.Select(id => long.TryParse(id, out var number) && number > 0 ? number : 1).DefaultIfEmpty(1).Min();
        string Allocate()
        {
            while (reserved.Contains(next)) next++;
            return (next++).ToString(CultureInfo.InvariantCulture);
        }
        JsonObject CopyStep(JsonObject wrapper)
        {
            var kind = AdvancedTaskDefinition.Kind(wrapper);
            var body = wrapper[kind]!.DeepClone().AsObject();
            var id = Allocate();
            _stepTemplates[id] = GetString(body, "ID");
            body["ID"] = id;
            return new JsonObject { [kind] = body };
        }
        var actions = Actions(source).Select((action, index) => (Action: action, Index: index)).ToList();
        foreach (var entry in actions)
        {
            if (GetString(entry.Action, "Type") is not ("Email" or "Task"))
                throw new InvalidOperationException("The Task contains a Next Action without a verified Advanced conversion pattern.");
            var scope = GetString(entry.Action, "DoAfter");
            if (scope.Length > 0 && scope is not ("Task" or "FileDone"))
                throw new InvalidOperationException("The Next Action timing has no verified Advanced conversion pattern.");
        }
        JsonObject CopyAction(JsonObject action, int index)
        {
            var body = action.DeepClone().AsObject();
            var kind = GetString(action, "Type") == "Email" ? "Email" : "RunTask";
            foreach (var key in Conditions.Concat(new[] { "DoAfter", "Type", "ID" })) body.Remove(key);
            var id = Allocate(); body["ID"] = id;
            if (body["Disabled"] is null) body["Disabled"] = 0;
            if (kind == "Email")
                foreach (var key in new[] { "UseDefRetryCount", "UseDefRetryTimeoutSecs", "UseDefConnTimeoutSecs", "UseDefDataTimeoutSecs" })
                    if (body[key] is null) body[key] = 1;
            if (kind == "RunTask" && body["Wait"] is null) body["Wait"] = 0;
            _actionTemplates[id] = index;
            return new JsonObject { [kind] = body };
        }
        void AppendActions(JsonArray target, List<(JsonObject Action, int Index)> entries, bool eachFile)
        {
            // File No Transfer has no Advanced equivalent. Even all-result file actions
            // use the captured numeric comparisons, never an unconditional fallback.
            var count = eachFile ? 2 : Conditions.Length;
            var conditional = entries.Any(entry => Enumerable.Range(0, count).Any(index => GetBool(entry.Action, Conditions[index]))
                && (eachFile || !Conditions.All(key => GetBool(entry.Action, key))));
            var ifId = conditional ? Allocate() : "";
            var branches = Enumerable.Range(0, count).Select(_ => new JsonArray()).ToArray();
            var order = new List<int>(); var unconditional = new JsonArray();
            // Allocate IDs in original action order; emit branches in first-use order.
            foreach (var entry in entries)
            {
                if (!eachFile && Conditions.All(key => GetBool(entry.Action, key)))
                    unconditional.Add(CopyAction(entry.Action, entry.Index));
                else for (var index = 0; index < count; index++)
                        if (GetBool(entry.Action, Conditions[index]))
                        {
                            if (branches[index].Count == 0) order.Add(index);
                            branches[index].Add(CopyAction(entry.Action, entry.Index));
                        }
            }
            if (conditional)
            {
                var when = new JsonArray();
                foreach (var index in order)
                {
                    var comparison = eachFile
                        ? new JsonObject { ["a"] = "[ErrorCodeFile]", ["b"] = 0, ["test"] = index == 0 ? "NEQ" : "NGT" }
                        : new JsonObject { ["a"] = "[TaskStatus]", ["b"] = Outcomes[index], ["test"] = "MASK" };
                    when.Add(new JsonObject
                    {
                        ["steps"] = branches[index], ["__parentStepId"] = ifId, ["__branchIndex"] = when.Count,
                        ["Criteria"] = new JsonObject { ["comp"] = comparison }
                    });
                }
                target.Add(new JsonObject { ["If"] = new JsonObject { ["ID"] = ifId, ["Disabled"] = 0, ["When"] = when } });
            }
            foreach (var action in unconditional.ToArray()) { unconditional.Remove(action); target.Add(action); }
        }
        var output = new JsonArray();
        foreach (var sourceStep in steps.Where(step => step["Source"] is JsonObject)) output.Add(CopyStep(sourceStep));
        var perFile = steps.Where(step => step["Process"] is JsonObject process
            && (GetString(process, "Run") != "Once" || GetBool(process, "IsDest"))).ToList();
        var destinations = steps.Where(step => step["Destination"] is JsonObject).ToList();
        var fileActions = actions.Where(entry => GetString(entry.Action, "DoAfter") == "FileDone"
            && (GetBool(entry.Action, "DoIfSuccess") || GetBool(entry.Action, "DoIfFailure"))).ToList();
        if (output.Count > 0 || perFile.Count > 0 || destinations.Count > 0 || fileActions.Count > 0)
        {
            var loopId = Allocate(); var loop = new JsonArray();
            foreach (var process in perFile) loop.Add(CopyStep(process));
            foreach (var destination in destinations) loop.Add(CopyStep(destination));
            AppendActions(loop, fileActions, eachFile: true);
            output.Add(new JsonObject { ["For"] = new JsonObject { ["ID"] = loopId, ["Disabled"] = 0, ["steps"] = loop } });
        }
        foreach (var process in steps.Where(step => step["Process"] is JsonObject body
            && GetString(body, "Run") == "Once" && !GetBool(body, "IsDest"))) output.Add(CopyStep(process));
        AppendActions(output, actions.Where(entry => GetString(entry.Action, "DoAfter") != "FileDone").ToList(), eachFile: false);
        Definition = source.DeepClone().AsObject();
        Definition["Name"] = name.Trim(); Definition["ID"] = ""; Definition["TT"] = "COS"; Definition["Active"] = 0;
        Definition["ClonedFromTaskID"] = GetString(source, "ID"); Definition["steps"] = output; Definition.Remove("NextActions");
        Definition["NextEID"] = Math.Max(Math.Max(next, reserved.DefaultIfEmpty(0).Max() + 1), GetInt(source, "NextEID", 1));
    }

    public static TraditionalTaskAdvancedConversion Build(JsonObject source, string name) => new(source, name);

    internal static IReadOnlyList<JsonObject> Actions(JsonObject task) => task["NextActions"]?["NextAction"] switch
    {
        JsonArray array => array.OfType<JsonObject>().ToList(), JsonObject single => [single], _ => []
    };

    public string ToNativeXml(string sourceXml)
    {
        var root = XElement.Parse(sourceXml, LoadOptions.PreserveWhitespace);
        if (root.Name.LocalName != "Task" || (string?)root.Attribute("ID") != GetString(_source, "ID")
            || !string.Equals((string?)root.Attribute("Name"), GetString(_source, "Name"), StringComparison.Ordinal))
            throw new InvalidOperationException("The Traditional Task native snapshot does not match the Task being converted.");
        var nativeType = (string?)root.Attribute("TT") ?? "";
        if (nativeType.Length > 0 && nativeType != "Tr") throw new InvalidOperationException("The native snapshot is not a Traditional Task.");
        var nativeSteps = root.Elements().Where(element => element.Name.LocalName is "Source" or "Process" or "Destination")
            .ToDictionary(element => (string?)element.Attribute("ID") ?? "", StringComparer.Ordinal);
        var nativeActions = root.Elements().FirstOrDefault(element => element.Name.LocalName == "NextActions")?.Elements()
            .Where(element => element.Name.LocalName == "NextAction").ToList() ?? [];
        if (nativeSteps.Count != (_source["steps"] as JsonArray ?? []).Count || nativeActions.Count != Actions(_source).Count)
            throw new InvalidOperationException("Task snapshots contain different steps or Next Actions. Reload the Task before converting.");
        foreach (var wrapper in (_source["steps"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var kind = AdvancedTaskDefinition.Kind(wrapper); var body = wrapper[kind]!.AsObject();
            if (!nativeSteps.TryGetValue(GetString(body, "ID"), out var native) || native.Name.LocalName != kind)
                throw new InvalidOperationException("A native step no longer matches the Traditional Task.");
        }
        for (var index = 0; index < nativeActions.Count; index++)
        {
            var action = Actions(_source)[index]; var native = nativeActions[index];
            foreach (var field in new[] { "Type", "HostID", "TaskID" })
                if (GetString(action, field) != ((string?)native.Attribute(field) ?? ""))
                    throw new InvalidOperationException("A native Next Action no longer matches the Traditional Task.");
        }
        var ns = root.Name.Namespace;
        XElement Element(string kind, JsonObject body)
        {
            var id = GetString(body, "ID"); XElement element;
            if (_stepTemplates.TryGetValue(id, out var originalId))
                element = new XElement(nativeSteps[originalId]);
            else if (_actionTemplates.TryGetValue(id, out var actionIndex))
            {
                element = new XElement(nativeActions[actionIndex]); element.Name = ns + kind;
                foreach (var key in Conditions.Concat(new[] { "DoAfter", "Type", "ID" })) element.Attribute(key)?.Remove();
            }
            else element = new XElement(ns + kind);
            // Original step XML is authoritative for secrets and opaque native members.
            if (_stepTemplates.ContainsKey(id)) element.SetAttributeValue("ID", id);
            else foreach (var field in body.Where(field => field.Value is JsonValue && !field.Key.StartsWith("__", StringComparison.Ordinal)))
                    element.SetAttributeValue(field.Key, field.Value!.ToString());
            if (body["steps"] is JsonArray children) foreach (var child in children.OfType<JsonObject>())
                    element.Add(Element(AdvancedTaskDefinition.Kind(child), child[AdvancedTaskDefinition.Kind(child)]!.AsObject()));
            if (body["When"] is JsonArray branches) foreach (var branch in branches.OfType<JsonObject>())
                {
                    var when = new XElement(ns + "When");
                    var comparison = branch["Criteria"]!["comp"]!.AsObject();
                    when.Add(new XElement(ns + "Criteria", new XElement(ns + "comp", comparison.Select(field => new XAttribute(field.Key, field.Value!.ToString())))));
                    foreach (var child in branch["steps"]!.AsArray().OfType<JsonObject>())
                        when.Add(Element(AdvancedTaskDefinition.Kind(child), child[AdvancedTaskDefinition.Kind(child)]!.AsObject()));
                    element.Add(when);
                }
            return element;
        }
        root.Elements().Where(element => element.Name.LocalName is "Source" or "Process" or "Destination" or "NextActions").Remove();
        root.SetAttributeValue("Name", GetString(Definition, "Name")); root.SetAttributeValue("Active", "0"); root.SetAttributeValue("TT", "COS");
        root.SetAttributeValue("NextEID", GetString(Definition, "NextEID"));
        // ClonedFromTaskID is the Web Admin creation hint in the JSON capture. Native
        // configuration creation already receives complete credential-bearing templates.
        root.Attribute("ClonedFromTaskID")?.Remove();
        foreach (var wrapper in Definition["steps"]!.AsArray().OfType<JsonObject>())
            root.Add(Element(AdvancedTaskDefinition.Kind(wrapper), wrapper[AdvancedTaskDefinition.Kind(wrapper)]!.AsObject()));
        return root.ToString(SaveOptions.DisableFormatting);
    }
}
