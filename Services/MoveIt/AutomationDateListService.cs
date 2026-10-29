// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.MoveIt.Resources.DateLists;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.MoveIt;

public interface IAutomationDateListService
{
    // Returns any existing Date List cache immediately for initial navigation and indicates whether it should be reconciled in the background.
    Task<InitialCacheSnapshot<DateListCacheSnapshot>> GetInitialSnapshotAsync(CancellationToken cancellationToken = default);
    // Purpose: Returns the current Date List inventory and refresh timestamp.
    Task<DateListCacheSnapshot> GetSnapshotAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
    // Purpose: Returns one Date List by persistent ID.
    Task<AutomationDateList> GetAsync(string id, CancellationToken cancellationToken = default);
    // Purpose: Returns assigned tasks.
    Task<IReadOnlyList<AutomationDateListTaskReference>> GetAssignedTasksAsync(string id, CancellationToken cancellationToken = default);
    // Purpose: Creates a Date List after validating its name and description.
    Task<MoveItCreatedDateList> CreateAsync(string name, string description, CancellationToken cancellationToken = default);
    // Purpose: Updates basic details while preserving the applicable guardrails.
    Task UpdateBasicDetailsAsync(string id, string name, string description, CancellationToken cancellationToken = default);
    // Purpose: Returns configuration snapshot.
    Task<DateListConfigurationSnapshot> GetConfigurationSnapshotAsync(string id, CancellationToken cancellationToken = default);
    // Purpose: Restores configuration from a previously captured configuration.
    Task<DateListConfigurationSnapshot> RestoreConfigurationAsync(string id, string xml, CancellationToken cancellationToken = default);
    // Purpose: Validates delete and returns the information required before execution.
    Task<IReadOnlyList<DateListDeletePreflightResult>> PreflightDeleteAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default);
    // Purpose: Deletes the requested Date Lists after dependency preflight checks.
    Task<DateListBulkDeleteResult> DeleteAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default);
}

public sealed record DateListCacheSnapshot(IReadOnlyList<AutomationDateList> DateLists, DateTimeOffset RefreshedUtc);
public sealed record DateListConfigurationSnapshot(string Id, string Name, string Xml);
public sealed record DateListDeletePreflightResult(bool CanDelete, string DateListId, string DateListName, IReadOnlyList<AutomationDateListTaskReference> TaskReferences, string Message);
public sealed record DateListBulkDeleteItemResult(string Id, string Name, bool Success, bool Skipped, string Message);
public sealed record DateListBulkDeleteResult(IReadOnlyList<DateListBulkDeleteItemResult> Items);

public sealed class DateListNameAlreadyExistsException : InvalidOperationException
{
    // Purpose: Initializes the duplicate-name exception for a Date List.
    public DateListNameAlreadyExistsException(string name) : base($"A Date List named '{name}' already exists. Enter a unique Date List Name.") { }
}

public sealed class AutomationDateListService : IAutomationDateListService
{
    private const string CachePrefix = "MoveItAutomationUI:DateLists:";
    private readonly IMoveItRestClient _rest;
    private readonly IDateListCrudCoordinator _crud;
    private readonly IMoveItSessionService _session;
    private readonly IMemoryCache _cache;
    private readonly DateListBehaviorOptions _options;

    // Purpose: Initializes automation date list service with its required dependencies.
    public AutomationDateListService(IMoveItRestClient rest, IMoveItComClient com, IMoveItSessionService session, IMemoryCache cache, IOptionsSnapshot<DateListBehaviorOptions> options, IDateListCrudCoordinator? crud = null)
    { _rest = rest; _crud = crud ?? new DateListCrudCoordinator(rest, session, cache, com); _session = session; _cache = cache; _options = options.Value; }

    // Returns the initial Date List snapshot through the shared cached-inventory navigation contract.
    public async Task<InitialCacheSnapshot<DateListCacheSnapshot>> GetInitialSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var key = CacheKey();
        return await MoveItServiceCache.ResolveInitialAsync<DateListCacheEntry, DateListCacheSnapshot>(
            _cache,
            key,
            _options.CacheRefreshSeconds.Value,
            entry => entry.RefreshedUtc,
            entry => new DateListCacheSnapshot(entry.Items, entry.RefreshedUtc),
            token => GetSnapshotAsync(false, token),
            cancellationToken);
    }

    // Purpose: Returns the current Date List inventory and refresh timestamp.
    public async Task<DateListCacheSnapshot> GetSnapshotAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        var key = CacheKey();
        var entry = await MoveItServiceCache.ResolveAsync<DateListCacheEntry>(
            _cache, key, _options.CacheRefreshSeconds.Value, forceRefresh,
            value => value.RefreshedUtc,
            async token =>
            {
                var raw = await _crud.GetDefinitionsAsync(token);
                var items = raw.Select(Parse).Where(x => x.Id.Length > 0).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
                return new DateListCacheEntry(items, DateTimeOffset.UtcNow);
            },
            cancellationToken);
        return new(entry.Items, entry.RefreshedUtc);
    }

    // Purpose: Returns one Date List by persistent ID.
    public async Task<AutomationDateList> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        // Details intentionally use the full single-resource response so Entries are available for the Information modal.
        var raw = await _crud.ReadRawAsync(NormalizeId(id), cancellationToken, false);
        return Parse(raw);
    }

    // Purpose: Returns assigned tasks.
    public async Task<IReadOnlyList<AutomationDateListTaskReference>> GetAssignedTasksAsync(string id, CancellationToken cancellationToken = default)
    {
        var key = NormalizeId(id);
        var tasks = await _rest.GetTaskDateListReferenceDefinitionsAsync(cancellationToken);
        return tasks.Where(t => ContainsReference(t, key)).Select(t => new AutomationDateListTaskReference(MoveItRestClient.GetString(t,"ID").Trim(), MoveItRestClient.GetString(t,"Name").Trim()))
            .Where(x => x.Id.Length > 0).GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Purpose: Creates a Date List after validating its name and description.
    public async Task<MoveItCreatedDateList> CreateAsync(string name, string description, CancellationToken cancellationToken = default)
    {
        var saved = await _crud.CreateAsync(new(name, description), cancellationToken);
        return new(saved.Id, saved.Name, saved.Description);
    }

    // Information and the full editor use the same checked-out v0 patch.
    public async Task UpdateBasicDetailsAsync(string id, string name, string description, CancellationToken cancellationToken = default)
        => _ = await _crud.SaveAsync(id, new(name, description), informationOnly: true, ct: cancellationToken);

    public async Task<DateListConfigurationSnapshot> GetConfigurationSnapshotAsync(string id, CancellationToken cancellationToken = default)
    { var x = await _crud.GetNativeConfigurationAsync(id, cancellationToken); return new(x.Id, x.Name, x.Xml); }
    public async Task<DateListConfigurationSnapshot> RestoreConfigurationAsync(string id, string xml, CancellationToken cancellationToken = default)
    { var x = await _crud.RestoreNativeConfigurationAsync(id, xml, cancellationToken); return new(x.Id, x.Name, x.Xml); }

    // Purpose: Validates delete and returns the information required before execution.
    public async Task<IReadOnlyList<DateListDeletePreflightResult>> PreflightDeleteAsync(IEnumerable<string> ids,CancellationToken cancellationToken=default)
    {
        var list=NormalizeIds(ids); var inventory=(await GetSnapshotAsync(true,cancellationToken)).DateLists; var tasks=await _rest.GetTaskDateListReferenceDefinitionsAsync(cancellationToken);
        var results=new List<DateListDeletePreflightResult>();
        foreach(var id in list)
        {
            var item=inventory.FirstOrDefault(x=>x.Id.Equals(id,StringComparison.OrdinalIgnoreCase));
            if(item is null){results.Add(new(false,id,id,Array.Empty<AutomationDateListTaskReference>(),"The Date List is no longer visible or no longer exists."));continue;}
            if(!item.CanEditDelete){results.Add(new(false,id,item.Name,Array.Empty<AutomationDateListTaskReference>(),$"MOVEit reports that the current user cannot delete Date List '{item.Name}'."));continue;}
            var refs=tasks.Where(t=>ContainsReference(t,id)).Select(t=>new AutomationDateListTaskReference(MoveItRestClient.GetString(t,"ID").Trim(),MoveItRestClient.GetString(t,"Name").Trim())).Where(x=>x.Id.Length>0).GroupBy(x=>x.Id,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToList();
            if(refs.Count>0){results.Add(new(false,id,item.Name,refs,$"Date List '{item.Name}' is referenced by {refs.Count} visible Task{(refs.Count==1?"":"s")}. Remove those references before deleting it."));continue;}
            results.Add(new(true,id,item.Name,refs,string.Empty));
        }
        return results;
    }

    // Purpose: Deletes the requested Date Lists after dependency preflight checks.
    public async Task<DateListBulkDeleteResult> DeleteAsync(IEnumerable<string> ids,CancellationToken cancellationToken=default)
    {
        var pre=await PreflightDeleteAsync(ids,cancellationToken); var outp=new List<DateListBulkDeleteItemResult>(); bool any=false;
        foreach(var p in pre)
        {
            if(!p.CanDelete){outp.Add(new(p.DateListId,p.DateListName,false,true,p.Message));continue;}
            try{await _crud.DeleteAsync(p.DateListId,cancellationToken);any=true;outp.Add(new(p.DateListId,p.DateListName,true,false,string.Empty));}
            catch(Exception ex) when(ex is MoveItComException or InvalidOperationException or ArgumentException){outp.Add(new(p.DateListId,p.DateListName,false,false,ex.Message));}
        }
        if(any) Invalidate(); return new(outp);
    }

    // Purpose: Maps a raw MOVEit Date List record to the MAUI model.
    private static AutomationDateList Parse(JsonObject raw) => new(){ Id=MoveItRestClient.GetString(raw,"ID").Trim(), Name=MoveItRestClient.GetString(raw,"Name").Trim(), Description=MoveItRestClient.GetString(raw,"Description").Trim(), Group=ReadGroups(raw["Group"]), CanEditDelete=!raw.ContainsKey("xAddEditDelete") || ReadBool(raw["xAddEditDelete"]), EntryCount=raw["Entries"] is JsonArray a?a.Count:0 };
    // Purpose: Returns whether a task definition contains the requested Date List reference.
    private static bool ContainsReference(JsonNode? node, string id, bool inDateListContext = false)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj)
            {
                var key = property.Key.Replace("_", string.Empty).Trim();
                var nextDateListContext = inDateListContext
                    || key.Equals("DateListRef", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("DateListRefs", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("DateList", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("DateLists", StringComparison.OrdinalIgnoreCase);

                if ((key.Equals("DateListID", StringComparison.OrdinalIgnoreCase)
                        || key.Equals("SchedDateListID", StringComparison.OrdinalIgnoreCase))
                    && Read(property.Value).Trim().Equals(id, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (nextDateListContext
                    && key.Equals("ID", StringComparison.OrdinalIgnoreCase)
                    && Read(property.Value).Trim().Equals(id, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (ContainsReference(property.Value, id, nextDateListContext)) return true;
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
                if (ContainsReference(item, id, inDateListContext)) return true;
        }

        return false;
    }
    // Reads MOVEit Resource Group membership into one display value.
    private static string ReadGroups(JsonNode? node)
        => node is JsonArray array
            ? string.Join(", ", array.Select(Read).Where(value => !string.IsNullOrWhiteSpace(value)))
            : Read(node).Trim();

    // Purpose: Reads a normalized string value from a JSON node.
    private static string Read(JsonNode? n){if(n is JsonValue v){if(v.TryGetValue<string>(out var s))return s??"";if(v.TryGetValue<int>(out var i))return i.ToString();if(v.TryGetValue<long>(out var l))return l.ToString();}return n?.ToJsonString()??"";}
    // Purpose: Reads bool.
    private static bool ReadBool(JsonNode? n){var s=Read(n).Trim();return s.Equals("1")||s.Equals("true",StringComparison.OrdinalIgnoreCase);}
    // Purpose: Builds the Date List cache key for the current MOVEit session.
    private string CacheKey() => MoveItServiceCache.UserScopedKey(CachePrefix, _session);
    // Purpose: Invalidates the Date List cache for the current MOVEit session.
    private void Invalidate()=>_cache.Remove(CacheKey());
    // Purpose: Normalizes ID.
    private static string NormalizeId(string? id){var s=(id??"").Trim();if(s.Length==0)throw new ArgumentException("Date List ID is required.");return s;}
    // Purpose: Normalizes ids.
    private static IReadOnlyList<string> NormalizeIds(IEnumerable<string>? ids){var x=(ids??Array.Empty<string>()).Select(v=>(v??"").Trim()).Where(v=>v.Length>0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();if(x.Count==0)throw new ArgumentException("Select at least one Date List.");return x;}
    private sealed record DateListCacheEntry(IReadOnlyList<AutomationDateList> Items, DateTimeOffset RefreshedUtc);
}
