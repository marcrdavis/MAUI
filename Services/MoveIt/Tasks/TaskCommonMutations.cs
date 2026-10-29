using System.Text.Json.Nodes;
using static MoveItAutomationUI.Services.MoveIt.TraditionalTasks.TraditionalTaskDefinitionEditorOperations;

namespace MoveItAutomationUI.Services.MoveIt.Tasks;

public sealed partial class TaskCrudCoordinator
{
    // Patch only the information fields on the authoritative checked-out definition.
    // The same save service selects v1 for Traditional and v0 for Advanced/Sync.
    public Task UpdateBasicDetailsAsync(string taskId, string name, string description, string notes, CancellationToken cancellationToken = default)
    {
        var id = RequireId(taskId); var taskName = (name ?? "").Trim();
        if (taskName.Length is 0 or > 254) throw new InvalidOperationException("Task Name must contain 1–254 characters.");
        var taskDescription = description ?? ""; var taskNotes = notes ?? "";
        return _definitions.UpdateFreshAsync(id, null, current =>
        {
            RequireCanAlter(current);
            current["Name"] = taskName;
            if (current["Info"] is not JsonObject) current["Info"] = new JsonObject();
            current["Info"]!["Description"] = taskDescription;
            current["Info"]!["Notes"] = taskNotes;
            return current;
        }, saved =>
        {
            VerifyTextValue("Task Name", taskName, GetString(saved, "Name"));
            VerifyTextValue("Task Description", taskDescription, GetString(GetObject(saved, "Info"), "Description"));
            VerifyTextValue("Task Operator Notes", taskNotes, GetString(GetObject(saved, "Info"), "Notes"));
        }, requireReadback: true, cancellationToken: cancellationToken);
    }

    public Task SetEnabledAsync(string taskId, bool enabled, CancellationToken cancellationToken = default)
        => _definitions.UpdateFreshAsync(RequireId(taskId), null, current =>
        {
            RequireCanAlter(current);
            current["Active"] = enabled ? 1 : 0;
            return current;
        }, saved => VerifyFlagValue("Task Enabled state", enabled, saved, "Active"),
        requireReadback: true, cancellationToken: cancellationToken);

    private static void RequireCanAlter(JsonObject task)
    {
        if (GetNode(task, "xAlter") is not null && !GetBool(task, "xAlter"))
            throw new InvalidOperationException("The current MOVEit user cannot alter this Task.");
    }
}
