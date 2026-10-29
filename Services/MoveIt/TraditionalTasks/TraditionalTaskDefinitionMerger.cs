// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Text.Json.Nodes;

namespace MoveItAutomationUI.Services.MoveIt.TraditionalTasks;

/// <summary>
/// Applies feature-owned fields to a fresh MOVEit Task, without reconstructing
/// the Task's server-owned or version-specific root properties.
/// An adapter must explicitly state which properties it owns and which absences
/// mean deletion. This avoids silently treating an absent CSV column as a
/// request to erase unrelated configuration.
/// </summary>
public static class TraditionalTaskDefinitionMerger
{
    public static JsonObject MergeOwnedProperties(
        JsonObject current,
        JsonObject requested,
        IReadOnlyCollection<string> ownedProperties,
        IReadOnlyCollection<string>? clearWhenMissing = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(ownedProperties);
        var merged = current.DeepClone().AsObject();
        var clear = new HashSet<string>(clearWhenMissing ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var name in ownedProperties)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("An owned property name cannot be blank.", nameof(ownedProperties));
            var currentKey = FindKey(merged, name);
            var requestedKey = FindKey(requested, name);
            if (requestedKey is null)
            {
                if (!clear.Contains(name))
                    throw new InvalidOperationException($"The requested Task definition is missing required property '{name}'.");
                if (currentKey is not null) merged.Remove(currentKey);
                continue;
            }
            // Preserve the existing CSV PUT contract: remove any case variant of
            // the live property and use the analyzer's canonical requested spelling.
            // This avoids sending duplicate differently-cased fields to MOVEit.
            if (currentKey is not null) merged.Remove(currentKey);
            merged[requestedKey] = requested[requestedKey]?.DeepClone();
        }
        return merged;
    }

    public static JsonObject PrepareSpreadsheetUpdate(JsonObject current, JsonObject analyzed, bool leaveDisabled)
    {
        // The importer owns the *whole* steps and Schedules collections, not
        // individual fields within existing Steps. The RC2 editor will use its
        // verified per-profile merge rules instead of this replacement policy.
        var merged = MergeOwnedProperties(current, analyzed,
            new[] { "Name", "Info", "Schedules", "steps", "NextActions" },
            new[] { "NextActions" });
        if (leaveDisabled)
            SetProperty(merged, "Active", JsonValue.Create(0));
        // Group is intentionally left untouched on update (MOVEit group moves
        // are not reliable through normal REST Task PUT).
        return merged;
    }

    public static void SetProperty(JsonObject destination, string name, JsonNode? value)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var key = FindKey(destination, name) ?? name;
        destination[key] = value?.DeepClone();
    }

    private static string? FindKey(JsonObject source, string name)
        => source.Select(pair => pair.Key).FirstOrDefault(key => key.Equals(name, StringComparison.OrdinalIgnoreCase));
}
