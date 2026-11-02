namespace MoveItAutomationUI.Models;

public sealed class TaskRenameRequest
{
    public string TaskId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string OriginalName { get; set; } = string.Empty;
}
