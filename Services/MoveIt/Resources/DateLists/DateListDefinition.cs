using System.Globalization;
using System.Text.Json.Nodes;

namespace MoveItAutomationUI.Services.MoveIt.Resources.DateLists;

// Only Name, Description and Date values belong to the form. Preserve native entry
// annotations and resource-group membership from a fresh definition under checkout.
public sealed record DateListEdit(string Name = "", string Description = "", IReadOnlyList<string>? Entries = null, string EditVersion = "");
public sealed record DateListDefinition(string Id, string Name, string Description, IReadOnlyList<string> Entries, string EditVersion, string ResourceGroups = "");
public sealed class DateListWriteUncertainException(string id, bool creating, Exception inner)
    : InvalidOperationException($"The Date List {(string.IsNullOrEmpty(id) ? "" : $"'{id}' ")}{(creating ? "may have been created" : "save may have completed")}, but its saved state could not be confirmed. Inspect Date Lists and reload before saving again.", inner)
{
    public string DateListId { get; } = id;
}
public static class DateListConfiguration
{
    public const int MaxEntries = 10000;
    public static string Id(string? value)
        => long.TryParse(value, out var id) && id > 0 && id.ToString(CultureInfo.InvariantCulture) == value?.Trim()
            ? value!.Trim() : throw new ArgumentException("A valid Date List ID is required.");
    public static string Date(string? value)
    {
        var date = value ?? "";
        var parts = date.Split('-');
        if (parts.Length == 3 && parts[0].Length == 4 && parts[2].Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var year) && year is >= 1 and <= 9999
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var day) && day is >= 1 and <= 31
            && (parts[1] == "*" || parts[1].Length == 2
                && DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))) return date;
        throw new ArgumentException($"Invalid Date List entry '{date}'. Use YYYY-MM-DD or YYYY-*-DD with a valid date.");
    }
    public static DateListEdit Validate(DateListEdit edit)
    {
        var name = edit.Name?.Trim() ?? "";
        if (name.Length is 0 or > 255) throw new ArgumentException("Date List Name is required and must contain at most 255 characters.");
        var entries = (edit.Entries ?? []).Select(Date).ToArray();
        if (entries.Length > MaxEntries) throw new ArgumentException($"A Date List may contain at most {MaxEntries} entries in this editor.");
        return edit with { Name = name, Description = edit.Description ?? "", Entries = entries };
    }
    public static JsonObject Unwrap(JsonObject raw, string id)
    {
        var list = raw["DateList"] as JsonObject ?? raw["object"] as JsonObject ?? raw;
        if (MoveItRestClient.GetString(list, "ID") != Id(id)) throw new InvalidOperationException("MOVEit returned a different Date List ID. No changes were submitted.");
        if (list["Entries"] is not JsonArray entries || entries.Any(entry => entry is not JsonObject || entry["Date"] is not JsonValue))
            throw new InvalidOperationException("MOVEit did not return the complete Date List entries. Reload before editing.");
        return list;
    }
    public static JsonObject Revision(JsonObject list)
    {
        var copy = (JsonObject)list.DeepClone();
        foreach (var key in new[] { "xAddEditDelete", "UsedByCount", "Renamed" }) copy.Remove(key);
        return copy;
    }
    public static JsonObject Compose(JsonObject? current, string id, DateListEdit input)
    {
        var edit = Validate(input);
        var list = current is null ? new JsonObject { ["Group"] = new JsonArray() } : Revision(current);
        var prior = (current?["Entries"] as JsonArray ?? []).OfType<JsonObject>().GroupBy(entry => MoveItRestClient.GetString(entry, "Date"))
            .ToDictionary(group => group.Key, group => new Queue<JsonObject>(group), StringComparer.Ordinal);
        var entries = new JsonArray();
        foreach (var date in edit.Entries!)
            entries.Add(prior.TryGetValue(date, out var retained) && retained.Count > 0 ? retained.Dequeue().DeepClone() : new JsonObject { ["Date"] = date });
        list["ID"] = id; list["Name"] = edit.Name; list["Description"] = edit.Description; list["Entries"] = entries;
        return new JsonObject { ["DateList"] = list };
    }
    public static DateListDefinition Model(JsonObject raw, string id)
    {
        var list = Unwrap(raw, id);
        return new(id, MoveItRestClient.GetString(list, "Name"), MoveItRestClient.GetString(list, "Description"),
            list["Entries"]!.AsArray().Select(entry => MoveItRestClient.GetString(entry!.AsObject(), "Date")).ToArray(),
            ObjectEditVersion.Create("DateListEditor", id, Revision(list)), ResourceGroupLookupService.FormatAssignments(list["Group"]));
    }
    // MOVEit may return entries in a different order; the dates are a set, not a schedule.
    public static bool Matches(DateListEdit edit, DateListDefinition saved) => edit.Name == saved.Name && edit.Description == saved.Description
        && (edit.Entries ?? []).Order(StringComparer.Ordinal).SequenceEqual(saved.Entries.Order(StringComparer.Ordinal));
}
