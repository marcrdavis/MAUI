// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Task spreadsheet import is intentionally independent of the native MAUI portability-package workflow.

using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;
using MoveItAutomationUI.Services.MoveIt.Tasks;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;

namespace MoveItAutomationUI.Services.MoveIt;

public interface ITaskSpreadsheetImportService
{
    Task<TaskSpreadsheetAnalysisResult> AnalyzeAsync(byte[] bytes, string fileName, string taskNamePrefix, bool updateExisting, bool leaveTasksDisabled, string username, CancellationToken cancellationToken = default);
    Task<TaskSpreadsheetImportRowResult> ImportRowAsync(string analysisId, int rowNumber, string username, CancellationToken cancellationToken = default);
}

public sealed class TaskSpreadsheetImportService : ITaskSpreadsheetImportService
{
    private const string ObjectType = "Task";
    private const string CachePrefix = "MoveItAutomationUI:TaskSpreadsheetImport:";
    private static readonly TimeSpan AnalysisLifetime = TimeSpan.FromMinutes(30);

    // Keep spreadsheet Task import scoped to the verified file-transfer Host profiles.
    // POP3/SMTP/ASx and the default Local File System Host are intentionally outside this workflow.
    private static readonly HashSet<string> SupportedTransferHostTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Share", "siLock", "FTP", "SSHFTP", "S3", "AzureBlob", "SharePoint", "GoogleStorage"
    };

    private static readonly HashSet<string> SupportedColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "RESOURCE_GROUP", "TASK_NAME", "REQ_NAME", "DESCRIPTION", "OPERATOR_NOTES",
        "AUTOMATION_SOURCE_HOST", "SOURCE_PATH", "SOURCE_FILE", "ONLY_NEW_FILES", "DELETE_SOURCE",
        "AUTOMATION_DESTINATION_HOST", "DEST_PATH", "DEST_FILE", "USE_DEST_FILENAME", "DEST_OVERWRITE",
        "USE_RELATIVE_SUBDIRS", "CREATE_DIRS_IF_NECESSARY", "ENCRYPTION", "PGP_KEY", "SIGNING_KEY", "COMPRESSION",
        "NOACTION_EMAIL_TO", "NOACTION_EMAIL_TEMPLATE", "SUCCESS_EMAIL_TO", "SUCCESS_EMAIL_TEMPLATE",
        "FAILURE_EMAIL_TO", "FAILURE_EMAIL_TEMPLATE", "SCHED_DOW", "SCHED_DOM", "SCHED_START", "SCHED_END",
        "SCHED_EVERY_MIN", "SCHED_REPEAT", "SCHED_ONLY_UNTIL_SUCCESS", "SCHED_FAIL_IF_NO_FILES", "SCHED_POLL_WITH_NOTIF", "SCHED_POLL_WITH_NOTIFY",
        "ARCHIVE", "ARCHIVE_HOST", "ARCHIVE_PATH", "ARCHIVE_FILE"
    };

    private static readonly string[] RequiredValueColumns =
    {
        "RESOURCE_GROUP", "TASK_NAME", "SOURCE_FILE", "AUTOMATION_SOURCE_HOST", "SOURCE_PATH",
        "AUTOMATION_DESTINATION_HOST", "DEST_PATH"
    };

    private static readonly string[] RequiredStrictYesNoColumns =
    {
        "DELETE_SOURCE", "USE_DEST_FILENAME", "DEST_OVERWRITE", "ARCHIVE"
    };

    private static readonly Dictionary<string, string> DefaultNoYesNoColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["COMPRESSION"] = "N",
        ["SCHED_REPEAT"] = "N",
        ["SCHED_ONLY_UNTIL_SUCCESS"] = "N",
        ["SCHED_FAIL_IF_NO_FILES"] = "N",
        ["SCHED_POLL_WITH_NOTIF"] = "N"
    };

    private static readonly Dictionary<string, string> DefaultYesYesNoColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ONLY_NEW_FILES"] = "Y",
        ["USE_RELATIVE_SUBDIRS"] = "Y",
        ["CREATE_DIRS_IF_NECESSARY"] = "Y"
    };

    private readonly IMemoryCache _cache;
    private readonly IMoveItSessionService _session;
    private readonly IMoveItRestClient _rest;
    private readonly IResourceGroupLookupService _resourceGroupLookup;
    private readonly IAutomationTaskService _tasks;
    private readonly IAutomationHostService _hosts;
    private readonly IAutomationHostBrowseService _browse;
    private readonly ITraditionalTaskHostProfileCatalog _profiles;
    private readonly ITraditionalTaskDefinitionComposer _composer;
    private readonly ITaskCrudCoordinator _taskCrud;
    private readonly IAutomationKeyCertificateService _keys;
    private readonly ITaskNotificationTemplateService _templates;
    private readonly IMetadataValueValidationService _metadataValidation;
    private readonly IMetadataRepository _metadata;
    private readonly ITaskUndoService _undo;
    private readonly MetadataUiOptions _metadataOptions;

    public TaskSpreadsheetImportService(
        IMemoryCache cache,
        IMoveItSessionService session,
        IMoveItRestClient rest,
        IResourceGroupLookupService resourceGroupLookup,
        IAutomationTaskService tasks,
        IAutomationHostService hosts,
        IAutomationHostBrowseService browse,
        ITraditionalTaskHostProfileCatalog profiles,
        ITraditionalTaskDefinitionComposer composer,
        ITaskCrudCoordinator taskCrud,
        IAutomationKeyCertificateService keys,
        ITaskNotificationTemplateService templates,
        IMetadataValueValidationService metadataValidation,
        IMetadataRepository metadata,
        ITaskUndoService undo,
        IOptionsSnapshot<MetadataUiOptions> metadataOptions)
    {
        _cache = cache;
        _session = session;
        _rest = rest;
        _resourceGroupLookup = resourceGroupLookup;
        _tasks = tasks;
        _hosts = hosts;
        _browse = browse;
        _profiles = profiles;
        _composer = composer;
        _taskCrud = taskCrud;
        _keys = keys;
        _templates = templates;
        _metadataValidation = metadataValidation;
        _metadata = metadata;
        _undo = undo;
        _metadataOptions = metadataOptions.Value;
    }

    public async Task<TaskSpreadsheetAnalysisResult> AnalyzeAsync(
        byte[] bytes,
        string fileName,
        string taskNamePrefix,
        bool updateExisting,
        bool leaveTasksDisabled,
        string username,
        CancellationToken cancellationToken = default)
    {
        var session = RequireSession();
        var prefix = NormalizePrefix(taskNamePrefix);
        var file = TaskSpreadsheetImportFileReader.Read(bytes, fileName);
        if (file.Rows.Count == 0) throw new InvalidOperationException("The import file does not contain any data rows.");

        var tasksTask = _tasks.GetTaskSnapshotAsync(true, cancellationToken);
        var hostsTask = _hosts.GetHostSnapshotAsync(true, cancellationToken);
        var groupsTask = _resourceGroupLookup.GetVisibleDefinitionsAsync(cancellationToken);
        var listsTask = _rest.GetDateListsAsync(cancellationToken);
        var scriptsTask = _rest.GetStandardScriptsAsync(cancellationToken);
        var templatesTask = _templates.GetCurrentAsync(cancellationToken);
        var keysTask = MoveItLicensePolicy.GetCapabilities(session).PgpLicensed
            ? _keys.GetSnapshotAsync(true, cancellationToken)
            : Task.FromResult(new KeyCertificateCacheSnapshot(Array.Empty<AutomationKeyCertificate>(), DateTimeOffset.UtcNow));

        await Task.WhenAll(tasksTask, hostsTask, groupsTask, listsTask, scriptsTask, templatesTask, keysTask);

        var taskSnapshot = await tasksTask;
        var hostSnapshot = await hostsTask;
        var resourceGroups = await groupsTask;
        var dateLists = await listsTask;
        var standardScripts = await scriptsTask;
        var notificationTemplates = await templatesTask;
        var keySnapshot = await keysTask;
        var pgpLicensed = MoveItLicensePolicy.GetCapabilities(session).PgpLicensed;
        var environmentKey = session.GetEnvironmentKey();

        var globalIssues = new List<TaskSpreadsheetImportIssue>();
        var headerSet = file.Headers.ToHashSet(StringComparer.OrdinalIgnoreCase);
        ValidateRequiredHeaders(headerSet, globalIssues);

        var metadataGroups = (_metadataOptions.Tasks.Fields ?? new List<MetadataFieldDefinition>())
            .Where(field => !string.IsNullOrWhiteSpace(field.Name))
            .Select(field => new { Field = field, Header = NormalizeHeader(field.Name) })
            .Where(item => item.Header.Length > 0)
            .GroupBy(item => item.Header, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var duplicate in metadataGroups.Where(group => group.Count() > 1))
        {
            globalIssues.Add(new TaskSpreadsheetImportIssue(
                "Error",
                null,
                duplicate.Key,
                $"Multiple MAUI Task metadata fields normalize to spreadsheet column '{duplicate.Key}': {string.Join(", ", duplicate.Select(item => $"'{item.Field.Name}'"))}. Rename the metadata fields so the spreadsheet mapping is unambiguous."));
        }
        var configuredMetadata = metadataGroups
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().Field, StringComparer.OrdinalIgnoreCase);
        var ignoredColumns = file.Headers.Count(header => !SupportedColumns.Contains(header) && !configuredMetadata.ContainsKey(header));

        var context = new AnalyzeContext(
            taskSnapshot.Tasks,
            hostSnapshot.Hosts,
            resourceGroups,
            dateLists,
            standardScripts,
            notificationTemplates,
            keySnapshot.Items,
            pgpLicensed,
            configuredMetadata,
            environmentKey,
            prefix,
            updateExisting);

        var plans = new List<SpreadsheetRowPlan>();
        foreach (var raw in file.Rows)
            plans.Add(await AnalyzeRowAsync(raw, context, cancellationToken));

        foreach (var duplicate in plans.Where(plan => plan.TaskName.Length > 0)
                     .GroupBy(plan => plan.TaskName, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            foreach (var plan in duplicate)
                plan.AddError("TASK_NAME", $"Task name '{plan.TaskName}' appears more than once in this import file after normalization/prefixing.");
        }

        var existingNames = taskSnapshot.Tasks.Select(task => task.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var plannedCreates = plans.Where(plan => plan.Errors.Count == 0 && plan.TaskName.Length > 0 && !existingNames.Contains(plan.TaskName))
            .Select(plan => plan.TaskName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var capacityBlock = MoveItLicensePolicy.GetTaskCapacityBlock(session, taskSnapshot.Tasks.Count, plannedCreates);
        if (capacityBlock is not null)
            globalIssues.Add(new TaskSpreadsheetImportIssue("Error", null, "TASK_LICENSE", capacityBlock));

        var analysisId = Guid.NewGuid().ToString("N");
        var state = new SpreadsheetAnalysisState(
            analysisId,
            session.GetEnvironmentKey(),
            (username ?? string.Empty).Trim(),
            fileName,
            prefix,
            updateExisting,
            leaveTasksDisabled,
            file.Headers.Select(header => header.Equals("REQ_NAME", StringComparison.OrdinalIgnoreCase) ? "TASK_NAME" : header)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            plans,
            ignoredColumns,
            globalIssues.Any(issue => IsError(issue.Severity)));
        // Keep active multi-batch imports alive while they are being processed.
        // Idle analyzed imports still expire after 30 minutes.
        _cache.Set(CachePrefix + analysisId, state, new MemoryCacheEntryOptions
        {
            SlidingExpiration = AnalysisLifetime
        });
        return ToPublicResult(state, globalIssues);
    }

    public async Task<TaskSpreadsheetImportRowResult> ImportRowAsync(string analysisId, int rowNumber, string username, CancellationToken cancellationToken = default)
    {
        var session = RequireSession();
        var state = GetAnalysis(analysisId);
        if (!string.Equals(state.EnvironmentKey, session.GetEnvironmentKey(), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(state.Username, (username ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This analyzed import no longer belongs to the current MAUI session. Analyze the file again.");
        if (state.HasGlobalErrors || state.Plans.Any(plan => plan.Errors.Count > 0))
            throw new InvalidOperationException("The analyzed file contains blocking errors. Resolve them and analyze the file again before importing.");

        var plan = state.Plans.SingleOrDefault(item => item.RowNumber == rowNumber)
            ?? throw new InvalidOperationException($"Row {rowNumber} is not part of the analyzed import.");
        if (state.CompletedRows.ContainsKey(rowNumber))
            throw new InvalidOperationException($"Row {rowNumber} has already been imported from this analysis. Analyze the file again before intentionally applying it another time.");
        if (!state.InProgressRows.TryAdd(rowNumber, 0))
            throw new InvalidOperationException($"Row {rowNumber} is already being imported. Wait for the current request to finish before retrying it.");
        if (state.CompletedRows.ContainsKey(rowNumber))
        {
            state.InProgressRows.TryRemove(rowNumber, out _);
            throw new InvalidOperationException($"Row {rowNumber} has already been imported from this analysis. Analyze the file again before intentionally applying it another time.");
        }

        var taskId = string.Empty;
        var outcome = string.Empty;
        try
        {
            var live = await _tasks.GetTaskSnapshotAsync(true, cancellationToken);
            var matches = live.Tasks.Where(task => task.Name.Equals(plan.TaskName, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length > 1)
                throw new InvalidOperationException($"Task name '{plan.TaskName}' now matches multiple MOVEit Tasks. Import cannot safely choose one.");

            if (plan.ExistingTaskId.Length == 0)
            {
                if (matches.Length > 0)
                    throw new InvalidOperationException($"Task '{plan.TaskName}' appeared after analysis. Analyze the file again before importing this row.");

                using var creationGate = await MoveItLicensePolicy.AcquireTaskCreationAsync(session, cancellationToken);
                live = await _tasks.GetTaskSnapshotAsync(true, cancellationToken);
                matches = live.Tasks.Where(task => task.Name.Equals(plan.TaskName, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length > 0)
                    throw new InvalidOperationException($"Task '{plan.TaskName}' appeared after analysis. Analyze the file again before importing this row.");
                var capacityBlock = MoveItLicensePolicy.GetTaskCapacityBlock(session, live.Tasks.Count, 1);
                if (capacityBlock is not null) throw new InvalidOperationException(capacityBlock);

                var payload = plan.TaskPayload.DeepClone().AsObject();
                payload["Active"] = state.LeaveTasksDisabled ? 0 : 1;
                var created = await _taskCrud.CreateTraditionalResolvedAsync(payload, cancellationToken);
                taskId = created.Id;
                outcome = "Created";
            }
            else
            {
                if (matches.Length == 0)
                    throw new InvalidOperationException($"Task '{plan.TaskName}' disappeared after analysis. Analyze the file again before importing this row.");
                if (!matches[0].Id.Equals(plan.ExistingTaskId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Task '{plan.TaskName}' now resolves to a different MOVEit Task than the one analyzed. Analyze the file again before importing this row.");
                if (!state.UpdateExisting)
                    throw new InvalidOperationException($"Task '{plan.TaskName}' already exists. Enable 'Update existing task if name already exists' and analyze again to update it.");
                var existing = matches[0];
                if (!IsTraditionalTask(existing))
                    throw new InvalidOperationException($"Task '{plan.TaskName}' is a {existing.Type} Task and cannot be updated by the CSV/XLSX Traditional Task importer.");

                taskId = existing.Id;
                await _undo.CaptureAsync(taskId, state.Username, cancellationToken);
                await _taskCrud.UpdateTraditionalFreshAsync(taskId,
                    current =>
                    {
                        if (!IsTraditionalTaskDefinition(current))
                            throw new InvalidOperationException($"Task '{plan.TaskName}' changed to a non-Traditional Task after analysis and cannot be updated by the CSV/XLSX importer.");
                        return TraditionalTaskDefinitionMerger.PrepareSpreadsheetUpdate(current, plan.TaskPayload, state.LeaveTasksDisabled);
                    },
                    // The existing spreadsheet importer did not perform post-PUT readback.
                    // Preserve that behavior until native payload/readback parity is tested in Phase 2.
                    cancellationToken: cancellationToken);
                outcome = "Updated";
            }

            // MOVEit is the system of record. Once its create/update has returned successfully, mark this analyzed row
            // committed before performing MAUI-only metadata/audit bookkeeping so a local database problem cannot invite
            // an unsafe duplicate retry of a confirmed MOVEit mutation.
            state.CompletedRows.TryAdd(rowNumber, 0);
        }
        catch (TraditionalTaskCreationUnverifiedException ex)
        {
            // MOVEit accepted the create request, but its Task ID/readback could not
            // be verified. This must not be marked as an ordinary retriable failure:
            // the next batch must not send a duplicate POST for the same row.
            state.CompletedRows.TryAdd(rowNumber, 0);
            state.InProgressRows.TryRemove(rowNumber, out _);
            _tasks.InvalidateCache();
            return new TaskSpreadsheetImportRowResult(plan.RowNumber, plan.TaskName, "Unverified",
                ex.TaskId ?? string.Empty, ex.Message,
                plan.Warnings.Select(issue => issue.Message).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        }
        catch
        {
            state.InProgressRows.TryRemove(rowNumber, out _);
            throw;
        }
        state.InProgressRows.TryRemove(rowNumber, out _);
        _tasks.InvalidateCache();

        var warnings = plan.Warnings.Select(issue => issue.Message).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        try
        {
            if (outcome == "Created")
                await _metadata.RecordObjectCreatedAsync(state.EnvironmentKey, ObjectType, taskId, state.Username, cancellationToken);
            else
                await _metadata.RecordObjectConfigurationModifiedAsync(state.EnvironmentKey, ObjectType, taskId, state.Username, cancellationToken);
            if (plan.Metadata.Count > 0)
                await _metadata.SaveObjectMetadataAsync(state.EnvironmentKey, ObjectType, taskId, plan.Metadata, state.Username, cancellationToken);
        }
        catch (Exception ex)
        {
            warnings.Add($"The Task was {outcome.ToLowerInvariant()} in MOVEit, but MAUI could not fully update local metadata/audit state: {ex.Message}");
        }

        return new TaskSpreadsheetImportRowResult(plan.RowNumber, plan.TaskName, outcome, taskId,
            $"{outcome} Task '{plan.TaskName}'.", warnings.ToArray());
    }

    private async Task<SpreadsheetRowPlan> AnalyzeRowAsync(TaskSpreadsheetRawRow raw, AnalyzeContext context, CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, string>(raw.Values, StringComparer.OrdinalIgnoreCase);
        var plan = new SpreadsheetRowPlan(raw.RowNumber, values);

        CanonicalizeTaskName(values, plan, context.Prefix);
        CanonicalizePollWithNotification(values, plan);
        // Preserve the original schedule-presence semantics from the proven importer. Blank schedule flags
        // validate/default to N in the preview but must not by themselves create a schedule override.
        var scheduleInputs = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        var createDirsWasSpecified = Get(values, "CREATE_DIRS_IF_NECESSARY").Length > 0;
        NormalizeYesNo(values, plan);
        NormalizeFileMasks(values, plan);
        NormalizeMacros(values, plan);
        NormalizeEmailAddresses(values, plan);

        foreach (var required in RequiredValueColumns)
            if (Get(values, required).Length == 0) plan.AddError(required, $"{required} is required.");

        if (Get(values, "USE_DEST_FILENAME").Equals("Y", StringComparison.OrdinalIgnoreCase) && Get(values, "DEST_FILE").Length == 0)
            plan.AddError("DEST_FILE", "DEST_FILE is required when USE_DEST_FILENAME=Y.");

        // RESOURCE_GROUP is required for every imported row (including updates).
        // Named groups are resolved only after the required-field check to avoid duplicate errors.
        var requestedGroup = Get(values, "RESOURCE_GROUP");
        if (requestedGroup.Length > 0)
        {
            if (!_resourceGroupLookup.IsVisible(context.ResourceGroups, requestedGroup))
                plan.AddError("RESOURCE_GROUP", $"Resource Group '{requestedGroup}' was not found or is not visible to the current MOVEit user.");
        }

        ResolvedHostLocation? sourceLocation = null;
        var sourceHost = ResolveHost(Get(values, "AUTOMATION_SOURCE_HOST"), Get(values, "SOURCE_PATH"), context.Hosts, "Source");
        if (!sourceHost.Success)
        {
            // Blank Hosts already have a required-field validation error. Never dereference
            // an unresolved Host when another column (such as the Path) is populated.
            if (Get(values, "AUTOMATION_SOURCE_HOST").Length > 0)
                plan.AddError("AUTOMATION_SOURCE_HOST", sourceHost.Error);
        }
        else if (Get(values, "SOURCE_PATH").Length > 0)
        {
            var resolvedSourceHost = sourceHost.Host!;
            WarnIfChanged(plan, "AUTOMATION_SOURCE_HOST", Get(values, "AUTOMATION_SOURCE_HOST"), resolvedSourceHost.Name);
            values["AUTOMATION_SOURCE_HOST"] = resolvedSourceHost.Name;
            var location = await ResolveImportedLocationAsync(resolvedSourceHost, sourceHost.Profile!, Get(values, "SOURCE_PATH"), "SOURCE_PATH", context, cancellationToken);
            if (!location.Success) plan.AddError("SOURCE_PATH", location.Error);
            else
            {
                sourceLocation = location.Location;
                WarnIfChanged(plan, "SOURCE_PATH", Get(values, "SOURCE_PATH"), sourceLocation!.DisplayPath);
                values["SOURCE_PATH"] = sourceLocation.DisplayPath;
            }
        }

        ResolvedHostLocation? destinationLocation = null;
        var destinationHost = ResolveHost(Get(values, "AUTOMATION_DESTINATION_HOST"), Get(values, "DEST_PATH"), context.Hosts, "Destination");
        if (!destinationHost.Success)
        {
            // Blank Hosts already have a required-field validation error. Never dereference
            // an unresolved Host when another column (such as the Path) is populated.
            if (Get(values, "AUTOMATION_DESTINATION_HOST").Length > 0)
                plan.AddError("AUTOMATION_DESTINATION_HOST", destinationHost.Error);
        }
        else if (Get(values, "DEST_PATH").Length > 0)
        {
            var resolvedDestinationHost = destinationHost.Host!;
            WarnIfChanged(plan, "AUTOMATION_DESTINATION_HOST", Get(values, "AUTOMATION_DESTINATION_HOST"), resolvedDestinationHost.Name);
            values["AUTOMATION_DESTINATION_HOST"] = resolvedDestinationHost.Name;
            var location = await ResolveImportedLocationAsync(resolvedDestinationHost, destinationHost.Profile!, Get(values, "DEST_PATH"), "DEST_PATH", context, cancellationToken);
            if (!location.Success) plan.AddError("DEST_PATH", location.Error);
            else
            {
                destinationLocation = location.Location;
                WarnIfChanged(plan, "DEST_PATH", Get(values, "DEST_PATH"), destinationLocation!.DisplayPath);
                values["DEST_PATH"] = destinationLocation.DisplayPath;
                if (createDirsWasSpecified && !destinationLocation.Profile.DestinationSupportsForceDir)
                    plan.AddWarning("CREATE_DIRS_IF_NECESSARY", $"CREATE_DIRS_IF_NECESSARY is not exposed by the verified {destinationLocation.Profile.DisplayName} Destination contract and will be ignored for this row.");
            }
        }

        ResolvedHostLocation? archiveLocation = null;
        if (Get(values, "ARCHIVE").Equals("Y", StringComparison.OrdinalIgnoreCase))
        {
            if (Get(values, "ARCHIVE_HOST").Length == 0) plan.AddError("ARCHIVE_HOST", "ARCHIVE_HOST is required when ARCHIVE=Y.");
            if (Get(values, "ARCHIVE_PATH").Length == 0) plan.AddError("ARCHIVE_PATH", "ARCHIVE_PATH is required when ARCHIVE=Y.");
            if (Get(values, "ARCHIVE_FILE").Length == 0) values["ARCHIVE_FILE"] = "[OrigName]";
            if (Get(values, "ARCHIVE_HOST").Length > 0)
            {
                var archive = ResolveHost(Get(values, "ARCHIVE_HOST"), Get(values, "ARCHIVE_PATH"), context.Hosts, "Destination");
                if (!archive.Success) plan.AddError("ARCHIVE_HOST", archive.Error);
                else
                {
                    var resolvedArchiveHost = archive.Host!;
                    WarnIfChanged(plan, "ARCHIVE_HOST", Get(values, "ARCHIVE_HOST"), resolvedArchiveHost.Name);
                    values["ARCHIVE_HOST"] = resolvedArchiveHost.Name;
                    var location = await ResolveImportedLocationAsync(resolvedArchiveHost, archive.Profile!, Get(values, "ARCHIVE_PATH"), "ARCHIVE_PATH", context, cancellationToken);
                    if (!location.Success) plan.AddError("ARCHIVE_PATH", location.Error);
                    else
                    {
                        archiveLocation = location.Location;
                        WarnIfChanged(plan, "ARCHIVE_PATH", Get(values, "ARCHIVE_PATH"), archiveLocation!.DisplayPath);
                        values["ARCHIVE_PATH"] = archiveLocation.DisplayPath;
                    }
                }
            }
        }

        JsonObject? process = null;
        ValidateAndBuildPgp(values, plan, context, ref process);

        string? customDateListId = null;
        var dom = Get(scheduleInputs, "SCHED_DOM");
        if (dom.Length > 0 && !LooksLikeDayOfMonthExpression(dom))
        {
            var dateList = ResolveNamedObject(context.DateLists, dom, "Name", "Title", "name");
            if (dateList.Status != ResolutionStatus.Found) plan.AddError("SCHED_DOM", $"Custom Date List '{dom}' was not found or is ambiguous.");
            else
            {
                customDateListId = ReadString(dateList.Value!, "ID", "Id", "id");
                if (customDateListId.Length == 0)
                    plan.AddError("SCHED_DOM", $"Custom Date List '{dom}' does not expose a usable MOVEit ID.");
            }
        }
        var schedule = TaskSpreadsheetScheduleBuilder.TryBuild(scheduleInputs, customDateListId);
        if (!schedule.Success) plan.AddError("SCHEDULE", schedule.Error);

        var requiresNotification = new[] { "FAILURE_EMAIL_TO", "SUCCESS_EMAIL_TO", "NOACTION_EMAIL_TO" }.Any(column => Get(values, column).Length > 0);
        AutomationHost? smtpHost = null;
        if (requiresNotification)
        {
            var smtpHosts = context.Hosts.Where(host => host.TypeCode.Equals("SMTP", StringComparison.OrdinalIgnoreCase) || host.Type.Equals("SMTP", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (smtpHosts.Length != 1) plan.AddError("EMAIL", smtpHosts.Length == 0
                ? "Email notifications were requested, but no visible SMTP Host is available in MOVEit."
                : "Email notifications were requested, but multiple SMTP Hosts are visible. MAUI cannot safely choose one for this import.");
            else smtpHost = smtpHosts[0];
        }
        var actions = BuildNotifications(values, plan, context.Templates, smtpHost);

        var metadataSubmitted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in values)
        {
            if (!SupportedColumns.Contains(pair.Key) && context.MetadataFields.TryGetValue(pair.Key, out var field))
                metadataSubmitted[field.Name] = pair.Value;
        }
        try
        {
            plan.Metadata = await _metadataValidation.FilterAndValidateAsync(context.EnvironmentKey, _metadataOptions.Tasks, metadataSubmitted, cancellationToken);
            foreach (var pair in plan.Metadata) values[NormalizeHeader(pair.Key)] = pair.Value;
        }
        catch (ArgumentException ex)
        {
            plan.AddError("METADATA", ex.Message);
        }

        if (plan.TaskName.Length > 0)
        {
            var existing = context.Tasks.Where(task => task.Name.Equals(plan.TaskName, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (existing.Length > 1) plan.AddError("TASK_NAME", $"Task name '{plan.TaskName}' matches multiple existing MOVEit Tasks.");
            else if (existing.Length == 1)
            {
                plan.ExistingTaskId = existing[0].Id.Trim();
                if (plan.ExistingTaskId.Length == 0)
                    plan.AddError("TASK_NAME", $"Existing Task '{plan.TaskName}' does not expose a usable MOVEit Task ID.");
                else if (!context.UpdateExisting) plan.AddError("TASK_NAME", $"Task '{plan.TaskName}' already exists. Enable 'Update existing task if name already exists' to import this row.");
                else if (!IsTraditionalTask(existing[0])) plan.AddError("TASK_NAME", $"Task '{plan.TaskName}' is a {existing[0].Type} Task and cannot be overwritten by this Traditional Task importer.");
                else if (requestedGroup.Length > 0 && !existing[0].Group.Equals(requestedGroup, StringComparison.OrdinalIgnoreCase))
                    plan.AddWarning("RESOURCE_GROUP", $"Existing Task '{plan.TaskName}' is in Resource Group '{existing[0].Group}'. MOVEit REST updates do not reliably move an existing Task between Resource Groups, so its current group will be preserved.");
            }
        }

        if (plan.Errors.Count == 0 && sourceLocation is not null && destinationLocation is not null && schedule.Success)
        {
            try
            {
                plan.TaskPayload = BuildTaskPayload(values, sourceLocation, destinationLocation, archiveLocation, process, schedule.Schedules, actions);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                plan.AddError("TASK", $"The Task definition could not be built from this row: {ex.Message}");
            }
        }
        return plan;
    }

    private JsonObject BuildTaskPayload(
        IReadOnlyDictionary<string, string> values,
        ResolvedHostLocation sourceLocation,
        ResolvedHostLocation destinationLocation,
        ResolvedHostLocation? archiveLocation,
        JsonObject? process,
        JsonObject? schedules,
        JsonArray nextActions)
    {
        var description = Get(values, "DESCRIPTION");
        if (description.Length == 0) description = $"Created by MAUI Task spreadsheet import on {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        var source = new JsonObject
        {
            ["Type"] = sourceLocation.Profile.TypeCode,
            ["HostID"] = ParseLong(sourceLocation.Host.Id, "Source HostID"),
            ["FileMask"] = Get(values, "SOURCE_FILE"),
            ["NewFilesOnly"] = IsYes(values, "ONLY_NEW_FILES") ? 1 : 0,
            ["DeleteOrig"] = IsYes(values, "DELETE_SOURCE") ? 1 : 0
        };
        source = _composer.BuildStepBody(new TraditionalTaskStepDefinition(
            TraditionalTaskStepRole.Source, sourceLocation.Profile, source,
            sourceLocation.TaskPath, sourceLocation.RootName, sourceLocation.RootId));

        var steps = new JsonArray { new JsonObject { ["Source"] = source } };
        if (process is not null) steps.Add(new JsonObject { ["Process"] = process.DeepClone() });

        var destinationFileName = IsYes(values, "USE_DEST_FILENAME") ? Get(values, "DEST_FILE") : "[OrigName]";
        if (string.IsNullOrWhiteSpace(destinationFileName)) destinationFileName = "[OrigName]";
        var destination = new JsonObject
        {
            ["Type"] = destinationLocation.Profile.TypeCode,
            ["HostID"] = ParseLong(destinationLocation.Host.Id, "Destination HostID"),
            ["UseOrigName"] = 0,
            ["FileName"] = destinationFileName,
            ["OverwriteOrig"] = IsYes(values, "DEST_OVERWRITE") ? 1 : 0,
            ["Zip"] = IsYes(values, "COMPRESSION") ? 1 : 0,
            ["UseRelativeSubdirs"] = IsYes(values, "USE_RELATIVE_SUBDIRS") ? 1 : 0
        };
        if (destinationLocation.Profile.DestinationSupportsForceDir)
            destination["ForceDir"] = IsYes(values, "CREATE_DIRS_IF_NECESSARY") ? 1 : 0;
        destination = _composer.BuildStepBody(new TraditionalTaskStepDefinition(
            TraditionalTaskStepRole.Destination, destinationLocation.Profile, destination,
            destinationLocation.TaskPath, destinationLocation.RootName, destinationLocation.RootId));
        steps.Add(new JsonObject { ["Destination"] = destination });

        if (IsYes(values, "ARCHIVE") && archiveLocation is not null)
        {
            var archive = new JsonObject
            {
                ["Type"] = archiveLocation.Profile.TypeCode,
                ["HostID"] = ParseLong(archiveLocation.Host.Id, "Archive HostID"),
                ["UseOrigName"] = 0,
                ["FileName"] = Get(values, "ARCHIVE_FILE").Length > 0 ? Get(values, "ARCHIVE_FILE") : "[OrigName]",
                // Preserve the proven ImportFromCSV archive create-directory contract.
                ["CreateDir"] = 1,
                ["OverwriteOrig"] = 1,
                ["Zip"] = 0
            };
            var composedArchive = _composer.BuildStepBody(new TraditionalTaskStepDefinition(
                TraditionalTaskStepRole.Destination, archiveLocation.Profile, archive,
                archiveLocation.TaskPath, archiveLocation.RootName, archiveLocation.RootId));
            steps.Add(new JsonObject { ["Destination"] = composedArchive });
        }

        return _composer.BuildRootDefinition(new TraditionalTaskRootDefinition(
            Get(values, "TASK_NAME"), false, description,
            Get(values, "OPERATOR_NOTES").Length > 0 ? Get(values, "OPERATOR_NOTES") : null,
            schedules ?? BuildDefaultSchedule(), steps,
            BuildResourceGroupPayload(Get(values, "RESOURCE_GROUP")),
            nextActions.Count > 0 ? nextActions : null));
    }

    // A missing RESOURCE_GROUP is different from a non-existent named group: do not send [""].
    internal static JsonArray BuildResourceGroupPayload(string? groupName)
    {
        var name = (groupName ?? string.Empty).Trim();
        return name.Length == 0 ? new JsonArray() : new JsonArray(name);
    }

    private static JsonObject BuildDefaultSchedule() => new()
    {
        ["Schedule"] = new JsonArray(new JsonObject
        {
            ["RunEvenIfNotif"] = 0,
            ["FailIfNoSuccessInSched"] = 1,
            ["OnlyUntilFirstSuccess"] = 0,
            ["Days"] = new JsonObject { ["DayOfWeek"] = new JsonArray("Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday") },
            ["Frequency"] = new JsonObject { ["Interval"] = new JsonArray(new JsonObject { ["StartTime"] = "00:00", ["EndTime"] = "23:59", ["EveryMinutes"] = 1 }) }
        })
    };

    private void ValidateAndBuildPgp(Dictionary<string, string> values, SpreadsheetRowPlan plan, AnalyzeContext context, ref JsonObject? process)
    {
        var encryption = Get(values, "ENCRYPTION");
        var pgpKey = Get(values, "PGP_KEY");
        var signingKey = Get(values, "SIGNING_KEY");
        if (encryption.Length == 0)
        {
            if (pgpKey.Length > 0 || signingKey.Length > 0) plan.AddWarning("ENCRYPTION", "PGP_KEY/SIGNING_KEY values were provided without ENCRYPTION and will be ignored.");
            return;
        }
        if (!context.PgpLicensed) { plan.AddError("ENCRYPTION", "The connected MOVEit Automation license does not include PGP."); return; }

        var normalizedEncryption = encryption.Trim();
        string scriptName;
        var parameters = new JsonArray();
        if (normalizedEncryption.Equals("Encrypt", StringComparison.OrdinalIgnoreCase))
        {
            scriptName = "PGP Encrypt Only";
            if (pgpKey.Length == 0) plan.AddError("PGP_KEY", "PGP_KEY is required when ENCRYPTION=Encrypt.");
        }
        else if (normalizedEncryption.Equals("Encrypt and Sign", StringComparison.OrdinalIgnoreCase))
        {
            scriptName = "PGP Encrypt and Sign";
            if (pgpKey.Length == 0) plan.AddError("PGP_KEY", "PGP_KEY is required when ENCRYPTION=Encrypt and Sign.");
            if (signingKey.Length == 0) plan.AddError("SIGNING_KEY", "SIGNING_KEY is required when ENCRYPTION=Encrypt and Sign.");
        }
        else if (normalizedEncryption.Equals("Decrypt", StringComparison.OrdinalIgnoreCase))
        {
            scriptName = "PGP Decrypt";
            if (pgpKey.Length > 0 || signingKey.Length > 0) plan.AddWarning("ENCRYPTION", "PGP_KEY/SIGNING_KEY values are not used for ENCRYPTION=Decrypt and will be ignored.");
        }
        else { plan.AddError("ENCRYPTION", "ENCRYPTION must be Encrypt, Decrypt, Encrypt and Sign, or blank."); return; }

        var script = ResolveNamedObject(context.StandardScripts, scriptName, "Name", "name");
        if (script.Status != ResolutionStatus.Found) { plan.AddError("ENCRYPTION", $"Required MOVEit standard script '{scriptName}' was not found or is ambiguous."); return; }
        var scriptId = ReadString(script.Value!, "ID", "Id", "id");
        if (!long.TryParse(scriptId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericScriptId)) { plan.AddError("ENCRYPTION", $"Standard script '{scriptName}' has an invalid ID."); return; }

        if (!normalizedEncryption.Equals("Decrypt", StringComparison.OrdinalIgnoreCase) && pgpKey.Length > 0)
        {
            var resolved = ResolvePgpKey(context.Keys, pgpKey);
            if (!resolved.Success) plan.AddError("PGP_KEY", resolved.Error);
            else
            {
                var key = resolved.Key!;
                var nativeKeyId = GetPgpTaskKeyToken(key);
                if (nativeKeyId.Length == 0) plan.AddError("PGP_KEY", $"PGP key '{key.Name}' does not expose a MOVEit key ID and cannot be used in a persisted Task.");
                else
                {
                    values["PGP_KEY"] = key.Name;
                    parameters.Add(new JsonObject { ["Name"] = "PGPRecipientKey", ["value"] = nativeKeyId, ["Secure"] = 0 });
                }
            }
        }
        if (normalizedEncryption.Equals("Encrypt and Sign", StringComparison.OrdinalIgnoreCase) && signingKey.Length > 0)
        {
            var resolved = ResolvePgpKey(context.Keys, signingKey);
            if (!resolved.Success) plan.AddError("SIGNING_KEY", resolved.Error);
            else
            {
                var key = resolved.Key!;
                var nativeKeyId = GetPgpTaskKeyToken(key);
                if (nativeKeyId.Length == 0) plan.AddError("SIGNING_KEY", $"PGP signing key '{key.Name}' does not expose a MOVEit key ID and cannot be used in a persisted Task.");
                else
                {
                    values["SIGNING_KEY"] = key.Name;
                    parameters.Add(new JsonObject { ["Name"] = "PGPSignerKey", ["value"] = nativeKeyId, ["Secure"] = 0 });
                }
            }
        }
        if (plan.Errors.Any(issue => issue.Column is "PGP_KEY" or "SIGNING_KEY")) return;
        values["ENCRYPTION"] = normalizedEncryption.Equals("Encrypt and Sign", StringComparison.OrdinalIgnoreCase) ? "Encrypt and Sign" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalizedEncryption.ToLowerInvariant());
        // Preserve CSV's native shape, including Parameters.Parameter: [] for Decrypt.
        // Quick Transfer and CSV now serialize Process steps through the same helper.
        process = _composer.BuildProcessBody(new TraditionalTaskProcessDefinition(
            JsonValue.Create(numericScriptId)!, parameters, IncludeParametersWhenEmpty: true));
    }

    private static JsonArray BuildNotifications(Dictionary<string, string> values, SpreadsheetRowPlan plan, IReadOnlyList<TaskNotificationTemplate> templates, AutomationHost? smtpHost)
    {
        var result = new JsonArray();
        if (smtpHost is null) return result;
        if (!long.TryParse(smtpHost.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var smtpHostId))
        {
            plan.AddError("EMAIL", $"SMTP Host '{smtpHost.Name}' does not expose a usable numeric MOVEit Host ID.");
            return result;
        }
        AddNotification("FAILURE_EMAIL_TO", "FAILURE_EMAIL_TEMPLATE", failure: true, success: false, noAction: false,
            "Automation Task Failure: [TaskName]",
            "----------------------------------------------------------------------------\n\n*** Automation FAILURE ALERT ***\n\nTask: [TaskName]\nTask ID: [TaskID]\nStatus: [TaskStatus]\n\nTask Error: #[ErrorCodeTask] - [ErrorDescriptionTask]\nFile Error: #[ErrorCodeFile] - [ErrorDescriptionFile]\n\n----------------------------------------------------------------------------\n\nImmediate investigation recommended.");
        AddNotification("SUCCESS_EMAIL_TO", "SUCCESS_EMAIL_TEMPLATE", failure: false, success: true, noAction: false,
            "Automation Task Success: [TaskName]", "[TaskName] ([TaskID]) completed successfully.");
        AddNotification("NOACTION_EMAIL_TO", "NOACTION_EMAIL_TEMPLATE", failure: false, success: false, noAction: true,
            "Automation Task: [TaskName]", "[TaskName] ([TaskID]) completed without performing a transfer.");
        return result;

        void AddNotification(string toColumn, string templateColumn, bool failure, bool success, bool noAction, string defaultSubject, string defaultMessage)
        {
            var recipients = Get(values, toColumn);
            if (recipients.Length == 0) return;
            var subject = defaultSubject;
            var message = defaultMessage;
            var isHtml = 0;
            var templateName = Get(values, templateColumn);
            if (templateName.Length > 0)
            {
                var matches = templates.Where(item => item.Name.Equals(templateName, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length != 1)
                {
                    plan.AddError(templateColumn, matches.Length == 0 ? $"Notification template '{templateName}' was not found." : $"Notification template '{templateName}' is ambiguous.");
                    return;
                }
                subject = matches[0].Subject;
                message = matches[0].Message;
                isHtml = string.Equals(matches[0].Format, "HTML", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                values[templateColumn] = matches[0].Name;
            }
            result.Add(new JsonObject
            {
                ["Type"] = "Email", ["DoAfter"] = "Task", ["DoIfFailure"] = failure ? 1 : 0,
                ["DoIfSuccess"] = success ? 1 : 0, ["DoIfNoAction"] = noAction ? 1 : 0,
                ["HostID"] = smtpHostId, ["AddressTo"] = recipients,
                ["Subject"] = subject, ["Message"] = message, ["IsHTML"] = isHtml
            });
        }
    }

    // Backward-compatible contract helper for importer regression tests; the
    // implementation is shared with all future Traditional Task update adapters.
    internal static JsonObject PrepareUpdatePayload(JsonObject current, JsonObject analyzed, bool leaveDisabled)
        => TraditionalTaskDefinitionMerger.PrepareSpreadsheetUpdate(current, analyzed, leaveDisabled);

    private static void CanonicalizeTaskName(Dictionary<string, string> values, SpreadsheetRowPlan plan, string prefix)
    {
        var taskName = Get(values, "TASK_NAME");
        var legacy = Get(values, "REQ_NAME");
        if (taskName.Length > 0 && legacy.Length > 0 && !NormalizeTaskName(taskName).Equals(NormalizeTaskName(legacy), StringComparison.OrdinalIgnoreCase))
            plan.AddError("TASK_NAME", "TASK_NAME and legacy REQ_NAME contain different values. Keep only one value or make them equivalent.");
        var selected = taskName.Length > 0 ? taskName : legacy;
        var normalized = ComposeTaskName(selected, prefix);
        values["TASK_NAME"] = normalized;
        plan.TaskName = normalized;
        if (selected.Length > 0 && !selected.Equals(normalized, StringComparison.Ordinal)) plan.AddWarning("TASK_NAME", $"Task name was normalized to '{normalized}'.");
    }

    internal static string NormalizeTaskName(string? value)
    {
        var upper = (value ?? string.Empty).Trim().ToUpperInvariant();
        return Regex.Replace(upper, @"[^A-Z0-9]+", "_").Trim('_');
    }

    private static void CanonicalizePollWithNotification(Dictionary<string, string> values, SpreadsheetRowPlan plan)
    {
        var canonical = Get(values, "SCHED_POLL_WITH_NOTIF");
        var legacy = Get(values, "SCHED_POLL_WITH_NOTIFY");
        if (canonical.Length > 0 && legacy.Length > 0 && !canonical.Equals(legacy, StringComparison.OrdinalIgnoreCase))
            plan.AddError("SCHED_POLL_WITH_NOTIF", "SCHED_POLL_WITH_NOTIF and legacy SCHED_POLL_WITH_NOTIFY contain different values.");
        if (canonical.Length == 0 && legacy.Length > 0) values["SCHED_POLL_WITH_NOTIF"] = legacy;
    }

    private static void NormalizeYesNo(Dictionary<string, string> values, SpreadsheetRowPlan plan)
    {
        foreach (var column in RequiredStrictYesNoColumns)
        {
            var value = Get(values, column);
            if (value.Length == 0) { plan.AddError(column, $"{column} is required and must be Y or N."); continue; }
            if (!TryStrictYesNo(value, out var normalized)) plan.AddError(column, $"{column} must be Y or N.");
            else values[column] = normalized;
        }
        foreach (var pair in DefaultNoYesNoColumns)
            NormalizeDefaultedYesNo(values, plan, pair.Key, pair.Value);
        foreach (var pair in DefaultYesYesNoColumns)
            NormalizeDefaultedYesNo(values, plan, pair.Key, pair.Value);
    }

    private static void NormalizeDefaultedYesNo(Dictionary<string, string> values, SpreadsheetRowPlan plan, string column, string defaultValue)
    {
        var value = Get(values, column);
        if (value.Length == 0) { values[column] = defaultValue; return; }
        if (!TryStrictYesNo(value, out var normalized)) plan.AddError(column, $"{column} must be Y or N.");
        else values[column] = normalized;
    }

    private static bool TryStrictYesNo(string value, out string normalized)
    {
        normalized = string.Empty;
        if (value.Trim().Equals("Y", StringComparison.OrdinalIgnoreCase)) { normalized = "Y"; return true; }
        if (value.Trim().Equals("N", StringComparison.OrdinalIgnoreCase)) { normalized = "N"; return true; }
        return false;
    }

    private static void NormalizeFileMasks(Dictionary<string, string> values, SpreadsheetRowPlan plan)
    {
        var source = Get(values, "SOURCE_FILE");
        if (source.Contains(','))
        {
            var normalized = string.Join(';', source.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            values["SOURCE_FILE"] = normalized;
            plan.AddWarning("SOURCE_FILE", $"SOURCE_FILE was normalized from comma-separated masks to '{normalized}'.");
        }
    }

    private static void NormalizeMacros(Dictionary<string, string> values, SpreadsheetRowPlan plan)
    {
        foreach (var column in new[] { "SOURCE_PATH", "SOURCE_FILE", "DEST_PATH", "DEST_FILE", "ARCHIVE_PATH", "ARCHIVE_FILE" })
        {
            var original = Get(values, column);
            if (original.Length == 0) continue;
            foreach (var warning in TaskSpreadsheetMacroTranslator.GetDateTimeMacroWarnings(original, column, column.Contains("PATH", StringComparison.OrdinalIgnoreCase))) plan.AddWarning(column, warning);
            var translated = TaskSpreadsheetMacroTranslator.TranslateKnownPatterns(original);
            translated = TaskSpreadsheetMacroTranslator.NormalizeMacroDelimiters(translated, column, out var warnings);
            foreach (var warning in warnings) plan.AddWarning(column, warning);
            // Host-aware path normalization happens after the Host is resolved. Root-based Hosts need
            // to preserve whether the imported value began with a separator because `/folder` means
            // "use the Host default root", while `root/folder` explicitly selects a root.
            if (!original.Equals(translated, StringComparison.Ordinal)) plan.AddWarning(column, $"{column} was normalized to '{translated}'.");
            values[column] = translated;
        }
    }

    private static void NormalizeEmailAddresses(Dictionary<string, string> values, SpreadsheetRowPlan plan)
    {
        foreach (var column in new[] { "FAILURE_EMAIL_TO", "SUCCESS_EMAIL_TO", "NOACTION_EMAIL_TO" })
        {
            var original = Get(values, column);
            if (original.Length == 0) continue;
            var normalized = original.Replace(';', ',');
            if (!original.Equals(normalized, StringComparison.Ordinal)) plan.AddWarning(column, $"{column} semicolon separators were normalized to commas.");
            var addresses = normalized.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (addresses.Length == 0 || addresses.Any(address => !LooksLikeEmail(address)))
                plan.AddWarning(column, $"{column} contains one or more email addresses that do not appear to be valid.");
            values[column] = normalized;
        }
    }

    private static bool LooksLikeEmail(string value)
    {
        try { return new System.Net.Mail.MailAddress(value).Address.Equals(value, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    private HostResolution ResolveHost(string hostValue, string relatedPath, IReadOnlyList<AutomationHost> hosts, string role)
    {
        if (string.IsNullOrWhiteSpace(hostValue)) return HostResolution.Fail("Host value is blank.");
        var friendly = hosts.Where(host => host.Name.Trim().Equals(hostValue.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (friendly.Length == 1) return ValidateSupportedHost(friendly[0], role);
        if (friendly.Length > 1) return HostResolution.Fail($"Friendly Name '{hostValue}' is ambiguous. Matching hosts: {FormatHosts(friendly)}.");

        // Preserve the proven ImportFromCSV endpoint fallback only for the Host types where that behavior
        // was already captured. Newer/cloud Host types must be referenced by MOVEit Friendly Name.
        var matches = new List<AutomationHost>();
        var input = hostValue.Trim();
        var inputUnc = IsFullUncPath(input);
        foreach (var host in hosts)
        {
            if (host.Destination.Length == 0) continue;
            if (IsFtpType(host))
            {
                if (!inputUnc && NormalizeHostAddress(input).Equals(NormalizeHostAddress(host.Destination), StringComparison.OrdinalIgnoreCase)) matches.Add(host);
            }
            else if (host.TypeCode.Equals("Share", StringComparison.OrdinalIgnoreCase))
            {
                if (inputUnc && (AreEquivalentUncPaths(input, host.Destination) || UncPathBeginsWithResourceUnc(input, host.Destination))) matches.Add(host);
                else if (!inputUnc && (NormalizeHostAddress(input).Equals(GetUncServer(host.Destination), StringComparison.OrdinalIgnoreCase) ||
                         (IsFullUncPath(relatedPath) && HostReferenceMatchesRelatedUncPath(input, relatedPath, host.Destination)))) matches.Add(host);
            }
        }
        matches = matches.GroupBy(host => host.Id, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToList();
        if (matches.Count == 1) return ValidateSupportedHost(matches[0], role);
        if (matches.Count > 1) return HostResolution.Fail($"Host value '{hostValue}' matches multiple Automation hosts: {FormatHosts(matches)}. Enter the Automation Friendly Name explicitly.");
        return HostResolution.Fail($"Host '{hostValue}' was not found. Enter the MOVEit Automation Friendly Name; legacy FTP/SFTP endpoint and Share UNC matching are also supported.");
    }

    private HostResolution ValidateSupportedHost(AutomationHost host, string role)
    {
        if (host.Id.Equals("0", StringComparison.OrdinalIgnoreCase) || host.TypeCode.Equals("FileSystem", StringComparison.OrdinalIgnoreCase))
            return HostResolution.Fail("The MOVEit (Default) Local File System Host is not supported by Task CSV/XLSX import. Select a saved file-transfer Host.");
        if (!long.TryParse(host.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            return HostResolution.Fail($"Host '{host.Name}' does not expose a usable numeric MOVEit Host ID.");

        var profile = _profiles.Find(host.TypeCode);
        if (profile is null || !SupportedTransferHostTypes.Contains(profile.TypeCode))
            return HostResolution.Fail($"Host '{host.Name}' is type '{host.TypeCode}'. Task CSV/XLSX import supports UNC Share, MOVEit Transfer, FTP/FTPS, SSH/SFTP, Amazon S3, Azure Blob, SharePoint, and Google Storage hosts.");

        var sourceRole = role.Equals("Source", StringComparison.OrdinalIgnoreCase);
        if (sourceRole ? !profile.SourceWriteSupported : !profile.DestinationWriteSupported)
            return HostResolution.Fail($"Host '{host.Name}' ({profile.DisplayName}) cannot be used as a {role} in the verified Traditional Task contract.");

        return HostResolution.Ok(host, profile);
    }

    private async Task<LocationResolution> ResolveImportedLocationAsync(
        AutomationHost host,
        TraditionalTaskHostProfile profile,
        string value,
        string column,
        AnalyzeContext context,
        CancellationToken cancellationToken)
    {
        var imported = (value ?? string.Empty).Trim();
        if (profile.RootKind == TraditionalTaskLocationRootKind.None)
        {
            var normalized = NormalizePathForResolvedHost(imported, host);
            return normalized.Success
                ? LocationResolution.Ok(new ResolvedHostLocation(host, profile, normalized.Value, string.Empty, string.Empty, normalized.Value))
                : LocationResolution.Fail(normalized.Error);
        }

        if (IsFullUncPath(imported))
            return LocationResolution.Fail($"Path '{value}' begins with a UNC prefix, but Host '{host.Name}' uses a {profile.DisplayName} root. Use '/folder' for the Host default root or 'root/folder' to explicitly select a root.");

        var parsed = ParseRootedLocation(imported);
        if (!parsed.Success) return LocationResolution.Fail(parsed.Error);

        if (parsed.UseDefaultRoot)
        {
            return LocationResolution.Ok(new ResolvedHostLocation(
                host, profile, parsed.TaskPath, string.Empty, string.Empty, parsed.DisplayPath));
        }

        if (profile.TypeCode.Equals("AzureBlob", StringComparison.OrdinalIgnoreCase)
            && (parsed.RootName.Equals("$logs", StringComparison.OrdinalIgnoreCase)
                || parsed.RootName.Equals("$blobchangefeed", StringComparison.OrdinalIgnoreCase)))
            return LocationResolution.Fail($"Azure container '{parsed.RootName}' cannot be selected through the verified MOVEit browse contract. Use the Host default container or select another container.");

        var catalog = await GetRootCatalogAsync(host, context, cancellationToken);
        if (!catalog.Success)
            return LocationResolution.Fail($"{column} explicitly selects {DescribeRoot(profile.RootKind)} '{parsed.RootName}', but MAUI could not validate that root for Host '{host.Name}': {catalog.Error}");

        var matches = catalog.Items.Where(item => item.Name.Equals(parsed.RootName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0)
            return LocationResolution.Fail($"{DescribeRoot(profile.RootKind)} '{parsed.RootName}' was not found for Host '{host.Name}'. Use '/folder' to use the Host default root, or specify a root returned by MOVEit Browse.");
        if (matches.Length > 1)
            return LocationResolution.Fail($"{DescribeRoot(profile.RootKind)} '{parsed.RootName}' is ambiguous for Host '{host.Name}'.");

        var selected = matches[0];
        if (profile.RootKind == TraditionalTaskLocationRootKind.DocumentLibrary && string.IsNullOrWhiteSpace(selected.NativeId))
            return LocationResolution.Fail($"SharePoint Document Library '{selected.Name}' did not include the MOVEit library GUID required by the Traditional Task contract.");

        var display = selected.Name + (parsed.TaskPath == "/" ? string.Empty : parsed.TaskPath);
        return LocationResolution.Ok(new ResolvedHostLocation(
            host, profile, parsed.TaskPath, selected.Name, selected.NativeId, display));
    }

    private async Task<RootCatalogResult> GetRootCatalogAsync(AutomationHost host, AnalyzeContext context, CancellationToken cancellationToken)
    {
        if (context.RootCatalogs.TryGetValue(host.Id, out var cached)) return cached;

        try
        {
            var browsed = await _browse.BrowseAsync(host.Id, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, cancellationToken);
            var result = RootCatalogResult.Ok(browsed.Items);
            context.RootCatalogs[host.Id] = result;
            return result;
        }
        catch (MoveItAuthenticationException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var result = RootCatalogResult.Fail(ex.Message);
            context.RootCatalogs[host.Id] = result;
            return result;
        }
    }

    internal static RootedLocationParseResult ParseRootedLocation(string value)
    {
        var original = (value ?? string.Empty).Trim();
        if (original.Length == 0) return RootedLocationParseResult.Fail("Path is blank.");

        var normalized = original.Replace('\\', '/');
        while (normalized.Contains("//", StringComparison.Ordinal)) normalized = normalized.Replace("//", "/", StringComparison.Ordinal);

        if (normalized.StartsWith("/", StringComparison.Ordinal))
        {
            var taskPath = TaskSpreadsheetPathNormalizer.NormalizeForNonShareHost(normalized);
            return RootedLocationParseResult.Ok(true, string.Empty, taskPath, taskPath);
        }

        var slash = normalized.IndexOf('/');
        var rootName = slash < 0 ? normalized : normalized[..slash];
        if (string.IsNullOrWhiteSpace(rootName)) return RootedLocationParseResult.Fail("The explicit root name is blank.");
        var remainder = slash < 0 ? string.Empty : normalized[(slash + 1)..];
        var path = remainder.Length == 0 ? "/" : TaskSpreadsheetPathNormalizer.NormalizeForNonShareHost(remainder);
        var display = rootName + (path == "/" ? string.Empty : path);
        return RootedLocationParseResult.Ok(false, rootName, path, display);
    }

    private static string DescribeRoot(TraditionalTaskLocationRootKind kind) => kind switch
    {
        TraditionalTaskLocationRootKind.Bucket => "bucket",
        TraditionalTaskLocationRootKind.Container => "container",
        TraditionalTaskLocationRootKind.DocumentLibrary => "Document Library",
        _ => "root"
    };

    private static PathResolution NormalizePathForResolvedHost(string value, AutomationHost host)
    {
        if (string.IsNullOrWhiteSpace(value)) return PathResolution.Ok(string.Empty);
        var path = value.Trim();
        if (!IsFullUncPath(path))
            return PathResolution.Ok(host.TypeCode.Equals("Share", StringComparison.OrdinalIgnoreCase)
                ? TaskSpreadsheetPathNormalizer.NormalizeForShareHost(path)
                : TaskSpreadsheetPathNormalizer.NormalizeForNonShareHost(path));
        if (!host.TypeCode.Equals("Share", StringComparison.OrdinalIgnoreCase))
            return PathResolution.Fail($"Path '{value}' begins with a UNC prefix, but host '{host.Name}' is type '{host.TypeCode}'. Enter a host-relative path for non-Share hosts.");
        if (!TryParseUncPath(path, out var importedServer, out var importedComponents) || !TryParseUncPath(host.Destination, out var resourceServer, out var resourceComponents))
            return PathResolution.Fail($"Share host '{host.Name}' does not expose a usable Resource UNC or the imported path is invalid.");
        if (!AreEquivalentUncServers(importedServer, resourceServer) || resourceComponents.Count > importedComponents.Count)
            return PathResolution.Fail($"Path '{value}' does not begin with Resource UNC '{host.Destination}' configured for Share host '{host.Name}'.");
        for (var i = 0; i < resourceComponents.Count; i++)
            if (!importedComponents[i].Equals(resourceComponents[i], StringComparison.OrdinalIgnoreCase))
                return PathResolution.Fail($"Path '{value}' does not begin with Resource UNC '{host.Destination}' configured for Share host '{host.Name}'.");
        var remaining = importedComponents.Skip(resourceComponents.Count).ToArray();
        return PathResolution.Ok(remaining.Length == 0 ? @"\" : @"\" + string.Join(@"\", remaining));
    }

    private static PgpResolution ResolvePgpKey(IReadOnlyList<AutomationKeyCertificate> keys, string requested)
    {
        var candidates = keys.Where(key => key.Kind == KeyCertificateKind.Pgp && !IsExpired(key)).ToArray();
        var trimmed = requested.Trim();
        var normalizedRequestedId = NormalizeHex(trimmed);
        var direct = candidates.Where(key =>
                key.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)
                || key.Uid.Equals(trimmed, StringComparison.OrdinalIgnoreCase)
                || key.Id.Equals(trimmed, StringComparison.OrdinalIgnoreCase)
                || (normalizedRequestedId.Length >= 8 && NormalizeHex(key.Id).Equals(normalizedRequestedId, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (direct.Length == 1) return PgpResolution.Ok(direct[0]);
        if (direct.Length > 1) return PgpResolution.Fail($"PGP key '{requested}' is ambiguous.");
        var fingerprint = normalizedRequestedId;
        if (fingerprint.Length >= 8)
        {
            var suffix = candidates.Where(key =>
            {
                var candidate = NormalizeHex(key.Fingerprint);
                return candidate.Length >= 8
                    && (candidate.EndsWith(fingerprint, StringComparison.OrdinalIgnoreCase)
                        || fingerprint.EndsWith(candidate, StringComparison.OrdinalIgnoreCase));
            }).ToArray();
            if (suffix.Length == 1) return PgpResolution.Ok(suffix[0]);
            if (suffix.Length > 1) return PgpResolution.Fail($"PGP key fingerprint '{requested}' is ambiguous.");
        }
        return PgpResolution.Fail($"PGP key '{requested}' was not found, is expired, or is not visible to the current MOVEit user.");
    }

    internal static string GetPgpTaskKeyToken(AutomationKeyCertificate key) => key.Id.Trim();

    private static bool IsExpired(AutomationKeyCertificate key)
    {
        if (key.Status.Contains("expired", StringComparison.OrdinalIgnoreCase)) return true;
        return DateTimeOffset.TryParse(key.ExpirationDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var expiration) && expiration <= DateTimeOffset.Now;
    }

    private static string NormalizeHex(string value) => Regex.Replace(value ?? string.Empty, "[^0-9A-Fa-f]", string.Empty).ToUpperInvariant();

    private static ObjectResolution ResolveNamedObject(IReadOnlyList<JsonObject> items, string value, params string[] nameProperties)
    {
        var requested = (value ?? string.Empty).Trim();
        if (requested.Length == 0) return new(ResolutionStatus.Missing, null, "Value is blank.");
        var matches = items.Where(item => nameProperties.Any(property => ReadString(item, property).Equals(requested, StringComparison.OrdinalIgnoreCase))).ToArray();
        return matches.Length switch
        {
            1 => new(ResolutionStatus.Found, matches[0], string.Empty),
            > 1 => new(ResolutionStatus.Ambiguous, null, $"'{requested}' is ambiguous."),
            _ => new(ResolutionStatus.Missing, null, $"'{requested}' was not found.")
        };
    }

    private static bool LooksLikeDayOfMonthExpression(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Equals("ALL", StringComparison.OrdinalIgnoreCase)) return true;
        return Regex.IsMatch(trimmed, @"^[0-9,;\-\s]+$");
    }

    internal static void ValidateRequiredHeaders(HashSet<string> headers, List<TaskSpreadsheetImportIssue> issues)
    {
        if (!headers.Contains("TASK_NAME") && !headers.Contains("REQ_NAME")) issues.Add(new("Error", null, "TASK_NAME", "The file must contain TASK_NAME (legacy REQ_NAME is also accepted)."));
        foreach (var column in RequiredValueColumns.Where(column => !column.Equals("TASK_NAME", StringComparison.OrdinalIgnoreCase)))
            if (!headers.Contains(column)) issues.Add(new("Error", null, column, $"Required column {column} is missing."));
        foreach (var column in RequiredStrictYesNoColumns)
            if (!headers.Contains(column)) issues.Add(new("Error", null, column, $"Required column {column} is missing."));
        foreach (var column in DefaultNoYesNoColumns.Keys)
        {
            if (column.Equals("SCHED_POLL_WITH_NOTIF", StringComparison.OrdinalIgnoreCase) && (headers.Contains("SCHED_POLL_WITH_NOTIF") || headers.Contains("SCHED_POLL_WITH_NOTIFY"))) continue;
            if (!headers.Contains(column)) issues.Add(new("Error", null, column, $"Required column {column} is missing. Values may be blank to use the default N."));
        }
    }

    internal static string ComposeTaskName(string? name, string? prefix)
    {
        var normalizedPrefix = NormalizePrefix(prefix);
        var normalizedName = NormalizeTaskName(name);
        return normalizedName.Length == 0 ? string.Empty : normalizedPrefix + normalizedName;
    }

    private static string NormalizePrefix(string? prefix)
    {
        var normalized = (prefix ?? string.Empty).Trim();
        if (normalized.Length > 60)
            throw new ArgumentException("Task Name Prefix must be 60 characters or fewer.");
        return normalized;
    }

    private SpreadsheetAnalysisState GetAnalysis(string analysisId)
    {
        var key = (analysisId ?? string.Empty).Trim();
        if (key.Length == 0 || !_cache.TryGetValue(CachePrefix + key, out SpreadsheetAnalysisState? state) || state is null)
            throw new InvalidOperationException("This analyzed import has expired or is no longer available. Analyze the file again.");
        return state;
    }

    private static TaskSpreadsheetAnalysisResult ToPublicResult(SpreadsheetAnalysisState state, IReadOnlyList<TaskSpreadsheetImportIssue> globalIssues)
    {
        var rows = state.Plans.Select(plan => new TaskSpreadsheetPreviewRow(plan.RowNumber, plan.Errors.Count > 0 ? "Error" : plan.Warnings.Count > 0 ? "Warning" : "Valid",
            new Dictionary<string, string>(plan.Values, StringComparer.OrdinalIgnoreCase), plan.Errors.Concat(plan.Warnings).ToArray())).ToArray();
        var issues = globalIssues.Concat(rows.SelectMany(row => row.Issues)).ToArray();
        var errors = rows.Count(row => row.Status == "Error");
        var hasGlobalErrors = globalIssues.Any(issue => IsError(issue.Severity));
        return new(state.AnalysisId, state.FileName, state.Prefix, state.UpdateExisting, state.LeaveTasksDisabled, state.Columns, rows, issues,
            rows.Count(row => row.Status == "Valid"), rows.Count(row => row.Status == "Warning"), errors, state.IgnoredColumns, !hasGlobalErrors && errors == 0 && rows.Length > 0);
    }

    private MoveItSessionState RequireSession() => _session.Get() ?? throw new MoveItAuthenticationException("Your MOVEit session has expired. Sign in again.");
    private static string Get(IReadOnlyDictionary<string, string> values, string key) => values.TryGetValue(key, out var value) ? (value ?? string.Empty).Trim() : string.Empty;
    private static bool IsYes(IReadOnlyDictionary<string, string> values, string key) => Get(values, key).Equals("Y", StringComparison.OrdinalIgnoreCase);
    private static bool IsError(string severity) => severity.Equals("Error", StringComparison.OrdinalIgnoreCase);
    private static long ParseLong(string value, string field) => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : throw new InvalidOperationException($"{field} '{value}' is not numeric.");
    private static string NormalizeHeader(string value) => Regex.Replace((value ?? string.Empty).Trim().ToUpperInvariant(), @"[^A-Z0-9]+", "_").Trim('_');
    internal static bool IsTraditionalTask(AutomationTask task) => task.Type.Equals("Traditional", StringComparison.OrdinalIgnoreCase);
    internal static bool IsTraditionalTaskDefinition(JsonObject task)
    {
        var taskType = ReadString(task, "TT", "tt");
        return !taskType.Equals("COS", StringComparison.OrdinalIgnoreCase)
            && !taskType.Equals("Sy", StringComparison.OrdinalIgnoreCase);
    }
    private static bool IsFtpType(AutomationHost host) => host.TypeCode.Equals("FTP", StringComparison.OrdinalIgnoreCase) || host.TypeCode.Equals("SSHFTP", StringComparison.OrdinalIgnoreCase);
    private static string NormalizeHostAddress(string value) => (value ?? string.Empty).Trim().TrimEnd('.');
    private static string FormatHosts(IEnumerable<AutomationHost> hosts) => string.Join(", ", hosts.OrderBy(host => host.Name).Select(host => $"'{host.Name}' ({host.TypeCode})"));
    private static void WarnIfChanged(SpreadsheetRowPlan plan, string column, string original, string normalized) { if (!string.Equals(original?.Trim(), normalized?.Trim(), StringComparison.Ordinal)) plan.AddWarning(column, $"{column} was normalized to '{normalized}'."); }

    private static string ReadString(JsonObject obj, params string[] properties)
    {
        foreach (var property in properties)
        {
            if (!obj.TryGetPropertyValue(property, out var node) || node is null) continue;
            if (node is JsonValue value && value.TryGetValue<string>(out var text)) return (text ?? string.Empty).Trim();
            return node.ToString().Trim('"').Trim();
        }
        return string.Empty;
    }

    private static bool IsFullUncPath(string value) => (value ?? string.Empty).Trim().StartsWith(@"\\", StringComparison.Ordinal) || (value ?? string.Empty).Trim().StartsWith("//", StringComparison.Ordinal);
    private static string GetUncServer(string value) => TryParseUncPath(value, out var server, out _) ? server : string.Empty;
    private static bool TryParseUncPath(string value, out string server, out List<string> components)
    {
        server = string.Empty; components = new();
        var normalized = (value ?? string.Empty).Trim().Replace('/', '\\');
        if (!normalized.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        var parts = normalized[2..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        server = NormalizeHostAddress(parts[0]); components.AddRange(parts.Skip(1)); return server.Length > 0;
    }
    private static bool AreEquivalentUncServers(string left, string right)
    {
        var a = NormalizeHostAddress(left); var b = NormalizeHostAddress(right);
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase)) return true;
        if (IPAddress.TryParse(a, out _) || IPAddress.TryParse(b, out _)) return false;
        return a.Split('.')[0].Equals(b.Split('.')[0], StringComparison.OrdinalIgnoreCase);
    }
    private static bool AreEquivalentUncPaths(string left, string right)
    {
        if (!TryParseUncPath(left, out var ls, out var lc) || !TryParseUncPath(right, out var rs, out var rc) || !AreEquivalentUncServers(ls, rs) || lc.Count != rc.Count) return false;
        return lc.Zip(rc).All(pair => pair.First.Equals(pair.Second, StringComparison.OrdinalIgnoreCase));
    }
    private static bool UncPathBeginsWithResourceUnc(string path, string resource)
    {
        if (!TryParseUncPath(path, out var ps, out var pc) || !TryParseUncPath(resource, out var rs, out var rc) || !AreEquivalentUncServers(ps, rs) || rc.Count > pc.Count) return false;
        return rc.Select((part, index) => part.Equals(pc[index], StringComparison.OrdinalIgnoreCase)).All(value => value);
    }
    private static bool HostReferenceMatchesRelatedUncPath(string reference, string relatedPath, string resource)
    {
        if (!TryParseUncPath(relatedPath, out var pathServer, out _) || !TryParseUncPath(resource, out var resourceServer, out _)) return false;
        var referenceServer = IsFullUncPath(reference) && TryParseUncPath(reference, out var parsed, out _) ? parsed : NormalizeHostAddress(reference);
        return AreEquivalentUncServers(referenceServer, pathServer) && AreEquivalentUncServers(pathServer, resourceServer) && UncPathBeginsWithResourceUnc(relatedPath, resource);
    }

    private sealed class SpreadsheetRowPlan
    {
        public SpreadsheetRowPlan(int rowNumber, Dictionary<string, string> values) { RowNumber = rowNumber; Values = values; }
        public int RowNumber { get; }
        public Dictionary<string, string> Values { get; }
        public string TaskName { get; set; } = string.Empty;
        public string ExistingTaskId { get; set; } = string.Empty;
        public List<TaskSpreadsheetImportIssue> Errors { get; } = new();
        public List<TaskSpreadsheetImportIssue> Warnings { get; } = new();
        public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public JsonObject TaskPayload { get; set; } = new();
        public void AddError(string column, string message) => Errors.Add(new("Error", RowNumber, column, message));
        public void AddWarning(string column, string message) => Warnings.Add(new("Warning", RowNumber, column, message));
    }

    private sealed record SpreadsheetAnalysisState(string AnalysisId, string EnvironmentKey, string Username, string FileName, string Prefix, bool UpdateExisting, bool LeaveTasksDisabled, IReadOnlyList<string> Columns, IReadOnlyList<SpreadsheetRowPlan> Plans, int IgnoredColumns, bool HasGlobalErrors)
    {
        public ConcurrentDictionary<int, byte> CompletedRows { get; } = new();
        public ConcurrentDictionary<int, byte> InProgressRows { get; } = new();
    }
    private sealed record AnalyzeContext(IReadOnlyList<AutomationTask> Tasks, IReadOnlyList<AutomationHost> Hosts, IReadOnlyList<JsonObject> ResourceGroups, IReadOnlyList<JsonObject> DateLists, IReadOnlyList<JsonObject> StandardScripts, IReadOnlyList<TaskNotificationTemplate> Templates, IReadOnlyList<AutomationKeyCertificate> Keys, bool PgpLicensed, IReadOnlyDictionary<string, MetadataFieldDefinition> MetadataFields, string EnvironmentKey, string Prefix, bool UpdateExisting)
    {
        // Root discovery can require a native Host checkout. Cache one root listing per Host for the
        // duration of Analyze so a spreadsheet with many rows does not repeatedly browse the same Host.
        public Dictionary<string, RootCatalogResult> RootCatalogs { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
    private enum ResolutionStatus { Found, Missing, Ambiguous }
    private sealed record ObjectResolution(ResolutionStatus Status, JsonObject? Value, string Message);
    private sealed record HostResolution(bool Success, AutomationHost? Host, TraditionalTaskHostProfile? Profile, string Error)
    {
        public static HostResolution Ok(AutomationHost host, TraditionalTaskHostProfile profile) => new(true, host, profile, string.Empty);
        public static HostResolution Fail(string error) => new(false, null, null, error);
    }
    private sealed record ResolvedHostLocation(AutomationHost Host, TraditionalTaskHostProfile Profile, string TaskPath, string RootName, string RootId, string DisplayPath);
    private sealed record LocationResolution(bool Success, ResolvedHostLocation? Location, string Error)
    {
        public static LocationResolution Ok(ResolvedHostLocation location) => new(true, location, string.Empty);
        public static LocationResolution Fail(string error) => new(false, null, error);
    }
    private sealed record RootCatalogResult(bool Success, IReadOnlyList<AutomationHostBrowseItem> Items, string Error)
    {
        public static RootCatalogResult Ok(IReadOnlyList<AutomationHostBrowseItem> items) => new(true, items, string.Empty);
        public static RootCatalogResult Fail(string error) => new(false, Array.Empty<AutomationHostBrowseItem>(), error);
    }
    internal sealed record RootedLocationParseResult(bool Success, bool UseDefaultRoot, string RootName, string TaskPath, string DisplayPath, string Error)
    {
        public static RootedLocationParseResult Ok(bool useDefaultRoot, string rootName, string taskPath, string displayPath)
            => new(true, useDefaultRoot, rootName, taskPath, displayPath, string.Empty);
        public static RootedLocationParseResult Fail(string error)
            => new(false, false, string.Empty, string.Empty, string.Empty, error);
    }
    private sealed record PathResolution(bool Success, string Value, string Error) { public static PathResolution Ok(string value) => new(true, value, string.Empty); public static PathResolution Fail(string error) => new(false, string.Empty, error); }
    private sealed record PgpResolution(bool Success, AutomationKeyCertificate? Key, string Error) { public static PgpResolution Ok(AutomationKeyCertificate key) => new(true, key, string.Empty); public static PgpResolution Fail(string error) => new(false, null, error); }
}

