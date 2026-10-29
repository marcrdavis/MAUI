// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Xml.Linq;
using MoveItAutomationUI.Services.Metadata;

namespace MoveItAutomationUI.Services.MoveIt;

public interface ITaskPortabilityService
{
    // Purpose: Clones one Task through MAUI's internal in-memory snapshot/import pipeline.
    // User-facing Task .maui import/export is owned exclusively by IMauiNativePortabilityService.
    Task<TaskImportResult> CloneAsync(string taskId, string newTaskName, string username, CancellationToken cancellationToken = default);
}

public sealed record TaskImportDependencySummary(int Referenced, int Found, IReadOnlyList<string> MissingIds);
public sealed record TaskImportTarget(string Id, string Name, string Type, bool Running, bool CanEdit);
public sealed record TaskImportItemPreflight(
    string SourceTaskId,
    string SourceTaskName,
    string TaskType,
    string SuggestedTaskName,
    bool SuggestedNameAvailable,
    string PlannedAction,
    string TargetTaskId,
    TaskImportDependencySummary Hosts,
    TaskImportDependencySummary Scripts,
    TaskImportDependencySummary DateLists,
    MetadataImportSummary Metadata,
    string Message);
public sealed record TaskImportPreflight(
    bool Valid,
    string SourceTaskId,
    string SourceTaskName,
    string TaskType,
    DateTimeOffset? ExportedUtc,
    string SuggestedTaskName,
    bool SuggestedNameAvailable,
    TaskImportDependencySummary Hosts,
    TaskImportDependencySummary Scripts,
    TaskImportDependencySummary DateLists,
    IReadOnlyList<TaskImportTarget> Targets,
    MetadataImportSummary Metadata,
    string Message,
    int ItemCount,
    bool IsMultiItem,
    int CreateCount,
    int OverwriteCount,
    IReadOnlyList<TaskImportItemPreflight> Items);
public sealed record TaskImportItemResult(string SourceTaskName, string TaskId, string TaskName, bool Success, bool Created, string Warning, string Error);
public sealed record TaskImportResult(
    string TaskId,
    string TaskName,
    bool Created,
    string Warning,
    int ItemCount,
    int ImportedCount,
    int CreatedCount,
    int OverwrittenCount,
    int FailedCount,
    IReadOnlyList<TaskImportItemResult> Items);

public sealed class TaskPortabilityService : ITaskPortabilityService
{
    private readonly IAutomationTaskService _tasks;
    private readonly IAutomationHostService _hosts;
    private readonly IAutomationScriptService _scripts;
    private readonly IAutomationDateListService _dateLists;
    private readonly ITaskUndoService _undo;
    private readonly IMetadataRepository _metadata;
    private readonly IMauiMetadataPortabilityService _portableMetadata;
    private readonly IMoveItSessionService _session;
    private readonly ILogger<TaskPortabilityService> _logger;

    // Purpose: Initializes task portability service with its required dependencies.
    public TaskPortabilityService(
        IAutomationTaskService tasks,
        IAutomationHostService hosts,
        IAutomationScriptService scripts,
        IAutomationDateListService dateLists,
        ITaskUndoService undo,
        IMetadataRepository metadata,
        IMauiMetadataPortabilityService portableMetadata,
        IMoveItSessionService session,
        ILogger<TaskPortabilityService> logger)
    {
        _tasks = tasks;
        _hosts = hosts;
        _scripts = scripts;
        _dateLists = dateLists;
        _undo = undo;
        _metadata = metadata;
        _portableMetadata = portableMetadata;
        _session = session;
        _logger = logger;
    }

    // Purpose: Builds the internal single-Task snapshot package used only by Clone.
    // This byte stream is never exposed as a user-downloadable .maui file.
    private async Task<byte[]> BuildCloneSnapshotPackageAsync(string taskId, CancellationToken cancellationToken)
    {
        var id = PortabilitySupport.NormalizeId(taskId, "Task");
        var session = RequireSession();
        var snapshot = await _tasks.GetTaskConfigurationSnapshotAsync(id, cancellationToken);
        var parsed = ParseTaskXml(snapshot.Xml);
        var metadata = await _portableMetadata.BuildExportElementAsync(session.GetEnvironmentKey(), "Task", snapshot.Id, cancellationToken);
        var item = MauiPortablePackage.BuildItem(snapshot.Id, snapshot.Name, parsed.TaskType, parsed.Root, metadata);
        return MauiPortablePackage.Build("Task", new[] { item }, DateTimeOffset.UtcNow);
    }

    // Purpose: Clones one Task through an internal in-memory snapshot/import pipeline.
    // The clone is deliberately created disabled so a copied schedule cannot immediately execute alongside its source.
    // User-facing Task portability is handled only by the native MOVEit .maui workflow.
    public async Task<TaskImportResult> CloneAsync(string taskId, string newTaskName, string username, CancellationToken cancellationToken = default)
    {
        var sourceId = PortabilitySupport.NormalizeId(taskId, "Task");
        var cloneName = NormalizeTaskName(newTaskName);
        var clonePackage = RenameSingleTaskPackage(await BuildCloneSnapshotPackageAsync(sourceId, cancellationToken), cloneName);

        // Perform the same live create preflight used by normal imports so duplicate names and dependency
        // problems are detected before COM is asked to create the cloned Task.
        var preflight = await PreflightImportAsync(clonePackage, "create", cancellationToken);
        if (!preflight.SuggestedNameAvailable)
            throw new TaskNameAlreadyExistsException(cloneName);
        if (!string.IsNullOrWhiteSpace(preflight.Message))
            throw new InvalidOperationException(preflight.Message);

        return await ImportAsync(
            clonePackage,
            "create",
            leaveDisabled: true,
            acknowledgeMissingDependencies: false,
            acknowledgeUnsupportedMetadata: false,
            username: username,
            cancellationToken: cancellationToken);
    }

    // Purpose: Validates one or many imported Tasks and returns the complete import plan.
    private async Task<TaskImportPreflight> PreflightImportAsync(byte[] fileBytes, string mode, CancellationToken cancellationToken = default)
    {
        var session = RequireSession();
        var packages = ParsePackages(fileBytes);
        var normalizedMode = PortabilitySupport.NormalizeMode(mode, "Task");
        var inventoryTask = _tasks.GetTaskSnapshotAsync(true, cancellationToken);
        var hostsTask = _hosts.GetHostSnapshotAsync(true, cancellationToken);
        var scriptsTask = _scripts.GetScriptSnapshotAsync(true, cancellationToken);
        var dateListsTask = _dateLists.GetSnapshotAsync(true, cancellationToken);
        await Task.WhenAll(inventoryTask, hostsTask, scriptsTask, dateListsTask);

        var inventory = (await inventoryTask).Tasks;
        var hosts = (await hostsTask).Hosts;
        var scripts = (await scriptsTask).Scripts;
        var dateLists = (await dateListsTask).DateLists;
        var targets = inventory.OrderBy(task => task.Name, StringComparer.OrdinalIgnoreCase)
            .Select(task => new TaskImportTarget(task.Id, task.Name, NormalizeTaskType(task.Type), task.Status.Equals("Running", StringComparison.OrdinalIgnoreCase), task.CanEditMoveItFields))
            .ToList();
        var usedNames = new HashSet<string>(inventory.Select(task => task.Name), StringComparer.OrdinalIgnoreCase);
        var itemPlans = new List<TaskImportItemPreflight>(packages.Count);
        var isMulti = packages.Count > 1;

        var plannedOverwriteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packages)
        {
            var suggestedName = package.TaskName;
            var nameAvailable = !usedNames.Contains(package.TaskName);
            var plannedAction = normalizedMode == "overwrite" ? "overwrite" : "create";
            var plannedTargetId = string.Empty;
            var itemMessage = string.Empty;

            if (normalizedMode == "overwrite")
            {
                var target = targets.FirstOrDefault(item => item.Name.Equals(package.TaskName, StringComparison.OrdinalIgnoreCase));
                if (target is null)
                {
                    nameAvailable = false;
                    itemMessage = $"Task '{package.TaskName}' does not exist. Update existing Tasks requires an existing Task with the same name as the export file.";
                }
                else if (!plannedOverwriteNames.Add(package.TaskName))
                {
                    nameAvailable = false;
                    itemMessage = $"The package contains more than one Task named '{package.TaskName}'. Each exported Task name must be unique for Update existing Tasks.";
                }
                else
                {
                    plannedTargetId = target.Id;
                    nameAvailable = true;
                    if (!target.CanEdit) itemMessage = $"MOVEit reports that the current user cannot edit task '{target.Name}'.";
                    else if (target.Running) itemMessage = $"Task '{target.Name}' is currently running and cannot be overwritten.";
                    else if (!target.Type.Equals(package.TaskType, StringComparison.OrdinalIgnoreCase))
                        itemMessage = $"The exported task is {package.TaskType}, but '{target.Name}' is {target.Type}. MAUI does not overwrite a task with a different task type.";
                }
            }
            else
            {
                var advancedBlock = MoveItLicensePolicy.GetAdvancedTaskCreationBlock(session, package.TaskType);
                if (!string.IsNullOrWhiteSpace(advancedBlock)) itemMessage = advancedBlock;
                else if (!nameAvailable) itemMessage = $"Task '{package.TaskName}' already exists. Choose Update existing Tasks to update the same-name Task.";
                else usedNames.Add(package.TaskName);
            }

            var hostSummary = Summarize(CollectReferenceIds(package.TaskRoot, IsHostReferenceAttribute), hosts.Select(item => item.Id));
            var scriptSummary = Summarize(CollectReferenceIds(package.TaskRoot, IsScriptReferenceAttribute), scripts.Select(item => item.Id));
            var dateListSummary = Summarize(CollectReferenceIds(package.TaskRoot, IsDateListReferenceAttribute), dateLists.Select(item => item.Id));
            var metadataSummary = await _portableMetadata.ValidateAsync(session.GetEnvironmentKey(), package.Metadata, "Task", cancellationToken);
            itemPlans.Add(new TaskImportItemPreflight(package.SourceTaskId, package.TaskName, package.TaskType, suggestedName, nameAvailable,
                plannedAction, plannedTargetId, hostSummary, scriptSummary, dateListSummary, metadataSummary, itemMessage));
        }

        var firstPackage = packages[0];
        var firstPlan = itemPlans[0];
        var aggregateHosts = Summarize(packages.SelectMany(package => CollectReferenceIds(package.TaskRoot, IsHostReferenceAttribute)), hosts.Select(item => item.Id));
        var aggregateScripts = Summarize(packages.SelectMany(package => CollectReferenceIds(package.TaskRoot, IsScriptReferenceAttribute)), scripts.Select(item => item.Id));
        var aggregateDateLists = Summarize(packages.SelectMany(package => CollectReferenceIds(package.TaskRoot, IsDateListReferenceAttribute)), dateLists.Select(item => item.Id));
        var aggregateMetadata = AggregateMetadata(itemPlans.Select(item => (item.SourceTaskName, item.Metadata)));
        var blockerCount = itemPlans.Count(item => !string.IsNullOrWhiteSpace(item.Message));
        var validCreateCount = itemPlans.Count(item => item.PlannedAction == "create" && string.IsNullOrWhiteSpace(item.Message));
        var capacityBlock = MoveItLicensePolicy.GetTaskCapacityBlock(session, inventory.Count, validCreateCount);
        var summaryMessage = !string.IsNullOrWhiteSpace(capacityBlock)
            ? capacityBlock
            : isMulti && blockerCount > 0
                ? $"{blockerCount} of {packages.Count} Tasks cannot be imported. Review the item validation details."
                : firstPlan.Message;

        return new TaskImportPreflight(
            true,
            firstPackage.SourceTaskId,
            firstPackage.TaskName,
            firstPackage.TaskType,
            firstPackage.ExportedUtc,
            firstPlan.SuggestedTaskName,
            firstPlan.SuggestedNameAvailable,
            aggregateHosts,
            aggregateScripts,
            aggregateDateLists,
            targets,
            aggregateMetadata,
            summaryMessage,
            packages.Count,
            isMulti,
            validCreateCount,
            itemPlans.Count(item => item.PlannedAction == "overwrite" && string.IsNullOrWhiteSpace(item.Message)),
            itemPlans);
    }

    // Purpose: Imports a validated single- or multi-item Task package.
    private async Task<TaskImportResult> ImportAsync(byte[] fileBytes, string mode, bool leaveDisabled,
        bool acknowledgeMissingDependencies, bool acknowledgeUnsupportedMetadata, string username, CancellationToken cancellationToken = default)
    {
        var packages = ParsePackages(fileBytes);
        var normalizedMode = PortabilitySupport.NormalizeMode(mode, "Task");
        var session = RequireSession();
        using var creationLease = normalizedMode == "create"
            ? await MoveItLicensePolicy.AcquireTaskCreationAsync(session, cancellationToken)
            : null;
        var preflight = await PreflightImportAsync(fileBytes, normalizedMode, cancellationToken);
        if (!string.IsNullOrWhiteSpace(preflight.Message)) throw new InvalidOperationException(preflight.Message);
        var hasMissing = preflight.Hosts.MissingIds.Count > 0 || preflight.Scripts.MissingIds.Count > 0 || preflight.DateLists.MissingIds.Count > 0;
        if (hasMissing && !acknowledgeMissingDependencies)
            throw new InvalidOperationException("The package references MOVEit resources that are not present or not visible. Review the warnings and explicitly acknowledge the missing dependencies before importing.");
        if (preflight.Metadata.RequiresAcknowledgement && !acknowledgeUnsupportedMetadata)
            throw new InvalidOperationException("The export contains MAUI custom metadata fields that are not compatible with the current Task metadata definition. Review the warnings and explicitly acknowledge that unsupported metadata will be ignored before importing.");

        if (packages.Count == 1)
        {
            var plan = preflight.Items[0];
            var single = await ImportOneAsync(packages[0], plan.PlannedAction, plan.SuggestedTaskName, plan.TargetTaskId, leaveDisabled, username, plan.Metadata, cancellationToken);
            return BuildResult(new[] { single });
        }

        var results = new List<TaskImportItemResult>(packages.Count);
        for (var index = 0; index < packages.Count; index++)
        {
            var package = packages[index];
            var plan = preflight.Items[index];
            try
            {
                var target = plan.PlannedAction == "overwrite" ? plan.TargetTaskId : string.Empty;
                var result = await ImportOneAsync(package, plan.PlannedAction, plan.SuggestedTaskName, target, leaveDisabled, username, plan.Metadata, cancellationToken);
                results.Add(result);
            }
            catch (OperationCanceledException) { throw; }
            catch (MoveItAuthenticationException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bulk Task import failed for exported Task {TaskName}.", package.TaskName);
                results.Add(new TaskImportItemResult(package.TaskName, string.Empty, plan.SuggestedTaskName, false, plan.PlannedAction == "create", string.Empty, ex.Message));
            }
        }
        return BuildResult(results);
    }

    // Purpose: Imports one parsed Task package item using the resolved create/overwrite plan.
    private async Task<TaskImportItemResult> ImportOneAsync(MauiTaskPackage package, string mode, string taskName, string targetTaskId,
        bool leaveDisabled, string username, MetadataImportSummary metadataSummary, CancellationToken cancellationToken)
    {
        var session = RequireSession();
        var warning = metadataSummary.Issues.Count > 0
            ? $"{metadataSummary.Issues.Count} unsupported custom metadata field(s) were ignored during import."
            : string.Empty;

        if (mode.Equals("create", StringComparison.OrdinalIgnoreCase))
        {
            var created = await _tasks.CreateTaskFromConfigurationAsync(taskName, package.TaskRoot.ToString(SaveOptions.DisableFormatting), leaveDisabled, cancellationToken);
            try { await _metadata.RecordObjectCreatedAsync(session.GetEnvironmentKey(), "Task", created.Id, username, cancellationToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Imported MOVEit Task {TaskId} was created, but MAUI could not record creation metadata.", created.Id);
                warning = PortabilitySupport.AppendWarning(warning, "The task was imported, but MAUI could not fully update its local audit metadata.");
            }
            try { await _portableMetadata.ApplyCompatibleAsync(session.GetEnvironmentKey(), "Task", created.Id, package.Metadata, username, cancellationToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Imported MOVEit Task {TaskId} was created, but MAUI could not import its custom metadata.", created.Id);
                warning = PortabilitySupport.AppendWarning(warning, "The task was imported, but its MAUI custom metadata could not be saved.");
            }
            return new TaskImportItemResult(package.TaskName, created.Id, created.Name, true, true, warning, string.Empty);
        }

        var targetId = PortabilitySupport.NormalizeId(targetTaskId, "Task");
        var live = (await _tasks.GetTaskSnapshotAsync(true, cancellationToken)).Tasks;
        var target = live.FirstOrDefault(item => item.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The same-name destination Task no longer exists or is not visible.");
        if (!target.CanEditMoveItFields) throw new InvalidOperationException($"MOVEit reports that the current user cannot edit task '{target.Name}'.");
        if (target.Status.Equals("Running", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"Task '{target.Name}' is currently running and cannot be overwritten.");
        if (!NormalizeTaskType(target.Type).Equals(package.TaskType, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The exported task is {package.TaskType}, but '{target.Name}' is {NormalizeTaskType(target.Type)}. MAUI does not overwrite a task with a different task type.");
        var conflictingName = live.FirstOrDefault(item => !item.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase) && item.Name.Equals(taskName, StringComparison.OrdinalIgnoreCase));
        if (conflictingName is not null) throw new TaskNameAlreadyExistsException(taskName);

        await _undo.CaptureAsync(targetId, username, cancellationToken);
        var replacement = new XElement(package.TaskRoot);
        replacement.SetAttributeValue("ID", targetId);
        replacement.SetAttributeValue("Name", taskName);
        if (leaveDisabled) replacement.SetAttributeValue("Active", "0");
        var restored = await _tasks.RestoreTaskConfigurationAsync(targetId, replacement.ToString(SaveOptions.DisableFormatting), cancellationToken);
        try { await _metadata.RecordObjectConfigurationModifiedAsync(session.GetEnvironmentKey(), "Task", restored.Id, username, cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Imported MOVEit Task {TaskId} was overwritten, but MAUI could not record modification metadata.", restored.Id);
            warning = PortabilitySupport.AppendWarning(warning, "The task was overwritten and an Undo version was saved, but MAUI could not fully update its local audit metadata.");
        }
        try { await _portableMetadata.ApplyCompatibleAsync(session.GetEnvironmentKey(), "Task", restored.Id, package.Metadata, username, cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Imported MOVEit Task {TaskId} was overwritten, but MAUI could not import its custom metadata.", restored.Id);
            warning = PortabilitySupport.AppendWarning(warning, "The task configuration was overwritten, but its MAUI custom metadata could not be saved.");
        }
        return new TaskImportItemResult(package.TaskName, restored.Id, restored.Name, true, false, warning, string.Empty);
    }

    // Purpose: Builds the combined single- or multi-item import result returned to the UI.
    private static TaskImportResult BuildResult(IReadOnlyList<TaskImportItemResult> results)
    {
        var successes = results.Where(item => item.Success).ToList();
        var first = successes.FirstOrDefault() ?? results.FirstOrDefault();
        var warning = string.Join(" ", successes.Select(item => item.Warning).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase));
        return new TaskImportResult(
            first?.TaskId ?? string.Empty,
            first?.TaskName ?? string.Empty,
            first?.Created ?? false,
            warning,
            results.Count,
            successes.Count,
            successes.Count(item => item.Created),
            successes.Count(item => !item.Created),
            results.Count(item => !item.Success),
            results);
    }

    // Purpose: Renames the single exported Task in memory while preserving its native configuration and MAUI metadata.
    private static byte[] RenameSingleTaskPackage(byte[] fileBytes, string newTaskName)
    {
        var package = MauiPortablePackage.Parse(fileBytes, "Task", "Task");
        if (package.Items.Count != 1)
            throw new InvalidOperationException("Clone Task requires an export containing exactly one Task.");

        var item = package.Items[0];
        var configuration = new XElement(item.Configuration);
        if (!configuration.Name.LocalName.Equals("Task", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The exported configuration is not a MOVEit Task.");
        configuration.SetAttributeValue("Name", newTaskName);

        var metadata = item.Envelope.Elements()
            .Where(element => element.Name.LocalName.Equals("MauiMetadata", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (metadata.Count != 1)
            throw new InvalidOperationException("The exported Task does not contain exactly one MAUI metadata block.");

        var renamedItem = MauiPortablePackage.BuildItem(
            item.SourceId,
            newTaskName,
            item.Type,
            configuration,
            new XElement(metadata[0]));
        return MauiPortablePackage.Build("Task", new[] { renamedItem }, package.ExportedUtc ?? DateTimeOffset.UtcNow);
    }

    // Purpose: Parses every Task item in the common MAUI package format.
    private IReadOnlyList<MauiTaskPackage> ParsePackages(byte[] fileBytes)
    {
        var package = MauiPortablePackage.Parse(fileBytes, "Task", "Task");
        var parsed = new List<MauiTaskPackage>(package.Items.Count);
        foreach (var item in package.Items)
        {
            if (!item.Configuration.Name.LocalName.Equals("Task", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"MAUI package item '{item.Name}' does not contain a MOVEit Task configuration.");
            var task = ParseTaskXml(item.Configuration.ToString(SaveOptions.DisableFormatting));
            if (!item.Name.Equals(task.TaskName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"MAUI package item '{item.Name}' does not match the Task name stored in its configuration.");
            if (item.Type.Length > 0 && !item.Type.Equals(task.TaskType, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"MAUI package item '{item.Name}' has an inconsistent Task type.");
            parsed.Add(new MauiTaskPackage(item.SourceId.Length > 0 ? item.SourceId : task.TaskId, item.Name, task.TaskType,
                package.ExportedUtc, task.Root, _portableMetadata.ParseFromEnvelope(item.Envelope, "Task")));
        }
        return parsed;
    }

    // Purpose: Parses native MOVEit Task XML.
    private static ParsedTaskXml ParseTaskXml(string xml)
    {
        XElement root;
        try { root = XElement.Parse(xml, LoadOptions.PreserveWhitespace); }
        catch (Exception ex) { throw new InvalidOperationException("The task configuration contains invalid XML.", ex); }
        if (!root.Name.LocalName.Equals("Task", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The configuration object is not a MOVEit Task.");
        var id = ((string?)root.Attribute("ID") ?? string.Empty).Trim();
        var name = ((string?)root.Attribute("Name") ?? string.Empty).Trim();
        if (name.Length == 0) throw new InvalidOperationException("The MOVEit Task configuration does not contain a Task Name.");
        return new ParsedTaskXml(id, name, TaskTypeFromCode((string?)root.Attribute("TT")), root);
    }

    private static string TaskTypeFromCode(string? code)
    {
        var value = (code ?? string.Empty).Trim();
        if (value.Equals("COS", StringComparison.OrdinalIgnoreCase)) return "Advanced";
        if (value.Equals("Sy", StringComparison.OrdinalIgnoreCase)) return "Synchronization";
        return "Traditional";
    }

    private static string NormalizeTaskType(string? type)
    {
        var value = (type ?? string.Empty).Trim();
        if (value.Equals("Advanced", StringComparison.OrdinalIgnoreCase)) return "Advanced";
        if (value.Equals("Synchronization", StringComparison.OrdinalIgnoreCase) || value.Equals("Sync", StringComparison.OrdinalIgnoreCase)) return "Synchronization";
        return "Traditional";
    }

    // Purpose: Collects referenced resource IDs from a Task configuration.
    private static HashSet<string> CollectReferenceIds(XElement root, Func<string, bool> predicate)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in root.DescendantsAndSelf().Attributes())
        {
            var key = attribute.Name.LocalName.Replace("_", string.Empty, StringComparison.Ordinal).Trim();
            if (!predicate(key)) continue;
            var value = attribute.Value.Trim();
            if (value.Length > 0 && value != "0") ids.Add(value);
        }
        return ids;
    }

    private static bool IsHostReferenceAttribute(string key) => key.Equals("HostID", StringComparison.OrdinalIgnoreCase);
    private static bool IsScriptReferenceAttribute(string key) => key.Equals("ScriptID", StringComparison.OrdinalIgnoreCase) || key.Equals("CustomScriptID", StringComparison.OrdinalIgnoreCase) || (key.EndsWith("ScriptID", StringComparison.OrdinalIgnoreCase) && key.Length <= 32);
    private static bool IsDateListReferenceAttribute(string key) => key.Equals("DateListID", StringComparison.OrdinalIgnoreCase) || key.Equals("SchedDateListID", StringComparison.OrdinalIgnoreCase);

    private static TaskImportDependencySummary Summarize(IEnumerable<string> referenced, IEnumerable<string> available)
    {
        var refs = referenced.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var have = new HashSet<string>(available.Where(value => !string.IsNullOrWhiteSpace(value)), StringComparer.OrdinalIgnoreCase);
        var missing = refs.Where(value => !have.Contains(value)).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
        return new TaskImportDependencySummary(refs.Count, refs.Count - missing.Count, missing);
    }

    // Purpose: Aggregates per-item metadata compatibility into one package-level acknowledgement summary.
    private static MetadataImportSummary AggregateMetadata(IEnumerable<(string Name, MetadataImportSummary Summary)> items)
    {
        var list = items.ToList();
        var issues = list.SelectMany(item => item.Summary.Issues.Select(issue => $"{item.Name}: {issue}")).ToList();
        return new MetadataImportSummary(
            MauiMetadataPortabilityService.SchemaVersion,
            list.Sum(item => item.Summary.ExportedFields),
            list.Sum(item => item.Summary.CompatibleFields),
            issues,
            issues.Count > 0);
    }

    private static string NormalizeTaskName(string? name)
    {
        var normalized = (name ?? string.Empty).Trim();
        if (normalized.Length == 0) throw new InvalidOperationException("Task Name is required.");
        if (normalized.Length > 254) throw new InvalidOperationException("Task Name cannot exceed 254 characters.");
        return normalized;
    }

    private MoveItSessionState RequireSession() => _session.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");

    private sealed record ParsedTaskXml(string TaskId, string TaskName, string TaskType, XElement Root);
    private sealed record MauiTaskPackage(string SourceTaskId, string TaskName, string TaskType, DateTimeOffset? ExportedUtc, XElement TaskRoot, PortableMetadataPackage Metadata);
}
