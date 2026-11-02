using System.Globalization;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.MoveIt.Hosts;
using MoveItAutomationUI.Services.MoveIt.Tasks;

namespace MoveItAutomationUI.Services.MoveIt;

public sealed record ObjectJsonSearchValue(string[] Path, JsonNode? Value);
public sealed record ObjectJsonSearchDocument(string Id, IReadOnlyList<ObjectJsonSearchValue> Values);

public interface IObjectJsonSearchService
{
    Task<ObjectJsonSearchDocument> ReadAsync(string type, string id, CancellationToken cancellationToken = default);
}

// On-demand read through the authoritative object libraries and the signed-in user's REST session.
// No edit permission, native export, checkout, write or persistent definition cache is involved.
public sealed class ObjectJsonSearchService(ITaskCrudCoordinator tasks, IHostCrudCoordinator hosts,
    IMoveItSessionService sessions) : IObjectJsonSearchService
{
    public async Task<ObjectJsonSearchDocument> ReadAsync(string type, string id, CancellationToken cancellationToken = default)
    {
        _ = sessions.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        if (id.Length is 0 or > 32 || !id.All(char.IsAsciiDigit)) throw new ArgumentException("Select a valid object.");
        var definition = type switch
        {
            "Task" => await tasks.GetDefinitionAsync(id, cancellationToken),
            "Host" => await hosts.GetDefinitionAsync(id, cancellationToken),
            _ => throw new ArgumentException("Select Tasks or Hosts.")
        };
        // Host definitions may have their native protocol envelope; retain that path in the projection.
        var identity = type == "Host" && definition["ID"] is null
            ? definition.Select(pair => pair.Value).OfType<JsonObject>().SingleOrDefault(value => value["ID"] is not null)
            : definition;
        if (identity is null || MoveItRestClient.GetString(identity, "ID") != id)
            throw new InvalidOperationException("MOVEit returned a different object.");
        return new(id, Project(definition, cancellationToken));
    }

    internal static IReadOnlyList<ObjectJsonSearchValue> Project(JsonObject definition, CancellationToken cancellationToken = default)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(definition.ToJsonString()) > TaskJsonValues.MaximumDefinitionBytes)
            throw new InvalidOperationException("This definition is too large for JSON search.");
        var result = new List<ObjectJsonSearchValue>();
        var nodes = 0;
        void Visit(JsonNode? node, string[] path)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++nodes > TaskJsonValues.MaximumNodes || path.Length > TaskJsonValues.MaximumDepth)
                throw new InvalidOperationException("This definition is too large for JSON search.");
            if (node is JsonObject obj)
            {
                var secure = obj.Any(pair => pair.Key.Equals("Secure", StringComparison.OrdinalIgnoreCase)
                    && (pair.Value?.ToString() is "1" or "true" or "True"));
                foreach (var pair in obj)
                {
                    // Reuse the JSON Editor's credential rule. Omit hidden values entirely so even !=
                    // and plain searches cannot infer a credential from a masked/null placeholder.
                    if (TaskJsonValues.Secret(pair.Key) || secure && (pair.Key.Equals("value", StringComparison.OrdinalIgnoreCase)
                        || pair.Key.Equals("DisplayValue", StringComparison.OrdinalIgnoreCase))) continue;
                    Visit(pair.Value, [.. path, pair.Key]);
                }
            }
            else if (node is JsonArray array)
            {
                for (var i = 0; i < array.Count; i++) Visit(array[i], [.. path, i.ToString(CultureInfo.InvariantCulture)]);
            }
            else if (node is JsonValue value)
            {
                // Match the Editor's supported numeric range.
                if (TaskJsonValues.KindOf(value) == System.Text.Json.JsonValueKind.Number
                    && (!decimal.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                        || number is < -9007199254740991m or > 9007199254740991m)) return;
                result.Add(new(path, value.DeepClone()));
            }
            else if (path.Length > 0) result.Add(new(path, null));
        }
        Visit(definition, []);
        return result;
    }
}
