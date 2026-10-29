// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Optional RC2 Traditional Task editor Source Timestamp support.

using System.Globalization;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt.Tasks;

namespace MoveItAutomationUI.Services.MoveIt;

public interface ITraditionalTaskEditorSourceTimestampService
{
    Task<IReadOnlyList<TraditionalTaskEditorSourceTimestamp>> GetAsync(
        string taskId,
        string hostId,
        CancellationToken cancellationToken = default);

    Task<TraditionalTaskEditorSourceTimestamp> UpdateAsync(
        string taskId,
        TraditionalTaskEditorSourceTimestampUpdateRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class TraditionalTaskEditorSourceTimestampService : ITraditionalTaskEditorSourceTimestampService
{
    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss";
    private readonly ITaskCrudCoordinator _crud;

    public TraditionalTaskEditorSourceTimestampService(ITaskCrudCoordinator crud)
    {
        _crud = crud;
    }

    public async Task<IReadOnlyList<TraditionalTaskEditorSourceTimestamp>> GetAsync(
        string taskId,
        string hostId,
        CancellationToken cancellationToken = default)
    {
        var normalizedTaskId = Require(taskId, "Task ID", nameof(taskId));
        var normalizedHostId = Require(hostId, "Host ID", nameof(hostId));
        await ValidateEligibleSourceHostAsync(normalizedTaskId, normalizedHostId, cancellationToken);

        var root = await _crud.GetSourceTimestampsAsync(normalizedTaskId, normalizedHostId, cancellationToken);
        return ParseEntries(root, normalizedHostId);
    }

    public async Task<TraditionalTaskEditorSourceTimestamp> UpdateAsync(
        string taskId,
        TraditionalTaskEditorSourceTimestampUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalizedTaskId = Require(taskId, "Task ID", nameof(taskId));
        var normalizedHostId = Require(request.HostId, "Host ID", nameof(request.HostId));
        var normalizedPath = Require(request.Path, "Path", nameof(request.Path));
        var normalizedMask = Require(request.Mask, "File mask", nameof(request.Mask));
        var normalizedTimestamp = (request.Timestamp ?? string.Empty).Trim();

        await ValidateEligibleSourceHostAsync(normalizedTaskId, normalizedHostId, cancellationToken);

        if (normalizedTimestamp.Length > 0 && !DateTime.TryParseExact(
                normalizedTimestamp,
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
        {
            throw new ArgumentException($"Timestamp must use {TimestampFormat}.", nameof(request.Timestamp));
        }

        // MOVEit documents clearing an existing timestamp to treat all matching files as new. The native
        // PUT contract uses the same timestamp member, so allow an empty value only when the Host/path/mask
        // combination already exists. Do not manufacture an empty timestamp record through Add Timestamp.
        if (normalizedTimestamp.Length == 0)
        {
            var current = await _crud.GetSourceTimestampsAsync(normalizedTaskId, normalizedHostId, cancellationToken);
            var exists = ParseEntries(current, normalizedHostId).Any(item =>
                item.Path.Equals(normalizedPath, StringComparison.Ordinal)
                && item.Mask.Equals(normalizedMask, StringComparison.Ordinal));
            if (!exists)
                throw new ArgumentException("Timestamp is required when adding a new source timestamp.", nameof(request.Timestamp));
        }

        var response = await _crud.SetSourceTimestampAsync(
            normalizedTaskId,
            normalizedHostId,
            normalizedPath,
            normalizedMask,
            normalizedTimestamp,
            cancellationToken);

        var responseHostId = Read(response, "HostID").Trim();
        if (responseHostId.Length > 0 && !responseHostId.Equals(normalizedHostId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("MOVEit returned a Source Timestamp for a different Host than the one requested.");

        return new TraditionalTaskEditorSourceTimestamp(
            responseHostId.Length > 0 ? responseHostId : normalizedHostId,
            FirstNonBlank(Read(response, "NormalPath"), normalizedPath),
            FirstNonBlank(Read(response, "Mask"), normalizedMask),
            Read(response, "MostRecentStamp"),
            string.Empty,
            0,
            string.Empty);
    }

    private async Task ValidateEligibleSourceHostAsync(
        string taskId,
        string hostId,
        CancellationToken cancellationToken)
    {
        var task = await _crud.GetTraditionalDefinitionAsync(taskId, cancellationToken);
        var matchedHost = false;

        foreach (var wrapper in GetStepObjects(GetNode(task, "steps")))
        {
            if (GetNode(wrapper, "Source") is not JsonObject source) continue;
            var sourceHostId = Read(source, "HostID").Trim();
            var typeCode = TraditionalTaskEditorHostCapabilities.NormalizeTypeCode(Read(source, "Type"));
            if (sourceHostId.Length == 0 && typeCode.Equals("FileSystem", StringComparison.OrdinalIgnoreCase)) sourceHostId = "0";
            if (!sourceHostId.Equals(hostId, StringComparison.OrdinalIgnoreCase)) continue;

            matchedHost = true;
            if (sourceHostId == "0" && typeCode.Length == 0) typeCode = "FileSystem";
            if (!TraditionalTaskEditorHostCapabilities.SupportsSourceTimestamps(typeCode))
                continue;
            if (!ReadFlag(source, "NewFilesOnly"))
                continue;

            return;
        }

        if (matchedHost)
            throw new InvalidOperationException("Source timestamps require a supported Source with Collect only new files enabled.");
        throw new InvalidOperationException($"Host {hostId} is not used by a Source in Task {taskId}.");
    }

    private static IReadOnlyList<TraditionalTaskEditorSourceTimestamp> ParseEntries(JsonObject root, string requestedHostId)
    {
        if (GetNode(root, "Hosts") is not JsonObject hosts) return Array.Empty<TraditionalTaskEditorSourceTimestamp>();

        var result = new List<TraditionalTaskEditorSourceTimestamp>();
        foreach (var host in EnumerateObjects(GetNode(hosts, "Host")))
        {
            var hostId = Read(host, "ID").Trim();
            if (hostId.Length == 0) hostId = requestedHostId;
            if (!hostId.Equals(requestedHostId, StringComparison.OrdinalIgnoreCase)) continue;

            foreach (var path in EnumerateObjects(GetNode(host, "HostPath")))
            {
                var normalPath = Read(path, "Path").Trim();
                var mask = Read(path, "Mask").Trim();
                if (normalPath.Length == 0 || mask.Length == 0) continue;

                result.Add(new TraditionalTaskEditorSourceTimestamp(
                    hostId,
                    normalPath,
                    mask,
                    Read(path, "MostRecentStamp").Trim(),
                    Read(path, "DST").Trim(),
                    ReadInt(path, "Version"),
                    Read(path, "UStamp").Trim()));
            }
        }

        return result
            .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Mask, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<JsonObject> GetStepObjects(JsonNode? root)
    {
        if (root is JsonArray array) return array.OfType<JsonObject>().ToList();
        if (root is not JsonObject obj) return Array.Empty<JsonObject>();
        if (GetNode(obj, "Step") is JsonArray steps) return steps.OfType<JsonObject>().ToList();
        if (GetNode(obj, "Step") is JsonObject single) return new[] { single };
        return obj.Select(pair => pair.Value).OfType<JsonObject>().ToList();
    }

    private static IEnumerable<JsonObject> EnumerateObjects(JsonNode? node)
    {
        if (node is JsonObject single) yield return single;
        else if (node is JsonArray array)
        {
            foreach (var item in array.OfType<JsonObject>()) yield return item;
        }
    }

    private static JsonNode? GetNode(JsonObject? obj, string name)
    {
        if (obj is null) return null;
        if (obj.TryGetPropertyValue(name, out var direct)) return direct;
        foreach (var pair in obj)
            if (pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)) return pair.Value;
        return null;
    }

    private static string Read(JsonObject? obj, string name)
    {
        var node = GetNode(obj, name);
        if (node is not JsonValue value) return string.Empty;
        if (value.TryGetValue<string>(out var text)) return text ?? string.Empty;
        if (value.TryGetValue<int>(out var integer)) return integer.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue<long>(out var number)) return number.ToString(CultureInfo.InvariantCulture);
        if (value.TryGetValue<bool>(out var boolean)) return boolean ? "1" : "0";
        return value.ToJsonString().Trim('"');
    }

    private static bool ReadFlag(JsonObject obj, string name)
    {
        var node = GetNode(obj, name);
        if (node is not JsonValue value) return false;
        if (value.TryGetValue<bool>(out var boolean)) return boolean;
        if (value.TryGetValue<int>(out var integer)) return integer != 0;
        if (value.TryGetValue<long>(out var number)) return number != 0;
        var text = Read(obj, name).Trim();
        return text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase) || text.Equals("y", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadInt(JsonObject obj, string name)
        => int.TryParse(Read(obj, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static string Require(string? value, string label, string parameterName)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) throw new ArgumentException($"{label} is required.", parameterName);
        return normalized;
    }

    private static string FirstNonBlank(params string[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
