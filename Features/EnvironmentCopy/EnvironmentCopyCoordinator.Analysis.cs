using System.Text.Json.Nodes;
using System.Xml.Linq;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Features.EnvironmentCopy;

public sealed partial class EnvironmentCopyCoordinator
{
    public async Task<object> AnalyzeAsync(CopyAnalyzeRequest request, string owner, CancellationToken ct)
    {
        RequireEnabled();
        var selection = EnvironmentCopyObjects.NormalizeSelection(request.Objects);
        var sourceKey = Models.MoveItEnvironmentKey.Normalize(request.Source); var targetKey = Models.MoveItEnvironmentKey.Normalize(request.Destination);
        if (sourceKey == targetKey) throw new ArgumentException("Choose two different MOVEit environments.");
        await using var source = await ConnectAsync(sourceKey, ct); await using var destination = await ConnectAsync(targetKey, ct);
        var sourceObjects = await source.UseAsync(sp => EnvironmentCopyObjects.InventoryAsync(sp, ct));
        var plan = new CopyPlan { Owner = owner, Source = sourceKey, Destination = targetKey, IncludeDependencies = request.IncludeDependencies,
            SourceAccount = source.State.Username, DestinationAccount = destination.State.Username };
        plan.DestinationObjects = await destination.UseAsync(sp => EnvironmentCopyObjects.InventoryAsync(sp, ct));
        plan.Groups = await destination.UseAsync(sp => sp.GetRequiredService<IResourceGroupLookupService>().GetVisibleNamesAsync(ct));
        plan.Folders = await destination.UseAsync(sp => sp.GetRequiredService<IMetadataRepository>().GetFoldersAsync(targetKey, "Task", ct));
        var selected = EnvironmentCopyObjects.SelectRoots(selection, sourceObjects);
        plan.Items.AddRange(await ReadSourceGraphAsync(sourceObjects, selected, obj => source.UseAsync(sp => EnvironmentCopyObjects.ReadAsync(sp, obj, ct)),
            obj => source.UseAsync(sp => EnvironmentCopyObjects.MetadataAsync(sp, sourceKey, obj, ct)), ct));
        foreach (var item in plan.Items.Where(item => item.Source.Type == "Host"))
        {
            try { MoveItFeatureSupport.RequireHost(new JsonObject { [item.Source.Subtype] = item.Definition.DeepClone() }, destination.State.Info); }
            catch (InvalidOperationException ex) { item.Issues.Add(ex.Message); }
        }
        // Fetch one protected package for all Tasks, then only Hosts absent from that package.
        // Existing native export owns passphrase protection and temporary Host-carrier cleanup.
        var taskIds = plan.Items.Where(x => x.Source.Type == "Task" && x.Issues.Count == 0).Select(x => x.Source.Id).ToArray();
        if (taskIds.Length > 0)
        {
            var xml = await source.UseAsync(async sp =>
            {
                var file = await sp.GetRequiredService<IMauiNativePortabilityService>().ExportTasksAsync(taskIds, "passphrase", plan.Passphrase, true, ct);
                return MauiNativePortablePackage.Parse(file.Content, "Task", sp.GetRequiredService<IMauiMetadataPortabilityService>()).NativeXml;
            });
            AttachProtectedObjects(plan, xml);
        }
        var hostIds = plan.Items.Where(x => x.Source.Type == "Host" && x.Source.Id != "0" && x.Issues.Count == 0 && x.PortableXml.Length == 0).Select(x => x.Source.Id).ToArray();
        if (hostIds.Length > 0)
        {
            var xml = await source.UseAsync(async sp =>
            {
                var file = await sp.GetRequiredService<IMauiNativePortabilityService>().ExportHostsAsync(hostIds, "passphrase", plan.Passphrase, true, ct);
                return MauiNativePortablePackage.Parse(file.Content, "Host", sp.GetRequiredService<IMauiMetadataPortabilityService>()).NativeXml;
            });
            AttachProtectedObjects(plan, xml);
        }
        await VerifyPgpIdentitiesAsync(plan, source, destination, true, ct);
        foreach (var item in plan.Items)
            item.Choice = EnvironmentCopyObjects.DefaultChoice(item.Source, plan.DestinationObjects, item.Selected, request.IncludeDependencies);
        Validate(plan); store.Add(plan); return View(plan);
    }
    // Selection preview and Analyze share the same bounded resolver. Preview reads definitions only:
    // no protected exports, carrier Tasks, metadata reads, destination writes or stored copy plan.
    public async Task<object> SelectionAsync(CopySelectionRequest request, CancellationToken ct)
    {
        RequireEnabled();
        var selection = EnvironmentCopyObjects.NormalizeSelection(request.Objects);
        await using var source = await ConnectAsync(request.Source, ct);
        var inventory = await source.UseAsync(sp => EnvironmentCopyObjects.InventoryAsync(sp, ct));
        var selected = EnvironmentCopyObjects.SelectRoots(selection, inventory);
        var graph = await ReadSourceGraphAsync(inventory, selected, obj => source.UseAsync(sp => EnvironmentCopyObjects.ReadAsync(sp, obj, ct)), null, ct);
        return new { dependencies = graph.Where(x => !x.Selected).Select(x => x.Source.Key).ToArray() };
    }
    internal static async Task<List<CopyPlanItem>> ReadSourceGraphAsync(IReadOnlyList<CopyObject> sourceObjects, CopySelection[] selected,
        Func<CopyObject, Task<CopyDefinition>> readDefinition, Func<CopyObject, Task<PortableMetadataPackage>>? readMetadata, CancellationToken ct)
    {
        var items = new List<CopyPlanItem>();
        var pending = new Queue<CopySelection>(selected); var selectedKeys = selected.Select(x => x.Type + "|" + x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (pending.TryDequeue(out var next))
        {
            ct.ThrowIfCancellationRequested(); var key = next.Type + "|" + next.Id; if (!seen.Add(key)) continue;
            if (seen.Count > 1000) throw new InvalidOperationException("The dependency graph exceeds 1,000 objects. Copy a smaller selection.");
            var obj = sourceObjects.SingleOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            var item = new CopyPlanItem(obj ?? new(next.Type, next.Id, "Missing " + next.Type + " " + next.Id, "Unknown"), selectedKeys.Contains(key));
            items.Add(item);
            if (obj is null) { item.Issues.Add("A required dependency is missing from the source. Repair the source reference, then analyze again."); continue; }
            var definition = await readDefinition(obj);
            item.Source = EnvironmentCopyObjects.WithDefinitionAssignments(obj, definition.Json);
            item.Definition = definition.Json; item.NativeXml = definition.Xml; item.Revision = definition.Revision;
            if (readMetadata is not null) item.Metadata = await readMetadata(obj);
            if (obj.Type == "Host" && obj.Subtype.StartsWith("AS", StringComparison.OrdinalIgnoreCase)) item.Issues.Add("AS1/AS2/AS3 portability is outside the supported scope. Reuse a compatible destination Host.");
            if (obj.Type is "Task" or "Host")
                foreach (var reference in EnvironmentCopyObjects.References(item, sourceObjects))
                {
                    item.Dependencies.Add(reference.Type + "|" + reference.Id); pending.Enqueue(reference);
                }
        }
        return items;
    }
    private static void AttachProtectedObjects(CopyPlan plan, string xml)
    {
        var document = XDocument.Parse(xml);
        foreach (var item in plan.Items.Where(x => x.Source.Type is "Task" or "Host" or "KeyCertificate"))
        {
            var type = NativeType(item.Source); var id = NativeId(item.Source);
            var container = document.Root?.Element(Container(type));
            if (container?.Elements().Any(x => (type == "Host" || x.Name.LocalName == type) && (string?)x.Attribute("ID") == id) == true)
            {
                item.PortableXml = MauiNativePortabilityService.IsolateCopyObject(xml, type, id);
                if (item.Source.Type == "KeyCertificate") item.NativeXml = container.Elements().Single(x => x.Name.LocalName == type && (string?)x.Attribute("ID") == id).ToString(SaveOptions.DisableFormatting);
            }
        }
    }
    internal static string NativeType(CopyObject obj) => obj.Type != "KeyCertificate" ? obj.Type : obj.Subtype switch
    { "Ssh" => "SSHClientKey", "Tls" => "SSLClientCert", "Pgp" => "PGPKey", _ => throw new ArgumentException("Unknown key kind.") };
    internal static string NativeId(CopyObject obj) => obj.Type == "KeyCertificate" ? obj.Id[(obj.Id.IndexOf('|') + 1)..] : obj.Id;
    internal static string Container(string type) => type switch { "Task" => "Tasks", "Host" => "Hosts", "SSHClientKey" => "SSHClientKeys", "SSLClientCert" => "SSLClientCerts", "PGPKey" => "PGPKeys", _ => throw new ArgumentException("Unknown native copy type.") };

    public async Task<object> ResolveAsync(CopyResolveRequest request, string owner, CancellationToken ct)
    {
        RequireEnabled(); var plan = store.Get(request.PlanId, owner); await plan.Gate.WaitAsync(ct);
        try
        {
            if (plan.Started) throw new InvalidOperationException("This copy operation has already started.");
            if (request.Choices is null || request.Choices.Any(x => x is null || x.Key is null || x.Action is null || x.Name is null || x.TargetId is null)
                || request.Choices.Count != plan.Items.Count || request.Choices.Select(x => x.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plan.Items.Count)
                throw new ArgumentException("Every planned object requires exactly one resolution.");
            var names = plan.DestinationObjects.GroupBy(x => x.Type).ToDictionary(g => g.Key, g => g.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase));
            // Stage the complete request before changing the plan. A rejected later row must not
            // alter earlier choices while leaving a previously issued Review token usable.
            var resolved = new List<(CopyPlanItem Item, CopyChoice Choice)>();
            foreach (var choice in request.Choices)
            {
                var item = plan.Items.SingleOrDefault(x => x.Source.Key == choice.Key) ?? throw new ArgumentException("A resolution does not belong to this plan.");
                var action = choice.Action.ToLowerInvariant(); if (action is not ("" or "create" or "reuse" or "overwrite")) throw new ArgumentException("Choose a supported destination action.");
                if (item.Source.ReuseOnly && (action is "create" or "overwrite")) throw new InvalidOperationException("Built-in Scripts and the Local File System Host can only be reused.");
                var name = choice.Name;
                if (action == "create" && item.Source.Type != "KeyCertificate")
                {
                    if (!names.TryGetValue(item.Source.Type, out var occupied)) names[item.Source.Type] = occupied = new(StringComparer.OrdinalIgnoreCase);
                    name = EnvironmentCopyRules.NewName(string.IsNullOrWhiteSpace(choice.Name) ? item.Source.Name : choice.Name, item.Source.Type, request.Prefix, request.Suffix, request.NameScope, request.UniqueNames, occupied);
                    occupied.Add(name);
                }
                var target = plan.DestinationObjects.SingleOrDefault(x => x.Type == item.Source.Type && x.Id == choice.TargetId);
                resolved.Add((item, new(choice.Key, action, (action is "reuse" or "overwrite") && target is not null ? target.Name : name, action == "create" ? "" : choice.TargetId)));
            }
            plan.ReviewToken = ""; plan.Review = null;
            foreach (var (item, choice) in resolved)
            {
                if (item.Choice.Action != choice.Action || item.Choice.TargetId != choice.TargetId) item.ImpactRevision = "";
                item.Choice = choice;
            }
            Validate(plan); return View(plan);
        }
        finally { plan.Gate.Release(); }
    }
    internal static void Validate(CopyPlan plan)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in plan.Items)
        {
            item.Validation.Clear(); item.Validation.AddRange(item.Choice.Action == "reuse" ? [] : item.Issues);
            var src = item.Source; var choice = item.Choice;
            if (choice.Action == "") { item.Validation.Add("Choose Create New, Reuse Existing or Overwrite."); continue; }
            if (choice.Action is "reuse" or "overwrite")
            {
                var target = plan.DestinationObjects.SingleOrDefault(x => x.Type == src.Type && x.Id == choice.TargetId);
                if (target is null || !EnvironmentCopyObjects.Compatible(src, target)) item.Validation.Add("Select a compatible destination object. Key identity requires a matching verified fingerprint.");
                if (choice.Action == "overwrite")
                {
                    if (src.Type == "KeyCertificate" || src.ReuseOnly || target?.BuiltIn == true) item.Validation.Add("This object can only be reused; overwriting is unavailable.");
                    if (target?.Running == true) item.Validation.Add("The destination Task is running.");
                    if (item.ImpactRevision.Length == 0) item.Validation.Add("Review differences and overwrite impact before continuing.");
                    if (!targets.Add(src.Type + "|" + choice.TargetId)) item.Validation.Add("More than one source object would overwrite the same destination object.");
                }
                continue;
            }
            if (src.ReuseOnly) item.Validation.Add("Built-in resources and the default Local File System Host must map to a compatible destination object.");
            if (!plan.IncludeDependencies && !item.Selected) item.Validation.Add("Include dependencies is off. Select Reuse Existing for this dependency.");
            if (src.Type == "KeyCertificate" && src.Subtype == "Ssh" && item.PortableXml.Length == 0)
                item.Validation.Add("Standalone SSH private-key export has no verified API contract. Reuse a matching destination key, or include it through a Task/Host native export.");
            if (src.Type == "KeyCertificate" && src.Subtype == "Pgp" && item.PortableXml.Length == 0
                && (item.Definition["PublicPrivate"]?.ToString().ToLowerInvariant() is "pair" or "private"))
                item.Validation.Add("Standalone private PGP credential transfer is not verified. Reuse the destination key or include it through a Task/Host native export.");
            if (src.Type == "KeyCertificate" && choice.Name != src.Name) item.Validation.Add("Key/certificate names identify key material and cannot be renamed by this copy path.");
            try { _ = EnvironmentCopyRules.Name(choice.Name); } catch (ArgumentException ex) { item.Validation.Add(ex.Message); }
            if (plan.DestinationObjects.Any(x => x.Type == src.Type && x.Name.Equals(choice.Name, StringComparison.OrdinalIgnoreCase))) item.Validation.Add("The destination name already exists. Reuse, overwrite or enter a unique name.");
            if (!names.Add(src.Type + "|" + choice.Name)) item.Validation.Add("This destination name is used by another row in the plan.");
            if (src.Type == "KeyCertificate" && item.PortableXml.Length > 0 && plan.DestinationObjects.Any(x => x.Type == src.Type && x.Id == src.Id))
                item.Validation.Add("This native key ID already identifies a destination key. Map a matching fingerprint or import the key separately.");
        }
        try { _ = ExecutionOrder(plan); }
        catch (InvalidOperationException ex) { foreach (var item in plan.Items.Where(x => x.Choice.Action != "reuse")) item.Validation.Add(ex.Message); }
    }
    internal static IReadOnlyList<string> ExecutionOrder(CopyPlan plan) => EnvironmentCopyRules.Order(plan.Items.ToDictionary(x => x.Source.Key,
        x => (IReadOnlyList<string>)(x.Choice.Action == "reuse" ? [] : x.Dependencies), StringComparer.OrdinalIgnoreCase));
}
