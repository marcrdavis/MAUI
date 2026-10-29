// Author: Marc R. Davis (marcdavis@comcast.net)
// Normalize captured synccompare singleton/array records without inventing file actions.
using System.Text.Json.Nodes;
using static MoveItAutomationUI.Services.MoveIt.TraditionalTasks.TraditionalTaskDefinitionEditorOperations;

namespace MoveItAutomationUI.Services.MoveIt.Tasks;

public sealed record SynchronizationPreviewSide(string Path, string Size, string Timestamp, string Status);
public sealed record SynchronizationPreviewEntry(SynchronizationPreviewSide? FolderA, SynchronizationPreviewSide? FolderB,
    string Action, bool NoAction);
public sealed record SynchronizationPreview(bool CanChangeInitialRunOptions, int TotalEntries, bool Truncated,
    IReadOnlyList<SynchronizationPreviewEntry> Entries)
{
    public static SynchronizationPreview Parse(JsonObject response)
    {
        var saved = GetObject(response, "SavedDirs") ?? throw new InvalidOperationException("MOVEit returned no Sync listing state.");
        var a = Flag(saved, "s1"); var b = Flag(saved, "s2");
        if (GetNode(response, "files") is JsonNode container && container is not JsonObject)
            throw new InvalidOperationException("MOVEit returned an invalid Sync Preview file container.");
        var rows = new List<SynchronizationPreviewEntry>(); var total = 0;
        foreach (var file in Files(GetNode(GetObject(response, "files"), "file")))
        {
            var left = Side(GetObject(file, "s1")); var right = Side(GetObject(file, "s2"));
            if (left is null && right is null) throw new InvalidOperationException("A Sync Preview record contains no folder data.");
            total++;
            if (rows.Count >= 1000) continue;
            var statuses = new[] { left?.Status ?? "", right?.Status ?? "" }.Where(value => value.Length > 0).ToArray();
            var noAction = statuses.Length > 0 && statuses.All(value => value.ToLowerInvariant() is "none" or "noaction" or "same" or "unchanged");
            var action = noAction ? "No action"
                : left?.Status.Equals("copyfrom", StringComparison.OrdinalIgnoreCase) == true ? "Copy A → B"
                : right?.Status.Equals("copyfrom", StringComparison.OrdinalIgnoreCase) == true ? "Copy B → A"
                : string.Join(" · ", new[] { left is not null && left.Status.Length > 0 ? $"A: {left.Status}" : "", right is not null && right.Status.Length > 0 ? $"B: {right.Status}" : "" }.Where(value => value.Length > 0));
            rows.Add(new(left, right, action.Length > 0 ? action : "Status not supplied", noAction));
        }
        return new(!a && !b, total, total > rows.Count, rows);
    }

    private static bool Flag(JsonObject saved, string name)
    {
        var value = GetString(saved, name).ToLowerInvariant();
        return value switch { "0" or "false" => false, "1" or "true" => true, _ => throw new InvalidOperationException("MOVEit returned an invalid Sync listing state.") };
    }
    private static SynchronizationPreviewSide? Side(JsonObject? source)
        => source is null ? null : new(GetString(source, "Path"), GetString(source, "Size"), GetString(source, "Stamp"), GetString(source, "Stat"));
    private static IEnumerable<JsonObject> Files(JsonNode? node)
    {
        if (node is null) yield break;
        if (node is JsonObject single && (GetNode(single, "s1") is not null || GetNode(single, "s2") is not null)) { yield return single; yield break; }
        if (node is JsonObject empty && empty.Count == 0) yield break;
        if (node is JsonArray array)
        {
            foreach (var item in array) yield return item as JsonObject ?? throw new InvalidOperationException("MOVEit returned a malformed Sync Preview row.");
            yield break;
        }
        throw new InvalidOperationException("MOVEit returned an unrecognized Sync Preview file collection.");
    }
}
