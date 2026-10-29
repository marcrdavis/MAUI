// Author: Marc R. Davis (marcdavis@comcast.net)
// Read-only built-in Script guidance. Parameter definitions are separate from configured Task values.
using System.Text.Json.Nodes;

namespace MoveItAutomationUI.Services.MoveIt.Resources.Scripts;

public sealed record ScriptParameterDefinition(string Name, string Description, string Direction, bool Required,
    string Type, string? DefaultValue, IReadOnlyList<string> Choices);
public sealed record StandardScriptMetadata(string Id, string Name, string Description, string ProcessDescription,
    bool ParametersAvailable, IReadOnlyList<ScriptParameterDefinition> Parameters);

public static class StandardScriptMetadataReader
{
    public static StandardScriptMetadata Read(JsonObject raw, string id)
    {
        if (Text(raw, "ID") != id || string.IsNullOrWhiteSpace(Text(raw, "Name")))
            throw new InvalidOperationException("MOVEit did not return the requested built-in Script metadata.");
        var definitions = new List<ScriptParameterDefinition>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parameters = Node(raw, "Params");
        // Different REST generations expose different parameter models. Inspect definition fields,
        // never interpret a configured name/value entry as a required/default parameter definition.
        var available = parameters is JsonObject { Count: 0 } or JsonArray { Count: 0 };
        var invalid = false;
        void Visit(JsonNode? node)
        {
            if (node is JsonArray array) { if (array.Count == 0) available = true; foreach (var child in array) Visit(child); return; }
            if (node is not JsonObject obj) { if (node is not null) invalid = true; return; }
            if (Node(obj, "ParamName") is not null)
            {
                var name = Text(obj, "ParamName").Trim();
                var required = Text(obj, "Required").ToLowerInvariant();
                if (name.Length == 0 || !names.Add(name) || required is not ("" or "true" or "false" or "1" or "0")) { invalid = true; return; }
                var choicesNode = Node(obj, "Choices");
                if (choicesNode is JsonObject choicesObject) choicesNode = Node(choicesObject, "Choice");
                var choices = new List<string>();
                if (choicesNode is JsonArray choicesArray)
                {
                    foreach (var choice in choicesArray)
                        if (choice is JsonValue value && value.TryGetValue<string>(out var text)) choices.Add(text);
                        else invalid = true;
                }
                else if (choicesNode is not null) invalid = true;
                definitions.Add(new(name, Text(obj, "ParamDesc"), Text(obj, "InOut"), required is "true" or "1",
                    Text(obj, "Type"), Node(obj, "Default") is null ? null : Text(obj, "Default"), choices));
                available = true;
                return;
            }
            foreach (var child in obj) Visit(child.Value);
        }
        Visit(parameters);
        available &= !invalid;
        return new(id, Text(raw, "Name"), Text(raw, "Description"), Text(raw, "ProcessDesc"), available,
            available ? definitions : Array.Empty<ScriptParameterDefinition>());
    }
    private static JsonNode? Node(JsonObject obj, string key)
        => obj.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;
    private static string Text(JsonObject obj, string key)
        => Node(obj, key) is JsonValue value && value.TryGetValue<string>(out var text) ? text : Node(obj, key)?.ToJsonString() ?? "";
}
