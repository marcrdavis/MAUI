// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

public sealed class SystemMessageOptions
{
    public const string SectionName = "SystemMessage";
    public static readonly IReadOnlyList<string> SupportedMessageTypes = new[] { "Information", "Important", "Critical" };

    public SettingDefinition<bool> Enabled { get; set; } = new()
    {
        Name = "Display System Message",
        Value = false,
        Description = "Displays the configured System Message banner on authenticated MAUI pages.",
        Type = "Boolean",
        PossibleValues = new List<bool> { false, true }
    };

    public SettingDefinition<string> MessageType { get; set; } = new()
    {
        Name = "Message Type",
        Value = "Information",
        Description = "Controls the icon and visual severity of the System Message banner.",
        Type = "List",
        PossibleValues = new List<string> { "Information", "Important", "Critical" }
    };

    public SettingDefinition<string> MessageBody { get; set; } = new()
    {
        Name = "Message Body",
        Value = string.Empty,
        Description = "Plain-text message displayed to authenticated MAUI users.",
        Type = "MultilineString"
    };
}
