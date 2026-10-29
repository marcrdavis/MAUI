using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;

namespace MoveItAutomationUI.Services.MoveIt;

public sealed record MoveItObjectAccess(bool CanEdit, bool CanDelete, bool CanRun);
public sealed record MoveItCreationAccess(bool IsAdmin, IReadOnlyDictionary<string, string[]> Groups)
{
    public bool CanCreate(string type) => IsAdmin || Groups.GetValueOrDefault(type)?.Length > 0;
}

public interface IMoveItObjectAccessService
{
    Task<MoveItCreationAccess> GetCreationAsync(bool fresh = false, CancellationToken ct = default);
    Task<string> RequireCreationAsync(string type, string? group, CancellationToken ct = default);
    Task<MoveItObjectAccess> GetObjectAsync(string type, string id, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, MoveItObjectAccess>> GetObjectsAsync(string type, IEnumerable<string> ids, CancellationToken ct = default);
    Task RequireAsync(string type, string id, string operation, CancellationToken ct = default);
}

// MOVEit computes effective grants for the authenticated Windows principal. Do not
// infer membership from the admin-only ACL inventory or grant write access from visibility.
public sealed class MoveItObjectAccessService(IMoveItRestClient rest, IMoveItSessionService sessions, IMemoryCache cache)
    : IMoveItObjectAccessService
{
    internal static readonly IReadOnlyDictionary<string, string> CreationFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Task"] = "xAddTask", ["Host"] = "xAddHost", ["Script"] = "xAddScript",
        ["TLS"] = "xAddSSLCert", ["SSH"] = "xAddSSHClientKey", ["PGP"] = "xAddPGPKey"
    };
    private Task<IReadOnlyList<JsonObject>>? _groups;
    // Only the read-only bulk endpoint reuses inventories. A create followed by
    // restore in the same request must read the newly created object, not an old list.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<JsonObject>>>> _inventories = new();
    private bool _readingBatch;
    private Task<IReadOnlyList<JsonObject>> InventoryAsync(string type, Func<CancellationToken, Task<IReadOnlyList<JsonObject>>> read, CancellationToken ct)
        => _readingBatch ? _inventories.GetOrAdd(type, _ => new(() => read(ct))).Value : read(ct);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<MoveItObjectAccess>> _objects = new(StringComparer.OrdinalIgnoreCase);
    private MoveItSessionState Session => sessions.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
    private async Task<IReadOnlyList<JsonObject>> GroupsAsync(bool fresh, CancellationToken ct)
    {
        if (_groups is not null) return await _groups;
        var key = MoveItServiceCache.UserScopedKey("object-creation-access:", sessions);
        if (!fresh && cache.TryGetValue<IReadOnlyList<JsonObject>>(key, out var stored) && stored is not null) return stored;
        _groups = rest.GetResourceGroupDefinitionsAsync(ct);
        var groups = await _groups;
        cache.Set(key, groups.Select(row => row.DeepClone().AsObject()).ToArray(), TimeSpan.FromSeconds(30));
        return groups;
    }
    internal static MoveItCreationAccess Creation(bool admin, IReadOnlyList<JsonObject> groups)
        => new(admin, CreationFields.ToDictionary(pair => pair.Key, pair => groups
            .Where(group => Flag(group, pair.Value) == true).Select(group => Text(group, "Name"))
            .Where(name => name.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray(), StringComparer.OrdinalIgnoreCase));

    public async Task<MoveItCreationAccess> GetCreationAsync(bool fresh = false, CancellationToken ct = default)
        => Session.Info?.IsAdmin == true ? Creation(true, []) : Creation(false, await GroupsAsync(fresh, ct));

    public async Task<string> RequireCreationAsync(string type, string? group, CancellationToken ct = default)
    {
        var access = await GetCreationAsync(true, ct);
        var name = (group ?? "").Trim();
        if (access.IsAdmin) return name;
        if (!access.CanCreate(type)) throw new MoveItObjectAccessException($"Your MOVEit account cannot create {type} objects in any Resource Group.");
        if (name.Length == 0) throw new MoveItObjectAccessException("Select a Resource Group before creating this object.");
        if (!access.Groups.GetValueOrDefault(type, []).Contains(name, StringComparer.OrdinalIgnoreCase))
            throw new MoveItObjectAccessException("The selected Resource Group does not permit creating this object. Reload the available groups.");
        return access.Groups[type].First(value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public Task<MoveItObjectAccess> GetObjectAsync(string type, string id, CancellationToken ct = default)
        => _objects.GetOrAdd(type + "|" + id, _ => ReadObjectAsync(type, id, ct));
    private async Task<MoveItObjectAccess> ReadObjectAsync(string type, string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Object ID is required.");
        if (Session.Info?.IsAdmin == true) return new(true, true, true);
        if (type == "GlobalParameter") return new(false, false, false);
        id = id.Trim();
        if (type.Equals("KeyCertificate", StringComparison.OrdinalIgnoreCase))
        {
            var parts = id.Split('|', 2); if (parts.Length != 2) throw new ArgumentException("Key/certificate identity is required.");
            type = parts[0].ToUpperInvariant(); id = parts[1];
        }
        JsonObject? raw = type switch
        {
            "Task" => await rest.GetTaskAsync(id, ct),
            "Host" => await rest.GetHostAsync(id, ct),
            "Script" => await ReadScriptAsync(id, ct),
            "DateList" => await rest.GetDateListAsync(id, ct),
            "GlobalParameter" => await rest.GetGlobalParameterAsync(id, ct),
            "SSH" => Find(await InventoryAsync("SSH", rest.GetSshKeysAsync, ct), id),
            "TLS" or "SSL" => Find(await InventoryAsync("TLS", rest.GetTlsCertificatesAsync, ct), id),
            "PGP" => Find(await InventoryAsync("PGP", rest.GetPgpKeysAsync, ct), id),
            _ => throw new ArgumentException("Unsupported MOVEit object type.")
        };
        if (raw is null) throw new MoveItObjectAccessException("The object is unavailable for your MOVEit account.");
        var body = raw.FirstOrDefault(pair => pair.Value is JsonObject child && Text(child, "ID").Equals(id, StringComparison.OrdinalIgnoreCase)).Value as JsonObject ?? raw;
        var nativeFull = Flag(body, "xAddEditDelete") ?? Flag(raw, "xAddEditDelete");
        var owned = ObjectCheckout.HasLifetimeFor(type, id);
        var groups = nativeFull is null || owned ? await GroupsAsync(true, ct) : [];
        var memberType = type switch { "TLS" or "SSL" => "ssl", "SSH" => "ssh", "PGP" => "pgp", _ => type.ToLowerInvariant() };
        var field = CreationFields.GetValueOrDefault(type == "SSL" ? "TLS" : type);
        var assigned = field is not null && groups.Any(group => Flag(group, field) == true &&
            (Node(body, "Group") is JsonArray names && names.Any(name => string.Equals(name?.ToString(), Text(group, "Name"), StringComparison.OrdinalIgnoreCase))
             || Node(group, "Members") is JsonArray members && members.OfType<JsonObject>().Any(member => Text(member, "t") == memberType && Text(member, "value") == id)));
        var full = (nativeFull ?? assigned) || owned && assigned;
        // Edit Existing authorizes the entire visible Task definition, including element additions/removals.
        // Add/Edit/Delete is required only for creating/deleting the Task object itself.
        var edit = type == "Task" ? Flag(body, "xAlter") == true || full : full;
        // An owned lease was authorized before native availability flags were cleared by checkout.
        // It only restores edit availability; it never supplies a New/Delete grant.
        edit = ObjectCheckout.CanEditCheckedOut(type, id, edit);
        var run = type == "Task" && (Flag(body, "xRun") ?? (full || edit));
        return new(edit, full, run);
    }
    public async Task<IReadOnlyDictionary<string, MoveItObjectAccess>> GetObjectsAsync(string type, IEnumerable<string> ids, CancellationToken ct = default)
    {
        var result = new Dictionary<string, MoveItObjectAccess>(StringComparer.OrdinalIgnoreCase);
        _readingBatch = true;
        try
        {
            // Bound parallel detail reads so a bulk menu cannot flood MOVEit.
            foreach (var batch in ids.Distinct(StringComparer.OrdinalIgnoreCase).Chunk(6))
            {
                var values = await Task.WhenAll(batch.Select(async id => (Id: id, Rights: await GetObjectAsync(type, id, ct))));
                foreach (var value in values) result[value.Id] = value.Rights;
            }
            return result;
        }
        finally { _readingBatch = false; _inventories.Clear(); }
    }

    public async Task RequireAsync(string type, string id, string operation, CancellationToken ct = default)
    {
        var access = await GetObjectAsync(type, id, ct);
        var allowed = operation switch { "Read" => true, "Delete" => access.CanDelete, "Run" => access.CanRun, "EditRun" => access.CanEdit && access.CanRun, _ => access.CanEdit };
        if (!allowed) throw new MoveItObjectAccessException($"Your MOVEit account does not permit {operation.ToLowerInvariant()} access to this {type}.");
    }
    private async Task<JsonObject?> ReadScriptAsync(string id, CancellationToken ct)
    {
        var all = await InventoryAsync("Script", rest.GetCustomScriptsAsync, ct);
        return Find(all, id) ?? Find(await InventoryAsync("BuiltInScript", rest.GetStandardScriptsAsync, ct), id);
    }
    private static JsonObject? Find(IReadOnlyList<JsonObject> objects, string id)
        => objects.FirstOrDefault(row => new[] { "ID", "Sha1Thumbprint", "SerialNum" }.Any(key => Text(row, key).Equals(id, StringComparison.OrdinalIgnoreCase)));
    internal static JsonNode? Node(JsonObject obj, string key) => obj.FirstOrDefault(pair => pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value;
    internal static string Text(JsonObject obj, string key) => Node(obj, key)?.ToString().Trim() ?? "";
    internal static bool? Flag(JsonObject obj, string key) => Node(obj, key)?.ToString().ToLowerInvariant() switch { "true" or "1" => true, "false" or "0" => false, _ => null };
}

public sealed class MoveItObjectAccessException(string message) : InvalidOperationException(message) { }

public static class MoveItObjectAccessExtensions
{
    // Import/copy must authorize every destination assignment before any native write.
    public static async Task RequireDefinitionCreationAsync(this IMoveItObjectAccessService access, string type, JsonObject definition, CancellationToken ct)
    {
        var node = MoveItObjectAccessService.Node(definition, "Group");
        var groups = node is JsonArray array ? array.Select(value => value?.ToString() ?? "").ToArray()
            : node is JsonValue ? [node.ToString()] : [];
        if (groups.Length == 0) await access.RequireCreationAsync(type, null, ct);
        foreach (var group in groups) await access.RequireCreationAsync(type, group, ct);
    }
    public static async Task RequireXmlCreationAsync(this IMoveItObjectAccessService access, string type, System.Xml.Linq.XElement definition, CancellationToken ct)
    {
        var groups = definition.Elements().Where(element => element.Name.LocalName == "Group").Select(element => element.Value.Trim()).ToArray();
        if (groups.Length == 0) await access.RequireCreationAsync(type, null, ct);
        foreach (var group in groups) await access.RequireCreationAsync(type, group, ct);
    }
}
