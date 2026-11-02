namespace MoveItAutomationUI.Models;

public sealed class HostRenameRequest
{
    public string HostId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string OriginalName { get; set; } = string.Empty;
}
