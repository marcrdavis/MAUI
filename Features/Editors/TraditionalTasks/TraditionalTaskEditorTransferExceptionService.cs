// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Optional RC2 Traditional Task editor runtime-state support.

using System.Text.Json.Nodes;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.MoveIt;

public interface ITraditionalTaskEditorTransferExceptionService
{
    Task<IReadOnlyList<TraditionalTaskEditorTransferException>> GetAsync(string taskId, CancellationToken cancellationToken = default);
    Task DeleteAsync(string taskId, IReadOnlyCollection<string> exceptionIds, CancellationToken cancellationToken = default);
}

public sealed class TraditionalTaskEditorTransferExceptionService : ITraditionalTaskEditorTransferExceptionService
{
    private readonly IMoveItRestClient _rest;

    public TraditionalTaskEditorTransferExceptionService(IMoveItRestClient rest)
    {
        _rest = rest;
    }

    public async Task<IReadOnlyList<TraditionalTaskEditorTransferException>> GetAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        var normalizedTaskId = (taskId ?? string.Empty).Trim();
        if (normalizedTaskId.Length == 0) throw new ArgumentException("Task ID is required.", nameof(taskId));

        // Do not reinterpret HTTP 400 as an empty result. The captured native endpoint contract
        // establishes the request/response shape, but not that every Bad Request means "no entries".
        // Let MOVEit's response body surface through MoveItApiException so malformed/auth/version
        // failures remain diagnosable inside the editor modal.
        var root = await _rest.GetTaskFailedInfoV0Async(normalizedTaskId, cancellationToken);

        var failedTasks = root["FailedTasks"] as JsonObject;
        if (failedTasks is null) return Array.Empty<TraditionalTaskEditorTransferException>();

        var task = EnumerateObjects(failedTasks["FailedTask"])
            .FirstOrDefault(item => StringValue(item, "ID") == normalizedTaskId)
            ?? EnumerateObjects(failedTasks["FailedTask"]).FirstOrDefault();
        if (task is null || task["ProcessedFiles"] is not JsonObject processedFiles)
            return Array.Empty<TraditionalTaskEditorTransferException>();

        return EnumerateObjects(processedFiles["pf"])
            .Select(item => new TraditionalTaskEditorTransferException(
                StringValue(item, "ExID"),
                StringValue(item, "SourceID"),
                StringValue(item, "FullPath"),
                LongValue(item, "Size"),
                StringValue(item, "Stamp"),
                StringValue(item, "Done"),
                StringValue(item, "SDone"),
                IntValue(item, "Virus"),
                IntValue(item, "p")))
            .Where(item => !string.IsNullOrWhiteSpace(item.ExceptionId))
            .ToList();
    }

    public async Task DeleteAsync(
        string taskId,
        IReadOnlyCollection<string> exceptionIds,
        CancellationToken cancellationToken = default)
    {
        var normalizedTaskId = (taskId ?? string.Empty).Trim();
        if (normalizedTaskId.Length == 0) throw new ArgumentException("Task ID is required.", nameof(taskId));
        var normalizedIds = (exceptionIds ?? Array.Empty<string>())
            .Select(value => (value ?? string.Empty).Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalizedIds.Count == 0) throw new ArgumentException("At least one transfer exception ID is required.", nameof(exceptionIds));

        var response = await _rest.DeleteTaskFailedEntriesV0Async(normalizedTaskId, normalizedIds, cancellationToken);
        var status = StringValue(response, "status");
        var errorCode = IntValue(response, "errorcode");
        if (!string.Equals(status, "success", StringComparison.OrdinalIgnoreCase) || errorCode != 0)
            throw new InvalidOperationException($"MOVEit did not remove the selected transfer exceptions (status '{status}', error code {errorCode}).");
    }

    private static IEnumerable<JsonObject> EnumerateObjects(JsonNode? node)
    {
        if (node is JsonObject single) yield return single;
        else if (node is JsonArray array)
        {
            foreach (var item in array.OfType<JsonObject>()) yield return item;
        }
    }

    private static string StringValue(JsonObject obj, string name)
    {
        if (obj[name] is not JsonValue value) return string.Empty;
        if (value.TryGetValue<string>(out var text)) return text ?? string.Empty;
        if (value.TryGetValue<long>(out var number)) return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (value.TryGetValue<int>(out var integer)) return integer.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return value.ToJsonString().Trim('"');
    }

    private static int IntValue(JsonObject obj, string name)
    {
        if (obj[name] is not JsonValue value) return 0;
        if (value.TryGetValue<int>(out var integer)) return integer;
        if (value.TryGetValue<long>(out var number)) return checked((int)number);
        return int.TryParse(StringValue(obj, name), out integer) ? integer : 0;
    }

    private static long LongValue(JsonObject obj, string name)
    {
        if (obj[name] is not JsonValue value) return 0;
        if (value.TryGetValue<long>(out var number)) return number;
        if (value.TryGetValue<int>(out var integer)) return integer;
        return long.TryParse(StringValue(obj, name), out number) ? number : 0;
    }
}
