using System.Xml.Linq;
using MoveItAutomationUI.Features.EnvironmentCopy;

namespace MoveItAutomationUI.Services.MoveIt;

public sealed partial class MauiNativePortabilityService
{
    internal sealed record ReuseTarget(string Id, string Name);
    internal sealed record ReusePlan(ImportObjectRef Object, ReuseTarget Target);

    internal static ReuseTarget? ResolveReuseTarget(ImportObjectRef obj, DestinationInventory inventory, ISet<string> selectedKeys)
    {
        // Preserve all Task choices, including referenced Tasks, and never silently reuse an export root.
        if (obj.ObjectType == "Task" || selectedKeys.Contains(MetadataKey(obj.ObjectType, obj.SourceId))) return null;
        if (IsKeyType(obj.ObjectType))
        {
            // Native keys can contain encrypted material rather than a verifiable public fingerprint.
            // Preserve the existing same-native-ID contract; a matching display name alone is insufficient.
            var matches = inventory.Keys.Where(item => item.Kind == KeyKindFromObjectType(obj.ObjectType)
                && item.Id.Equals(obj.SourceId, StringComparison.OrdinalIgnoreCase)).ToArray();
            return matches.Length == 1 && !string.IsNullOrWhiteSpace(matches[0].RowId) ? new(matches[0].RowId, matches[0].Name) : null;
        }

        var subtype = obj.ObjectType switch
        {
            "Host" => obj.NativeSubtype,
            "Script" => AutomationScriptService.NormalizeLanguageCodeOrUnknown(GetString(obj.Node, "Lang")),
            "DateList" => "Date List",
            _ => ""
        };
        if (subtype.Length == 0) return null;
        IEnumerable<CopyObject> candidates = obj.ObjectType switch
        {
            "Host" => inventory.Hosts.Select(item => new CopyObject("Host", item.Id, item.Name, item.TypeCode)),
            "Script" => inventory.Scripts.Select(item => new CopyObject("Script", item.Id, item.Name, item.LanguageCode, BuiltIn: item.IsBuiltIn)),
            "DateList" => inventory.DateLists.Select(item => new CopyObject("DateList", item.Id, item.Name, "Date List")),
            _ => []
        };
        // An unrelated ID collision must not override a unique name match. Ambiguous name matches and
        // incompatible protocols/languages also require the existing explicit conflict choices.
        var targets = candidates.Where(item => item.Id.Equals(obj.SourceId, StringComparison.OrdinalIgnoreCase)
            || item.Name.Equals(obj.SourceName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (targets.Length != 1 || !(obj.ObjectType == "Host" && obj.SourceId == "0")
            && !targets[0].Name.Equals(obj.SourceName, StringComparison.OrdinalIgnoreCase)) return null;
        var target = targets[0];
        var source = new CopyObject(obj.ObjectType, obj.SourceId, obj.SourceName, subtype);
        if (string.IsNullOrWhiteSpace(target.Id) || !EnvironmentCopyObjects.Compatible(source, target)
            || target.BuiltIn && !target.Id.Equals(obj.SourceId, StringComparison.OrdinalIgnoreCase)) return null;
        return new(target.Id, target.Name);
    }

    internal static IReadOnlyList<ReusePlan> BuildReusePlans(
        IReadOnlyList<ImportObjectRef> objects, IReadOnlyList<DuplicateRef> duplicates,
        IReadOnlyDictionary<string, NativePortabilityConflictResolution> resolutions,
        DestinationInventory inventory, ISet<string> selectedKeys)
    {
        var plans = new List<ReusePlan>();
        foreach (var obj in objects)
        {
            if (!resolutions.TryGetValue(MetadataKey(obj.ObjectType, obj.SourceId), out var resolution)
                || NormalizeResolutionAction(resolution.Action) != "useexisting") continue;
            var target = ResolveReuseTarget(obj, inventory, selectedKeys)
                ?? throw new InvalidOperationException($"{FriendlyObjectType(obj.ObjectType, obj.NativeSubtype)} '{obj.SourceName}' cannot be reused with its current destination match. Analyze the package again and choose a supported action.");
            ValidateConflictTargetIdentity(obj.SourceName, resolution.TargetId, target.Id, FindDuplicate(duplicates, obj) is not null, "useexisting");
            plans.Add(new(obj, target));
        }
        return plans;
    }

    internal static void ValidateReusePlans(IReadOnlyList<ReusePlan> plans, DestinationInventory inventory, ISet<string> selectedKeys)
    {
        foreach (var plan in plans)
        {
            var target = ResolveReuseTarget(plan.Object, inventory, selectedKeys);
            if (target is null || !plan.Target.Id.Equals(target.Id, StringComparison.OrdinalIgnoreCase)
                || !plan.Target.Name.Equals(target.Name, StringComparison.Ordinal))
                throw new InvalidOperationException($"The destination {FriendlyObjectType(plan.Object.ObjectType, plan.Object.NativeSubtype)} '{plan.Object.SourceName}' selected for Reuse Existing changed after analysis. Analyze the package again before importing.");
        }
    }

    internal static string PrepareReuseImportXml(string nativeXml, IReadOnlyList<ReusePlan> plans, IReadOnlyList<ImportObjectRef> remaining)
    {
        var document = ParseExportDocument(nativeXml);
        var root = document.Root!;
        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in plans)
        {
            var obj = plan.Object;
            if (obj.ObjectType == "Task") throw new InvalidOperationException("Tasks cannot be reused during native import.");
            // Otherwise MOVEit's own ID remapping for a remaining imported object could redirect a
            // reused reference to that imported object instead of the chosen destination resource.
            if (remaining.Any(item => item.ObjectType == obj.ObjectType
                && MetadataSourceId(item).Equals(plan.Target.Id, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"The Reuse Existing target for '{obj.SourceName}' has an ID also used by another imported object. Resolve that conflict and analyze the package again.");
            var containerName = obj.ObjectType switch
            {
                "Host" => "Hosts", "Script" => "Scripts", "DateList" => "DateLists",
                "SSHClientKey" => "SSHClientKeys", "SSLClientCert" => "SSLClientCerts", "PGPKey" => "PGPKeys",
                _ => throw new InvalidOperationException("Unsupported Reuse Existing dependency type.")
            };
            var container = root.Elements().SingleOrDefault(element => element.Name.LocalName == containerName)
                ?? throw new InvalidOperationException($"The native export is missing its {containerName} collection.");
            var matches = container.Elements().Where(element => (obj.ObjectType == "Host" || element.Name.LocalName == obj.ObjectType)
                && string.Equals(((string?)element.Attribute("ID"))?.Trim(), obj.SourceId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException($"MAUI could not uniquely identify '{obj.SourceName}' in the native export for Reuse Existing.");
            if (obj.ObjectType == "Host" && !matches[0].Name.LocalName.Equals(obj.NativeSubtype, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The reused Host's native type does not match MOVEit's decoded import model.");
            matches[0].Remove();
            mappings[ToMetadataObjectType(obj.ObjectType) + "|" + MetadataSourceId(obj)] = plan.Target.Id;
        }
        EnvironmentCopyRules.RewriteReferences(root, mappings);
        // Preserve the native Host-import empty Tasks contract.
        return SerializeCopyDocument(document);
    }

    internal static void ValidateReuseDecodedObjects(IReadOnlyList<ImportObjectRef> expected, IReadOnlyList<ImportObjectRef> actual)
    {
        var expectedKeys = expected.Select(obj => MetadataKey(obj.ObjectType, obj.SourceId)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (expected.Count != actual.Count || !expectedKeys.SetEquals(actual.Select(obj => MetadataKey(obj.ObjectType, obj.SourceId)))
            || actual.Any(obj => !expected.Any(original => original.ObjectType == obj.ObjectType
                && original.SourceId.Equals(obj.SourceId, StringComparison.OrdinalIgnoreCase)
                && original.SourceName == obj.SourceName && original.NativeSubtype == obj.NativeSubtype)))
            throw new InvalidOperationException("MOVEit's authoritative import model did not preserve the planned dependency exclusions. Analyze the package again before importing.");
    }
}
