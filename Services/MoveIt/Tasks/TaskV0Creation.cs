using System.Net;
using System.Text.Json.Nodes;

namespace MoveItAutomationUI.Services.MoveIt.Tasks;

// Empty Task creation uses the captured Web Admin POST. Native XML copy/restore
// and the retained COM strategy remain separate; an uncertain POST never falls back.
public sealed class RestV0TaskCrudStrategy : ITaskCrudStrategy
{
    private readonly IMoveItRestClient _rest;
    public MoveItTaskKind Kind { get; }
    public MoveItTaskDefinitionFormat DefinitionFormat => MoveItTaskDefinitionFormat.RestJsonV0;

    public RestV0TaskCrudStrategy(MoveItTaskKind kind, IMoveItRestClient rest)
    {
        if (kind is not (MoveItTaskKind.Advanced or MoveItTaskKind.Synchronization))
            throw new ArgumentException("The captured v0 creation contract is for Advanced/Sync Tasks.", nameof(kind));
        Kind = kind;
        _rest = rest;
    }

    public async Task<TaskCreationResult> CreateEmptyAsync(string name, string? resourceGroup, CancellationToken cancellationToken)
    {
        var taskName = (name ?? "").Trim();
        if (taskName.Length is 0 or > 254) throw new InvalidOperationException("Task Name must contain 1–254 characters.");
        var type = Kind == MoveItTaskKind.Advanced ? "COS" : "Sy";
        var definition = new JsonObject
        {
            ["Name"] = taskName, ["ID"] = "", ["AR"] = 0, ["Active"] = 0,
            ["Info"] = new JsonObject { ["Description"] = "", ["Notes"] = "" },
            ["CacheNames"] = "random", ["UseDefStateCaching"] = 1, ["NextEID"] = 11,
            ["Watched"] = 0, ["InvalidOverride"] = 0, ["Group"] = string.IsNullOrWhiteSpace(resourceGroup) ? new JsonArray() : new JsonArray(resourceGroup.Trim()), ["TT"] = type
        };
        if (Kind == MoveItTaskKind.Synchronization) definition["SameSecs"] = -1;
        cancellationToken.ThrowIfCancellationRequested();
        JsonObject response;
        try { response = await _rest.CreateTaskV0Async(definition, cancellationToken); }
        catch (MoveItAuthenticationException) { throw; }
        catch (MoveItApiException ex) when (ex.StatusCode is not null && (int)ex.StatusCode is >= 400 and < 500 && ex.StatusCode != HttpStatusCode.RequestTimeout) { throw; }
        catch (Exception ex) { throw new TaskV0CreationOutcomeException(null, taskName, ex); }

        var id = Text(response, "id");
        try
        {
            if (id.Length == 0 || id is "0" or "-1" || Text(response, "status") != "success" || Text(response, "errorcode") != "0")
                throw new InvalidOperationException("MOVEit did not return the captured v0 Task creation acknowledgement.");
            if (response.ContainsKey("object"))
                VerifyCreated(response["object"] as JsonObject ?? throw new InvalidOperationException("The created Task object was invalid."), id, taskName, type);
            // GET is read-only verification, never name-based ownership guessing or another POST.
            VerifyCreated(await _rest.GetTaskAsync(id, cancellationToken), id, taskName, type);
            return new TaskCreationResult(id, taskName, Kind == MoveItTaskKind.Advanced ? "Advanced" : "Synchronization");
        }
        catch (Exception ex) { throw new TaskV0CreationOutcomeException(id is "" or "0" or "-1" ? null : id, taskName, ex); }
    }

    private static void VerifyCreated(JsonObject task, string id, string name, string type)
    {
        if (Text(task, "ID") != id || Text(task, "Name") != name || Text(task, "TT") != type
            || Text(task, "Active").ToLowerInvariant() is not ("0" or "false")
            || type == "Sy" && Text(task, "SameSecs") != "-1")
            throw new InvalidOperationException("The created Task identity, type or disabled initial state could not be verified.");
        if (task["steps"] is not null && task["steps"] is not JsonArray { Count: 0 })
            throw new InvalidOperationException("The created Task was not empty.");
        if (task["Schedules"]?["Schedule"] is JsonNode schedules && schedules is not JsonArray { Count: 0 })
            throw new InvalidOperationException("The created Task unexpectedly contained a Schedule.");
    }

    private static string Text(JsonObject value, string key) => value[key]?.ToString() ?? "";
}

public sealed class TaskV0CreationOutcomeException : InvalidOperationException
{
    public string? TaskId { get; }
    public string TaskName { get; }
    public TaskV0CreationOutcomeException(string? id, string name, Exception inner)
        : base(string.IsNullOrWhiteSpace(id)
            ? "MAUI could not confirm whether the Task was created. Inspect Tasks before trying to create it again."
            : $"Task '{name}' (ID {id}) may have been created, but its saved state could not be verified. Inspect that Task before creating another copy.", inner)
        => (TaskId, TaskName) = (id, name);
}
