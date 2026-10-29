// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Text.Json.Nodes;

namespace MoveItAutomationUI.Services.MoveIt.TraditionalTasks;

/// <summary>
/// The role of a MOVEit Traditional Task step. This is intentionally distinct from
/// the Host configuration editor, which persists saved Host definitions.
/// </summary>
public enum TraditionalTaskStepRole { Source, Destination }

/// <summary>
/// A location and native field set for one file-transfer step. Caller-owned fields
/// (for example FileMask, UseOrigName, HostID) remain caller-owned in Phase 1.
/// The composer applies only verified Host defaults and location mappings.
/// </summary>
public sealed record TraditionalTaskStepDefinition(
    TraditionalTaskStepRole Role,
    TraditionalTaskHostProfile Profile,
    JsonObject Fields,
    string Path,
    string RootName = "",
    string RootId = "");

/// <summary>
/// Specifies the native Process step without resolving scripts or secrets.
/// Key tokens must already be resolved and authorized by the invoking feature.
/// </summary>
public sealed record TraditionalTaskProcessDefinition(
    JsonNode ScriptId,
    JsonArray? Parameters = null,
    bool IncludeParametersWhenEmpty = false,
    string Run = "PerFile");

/// <summary>
/// Typed properties for the common native Traditional Task root. Null optional arrays
/// mean "omit the property"; an explicitly empty array means "send an empty array".
/// This distinction preserves MOVEit's verified Quick Transfer / CSV / skeleton payloads.
/// </summary>
public sealed record TraditionalTaskRootDefinition(
    string Name,
    bool Active,
    string Description,
    string? Notes,
    JsonObject Schedules,
    JsonArray Steps,
    JsonArray? ResourceGroups = null,
    JsonArray? NextActions = null);

/// <summary>
/// Shared native JSON construction; not an API/COM persistence service.
/// Workflow services keep permissions, batching, audits, and operational cleanup.
/// Native definition and Step serialization (including editor patches) are shared.
/// All construction methods return detached, caller-owned JSON nodes.
/// </summary>
public interface ITraditionalTaskDefinitionComposer
{
    JsonObject BuildStepBody(TraditionalTaskStepDefinition definition);
    JsonObject BuildWrappedStep(TraditionalTaskStepDefinition definition);
    JsonObject BuildProcessBody(TraditionalTaskProcessDefinition definition);
    JsonObject BuildWrappedProcess(TraditionalTaskProcessDefinition definition);
    JsonArray BuildSteps(IEnumerable<JsonObject> wrappedSteps);
    // Same native Step writer for new editor Steps, edited Steps and Host changes.
    // Existing unsupported/opaque Steps must be preserved exactly.
    void ApplyEditorSteps(JsonObject currentTask, JsonArray? requestedSteps);
    void VerifyEditorSteps(JsonArray? requestedSteps, JsonNode? persistedSteps);
    JsonObject BuildDisabledTaskDefinition(string taskName, string? resourceGroup = null);
    JsonObject BuildRootDefinition(TraditionalTaskRootDefinition definition);
    void ApplyRootDefinition(JsonObject task, TraditionalTaskRootDefinition definition);
}

public sealed class TraditionalTaskDefinitionComposer : ITraditionalTaskDefinitionComposer
{
    private readonly ITraditionalTaskHostProfileCatalog _profiles;

    public TraditionalTaskDefinitionComposer(ITraditionalTaskHostProfileCatalog profiles)
        => _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));

    public void ApplyEditorSteps(JsonObject currentTask, JsonArray? requestedSteps)
    {
        ArgumentNullException.ThrowIfNull(currentTask);
        TraditionalTaskDefinitionEditorOperations.ApplySteps(currentTask, requestedSteps);
    }

    public void VerifyEditorSteps(JsonArray? requestedSteps, JsonNode? persistedSteps)
        => TraditionalTaskDefinitionEditorOperations.VerifySteps(requestedSteps, persistedSteps);

    public JsonObject BuildDisabledTaskDefinition(string taskName, string? resourceGroup = null)
    {
        var name = (taskName ?? string.Empty).Trim();
        if (name.Length == 0 || name.Length > 254)
            throw new ArgumentException("A Task Name of 1–254 characters is required.", nameof(taskName));
        var group = new JsonArray();
        if (!string.IsNullOrWhiteSpace(resourceGroup)) group.Add(resourceGroup.Trim());
        return BuildRootDefinition(new TraditionalTaskRootDefinition(
            name, false, string.Empty, string.Empty,
            new JsonObject { ["Schedule"] = new JsonArray() }, new JsonArray(), group));
    }

    public JsonObject BuildRootDefinition(TraditionalTaskRootDefinition definition)
    {
        var task = new JsonObject();
        ApplyRootDefinition(task, definition);
        return task;
    }

    public void ApplyRootDefinition(JsonObject task, TraditionalTaskRootDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(definition);
        var name = (definition.Name ?? string.Empty).Trim();
        if (name.Length == 0 || name.Length > 254)
            throw new ArgumentException("A Task Name of 1–254 characters is required.", nameof(definition));
        ArgumentNullException.ThrowIfNull(definition.Schedules);
        ArgumentNullException.ThrowIfNull(definition.Steps);
        task["Name"] = name;
        task["Active"] = definition.Active ? 1 : 0;
        var info = new JsonObject { ["Description"] = definition.Description ?? string.Empty };
        if (definition.Notes is not null) info["Notes"] = definition.Notes;
        task["Info"] = info;
        task["Schedules"] = definition.Schedules.DeepClone();
        task["steps"] = definition.Steps.DeepClone();
        // Do not clear unowned Group/NextActions when patching an existing Task.
        if (definition.ResourceGroups is not null) task["Group"] = definition.ResourceGroups.DeepClone();
        if (definition.NextActions is not null)
            task["NextActions"] = new JsonObject { ["NextAction"] = definition.NextActions.DeepClone() };
    }

    public JsonObject BuildStepBody(TraditionalTaskStepDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(definition.Profile);
        ArgumentNullException.ThrowIfNull(definition.Fields);
        if (definition.Role is not (TraditionalTaskStepRole.Source or TraditionalTaskStepRole.Destination))
            throw new ArgumentOutOfRangeException(nameof(definition), "Unknown Traditional Task step role.");

        var role = definition.Role == TraditionalTaskStepRole.Source ? "Source" : "Destination";
        // Re-resolve the trusted profile from the catalog rather than trusting arbitrary
        // call-site metadata. This keeps write eligibility and location rules centralized.
        var profile = _profiles.Require(definition.Profile.TypeCode, role);
        var step = (JsonObject)definition.Fields.DeepClone();
        if (step.TryGetPropertyValue("Type", out var typeNode)
            && typeNode is not null
            && !string.Equals(typeNode.ToString(), profile.TypeCode, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{role} step Host type does not match the verified Host profile.");

        if (definition.Role == TraditionalTaskStepRole.Source)
            _profiles.ApplySourceDefaults(step, profile);
        else
            _profiles.ApplyDestinationDefaults(step, profile);
        _profiles.ApplyLocation(step, profile, definition.Path, definition.RootName, definition.RootId);
        return step;
    }

    public JsonObject BuildWrappedStep(TraditionalTaskStepDefinition definition)
        => new() { [definition.Role == TraditionalTaskStepRole.Source ? "Source" : "Destination"] = BuildStepBody(definition) };

    public JsonObject BuildProcessBody(TraditionalTaskProcessDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(definition.ScriptId);
        var process = new JsonObject
        {
            ["Run"] = definition.Run,
            ["ScriptID"] = definition.ScriptId.DeepClone()
        };
        if (definition.Parameters is not null && (definition.Parameters.Count > 0 || definition.IncludeParametersWhenEmpty))
            process["Parameters"] = new JsonObject { ["Parameter"] = definition.Parameters.DeepClone() };
        else if (definition.IncludeParametersWhenEmpty)
            process["Parameters"] = new JsonObject { ["Parameter"] = new JsonArray() };
        return process;
    }

    public JsonObject BuildWrappedProcess(TraditionalTaskProcessDefinition definition)
        => new() { ["Process"] = BuildProcessBody(definition) };

    public JsonArray BuildSteps(IEnumerable<JsonObject> wrappedSteps)
    {
        ArgumentNullException.ThrowIfNull(wrappedSteps);
        var result = new JsonArray();
        foreach (var step in wrappedSteps)
        {
            ArgumentNullException.ThrowIfNull(step);
            if (step.Count != 1 ||
                !(step.ContainsKey("Source") || step.ContainsKey("Destination") || step.ContainsKey("Process")) ||
                step.First().Value is not JsonObject)
                throw new InvalidOperationException("A Traditional Task step must contain one Source, Destination, or Process object wrapper.");
            result.Add(step.DeepClone());
        }
        return result;
    }
}
