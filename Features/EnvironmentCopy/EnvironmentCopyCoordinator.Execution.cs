using System.Text.Json.Nodes;
using System.Xml.Linq;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Resources.Scripts;
using MoveItAutomationUI.Services.MoveIt.Resources.DateLists;

namespace MoveItAutomationUI.Features.EnvironmentCopy;

public sealed partial class EnvironmentCopyCoordinator
{
    public async Task<object> StartAsync(CopyStartRequest request, string owner, CancellationToken ct)
    {
        RequireEnabled(); var plan = store.Get(request.PlanId, owner); await plan.Gate.WaitAsync(ct);
        try
        {
            if (plan.Started) return new { operationId = plan.Id };
            Validate(plan);
            if (plan.Review is null || plan.ReviewToken.Length == 0 || plan.ReviewToken != request.ReviewToken || plan.Items.Any(x => x.Validation.Count > 0))
                throw new InvalidOperationException("Review the current resolved plan before copying.");
            if (plan.Items.Any(x => x.Choice.Action == "overwrite") && !request.ConfirmOverwrite) throw new InvalidOperationException("Confirm the reviewed overwrites before copying.");
            var report = new CopyReport { OperationId = plan.Id, Source = plan.Source, Destination = plan.Destination, InitiatedBy = owner,
                SourceAccount = plan.SourceAccount, DestinationAccount = plan.DestinationAccount, StartedUtc = DateTimeOffset.UtcNow,
                TotalObjects = plan.Items.Count, Phase = "Waiting to start", PhaseStartedUtc = DateTimeOffset.UtcNow,
                Placement = $"Resource Groups: {plan.Review.GroupMode}; new Tasks folder: {plan.Review.FolderMode} {plan.Review.FolderPath}" };
            await store.SaveReportAsync(report, ct); plan.Started = true;
            // The queue owns execution after this point; browser cancellation cannot abandon checkout cleanup.
            await store.Queue.Writer.WriteAsync(plan, CancellationToken.None); return new { operationId = plan.Id };
        }
        finally { plan.Gate.Release(); }
    }
    internal async Task ExecuteAsync(CopyPlan plan, CancellationToken stopping)
    {
        var report = await store.ReadReportAsync(plan.Id, plan.Owner, CancellationToken.None);
        try
        {
            ThrowIfCopyStopped(plan, stopping, Enabled);
            report.Status = "Running"; report.TotalObjects = plan.Items.Count;
            store.UpdateProgress(report, "Connecting to source environment");
            await store.SaveReportAsync(report, CancellationToken.None);
            await using var source = await ConnectAsync(plan.Source, stopping);
            ThrowIfCopyStopped(plan, stopping, Enabled);
            store.UpdateProgress(report, "Connecting to destination environment");
            await using var destination = await ConnectAsync(plan.Destination, stopping);
            foreach (var item in plan.Items.Where(item => item.Source.Type == "Host" && item.Choice.Action is "create" or "overwrite"))
                MoveItFeatureSupport.RequireHost(new JsonObject { [item.Source.Subtype] = item.Definition.DeepClone() }, destination.State.Info);
            // Do not silently substitute credentials changed since the reviewed authentication status.
            if (source.State.Username != plan.SourceAccount || destination.State.Username != plan.DestinationAccount)
                throw new InvalidOperationException("The configured Data Access account changed after review. Analyze and review again.");
            await RevalidateAsync(plan, source, destination, stopping, (phase, item) => { ThrowIfCopyStopped(plan, stopping, Enabled); store.UpdateProgress(report, phase, item); });
            ThrowIfCopyStopped(plan, stopping, Enabled);
            var review = plan.Review!;
            foreach (var item in plan.Items.Where(x => x.Choice.Action == "overwrite"))
            {
                ThrowIfCopyStopped(plan, stopping, Enabled);
                store.UpdateProgress(report, "Checking overwrite target and preparing recovery", item);
                var target = plan.DestinationObjects.Single(x => x.Type == item.Source.Type && x.Id == item.Choice.TargetId);
                var fresh = await destination.UseAsync(sp => EnvironmentCopyObjects.ReadAsync(sp, target, stopping));
                RequireUnchangedGroups(item.DestinationGroupsBefore, EnvironmentCopyObjects.WithDefinitionAssignments(target, fresh.Json).AssignedGroups,
                    "Destination Resource Groups changed after Review. No objects were copied; analyze and review again.");
                if (fresh.Revision != item.DestinationRevision) throw new InvalidOperationException("A destination object changed after Review. No objects were copied; analyze and review again.");
                if (review.RecoveryCopies) await store.CaptureRecoveryAsync(plan, item, stopping);
            }
            ThrowIfCopyStopped(plan, stopping, Enabled);
            store.UpdateProgress(report, "Checking destination Resource Groups");
            plan.Groups = await destination.UseAsync(sp => sp.GetRequiredService<IResourceGroupLookupService>().GetVisibleNamesAsync(stopping));
            if (review.GroupMode == "one" && !plan.Groups.Contains(review.Group, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("The selected destination Resource Group no longer exists.");
            if (review.GroupMode == "create-source")
            {
                report.GroupsPlanned = MissingGroups(plan, review);
                if (report.GroupsPlanned.Count > 0)
                {
                    ThrowIfCopyStopped(plan, stopping, Enabled);
                    store.UpdateProgress(report, "Creating missing Resource Groups");
                    // Journal names before the one dataset PUT; an uncertain result is never automatically replayed.
                    report.GroupCreationStatus = "Not verified"; await store.SaveReportAsync(report, CancellationToken.None);
                    report.GroupsCreated = await destination.UseAsync(sp => sp.GetRequiredService<IResourceGroupManagementService>()
                        .EnsureGroupsAsync(report.GroupsPlanned, CancellationToken.None));
                    report.GroupCreationStatus = "Confirmed"; await store.SaveReportAsync(report, CancellationToken.None);
                    plan.Groups = await destination.UseAsync(sp => sp.GetRequiredService<IResourceGroupLookupService>().GetVisibleNamesAsync(stopping));
                }
                else report.GroupCreationStatus = "Existing groups reused";
            }
            ThrowIfCopyStopped(plan, stopping, Enabled);
            store.UpdateProgress(report, "Preparing destination Task folder");
            var folderId = await destination.UseAsync(sp => ResolveFolderAsync(sp.GetRequiredService<IMetadataRepository>(), plan, stopping,
                () => plan.CancelRequested || stopping.IsCancellationRequested || !Enabled));
            var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in ExecutionOrder(plan))
            {
                var item = plan.Items.Single(x => x.Source.Key == key);
                if (plan.CancelRequested || stopping.IsCancellationRequested || !Enabled)
                {
                    report.Status = "Cancelled"; report.Message = "Stopped before the next object write. Completed destination objects were retained."; break;
                }
                var choice = item.Choice; var destinationId = choice.Action is "reuse" or "overwrite" ? choice.TargetId : "";
                store.UpdateProgress(report, choice.Action == "reuse" ? "Reusing existing object" : "Copying and verifying object", item);
                var recovery = choice.Action == "overwrite" && review.RecoveryCopies;
                if (choice.Action == "reuse")
                {
                    mappings[key] = destinationId; AddKeyAliases(plan, item, destinationId, mappings);
                    report.Items.Add(new(key, item.Source.Type, item.Source.Id, item.Source.Name, choice.Action, destinationId, choice.Name, "Reused", "Destination configuration, credentials, groups, folder and metadata retained.", []));
                    await store.SaveReportAsync(report, CancellationToken.None); continue;
                }
                var index = report.Items.Count;
                // Write-ahead status makes a crash or lost response explicit. Never replay these entries on startup.
                var needsCheckout = choice.Action == "overwrite" || item.Source.Type is "Script" or "DateList";
                report.Items.Add(new(key, item.Source.Type, item.Source.Id, item.Source.Name, choice.Action, destinationId, choice.Name, "Not verified", "Write is pending or in progress; inspect before retrying if MAUI stops.", item.Skipped.ToArray(), recovery, item.CredentialStatus, needsCheckout ? "Pending" : "Not required") { MetadataNotes = item.MetadataNotes.ToArray() });
                await store.SaveReportAsync(report, CancellationToken.None);
                async Task KnownId(string id)
                {
                    destinationId = id; report.Items[index] = report.Items[index] with { DestinationId = id };
                    await store.SaveReportAsync(report, CancellationToken.None);
                }
                try
                {
                    var finalName = choice.Name;
                    var groups = DestinationGroups(plan, item);
                    if (item.Source.Type is "Task" or "Host")
                    {
                        var nativeId = choice.Action == "create" ? item.Source.Id : choice.TargetId;
                        var xml = MauiNativePortabilityService.RewriteCopyObject(item.PortableXml, item.Source.Type, item.Source.Id, nativeId, choice.Name, mappings, groups);
                        var copied = await destination.UseAsync(sp => sp.GetRequiredService<IEnvironmentCopyNativeTransfer>().CopyAsync(new(item.Source.Type, item.Source.Id, xml,
                            plan.Passphrase, choice.Name, choice.Action == "overwrite" ? choice.TargetId : "", item.DestinationRevision, groups,
                            review.PreserveDestinationCredentials, item.DestinationPortableXml), KnownId, CancellationToken.None));
                        destinationId = copied.Id;
                    }
                    else if (item.Source.Type is "Script" or "DateList")
                    {
                        await destination.UseAsync(async sp =>
                        {
                            if (choice.Action == "create")
                            {
                                if (item.Source.Type == "Script")
                                {
                                    var created = await sp.GetRequiredService<IAutomationScriptService>().CreateScriptAsync(choice.Name, item.Source.Subtype, CancellationToken.None, groups?.FirstOrDefault());
                                    await KnownId(created.Id);
                                }
                                else
                                {
                                    var created = await sp.GetRequiredService<IAutomationDateListService>().CreateAsync(choice.Name, "", CancellationToken.None);
                                    await KnownId(created.Id);
                                }
                            }
                            var xml = EnvironmentCopyRules.Rewrite(item.NativeXml, destinationId, choice.Name, mappings, item.Source.Type == "DateList" ? [] : groups);
                            // COM restore performs its own checkout. The optional copy revision guard runs after CheckoutItem.
                            using var version = choice.Action == "overwrite" ? ObjectEditVersion.Require("EnvironmentCopy", item.Source.Type + "|" + destinationId, item.DestinationRevision) : null;
                            string savedXml;
                            if (item.Source.Type == "Script") savedXml = (await sp.GetRequiredService<IScriptCrudCoordinator>().RestoreNativeConfigurationAsync(destinationId, xml, CancellationToken.None)).Xml;
                            else savedXml = (await sp.GetRequiredService<IDateListCrudCoordinator>().RestoreNativeConfigurationAsync(destinationId, xml, CancellationToken.None)).Xml;
                            if (!EquivalentResourceXml(xml, savedXml)) throw new InvalidOperationException("The resource was saved but its complete configuration could not be verified.");
                            return true;
                        });
                    }
                    else
                    {
                        if (item.PortableXml.Length > 0)
                            destinationId = await destination.UseAsync(sp => sp.GetRequiredService<IEnvironmentCopyNativeTransfer>().CopyKeyAsync(NativeType(item.Source), NativeId(item.Source), item.PortableXml,
                                plan.Passphrase, groups ?? [], KnownId, CancellationToken.None));
                        else
                        {
                            var imported = await destination.UseAsync(sp => sp.GetRequiredService<IAutomationKeyCertificateService>().ImportAsync(item.Source.Subtype == "Tls" ? "TLS" : "PGP",
                                choice.Name, item.KeyFileName, item.KeyBytes ?? throw new InvalidOperationException("The reviewed key export is unavailable."), item.Source.Subtype == "Tls" && review.IncludeTlsPrivateKeys ? plan.Passphrase : "", CancellationToken.None, groups?.FirstOrDefault()));
                            await KnownId(imported.RowId);
                        }
                        var saved = await destination.UseAsync(sp => sp.GetRequiredService<IAutomationKeyCertificateService>().GetAsync(destinationId, CancellationToken.None));
                        finalName = saved.Name;
                        var fingerprint = EnvironmentCopyObjects.KeyFingerprint(saved);
                        if (fingerprint.Length == 0 && item.Source.Subtype == "Pgp")
                            fingerprint = await destination.UseAsync(sp => sp.GetRequiredService<EnvironmentCopyKeyIdentity>().FingerprintAsync(destinationId, CancellationToken.None));
                        if (item.Source.Fingerprint.Length > 0 && EnvironmentCopyObjects.NormalizeFingerprint(fingerprint) != EnvironmentCopyObjects.NormalizeFingerprint(item.Source.Fingerprint)
                            || item.Source.Subtype == "Pgp" && saved.Id != NativeId(item.Source)) throw new InvalidOperationException("The imported key identity could not be verified.");
                    }
                    if (item.Source.Type != "DateList")
                    {
                        store.UpdateProgress(report, "Assigning Resource Groups", item);
                        await destination.UseAsync(async sp =>
                        {
                            var memberType = item.Source.Type switch { "Task" => "task", "Host" => "host", "Script" => "script", _ => item.Source.Subtype switch { "Ssh" => "ssh", "Tls" => "ssl", _ => "pgp" } };
                            var memberId = item.Source.Type == "KeyCertificate" ? destinationId[(destinationId.IndexOf('|') + 1)..] : destinationId;
                            await sp.GetRequiredService<IResourceGroupManagementService>().SetObjectGroupsAsync(new(memberType, memberId), groups ?? [], CancellationToken.None);
                            return true;
                        });
                    }
                    store.UpdateProgress(report, "Saving MAUI properties and folder placement", item);
                    mappings[key] = destinationId; AddKeyAliases(plan, item, destinationId, mappings);
                    await destination.UseAsync(async sp => { await SaveLocalPropertiesAsync(sp, plan, item, destinationId, folderId); return true; });
                    report.Items[index] = report.Items[index] with { DestinationId = destinationId, DestinationName = finalName, Status = choice.Action == "create" ? "Created" : "Overwritten", CheckoutCleanup = needsCheckout ? "Released" : "Not required",
                        Message = item.Source.Type == "Task" ? "Saved and verified; Task remains disabled." : "Saved and verified.", Skipped = item.Skipped.ToArray(), MetadataNotes = item.MetadataNotes.ToArray() };
                    await store.SaveReportAsync(report, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    // Keep a shell ID even when its subsequent restore/readback failed.
                    if (ex is ScriptWriteUncertainException script && script.ScriptId.Length > 0) destinationId = script.ScriptId;
                    if (ex is DateListWriteUncertainException date && date.DateListId.Length > 0) destinationId = date.DateListId;
                    if (destinationId.Length == 0)
                    {
                        try
                        {
                            var now = await destination.UseAsync(sp => EnvironmentCopyObjects.InventoryAsync(sp, CancellationToken.None));
                            destinationId = now.SingleOrDefault(x => x.Type == item.Source.Type && x.Name.Equals(choice.Name, StringComparison.OrdinalIgnoreCase))?.Id ?? "";
                        }
                        catch { /* Reconciliation is best effort; no second create is attempted. */ }
                    }
                    var rejected = ex is ObjectCheckoutRejectedException or ObjectEditConflictException
                        || ex is MoveItApiException api && api.StatusCode.HasValue && (int)api.StatusCode.Value is >= 400 and < 500 && (int)api.StatusCode.Value != 408;
                    var message = ex is ObjectCheckoutReleaseException ? "Checkout release could not be confirmed. Inspect this object and its checkout before retrying."
                        : rejected ? "MOVEit rejected the operation or the reviewed revision changed. Analyze and review again before retrying."
                        : "The destination write or verification did not complete. Inspect this object and any recorded destination ID before starting another copy.";
                    report.Items[index] = report.Items[index] with { DestinationId = destinationId, Status = rejected ? "Failed" : "Not verified", Message = message, Skipped = item.Skipped.ToArray(), MetadataNotes = item.MetadataNotes.ToArray(),
                        CheckoutCleanup = !needsCheckout ? "Not required" : ex is ObjectCheckoutReleaseException ? "Unconfirmed" : "No owned checkout retained" };
                    report.Status = "Stopped"; report.Message = "Stopped at the first write failure. Completed objects were retained; dependent writes were not attempted.";
                    break;
                }
            }
            if (report.Status == "Running") store.UpdateProgress(report, "Closing environment connections");
        }
        catch (OperationCanceledException) when (plan.CancelRequested || stopping.IsCancellationRequested || !Enabled)
        {
            report.Status = "Cancelled"; report.Message = "Stopped before the next write. Completed destination objects and Resource Groups were retained.";
        }
        catch
        {
            report.Status = "Stopped"; report.Message = report.GroupCreationStatus == "Not verified"
                ? "Resource Group creation could not be confirmed. Inspect the planned Group names before retrying. No object copies were attempted."
                : "Copy preflight or recovery capture failed. No further writes were attempted. Analyze and review again; inspect any entries already recorded as Not verified.";
        }
        finally
        {
            if (report.Status == "Running") report.SetSuccessfulCompletionStatus();
            foreach (var item in plan.Items.Where(x => report.Items.All(done => done.Key != x.Source.Key)))
                report.Items.Add(new(item.Source.Key, item.Source.Type, item.Source.Id, item.Source.Name, item.Choice.Action, "", item.Choice.Name, "Skipped", "Not attempted after cancellation or a prerequisite failure.", []));
            report.CancelRequested = plan.CancelRequested; report.CompletedUtc = DateTimeOffset.UtcNow;
            store.UpdateProgress(report, report.Status);
            try { await store.SaveReportAsync(report, CancellationToken.None); }
            finally { store.Forget(plan); }
        }
    }
    internal static void ThrowIfCopyStopped(CopyPlan plan, CancellationToken ct, bool enabled)
    {
        if (plan.CancelRequested || ct.IsCancellationRequested || !enabled) throw new OperationCanceledException("Copy stopped before the next write.", ct);
    }
    private static IReadOnlyList<string>? DestinationGroups(CopyPlan plan, CopyPlanItem item, CopyReviewRequest? placement = null)
    {
        var review = placement ?? plan.Review!;
        if (item.Source.Type == "DateList") return [];
        if (item.Choice.Action == "overwrite" && review.PreserveDestinationGroups) return item.DestinationGroupsBefore;
        if (review.GroupMode == "none") return [];
        if (review.GroupMode == "one") return [plan.Groups.Single(x => x.Equals(review.Group, StringComparison.OrdinalIgnoreCase))];
        if (review.GroupMode == "create-source") return item.Source.AssignedGroups.Select(name => plan.Groups.FirstOrDefault(group => group.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? name).ToArray();
        return item.Source.AssignedGroups.Where(x => plan.Groups.Contains(x, StringComparer.OrdinalIgnoreCase))
            .Select(x => plan.Groups.Single(g => g.Equals(x, StringComparison.OrdinalIgnoreCase))).ToArray();
    }
    private static void AddKeyAliases(CopyPlan plan, CopyPlanItem item, string id, IDictionary<string, string> mappings)
    {
        if (item.Source.Type != "KeyCertificate") return;
        var mapped = item.Source.Subtype == "Tls" && item.Source.Fingerprint.Length > 0 ? "tls|" + item.Source.Fingerprint : id;
        mappings[item.Source.Key] = mapped;
        if (item.Source.Fingerprint.Length > 0)
        {
            mappings["KeyCertificate|" + item.Source.Id[..4] + item.Source.Fingerprint] = mapped;
            foreach (var reference in plan.Items.Where(x => x.NativeXml.Length > 0).SelectMany(x => XElement.Parse(x.NativeXml).DescendantsAndSelf().Attributes())
                .Where(x => x.Name.LocalName is "ClientCertSHA1" or "DefClientCertSHA1"))
                if (EnvironmentCopyObjects.NormalizeFingerprint(reference.Value) == EnvironmentCopyObjects.NormalizeFingerprint(item.Source.Fingerprint))
                    mappings["KeyCertificate|tls|" + reference.Value] = mapped;
        }
    }
    private static bool EquivalentResourceXml(string intended, string saved)
    {
        var a = XElement.Parse(intended); var b = XElement.Parse(saved);
        static void Clean(XElement root) => root.Attributes().Where(x => x.Name.LocalName.StartsWith('x') || x.Name.LocalName is "UsedByCount" or "Renamed").Remove();
        Clean(a); Clean(b); return ObjectEditVersion.Xml(a).ToJsonString() == ObjectEditVersion.Xml(b).ToJsonString();
    }
    internal static async Task SaveLocalPropertiesAsync(IServiceProvider services, CopyPlan plan, CopyPlanItem item, string destinationId, int? folderId)
    {
        // Native configuration is already verified. Keep expected metadata differences separate
        // from actual local-write failures. Keep the existing follow-up order and stop on failure.
        var failureMessage = "Object copied; its MAUI audit record could not be saved.";
        try
        {
            var metadata = services.GetRequiredService<IMetadataRepository>();
            if (item.Choice.Action == "create") await metadata.RecordObjectCreatedAsync(plan.Destination, item.Source.Type, destinationId, plan.Owner);
            else await metadata.RecordObjectConfigurationModifiedAsync(plan.Destination, item.Source.Type, destinationId, plan.Owner);
            if (item.Source.Type == "Task" && item.Choice.Action == "create")
            {
                failureMessage = "Task copied; its MAUI folder assignment could not be saved. Check its destination folder.";
                await metadata.AssignObjectToFolderAsync(plan.Destination, "Task", destinationId, folderId, plan.Owner);
            }
            var review = plan.Review!;
            if (review.CopyMetadata && item.Metadata is not null && !(item.Choice.Action == "overwrite" && review.PreserveDestinationMetadata))
            {
                failureMessage = "Object copied; its MAUI metadata could not be saved. Check its destination properties.";
                var portable = await MergeMetadataAsync(services, plan, item, destinationId);
                var service = services.GetRequiredService<IMauiMetadataPortabilityService>();
                var summary = await service.ValidateAsync(plan.Destination, portable, item.Source.Type);
                foreach (var issue in summary.Issues) if (!item.MetadataNotes.Contains(issue)) item.MetadataNotes.Add(issue);
                await service.ApplyCompatibleAsync(plan.Destination, item.Source.Type, destinationId, portable, plan.Owner);
            }
        }
        catch { item.Skipped.Add(failureMessage); }
    }
    private static async Task<PortableMetadataPackage> MergeMetadataAsync(IServiceProvider services, CopyPlan plan, CopyPlanItem item, string destinationId)
    {
        var existing = await services.GetRequiredService<IMetadataRepository>().GetObjectMetadataAsync(plan.Destination, item.Source.Type, destinationId);
        var fields = new List<PortableMetadataField>();
        foreach (var field in item.Metadata!.Fields)
        {
            // Empty source values do not clear an existing destination value; skipped/incompatible fields remain untouched.
            if (item.Choice.Action == "overwrite" && string.IsNullOrEmpty(field.Value)) { item.MetadataNotes.Add($"Metadata '{field.Name}' was empty; destination value retained."); continue; }
            if (field.Type.Equals("Tags", StringComparison.OrdinalIgnoreCase) && existing.Values.TryGetValue(field.Name, out var tags))
            {
                var merged = (tags + ";" + field.Value).Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase);
                fields.Add(field with { Value = string.Join(';', merged) });
            }
            else fields.Add(field);
        }
        return item.Metadata with { Fields = fields };
    }
    internal static async Task<int?> ResolveFolderAsync(IMetadataRepository metadata, CopyPlan plan, CancellationToken ct, Func<bool> shouldStop)
    {
        var review = plan.Review!;
        if (!plan.Items.Any(x => x.Source.Type == "Task" && x.Choice.Action == "create") || review.FolderMode == "root") return null;
        void CheckStop() { if (shouldStop()) throw new OperationCanceledException("Copy stopped before folder creation.", ct); }
        CheckStop();
        var folders = (await metadata.GetFoldersAsync(plan.Destination, "Task", ct)).ToList();
        CheckStop();
        if (review.FolderMode == "existing") return folders.Any(x => x.Id == review.FolderId) ? review.FolderId : throw new InvalidOperationException("The destination folder no longer exists.");
        int? parent = null;
        foreach (var segment in FolderSegments(review.FolderPath))
        {
            CheckStop();
            var found = folders.SingleOrDefault(x => x.ParentId == parent && x.Name.Equals(segment, StringComparison.OrdinalIgnoreCase));
            if (found is null)
            {
                if (!review.CreateFolder) throw new InvalidOperationException("A destination folder is missing and creation is disabled.");
                // Finish one local folder transaction before observing Stop again.
                found = await metadata.CreateFolderAsync(plan.Destination, "Task", segment, parent, CancellationToken.None); folders.Add(found);
            }
            parent = found.Id;
        }
        return parent;
    }
}
