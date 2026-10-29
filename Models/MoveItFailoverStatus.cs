// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

// Read-only status returned by MOVEit Automation's native failover-status endpoint.
public sealed class MoveItFailoverStatus
{
    public bool ResilEnabled { get; set; }
    public string PrimaryStatus { get; set; } = string.Empty;
    public string PrimaryStatusStamp { get; set; } = string.Empty;
    public string SecondaryStatus { get; set; } = string.Empty;
    public string SecondaryStatusStamp { get; set; } = string.Empty;
    public int ThisNode { get; set; }
    public IReadOnlyList<MoveItFailoverNode> Nodes { get; set; } = Array.Empty<MoveItFailoverNode>();
}

public sealed class MoveItFailoverNode
{
    public int NodeNum { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public string ResilRole { get; set; } = string.Empty;
    public bool NodeHasError { get; set; }
}
