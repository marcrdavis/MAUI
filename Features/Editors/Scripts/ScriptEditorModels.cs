using MoveItAutomationUI.Services.MoveIt.Resources.Scripts;

namespace MoveItAutomationUI.Features.Editors.Scripts;

// MOVEit configuration and MAUI metadata remain separate persistence domains.
public sealed class ScriptEditorSaveRequest
{
    public string ResourceGroup { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Language { get; set; } = "PS1";
    public string Source { get; set; } = "";
    public string EditVersion { get; set; } = "";
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public ScriptEdit Definition() => new(Name, Description, Language, Source, EditVersion, ResourceGroup);
}

// Built-in metadata has a separate contract; it cannot carry native Script fields.
public sealed class ScriptMetadataSaveRequest
{
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
