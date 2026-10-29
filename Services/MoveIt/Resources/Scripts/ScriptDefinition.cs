using System.Text.Json.Nodes;

namespace MoveItAutomationUI.Services.MoveIt.Resources.Scripts;

// The editor owns these four fields only. Unknown configuration and resource-group
// assignments are taken from the fresh server definition, never from the browser.
public sealed record ScriptEdit(string Name = "", string Description = "", string Language = "PS1", string Source = "", string EditVersion = "", string ResourceGroup = "");
public sealed record ScriptDefinition(string Id, string Name, string Description, string Language, string Source, string EditVersion, string ResourceGroups = "");
public sealed class ScriptWriteUncertainException(string id, bool creating, Exception inner)
    : InvalidOperationException($"The Script {(string.IsNullOrEmpty(id) ? "" : $"'{id}' ")}{(creating ? "may have been created" : "save may have completed")}, but its saved state could not be confirmed. Inspect Scripts and reload before saving again.", inner)
{
    public string ScriptId { get; } = id;
}

public static class ScriptConfiguration
{
    public const int MaxSourceLength = 2 * 1024 * 1024;
    public static string Id(string? value)
        => long.TryParse(value, out var id) && id > 0 && id.ToString() == value?.Trim()
            ? value!.Trim() : throw new ArgumentException("A valid custom Script ID is required.");
    public static string Language(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "PS1" or "POWERSHELL" => "PS1",
        "VBS" or "VBSCRIPT" => "VBScript",
        _ => throw new ArgumentException("Script Type must be PowerShell or VBScript.")
    };
    public static ScriptEdit Validate(ScriptEdit edit)
    {
        var name = edit.Name?.Trim() ?? "";
        if (name.Length is 0 or > 255) throw new ArgumentException("Script Name is required and must contain at most 255 characters.");
        var source = edit.Source ?? "";
        if (source.Length > MaxSourceLength) throw new ArgumentException("Script Source must be no larger than 2 MiB of text.");
        return edit with { Name = name, Description = edit.Description ?? "", Language = Language(edit.Language), Source = source };
    }
    public static JsonObject Unwrap(JsonObject raw, string id)
    {
        var script = raw["Script"] as JsonObject ?? raw["object"] as JsonObject ?? raw;
        if (MoveItRestClient.GetString(script, "ID") != Id(id)) throw new InvalidOperationException("MOVEit returned a different Script ID. No changes were submitted.");
        if (script["Source"] is not JsonValue) throw new InvalidOperationException("MOVEit did not return the Script Source. Editing is unavailable until the full definition can be read.");
        _ = Language(MoveItRestClient.GetString(script, "Lang"));
        return script;
    }
    // Newlines are normalized only for verification/revisions. Source is never trimmed
    // or interpreted; JSON serialization preserves quotes, backslashes and literal macros.
    public static JsonObject Revision(JsonObject script)
    {
        var copy = (JsonObject)script.DeepClone();
        foreach (var key in copy.Select(x => x.Key).Where(x => x is "xAddEditDelete" or "UsedByCount" or "Renamed").ToArray()) copy.Remove(key);
        if (copy["Source"] is JsonValue value) copy["Source"] = Newlines(value.GetValue<string>());
        return copy;
    }
    public static string Newlines(string source) => source.Replace("\r\n", "\n").Replace('\r', '\n');
    public static JsonObject Compose(JsonObject? current, string id, ScriptEdit input)
    {
        var edit = Validate(input);
        var script = current is null ? new JsonObject { ["Group"] = string.IsNullOrWhiteSpace(edit.ResourceGroup) ? new JsonArray() : new JsonArray(edit.ResourceGroup.Trim()) } : Revision(current);
        script["ID"] = id; script["Name"] = edit.Name; script["Description"] = edit.Description;
        script["Lang"] = edit.Language; script["Source"] = edit.Source;
        return new JsonObject { ["Script"] = script };
    }
    public static ScriptDefinition Model(JsonObject raw, string id)
    {
        var script = Unwrap(raw, id);
        return new(id, MoveItRestClient.GetString(script, "Name"), MoveItRestClient.GetString(script, "Description"),
            Language(MoveItRestClient.GetString(script, "Lang")), MoveItRestClient.GetString(script, "Source"),
            ObjectEditVersion.Create("ScriptEditor", id, Revision(script)), ResourceGroupLookupService.FormatAssignments(script["Group"]));
    }
    public static bool Matches(ScriptEdit edit, ScriptDefinition saved) => edit.Name == saved.Name && edit.Description == saved.Description
        && edit.Language == saved.Language && Newlines(edit.Source) == Newlines(saved.Source);
}
