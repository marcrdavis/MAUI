using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Hosts;
using MoveItAutomationUI.Services.MoveIt.Resources.Scripts;
using MoveItAutomationUI.Services.MoveIt.Resources.DateLists;
using MoveItAutomationUI.Services.MoveIt.Tasks;

namespace MoveItAutomationUI.Features.EnvironmentCopy;

// Thin adapters over the authoritative object libraries, scoped to a Data Access connection.
internal static class EnvironmentCopyObjects
{
    public static async Task<List<CopyObject>> InventoryAsync(IServiceProvider services, CancellationToken ct, Action<string>? progress = null)
    {
        progress?.Invoke("Tasks");
        var tasks = await services.GetRequiredService<IAutomationTaskService>().GetTaskSnapshotAsync(true, ct);
        progress?.Invoke("Hosts");
        var hosts = await services.GetRequiredService<IAutomationHostService>().GetHostSnapshotAsync(true, ct);
        progress?.Invoke("Scripts");
        var scripts = await services.GetRequiredService<IAutomationScriptService>().GetScriptSnapshotAsync(true, ct);
        progress?.Invoke("Date Lists");
        var dates = await services.GetRequiredService<IAutomationDateListService>().GetSnapshotAsync(true, ct);
        progress?.Invoke("Keys and Certificates");
        var keys = await services.GetRequiredService<IAutomationKeyCertificateService>().GetSnapshotAsync(true, ct);
        progress?.Invoke("Resource Groups");
        var groups = await services.GetRequiredService<IMoveItRestClient>().GetResourceGroupDefinitionsAsync(ct);
        var objects = tasks.Tasks.Select(x => new CopyObject("Task", x.Id, x.Name, x.Type, x.Group, Running: x.Status.Equals("Running", StringComparison.OrdinalIgnoreCase)))
            .Concat(hosts.Hosts.Select(x => new CopyObject("Host", x.Id, x.Name, x.TypeCode, x.Group)))
            .Concat(scripts.Scripts.Select(x => new CopyObject("Script", x.Id, x.Name, x.LanguageCode, x.Group, x.IsBuiltIn)))
            .Concat(dates.DateLists.Select(x => new CopyObject("DateList", x.Id, x.Name, "Date List", x.Group)))
            .Concat(keys.Items.Select(x => new CopyObject("KeyCertificate", x.RowId, x.Name, x.Kind.ToString(), x.Group, Fingerprint: KeyFingerprint(x), PublicPrivate: x.PublicPrivate)))
            .OrderBy(x => x.Type, StringComparer.Ordinal).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return WithAssignments(objects, groups);
    }
    internal static List<CopyObject> WithAssignments(IReadOnlyList<CopyObject> objects, IReadOnlyList<JsonObject> groups)
    {
        var assignments = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            var name = MoveItRestClient.GetString(group, "Name").Trim();
            if (name.Length == 0) throw new InvalidOperationException("MOVEit returned a Resource Group without a Name. Refresh the source list.");
            if (group["Members"] is null) continue;
            if (group["Members"] is not JsonArray members || members.Any(member => member is not JsonObject))
                throw new InvalidOperationException("MOVEit returned invalid Resource Group members. Refresh the source list.");
            foreach (var member in members.Cast<JsonObject>())
            {
                var key = MoveItRestClient.GetString(member, "t") + "|" + MoveItRestClient.GetString(member, "value");
                if (!assignments.TryGetValue(key, out var names)) assignments[key] = names = [];
                names.Add(name);
            }
        }
        return objects.Select(obj =>
        {
            var memberType = obj.Type switch { "Task" => "task", "Host" => "host", "Script" => "script",
                "KeyCertificate" => obj.Subtype switch { "Ssh" => "ssh", "Tls" => "ssl", "Pgp" => "pgp", _ => "" }, _ => "" };
            var id = EnvironmentCopyCoordinator.NativeId(obj);
            var names = memberType.Length > 0 && assignments.TryGetValue(memberType + "|" + id, out var assigned)
                ? assigned.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray() : [];
            return obj with { AssignedGroups = names, Groups = names.Length > 0 || string.IsNullOrWhiteSpace(obj.Groups) ? string.Join(", ", names) : obj.Groups };
        }).ToList();
    }
    internal static CopyObject WithDefinitionAssignments(CopyObject obj, JsonObject definition)
    {
        // Information/editor reads use the object's structured Group array. It takes precedence
        // over inverse inventory projections; an explicitly empty array is also authoritative.
        if (obj.Type is not ("Task" or "Host" or "Script") || !definition.TryGetPropertyValue("Group", out var value)) return obj;
        IEnumerable<JsonNode?> values = value switch
        {
            null => [],
            JsonArray array => array,
            JsonValue scalar when scalar.TryGetValue<string>(out _) => [scalar],
            _ => throw new InvalidOperationException("MOVEit returned invalid object Resource Groups. Reload the source object.")
        };
        var names = values.Select(group => group is null ? "" : group is JsonValue scalar && scalar.TryGetValue<string>(out var name) ? (name ?? "").Trim()
            : throw new InvalidOperationException("MOVEit returned an invalid Resource Group name. Reload the source object."))
            .Where(name => name.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        return obj with { AssignedGroups = names, Groups = string.Join(", ", names) };
    }
    internal static CopyChoice DefaultChoice(CopyObject source, IReadOnlyList<CopyObject> destination, bool selected, bool includeDependencies)
    {
        var compatible = destination.Where(target => Compatible(source, target));
        // Names default to reuse only when one compatible object matches. Key material still requires verified identity.
        var matches = (source.Type == "KeyCertificate" ? compatible : source.Type == "Host" && source.Id == "0"
            ? compatible.Where(target => target.Id == "0") : compatible.Where(target => target.Name.Equals(source.Name, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (matches.Length == 1) return new(source.Key, "reuse", matches[0].Name, matches[0].Id);
        var conflict = matches.Length > 1 || destination.Any(target => target.Type == source.Type && (target.Id == source.Id || target.Name.Equals(source.Name, StringComparison.OrdinalIgnoreCase)));
        return new(source.Key, !source.ReuseOnly && !conflict && (selected || includeDependencies) ? "create" : "", source.Name);
    }
    internal static CopySelection[] NormalizeSelection(IReadOnlyList<CopySelection> selection)
    {
        if (selection is null || selection.Count is 0 or > 500) throw new ArgumentException("Select between 1 and 500 objects.");
        if (selection.Any(item => item is null || string.IsNullOrWhiteSpace(item.Id)))
            throw new ArgumentException("Every selected object requires a type and ID.");
        return selection.Select(x => new CopySelection(EnvironmentCopyRules.Type(x.Type), x.Id.Trim())).Distinct().ToArray();
    }
    internal static CopySelection[] SelectRoots(IReadOnlyList<CopySelection> selection, IReadOnlyList<CopyObject> inventory)
    {
        var roots = NormalizeSelection(selection);
        foreach (var root in roots)
        {
            var source = inventory.SingleOrDefault(x => x.Type == root.Type && x.Id == root.Id)
                ?? throw new InvalidOperationException("A selected source object no longer exists. Refresh the source list.");
            if (source.ReuseOnly) throw new InvalidOperationException("Built-in Scripts and the Local File System Host cannot be selected for copying. They are reused when required by another object.");
        }
        return roots;
    }
    public static async Task<CopyDefinition> ReadAsync(IServiceProvider services, CopyObject item, CancellationToken ct)
    {
        JsonObject json; string xml;
        switch (item.Type)
        {
            case "Task":
                var tasks = services.GetRequiredService<ITaskCrudCoordinator>();
                json = await tasks.GetDefinitionAsync(item.Id, ct); xml = (await tasks.GetNativeConfigurationAsync(item.Id, ct)).Xml; break;
            case "Host" when item.Id != "0":
                var hosts = services.GetRequiredService<IHostCrudCoordinator>();
                json = HostDefinitionJson.UnwrapHost(await hosts.GetDefinitionAsync(item.Id, ct)).Host;
                xml = (await hosts.GetNativeConfigurationAsync(item.Id, ct)).Xml; break;
            case "Host": json = new() { ["ID"] = "0", ["Name"] = item.Name }; xml = "<FileSystem ID=\"0\" />"; break;
            case "Script" when item.BuiltIn:
                json = (await services.GetRequiredService<IScriptCrudCoordinator>().GetBuiltInDefinitionsAsync(ct)).Single(x => MoveItRestClient.GetString(x, "ID") == item.Id);
                xml = new XElement("Script", new XAttribute("ID", item.Id), new XAttribute("Name", item.Name)).ToString(); break;
            case "Script":
                var scripts = services.GetRequiredService<IScriptCrudCoordinator>();
                json = await scripts.ReadRawAsync(item.Id, ct); xml = (await scripts.GetNativeConfigurationAsync(item.Id, ct)).Xml; break;
            case "DateList":
                var dates = services.GetRequiredService<IDateListCrudCoordinator>();
                json = await dates.ReadRawAsync(item.Id, ct); xml = (await dates.GetNativeConfigurationAsync(item.Id, ct)).Xml; break;
            case "KeyCertificate":
                json = JsonSerializer.SerializeToNode(await services.GetRequiredService<IAutomationKeyCertificateService>().GetAsync(item.Id, ct))!.AsObject();
                xml = new XElement("KeyCertificate", new XAttribute("ID", item.Id), new XAttribute("Fingerprint", item.Fingerprint)).ToString(); break;
            default: throw new ArgumentException("Unsupported copy object.");
        }
        // Native XML includes protected values and the complete version-specific configuration.
        var revision = ObjectEditVersion.Create("EnvironmentCopy", item.Key, item.Type == "KeyCertificate" || item.BuiltIn ? json : EnvironmentCopyRules.RevisionXml(xml));
        return new((JsonObject)json.DeepClone(), xml, revision);
    }
    public static async Task<PortableMetadataPackage> MetadataAsync(IServiceProvider services, string environment, CopyObject item, CancellationToken ct)
    {
        var metadata = services.GetRequiredService<IMauiMetadataPortabilityService>();
        var element = await metadata.BuildExportElementAsync(environment, item.Type, item.Id, ct);
        return metadata.ParseFromEnvelope(new XElement("Export", new XAttribute("MetadataSchemaVersion", MauiMetadataPortabilityService.SchemaVersion), element), item.Type);
    }
    public static IReadOnlyList<CopySelection> References(CopyPlanItem item, IReadOnlyList<CopyObject> inventory)
    {
        var refs = EnvironmentCopyRules.References(item.NativeXml).ToList();
        // Use the resource layer's exact key matching rather than interpreting arbitrary macro/script text.
        var xml = XElement.Parse(item.NativeXml);
        foreach (var attribute in xml.DescendantsAndSelf().Attributes())
        {
            string? prefix = attribute.Name.LocalName switch
            {
                "ClientKeyID" or "DefClientKeyID" => "ssh|", "ClientCertSHA1" or "DefClientCertSHA1" => "tls|", _ => null
            };
            if (prefix is null || string.IsNullOrWhiteSpace(attribute.Value) || attribute.Value == "0") continue;
            var key = inventory.Where(x => x.Type == "KeyCertificate" && x.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(x => x.Id[4..].Equals(attribute.Value, StringComparison.OrdinalIgnoreCase)
                    || NormalizeFingerprint(x.Fingerprint) == NormalizeFingerprint(attribute.Value));
            refs.Add(new("KeyCertificate", key?.Id ?? prefix + attribute.Value));
        }
        foreach (var parameter in xml.Descendants("Parameter"))
        {
            var name = (string?)parameter.Attribute("Name") ?? "";
            if (!name.StartsWith("PGP", StringComparison.OrdinalIgnoreCase) || !name.Contains("Key", StringComparison.OrdinalIgnoreCase)) continue;
            var value = ((string?)parameter.Attribute("value") ?? parameter.Value).Trim();
            if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) refs.Add(new("KeyCertificate", "pgp|" + value));
        }
        return refs.Distinct().ToArray();
    }
    public static string KeyFingerprint(AutomationKeyCertificate key) => key.Kind == KeyCertificateKind.Tls ? key.Sha1Thumbprint : key.Fingerprint;
    public static string NormalizeFingerprint(string value) => value.Replace(" ", "").Replace(":", "").ToUpperInvariant();
    public static bool SameKey(CopyObject source, CopyObject target) => source.Type == "KeyCertificate" && target.Type == source.Type && source.Subtype == target.Subtype
        && source.Fingerprint.Length > 0 && NormalizeFingerprint(source.Fingerprint) == NormalizeFingerprint(target.Fingerprint);
    public static bool Compatible(CopyObject source, CopyObject target) => source.Type == target.Type && source.Subtype.Equals(target.Subtype, StringComparison.OrdinalIgnoreCase)
        && (source.Type != "KeyCertificate" || SameKey(source, target))
        && (!(source.PublicPrivate.ToLowerInvariant() is "pair" or "private") || target.PublicPrivate.ToLowerInvariant() is "pair" or "private")
        && (!source.BuiltIn || target.BuiltIn && source.Name.Equals(target.Name, StringComparison.OrdinalIgnoreCase))
        && (source.Type != "Host" || source.Id != "0" || target.Id == "0");
}
