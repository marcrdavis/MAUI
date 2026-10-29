// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Native MOVEit Task XML Schedule adapter used only where the v1 REST Task update cannot
// faithfully persist the server's Multi=1/Multi=2 overnight representation.

using System.Globalization;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace MoveItAutomationUI.Services.MoveIt.TraditionalTasks;

internal static class TraditionalTaskScheduleXml
{
    internal static IReadOnlyList<JsonObject> ReadSchedules(string taskXml)
    {
        var task = ParseTask(taskXml);
        var schedules = Child(task, "Schedules");
        if (schedules is null) return Array.Empty<JsonObject>();
        return schedules.Elements()
            .Where(element => IsName(element, "Schedule"))
            .Select(ToJsonSchedule)
            .ToList();
    }

    internal static string ApplySchedules(string taskXml, JsonArray requested)
    {
        ArgumentNullException.ThrowIfNull(requested);
        var task = ParseTask(taskXml);
        var schedulesElement = Child(task, "Schedules");
        if (schedulesElement is null)
        {
            schedulesElement = new XElement("Schedules");
            task.AddFirst(schedulesElement);
        }

        var requestedCopy = (JsonArray)requested.DeepClone();
        PrepareOvernightLinkageIds(task, requestedCopy);

        var existing = schedulesElement.Elements()
            .Where(element => IsName(element, "Schedule"))
            .ToList();
        var consumed = new HashSet<int>();
        var output = new List<XElement>();

        foreach (var node in requestedCopy)
        {
            if (node is not JsonObject item) continue;
            var id = GetString(item, "id").Trim();
            var multi = GetInt(item, "multi", 0);
            var serverIndex = GetInt(item, "serverIndex", GetInt(item, "index", -1));
            var index = FindSchedule(existing, consumed, id, multi, serverIndex);
            var cloneFromIndex = GetInt(item, "cloneFromServerIndex", -1);
            var template = index >= 0
                ? existing[index]
                : (cloneFromIndex >= 0 && cloneFromIndex < existing.Count ? existing[cloneFromIndex] : null);
            if (index >= 0) consumed.Add(index);

            var schedule = template is null ? new XElement("Schedule") : new XElement(template);
            SetOrRemoveAttribute(schedule, "ID", id);
            SetOrRemoveAttribute(schedule, "Multi", multi == 0 ? string.Empty : multi.ToString(CultureInfo.InvariantCulture));
            schedule.SetAttributeValue("OnlyUntilFirstSuccess", GetBool(item, "onlyUntilFirstSuccess") ? "1" : "0");
            schedule.SetAttributeValue("FailIfNoSuccessInSched", GetBool(item, "failIfNoSuccessInSchedule") ? "1" : "0");
            schedule.SetAttributeValue("RunEvenIfNotif", GetBool(item, "runEvenIfNotifications") ? "1" : "0");

            ApplyDays(schedule, item);
            ApplyDateLists(schedule, item);
            ApplyIntervals(schedule, item);
            output.Add(schedule);
        }

        schedulesElement.Elements().Where(element => IsName(element, "Schedule")).Remove();
        schedulesElement.Add(output);
        return task.ToString(SaveOptions.DisableFormatting);
    }

    private static void PrepareOvernightLinkageIds(XElement task, JsonArray requested)
    {
        var schedulesElement = Child(task, "Schedules");
        var existingSchedules = schedulesElement?.Elements().Where(element => IsName(element, "Schedule")).ToList()
            ?? new List<XElement>();
        var usedScheduleIds = new HashSet<int>();
        var usedIntervalIds = new HashSet<int>();

        static void AddId(HashSet<int> used, string value)
        {
            if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0)
                used.Add(id);
        }

        foreach (var schedule in existingSchedules)
        {
            AddId(usedScheduleIds, Attr(schedule, "ID"));
            var frequency = Child(schedule, "Frequency");
            if (frequency is null) continue;
            foreach (var interval in frequency.Elements().Where(element => IsName(element, "Interval")))
                AddId(usedIntervalIds, Attr(interval, "ID"));
        }
        foreach (var item in requested.OfType<JsonObject>())
        {
            AddId(usedScheduleIds, GetString(item, "id"));
            foreach (var interval in (GetArray(item, "intervals") ?? new JsonArray()).OfType<JsonObject>())
                AddId(usedIntervalIds, GetString(interval, "id"));
        }

        static string NextId(HashSet<int> used, int first)
        {
            var candidate = first;
            while (used.Contains(candidate)) candidate++;
            used.Add(candidate);
            return candidate.ToString(CultureInfo.InvariantCulture);
        }

        var groups = requested.OfType<JsonObject>()
            .Where(item => GetString(item, "pairKey").Trim().Length > 0)
            .GroupBy(item => GetString(item, "pairKey").Trim(), StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var segments = group.ToList();
            if (segments.Count != 2) continue;
            var before = segments.SingleOrDefault(item => GetString(item, "pairRole").Equals("beforeMidnight", StringComparison.OrdinalIgnoreCase));
            var after = segments.SingleOrDefault(item => GetString(item, "pairRole").Equals("afterMidnight", StringComparison.OrdinalIgnoreCase));
            if (before is null || after is null) continue;

            var beforeId = GetString(before, "id").Trim();
            var afterId = GetString(after, "id").Trim();
            var sharedScheduleId = beforeId.Length > 0 ? beforeId : afterId;
            if (sharedScheduleId.Length == 0) sharedScheduleId = NextId(usedScheduleIds, 1001);
            before["id"] = sharedScheduleId;
            after["id"] = sharedScheduleId;

            var beforeIntervals = (GetArray(before, "intervals") ?? new JsonArray()).OfType<JsonObject>().ToList();
            var afterIntervals = (GetArray(after, "intervals") ?? new JsonArray()).OfType<JsonObject>().ToList();
            for (var index = 0; index < Math.Min(beforeIntervals.Count, afterIntervals.Count); index++)
            {
                var preId = GetString(beforeIntervals[index], "id").Trim();
                var postId = GetString(afterIntervals[index], "id").Trim();
                var sharedIntervalId = preId.Length > 0 ? preId : postId;
                if (sharedIntervalId.Length == 0) sharedIntervalId = NextId(usedIntervalIds, 2001);
                beforeIntervals[index]["id"] = sharedIntervalId;
                afterIntervals[index]["id"] = sharedIntervalId;
            }
        }
    }

    private static int FindSchedule(IReadOnlyList<XElement> existing, HashSet<int> consumed, string id, int multi, int fallbackIndex)
    {
        if (id.Length > 0)
        {
            for (var index = 0; index < existing.Count; index++)
            {
                if (consumed.Contains(index)) continue;
                if (!Attr(existing[index], "ID").Equals(id, StringComparison.OrdinalIgnoreCase)) continue;
                var candidateMulti = IntAttr(existing[index], "Multi", 0);
                if (candidateMulti == multi) return index;
            }
            for (var index = 0; index < existing.Count; index++)
            {
                if (!consumed.Contains(index) && Attr(existing[index], "ID").Equals(id, StringComparison.OrdinalIgnoreCase))
                    return index;
            }
        }
        if (fallbackIndex >= 0 && fallbackIndex < existing.Count && !consumed.Contains(fallbackIndex)) return fallbackIndex;
        return -1;
    }

    private static void ApplyDays(XElement schedule, JsonObject item)
    {
        var weekdays = ToStrings(GetArray(item, "daysOfWeek"));
        var monthDays = ToStrings(GetArray(item, "daysOfMonth"));
        var existing = Child(schedule, "Days");
        if (weekdays.Count == 0 && monthDays.Count == 0)
        {
            existing?.Remove();
            return;
        }

        var days = existing is null ? new XElement("Days") : new XElement(existing);
        days.Elements().Where(element => IsName(element, "DayOfWeek") || IsName(element, "DayOfMonth")).Remove();
        foreach (var weekday in weekdays) days.Add(new XElement("DayOfWeek", weekday));
        foreach (var monthDay in monthDays) days.Add(new XElement("DayOfMonth", monthDay));
        if (existing is null) schedule.Add(days); else existing.ReplaceWith(days);
    }

    private static void ApplyDateLists(XElement schedule, JsonObject item)
    {
        var requested = (GetArray(item, "dateLists") ?? new JsonArray()).OfType<JsonObject>()
            .Where(reference => GetString(reference, "id").Trim().Length > 0)
            .ToList();
        var existing = schedule.Elements().Where(element => IsName(element, "DateListRef")).ToList();
        var output = new List<XElement>();
        foreach (var reference in requested)
        {
            var id = GetString(reference, "id").Trim();
            var template = existing.FirstOrDefault(element => Attr(element, "ID").Equals(id, StringComparison.OrdinalIgnoreCase));
            var dateList = template is null ? new XElement("DateListRef") : new XElement(template);
            dateList.SetAttributeValue("ID", id);
            dateList.SetAttributeValue("Run", GetBool(reference, "run") ? "1" : "0");
            output.Add(dateList);
        }
        existing.ForEach(element => element.Remove());
        if (output.Count > 0)
        {
            var frequency = Child(schedule, "Frequency");
            if (frequency is null) schedule.Add(output);
            else frequency.AddBeforeSelf(output);
        }
    }

    private static void ApplyIntervals(XElement schedule, JsonObject item)
    {
        var frequencyExisting = Child(schedule, "Frequency");
        var frequency = frequencyExisting is null ? new XElement("Frequency") : new XElement(frequencyExisting);
        var existingIntervals = frequency.Elements().Where(element => IsName(element, "Interval")).ToList();
        var output = new List<XElement>();
        var ordinal = 0;
        foreach (var intervalNode in GetArray(item, "intervals") ?? new JsonArray())
        {
            if (intervalNode is not JsonObject interval) continue;
            var id = GetString(interval, "id").Trim();
            var serverIndex = GetInt(interval, "serverIndex", ordinal);
            var template = existingIntervals.FirstOrDefault(element => id.Length > 0 && Attr(element, "ID").Equals(id, StringComparison.OrdinalIgnoreCase))
                ?? (serverIndex >= 0 && serverIndex < existingIntervals.Count ? existingIntervals[serverIndex] : null);
            var value = template is null ? new XElement("Interval") : new XElement(template);
            SetOrRemoveAttribute(value, "ID", id);
            value.SetAttributeValue("StartTime", NormalizeTime(GetString(interval, "startTime")));
            value.SetAttributeValue("EndTime", NormalizeTime(GetString(interval, "endTime")));
            value.SetAttributeValue("EveryMinutes", Math.Max(0, GetInt(interval, "everyMinutes", 0)).ToString(CultureInfo.InvariantCulture));
            output.Add(value);
            ordinal++;
        }
        frequency.Elements().Where(element => IsName(element, "Interval")).Remove();
        frequency.Add(output);
        if (frequencyExisting is null) schedule.Add(frequency); else frequencyExisting.ReplaceWith(frequency);
    }

    private static JsonObject ToJsonSchedule(XElement schedule)
    {
        var result = new JsonObject
        {
            ["ID"] = Attr(schedule, "ID"),
            ["Multi"] = IntAttr(schedule, "Multi", 0),
            ["OnlyUntilFirstSuccess"] = IntAttr(schedule, "OnlyUntilFirstSuccess", 0),
            ["FailIfNoSuccessInSched"] = IntAttr(schedule, "FailIfNoSuccessInSched", 0),
            ["RunEvenIfNotif"] = IntAttr(schedule, "RunEvenIfNotif", 0)
        };

        var daysElement = Child(schedule, "Days");
        if (daysElement is not null)
        {
            var days = new JsonObject();
            var weekdays = new JsonArray();
            foreach (var element in daysElement.Elements().Where(element => IsName(element, "DayOfWeek")))
                weekdays.Add(element.Value);
            var monthDays = new JsonArray();
            foreach (var element in daysElement.Elements().Where(element => IsName(element, "DayOfMonth")))
            {
                if (int.TryParse(element.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var day)) monthDays.Add(day);
                else monthDays.Add(element.Value.Trim());
            }
            if (weekdays.Count > 0) days["DayOfWeek"] = weekdays;
            if (monthDays.Count > 0) days["DayOfMonth"] = monthDays;
            result["Days"] = days;
        }

        var dateLists = new JsonArray();
        foreach (var reference in schedule.Elements().Where(element => IsName(element, "DateListRef")))
            dateLists.Add(new JsonObject { ["ID"] = Attr(reference, "ID"), ["Run"] = IntAttr(reference, "Run", 0) });
        if (dateLists.Count > 0) result["DateListRef"] = dateLists;

        var frequencyElement = Child(schedule, "Frequency");
        if (frequencyElement is not null)
        {
            var intervals = new JsonArray();
            foreach (var interval in frequencyElement.Elements().Where(element => IsName(element, "Interval")))
            {
                intervals.Add(new JsonObject
                {
                    ["ID"] = Attr(interval, "ID"),
                    ["StartTime"] = Attr(interval, "StartTime"),
                    ["EndTime"] = Attr(interval, "EndTime"),
                    ["EveryMinutes"] = IntAttr(interval, "EveryMinutes", 0)
                });
            }
            result["Frequency"] = new JsonObject { ["Interval"] = intervals };
        }
        return result;
    }

    private static XElement ParseTask(string taskXml)
    {
        if (string.IsNullOrWhiteSpace(taskXml)) throw new InvalidOperationException("MOVEit returned an empty Task XML configuration.");
        XElement task;
        try { task = XElement.Parse(taskXml, LoadOptions.PreserveWhitespace); }
        catch (Exception ex) { throw new InvalidOperationException("MOVEit returned invalid Task XML configuration.", ex); }
        if (!IsName(task, "Task")) throw new InvalidOperationException("MOVEit returned an unexpected Task XML configuration.");
        return task;
    }

    private static XElement? Child(XElement parent, string localName)
        => parent.Elements().FirstOrDefault(element => IsName(element, localName));

    private static bool IsName(XElement element, string localName)
        => element.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase);

    private static string Attr(XElement element, string name)
        => element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;

    private static int IntAttr(XElement element, string name, int fallback)
        => int.TryParse(Attr(element, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static void SetOrRemoveAttribute(XElement element, string name, string value)
    {
        var attribute = element.Attributes().FirstOrDefault(candidate => candidate.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(value)) attribute?.Remove();
        else if (attribute is null) element.SetAttributeValue(name, value);
        else attribute.Value = value;
    }

    private static JsonArray? GetArray(JsonObject? source, string name)
    {
        if (source is null) return null;
        foreach (var pair in source)
            if (pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)) return pair.Value as JsonArray;
        return null;
    }

    private static string GetString(JsonObject? source, string name)
    {
        if (source is null) return string.Empty;
        foreach (var pair in source)
            if (pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)) return NodeString(pair.Value);
        return string.Empty;
    }

    private static int GetInt(JsonObject? source, string name, int fallback)
        => int.TryParse(GetString(source, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static bool GetBool(JsonObject? source, string name)
    {
        var value = GetString(source, name).Trim();
        return value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1" || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string NodeString(JsonNode? node)
    {
        if (node is null) return string.Empty;
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text)) return text ?? string.Empty;
            if (value.TryGetValue<int>(out var number)) return number.ToString(CultureInfo.InvariantCulture);
            if (value.TryGetValue<long>(out var longNumber)) return longNumber.ToString(CultureInfo.InvariantCulture);
            if (value.TryGetValue<bool>(out var boolean)) return boolean ? "true" : "false";
        }
        return node.ToJsonString().Trim('"');
    }

    private static IReadOnlyList<string> ToStrings(JsonArray? values)
    {
        if (values is null) return Array.Empty<string>();
        return values
            .Select(NodeString)
            .Where(value => value.Trim().Length > 0)
            .Select(value => value.Trim())
            .ToList();
    }

    private static string NormalizeTime(string value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (TimeOnly.TryParseExact(trimmed, new[] { "H:mm", "HH:mm", "h:mm tt", "hh:mm tt" }, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
            return parsed.ToString("HH:mm", CultureInfo.InvariantCulture);
        return trimmed;
    }
}
