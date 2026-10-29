using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MoveItAutomationUI.Services.MoveIt.Tasks;

// Native date designations only. Other MOVEit macros and all unrelated Task fields are retained.
public static class TaskRunAsOfDefinition
{
    private static readonly Regex Tokens = new(@"\[(AAAA|AAA|A|B|DD|D|HHHH|HHH|HH|H|II|JJJ|J|KKK|K|MMMM|MMM|MM|M|SS|S|TT|T|WW|W|XX|X|YYYY|YY)([+-]*)\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Fields = new(["Path", "FolderName", "FileMask", "ExFile", "ExFo", "RenameTo", "FileName", "ZipFileName"], StringComparer.OrdinalIgnoreCase);
    public sealed record Change(string Kind, string StepId, string Field, string Before, string After);

    public static string Expand(string text, DateTime selected)
    {
        var parts = text.Split('"');
        // Unquoted parts form one phrase; each quoted part has its own date offset.
        var outside = Adjust(selected, string.Concat(parts.Where((_, index) => index % 2 == 0)));
        for (var index = 0; index < parts.Length; index++)
        {
            var date = index % 2 == 0 ? outside : Adjust(selected, parts[index]);
            parts[index] = Tokens.Replace(parts[index], match => Format(date, match.Groups[1].Value.ToUpperInvariant()));
        }
        return string.Join('"', parts);
    }

    private static DateTime Adjust(DateTime value, string phrase)
    {
        foreach (Match match in Tokens.Matches(phrase))
            foreach (var operation in match.Groups[2].Value)
            {
                var amount = operation == '+' ? 1 : -1;
                var token = match.Groups[1].Value.ToUpperInvariant();
                value = token[0] switch
                {
                    'Y' => value.AddYears(amount), 'M' => value.AddMonths(amount),
                    'W' or 'X' => value.AddDays(amount * 7), 'H' => value.AddHours(amount),
                    'I' => value.AddHours(amount * 12), 'T' => value.AddMinutes(amount),
                    'S' => value.AddSeconds(amount), _ => value.AddDays(amount)
                };
            }
        return value;
    }

    private static string Format(DateTime date, string token)
    {
        var culture = CultureInfo.InvariantCulture;
        string Number(int value, int length = 0) => value.ToString(length == 0 ? "0" : new string('0', length), culture);
        var week = (date.DayOfYear - 1 + (int)new DateTime(date.Year, 1, 1).DayOfWeek) / 7;
        return token switch
        {
            "A" => Number((int)date.DayOfWeek), "B" => Number((int)date.DayOfWeek + 1),
            "AAA" => date.ToString("ddd", culture), "AAAA" => date.ToString("dddd", culture),
            "D" => Number(date.Day), "DD" => Number(date.Day, 2),
            "H" => Number(date.Hour), "HH" => Number(date.Hour, 2),
            "HHH" => Number(date.Hour % 12 == 0 ? 12 : date.Hour % 12),
            "HHHH" => Number(date.Hour % 12 == 0 ? 12 : date.Hour % 12, 2),
            "II" => date.Hour < 12 ? "am" : "pm",
            "J" => Number(date.DayOfYear - 1), "JJJ" => Number(date.DayOfYear - 1, 3),
            "K" => Number(date.DayOfYear), "KKK" => Number(date.DayOfYear, 3),
            "M" => Number(date.Month), "MM" => Number(date.Month, 2), "MMM" => date.ToString("MMM", culture), "MMMM" => date.ToString("MMMM", culture),
            "S" => Number(date.Second), "SS" => Number(date.Second, 2), "T" => Number(date.Minute), "TT" => Number(date.Minute, 2),
            "W" => Number(week), "WW" => Number(week, 2), "X" => Number(week + 1), "XX" => Number(week + 1, 2),
            "YY" => date.ToString("yy", culture), "YYYY" => date.ToString("yyyy", culture), _ => throw new InvalidOperationException("Unknown date designation.")
        };
    }

    public static IReadOnlyList<Change> Describe(JsonObject task, DateTime selected)
    {
        var changes = new List<Change>();
        foreach (var (kind, step) in Steps(task))
        {
            foreach (var field in step.Where(pair => Fields.Contains(pair.Key)).ToArray())
            {
                var before = field.Value?.ToString() ?? "";
                if (!Tokens.IsMatch(before)) continue;
                var id = Text(step, "ID");
                if (id.Length == 0) throw new InvalidOperationException("Save the Task before using Run As Of.");
                changes.Add(new(kind, id, field.Key, before, Expand(before, selected)));
            }
            // FolderName is the editable MOVEit Transfer path. Clear a resolved folder ID
            // while using its temporary macro expansion, then restore that ID with the path.
            if (changes.Any(change => change.Kind == kind && change.StepId == Text(step, "ID") && change.Field.Equals("FolderName", StringComparison.OrdinalIgnoreCase))
                && Text(step, "FolderID").Length > 0)
                changes.Add(new(kind, Text(step, "ID"), "FolderID", Text(step, "FolderID"), ""));
        }
        return changes;
    }

    public static IEnumerable<(string Kind, JsonObject Step)> Steps(JsonNode? node)
    {
        if (node is JsonArray array)
        { foreach (var child in array) foreach (var step in Steps(child)) yield return step; }
        else if (node is JsonObject obj)
            foreach (var pair in obj)
            {
                if ((pair.Key.Equals("Source", StringComparison.OrdinalIgnoreCase) || pair.Key.Equals("Destination", StringComparison.OrdinalIgnoreCase)) && pair.Value is JsonObject body)
                    yield return (pair.Key, body);
                else foreach (var step in Steps(pair.Value)) yield return step;
            }
    }

    public static JsonObject Apply(JsonObject current, IReadOnlyList<Change> changes, bool restore, bool originallyEnabled)
    {
        var targets = Steps(current).ToArray();
        foreach (var change in changes)
        {
            var matches = targets.Where(target => target.Kind.Equals(change.Kind, StringComparison.OrdinalIgnoreCase) && Text(target.Step, "ID") == change.StepId).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("The Task steps changed. Restore the Task from its Undo snapshot after inspecting it.");
            var step = matches[0].Step;
            var value = Text(step, change.Field);
            if (value != (restore ? change.After : change.Before) && !(restore && value == change.Before))
                throw new InvalidOperationException("A temporary Task field was changed by another edit. MAUI preserved that edit; inspect the Task and its Undo snapshot.");
            step[change.Field] = restore ? change.Before : change.After;
        }
        if (!restore && Enabled(current) != originallyEnabled)
            throw new InvalidOperationException("The Task enabled state changed. Reload it before running.");
        if (restore && !originallyEnabled && Enabled(current))
            throw new InvalidOperationException("The Task was enabled by another edit. MAUI preserved that change; inspect the Task and its Undo snapshot.");
        current["Active"] = restore && originallyEnabled ? 1 : 0;
        return current;
    }

    public static bool Enabled(JsonObject task) => Text(task, "Active") is "1" or "true" or "True";
    public static string Text(JsonObject obj, string name) => obj.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value?.ToString() ?? "";
}
