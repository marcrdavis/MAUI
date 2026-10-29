using MoveItAutomationUI.Services.MoveIt.Resources.DateLists;

namespace MoveItAutomationUI.Features.Editors.DateLists;

public sealed class DateListEditorSaveRequest
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Entries { get; set; } = [];
    public string EditVersion { get; set; } = "";
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateListEdit Definition() => new(Name, Description, Entries, EditVersion);
}
