// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.MoveIt;

public interface IAutomationGlobalParameterService
{
    Task RecoverCheckoutAsync(CancellationToken cancellationToken = default);
    // Returns any existing Global Parameter cache immediately for initial navigation and indicates whether it should be reconciled in the background.
    Task<InitialCacheSnapshot<GlobalParameterCacheSnapshot>> GetInitialSnapshotAsync(CancellationToken cancellationToken = default);
    // Purpose: Returns the current Global Parameter inventory and refresh timestamp.
    Task<GlobalParameterCacheSnapshot> GetSnapshotAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
    // Purpose: Returns one Global Parameter by name.
    Task<AutomationGlobalParameter> GetAsync(string name, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GlobalParameterTaskReference>> GetAssignedTasksAsync(string name, CancellationToken cancellationToken = default);
    Task<GlobalParameterEditSnapshot> GetEditSnapshotAsync(string name, CancellationToken cancellationToken = default);
    // Purpose: Creates a Global Parameter after validating its name, value, and security mode.
    Task<AutomationGlobalParameter> CreateAsync(string name, string value, bool secure, CancellationToken cancellationToken = default);
    // Purpose: Updates a Global Parameter while preserving protected secure values when requested.
    Task<AutomationGlobalParameter> UpdateAsync(string originalName, string name, string value, bool secure, bool preserveExistingSecureValue, CancellationToken cancellationToken = default);
    // Purpose: Validates delete and returns the information required before execution.
    Task<IReadOnlyList<GlobalParameterDeletePreflightResult>> PreflightDeleteAsync(IEnumerable<string> names, bool allowSecure, CancellationToken cancellationToken = default);
    // Purpose: Deletes the requested Global Parameters.
    Task<GlobalParameterBulkDeleteResult> DeleteAsync(IEnumerable<string> names, bool allowSecure, CancellationToken cancellationToken = default);
}

public sealed class AutomationGlobalParameterService : IAutomationGlobalParameterService
{
    public Task RecoverCheckoutAsync(CancellationToken cancellationToken = default)
        => _com.RecoverGlobalParameterCheckoutAsync(cancellationToken);
    public Task<GlobalParameterEditSnapshot> GetEditSnapshotAsync(string name, CancellationToken cancellationToken = default)
        => _com.GetGlobalParameterEditSnapshotAsync(name, cancellationToken);
    private const string CachePrefix = "global-parameters:";

    private readonly IMoveItRestClient _rest;
    private readonly IMoveItComClient _com;
    private readonly IMoveItSessionService _session;
    private readonly IMemoryCache _cache;
    private readonly GlobalParameterBehaviorOptions _options;

    // Purpose: Initializes automation global parameter service with its required dependencies.
    public AutomationGlobalParameterService(
        IMoveItRestClient rest,
        IMoveItComClient com,
        IMoveItSessionService session,
        IMemoryCache cache,
        IOptionsSnapshot<GlobalParameterBehaviorOptions> options)
    {
        _rest = rest;
        _com = com;
        _session = session;
        _cache = cache;
        _options = options.Value;
    }

    // Returns the initial Global Parameter snapshot through the shared cached-inventory navigation contract.
    public async Task<InitialCacheSnapshot<GlobalParameterCacheSnapshot>> GetInitialSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var key = CacheKey();
        return await MoveItServiceCache.ResolveInitialAsync<CacheEntry, GlobalParameterCacheSnapshot>(
            _cache,
            key,
            _options.CacheRefreshSeconds.Value,
            entry => entry.RefreshedUtc,
            entry => new GlobalParameterCacheSnapshot(entry.Items, entry.RefreshedUtc),
            token => GetSnapshotAsync(false, token),
            cancellationToken);
    }

    // Purpose: Returns the current Global Parameter inventory and refresh timestamp.
    public async Task<GlobalParameterCacheSnapshot> GetSnapshotAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        var key = CacheKey();
        var entry = await MoveItServiceCache.ResolveAsync<CacheEntry>(
            _cache, key, _options.CacheRefreshSeconds.Value, forceRefresh,
            value => value.RefreshedUtc,
            async token =>
            {
                var raw = await _rest.GetGlobalParametersAsync(token);
                var parsed = raw.Select(Parse).Where(item => item.Name.Length > 0).OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
                return new CacheEntry(parsed, DateTimeOffset.UtcNow);
            },
            cancellationToken);
        return new(entry.Items, entry.RefreshedUtc);
    }

    // Purpose: Returns one Global Parameter by name.
    public async Task<AutomationGlobalParameter> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeName(name);
        var raw = await _rest.GetGlobalParameterAsync(normalized, cancellationToken);
        var item = Parse(raw);
        if (item.Name.Length == 0) throw new MoveItApiException($"MOVEit returned an invalid Global Parameter response for '{normalized}'.");
        return item;
    }

    // Reuse the same exact macro-name rules as Task preflight; read only Tasks visible to this session.
    public async Task<IReadOnlyList<GlobalParameterTaskReference>> GetAssignedTasksAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeName(name);
        var tasks = await _rest.GetTaskGlobalParameterReferenceDefinitionsAsync(cancellationToken);
        return tasks.Where(task => TaskPreflightService.CollectGlobalParameterNames(task).Contains(normalized))
            .Select(task => new GlobalParameterTaskReference(MoveItRestClient.GetString(task, "ID").Trim(), MoveItRestClient.GetString(task, "Name").Trim()))
            .Where(task => task.Id.Length > 0).DistinctBy(task => task.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(task => task.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Purpose: Creates a Global Parameter after validating its name, value, and security mode.
    public async Task<AutomationGlobalParameter> CreateAsync(string name, string value, bool secure, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeName(name);
        if (await _rest.GlobalParameterNameExistsAsync(normalized, null, cancellationToken))
            throw new InvalidOperationException($"A Global Parameter named '{normalized}' already exists.");

        await _com.CreateGlobalParameterAsync(normalized, value ?? string.Empty, secure, cancellationToken);
        Invalidate();
        var created = await GetAsync(normalized, cancellationToken);
        if (created.Secure != secure)
            throw new MoveItComException($"MOVEit returned Global Parameter '{normalized}' with Secure={(created.Secure ? 1 : 0)} after MAUI requested Secure={(secure ? 1 : 0)}.");
        return created;
    }

    // Purpose: Updates a Global Parameter while preserving protected secure values when requested.
    public async Task<AutomationGlobalParameter> UpdateAsync(
        string originalName,
        string name,
        string value,
        bool secure,
        bool preserveExistingSecureValue,
        CancellationToken cancellationToken = default)
    {
        var original = NormalizeName(originalName);
        var updated = NormalizeName(name);
        if (original.Equals(updated, StringComparison.OrdinalIgnoreCase)
            && !original.Equals(updated, StringComparison.Ordinal))
            throw new InvalidOperationException("Changing only the capitalization of a Global Parameter name is not supported in this build. Use a distinct temporary name first if that change is required.");

        var current = await GetAsync(original, cancellationToken);
        if (preserveExistingSecureValue && !current.Secure)
            throw new InvalidOperationException("PreserveExistingSecureValue is valid only for an existing secure Global Parameter.");
        if (!original.Equals(updated, StringComparison.OrdinalIgnoreCase)
            && await _rest.GlobalParameterNameExistsAsync(updated, original, cancellationToken))
            throw new InvalidOperationException($"A Global Parameter named '{updated}' already exists.");

        await _com.UpdateGlobalParameterAsync(original, updated, value ?? string.Empty, secure, preserveExistingSecureValue, cancellationToken);
        Invalidate();
        var saved = await GetAsync(updated, cancellationToken);
        if (!preserveExistingSecureValue && saved.Secure != secure)
            throw new MoveItComException($"MOVEit returned Global Parameter '{updated}' with Secure={(saved.Secure ? 1 : 0)} after MAUI requested Secure={(secure ? 1 : 0)}.");
        if (preserveExistingSecureValue && !saved.Secure)
            throw new MoveItComException($"MOVEit returned Global Parameter '{updated}' as non-secure after MAUI preserved its existing secure value.");
        return saved;
    }

    // Purpose: Validates delete and returns the information required before execution.
    public async Task<IReadOnlyList<GlobalParameterDeletePreflightResult>> PreflightDeleteAsync(
        IEnumerable<string> names,
        bool allowSecure,
        CancellationToken cancellationToken = default)
    {
        var requested = NormalizeNames(names);
        var snapshot = await GetSnapshotAsync(true, cancellationToken);
        var byName = snapshot.Parameters.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
        var results = new List<GlobalParameterDeletePreflightResult>();
        foreach (var name in requested)
        {
            if (!byName.TryGetValue(name, out var item))
            {
                results.Add(new(name, false, false, $"Global Parameter '{name}' is no longer visible or no longer exists."));
                continue;
            }
            if (item.Secure && !allowSecure)
            {
                results.Add(new(item.Name, true, false, $"Secure Global Parameter '{item.Name}' must be deleted individually."));
                continue;
            }
            results.Add(new(item.Name, item.Secure, true, string.Empty));
        }
        return results;
    }

    // Purpose: Deletes the requested Global Parameters.
    public async Task<GlobalParameterBulkDeleteResult> DeleteAsync(
        IEnumerable<string> names,
        bool allowSecure,
        CancellationToken cancellationToken = default)
    {
        var requested = NormalizeNames(names);
        var preflight = await PreflightDeleteAsync(requested, allowSecure, cancellationToken);
        var deletable = preflight.Where(item => item.CanDelete).Select(item => item.Name).ToList();
        var results = preflight.Where(item => !item.CanDelete)
            .Select(item => new GlobalParameterBulkDeleteItemResult(item.Name, false, true, item.Message))
            .ToList();

        if (deletable.Count > 0)
        {
            try
            {
                await _com.DeleteGlobalParametersAsync(deletable, cancellationToken);
                Invalidate();
                results.AddRange(deletable.Select(name => new GlobalParameterBulkDeleteItemResult(name, true, false, string.Empty)));
            }
            catch (Exception ex) when (ex is MoveItComException or InvalidOperationException or ArgumentException)
            {
                results.AddRange(deletable.Select(name => new GlobalParameterBulkDeleteItemResult(name, false, false, ex.Message)));
            }
        }

        var order = requested.Select((name, index) => new { name, index }).ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);
        return new(results.OrderBy(item => order.TryGetValue(item.Name, out var index) ? index : int.MaxValue).ToList());
    }

    // Purpose: Maps a raw MOVEit Global Parameter record to the MAUI model.
    private static AutomationGlobalParameter Parse(JsonObject raw)
    {
        var name = Read(raw, "name", "Name").Trim();
        var secureText = Read(raw, "secure", "Secure").Trim();
        var secure = secureText.Equals("1", StringComparison.OrdinalIgnoreCase) || secureText.Equals("true", StringComparison.OrdinalIgnoreCase);
        return new AutomationGlobalParameter
        {
            Name = name,
            Value = Read(raw, "value", "Value"),
            DisplayValue = Read(raw, "displayValue", "DisplayValue"),
            CountedDisplayValue = Read(raw, "countedDisplayValue", "CountedDisplayValue"),
            Group = ReadGroups(raw),
            Secure = secure
        };
    }


    // Reads MOVEit Resource Group membership from the complete Global Parameter response.
    private static string ReadGroups(JsonObject raw)
    {
        if (!TryGetIgnoreCase(raw, "Group", out var node) || node is null) return string.Empty;
        if (node is JsonArray array)
            return string.Join(", ", array.Select(ReadNode).Where(value => !string.IsNullOrWhiteSpace(value)));
        return ReadNode(node).Trim();
    }

    // Purpose: Reads a normalized value from a Global Parameter JSON node.
    private static string Read(JsonObject raw, params string[] keys)
    {
        foreach (var key in keys)
            if (TryGetIgnoreCase(raw, key, out var node)) return ReadNode(node);
        return string.Empty;
    }

    // Purpose: Attempts to retrieve ignore case.
    private static bool TryGetIgnoreCase(JsonObject obj, string key, out JsonNode? value)
    {
        foreach (var pair in obj)
        {
            if (!pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            value = pair.Value;
            return true;
        }
        value = null;
        return false;
    }

    // Purpose: Reads node.
    private static string ReadNode(JsonNode? node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text)) return text ?? string.Empty;
            if (value.TryGetValue<int>(out var integer)) return integer.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (value.TryGetValue<long>(out var longInteger)) return longInteger.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (value.TryGetValue<bool>(out var boolean)) return boolean ? "true" : "false";
        }
        return node?.ToJsonString() ?? string.Empty;
    }

    // Purpose: Returns whether fresh.

    // Purpose: Builds the Global Parameter cache key for the current MOVEit session.
    private string CacheKey() => MoveItServiceCache.UserScopedKey(CachePrefix, _session);

    // Purpose: Invalidates the Global Parameter cache for the current MOVEit session.
    private void Invalidate() => _cache.Remove(CacheKey());

    // Purpose: Normalizes name.
    private static string NormalizeName(string? name)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length == 0) throw new ArgumentException("Global Parameter Name is required.");
        if (value.Length > 254) throw new ArgumentException("Global Parameter Name cannot exceed 254 characters.");
        return value;
    }

    // Purpose: Normalizes names.
    private static IReadOnlyList<string> NormalizeNames(IEnumerable<string>? names)
    {
        var values = (names ?? Array.Empty<string>())
            .Select(name => (name ?? string.Empty).Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (values.Count == 0) throw new ArgumentException("Select at least one Global Parameter.");
        return values;
    }

    private sealed record CacheEntry(IReadOnlyList<AutomationGlobalParameter> Items, DateTimeOffset RefreshedUtc);
}
