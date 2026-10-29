using System.Text.Json.Nodes;

namespace MoveItAutomationUI.Services.MoveIt.Tasks;

public sealed record TaskFilenameSearchItem(string TaskId, string Text);

public interface ITaskFilenameSearchService
{
    Task<IReadOnlyList<TaskFilenameSearchItem>> ReadAsync(CancellationToken cancellationToken = default);
}

// Read-only projection of the bulk field projection used by dependency preflight. The native
// user's REST session determines visibility. Never use Data Access credentials or acquire checkout.
// Only filename text leaves this service; complete definitions and credentials are not cached.
public sealed class TaskFilenameSearchService(IMoveItRestClient rest, IMoveItSessionService sessions) : ITaskFilenameSearchService
{
    public async Task<IReadOnlyList<TaskFilenameSearchItem>> ReadAsync(CancellationToken cancellationToken = default)
    {
        _ = sessions.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        var definitions = await rest.GetTaskFilenameDefinitionsAsync(cancellationToken);
        var results = new List<TaskFilenameSearchItem>(definitions.Count);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var task in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var id = MoveItRestClient.GetString(task, "ID").Trim();
            if (id.Length == 0 || !ids.Add(id)) throw new InvalidOperationException("MOVEit returned an incomplete Task filename list.");
            var filenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            VisitSteps(task["steps"], filenames);
            results.Add(new(id, string.Join("\n", filenames)));
        }
        return results;
    }

    private static void VisitSteps(JsonNode? node, HashSet<string> filenames)
    {
        if (node is null) return; // Empty Tasks can omit steps.
        IEnumerable<JsonNode?> steps = node is JsonArray array ? array : new[] { node };
        foreach (var item in steps)
        {
            if (item is not JsonObject wrapper) throw new InvalidOperationException("MOVEit returned an incomplete Task step list.");
            Add(wrapper["Source"], "FileMask", filenames); // Includes both native Sync folders.
            Add(wrapper["Destination"], "FileName", filenames);
            if (ReadObject(wrapper["For"]) is { } loop) VisitSteps(loop["steps"], filenames);
            var conditional = ReadObject(wrapper["If"]);
            if (conditional is null) continue;
            var when = conditional["When"];
            IEnumerable<JsonNode?> branches = when is JsonArray choices ? choices : when is null ? Array.Empty<JsonNode?>() : new[] { when };
            foreach (var branch in branches)
            {
                if (branch is not JsonObject value) throw new InvalidOperationException("MOVEit returned an incomplete Task condition list.");
                VisitSteps(value["steps"], filenames);
            }
            if (ReadObject(conditional["Otherwise"]) is { } otherwise) VisitSteps(otherwise["steps"], filenames);
        }
    }

    // Known containers must be objects. Silently skipping a malformed loop/branch would
    // report a successful but incomplete filename index. Unknown step types remain ignored.
    private static JsonObject? ReadObject(JsonNode? node) => node switch
    {
        null => null,
        JsonObject value => value,
        _ => throw new InvalidOperationException("MOVEit returned an incomplete Task step list.")
    };

    private static void Add(JsonNode? node, string field, HashSet<string> filenames)
    {
        var fieldValue = ReadObject(node)?[field];
        if (fieldValue is null) return;
        if (fieldValue is not JsonValue value || !value.TryGetValue<string>(out var text))
            throw new InvalidOperationException("MOVEit returned invalid Task filename text.");
        if (!string.IsNullOrWhiteSpace(text)) filenames.Add(text);
    }
}
