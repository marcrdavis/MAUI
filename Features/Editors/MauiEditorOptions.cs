// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Experimental editor feature settings kept with the editor feature boundary.

using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Features.Editors;

public sealed class MauiEditorOptions
{
    public const string SectionName = "Editors";

    public SettingDefinition<bool> UseMauiSystemSettingsEditor { get; set; } = new()
    {
        Name = "Use MAUI Settings Editor", Value = false, Type = "Boolean",
        Description = "Edit MOVEit Settings and manage lockouts with MAUI. Disable to use the MOVEit native editor.",
        PossibleValues = new List<bool> { false, true }, Experimental = true
    };

    public SettingDefinition<bool> UseMauiGroupsAndPermissionsEditor { get; set; } = new()
    {
        Name = "Use MAUI Group & Permissions Editor", Value = false, Type = "Boolean",
        Description = "Manage Resource Groups, membership and assigned user-group permissions with MAUI. Disable to use the native MOVEit editor.",
        PossibleValues = new List<bool> { false, true }, Experimental = true
    };

    public SettingDefinition<bool> UseMauiTaskEditor { get; set; } = new()
    {
        Name = "Use MAUI Task Editor",
        Value = true,
        Description = "Open Traditional, Advanced and Synchronization Tasks in the experimental MAUI Task Editor. When disabled, Task links use the native MOVEit Task Editor.",
        Type = "Boolean",
        PossibleValues = new List<bool> { false, true },
        Experimental = true
    };
    public SettingDefinition<bool> UseMauiHostEditor { get; set; } = new()
    {
        Name = "Use MAUI Host Editor",
        Value = false,
        Description = "Open Hosts in the experimental MAUI Host Editor. When disabled, Host links use the native MOVEit Host Editor.",
        Type = "Boolean",
        PossibleValues = new List<bool> { false, true },
        Experimental = true
    };

    public SettingDefinition<bool> UseMauiScriptEditor { get; set; } = new()
    {
        Name = "Use MAUI Script Editor", Value = false, Type = "Boolean",
        Description = "Create and edit custom PowerShell and VBScript Scripts in MAUI. When disabled, Script links use the native MOVEit editor. The built-in Script editor is read-only; administrators can maintain MAUI properties through Script Information.",
        PossibleValues = new List<bool> { false, true }, Experimental = true
    };

    public SettingDefinition<bool> UseMauiDateListEditor { get; set; } = new()
    {
        Name = "Use MAUI Date List Editor", Value = false, Type = "Boolean",
        Description = "Create and edit Date Lists in the experimental MAUI Date List Editor. When disabled, Date List links use the native MOVEit editor.",
        PossibleValues = new List<bool> { false, true }, Experimental = true
    };
}
