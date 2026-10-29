// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Models for the isolated MAUI Host Editor feature.

using System.Text.Json.Nodes;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Features.Editors.Hosts;

public sealed record HostEditorFieldOption(string Value, string Label, bool DefaultEnabled = false);
public sealed record HostEditorField(
    string Key,
    string Label,
    string Value,
    string ValueType = "Text",
    bool Sensitive = false,
    bool Editable = false,
    IReadOnlyList<HostEditorFieldOption>? Options = null,
    string ConditionKey = "", string ConditionValue = "", string HelpText = "");
public sealed record HostEditorSection(string Key, string Label, IReadOnlyList<HostEditorField> Fields);
public sealed record HostEditorTaskReference(string Id, string Name);
public sealed record HostEditorAudit(DateTimeOffset? CreatedUtc, string CreatedBy, DateTimeOffset? ModifiedUtc, string ModifiedBy);

public sealed record HostEditorHost(
    string Id,
    string Name,
    string Description,
    string Type,
    string TypeCode,
    string Destination,
    string Secure,
    int AssignedCount,
    bool CanEditDelete,
    bool CanEditMoveItFields,
    bool CanEditConfiguration,
    string WriteProfile,
    string ReadOnlyReason,
    bool TestSupported,
    IReadOnlyList<HostEditorSection> ConfigurationSections,
    IReadOnlyList<HostEditorTaskReference> Tasks,
    string EditVersion = "", string ResourceGroups = "");

public sealed class HostEditorShellViewModel
{
    public bool CheckoutObjectsForDurationOfEdit { get; init; }
    public bool CanUndo { get; init; }
    public bool ReadOnly { get; init; }
    public HostEditorPreferences Preferences { get; init; } = new(false);
    public HostEditorHost Host { get; init; } = new("", "", "", "", "", "", "", 0, false, false, false, "", "", false, Array.Empty<HostEditorSection>(), Array.Empty<HostEditorTaskReference>());
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<MetadataFieldDefinition> Fields { get; init; } = Array.Empty<MetadataFieldDefinition>();
    public HostEditorAudit Audit { get; init; } = new(null, "", null, "");
    public IReadOnlyList<HostEditorTaskReference> Tasks { get; init; } = Array.Empty<HostEditorTaskReference>();
}

public sealed class HostEditorSaveRequest
{
    public string EditVersion { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public JsonObject Configuration { get; set; } = new();
}

public sealed record HostEditorMoveItSaveResult(bool Changed, string HostName, string TypeCode, string UndoSnapshotId = "");

