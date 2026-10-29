using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Tasks;
using MoveItAutomationUI.Services.MoveIt.Tasks.AdvancedTasks;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;

namespace MoveItAutomationUI.Features.EnvironmentCopy;

// Native import readback checks transferable values, tolerating empty/default fields and native read-only annotations.
internal static class EnvironmentCopyVerification
{
    private static readonly HashSet<string> Ignored = new(["ID", "ID2", "NextEID", "new", "Renamed", "duplicate", "displayName", "UsedByCount", "OverridesTotal", "OverridesNoPerm", "InvalidOverride", "Scheduled", "Watched", "__parentStepId", "__branchIndex"], StringComparer.OrdinalIgnoreCase);
    public static void Verify(JsonObject expected, JsonObject actual, string type, string id, string name)
    {
        if (MoveItRestClient.GetString(actual, "ID") != id || MoveItRestClient.GetString(actual, "Name") != name)
            throw new InvalidOperationException("The native copy was accepted, but destination identity verification failed.");
        if (type == "Task" && MoveItRestClient.GetString(expected, "TT") == "COS") AdvancedTaskDefinition.Verify(expected, actual, new JsonObject());
        if (type == "Task" && MoveItRestClient.GetString(expected, "TT") == "Sy") SynchronizationTaskDefinitionComposer.VerifyNativeState(expected, actual);
        bool Equal(JsonNode? left, JsonNode? right)
        {
            if (left is JsonObject obj)
                return right is JsonObject saved && obj.All(pair => Ignored.Contains(pair.Key) || pair.Key.StartsWith('x') || EnvironmentCopyRules.IsSecret(pair.Key)
                    || (obj.FirstOrDefault(value => value.Key.Equals("Secure", StringComparison.OrdinalIgnoreCase)).Value?.ToString() is "1" or "true")
                        && (pair.Key.Equals("value", StringComparison.OrdinalIgnoreCase) || pair.Key.Equals("DisplayValue", StringComparison.OrdinalIgnoreCase))
                    || pair.Key == "Group" && pair.Value is JsonArray groups && saved[pair.Key] is JsonArray savedGroups && groups.Select(x => x?.ToString()).Order(StringComparer.Ordinal).SequenceEqual(savedGroups.Select(x => x?.ToString()).Order(StringComparer.Ordinal))
                    || pair.Key.Contains("SHA1", StringComparison.OrdinalIgnoreCase) && EnvironmentCopyObjects.NormalizeFingerprint(pair.Value?.ToString() ?? "") == EnvironmentCopyObjects.NormalizeFingerprint(saved[pair.Key]?.ToString() ?? "")
                    || pair.Value is JsonValue && TraditionalTaskDefinitionEditorOperations.EquivalentScalar(pair.Key, pair.Value, saved[pair.Key])
                    || pair.Key.StartsWith("UseDef", StringComparison.Ordinal) && saved[pair.Key] is null && (pair.Value?.ToString() is "1" or "true")
                    || Equal(pair.Value, saved[pair.Key] ?? (pair.Key == "Path" ? saved["FolderName"] : null)));
            if (left is JsonArray array)
                return right is JsonArray saved && array.Count == saved.Count && array.Select((value, index) => Equal(value, saved[index])).All(x => x)
                    || array.Count == 0 && right is null;
            static string Text(JsonNode? value) => (value?.ToString() ?? "").Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
            var a = Text(left); var b = Text(right);
            if (a == b) return true;
            if ((a is "0" or "false" or "") && (b is "0" or "false" or "")) return true;
            if ((a is "1" or "true") && (b is "1" or "true")) return true;
            return false;
        }
        if (!Equal(expected, actual)) throw new InvalidOperationException($"MOVEit accepted the {type} copy, but a transferable field could not be verified. Inspect the destination before retrying.");
    }
}
