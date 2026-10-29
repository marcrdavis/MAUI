using System.Text.Json.Nodes;
using System.Xml.Linq;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Features.EnvironmentCopy;

public sealed partial class EnvironmentCopyCoordinator
{
    public async Task<object> ReviewAsync(CopyReviewRequest request, string owner, CancellationToken ct)
    {
        RequireEnabled(); var plan = store.Get(request.PlanId, owner); await plan.Gate.WaitAsync(ct);
        try
        {
            if (plan.Started) throw new InvalidOperationException("This operation already started.");
            plan.ReviewToken = ""; Validate(plan);
            if (plan.Items.Any(x => x.Validation.Count > 0)) throw new InvalidOperationException("Resolve all highlighted rows before reviewing the copy.");
            ValidatePlacementOptions(plan, request, requireGroup: false);
            await using var source = await ConnectAsync(plan.Source, ct); await using var destination = await ConnectAsync(plan.Destination, ct);
            await RevalidateAsync(plan, source, destination, ct);
            plan.Groups = await destination.UseAsync(sp => sp.GetRequiredService<IResourceGroupLookupService>().GetVisibleNamesAsync(ct));
            ValidatePlacementOptions(plan, request);
            _ = MissingGroups(plan, request); // Validate prospective names before issuing a reviewed execution token.
            plan.Folders = await destination.UseAsync(sp => sp.GetRequiredService<IMetadataRepository>().GetFoldersAsync(plan.Destination, "Task", ct));
            ValidateFolder(plan, request);
            foreach (var item in plan.Items)
            {
                item.Skipped.Clear();
                item.MetadataNotes.Clear();
                if (item.Choice.Action == "reuse") continue;
                if (request.GroupMode == "source" && !(item.Choice.Action == "overwrite" && request.PreserveDestinationGroups))
                    foreach (var group in item.Source.AssignedGroups)
                        if (!plan.Groups.Contains(group, StringComparer.OrdinalIgnoreCase)) item.Skipped.Add($"Resource Group '{group}' is absent at the destination; assignment skipped.");
                if (item.Source.Type == "DateList" && request.GroupMode != "none") item.Skipped.Add("Resource Group assignment skipped: MOVEit's Resource Group member types do not include Date Lists.");
                if (request.CopyMetadata && item.Metadata is not null && !(item.Choice.Action == "overwrite" && request.PreserveDestinationMetadata))
                {
                    var summary = await destination.UseAsync(sp => sp.GetRequiredService<IMauiMetadataPortabilityService>().ValidateAsync(plan.Destination, item.Metadata, item.Source.Type, ct));
                    item.MetadataNotes.AddRange(summary.Issues);
                }
                if (item.Choice.Action == "overwrite")
                {
                    var target = plan.DestinationObjects.Single(x => x.Type == item.Source.Type && x.Id == item.Choice.TargetId);
                    var fresh = await destination.UseAsync(sp => EnvironmentCopyObjects.ReadAsync(sp, target, ct));
                    if (item.ImpactRevision != fresh.Revision) throw new InvalidOperationException("An overwrite target changed since differences were reviewed. Review its differences again.");
                    item.DestinationRevision = fresh.Revision; item.DestinationXml = fresh.Xml; item.DestinationGroupsBefore = EnvironmentCopyObjects.WithDefinitionAssignments(target, fresh.Json).AssignedGroups;
                    if (item.Source.Type == "Host" && request.PreserveDestinationCredentials)
                    {
                        var file = await destination.UseAsync(sp => sp.GetRequiredService<IMauiNativePortabilityService>().ExportHostsAsync([target.Id], "passphrase", plan.Passphrase, false, ct));
                        item.DestinationPortableXml = await destination.UseAsync(sp => Task.FromResult(MauiNativePortablePackage.Parse(file.Content, "Host", sp.GetRequiredService<IMauiMetadataPortabilityService>()).NativeXml));
                        item.CredentialStatus = "Retain destination credentials";
                    }
                }
                if (item.Source.Type == "KeyCertificate")
                {
                    if (item.PortableXml.Length > 0) { item.CredentialStatus = "Protected native key material and credential transfer"; continue; }
                    if (item.Source.Subtype == "Ssh") throw new InvalidOperationException("SSH keys require a matching destination key. Standalone SSH export is not supported.");
                    // Direct export/import contracts are verified for TLS and public PGP. Protected private PGP transfer is not inferred.
                    var privatePgp = item.Source.Subtype == "Pgp" && item.Definition["PublicPrivate"]?.ToString().ToLowerInvariant() is "pair" or "private";
                    if (privatePgp) throw new InvalidOperationException("Private PGP key transfer is not verified for this path. Reuse the destination key or import it separately.");
                    var exported = await source.UseAsync(sp => sp.GetRequiredService<IAutomationKeyCertificateService>().ExportAsync(new()
                    {
                        RowId = item.Source.Id, IncludePrivateKey = item.Source.Subtype == "Tls" && request.IncludeTlsPrivateKeys, Password = plan.Passphrase, ConfirmPassword = plan.Passphrase
                    }, ct));
                    item.KeyBytes = exported.Bytes; item.KeyFileName = exported.FileName;
                    item.CredentialStatus = item.Source.Subtype == "Tls" ? request.IncludeTlsPrivateKeys ? "Password-protected TLS private-key transfer" : "Public TLS certificate only" : "Public PGP key transfer";
                    if (item.Source.Subtype == "Tls" && !request.IncludeTlsPrivateKeys && plan.Items.Any(x => x.Choice.Action != "reuse" && x.Dependencies.Contains(item.Source.Key)))
                    {
                        if (!request.AllowIncompleteCredentials) throw new InvalidOperationException("A copied Host or Task requires this TLS client certificate. Include its private key, reuse an existing complete certificate, or explicitly accept manual credential completion.");
                        item.Skipped.Add("Private TLS key omitted by request. Complete client credentials manually before enabling dependent Tasks.");
                    }
                }
            }
            plan.Review = request; plan.ReviewToken = Guid.NewGuid().ToString("N");
            return View(plan);
        }
        finally { plan.Gate.Release(); }
    }
    public async Task<object> PlacementAsync(CopyReviewRequest request, string owner, CancellationToken ct)
    {
        RequireEnabled(); var plan = store.Get(request.PlanId, owner); await plan.Gate.WaitAsync(ct);
        try
        {
            if (plan.Started || plan.Review is null) throw new InvalidOperationException("Review the resolved plan before changing placement.");
            // Preview is a calculation over the owned plan: no connections, exports, checkouts or writes.
            // Actual Review must still revalidate the environment before issuing a new execution token.
            plan.ReviewToken = ""; ValidatePlacementOptions(plan, request);
            return new { groupsToCreate = MissingGroups(plan, request), placement = Placements(plan, request) };
        }
        finally { plan.Gate.Release(); }
    }
    private static void ValidatePlacementOptions(CopyPlan plan, CopyReviewRequest request, bool requireGroup = true)
    {
        if (request.GroupMode is not ("none" or "source" or "create-source" or "one") || request.FolderMode is not ("root" or "existing" or "new"))
            throw new ArgumentException("Choose valid Resource Group and folder modes.");
        if (requireGroup && request.GroupMode == "one" && !plan.Groups.Contains(request.Group, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Choose an existing destination Resource Group.");
    }
    private static async Task RevalidateAsync(CopyPlan plan, CopyEnvironmentConnection source, CopyEnvironmentConnection destination, CancellationToken ct, Action<string, CopyPlanItem?>? progress = null)
    {
        var sourceInventory = await source.UseAsync(sp => EnvironmentCopyObjects.InventoryAsync(sp, ct, type => progress?.Invoke("Reading source " + type, null)));
        var targetInventory = await destination.UseAsync(sp => EnvironmentCopyObjects.InventoryAsync(sp, ct, type => progress?.Invoke("Reading destination " + type, null)));
        plan.DestinationObjects = targetInventory;
        progress?.Invoke("Checking key identities", null);
        await VerifyPgpIdentitiesAsync(plan, source, destination, false, ct);
        targetInventory = plan.DestinationObjects;
        foreach (var item in plan.Items)
        {
            if (item.Revision.Length > 0)
            {
                var current = sourceInventory.SingleOrDefault(x => x.Key == item.Source.Key) ?? throw new InvalidOperationException("A source object disappeared. Analyze the selection again.");
                progress?.Invoke("Validating source object", item);
                var fresh = await source.UseAsync(sp => EnvironmentCopyObjects.ReadAsync(sp, current, ct));
                if (item.Choice.Action != "reuse") RequireUnchangedGroups(item.Source.AssignedGroups, EnvironmentCopyObjects.WithDefinitionAssignments(current, fresh.Json).AssignedGroups,
                    $"Source Resource Groups for '{item.Source.Name}' changed after analysis. Analyze the selection again.");
                if (fresh.Revision != item.Revision) throw new InvalidOperationException($"Source '{item.Source.Name}' changed after analysis. Analyze the selection again.");
            }
            if (item.Choice.Action == "create")
            {
                if (targetInventory.Any(x => x.Type == item.Source.Type && x.Name.Equals(item.Choice.Name, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"Destination name '{item.Choice.Name}' now exists. Analyze and resolve again.");
            }
            else
            {
                var target = targetInventory.SingleOrDefault(x => x.Type == item.Source.Type && x.Id == item.Choice.TargetId);
                if (target is null || !EnvironmentCopyObjects.Compatible(item.Source, target) || target.Name != item.Choice.Name)
                    throw new InvalidOperationException($"The destination mapping for '{item.Source.Name}' changed. Analyze and resolve again.");
                if (item.Choice.Action == "overwrite" && target.Running) throw new InvalidOperationException($"Destination Task '{target.Name}' is running.");
            }
        }
        plan.DestinationObjects = targetInventory;
        var session = destination.State; var createsTasks = plan.Items.Count(x => x.Source.Type == "Task" && x.Choice.Action == "create");
        var createsHosts = plan.Items.Count(x => x.Source.Type == "Host" && x.Choice.Action == "create");
        foreach (var block in new[] { MoveItLicensePolicy.GetTaskCapacityBlock(session, targetInventory.Count(x => x.Type == "Task"), createsTasks),
            MoveItLicensePolicy.GetHostCapacityBlock(session, targetInventory.Count(x => x.Type == "Host"), createsHosts) })
            if (!string.IsNullOrEmpty(block)) throw new InvalidOperationException(block);
        foreach (var item in plan.Items.Where(x => x.Choice.Action != "reuse"))
        {
            var block = item.Source.Type == "Task" ? MoveItLicensePolicy.GetAdvancedTaskCreationBlock(session, item.Source.Subtype)
                : item.Source.Type == "Script" ? MoveItLicensePolicy.GetCustomScriptBlock(session)
                : item.Source.Type == "KeyCertificate" && item.Source.Subtype == "Pgp" ? MoveItLicensePolicy.GetPgpBlock(session) : "";
            if (!string.IsNullOrEmpty(block)) throw new InvalidOperationException(block);
        }
    }
    private static void ValidateFolder(CopyPlan plan, CopyReviewRequest review)
    {
        if (!plan.Items.Any(x => x.Source.Type == "Task" && x.Choice.Action == "create")) return;
        if (review.FolderMode == "existing" && !plan.Folders.Any(x => x.Id == review.FolderId)) throw new ArgumentException("Choose an existing destination Task folder.");
        if (review.FolderMode == "new")
        {
            var segments = FolderSegments(review.FolderPath);
            int? parent = null; var missing = false;
            foreach (var segment in segments)
            {
                var found = plan.Folders.SingleOrDefault(x => x.ParentId == parent && x.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
                if (found is null) { missing = true; break; } parent = found.Id;
            }
            if (missing && !review.CreateFolder) throw new ArgumentException("Enable Create folder if it does not exist, or select an existing destination folder.");
        }
    }
    internal static string[] FolderSegments(string path)
    {
        var segments = (path ?? "").Trim('/').Split('/', StringSplitOptions.TrimEntries);
        if (segments.Length is 0 or > 20 || segments.Any(x => x.Length is 0 or > 255 || x is "." or ".." || x.Any(char.IsControl) || x.Contains('\\')))
            throw new ArgumentException("Enter a valid destination folder path using / between names.");
        return segments;
    }
    internal static string[] MissingGroups(CopyPlan plan, CopyReviewRequest review)
    {
        if (review.GroupMode != "create-source") return [];
        var names = plan.Items.Where(item => item.Choice.Action != "reuse" && item.Source.Type != "DateList"
            && !(item.Choice.Action == "overwrite" && review.PreserveDestinationGroups)).SelectMany(item => item.Source.AssignedGroups);
        return ResourceGroupManagementService.NormalizeGroupNames(names).Where(name => !plan.Groups.Contains(name, StringComparer.OrdinalIgnoreCase)).ToArray();
    }
    internal static void RequireUnchangedGroups(IReadOnlyList<string> expected, IReadOnlyList<string> current, string message)
    {
        if (!expected.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(current)) throw new InvalidOperationException(message);
    }
    public async Task<object> DifferencesAsync(string planId, string key, string targetId, string owner, CancellationToken ct)
    {
        RequireEnabled(); var plan = store.Get(planId, owner); await plan.Gate.WaitAsync(ct);
        try
        {
            if (plan.Started) throw new InvalidOperationException("This copy operation has already started.");
            var item = plan.Items.SingleOrDefault(x => x.Source.Key == key) ?? throw new ArgumentException("Choose a planned object.");
            var target = plan.DestinationObjects.SingleOrDefault(x => x.Type == item.Source.Type && x.Id == targetId) ?? throw new ArgumentException("Select a destination object to compare.");
            await using var destination = await ConnectAsync(plan.Destination, ct);
            var fresh = await destination.UseAsync(sp => EnvironmentCopyObjects.ReadAsync(sp, target, ct));
            if (target.Type == "KeyCertificate") fresh.Json["Fingerprint"] = target.Fingerprint;
            var sourceComparison = item.Definition.DeepClone().AsObject(); var destinationComparison = fresh.Json.DeepClone().AsObject();
            if (item.Metadata is not null)
            {
                var targetMetadata = await destination.UseAsync(sp => EnvironmentCopyObjects.MetadataAsync(sp, plan.Destination, target, ct));
                static JsonObject Fields(PortableMetadataPackage metadata) => new(metadata.Fields.Select(field => new KeyValuePair<string,JsonNode?>(field.Name, JsonValue.Create(field.Value))));
                sourceComparison["MAUI Metadata"] = Fields(item.Metadata); destinationComparison["MAUI Metadata"] = Fields(targetMetadata);
            }
            var a = EnvironmentCopyRules.RedactedProperties(sourceComparison); var b = EnvironmentCopyRules.RedactedProperties(destinationComparison);
            var values = a.Keys.Union(b.Keys).Order(StringComparer.Ordinal).Select(property =>
            {
                a.TryGetValue(property, out var left); b.TryGetValue(property, out var right);
                return new CopyDifference(property, left ?? "—", right ?? "—", left?.Contains("Not compared (protected value)") == true || right?.Contains("Not compared (protected value)") == true
                    ? "Not compared" : left is null ? "Destination only" : right is null ? "Source only" : left == right ? "Same" : "Changed");
            }).ToArray();
            var impact = await destination.UseAsync(async sp => target.Type switch
            {
                "Host" => (await sp.GetRequiredService<IAutomationHostService>().GetAssignedTasksAsync(target.Id, ct)).Select(x => x.Name).ToArray(),
                "Script" => (await sp.GetRequiredService<IAutomationScriptService>().GetAssignedTasksAsync(target.Id, ct)).Select(x => x.Name).ToArray(),
                "DateList" => (await sp.GetRequiredService<IAutomationDateListService>().GetAssignedTasksAsync(target.Id, ct)).Select(x => x.Name).ToArray(),
                "KeyCertificate" => (await sp.GetRequiredService<IAutomationKeyCertificateService>().GetDependenciesAsync(target.Id, ct)).Tasks.Select(x => x.Name).ToArray(),
                _ => null
            });
            if (item.Choice.Action == "overwrite" && item.Choice.TargetId == targetId) { item.ImpactRevision = fresh.Revision; Validate(plan); }
            return new { source = plan.Source, destination = plan.Destination, sourceId = item.Source.Id, destinationId = target.Id, name = item.Source.Name, type = item.Source.Type, values, impact,
                impactMessage = impact is null ? "Usage impact is unknown for this object type." : $"Used by {impact.Length} destination Task(s).", plan = View(plan) };
        }
        finally { plan.Gate.Release(); }
    }
}
