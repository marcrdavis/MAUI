// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.MoveIt.Resources.Scripts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.MoveIt;

public interface IAutomationScriptService
{
    // Purpose: Returns the current normalized custom and built-in Script inventory.
    Task<IReadOnlyList<AutomationScript>> GetScriptsAsync(CancellationToken cancellationToken = default);
    // Returns any existing Script cache immediately for initial navigation and indicates whether it should be reconciled in the background.
    Task<InitialCacheSnapshot<ScriptCacheSnapshot>> GetInitialScriptSnapshotAsync(CancellationToken cancellationToken = default);
    // Purpose: Returns script snapshot.
    Task<ScriptCacheSnapshot> GetScriptSnapshotAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
    // Purpose: Returns one Script by persistent ID.
    Task<AutomationScript> GetScriptAsync(string scriptId, CancellationToken cancellationToken = default);
    // Purpose: Returns assigned tasks.
    Task<IReadOnlyList<AutomationScriptTaskReference>> GetAssignedTasksAsync(string scriptId, CancellationToken cancellationToken = default);
    // Purpose: Creates script after validating the supplied request.
    Task<MoveItCreatedScript> CreateScriptAsync(string name, string language, CancellationToken cancellationToken = default, string? resourceGroup = null);
    // Purpose: Updates basic details while preserving the applicable guardrails.
    Task UpdateBasicDetailsAsync(string scriptId, string name, string description, CancellationToken cancellationToken = default);
    // Purpose: Returns script configuration snapshot.
    Task<ScriptConfigurationSnapshot> GetScriptConfigurationSnapshotAsync(string scriptId, CancellationToken cancellationToken = default);
    // Purpose: Restores script configuration from a previously captured configuration.
    Task<ScriptConfigurationSnapshot> RestoreScriptConfigurationAsync(string scriptId, string scriptXml, CancellationToken cancellationToken = default);
    // Purpose: Validates delete script and returns the information required before execution.
    Task<ScriptDeletePreflightResult> PreflightDeleteScriptAsync(string scriptId, CancellationToken cancellationToken = default);
    // Purpose: Validates delete scripts and returns the information required before execution.
    Task<IReadOnlyList<ScriptDeletePreflightResult>> PreflightDeleteScriptsAsync(IEnumerable<string> scriptIds, CancellationToken cancellationToken = default);
    // Purpose: Deletes script after applying the required guardrails.
    Task<ScriptDeleteResult> DeleteScriptAsync(string scriptId, CancellationToken cancellationToken = default);
    // Purpose: Deletes scripts after applying the required guardrails.
    Task<ScriptBulkDeleteResult> DeleteScriptsAsync(IEnumerable<string> scriptIds, CancellationToken cancellationToken = default);
}

public sealed record ScriptCacheSnapshot(IReadOnlyList<AutomationScript> Scripts, DateTimeOffset RefreshedUtc);
public sealed record ScriptConfigurationSnapshot(string Id, string Name, string LanguageCode, string Xml);
public sealed record ScriptDeletePreflightResult(
    bool CanDelete,
    string ScriptId,
    string ScriptName,
    IReadOnlyList<AutomationScriptTaskReference> TaskReferences,
    string Message);
public sealed record ScriptDeleteResult(string Id, string Name);
public sealed record ScriptBulkDeleteItemResult(string Id, string Name, bool Success, bool Skipped, string Message);
public sealed record ScriptBulkDeleteResult(IReadOnlyList<ScriptBulkDeleteItemResult> Items)
{
    public int Succeeded => Items.Count(item => item.Success);
    public int Skipped => Items.Count(item => item.Skipped);
    public int Failed => Items.Count(item => !item.Success && !item.Skipped);
}

public sealed class ScriptNameAlreadyExistsException : InvalidOperationException
{
    public string ScriptName { get; }

    // Purpose: Initializes the duplicate-name exception for a custom Script.
    public ScriptNameAlreadyExistsException(string scriptName)
        : base($"A Script named '{scriptName}' already exists. Enter a unique Script Name.")
    {
        ScriptName = scriptName;
    }
}

public sealed class AutomationScriptService : IAutomationScriptService
{
    private const string CachePrefix = "MoveItAutomationUI:Scripts:";

    private readonly IMoveItRestClient _restClient;
    private readonly IMoveItComClient _comClient;
    private readonly IMoveItSessionService _sessionService;
    private readonly IMemoryCache _memoryCache;
    private readonly ScriptBehaviorOptions _scriptBehaviorOptions;
    private readonly ILogger<AutomationScriptService> _logger;
    private readonly IScriptCrudCoordinator _crud;

    // Purpose: Initializes automation script service with its required dependencies.
    public AutomationScriptService(
        IMoveItRestClient restClient,
        IMoveItComClient comClient,
        IMoveItSessionService sessionService,
        IMemoryCache memoryCache,
        IOptionsSnapshot<ScriptBehaviorOptions> scriptBehaviorOptions,
        ILogger<AutomationScriptService> logger,
        IScriptCrudCoordinator? crud = null)
    {
        _restClient = restClient;
        _comClient = comClient;
        _sessionService = sessionService;
        _memoryCache = memoryCache;
        _scriptBehaviorOptions = scriptBehaviorOptions.Value;
        _logger = logger;
        _crud = crud ?? new ScriptCrudCoordinator(restClient, sessionService, memoryCache, comClient);
    }

    // Purpose: Returns the current normalized custom and built-in Script inventory.
    public async Task<IReadOnlyList<AutomationScript>> GetScriptsAsync(CancellationToken cancellationToken = default)
        => (await GetScriptSnapshotAsync(false, cancellationToken)).Scripts;

    // Returns the initial Script snapshot through the shared cached-inventory navigation contract.
    public async Task<InitialCacheSnapshot<ScriptCacheSnapshot>> GetInitialScriptSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var cacheKey = GetCacheKey();
        return await MoveItServiceCache.ResolveInitialAsync<ScriptCacheEntry, ScriptCacheSnapshot>(
            _memoryCache,
            cacheKey,
            _scriptBehaviorOptions.CacheRefreshSeconds.Value,
            entry => entry.RefreshedUtc,
            entry => new ScriptCacheSnapshot(entry.Scripts, entry.RefreshedUtc),
            token => GetScriptSnapshotAsync(false, token),
            cancellationToken);
    }

    // Purpose: Returns script snapshot.
    public async Task<ScriptCacheSnapshot> GetScriptSnapshotAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        var cacheKey = GetCacheKey();
        var refreshed = await RefreshScriptsCoreAsync(cacheKey, forceRefresh, cancellationToken);
        return new ScriptCacheSnapshot(refreshed.Scripts, refreshed.RefreshedUtc);
    }

    // Purpose: Returns one Script by persistent ID.
    public async Task<AutomationScript> GetScriptAsync(string scriptId, CancellationToken cancellationToken = default)
    {
        var normalizedScriptId = NormalizeScriptId(scriptId);
        var scripts = await GetScriptsAsync(cancellationToken);
        var cached = scripts.FirstOrDefault(script => string.Equals(script.Id, normalizedScriptId, StringComparison.OrdinalIgnoreCase));
        if (cached is not null) return cached;

        // A forced refresh checks both custom and built-in inventories without guessing the Script type.
        var refreshed = (await GetScriptSnapshotAsync(true, cancellationToken)).Scripts
            .FirstOrDefault(script => string.Equals(script.Id, normalizedScriptId, StringComparison.OrdinalIgnoreCase));
        return refreshed ?? throw new MoveItApiException($"MOVEit Script ID '{normalizedScriptId}' could not be found or is not visible to the current user.");
    }

    // Purpose: Returns assigned tasks.
    public async Task<IReadOnlyList<AutomationScriptTaskReference>> GetAssignedTasksAsync(string scriptId, CancellationToken cancellationToken = default)
    {
        var normalizedScriptId = NormalizeScriptId(scriptId);
        var tasks = await _restClient.GetTaskScriptReferenceDefinitionsAsync(cancellationToken);
        return BuildTaskReferences(tasks, normalizedScriptId);
    }

    // Purpose: Creates script after validating the supplied request.
    public async Task<MoveItCreatedScript> CreateScriptAsync(string name, string language, CancellationToken cancellationToken = default, string? resourceGroup = null)
    {
        var normalizedLanguage = ScriptConfiguration.Language(language);
        var placeholder = normalizedLanguage == "PS1" ? "# MAUI placeholder - edit this script in the MOVEit Automation editor." : "' MAUI placeholder - edit this script in the MOVEit Automation editor.";
        var saved = await Crud.CreateAsync(new ScriptEdit(name, Language: normalizedLanguage, Source: placeholder, ResourceGroup: resourceGroup ?? ""), cancellationToken);
        return new(saved.Id, saved.Name, saved.Language);
    }

    // Information edits share the same fresh-read, checked-out v0 composition as
    // the source editor, while preserving Source, Lang and unowned configuration.
    public async Task UpdateBasicDetailsAsync(string scriptId, string name, string description, CancellationToken cancellationToken = default)
        => _ = await Crud.SaveAsync(scriptId, new ScriptEdit(name, description), informationOnly: true, ct: cancellationToken);

    private IScriptCrudCoordinator Crud => _crud;

    // Purpose: Returns script configuration snapshot.
    public async Task<ScriptConfigurationSnapshot> GetScriptConfigurationSnapshotAsync(string scriptId, CancellationToken cancellationToken = default)
    {
        var result = await Crud.GetNativeConfigurationAsync(scriptId, cancellationToken);
        return new ScriptConfigurationSnapshot(result.Id, result.Name, result.LanguageCode, result.Xml);
    }

    // Purpose: Restores script configuration from a previously captured configuration.
    public async Task<ScriptConfigurationSnapshot> RestoreScriptConfigurationAsync(string scriptId, string scriptXml, CancellationToken cancellationToken = default)
    {
        var result = await Crud.RestoreNativeConfigurationAsync(scriptId, scriptXml, cancellationToken);
        InvalidateScriptCache();
        return new ScriptConfigurationSnapshot(result.Id, result.Name, result.LanguageCode, result.Xml);
    }

    // Purpose: Validates delete script and returns the information required before execution.
    public async Task<ScriptDeletePreflightResult> PreflightDeleteScriptAsync(string scriptId, CancellationToken cancellationToken = default)
    {
        var normalizedScriptId = NormalizeScriptId(scriptId);
        var results = await BuildDeletePreflightsAsync(new[] { normalizedScriptId }, cancellationToken);
        return results[0];
    }

    // Purpose: Validates delete scripts and returns the information required before execution.
    public async Task<IReadOnlyList<ScriptDeletePreflightResult>> PreflightDeleteScriptsAsync(IEnumerable<string> scriptIds, CancellationToken cancellationToken = default)
    {
        var normalizedIds = NormalizeScriptIds(scriptIds);
        return await BuildDeletePreflightsAsync(normalizedIds, cancellationToken);
    }

    // Purpose: Deletes script after applying the required guardrails.
    public async Task<ScriptDeleteResult> DeleteScriptAsync(string scriptId, CancellationToken cancellationToken = default)
    {
        var normalizedScriptId = NormalizeScriptId(scriptId);
        var preflight = (await BuildDeletePreflightsAsync(new[] { normalizedScriptId }, cancellationToken))[0];
        if (!preflight.CanDelete) throw new InvalidOperationException(preflight.Message);

        await Crud.DeleteAsync(normalizedScriptId, cancellationToken);
        InvalidateScriptCache();
        return new ScriptDeleteResult(preflight.ScriptId, preflight.ScriptName);
    }

    // Purpose: Deletes scripts after applying the required guardrails.
    public async Task<ScriptBulkDeleteResult> DeleteScriptsAsync(IEnumerable<string> scriptIds, CancellationToken cancellationToken = default)
    {
        var normalizedIds = NormalizeScriptIds(scriptIds);
        var preflights = await BuildDeletePreflightsAsync(normalizedIds, cancellationToken);
        var items = new List<ScriptBulkDeleteItemResult>(preflights.Count);
        var deletedAny = false;

        foreach (var preflight in preflights)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!preflight.CanDelete)
            {
                items.Add(new ScriptBulkDeleteItemResult(
                    preflight.ScriptId,
                    preflight.ScriptName,
                    false,
                    true,
                    preflight.Message));
                continue;
            }

            try
            {
                await Crud.DeleteAsync(preflight.ScriptId, cancellationToken);
                deletedAny = true;
                items.Add(new ScriptBulkDeleteItemResult(preflight.ScriptId, preflight.ScriptName, true, false, string.Empty));
            }
            catch (MoveItAuthenticationException)
            {
                throw;
            }
            catch (Exception ex) when (ex is MoveItComException or InvalidOperationException or ArgumentException)
            {
                items.Add(new ScriptBulkDeleteItemResult(preflight.ScriptId, preflight.ScriptName, false, false, ex.Message));
            }
        }

        if (deletedAny) InvalidateScriptCache();
        return new ScriptBulkDeleteResult(items);
    }

    // Purpose: Builds delete preflights.
    private async Task<IReadOnlyList<ScriptDeletePreflightResult>> BuildDeletePreflightsAsync(
        IReadOnlyList<string> scriptIds,
        CancellationToken cancellationToken)
    {
        // One live inventory/dependency read is shared by the full selection. This keeps bulk preflight
        // consistent and avoids repeating the same REST calls once per selected Script.
        var customScriptsTask = Crud.GetCustomDefinitionsAsync(cancellationToken);
        var standardScriptsTask = Crud.GetBuiltInDefinitionsAsync(cancellationToken);
        var tasksTask = _restClient.GetTaskScriptReferenceDefinitionsAsync(cancellationToken);
        await Task.WhenAll(customScriptsTask, standardScriptsTask, tasksTask);

        var customRaw = await customScriptsTask;
        var standardRaw = await standardScriptsTask;
        var tasks = await tasksTask;

        var customById = customRaw
            .Select(item => new { Raw = item, Script = ParseCustomScript(item) })
            .Where(item => !string.IsNullOrWhiteSpace(item.Script.Id))
            .ToDictionary(item => item.Script.Id, StringComparer.OrdinalIgnoreCase);

        var standardById = standardRaw
            .Select(ParseStandardScript)
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);

        var results = new List<ScriptDeletePreflightResult>(scriptIds.Count);
        foreach (var scriptId in scriptIds)
        {
            if (standardById.TryGetValue(scriptId, out var builtIn))
            {
                results.Add(new ScriptDeletePreflightResult(
                    false,
                    builtIn.Id,
                    builtIn.Name,
                    Array.Empty<AutomationScriptTaskReference>(),
                    "Built-in MOVEit Scripts are read-only and cannot be deleted from MAUI."));
                continue;
            }

            if (!customById.TryGetValue(scriptId, out var custom) || string.IsNullOrWhiteSpace(custom.Script.Id))
            {
                results.Add(new ScriptDeletePreflightResult(
                    false,
                    scriptId,
                    scriptId,
                    Array.Empty<AutomationScriptTaskReference>(),
                    "The Script could not be found or is not visible to the current user."));
                continue;
            }

            var permissionNode = custom.Raw["xAddEditDelete"];
            if (permissionNode is not null && !ReadBoolean(permissionNode))
            {
                results.Add(new ScriptDeletePreflightResult(
                    false,
                    custom.Script.Id,
                    custom.Script.Name,
                    Array.Empty<AutomationScriptTaskReference>(),
                    "MOVEit does not permit the current user to edit or delete this Script."));
                continue;
            }

            var taskReferences = BuildTaskReferences(tasks, custom.Script.Id);
            if (taskReferences.Count > 0)
            {
                results.Add(new ScriptDeletePreflightResult(
                    false,
                    custom.Script.Id,
                    custom.Script.Name,
                    taskReferences,
                    $"Script '{custom.Script.Name}' is referenced by {taskReferences.Count} visible Task{(taskReferences.Count == 1 ? string.Empty : "s")}. Remove those references before deleting the Script."));
                continue;
            }

            results.Add(new ScriptDeletePreflightResult(true, custom.Script.Id, custom.Script.Name, taskReferences, string.Empty));
        }

        return results;
    }

    // Resolves Script inventory through the shared MAUI cache contract.
    private Task<ScriptCacheEntry> RefreshScriptsCoreAsync(string cacheKey, bool forceRefresh, CancellationToken cancellationToken)
        => MoveItServiceCache.ResolveAsync<ScriptCacheEntry>(
            _memoryCache, cacheKey, _scriptBehaviorOptions.CacheRefreshSeconds.Value, forceRefresh,
            entry => entry.RefreshedUtc,
            async token =>
            {
                var capabilities = MoveItLicensePolicy.GetCapabilities(_sessionService.Get());
                var standardScriptsTask = Crud.GetBuiltInDefinitionsAsync(token);
                Task<IReadOnlyList<JsonObject>>? customScriptsTask = capabilities.CustomScriptsLicensed
                    ? Crud.GetCustomDefinitionsAsync(token)
                    : null;
                if (customScriptsTask is null) await standardScriptsTask;
                else await Task.WhenAll(customScriptsTask, standardScriptsTask);

                IEnumerable<AutomationScript> scripts = (await standardScriptsTask).Select(ParseStandardScript);
                if (customScriptsTask is not null)
                {
                    scripts = (await customScriptsTask).Select(ParseCustomScript).Concat(scripts);
                }

                var normalized = scripts
                    .Where(script => !string.IsNullOrWhiteSpace(script.Id))
                    .OrderBy(script => script.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(script => script.IsBuiltIn ? 0 : 1)
                    .ToList();
                return new ScriptCacheEntry(normalized, DateTimeOffset.UtcNow);
            },
            cancellationToken);

    // Purpose: Parses custom script.
    private static AutomationScript ParseCustomScript(JsonObject raw)
    {
        var languageCode = NormalizeLanguageCodeOrUnknown(MoveItRestClient.GetString(raw, "Lang"));
        var groups = ReadStringArray(raw["Group"]);
        return new AutomationScript
        {
            Id = MoveItRestClient.GetString(raw, "ID").Trim(),
            Name = MoveItRestClient.GetString(raw, "Name").Trim(),
            Description = MoveItRestClient.GetString(raw, "Description").Trim(),
            LanguageCode = languageCode,
            Language = FriendlyLanguage(languageCode),
            Type = "Custom",
            Group = string.Join(", ", groups),
            // Some MOVEit versions/models may omit xAddEditDelete from list results. Omission is not a denial.
            CanEditDelete = raw["xAddEditDelete"] is null || ReadBoolean(raw["xAddEditDelete"]),
            IsBuiltIn = false
        };
    }

    // Purpose: Parses standard script.
    private static AutomationScript ParseStandardScript(JsonObject raw)
    {
        var groups = ReadStringArray(raw["Group"]);
        var languageCode = DetectStandardScriptLanguageCode(raw);
        return new AutomationScript
        {
            Id = MoveItRestClient.GetString(raw, "ID").Trim(),
            Name = MoveItRestClient.GetString(raw, "Name").Trim(),
            Description = MoveItRestClient.GetString(raw, "Description").Trim(),
            LanguageCode = languageCode,
            Language = FriendlyLanguage(languageCode),
            Type = "Built-In",
            Group = string.Join(", ", groups),
            CanEditDelete = false,
            IsBuiltIn = true
        };
    }

    // Purpose: Builds task references.
    private static IReadOnlyList<AutomationScriptTaskReference> BuildTaskReferences(IReadOnlyList<JsonObject> tasks, string scriptId)
    {
        return tasks
            .Where(task => ContainsScriptReference(task, scriptId))
            .Select(task => new AutomationScriptTaskReference(
                MoveItRestClient.GetString(task, "ID").Trim(),
                MoveItRestClient.GetString(task, "Name").Trim()))
            .Where(task => !string.IsNullOrWhiteSpace(task.Id))
            .GroupBy(task => task.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(task => task.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Purpose: Returns whether a Task definition contains the requested Script reference.
    private static bool ContainsScriptReference(JsonNode? node, string scriptId)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj)
            {
                var key = pair.Key.Replace("_", string.Empty, StringComparison.Ordinal).Trim();
                if ((key.Equals("ScriptID", StringComparison.OrdinalIgnoreCase) ||
                     key.Equals("CustomScriptID", StringComparison.OrdinalIgnoreCase) ||
                     (key.EndsWith("ScriptID", StringComparison.OrdinalIgnoreCase) && key.Length <= 32)) &&
                    string.Equals(ReadNodeString(pair.Value).Trim(), scriptId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (ContainsScriptReference(pair.Value, scriptId)) return true;
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
                if (ContainsScriptReference(item, scriptId)) return true;
        }
        return false;
    }

    // Purpose: Reads string array.
    private static IReadOnlyList<string> ReadStringArray(JsonNode? node)
    {
        if (node is not JsonArray array) return Array.Empty<string>();
        return array.Select(ReadNodeString).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToList();
    }

    // Purpose: Reads node string.
    private static string ReadNodeString(JsonNode? node)
    {
        if (node is null) return string.Empty;
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text)) return text ?? string.Empty;
            if (value.TryGetValue<int>(out var intValue)) return intValue.ToString();
            if (value.TryGetValue<long>(out var longValue)) return longValue.ToString();
            if (value.TryGetValue<bool>(out var boolValue)) return boolValue ? "true" : "false";
        }
        return node.ToJsonString();
    }

    // Purpose: Reads boolean.
    private static bool ReadBoolean(JsonNode? node)
    {
        var value = ReadNodeString(node).Trim();
        return value.Equals("1", StringComparison.OrdinalIgnoreCase) || value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    // Purpose: Returns whether cache fresh.

    // Purpose: Invalidates script cache.
    private void InvalidateScriptCache() => _memoryCache.Remove(GetCacheKey());

    // Purpose: Returns cache key.
    private string GetCacheKey() => MoveItServiceCache.UserScopedKey(CachePrefix, _sessionService);

    // Purpose: Normalizes script ids.
    private static IReadOnlyList<string> NormalizeScriptIds(IEnumerable<string>? scriptIds)
    {
        var normalized = (scriptIds ?? Array.Empty<string>())
            .Select(value => (value ?? string.Empty).Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalized.Count == 0) throw new ArgumentException("Select at least one Script.", nameof(scriptIds));
        return normalized;
    }

    // Purpose: Normalizes script ID.
    private static string NormalizeScriptId(string? scriptId)
    {
        var value = (scriptId ?? string.Empty).Trim();
        if (value.Length == 0) throw new ArgumentException("Script ID is required.", nameof(scriptId));
        return value;
    }

    // Purpose: Normalizes language code or unknown.
    private static string NormalizeLanguageCodeOrUnknown(string? language)
    {
        var value = (language ?? string.Empty).Trim();
        if (value.Equals("PS1", StringComparison.OrdinalIgnoreCase) || value.Equals("PowerShell", StringComparison.OrdinalIgnoreCase)) return "PS1";
        if (value.Equals("VBScript", StringComparison.OrdinalIgnoreCase) || value.Equals("VBS", StringComparison.OrdinalIgnoreCase)) return "VBScript";
        return value;
    }

    // Purpose: Detects standard script language code.
    private static string DetectStandardScriptLanguageCode(JsonObject raw)
    {
        // Standard Script's published REST model does not document Lang, while the native Web Admin
        // distinguishes built-in PowerShell and VBScript entries. Prefer any language/type hint the
        // installed server supplies, including fields used by newer/older Web Admin builds.
        foreach (var key in new[] { "Lang", "Language", "ScriptLanguage", "ScriptType", "Type" })
        {
            var detected = NormalizeLanguageHint(GetStringCaseInsensitive(raw, key));
            if (!string.IsNullOrWhiteSpace(detected)) return detected;
        }

        // Some builds expose the engine only in descriptive fields. Restrict the heuristic to explicit
        // language tokens so MAUI never guesses merely from built-in/custom status.
        var descriptiveText = string.Join(" ", new[]
        {
            GetStringCaseInsensitive(raw, "Name"),
            GetStringCaseInsensitive(raw, "Description"),
            GetStringCaseInsensitive(raw, "ProcessDesc"),
            GetStringCaseInsensitive(raw, "ID")
        });

        var fromDescription = NormalizeLanguageHint(descriptiveText);
        if (!string.IsNullOrWhiteSpace(fromDescription)) return fromDescription;

        // MOVEit Automation 2025.x built-in/standard Scripts are VBScript when the REST payload
        // does not expose an explicit language. Keep explicit API hints authoritative so future
        // MOVEit releases can introduce built-in Scripts in other languages without a MAUI change.
        return "VBScript";
    }

    // Purpose: Normalizes language hint.
    private static string NormalizeLanguageHint(string? language)
    {
        var value = (language ?? string.Empty).Trim();
        if (value.Length == 0) return string.Empty;
        if (value.Contains("PowerShell", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("PS1", StringComparison.OrdinalIgnoreCase) ||
            value.Contains(".ps1", StringComparison.OrdinalIgnoreCase))
            return "PS1";
        if (value.Contains("VBScript", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("VBS", StringComparison.OrdinalIgnoreCase) ||
            value.Contains(".vbs", StringComparison.OrdinalIgnoreCase))
            return "VBScript";
        return string.Empty;
    }

    // Purpose: Returns string case insensitive.
    private static string GetStringCaseInsensitive(JsonObject raw, string key)
    {
        foreach (var pair in raw)
        {
            if (pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                return ReadNodeString(pair.Value);
        }
        return string.Empty;
    }

    // Purpose: Returns the friendly language.
    private static string FriendlyLanguage(string languageCode) => languageCode switch
    {
        "PS1" => "PowerShell",
        "VBScript" => "VBScript",
        "Unknown" => "Unknown",
        _ => string.IsNullOrWhiteSpace(languageCode) ? "Unknown" : languageCode
    };

    private sealed record ScriptCacheEntry(IReadOnlyList<AutomationScript> Scripts, DateTimeOffset RefreshedUtc);
}
