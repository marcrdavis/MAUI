// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Text.Json.Nodes;
using System.Xml.Linq;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt.Tasks;

namespace MoveItAutomationUI.Services.MoveIt;

public sealed record NativeConfigurationExportItem(string ID, string SectionType);
public sealed record NativePortabilityExportFile(string FileName, byte[] Content);
public sealed record NativePortabilityConflictResolution(string ObjectType, string SourceId, string Action, string NewName, string TargetId = "");
public sealed record NativePortabilityObjectPreview(
    string ObjectType,
    string SourceId,
    string SourceName,
    string DisplayType,
    bool Selected,
    bool Dependency,
    bool Conflict,
    IReadOnlyList<string> DuplicateReasons,
    bool CanRename,
    string SuggestedName,
    string ConflictTargetId,
    IReadOnlyList<string> MetadataIssues);
public sealed record NativePortabilityPreflight(
    bool Valid,
    string PackageType,
    string ProtectionMode,
    bool RequiresPassphrase,
    bool IncludesKeys,
    DateTimeOffset ExportedUtc,
    int NativeEncryptionSchemeVersion,
    int ItemCount,
    int ConflictCount,
    bool RequiresMetadataAcknowledgement,
    IReadOnlyList<NativePortabilityObjectPreview> Items,
    string Message);
public sealed record NativePortabilityImportedItem(string ObjectType, string SourceId, string SourceName, string FinalName, string DestinationId, bool Selected, bool Overwritten, bool MetadataApplied, string Warning);
public sealed record NativePortabilityImportResult(
    bool Success,
    string PackageType,
    int ImportedCount,
    int OverwrittenCount,
    int RenamedCount,
    IReadOnlyList<NativePortabilityImportedItem> Items,
    string Warning);

/// <summary>
/// MOVEit's native import request was sent, but MAUI did not receive a definitive upstream result.
/// The caller must treat the destination state as unknown and require verification before any retry.
/// </summary>
public sealed class NativePortabilityImportOutcomeUnknownException : InvalidOperationException
{
    public NativePortabilityImportOutcomeUnknownException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public interface IMauiNativePortabilityService
{
    Task<NativePortabilityExportFile> ExportTasksAsync(IReadOnlyList<string> taskIds, string protectionMode, string passphrase, bool includeKeys, CancellationToken cancellationToken = default);
    Task<NativePortabilityExportFile> ExportHostsAsync(IReadOnlyList<string> hostIds, string protectionMode, string passphrase, bool includeKeys, CancellationToken cancellationToken = default);
    Task<NativePortabilityPreflight> PreflightImportAsync(byte[] packageBytes, string expectedPackageType, string passphrase, CancellationToken cancellationToken = default);
    Task<NativePortabilityImportResult> ImportAsync(byte[] packageBytes, string expectedPackageType, string passphrase, IReadOnlyList<NativePortabilityConflictResolution> resolutions, bool leaveTasksDisabled, bool acknowledgeUnsupportedMetadata, string username, CancellationToken cancellationToken = default);
}

/// <summary>
/// Unified Task/Host portability. MOVEit owns native dependency discovery, credential protection, decoding,
/// conflict semantics, and import. MAUI wraps the native export with MAUI metadata and provides a safer preview.
/// </summary>
public sealed partial class MauiNativePortabilityService : IMauiNativePortabilityService
{
    private readonly IMoveItRestClient _rest;
    private readonly IMoveItObjectAccessService? _access;
    private readonly ITaskCrudCoordinator _taskCrud;
    private readonly IAutomationTaskService _tasks;
    private readonly IAutomationHostService _hosts;
    private readonly IAutomationScriptService _scripts;
    private readonly IAutomationDateListService _dateLists;
    private readonly IAutomationKeyCertificateService _keys;
    private readonly ITaskUndoService _taskUndo;
    private readonly IHostUndoService _hostUndo;
    private readonly IScriptUndoService _scriptUndo;
    private readonly IDateListUndoService _dateListUndo;
    private readonly IMetadataRepository _metadata;
    private readonly IMauiMetadataPortabilityService _portableMetadata;
    private readonly IMoveItSessionService _session;
    private readonly ILogger<MauiNativePortabilityService> _logger;

    public MauiNativePortabilityService(
        IMoveItRestClient rest,
        ITaskCrudCoordinator taskCrud,
        IAutomationTaskService tasks,
        IAutomationHostService hosts,
        IAutomationScriptService scripts,
        IAutomationDateListService dateLists,
        IAutomationKeyCertificateService keys,
        ITaskUndoService taskUndo,
        IHostUndoService hostUndo,
        IScriptUndoService scriptUndo,
        IDateListUndoService dateListUndo,
        IMetadataRepository metadata,
        IMauiMetadataPortabilityService portableMetadata,
        IMoveItSessionService session,
        ILogger<MauiNativePortabilityService> logger, IMoveItObjectAccessService? access = null)
    {
        _rest = rest;
        _access = access;
        _taskCrud = taskCrud;
        _tasks = tasks;
        _hosts = hosts;
        _scripts = scripts;
        _dateLists = dateLists;
        _keys = keys;
        _taskUndo = taskUndo;
        _hostUndo = hostUndo;
        _scriptUndo = scriptUndo;
        _dateListUndo = dateListUndo;
        _metadata = metadata;
        _portableMetadata = portableMetadata;
        _session = session;
        _logger = logger;
    }

    public async Task<NativePortabilityExportFile> ExportTasksAsync(
        IReadOnlyList<string> taskIds,
        string protectionMode,
        string passphrase,
        bool includeKeys,
        CancellationToken cancellationToken = default)
    {
        var session = RequireSession();
        var normalizedIds = NormalizeIds(taskIds, "Task");
        var protection = NormalizeProtection(protectionMode, passphrase);
        var snapshot = await _tasks.GetTaskSnapshotAsync(true, cancellationToken);
        var selected = new List<MauiNativePortablePackage.SelectedItem>();
        foreach (var id in normalizedIds)
        {
            var task = snapshot.Tasks.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"MOVEit Task '{id}' was not found or is not visible to the current user.");
            selected.Add(new MauiNativePortablePackage.SelectedItem("Task", task.Id, task.Name));
        }

        var nativeXml = await ExportNativeAsync(
            normalizedIds.Select(id => new NativeConfigurationExportItem(id, "Task")).ToList(),
            includeKeys,
            protection == "passphrase" ? passphrase : null,
            cancellationToken);

        return await BuildPackageAsync("Task", protection, includeKeys, selected, nativeXml, session, cancellationToken);
    }

    public async Task<NativePortabilityExportFile> ExportHostsAsync(
        IReadOnlyList<string> hostIds,
        string protectionMode,
        string passphrase,
        bool includeKeys,
        CancellationToken cancellationToken = default)
    {
        var session = RequireSession();
        var normalizedIds = NormalizeIds(hostIds, "Host");
        var protection = NormalizeProtection(protectionMode, passphrase);
        var snapshot = await _hosts.GetHostSnapshotAsync(true, cancellationToken);
        var requestedHosts = new List<AutomationHost>();
        foreach (var id in normalizedIds)
        {
            var host = snapshot.Hosts.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"MOVEit Host '{id}' was not found or is not visible to the current user.");
            if (host.TypeCode.Equals("FileSystem", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The Local File System Host is managed by MOVEit Automation and cannot be exported.");
            if (!IsSupportedHostCarrierType(host.TypeCode))
                throw new InvalidOperationException($"Host '{host.Name}' uses MOVEit Host type '{host.TypeCode}', which does not yet have a verified MAUI native carrier-step contract. No export was created.");
            requestedHosts.Add(host);
        }

        // A Host cannot be selected directly by MOVEit's native config/export endpoint. Create exactly one disabled,
        // intentionally incomplete carrier Task containing a reference step for every requested Host. The carrier is
        // never included in the MAUI package and is deleted through the raw REST API in finally.
        // Use the same per-environment creation lock as normal Task create/import/clone paths so the temporary
        // carrier cannot race another MAUI Task creation past a finite MOVEit license limit.
        using var creationLease = await MoveItLicensePolicy.AcquireTaskCreationAsync(session, cancellationToken);
        var taskCount = await _tasks.GetTaskCountAsync(cancellationToken);
        var capacityBlock = MoveItLicensePolicy.GetTaskCapacityBlock(session, taskCount, 1);
        if (!string.IsNullOrWhiteSpace(capacityBlock))
            throw new InvalidOperationException($"Portable Host export requires one temporary disabled Task. {capacityBlock}");

        var carrierName = $"_MAUI_HOST_EXPORT_{Guid.NewGuid():N}";
        MoveItTaskConfiguration? carrier = null;
        Exception? exportFailure = null;
        try
        {
            var carrierXml = BuildHostCarrierTaskXml(carrierName, requestedHosts);
            carrier = await CreateHostExportCarrierAsync(carrierName, carrierXml, cancellationToken);
            var exportedXml = await ExportNativeAsync(
                new[] { new NativeConfigurationExportItem(carrier.Id, "Task") },
                includeKeys,
                protection == "passphrase" ? passphrase : null,
                cancellationToken);

            var finalXml = BuildHostOnlyNativeExport(exportedXml, requestedHosts);
            var selected = requestedHosts.Select(host => new MauiNativePortablePackage.SelectedItem("Host", host.Id, host.Name)).ToList();
            return await BuildPackageAsync("Host", protection, includeKeys, selected, finalXml, session, cancellationToken);
        }
        catch (Exception ex)
        {
            exportFailure = ex;
            throw;
        }
        finally
        {
            if (carrier is not null)
            {
                var removed = false;
                try
                {
                    await _taskCrud.DeleteAsync(carrier.Id, CancellationToken.None);
                    removed = true;
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogError(cleanupEx, "MAUI could not remove temporary Host-export carrier Task {TaskId} ({TaskName}).", carrier.Id, carrier.Name);
                    // Always surface carrier-cleanup failure because leaving an internal Task behind requires
                    // administrator action. If the export itself also failed, include that original message without
                    // returning either MOVEit response body or any protected native configuration.
                    if (exportFailure is null)
                        throw new InvalidOperationException($"The Host export carrier Task '{carrier.Name}' could not be removed. Remove that disabled Task from MOVEit before retrying the export.", cleanupEx);
                    throw new InvalidOperationException($"Host export failed and MAUI also could not remove temporary carrier Task '{carrier.Name}'. Remove that disabled Task from MOVEit before retrying. Original export error: {exportFailure.Message}", cleanupEx);
                }
                finally
                {
                    if (removed)
                    {
                        // A background Task-grid reconcile could have observed the short-lived carrier. Refresh the
                        // authoritative Task cache after cleanup so MAUI never leaves the internal carrier visible.
                        try { _ = await _tasks.GetTaskSnapshotAsync(true, CancellationToken.None); }
                        catch (Exception refreshEx) { _logger.LogDebug(refreshEx, "Task cache refresh after Host-export carrier cleanup did not complete."); }
                    }
                }
            }
        }
    }

    public async Task<NativePortabilityPreflight> PreflightImportAsync(
        byte[] packageBytes,
        string expectedPackageType,
        string passphrase,
        CancellationToken cancellationToken = default)
    {
        var session = RequireSession();
        var package = MauiNativePortablePackage.Parse(packageBytes, expectedPackageType, _portableMetadata);
        ValidatePassphrase(package, passphrase);
        var decoded = await DecodeNativeImportAsync(package.NativeXml, package.Manifest.ProtectionMode == "passphrase" ? passphrase : null, cancellationToken);
        var decodedObjects = EnumerateImportObjects(decoded);
        ValidateDecodedBoundary(package, decodedObjects);
        var destination = await LoadDestinationInventoryAsync(cancellationToken);
        var duplicates = MergeKeyInventoryDuplicates(ReadDuplicates(decoded), decodedObjects, destination);
        var metadataByKey = package.MetadataItems.ToDictionary(item => MetadataKey(item.ObjectType, item.SourceId), StringComparer.OrdinalIgnoreCase);
        var selectedKeys = package.Manifest.SelectedItems.Select(item => MetadataKey(item.Type, item.SourceId)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usedNames = BuildUsedNames(destination);
        var previews = new List<NativePortabilityObjectPreview>(decodedObjects.Count);
        var requiresMetadataAck = false;

        foreach (var obj in decodedObjects)
        {
            var duplicate = FindDuplicate(duplicates, obj);
            var issues = new List<string>();
            var metadataKey = MetadataKey(ToMetadataObjectType(obj.ObjectType), MetadataSourceId(obj));
            if (metadataByKey.TryGetValue(metadataKey, out var metadataItem))
            {
                var summary = await _portableMetadata.ValidateAsync(session.GetEnvironmentKey(), metadataItem.Metadata, metadataItem.ObjectType, cancellationToken);
                issues.AddRange(summary.Issues);
                requiresMetadataAck |= summary.RequiresAcknowledgement;
            }

            var canRename = CanRename(obj);
            var suggestedName = duplicate is null || !canRename
                ? obj.SourceName
                : SuggestUniqueName(obj.ObjectType, obj.SourceName, usedNames);
            if (canRename && suggestedName.Length > 0) usedNames.GetOrAdd(obj.ObjectType).Add(suggestedName);
            var selected = selectedKeys.Contains(MetadataKey(obj.ObjectType, obj.SourceId));
            previews.Add(new NativePortabilityObjectPreview(
                obj.ObjectType,
                obj.SourceId,
                obj.SourceName,
                FriendlyObjectType(obj.ObjectType, obj.NativeSubtype),
                selected,
                !selected,
                duplicate is not null,
                duplicate?.Reasons ?? Array.Empty<string>(),
                canRename,
                suggestedName,
                duplicate is null ? string.Empty : ResolveConflictTargetId(obj, destination),
                issues));
        }

        var conflicts = previews.Count(item => item.Conflict);
        var message = conflicts == 0
            ? "Package decoded successfully. No destination conflicts were detected."
            : $"Package decoded successfully. Resolve {conflicts} conflicting object{(conflicts == 1 ? string.Empty : "s")} before import.";
        return new NativePortabilityPreflight(
            true,
            package.Manifest.PackageType,
            package.Manifest.ProtectionMode,
            package.Manifest.ProtectionMode == "passphrase",
            package.Manifest.IncludesKeys,
            package.Manifest.ExportedUtc,
            package.Manifest.NativeEncryptionSchemeVersion,
            previews.Count,
            conflicts,
            requiresMetadataAck,
            previews,
            message);
    }

    public async Task<NativePortabilityImportResult> ImportAsync(
        byte[] packageBytes,
        string expectedPackageType,
        string passphrase,
        IReadOnlyList<NativePortabilityConflictResolution> resolutions,
        bool leaveTasksDisabled,
        bool acknowledgeUnsupportedMetadata,
        string username,
        CancellationToken cancellationToken = default)
    {
        var session = RequireSession();
        var package = MauiNativePortablePackage.Parse(packageBytes, expectedPackageType, _portableMetadata);
        ValidatePassphrase(package, passphrase);
        var preflight = await PreflightImportAsync(packageBytes, expectedPackageType, passphrase, cancellationToken);
        if (preflight.RequiresMetadataAcknowledgement && !acknowledgeUnsupportedMetadata)
            throw new InvalidOperationException("One or more MAUI metadata fields are not compatible with this environment. Review the warnings and acknowledge them before importing.");

        var decoded = await DecodeNativeImportAsync(package.NativeXml, package.Manifest.ProtectionMode == "passphrase" ? passphrase : null, cancellationToken);
        var importObjects = EnumerateImportObjects(decoded);
        ValidateDecodedBoundary(package, importObjects);
        var destinationForConflictDetection = await LoadDestinationInventoryAsync(cancellationToken);
        var duplicates = MergeKeyInventoryDuplicates(ReadDuplicates(decoded), importObjects, destinationForConflictDetection);
        var resolutionMap = BuildResolutionMap(resolutions);
        ValidateSubmittedConflictResolutions(resolutionMap, importObjects, duplicates, destinationForConflictDetection);

        // "Use Existing" must not be implemented by hand-editing MOVEit's decoded JSON model. The native
        // Web Admin contract was captured for Rename and native duplicate Overwrite, but not for a synthetic
        // key/certificate remove operation. Instead remove only the selected existing key/certificate from the
        // original native XML and ask /config/encode to build a fresh authoritative import model. The imported
        // Task/Host keeps its original native key ID reference, which is valid only because CanUseExistingKey
        // has verified that the same native ID exists in the destination environment.
        var useExistingKeys = new List<UseExistingKeyPlan>();
        foreach (var obj in importObjects.Where(item => IsKeyType(item.ObjectType)))
        {
            var duplicate = FindDuplicate(duplicates, obj);
            if (duplicate is null) continue;
            if (!resolutionMap.TryGetValue(MetadataKey(obj.ObjectType, obj.SourceId), out var resolution)) continue;
            if (!NormalizeResolutionAction(resolution.Action).Equals("useexisting", StringComparison.OrdinalIgnoreCase)) continue;
            if (!CanUseExistingKey(obj, duplicate, destinationForConflictDetection))
                throw new InvalidOperationException($"Use Existing is only valid when the conflicting {FriendlyObjectType(obj.ObjectType, obj.NativeSubtype)} has the same native ID in the destination environment.");
            useExistingKeys.Add(new UseExistingKeyPlan(obj, (resolution.TargetId ?? string.Empty).Trim()));
        }

        if (useExistingKeys.Count > 0)
        {
            var nativeXmlWithoutExistingKeys = RemoveUseExistingKeyDependencies(package.NativeXml, useExistingKeys.Select(plan => plan.Object).ToList());
            decoded = await DecodeNativeImportAsync(
                nativeXmlWithoutExistingKeys,
                package.Manifest.ProtectionMode == "passphrase" ? passphrase : null,
                cancellationToken);
            importObjects = EnumerateImportObjects(decoded);
            ValidateDecodedBoundary(package, importObjects);
            duplicates = MergeKeyInventoryDuplicates(ReadDuplicates(decoded), importObjects, destinationForConflictDetection);
        }

        var plans = new List<ImportPlan>();
        // Revalidate rename choices against the live destination inventory. The preview suggests a unique name,
        // but another administrator can create an object before Import is submitted and the user can edit the suggestion.
        var usedNames = BuildUsedNames(destinationForConflictDetection);
        var plannedNewNames = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var obj in importObjects)
        {
            var duplicate = FindDuplicate(duplicates, obj);
            var action = "create";
            var finalName = obj.SourceName;
            var expectedTargetId = string.Empty;
            if (duplicate is not null)
            {
                if (!resolutionMap.TryGetValue(MetadataKey(obj.ObjectType, obj.SourceId), out var resolution))
                    throw new InvalidOperationException(ConflictResolutionRequiredMessage(obj, duplicate));
                action = NormalizeResolutionAction(resolution.Action);
                expectedTargetId = (resolution.TargetId ?? string.Empty).Trim();
                if (action == "useexisting")
                    throw new InvalidOperationException($"MAUI could not exclude the existing {FriendlyObjectType(obj.ObjectType, obj.NativeSubtype)} dependency from MOVEit's authoritative import model. Analyze the package again before retrying.");
                if (action == "rename")
                {
                    if (!CanRename(obj)) throw new InvalidOperationException($"{FriendlyObjectType(obj.ObjectType, obj.NativeSubtype)} '{obj.SourceName}' cannot be renamed during import; choose Overwrite.");
                    // Rename is captured native behavior for Task/Host duplicates. Preserve MOVEit's decoded
                    // duplicate model and change only the captured Rename fields on that object.
                    finalName = NormalizeImportName(resolution.NewName, obj.ObjectType);
                    if (finalName.Equals(obj.SourceName, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Enter a different name for '{obj.SourceName}' when importing it as new.");
                    var namesForType = usedNames.GetOrAdd(obj.ObjectType);
                    if (namesForType.Contains(finalName) || plannedNewNames.GetOrAdd(obj.ObjectType).Contains(finalName))
                        throw new InvalidOperationException($"{FriendlyObjectType(obj.ObjectType, obj.NativeSubtype)} name '{finalName}' already exists or is already planned by this import. Choose another name.");
                    plannedNewNames.GetOrAdd(obj.ObjectType).Add(finalName);
                    ApplyRenameResolution(obj.Node, finalName);
                }
                else
                {
                    // Overwrite deliberately leaves MOVEit's /config/encode object untouched. For Task/Host and
                    // other native duplicates, encode already returned the authoritative duplicate state. For a
                    // key/certificate conflict found only by MAUI's live inventory, do not invent `duplicate` or
                    // Duplicated[] fields that were not present in the captured native key payload contract.
                }
            }

            if (duplicate is null)
            {
                var planned = plannedNewNames.GetOrAdd(obj.ObjectType);
                if (!planned.Add(finalName))
                    throw new InvalidOperationException($"The import package contains more than one {FriendlyObjectType(obj.ObjectType, obj.NativeSubtype)} planned with name '{finalName}'.");
            }

            if (leaveTasksDisabled && obj.ObjectType.Equals("Task", StringComparison.OrdinalIgnoreCase))
                obj.Node["Active"] = 0;
            plans.Add(new ImportPlan(obj, action, finalName, duplicate, expectedTargetId));
        }

        // Validate the complete native import plan before any object is changed. Imported
        // dependencies do not inherit the selected Task/Host's grants, and overwrite needs Edit too.
        if (_access is not null)
        {
            foreach (var plan in plans)
            {
                var type = plan.Object.ObjectType switch { "SSHClientKey" => "SSH", "SSLClientCert" => "TLS", "PGPKey" => "PGP", _ => plan.Object.ObjectType };
                await _access.RequireDefinitionCreationAsync(type, plan.Object.Node, cancellationToken);
                if (plan.Action == "overwrite")
                    await _access.RequireAsync(ToMetadataObjectType(plan.Object.ObjectType),
                        plan.ExpectedTargetId, "Edit", cancellationToken);
            }
        }

        // Preserve MAUI's existing license/capacity guardrails. Native MOVEit import is still authoritative, but
        // MAUI must not bypass the proactive limits already enforced by New/Clone/legacy portability workflows.
        // Acquire creation locks in a fixed Task-then-Host order to prevent concurrent MAUI imports/creates from
        // independently passing the same finite capacity check.
        var createsTasks = plans.Count(plan => plan.Object.ObjectType.Equals("Task", StringComparison.OrdinalIgnoreCase) && plan.Action != "overwrite");
        var createsHosts = plans.Count(plan => plan.Object.ObjectType.Equals("Host", StringComparison.OrdinalIgnoreCase) && plan.Action != "overwrite");
        using var taskCreationLease = createsTasks > 0
            ? await MoveItLicensePolicy.AcquireTaskCreationAsync(session, cancellationToken)
            : null;
        using var hostCreationLease = createsHosts > 0
            ? await MoveItLicensePolicy.AcquireHostCreationAsync(session, cancellationToken)
            : null;

        // Recheck destination names immediately before native import. A conflicting object can be created by
        // another administrator between Analyze Package and Import; fail safely instead of allowing a stale
        // no-conflict/rename plan to become an unintended overwrite. Task/Host creation locks above make this
        // atomic with other MAUI Task/Host creates; dependency types are revalidated best-effort immediately before commit.
        var lockedUsedNames = await LoadUsedNamesAsync(cancellationToken);
        foreach (var plan in plans.Where(plan => plan.Action != "overwrite"))
        {
            if (lockedUsedNames.GetOrAdd(plan.Object.ObjectType).Contains(plan.FinalName))
                throw new InvalidOperationException($"{FriendlyObjectType(plan.Object.ObjectType, plan.Object.NativeSubtype)} name '{plan.FinalName}' now exists in the destination environment. Analyze the package again and choose Overwrite or another name.");
        }

        if (createsTasks > 0)
        {
            var currentTaskCount = await _tasks.GetTaskCountAsync(cancellationToken);
            var taskCapacityBlock = MoveItLicensePolicy.GetTaskCapacityBlock(session, currentTaskCount, createsTasks);
            if (!string.IsNullOrWhiteSpace(taskCapacityBlock)) throw new InvalidOperationException(taskCapacityBlock);
        }
        if (createsHosts > 0)
        {
            var currentHostCount = (await _hosts.GetHostSnapshotAsync(true, cancellationToken)).Hosts.Count;
            var hostCapacityBlock = MoveItLicensePolicy.GetHostCapacityBlock(session, currentHostCount, createsHosts);
            if (!string.IsNullOrWhiteSpace(hostCapacityBlock)) throw new InvalidOperationException(hostCapacityBlock);
        }

        foreach (var plan in plans.Where(plan => plan.Object.ObjectType.Equals("Task", StringComparison.OrdinalIgnoreCase) && plan.Action != "overwrite"))
        {
            var taskType = TaskTypeFromCode(GetString(plan.Object.Node, "TT"));
            var advancedBlock = MoveItLicensePolicy.GetAdvancedTaskCreationBlock(session, taskType);
            if (!string.IsNullOrWhiteSpace(advancedBlock)) throw new InvalidOperationException(advancedBlock);
        }
        if (plans.Any(plan => plan.Object.ObjectType.Equals("Script", StringComparison.OrdinalIgnoreCase)))
        {
            // Match the existing Recycle Bin/license guardrail: an unlicensed environment cannot import or overwrite
            // custom Script configuration through MAUI portability.
            var scriptBlock = MoveItLicensePolicy.GetCustomScriptBlock(session);
            if (!string.IsNullOrWhiteSpace(scriptBlock)) throw new InvalidOperationException(scriptBlock);
        }
        if (plans.Any(plan => plan.Object.ObjectType.Equals("PGPKey", StringComparison.OrdinalIgnoreCase)))
        {
            var pgpBlock = MoveItLicensePolicy.GetPgpBlock(session);
            if (!string.IsNullOrWhiteSpace(pgpBlock)) throw new InvalidOperationException(pgpBlock);
        }

        // Preserve the overwrite guardrails from the previous Task/Host importers. Native MOVEit import remains
        // authoritative, but MAUI should fail before commit when the destination object is not editable, is a running
        // Task, is a different Task/Host type, is a built-in Script, or when ID/name conflicts point at two different
        // destination objects and therefore do not identify one unambiguous overwrite target.
        var overwriteInventory = await LoadDestinationInventoryAsync(cancellationToken);
        ValidateOverwritePlans(plans, overwriteInventory);
        ValidateUseExistingKeyPlans(useExistingKeys, overwriteInventory);

        // Save MAUI Undo snapshots before MOVEit overwrites any supported existing object. This extends the same
        // guardrail previously used by the Task/Host importers to native dependencies that are being overwritten.
        foreach (var plan in plans.Where(plan => plan.Action == "overwrite"))
            await CaptureUndoBeforeOverwriteAsync(plan, username, cancellationToken);

        // Undo capture performs additional MOVEit reads and can take enough time for destination state to change.
        // Revalidate the exact analyzed targets one last time immediately before the native point of no return.
        var finalCommitInventory = await LoadDestinationInventoryAsync(cancellationToken);
        ValidateOverwritePlans(plans, finalCommitInventory);
        ValidateUseExistingKeyPlans(useExistingKeys, finalCommitInventory);

        var planSummary = string.Join(", ", plans
            .GroupBy(plan => $"{plan.Object.ObjectType}:{plan.Action}", StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{group.Key}={group.Count()}"));
        _logger.LogInformation("Submitting MOVEit native portability import. Plan: {PlanSummary}.", planSummary);

        // This is the point of no return. Once MAUI starts MOVEit's native commit, request-abort cancellation
        // must not create an ambiguous state where MOVEit completed the import but MAUI reports it as cancelled.
        // The captured MOVEit Web Admin /api/v0/config/import contract places the portable
        // decryption passphrase inside the Imported object. MoveItRestClient adds it there
        // while otherwise preserving MOVEit's decoded import model exactly.
        await CommitNativeImportAsync(
            decoded,
            package.Manifest.ProtectionMode == "passphrase" ? passphrase : null,
            CancellationToken.None);

        // Everything below is post-commit reconciliation. MOVEit has already changed at this point, so a refresh,
        // metadata, audit, or client-cancellation failure must never turn the completed native import into a generic
        // "import failed" response that could encourage an unsafe retry.
        DestinationInventory? destination = null;
        var warnings = new List<string>();
        try
        {
            // Force authoritative post-import inventories before resolving final destination IDs. Source IDs are not
            // trusted for Import-as-New because MOVEit may assign different IDs while remapping native relationships.
            destination = await LoadDestinationInventoryAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MOVEit native import completed, but MAUI could not reload the destination inventory for post-import reconciliation.");
            warnings.Add("MOVEit import completed, but MAUI could not complete post-import reconciliation. Refresh the grid and verify the imported objects before attempting any retry.");
        }

        var selectedKeys = package.Manifest.SelectedItems
            .Select(item => MetadataKey(item.Type, item.SourceId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var metadataByKey = package.MetadataItems.ToDictionary(item => MetadataKey(item.ObjectType, item.SourceId), StringComparer.OrdinalIgnoreCase);
        var results = new List<NativePortabilityImportedItem>();

        foreach (var plan in plans)
        {
            var destinationId = destination is null ? string.Empty : ResolveDestinationId(plan, destination);
            var warning = string.Empty;
            var metadataApplied = false;
            var metadataType = ToMetadataObjectType(plan.Object.ObjectType);
            var sourceMetadataId = MetadataSourceId(plan.Object);
            if (destination is null)
            {
                warning = "MAUI could not reconcile this object after the completed MOVEit import; refresh and verify it before retrying.";
            }
            else if (metadataByKey.TryGetValue(MetadataKey(metadataType, sourceMetadataId), out var metadataItem) && destinationId.Length > 0)
            {
                try
                {
                    _ = await _portableMetadata.ApplyCompatibleAsync(session.GetEnvironmentKey(), metadataType, destinationId, metadataItem.Metadata, username, CancellationToken.None);
                    metadataApplied = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "MOVEit imported {ObjectType} {ObjectName}, but MAUI metadata could not be restored.", plan.Object.ObjectType, plan.FinalName);
                    warning = "Native configuration imported, but MAUI metadata could not be fully restored.";
                }
            }

            if (destinationId.Length > 0)
            {
                try
                {
                    if (plan.Action == "overwrite")
                        await _metadata.RecordObjectConfigurationModifiedAsync(session.GetEnvironmentKey(), metadataType, destinationId, username, CancellationToken.None);
                    else
                        await _metadata.RecordObjectCreatedAsync(session.GetEnvironmentKey(), metadataType, destinationId, username, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "MOVEit imported {ObjectType} {ObjectName}, but MAUI audit metadata could not be updated.", plan.Object.ObjectType, plan.FinalName);
                    warning = JoinWarnings(warning, "MAUI audit metadata could not be fully updated.");
                }
            }
            else if (destination is not null)
            {
                warning = JoinWarnings(warning, "MAUI could not resolve the imported object's destination ID, so local metadata was not applied.");
            }

            if (warning.Length > 0) warnings.Add($"{plan.FinalName}: {warning}");
            var selected = selectedKeys.Contains(MetadataKey(plan.Object.ObjectType, plan.Object.SourceId));
            results.Add(new NativePortabilityImportedItem(plan.Object.ObjectType, plan.Object.SourceId, plan.Object.SourceName, plan.FinalName, destinationId, selected, plan.Action == "overwrite", metadataApplied, warning));
        }

        return new NativePortabilityImportResult(
            true,
            package.Manifest.PackageType,
            results.Count,
            plans.Count(plan => plan.Action == "overwrite"),
            plans.Count(plan => plan.Action == "rename"),
            results,
            string.Join(" ", warnings));
    }

    private async Task<MoveItTaskConfiguration> CreateHostExportCarrierAsync(string carrierName, string carrierXml, CancellationToken cancellationToken)
    {
        try
        {
            return await _taskCrud.CreateFromNativeConfigurationAsync(carrierName, carrierXml, leaveDisabled: true, cancellationToken: cancellationToken);
        }
        catch (Exception createEx)
        {
            // CreateTaskFromConfigurationAsync verifies the persisted Task after CreateItem. If MOVEit creates the Task
            // but that verification fails, the caller may not receive its ID. The GUID-based carrier name is unique, so
            // locate and remove any orphan by exact name before propagating the original creation error.
            try
            {
                var inventory = (await _tasks.GetTaskSnapshotAsync(true, CancellationToken.None)).Tasks;
                var orphan = inventory.FirstOrDefault(task => task.Name.Equals(carrierName, StringComparison.Ordinal));
                if (orphan is not null)
                {
                    await _taskCrud.DeleteAsync(orphan.Id, CancellationToken.None);
                    try { _ = await _tasks.GetTaskSnapshotAsync(true, CancellationToken.None); }
                    catch (Exception refreshEx) { _logger.LogDebug(refreshEx, "Task cache refresh after failed Host-export carrier cleanup did not complete."); }
                }
            }
            catch (Exception cleanupEx)
            {
                _logger.LogError(cleanupEx, "MAUI could not verify cleanup of failed Host-export carrier Task {TaskName}.", carrierName);
                throw new InvalidOperationException($"MAUI could not complete Host export and could not verify cleanup of temporary carrier Task '{carrierName}'. Check MOVEit for that disabled Task and remove it before retrying.", createEx);
            }
            throw;
        }
    }

    private async Task<string> ExportNativeAsync(
        IReadOnlyList<NativeConfigurationExportItem> items,
        bool includeKeys,
        string? passphrase,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _rest.ExportNativeItemsAsync(items, includeKeys, passphrase, cancellationToken);
        }
        catch (MoveItApiException ex) when (ex is not MoveItAuthenticationException)
        {
            _logger.LogWarning(
                "MOVEit native portability export failed. HttpStatus={StatusCode}; Diagnostic={Diagnostic}.",
                ex.StatusCode, SafeMoveItDiagnostic(ex.Message));
            throw new InvalidOperationException("MOVEit could not export the selected configuration. Verify the selected objects and try again.");
        }
    }

    private async Task<JsonObject> DecodeNativeImportAsync(string nativeXml, string? passphrase, CancellationToken cancellationToken)
    {
        try
        {
            return await _rest.EncodeNativeImportAsync(nativeXml, passphrase, cancellationToken);
        }
        catch (MoveItApiException ex) when (ex is not MoveItAuthenticationException)
        {
            _logger.LogWarning(
                "MOVEit native portability decode failed. HttpStatus={StatusCode}; ProtectionMode={ProtectionMode}; Diagnostic={Diagnostic}.",
                ex.StatusCode, passphrase is null ? "server-key" : "passphrase", SafeMoveItDiagnostic(ex.Message));
            throw new InvalidOperationException(passphrase is null
                ? "MOVEit could not decode this Server Key package. Verify that it was exported from this MOVEit environment or one sharing its encryption key material."
                : "MOVEit could not decode this Portable package. Verify the passphrase and try again.");
        }
    }

    private async Task CommitNativeImportAsync(JsonObject decoded, string? passphrase, CancellationToken cancellationToken)
    {
        try
        {
            await _rest.ImportNativeConfigurationAsync(decoded, passphrase, cancellationToken);
        }
        catch (MoveItApiException ex) when (ex is not MoveItAuthenticationException
            && ex.StatusCode.HasValue
            && (int)ex.StatusCode.Value >= 500)
        {
            // An upstream 5xx is not proof that MOVEit made no change; the server can fail after receiving the
            // request. Treat it like a transport timeout and require destination verification before any retry.
            _logger.LogError(ex, "MOVEit native portability import returned an ambiguous server error with HTTP status {StatusCode}.", ex.StatusCode);
            throw new NativePortabilityImportOutcomeUnknownException("MOVEit returned a server error after the native import request was submitted. The import may have completed. Refresh the grid and verify the destination objects before attempting any retry.", ex);
        }
        catch (MoveItApiException ex) when (ex is not MoveItAuthenticationException)
        {
            // A definitive 4xx is safe to retry after correction. MoveItRestClient intentionally limits this
            // exception message to structured error fields (or status only), never the raw response/config body.
            _logger.LogWarning(ex, "MOVEit native portability import failed with HTTP status {StatusCode}: {MoveItError}", ex.StatusCode, ex.Message);
            throw;
        }
        catch (OperationCanceledException ex)
        {
            // The native import request has already been sent with a non-caller cancellation token. A timeout can
            // occur after MOVEit committed but before its response headers reached MAUI, so the outcome is unknown.
            _logger.LogError(ex, "MOVEit native portability import did not return a definitive result before the request timed out.");
            throw new NativePortabilityImportOutcomeUnknownException("MOVEit did not return a definitive import result. The import may have completed. Refresh the grid and verify the destination objects before attempting any retry.", ex);
        }
        catch (HttpRequestException ex)
        {
            // Connection failures have the same ambiguity: MAUI cannot prove whether MOVEit received/committed the request.
            _logger.LogError(ex, "MOVEit native portability import lost the connection before a definitive result was returned.");
            throw new NativePortabilityImportOutcomeUnknownException("MAUI lost contact with MOVEit before a definitive import result was returned. The import may have completed. Refresh the grid and verify the destination objects before attempting any retry.", ex);
        }
    }

    private async Task<NativePortabilityExportFile> BuildPackageAsync(
        string packageType,
        string protection,
        bool includeKeys,
        IReadOnlyList<MauiNativePortablePackage.SelectedItem> selected,
        string nativeXml,
        MoveItSessionState session,
        CancellationToken cancellationToken)
    {
        var document = ParseExportDocument(nativeXml);
        var scheme = int.TryParse((string?)document.Root?.Attribute("BackendEncryptionSchemeVersion"), out var version) ? version : 0;
        var descriptors = EnumerateNativeXmlObjects(document);
        var metadataObjects = new List<XElement>();
        foreach (var descriptor in descriptors)
        {
            if (_access is not null) await _access.RequireAsync(descriptor.MetadataObjectType, descriptor.MetadataObjectId, "Edit", cancellationToken);
            // Every native object type enumerated here has a MAUI metadata definition. Do not silently omit
            // metadata when its configuration/database read fails; a package advertised as carrying MAUI metadata
            // must either contain that metadata or fail the export before a misleading package is downloaded.
            var metadata = await _portableMetadata.BuildExportElementAsync(session.GetEnvironmentKey(), descriptor.MetadataObjectType, descriptor.MetadataObjectId, cancellationToken);
            metadataObjects.Add(MauiNativePortablePackage.BuildMetadataObject(descriptor.MetadataObjectType, descriptor.MetadataObjectId, descriptor.SourceName, metadata));
        }

        var bytes = MauiNativePortablePackage.Build(packageType, protection, includeKeys, DateTimeOffset.UtcNow, scheme, selected, nativeXml, metadataObjects);
        return new NativePortabilityExportFile(MauiNativePortablePackage.BuildFileName(packageType, selected), bytes);
    }

    private static void ValidateSubmittedConflictResolutions(
        IReadOnlyDictionary<string, NativePortabilityConflictResolution> resolutionMap,
        IReadOnlyList<ImportObjectRef> importObjects,
        IReadOnlyList<DuplicateRef> duplicates,
        DestinationInventory inventory)
    {
        foreach (var entry in resolutionMap)
        {
            var obj = importObjects.FirstOrDefault(item =>
                MetadataKey(item.ObjectType, item.SourceId).Equals(entry.Key, StringComparison.OrdinalIgnoreCase));
            if (obj is null)
                throw new InvalidOperationException(
                    $"The submitted conflict resolution for {entry.Value.ObjectType} ID '{entry.Value.SourceId}' no longer matches an object in MOVEit's current import model. Analyze the package again.");

            var duplicate = FindDuplicate(duplicates, obj);
            var action = NormalizeResolutionAction(entry.Value.Action);
            var expectedTargetId = (entry.Value.TargetId ?? string.Empty).Trim();
            var currentTargetId = duplicate is null ? string.Empty : ResolveConflictTargetId(obj, inventory);
            ValidateConflictTargetIdentity(
                $"{FriendlyObjectType(obj.ObjectType, obj.NativeSubtype)} '{obj.SourceName}'",
                expectedTargetId,
                currentTargetId,
                duplicate is not null,
                action);
        }
    }

    internal static void ValidateConflictTargetIdentity(
        string objectLabel,
        string? expectedTargetId,
        string? currentTargetId,
        bool conflictStillExists,
        string? action)
    {
        var label = string.IsNullOrWhiteSpace(objectLabel) ? "import object" : objectLabel.Trim();
        if (!conflictStillExists)
            throw new InvalidOperationException(
                $"The destination conflict for {label} changed or disappeared after the package was analyzed. Analyze the package again before importing.");

        var expected = (expectedTargetId ?? string.Empty).Trim();
        var current = (currentTargetId ?? string.Empty).Trim();
        var normalizedAction = NormalizeResolutionAction(action);

        if (expected.Length > 0 && !expected.Equals(current, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"The destination object involved in the conflict for {label} changed after the package was analyzed. Analyze the package again before importing.");

        if ((normalizedAction == "overwrite" || normalizedAction == "useexisting")
            && (expected.Length == 0 || current.Length == 0))
        {
            var actionLabel = normalizedAction == "useexisting" ? "Use Existing" : "Overwrite Existing";
            throw new InvalidOperationException(
                $"MAUI can no longer identify the exact destination target for {label}. Analyze the package again before choosing {actionLabel}.");
        }
    }

    private static string ResolveConflictTargetId(ImportObjectRef obj, DestinationInventory inventory)
    {
        if (obj.ObjectType.Equals("Task", StringComparison.OrdinalIgnoreCase))
        {
            var byId = inventory.Tasks.FirstOrDefault(item => item.Id.Equals(obj.SourceId, StringComparison.OrdinalIgnoreCase));
            var byName = inventory.Tasks.FirstOrDefault(item => item.Name.Equals(obj.SourceName, StringComparison.OrdinalIgnoreCase));
            ThrowIfAmbiguousOverwrite("Task", obj.SourceName, byId?.Id, byName?.Id);
            return (byId ?? byName)?.Id ?? string.Empty;
        }

        if (obj.ObjectType.Equals("Host", StringComparison.OrdinalIgnoreCase))
        {
            var byId = inventory.Hosts.FirstOrDefault(item => item.Id.Equals(obj.SourceId, StringComparison.OrdinalIgnoreCase));
            var byName = inventory.Hosts.FirstOrDefault(item => item.Name.Equals(obj.SourceName, StringComparison.OrdinalIgnoreCase));
            ThrowIfAmbiguousOverwrite("Host", obj.SourceName, byId?.Id, byName?.Id);
            return (byId ?? byName)?.Id ?? string.Empty;
        }

        if (obj.ObjectType.Equals("Script", StringComparison.OrdinalIgnoreCase))
        {
            var byId = inventory.Scripts.FirstOrDefault(item => item.Id.Equals(obj.SourceId, StringComparison.OrdinalIgnoreCase));
            var byName = inventory.Scripts.FirstOrDefault(item => item.Name.Equals(obj.SourceName, StringComparison.OrdinalIgnoreCase));
            ThrowIfAmbiguousOverwrite("Script", obj.SourceName, byId?.Id, byName?.Id);
            return (byId ?? byName)?.Id ?? string.Empty;
        }

        if (obj.ObjectType.Equals("DateList", StringComparison.OrdinalIgnoreCase))
        {
            var byId = inventory.DateLists.FirstOrDefault(item => item.Id.Equals(obj.SourceId, StringComparison.OrdinalIgnoreCase));
            var byName = inventory.DateLists.FirstOrDefault(item => item.Name.Equals(obj.SourceName, StringComparison.OrdinalIgnoreCase));
            ThrowIfAmbiguousOverwrite("Date List", obj.SourceName, byId?.Id, byName?.Id);
            return (byId ?? byName)?.Id ?? string.Empty;
        }

        if (IsKeyType(obj.ObjectType))
        {
            var kind = KeyKindFromObjectType(obj.ObjectType);
            var byId = inventory.Keys.FirstOrDefault(item => item.Kind == kind && item.Id.Equals(obj.SourceId, StringComparison.OrdinalIgnoreCase));
            var byName = inventory.Keys.FirstOrDefault(item => item.Kind == kind && item.Name.Equals(obj.SourceName, StringComparison.OrdinalIgnoreCase));
            ThrowIfAmbiguousOverwrite(FriendlyObjectType(obj.ObjectType, obj.NativeSubtype), obj.SourceName, byId?.RowId, byName?.RowId);
            return (byId ?? byName)?.RowId ?? string.Empty;
        }

        return string.Empty;
    }

    private static void ValidateExpectedConflictTarget(ImportPlan plan, string currentTargetId)
    {
        var expectedTargetId = (plan.ExpectedTargetId ?? string.Empty).Trim();
        if (expectedTargetId.Length == 0)
            throw new InvalidOperationException(
                $"MAUI does not have the analyzed destination target identity for {FriendlyObjectType(plan.Object.ObjectType, plan.Object.NativeSubtype)} '{plan.Object.SourceName}'. Analyze the package again before overwriting.");

        if (!expectedTargetId.Equals(currentTargetId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"The overwrite target for {FriendlyObjectType(plan.Object.ObjectType, plan.Object.NativeSubtype)} '{plan.Object.SourceName}' changed after the package was analyzed. Analyze the package again before importing.");
    }

    private static void ValidateUseExistingKeyPlans(IReadOnlyList<UseExistingKeyPlan> plans, DestinationInventory inventory)
    {
        foreach (var plan in plans)
        {
            var obj = plan.Object;
            var kind = KeyKindFromObjectType(obj.ObjectType);
            var target = inventory.Keys.FirstOrDefault(item =>
                item.Kind == kind && item.Id.Equals(obj.SourceId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"The {FriendlyObjectType(obj.ObjectType, obj.NativeSubtype)} '{obj.SourceName}' selected for Use Existing no longer exists in the destination environment. Analyze the package again.");

            if (string.IsNullOrWhiteSpace(plan.ExpectedTargetId)
                || !plan.ExpectedTargetId.Equals(target.RowId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The destination {FriendlyObjectType(obj.ObjectType, obj.NativeSubtype)} selected for Use Existing changed after the package was analyzed. Analyze the package again.");
            }
        }
    }

    private static void ValidateOverwritePlans(IReadOnlyList<ImportPlan> plans, DestinationInventory inventory)
    {
        foreach (var plan in plans.Where(plan => plan.Action == "overwrite"))
        {
            var obj = plan.Object;
            if (obj.ObjectType.Equals("Task", StringComparison.OrdinalIgnoreCase))
            {
                var byId = inventory.Tasks.FirstOrDefault(item => item.Id.Equals(obj.SourceId, StringComparison.OrdinalIgnoreCase));
                var byName = inventory.Tasks.FirstOrDefault(item => item.Name.Equals(obj.SourceName, StringComparison.OrdinalIgnoreCase));
                ThrowIfAmbiguousOverwrite("Task", obj.SourceName, byId?.Id, byName?.Id);
                var target = byId ?? byName ?? throw new InvalidOperationException($"The conflicting Task '{obj.SourceName}' no longer exists in the destination environment. Analyze the package again.");
                ValidateExpectedConflictTarget(plan, target.Id);
                if (!target.CanEditMoveItFields)
                    throw new InvalidOperationException($"MOVEit reports that the current user cannot edit Task '{target.Name}'.");
                if (target.Status.Equals("Running", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Task '{target.Name}' is currently running and cannot be overwritten.");
                var importedType = TaskTypeFromCode(GetString(obj.Node, "TT"));
                if (!target.Type.Equals(importedType, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"The imported Task is {importedType}, but '{target.Name}' is {target.Type}. MAUI does not overwrite a Task with a different Task type.");
                continue;
            }

            if (obj.ObjectType.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                var byId = inventory.Hosts.FirstOrDefault(item => item.Id.Equals(obj.SourceId, StringComparison.OrdinalIgnoreCase));
                var byName = inventory.Hosts.FirstOrDefault(item => item.Name.Equals(obj.SourceName, StringComparison.OrdinalIgnoreCase));
                ThrowIfAmbiguousOverwrite("Host", obj.SourceName, byId?.Id, byName?.Id);
                var target = byId ?? byName ?? throw new InvalidOperationException($"The conflicting Host '{obj.SourceName}' no longer exists in the destination environment. Analyze the package again.");
                ValidateExpectedConflictTarget(plan, target.Id);
                if (!target.CanEditDelete)
                    throw new InvalidOperationException($"MOVEit reports that the current user cannot edit Host '{target.Name}'.");
                if (!target.TypeCode.Equals(obj.NativeSubtype, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"The imported Host is type '{obj.NativeSubtype}', but '{target.Name}' is type '{target.TypeCode}'. MAUI does not overwrite a Host with a different Host type.");
                continue;
            }

            if (obj.ObjectType.Equals("Script", StringComparison.OrdinalIgnoreCase))
            {
                var byId = inventory.Scripts.FirstOrDefault(item => item.Id.Equals(obj.SourceId, StringComparison.OrdinalIgnoreCase));
                var byName = inventory.Scripts.FirstOrDefault(item => item.Name.Equals(obj.SourceName, StringComparison.OrdinalIgnoreCase));
                ThrowIfAmbiguousOverwrite("Script", obj.SourceName, byId?.Id, byName?.Id);
                var target = byId ?? byName ?? throw new InvalidOperationException($"The conflicting Script '{obj.SourceName}' no longer exists in the destination environment. Analyze the package again.");
                ValidateExpectedConflictTarget(plan, target.Id);
                if (target.IsBuiltIn)
                    throw new InvalidOperationException($"Built-in MOVEit Script '{target.Name}' cannot be overwritten by MAUI portability import.");
                if (!target.CanEditDelete)
                    throw new InvalidOperationException($"MOVEit reports that the current user cannot edit Script '{target.Name}'.");
                continue;
            }

            if (obj.ObjectType.Equals("DateList", StringComparison.OrdinalIgnoreCase))
            {
                var byId = inventory.DateLists.FirstOrDefault(item => item.Id.Equals(obj.SourceId, StringComparison.OrdinalIgnoreCase));
                var byName = inventory.DateLists.FirstOrDefault(item => item.Name.Equals(obj.SourceName, StringComparison.OrdinalIgnoreCase));
                ThrowIfAmbiguousOverwrite("Date List", obj.SourceName, byId?.Id, byName?.Id);
                var target = byId ?? byName ?? throw new InvalidOperationException($"The conflicting Date List '{obj.SourceName}' no longer exists in the destination environment. Analyze the package again.");
                ValidateExpectedConflictTarget(plan, target.Id);
                if (!target.CanEditDelete)
                    throw new InvalidOperationException($"MOVEit reports that the current user cannot edit Date List '{target.Name}'.");
                continue;
            }

            if (IsKeyType(obj.ObjectType))
            {
                var kind = KeyKindFromObjectType(obj.ObjectType);
                var byId = inventory.Keys.FirstOrDefault(item => item.Kind == kind && item.Id.Equals(obj.SourceId, StringComparison.OrdinalIgnoreCase));
                var byName = inventory.Keys.FirstOrDefault(item => item.Kind == kind && item.Name.Equals(obj.SourceName, StringComparison.OrdinalIgnoreCase));
                ThrowIfAmbiguousOverwrite(FriendlyObjectType(obj.ObjectType, obj.NativeSubtype), obj.SourceName, byId?.RowId, byName?.RowId);
                var target = byId ?? byName ?? throw new InvalidOperationException($"The conflicting {FriendlyObjectType(obj.ObjectType, obj.NativeSubtype)} '{obj.SourceName}' no longer exists in the destination environment. Analyze the package again.");
                ValidateExpectedConflictTarget(plan, target.RowId);
                if (!target.CanEditDelete)
                    throw new InvalidOperationException($"MOVEit reports that the current user cannot edit {FriendlyObjectType(obj.ObjectType, obj.NativeSubtype)} '{target.Name}'.");
            }
        }
    }

    private static void ThrowIfAmbiguousOverwrite(string objectType, string sourceName, string? idMatch, string? nameMatch)
    {
        if (string.IsNullOrWhiteSpace(idMatch) || string.IsNullOrWhiteSpace(nameMatch) || idMatch.Equals(nameMatch, StringComparison.OrdinalIgnoreCase)) return;
        throw new InvalidOperationException($"The imported {objectType} '{sourceName}' conflicts with different destination objects by ID and by Name. MAUI cannot safely choose an overwrite target; resolve the destination conflict and analyze the package again.");
    }

    private async Task CaptureUndoBeforeOverwriteAsync(ImportPlan plan, string username, CancellationToken cancellationToken)
    {
        var obj = plan.Object;
        var destinationId = (plan.ExpectedTargetId ?? string.Empty).Trim();
        if (destinationId.Length == 0 || IsKeyType(obj.ObjectType)) return;

        // Use the exact target identity carried from Analyze Package and revalidated before this point. Do not
        // re-resolve by name here; that could capture Undo for a replacement object created after analysis.
        if (obj.ObjectType.Equals("Task", StringComparison.OrdinalIgnoreCase)) await _taskUndo.CaptureAsync(destinationId, username, cancellationToken);
        else if (obj.ObjectType.Equals("Host", StringComparison.OrdinalIgnoreCase)) await _hostUndo.CaptureAsync(destinationId, username, cancellationToken);
        else if (obj.ObjectType.Equals("Script", StringComparison.OrdinalIgnoreCase))
        {
            var script = await _scripts.GetScriptAsync(destinationId, cancellationToken);
            if (!script.IsBuiltIn) await _scriptUndo.CaptureAsync(destinationId, username, cancellationToken);
        }
        else if (obj.ObjectType.Equals("DateList", StringComparison.OrdinalIgnoreCase)) await _dateListUndo.CaptureAsync(destinationId, username, cancellationToken);
    }

    private async Task<DestinationInventory> LoadDestinationInventoryAsync(CancellationToken cancellationToken)
    {
        var tasksTask = _tasks.GetTaskSnapshotAsync(true, cancellationToken);
        var hostsTask = _hosts.GetHostSnapshotAsync(true, cancellationToken);
        var scriptsTask = _scripts.GetScriptSnapshotAsync(true, cancellationToken);
        var dateListsTask = _dateLists.GetSnapshotAsync(true, cancellationToken);
        var keysTask = _keys.GetSnapshotAsync(true, cancellationToken);
        await Task.WhenAll(tasksTask, hostsTask, scriptsTask, dateListsTask, keysTask);
        return new DestinationInventory((await tasksTask).Tasks, (await hostsTask).Hosts, (await scriptsTask).Scripts, (await dateListsTask).DateLists, (await keysTask).Items);
    }

    private static string ResolveDestinationId(ImportPlan plan, DestinationInventory inventory)
    {
        var obj = plan.Object;
        var finalName = plan.FinalName;
        var preferSourceId = plan.Action.Equals("overwrite", StringComparison.OrdinalIgnoreCase);

        if (obj.ObjectType.Equals("Task", StringComparison.OrdinalIgnoreCase))
            return (preferSourceId ? inventory.Tasks.FirstOrDefault(item => item.Id.Equals(obj.SourceId, StringComparison.OrdinalIgnoreCase))?.Id : null)
                   ?? inventory.Tasks.FirstOrDefault(item => item.Name.Equals(finalName, StringComparison.OrdinalIgnoreCase))?.Id ?? string.Empty;
        if (obj.ObjectType.Equals("Host", StringComparison.OrdinalIgnoreCase))
            return (preferSourceId ? inventory.Hosts.FirstOrDefault(item => item.Id.Equals(obj.SourceId, StringComparison.OrdinalIgnoreCase))?.Id : null)
                   ?? inventory.Hosts.FirstOrDefault(item => item.Name.Equals(finalName, StringComparison.OrdinalIgnoreCase))?.Id ?? string.Empty;
        if (obj.ObjectType.Equals("Script", StringComparison.OrdinalIgnoreCase))
            return (preferSourceId ? inventory.Scripts.FirstOrDefault(item => item.Id.Equals(obj.SourceId, StringComparison.OrdinalIgnoreCase))?.Id : null)
                   ?? inventory.Scripts.FirstOrDefault(item => item.Name.Equals(finalName, StringComparison.OrdinalIgnoreCase))?.Id ?? string.Empty;
        if (obj.ObjectType.Equals("DateList", StringComparison.OrdinalIgnoreCase))
            return (preferSourceId ? inventory.DateLists.FirstOrDefault(item => item.Id.Equals(obj.SourceId, StringComparison.OrdinalIgnoreCase))?.Id : null)
                   ?? inventory.DateLists.FirstOrDefault(item => item.Name.Equals(finalName, StringComparison.OrdinalIgnoreCase))?.Id ?? string.Empty;
        if (IsKeyType(obj.ObjectType))
        {
            var kind = KeyKindFromObjectType(obj.ObjectType);
            var nativeId = obj.SourceId;
            var match = inventory.Keys.FirstOrDefault(item => item.Kind == kind && item.Id.Equals(nativeId, StringComparison.OrdinalIgnoreCase))
                        ?? inventory.Keys.FirstOrDefault(item => item.Kind == kind && item.Name.Equals(finalName, StringComparison.OrdinalIgnoreCase));
            return match?.RowId ?? string.Empty;
        }
        return string.Empty;
    }

    private async Task<Dictionary<string, HashSet<string>>> LoadUsedNamesAsync(CancellationToken cancellationToken)
        => BuildUsedNames(await LoadDestinationInventoryAsync(cancellationToken));

    private static Dictionary<string, HashSet<string>> BuildUsedNames(DestinationInventory destination)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["Task"] = destination.Tasks.Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
            ["Host"] = destination.Hosts.Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
            ["Script"] = destination.Scripts.Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
            ["DateList"] = destination.DateLists.Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
            ["SSHClientKey"] = destination.Keys.Where(item => item.Kind == KeyCertificateKind.Ssh).Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
            ["SSLClientCert"] = destination.Keys.Where(item => item.Kind == KeyCertificateKind.Tls).Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
            ["PGPKey"] = destination.Keys.Where(item => item.Kind == KeyCertificateKind.Pgp).Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase)
        };

    internal static string SuggestUniqueName(string objectType, string sourceName, Dictionary<string, HashSet<string>> usedNames)
    {
        const int maxNameLength = 254;
        var used = usedNames.GetOrAdd(objectType);
        var normalizedSource = (sourceName ?? string.Empty).Trim();
        var fallback = $"Imported_{(objectType ?? string.Empty).Trim()}";
        var stemSource = normalizedSource.Length == 0 ? fallback : normalizedSource;
        const string importedSuffix = "_Imported";

        static string Fit(string value, int maxLength)
            => value.Length <= maxLength ? value : value[..maxLength];

        var baseStemLength = Math.Max(1, maxNameLength - importedSuffix.Length);
        var baseName = Fit(stemSource, baseStemLength) + importedSuffix;
        if (!used.Contains(baseName)) return baseName;

        var index = 2;
        while (true)
        {
            var numericSuffix = $"_{index++}";
            var stemLength = Math.Max(1, maxNameLength - importedSuffix.Length - numericSuffix.Length);
            var candidate = Fit(stemSource, stemLength) + importedSuffix + numericSuffix;
            if (!used.Contains(candidate)) return candidate;
        }
    }

    private static XDocument ParseExportDocument(string xml)
    {
        XDocument document;
        try { document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace); }
        catch (Exception ex) { throw new InvalidOperationException("MOVEit returned invalid native export XML.", ex); }
        if (document.Root is null || !document.Root.Name.LocalName.Equals("Exported", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("MOVEit returned an unexpected native export document.");
        _ = FindRequiredContainer(document, "Tasks");
        _ = FindRequiredContainer(document, "Hosts");
        return document;
    }

    private static XElement FindRequiredContainer(XDocument document, string name)
        => document.Root?.Elements().FirstOrDefault(element => element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))
           ?? throw new InvalidOperationException($"MOVEit native export is missing the required {name} collection.");

    private static IReadOnlyList<NativeXmlDescriptor> EnumerateNativeXmlObjects(XDocument document)
    {
        var items = new List<NativeXmlDescriptor>();
        AddXmlItems(items, document, "Tasks", "Task", "Task", element => ((string?)element.Attribute("ID") ?? string.Empty).Trim(), element => ((string?)element.Attribute("Name") ?? string.Empty).Trim());
        var hosts = document.Root?.Elements().FirstOrDefault(element => element.Name.LocalName.Equals("Hosts", StringComparison.OrdinalIgnoreCase));
        if (hosts is not null)
            foreach (var element in hosts.Elements())
            {
                var id = ((string?)element.Attribute("ID") ?? string.Empty).Trim();
                var name = ((string?)element.Attribute("Name") ?? id).Trim();
                if (id.Length > 0) items.Add(new NativeXmlDescriptor("Host", "Host", id, id, name));
            }
        AddXmlItems(items, document, "Scripts", "Script", "Script", element => ((string?)element.Attribute("ID") ?? string.Empty).Trim(), element => ((string?)element.Attribute("Name") ?? string.Empty).Trim());
        AddXmlItems(items, document, "DateLists", "DateList", "DateList", element => ((string?)element.Attribute("ID") ?? string.Empty).Trim(), element => ((string?)element.Attribute("Name") ?? string.Empty).Trim());
        AddKeyXmlItems(items, document, "SSHClientKeys", "SSHClientKey", "ssh");
        AddKeyXmlItems(items, document, "SSLClientCerts", "SSLClientCert", "tls");
        AddKeyXmlItems(items, document, "PGPKeys", "PGPKey", "pgp");
        return items;
    }

    private static void AddXmlItems(List<NativeXmlDescriptor> target, XDocument document, string containerName, string elementName, string metadataType, Func<XElement, string> idSelector, Func<XElement, string> nameSelector)
    {
        var container = document.Root?.Elements().FirstOrDefault(element => element.Name.LocalName.Equals(containerName, StringComparison.OrdinalIgnoreCase));
        if (container is null) return;
        foreach (var element in container.Elements().Where(element => element.Name.LocalName.Equals(elementName, StringComparison.OrdinalIgnoreCase)))
        {
            var id = idSelector(element); if (id.Length == 0) continue;
            var name = nameSelector(element); if (name.Length == 0) name = id;
            target.Add(new NativeXmlDescriptor(metadataType, metadataType, id, id, name));
        }
    }

    private static void AddKeyXmlItems(List<NativeXmlDescriptor> target, XDocument document, string containerName, string elementName, string prefix)
    {
        var container = document.Root?.Elements().FirstOrDefault(element => element.Name.LocalName.Equals(containerName, StringComparison.OrdinalIgnoreCase));
        if (container is null) return;
        foreach (var element in container.Elements().Where(element => element.Name.LocalName.Equals(elementName, StringComparison.OrdinalIgnoreCase)))
        {
            var id = ((string?)element.Attribute("ID") ?? string.Empty).Trim();
            if (id.Length == 0) continue;
            var name = ((string?)element.Attribute("Name") ?? (string?)element.Attribute("DisplayName") ?? id).Trim();
            target.Add(new NativeXmlDescriptor("KeyCertificate", "KeyCertificate", $"{prefix}|{id}", $"{prefix}|{id}", name));
        }
    }

    private static IReadOnlyList<ImportObjectRef> EnumerateImportObjects(JsonObject import)
    {
        var items = new List<ImportObjectRef>();
        AddImportArray(items, import["Tasks"] as JsonObject, "Task", "Task");
        var hosts = import["Hosts"] as JsonObject;
        if (hosts?["hosts"] is JsonArray hostArray)
            foreach (var wrapper in hostArray.OfType<JsonObject>())
            {
                var pair = wrapper.FirstOrDefault(entry => entry.Value is JsonObject);
                if (pair.Value is not JsonObject host) continue;
                var id = GetString(host, "ID"); if (id.Length == 0) continue;
                var name = GetString(host, "Name"); if (name.Length == 0) name = id;
                items.Add(new ImportObjectRef("Host", pair.Key, id, name, host));
            }
        AddImportArray(items, import["Scripts"] as JsonObject, "Script", "Script");
        AddImportArray(items, import["DateLists"] as JsonObject, "DateList", "DateList");
        AddImportArray(items, import["SSHClientKeys"] as JsonObject, "SSHClientKey", "SSHClientKey");
        AddImportArray(items, import["SSLClientCerts"] as JsonObject, "SSLClientCert", "SSLClientCert");
        AddImportArray(items, import["PGPKeys"] as JsonObject, "PGPKey", "PGPKey");
        return items;
    }

    private static void AddImportArray(List<ImportObjectRef> target, JsonObject? container, string arrayName, string objectType)
    {
        if (container?[arrayName] is not JsonArray array) return;
        foreach (var node in array.OfType<JsonObject>())
        {
            var id = GetString(node, "ID"); if (id.Length == 0) continue;
            var name = GetString(node, "Name");
            if (name.Length == 0) name = GetString(node, "displayName");
            if (name.Length == 0) name = id;
            target.Add(new ImportObjectRef(objectType, objectType, id, name, node));
        }
    }

    private static IReadOnlyList<DuplicateRef> ReadDuplicates(JsonObject import)
    {
        var result = new List<DuplicateRef>();
        if (import["Duplicated"] is not JsonArray duplicates) return result;
        foreach (var node in duplicates.OfType<JsonObject>())
        {
            var id = GetString(node, "ID");
            var type = GetString(node, "Type");
            var reasons = node["Reason"] is JsonArray reasonArray
                ? reasonArray.Select(value => value?.ToString() ?? string.Empty).Where(value => value.Length > 0).ToList()
                : new List<string>();
            if (id.Length > 0 && type.Length > 0) result.Add(new DuplicateRef(type, id, reasons));
        }
        return result;
    }

    private static IReadOnlyList<DuplicateRef> MergeKeyInventoryDuplicates(
        IReadOnlyList<DuplicateRef> moveItDuplicates,
        IReadOnlyList<ImportObjectRef> importObjects,
        DestinationInventory inventory)
    {
        var merged = moveItDuplicates.ToList();
        foreach (var obj in importObjects.Where(item => IsKeyType(item.ObjectType)))
        {
            var kind = KeyKindFromObjectType(obj.ObjectType);
            var byId = inventory.Keys.FirstOrDefault(item =>
                item.Kind == kind && item.Id.Equals(obj.SourceId, StringComparison.OrdinalIgnoreCase));
            var byName = inventory.Keys.FirstOrDefault(item =>
                item.Kind == kind && item.Name.Equals(obj.SourceName, StringComparison.OrdinalIgnoreCase));

            if (byId is not null && byName is not null && !byId.RowId.Equals(byName.RowId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The imported {FriendlyObjectType(obj.ObjectType, obj.NativeSubtype)} '{obj.SourceName}' matches one destination object by ID and a different destination object by Name. " +
                    "MAUI cannot safely choose an overwrite target. Resolve the destination conflict and analyze the package again.");
            }

            if (byId is null && byName is null) continue;

            var reasons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var moveItDuplicate = FindDuplicate(merged, obj);
            if (moveItDuplicate is not null)
                foreach (var reason in moveItDuplicate.Reasons)
                    if (!string.IsNullOrWhiteSpace(reason)) reasons.Add(reason);
            if (byId is not null) reasons.Add("id");
            if (byName is not null) reasons.Add("name");

            // Add an MAUI-normalized duplicate record even when MOVEit did not identify the dependency itself.
            // FindDuplicate() prefers this exact type/ID pair on subsequent calls, so Analyze and Import use the
            // same live destination evidence and key/certificate conflicts cannot fall through as create-new.
            merged.RemoveAll(item =>
                item.Id.Equals(obj.SourceId, StringComparison.OrdinalIgnoreCase) &&
                NormalizeDuplicateType(item.Type).Equals(NormalizeDuplicateType(obj.ObjectType), StringComparison.OrdinalIgnoreCase));
            merged.Insert(0, new DuplicateRef(obj.ObjectType, obj.SourceId, reasons.ToList()));
        }
        return merged;
    }

    private static DuplicateRef? FindDuplicate(IReadOnlyList<DuplicateRef> duplicates, ImportObjectRef obj)
    {
        var matched = duplicates.FirstOrDefault(item => item.Id.Equals(obj.SourceId, StringComparison.OrdinalIgnoreCase)
            && NormalizeDuplicateType(item.Type).Equals(NormalizeDuplicateType(obj.ObjectType), StringComparison.OrdinalIgnoreCase));
        if (matched is not null) return matched;

        // MOVEit's decoded object model also carries a per-object duplicate marker. Use it as a conservative
        // fallback so dependency conflicts are not missed if MOVEit uses a Type label not yet observed by MAUI.
        if (obj.Node["duplicate"] is JsonArray nativeReasons)
        {
            var reasons = nativeReasons
                .Select(value => value?.ToString() ?? string.Empty)
                .Where(value => value.Length > 0)
                .ToList();
            if (reasons.Count > 0) return new DuplicateRef(obj.ObjectType, obj.SourceId, reasons);
        }
        return null;
    }

    private static string NormalizeDuplicateType(string value)
    {
        var type = (value ?? string.Empty).Trim();
        if (type.Equals("Host", StringComparison.OrdinalIgnoreCase)) return "Host";
        if (type.Equals("Task", StringComparison.OrdinalIgnoreCase)) return "Task";
        if (type.Equals("Script", StringComparison.OrdinalIgnoreCase)) return "Script";
        if (type.Equals("DateList", StringComparison.OrdinalIgnoreCase) || type.Equals("Date List", StringComparison.OrdinalIgnoreCase)) return "DateList";
        if (type.Contains("PGP", StringComparison.OrdinalIgnoreCase)) return "PGPKey";
        if (type.Contains("SSH", StringComparison.OrdinalIgnoreCase) && type.Contains("Key", StringComparison.OrdinalIgnoreCase)) return "SSHClientKey";
        if (type.Contains("SSL", StringComparison.OrdinalIgnoreCase) || type.Contains("TLS", StringComparison.OrdinalIgnoreCase)) return "SSLClientCert";
        return type;
    }

    internal static string BuildHostOnlyNativeExport(string exportedXml, IReadOnlyList<AutomationHost> requestedHosts)
    {
        var document = ParseExportDocument(exportedXml);
        var tasksElement = FindRequiredContainer(document, "Tasks");
        // Host-only native imports were validated against MOVEit with an explicit empty <Tasks></Tasks> element
        // before <Hosts>. Remove the temporary carrier completely, including any container attributes MOVEit may
        // add in a future version, so MAUI preserves that exact required shape.
        tasksElement.RemoveAttributes();
        tasksElement.RemoveNodes();

        var exportedHostIds = FindRequiredContainer(document, "Hosts").Elements()
            .Select(element => ((string?)element.Attribute("ID") ?? string.Empty).Trim())
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = requestedHosts.Where(host => !exportedHostIds.Contains(host.Id)).Select(host => host.Name).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException($"MOVEit did not include the requested Host dependencies in the native carrier export: {string.Join(", ", missing)}. No package was created.");

        var finalXml = document.ToString(SaveOptions.DisableFormatting)
            .Replace("<Tasks />", "<Tasks></Tasks>", StringComparison.Ordinal)
            .Replace("<Tasks/>", "<Tasks></Tasks>", StringComparison.Ordinal);
        if (!finalXml.Contains("<Tasks></Tasks>", StringComparison.Ordinal))
            throw new InvalidOperationException("MAUI could not preserve the empty Tasks container required for native Host import.");
        return finalXml;
    }

    internal static string BuildHostCarrierTaskXml(string name, IReadOnlyList<AutomationHost> hosts)
    {
        var carrierName = (name ?? string.Empty).Trim();
        if (carrierName.Length == 0) throw new InvalidOperationException("A temporary Host-export carrier Task name is required.");
        if (hosts is null || hosts.Count == 0) throw new InvalidOperationException("Select at least one Host for native Host export.");

        var normalizedHosts = new List<(string Id, string TypeCode)>(hosts.Count);
        var seenHostIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < hosts.Count; index++)
        {
            var host = hosts[index];
            if (host is null) throw new InvalidOperationException($"The Host export selection contains an invalid Host at position {index + 1}.");
            var hostId = (host.Id ?? string.Empty).Trim();
            var typeCode = (host.TypeCode ?? string.Empty).Trim();
            if (hostId.Length == 0) throw new InvalidOperationException($"The Host export selection contains a Host without an ID at position {index + 1}.");
            if (typeCode.Length == 0) throw new InvalidOperationException($"Host '{host.Name}' does not have a native MOVEit Host type code and cannot be exported.");
            if (!IsSupportedHostCarrierType(typeCode))
                throw new InvalidOperationException($"Host '{host.Name}' uses MOVEit Host type '{typeCode}', which does not yet have a verified MAUI native carrier-step contract.");
            if (!seenHostIds.Add(hostId)) throw new InvalidOperationException($"Host ID '{hostId}' was selected more than once for export.");
            normalizedHosts.Add((hostId, typeCode));
        }

        var task = new XElement("Task",
            new XAttribute("ID", "0"),
            new XAttribute("Name", carrierName),
            new XAttribute("Active", "0"),
            new XAttribute("NextEID", normalizedHosts.Count + 1),
            new XAttribute("AR", "0"),
            new XAttribute("CacheNames", "random"),
            new XAttribute("UseDefStateCaching", "1"),
            new XElement("Schedules"),
            new XElement("NextActions"),
            new XElement("Info",
                new XElement("Description", "Temporary MAUI Host export carrier. Safe to delete if left behind after a failed export."),
                new XElement("Notes", "Created automatically by MAUI for native Host portability export.")));

        var stepId = 1;
        foreach (var host in normalizedHosts)
        {
            // Native MOVEit does not support SMTP as a Source. POP3 is Source-only. Other transfer Host types are
            // represented as a Source using the common Traditional Task fields MOVEit emits in native exports.
            // The carrier is disabled and never run; these steps exist only so /api/v0/config/export discovers the Hosts.
            if (host.TypeCode.Equals("SMTP", StringComparison.OrdinalIgnoreCase))
            {
                task.Add(new XElement("Destination",
                    new XAttribute("BlindUL", "0"),
                    new XAttribute("FileName", "[OrigName]"),
                    new XAttribute("ForceDir", "1"),
                    new XAttribute("OverwriteOrig", "1"),
                    new XAttribute("UseDefBlindUL", "1"),
                    new XAttribute("UseOrigName", "1"),
                    new XAttribute("UseRelativeSubdirs", "1"),
                    new XAttribute("Zip", "0"),
                    new XAttribute("ClientKeyID", "0"),
                    new XAttribute("ConnTimeoutSecs", "120"),
                    new XAttribute("DestFileAttr", string.Empty),
                    new XAttribute("HostID", host.Id),
                    new XAttribute("Path", string.Empty),
                    new XAttribute("Rsm", "0"),
                    new XAttribute("PreserveFileTimestamp", "0"),
                    new XAttribute("SetFileAttr", "0"),
                    new XAttribute("Type", host.TypeCode),
                    new XAttribute("UseDefClientKey", "1"),
                    new XAttribute("UseDefConnTimeoutSecs", "1"),
                    new XAttribute("UseDefSetFileAttr", "1"),
                    new XAttribute("UseDefRetryCount", "1"),
                    new XAttribute("UseDefRetryTimeoutSecs", "1"),
                    new XAttribute("UseDefRsm", "1"),
                    new XAttribute("UseDefPreserveFileTimestamp", "1"),
                    new XAttribute("UseDefUser", "1"),
                    new XAttribute("Username", string.Empty),
                    new XAttribute("InvalidOverride", "0"),
                    new XAttribute("RetryCount", "0"),
                    new XAttribute("RetryTimeoutSecs", "30"),
                    new XAttribute("Disabled", "0"),
                    new XAttribute("ID", stepId++)));
            }
            else
            {
                task.Add(new XElement("Source",
                    new XAttribute("DeleteOrig", "0"),
                    new XAttribute("DelRename", "1"),
                    new XAttribute("ExFile", string.Empty),
                    new XAttribute("ExFo", string.Empty),
                    new XAttribute("FileMask", "*.*"),
                    new XAttribute("NewFilesOnly", "0"),
                    new XAttribute("RenameTo", string.Empty),
                    new XAttribute("Unzip", "0"),
                    new XAttribute("BlindDL", "0"),
                    new XAttribute("ClientKeyID", "0"),
                    new XAttribute("ConnTimeoutSecs", "120"),
                    new XAttribute("HostID", host.Id),
                    new XAttribute("MxBy", "0"),
                    new XAttribute("MxFi", "0"),
                    new XAttribute("MD5File", "MD5SUM"),
                    new XAttribute("MD5Get", "Never"),
                    new XAttribute("Path", string.Empty),
                    new XAttribute("RescanSecs", "0"),
                    new XAttribute("RetryIfNoFiles", "0"),
                    new XAttribute("Rsm", "0"),
                    new XAttribute("SearchSubdirs", "0"),
                    new XAttribute("Type", host.TypeCode),
                    new XAttribute("UDMxBy", "1"),
                    new XAttribute("UDMxFi", "1"),
                    new XAttribute("UseDefBlindDL", "1"),
                    new XAttribute("UseDefClientKey", "1"),
                    new XAttribute("UseDefConnTimeoutSecs", "1"),
                    new XAttribute("UseDefMD5File", "1"),
                    new XAttribute("UseDefMD5Get", "1"),
                    new XAttribute("UseDefRescanSecs", "1"),
                    new XAttribute("UseDefRetryCount", "1"),
                    new XAttribute("UseDefRetryTimeoutSecs", "1"),
                    new XAttribute("UseDefRsm", "1"),
                    new XAttribute("UseDefUser", "1"),
                    new XAttribute("Username", string.Empty),
                    new XAttribute("InvalidOverride", "0"),
                    new XAttribute("RetryCount", "0"),
                    new XAttribute("RetryTimeoutSecs", "30"),
                    new XAttribute("Disabled", "0"),
                    new XAttribute("ID", stepId++)));
            }
        }
        return task.ToString(SaveOptions.DisableFormatting);
    }


    private static bool IsSupportedHostCarrierType(string? typeCode)
    {
        var value = (typeCode ?? string.Empty).Trim();
        return value.Equals("Share", StringComparison.OrdinalIgnoreCase)
               || value.Equals("siLock", StringComparison.OrdinalIgnoreCase)
               || value.Equals("FTP", StringComparison.OrdinalIgnoreCase)
               || value.Equals("SSHFTP", StringComparison.OrdinalIgnoreCase)
               || value.Equals("POP3", StringComparison.OrdinalIgnoreCase)
               || value.Equals("SMTP", StringComparison.OrdinalIgnoreCase)
               || value.Equals("S3", StringComparison.OrdinalIgnoreCase)
               || value.Equals("AzureBlob", StringComparison.OrdinalIgnoreCase)
               || value.Equals("SharePoint", StringComparison.OrdinalIgnoreCase)
               || value.Equals("GoogleStorage", StringComparison.OrdinalIgnoreCase);
    }

    private static string TaskTypeFromCode(string? code)
    {
        var normalized = (code ?? string.Empty).Trim();
        return normalized.Equals("COS", StringComparison.OrdinalIgnoreCase) ? "Advanced"
            : normalized.Equals("Sy", StringComparison.OrdinalIgnoreCase) ? "Synchronization"
            : "Traditional";
    }

    private static string NormalizeProtection(string mode, string passphrase)
    {
        var value = (mode ?? string.Empty).Trim();
        if (value.Equals("server-key", StringComparison.OrdinalIgnoreCase) || value.Equals("serverkey", StringComparison.OrdinalIgnoreCase)) return "server-key";
        if (value.Equals("passphrase", StringComparison.OrdinalIgnoreCase) || value.Equals("portable", StringComparison.OrdinalIgnoreCase))
        {
            ValidatePassphrasePolicy(passphrase, "Portable export passphrase");
            return "passphrase";
        }
        throw new InvalidOperationException("Choose Server Key or Portable / Passphrase protection.");
    }

    private static void ValidatePassphrase(MauiNativePortablePackage.Package package, string passphrase)
    {
        if (package.Manifest.ProtectionMode != "passphrase") return;
        if (string.IsNullOrWhiteSpace(passphrase))
            throw new InvalidOperationException("This portable package is protected by a passphrase. Enter the export passphrase and try again.");
        ValidatePassphrasePolicy(passphrase, "Portable package passphrase");
    }

    private static void ValidatePassphrasePolicy(string passphrase, string label)
    {
        var error = PortabilitySupport.PassphrasePolicyError(passphrase, label);
        if (error.Length > 0) throw new InvalidOperationException(error);
    }

    private static IReadOnlyList<string> NormalizeIds(IReadOnlyList<string> values, string label)
    {
        var ids = (values ?? Array.Empty<string>()).Select(value => (value ?? string.Empty).Trim()).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ids.Count == 0) throw new InvalidOperationException($"Select at least one {label}.");
        return ids;
    }

    private MoveItSessionState RequireSession() => _session.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");

    private static void ValidateDecodedBoundary(MauiNativePortablePackage.Package package, IReadOnlyList<ImportObjectRef> decodedObjects)
    {
        if (package.Manifest.PackageType.Equals("Host", StringComparison.OrdinalIgnoreCase)
            && decodedObjects.Any(item => item.ObjectType.Equals("Task", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("The Host portability package unexpectedly decoded one or more Tasks. Host imports must contain an empty Tasks collection.");
        }

        var decodedSelectedKeys = decodedObjects
            .Where(item => item.ObjectType.Equals(package.Manifest.PackageType, StringComparison.OrdinalIgnoreCase))
            .Select(item => MetadataKey(item.ObjectType, item.SourceId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var selected in package.Manifest.SelectedItems)
        {
            if (!decodedSelectedKeys.Contains(MetadataKey(selected.Type, selected.SourceId)))
            {
                throw new InvalidOperationException($"MOVEit did not decode selected {selected.Type} '{selected.SourceName}' (ID {selected.SourceId}) from the portability package.");
            }
        }
    }

    internal static void ApplyRenameResolution(JsonObject node, string finalName)
    {
        ArgumentNullException.ThrowIfNull(node);
        var normalizedName = (finalName ?? string.Empty).Trim();
        if (normalizedName.Length == 0) throw new ArgumentException("A renamed native import object requires a non-empty name.", nameof(finalName));
        node["Name"] = normalizedName;
        node["displayName"] = normalizedName;
        node["duplicate"] = null;
        node["Renamed"] = true;
        node["new"] = true;
    }

    private static string RemoveUseExistingKeyDependencies(string nativeXml, IReadOnlyList<ImportObjectRef> useExistingKeys)
    {
        if (useExistingKeys is null || useExistingKeys.Count == 0) return nativeXml;

        var document = ParseExportDocument(nativeXml);
        var root = document.Root ?? throw new InvalidOperationException("The native MOVEit export has an unexpected root element.");
        foreach (var obj in useExistingKeys)
        {
            var (containerName, elementName) = obj.ObjectType switch
            {
                "PGPKey" => ("PGPKeys", "PGPKey"),
                "SSHClientKey" => ("SSHClientKeys", "SSHClientKey"),
                "SSLClientCert" => ("SSLClientCerts", "SSLClientCert"),
                _ => throw new InvalidOperationException("Use Existing is only supported for key/certificate dependencies.")
            };

            var container = root.Elements().FirstOrDefault(element => element.Name.LocalName.Equals(containerName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"The native MOVEit export is missing its {containerName} collection.");
            var matches = container.Elements()
                .Where(element => element.Name.LocalName.Equals(elementName, StringComparison.OrdinalIgnoreCase)
                    && (((string?)element.Attribute("ID") ?? string.Empty).Trim()).Equals(obj.SourceId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count != 1)
                throw new InvalidOperationException($"MAUI could not uniquely identify {FriendlyObjectType(obj.ObjectType, obj.NativeSubtype)} ID '{obj.SourceId}' in the native export for Use Existing.");
            matches[0].Remove();
        }

        // Host packages require an explicit empty Tasks element before Hosts. XDocument may self-close it when
        // serializing the working XML, so preserve the validated native Host-import contract.
        return document.ToString(SaveOptions.DisableFormatting)
            .Replace("<Tasks />", "<Tasks></Tasks>", StringComparison.Ordinal)
            .Replace("<Tasks/>", "<Tasks></Tasks>", StringComparison.Ordinal);
    }

    private static bool CanUseExistingKey(ImportObjectRef obj, DuplicateRef duplicate, DestinationInventory inventory)
    {
        if (!IsKeyType(obj.ObjectType) || !duplicate.Reasons.Any(reason => reason.Equals("id", StringComparison.OrdinalIgnoreCase)))
            return false;

        var kind = KeyKindFromObjectType(obj.ObjectType);
        return inventory.Keys.Any(item =>
            item.Kind == kind && item.Id.Equals(obj.SourceId, StringComparison.OrdinalIgnoreCase));
    }

    private static string ConflictResolutionRequiredMessage(ImportObjectRef obj, DuplicateRef duplicate)
    {
        if (IsKeyType(obj.ObjectType) && duplicate.Reasons.Any(reason => reason.Equals("id", StringComparison.OrdinalIgnoreCase)))
            return $"Choose Use Existing or Overwrite Existing for conflicting {FriendlyObjectType(obj.ObjectType, obj.NativeSubtype)} '{obj.SourceName}'.";
        if (CanRename(obj))
            return $"Choose Overwrite Existing or Import as New for conflicting {FriendlyObjectType(obj.ObjectType, obj.NativeSubtype)} '{obj.SourceName}'.";
        return $"Choose Overwrite Existing for conflicting {FriendlyObjectType(obj.ObjectType, obj.NativeSubtype)} '{obj.SourceName}'.";
    }

    private static string NormalizeResolutionAction(string? value)
    {
        var action = (value ?? string.Empty).Trim();
        if (action.Equals("useexisting", StringComparison.OrdinalIgnoreCase) || action.Equals("use-existing", StringComparison.OrdinalIgnoreCase)) return "useexisting";
        if (action.Equals("overwrite", StringComparison.OrdinalIgnoreCase)) return "overwrite";
        if (action.Equals("rename", StringComparison.OrdinalIgnoreCase) || action.Equals("new", StringComparison.OrdinalIgnoreCase)) return "rename";
        throw new InvalidOperationException("Conflict resolution must be Use Existing, Overwrite Existing, or Import as New where supported.");
    }

    private static string NormalizeImportName(string? value, string objectType)
    {
        var name = (value ?? string.Empty).Trim();
        if (name.Length == 0) throw new InvalidOperationException($"A new name is required when importing a conflicting {FriendlyObjectType(objectType, objectType)} as new.");
        if (name.Length > 254) throw new InvalidOperationException("Imported object names cannot exceed 254 characters.");
        return name;
    }

    private static bool CanRename(ImportObjectRef obj)
        // The native Rename-as-new payload shape has been captured and verified for Tasks and Hosts.
        // Dependency object rename semantics vary by MOVEit resource type, so MAUI deliberately does not
        // offer rename for dependency conflicts. Key/certificate ID conflicts may use the existing object;
        // other dependency conflicts remain overwrite-only until their native contracts are captured.
        => obj.ObjectType.Equals("Task", StringComparison.OrdinalIgnoreCase)
           || obj.ObjectType.Equals("Host", StringComparison.OrdinalIgnoreCase);

    private static bool IsKeyType(string objectType)
        => objectType.Equals("SSHClientKey", StringComparison.OrdinalIgnoreCase)
           || objectType.Equals("SSLClientCert", StringComparison.OrdinalIgnoreCase)
           || objectType.Equals("PGPKey", StringComparison.OrdinalIgnoreCase);

    private static KeyCertificateKind KeyKindFromObjectType(string objectType)
        => objectType.Equals("PGPKey", StringComparison.OrdinalIgnoreCase) ? KeyCertificateKind.Pgp
            : objectType.Equals("SSLClientCert", StringComparison.OrdinalIgnoreCase) ? KeyCertificateKind.Tls
            : KeyCertificateKind.Ssh;

    private static string ToMetadataObjectType(string objectType)
        => IsKeyType(objectType) ? "KeyCertificate" : objectType;

    private static string MetadataSourceId(ImportObjectRef obj)
    {
        if (!IsKeyType(obj.ObjectType)) return obj.SourceId;
        var prefix = obj.ObjectType.Equals("PGPKey", StringComparison.OrdinalIgnoreCase) ? "pgp"
            : obj.ObjectType.Equals("SSLClientCert", StringComparison.OrdinalIgnoreCase) ? "tls" : "ssh";
        return $"{prefix}|{obj.SourceId}";
    }

    internal static Dictionary<string, NativePortabilityConflictResolution> BuildResolutionMap(IReadOnlyList<NativePortabilityConflictResolution>? resolutions)
    {
        var map = new Dictionary<string, NativePortabilityConflictResolution>(StringComparer.OrdinalIgnoreCase);
        foreach (var resolution in resolutions ?? Array.Empty<NativePortabilityConflictResolution>())
        {
            if (resolution is null)
                throw new InvalidOperationException("The import conflict-resolution request contains an invalid entry.");

            var objectType = (resolution.ObjectType ?? string.Empty).Trim();
            var sourceId = (resolution.SourceId ?? string.Empty).Trim();
            if (objectType.Length == 0 || sourceId.Length == 0)
                throw new InvalidOperationException("Each import conflict resolution must identify an object type and source ID.");

            _ = NormalizeResolutionAction(resolution.Action);
            var key = MetadataKey(objectType, sourceId);
            if (!map.TryAdd(key, resolution))
                throw new InvalidOperationException($"The import conflict-resolution request contains more than one resolution for {objectType} ID '{sourceId}'.");
        }
        return map;
    }

    private static string MetadataKey(string? objectType, string? sourceId) => $"{(objectType ?? string.Empty).Trim()}|{(sourceId ?? string.Empty).Trim()}";
    private static string GetString(JsonObject node, string name) => node[name]?.ToString()?.Trim() ?? string.Empty;
    private static string FriendlyObjectType(string objectType, string subtype)
    {
        if (objectType.Equals("Host", StringComparison.OrdinalIgnoreCase)) return $"Host ({subtype})";
        if (objectType.Equals("DateList", StringComparison.OrdinalIgnoreCase)) return "Date List";
        if (objectType.Equals("SSHClientKey", StringComparison.OrdinalIgnoreCase)) return "SSH Client Key";
        if (objectType.Equals("SSLClientCert", StringComparison.OrdinalIgnoreCase)) return "TLS Certificate";
        if (objectType.Equals("PGPKey", StringComparison.OrdinalIgnoreCase)) return "PGP Key";
        return objectType;
    }

    private static string SafeMoveItDiagnostic(string? message)
    {
        var text = (message ?? string.Empty).Trim();
        if (text.Length == 0) return "No additional MOVEit error detail was returned.";

        // Portability diagnostics must never place native configuration XML, decoded import JSON, encrypted
        // credentials, or other opaque key material into MAUI logs. Preserve short validation prose only.
        if (text.Contains("<Exported", StringComparison.OrdinalIgnoreCase)
            || text.Contains("<Settings", StringComparison.OrdinalIgnoreCase)
            || text.Contains("<PGPKeys", StringComparison.OrdinalIgnoreCase)
            || text.Contains("<SSHClientKeys", StringComparison.OrdinalIgnoreCase)
            || text.Contains("<SSLClientCerts", StringComparison.OrdinalIgnoreCase)
            || text.Contains("\"Imported\"", StringComparison.OrdinalIgnoreCase)
            || text.Contains("\"fileContents\"", StringComparison.OrdinalIgnoreCase)
            || text.Contains("DefPassword", StringComparison.OrdinalIgnoreCase)
            || text.Contains("privatekey", StringComparison.OrdinalIgnoreCase)
            || text.Contains("keymaterial", StringComparison.OrdinalIgnoreCase))
            return "MOVEit returned configuration-bearing error content; detail was suppressed.";

        var normalized = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < tokens.Length; index++)
        {
            if (tokens[index].Length >= 64 || tokens[index].StartsWith("4gAAAA", StringComparison.OrdinalIgnoreCase))
                tokens[index] = "[redacted]";
        }

        normalized = string.Join(" ", tokens);
        return normalized.Length <= 1024 ? normalized : normalized[..1024];
    }

    private static string JoinWarnings(string first, string second)
        => string.IsNullOrWhiteSpace(first) ? second : string.IsNullOrWhiteSpace(second) ? first : $"{first} {second}";

    private sealed record NativeXmlDescriptor(string NativeObjectType, string MetadataObjectType, string SourceId, string MetadataObjectId, string SourceName);
    private sealed record ImportObjectRef(string ObjectType, string NativeSubtype, string SourceId, string SourceName, JsonObject Node);
    private sealed record DuplicateRef(string Type, string Id, IReadOnlyList<string> Reasons);
    private sealed record ImportPlan(ImportObjectRef Object, string Action, string FinalName, DuplicateRef? Duplicate, string ExpectedTargetId);
    private sealed record UseExistingKeyPlan(ImportObjectRef Object, string ExpectedTargetId);
    private sealed record DestinationInventory(IReadOnlyList<AutomationTask> Tasks, IReadOnlyList<AutomationHost> Hosts, IReadOnlyList<AutomationScript> Scripts, IReadOnlyList<AutomationDateList> DateLists, IReadOnlyList<AutomationKeyCertificate> Keys);
}

internal static class NativePortabilityDictionaryExtensions
{
    public static HashSet<string> GetOrAdd(this Dictionary<string, HashSet<string>> dictionary, string key)
    {
        if (dictionary.TryGetValue(key, out var existing)) return existing;
        var created = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        dictionary[key] = created;
        return created;
    }
}
