// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Read-only Task preflight checks. Tests only behavior MAUI can verify without running or modifying the Task.

using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt.Tasks;

namespace MoveItAutomationUI.Services.MoveIt;

public interface ITaskPreflightService
{
    Task<TaskPreflightStageResult> RunStageAsync(string taskId, string stageKey, CancellationToken cancellationToken = default);
}

public static class TaskPreflightStages
{
    public const string Task = "task";
    public const string Hosts = "hosts";
    public const string Resources = "resources";
    public const string Certificates = "certificates";
    public const string Notifications = "notifications";
    public const string Scheduling = "scheduling";
    public const string Configuration = "configuration";
    public const string Runtime = "runtime";

    public static readonly IReadOnlyList<string> Ordered =
        new[] { Task, Hosts, Resources, Certificates, Notifications, Scheduling, Configuration, Runtime };
}

public static class TaskPreflightStatuses
{
    public const string Pass = "pass";
    public const string Warning = "warning";
    public const string Fail = "fail";
    public const string Unknown = "unknown";
    public const string Info = "info";
}

public sealed record TaskPreflightCheck(
    string Status,
    string Title,
    string Message);

public sealed record TaskPreflightStageResult(
    string TaskId,
    string TaskName,
    string Stage,
    string Status,
    IReadOnlyList<TaskPreflightCheck> Checks);

public sealed class TaskPreflightService : ITaskPreflightService
{
    private readonly IMoveItRestClient _rest;
    private readonly ITaskCrudCoordinator _taskCrud;
    private readonly IAutomationTaskService _tasks;
    private readonly IAutomationHostService _hosts;
    private readonly IHostConnectionTestService _hostTests;
    private readonly IAutomationScriptService _scripts;
    private readonly IAutomationDateListService _dateLists;
    private readonly IAutomationGlobalParameterService _globalParameters;
    private readonly IAutomationKeyCertificateService _keys;

    public TaskPreflightService(
        IMoveItRestClient rest,
        ITaskCrudCoordinator taskCrud,
        IAutomationTaskService tasks,
        IAutomationHostService hosts,
        IHostConnectionTestService hostTests,
        IAutomationScriptService scripts,
        IAutomationDateListService dateLists,
        IAutomationGlobalParameterService globalParameters,
        IAutomationKeyCertificateService keys)
    {
        _rest = rest;
        _taskCrud = taskCrud;
        _tasks = tasks;
        _hosts = hosts;
        _hostTests = hostTests;
        _scripts = scripts;
        _dateLists = dateLists;
        _globalParameters = globalParameters;
        _keys = keys;
    }

    public async Task<TaskPreflightStageResult> RunStageAsync(string taskId, string stageKey, CancellationToken cancellationToken = default)
    {
        var id = (taskId ?? string.Empty).Trim();
        if (id.Length == 0) throw new ArgumentException("Task ID is required.", nameof(taskId));
        var stage = NormalizeStage(stageKey);
        var task = await _taskCrud.GetDefinitionAsync(id, cancellationToken);
        var actualId = FirstNonBlank(Read(task, "ID"), id);
        var taskName = FirstNonBlank(Read(task, "Name"), actualId);

        IReadOnlyList<TaskPreflightCheck> checks = stage switch
        {
            TaskPreflightStages.Task => await CheckTaskAsync(task, cancellationToken),
            TaskPreflightStages.Hosts => await CheckHostsAsync(task, cancellationToken),
            TaskPreflightStages.Resources => await CheckResourcesAsync(task, cancellationToken),
            TaskPreflightStages.Certificates => await CheckCertificatesAsync(task, cancellationToken),
            TaskPreflightStages.Notifications => await CheckNotificationsAsync(task, cancellationToken),
            TaskPreflightStages.Scheduling => await CheckSchedulingAsync(task, cancellationToken),
            TaskPreflightStages.Configuration => await CheckConfigurationAsync(task, cancellationToken),
            TaskPreflightStages.Runtime => await CheckRuntimeAsync(task, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(stageKey))
        };

        return new TaskPreflightStageResult(actualId, taskName, stage, StageStatus(checks), checks);
    }

    private static Task<IReadOnlyList<TaskPreflightCheck>> CheckTaskAsync(JsonObject task, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var checks = new List<TaskPreflightCheck>();
        var tt = Read(task, "TT");
        var type = tt.Equals("COS", StringComparison.OrdinalIgnoreCase) ? "Advanced"
            : tt.Equals("Sy", StringComparison.OrdinalIgnoreCase) ? "Synchronization" : "Traditional";
        var scheduled = Read(task, "Scheduled");
        var active = ReadBool(GetNode(task, "Active"));

        checks.Add(new(TaskPreflightStatuses.Pass, "Task Configuration", $"{type} Task configuration loaded."));
        if (scheduled.Equals("INCOMPLETE", StringComparison.OrdinalIgnoreCase))
            checks.Add(new(TaskPreflightStatuses.Fail, "Task State", "MOVEit reports this Task as incomplete. It cannot be run until the configuration is completed."));
        else if (!active)
            checks.Add(new(TaskPreflightStatuses.Info, "Task State", "The Task is disabled. It can still be run manually, but it will not launch normally from its schedule."));
        else
            checks.Add(new(TaskPreflightStatuses.Pass, "Task State", "Task is enabled."));

        return Task.FromResult<IReadOnlyList<TaskPreflightCheck>>(checks);
    }

    private async Task<IReadOnlyList<TaskPreflightCheck>> CheckHostsAsync(JsonObject task, CancellationToken cancellationToken)
    {
        var checks = new List<TaskPreflightCheck>();
        var references = CollectHostLocations(task);
        if (references.Count == 0)
        {
            checks.Add(new(TaskPreflightStatuses.Info, "Host References", "No Source or Destination Host references were found in the Task definition."));
            return checks;
        }

        var inventory = await _hosts.GetHostsAsync(cancellationToken);
        var byId = inventory.Where(host => host.Id.Length > 0).GroupBy(host => host.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var group in references.GroupBy(reference => reference.HostId, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hostId = group.Key;
            if (hostId == "0")
            {
                checks.Add(new(TaskPreflightStatuses.Pass, "Local File System", "Local File System Host is available."));
                foreach (var reference in group.Where(item => !string.IsNullOrWhiteSpace(item.Directory)))
                    checks.Add(new(TaskPreflightStatuses.Unknown, $"{reference.Role} Path", $"'{reference.Directory}' is server-local and is not browsed remotely by MAUI Preflight."));
                continue;
            }

            if (!byId.TryGetValue(hostId, out var host))
            {
                checks.Add(new(TaskPreflightStatuses.Fail, "Host Reference", $"The Task references Host ID '{hostId}', but that Host is not visible in the current MOVEit inventory."));
                continue;
            }

            // Always run the established Host Health connection test once. Path browsing is an additional
            // validation and must not replace Host Health for Host types whose path-browse pattern is unknown.
            var connection = await _hostTests.TestHostAsync(hostId, cancellationToken);
            checks.Add(MapHostTest(connection, $"Host — {host.Name}"));

            var concreteLocations = group.Where(reference => IsConcreteBrowsePath(reference.Directory)).ToList();
            if (connection.Status != HostConnectionTestService.StatusPassed)
            {
                foreach (var reference in concreteLocations)
                    checks.Add(new(TaskPreflightStatuses.Unknown, $"{reference.Role} Path", $"Path browsing for '{reference.Directory}' was not attempted because the Host connection test did not pass."));
            }
            else foreach (var reference in concreteLocations)
            {
                if (reference.Role.Equals("Source", StringComparison.OrdinalIgnoreCase) && reference.BlindDownload)
                {
                    checks.Add(new(TaskPreflightStatuses.Unknown, "Source Path", $"The configured source '{reference.Directory}' uses Blind Downloads, so MOVEit does not provide a directory listing for Preflight path validation."));
                    continue;
                }

                var browseMask = IsSafeBrowseMask(reference.FileMask) ? reference.FileMask : string.Empty;
                var result = await _hostTests.TestHostPathAsync(hostId, reference.Directory, browseMask, cancellationToken);

                if (result.Status == HostConnectionTestService.StatusPassed)
                {
                    var pathText = string.IsNullOrWhiteSpace(browseMask)
                        ? $"Directory '{reference.Directory}' is accessible."
                        : $"Directory '{reference.Directory}' is accessible with file mask '{browseMask}'.";
                    checks.Add(new(TaskPreflightStatuses.Pass, $"{reference.Role} Path", pathText));

                    if (reference.Role.Equals("Source", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(browseMask)
                        && result.FileCount == 0)
                    {
                        checks.Add(new(TaskPreflightStatuses.Info, "Source Files", $"The source directory is accessible, but no files were returned for the configured mask '{browseMask}'. This can be normal when the source is currently empty."));
                    }
                }
                else if (result.Status == HostConnectionTestService.StatusUnsupported
                    || result.Status == HostConnectionTestService.StatusIncomplete)
                {
                    checks.Add(new(TaskPreflightStatuses.Unknown, $"{reference.Role} Path", result.Message));
                }
                else if (reference.Role.Equals("Destination", StringComparison.OrdinalIgnoreCase) && reference.ForceDirectory)
                {
                    checks.Add(new(TaskPreflightStatuses.Warning, "Destination Path", $"The configured destination directory '{reference.Directory}' could not be browsed now, but the Task is configured to create the directory if necessary. MOVEit reported: {result.Message}"));
                }
                else
                {
                    checks.Add(new(TaskPreflightStatuses.Fail, $"{reference.Role} Path", $"The configured directory '{reference.Directory}' could not be browsed. MOVEit reported: {result.Message}"));
                }

                if (reference.Role.Equals("Source", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(reference.FileMask)
                    && !IsSafeBrowseMask(reference.FileMask))
                {
                    checks.Add(new(TaskPreflightStatuses.Info, "Source File Mask", $"The configured file mask '{reference.FileMask}' contains runtime macros and was not applied during Preflight browsing."));
                }
            }

            foreach (var reference in group.Where(reference => !string.IsNullOrWhiteSpace(reference.Directory) && !IsConcreteBrowsePath(reference.Directory)))
            {
                checks.Add(new(TaskPreflightStatuses.Unknown, $"{reference.Role} Path", $"The configured path '{reference.Directory}' contains runtime macros, wildcards, or multiple folder expressions and cannot be safely reduced to one literal directory for Preflight browsing."));
            }
        }

        for (var index = 0; index < checks.Count; index++)
            checks[index] = NormalizeHostCheckSeverity(checks[index]);

        return checks;
    }

    private static TaskPreflightCheck NormalizeHostCheckSeverity(TaskPreflightCheck check)
    {
        if (check.Status == TaskPreflightStatuses.Fail || !LooksLikeHostErrorDetail(check.Message)) return check;
        return check with { Status = TaskPreflightStatuses.Fail };
    }

    private static bool LooksLikeHostErrorDetail(string message)
    {
        var text = (message ?? string.Empty).Trim();
        if (text.Length == 0) return false;

        // Ignore quoted configured values before interpreting diagnostic keywords/codes so a legitimate
        // path or file mask containing text such as "error" or "(2026)" is not promoted to Error.
        var unquoted = RemoveQuotedSegments(text);
        if (Regex.IsMatch(unquoted, @"\b(failed|failure|error)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return true;

        // MOVEit/Windows diagnostics sometimes append only a numeric error code, e.g. "(53)".
        return Regex.IsMatch(unquoted, @"\(\s*-?\d{1,10}\s*\)", RegexOptions.CultureInvariant);
    }

    private static string RemoveQuotedSegments(string value)
    {
        var buffer = new System.Text.StringBuilder(value.Length);
        char? quote = null;
        foreach (var ch in value)
        {
            if (quote.HasValue)
            {
                if (ch == quote.Value) quote = null;
                continue;
            }
            if (ch is '\'' or '"')
            {
                quote = ch;
                continue;
            }
            buffer.Append(ch);
        }
        return buffer.ToString();
    }

    private async Task<IReadOnlyList<TaskPreflightCheck>> CheckResourcesAsync(JsonObject task, CancellationToken cancellationToken)
    {
        var checks = new List<TaskPreflightCheck>();
        var scriptIds = CollectReferenceValues(task, key =>
            key.Equals("ScriptID", StringComparison.OrdinalIgnoreCase)
            || key.Equals("CustomScriptID", StringComparison.OrdinalIgnoreCase)
            || (key.EndsWith("ScriptID", StringComparison.OrdinalIgnoreCase) && key.Length <= 32));
        var dateListIds = CollectReferenceValues(task, key =>
            key.Equals("DateListID", StringComparison.OrdinalIgnoreCase)
            || key.Equals("SchedDateListID", StringComparison.OrdinalIgnoreCase));

        if (scriptIds.Count == 0 && dateListIds.Count == 0)
        {
            checks.Add(new(TaskPreflightStatuses.Info, "Resources", "No Script or Date List references were found in the Task definition."));
            return checks;
        }

        var scriptsTask = _scripts.GetScriptsAsync(cancellationToken);
        var dateListsTask = _dateLists.GetSnapshotAsync(false, cancellationToken);
        await Task.WhenAll(scriptsTask, dateListsTask);
        var scripts = (await scriptsTask).ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var dateLists = (await dateListsTask).DateLists.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var id in scriptIds)
        {
            if (scripts.TryGetValue(id, out var script)) checks.Add(new(TaskPreflightStatuses.Pass, "Script", $"Script '{script.Name}' exists."));
            else checks.Add(new(TaskPreflightStatuses.Fail, "Script", $"Referenced Script ID '{id}' was not found in the current MOVEit inventory."));
        }
        foreach (var id in dateListIds)
        {
            if (dateLists.TryGetValue(id, out var list)) checks.Add(new(TaskPreflightStatuses.Pass, "Date List", $"Date List '{list.Name}' exists."));
            else checks.Add(new(TaskPreflightStatuses.Fail, "Date List", $"Referenced Date List ID '{id}' was not found in the current MOVEit inventory."));
        }

        return checks;
    }

    private async Task<IReadOnlyList<TaskPreflightCheck>> CheckCertificatesAsync(JsonObject task, CancellationToken cancellationToken)
    {
        var checks = new List<TaskPreflightCheck>();
        var inventory = (await _keys.GetSnapshotAsync(false, cancellationToken)).Items;
        var referenced = inventory.Where(item => ContainsKeyCertificateReference(task, item)).ToList();
        var explicitReferences = CollectExplicitKeyCertificateReferences(task);
        var knownValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in referenced)
            foreach (var candidate in KeyCertificateCandidates(item))
                if (!string.IsNullOrWhiteSpace(candidate)) knownValues.Add(candidate);

        foreach (var reference in explicitReferences.Where(reference => !knownValues.Contains(reference.Value)))
            checks.Add(new(TaskPreflightStatuses.Fail, "Key / Certificate Reference", $"Task field '{reference.Property}' references '{reference.Value}', but no matching key/certificate is visible in the current MOVEit inventory."));

        if (referenced.Count == 0 && explicitReferences.Count == 0)
        {
            checks.Add(new(TaskPreflightStatuses.Info, "Keys / Certificates", "No direct Task key or certificate references were identified."));
            return checks;
        }

        foreach (var item in referenced)
        {
            var statusText = (item.Status ?? string.Empty).Trim();
            if (statusText.Contains("expired", StringComparison.OrdinalIgnoreCase))
            {
                checks.Add(new(TaskPreflightStatuses.Fail, item.Type, $"'{item.Name}' is reported as expired."));
                continue;
            }

            if (TryParseExpiration(item.ExpirationDate, out var expiration))
            {
                var remaining = expiration - DateTimeOffset.Now;
                if (remaining <= TimeSpan.Zero)
                    checks.Add(new(TaskPreflightStatuses.Fail, item.Type, $"'{item.Name}' expired on {expiration:d}."));
                else if (remaining <= TimeSpan.FromDays(30))
                    checks.Add(new(TaskPreflightStatuses.Warning, item.Type, $"'{item.Name}' expires on {expiration:d} ({Math.Max(0, (int)Math.Ceiling(remaining.TotalDays))} day(s))."));
                else
                    checks.Add(new(TaskPreflightStatuses.Pass, item.Type, $"'{item.Name}' is valid."));
            }
            else
            {
                checks.Add(new(TaskPreflightStatuses.Pass, item.Type, $"'{item.Name}' exists."));
            }
        }
        return checks;
    }

    private async Task<IReadOnlyList<TaskPreflightCheck>> CheckNotificationsAsync(JsonObject task, CancellationToken cancellationToken)
    {
        var checks = new List<TaskPreflightCheck>();
        var isAdvanced = Read(task, "TT").Equals("COS", StringComparison.OrdinalIgnoreCase);

        // Advanced Tasks do not use task-level Email NextActions. MOVEit serializes Send Email
        // steps inside the Advanced Task steps tree, and they may appear anywhere in the workflow,
        // including inside File Loops and conditional branches. Walk the complete tree so nested
        // Email steps are not missed. Traditional/Sync Tasks retain the existing NextAction behavior.
        if (isAdvanced)
        {
            // Prefer the documented REST steps collection, but fall back to the full Task payload so
            // Preflight remains read-compatible if MOVEit returns an Advanced Task shape with steps at the root.
            var emailSteps = CollectAdvancedEmailSteps(GetNode(task, "steps") ?? task);
            if (emailSteps.Count == 0)
            {
                checks.Add(new(TaskPreflightStatuses.Info, "Notifications", "No Send Email steps are configured in this Advanced Task."));
                return checks;
            }

            var hosts = await _hosts.GetHostsAsync(cancellationToken);
            var smtp = hosts.Where(host => host.Type.StartsWith("SMTP", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(host => host.Id, StringComparer.OrdinalIgnoreCase);

            for (var index = 0; index < emailSteps.Count; index++)
            {
                var email = emailSteps[index];
                var label = emailSteps.Count == 1 ? "Send Email Step" : $"Send Email Step {index + 1}";
                checks.Add(new(TaskPreflightStatuses.Pass, label, "Send Email step is configured."));
                AddEmailDeliveryChecks(checks, label, email, smtp);
            }

            return checks;
        }

        var nextActions = GetNode(task, "NextActions");
        var nextActionItems = nextActions is JsonObject nextActionObject
            ? GetNode(nextActionObject, "NextAction") ?? nextActions
            : nextActions;
        var actions = GetObjectList(nextActionItems)
            .Where(action => Read(action, "Type").Equals("Email", StringComparison.OrdinalIgnoreCase)).ToList();
        if (actions.Count == 0)
        {
            checks.Add(new(TaskPreflightStatuses.Info, "Notifications", "No task-level Email notifications are configured."));
            return checks;
        }

        var traditionalHosts = await _hosts.GetHostsAsync(cancellationToken);
        var traditionalSmtp = traditionalHosts.Where(host => host.Type.StartsWith("SMTP", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(host => host.Id, StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < actions.Count; index++)
        {
            var action = actions[index];
            var failure = ReadBool(GetNode(action, "DoIfFailure"));
            var noAction = ReadBool(GetNode(action, "DoIfNoAction"));
            var success = ReadBool(GetNode(action, "DoIfSuccess"));
            var resultName = FormatResults(failure, noAction, success);
            var label = resultName.Length > 0 ? $"Task {resultName} Notification" : $"Email Notification {index + 1}";

            if (!failure && !noAction && !success)
                checks.Add(new(TaskPreflightStatuses.Fail, label, "No Task Result condition is selected."));
            else
                checks.Add(new(TaskPreflightStatuses.Pass, label, "Notification is configured."));

            AddEmailDeliveryChecks(checks, label, action, traditionalSmtp);
        }
        return checks;
    }

    private static void AddEmailDeliveryChecks(
        List<TaskPreflightCheck> checks,
        string label,
        JsonObject email,
        IReadOnlyDictionary<string, AutomationHost> smtpHosts)
    {
        var hostId = Read(email, "HostID");
        if (hostId.Length == 0) checks.Add(new(TaskPreflightStatuses.Fail, label, "SMTP Host is not configured."));
        else if (!smtpHosts.TryGetValue(hostId, out var smtpHost)) checks.Add(new(TaskPreflightStatuses.Fail, label, $"SMTP Host ID '{hostId}' is not available in the current SMTP Host inventory."));
        else checks.Add(new(TaskPreflightStatuses.Pass, label, $"SMTP Host '{smtpHost.Name}' is available."));

        if (Read(email, "AddressTo").Length == 0) checks.Add(new(TaskPreflightStatuses.Fail, label, "Recipients are empty."));
        if (Read(email, "Subject").Length == 0) checks.Add(new(TaskPreflightStatuses.Warning, label, "Subject is empty."));
        if (Read(email, "Message").Length == 0) checks.Add(new(TaskPreflightStatuses.Warning, label, "Message body is empty."));
    }

    private async Task<IReadOnlyList<TaskPreflightCheck>> CheckSchedulingAsync(JsonObject task, CancellationToken cancellationToken)
    {
        var checks = new List<TaskPreflightCheck>();
        var scheduled = Read(task, "Scheduled");
        var scheduleRoot = GetNode(task, "Schedules");
        var scheduleItems = scheduleRoot is JsonObject scheduleObject
            ? GetNode(scheduleObject, "Schedule") ?? scheduleRoot
            : scheduleRoot;
        var schedules = GetObjectList(scheduleItems);

        if (scheduled.Equals("INCOMPLETE", StringComparison.OrdinalIgnoreCase))
            checks.Add(new(TaskPreflightStatuses.Fail, "Schedule", "MOVEit reports the Task as incomplete."));
        else if (scheduled.Equals("DISABLED", StringComparison.OrdinalIgnoreCase))
            checks.Add(new(TaskPreflightStatuses.Warning, "Schedule", "Task schedule is disabled."));
        else if (scheduled.Equals("UNSCHEDULED", StringComparison.OrdinalIgnoreCase))
            checks.Add(new(TaskPreflightStatuses.Warning, "Schedule", "Task has no schedule."));
        else if (schedules.Count == 0)
            checks.Add(new(TaskPreflightStatuses.Warning, "Schedule", "No schedule definitions were returned for this Task."));
        else
            checks.Add(new(TaskPreflightStatuses.Pass, "Schedule", schedules.Count == 1 ? "Schedule is configured." : $"{schedules.Count} schedules are configured."));

        var dateListIds = CollectReferenceValues(task, key => key.Equals("DateListID", StringComparison.OrdinalIgnoreCase) || key.Equals("SchedDateListID", StringComparison.OrdinalIgnoreCase));
        if (dateListIds.Count > 0)
        {
            var lists = (await _dateLists.GetSnapshotAsync(false, cancellationToken)).DateLists.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var id in dateListIds)
                checks.Add(lists.ContainsKey(id)
                    ? new(TaskPreflightStatuses.Pass, "Schedule Date List", $"Date List ID '{id}' exists.")
                    : new(TaskPreflightStatuses.Fail, "Schedule Date List", $"Referenced Date List ID '{id}' was not found."));
        }

        try
        {
            var scheduler = await _rest.GetSchedulerStatusAsync(cancellationToken);
            if (scheduler.Equals("SCHEDULER_ENABLED", StringComparison.OrdinalIgnoreCase))
                checks.Add(new(TaskPreflightStatuses.Pass, "Automation Scheduler", "The MOVEit Task Scheduler is enabled."));
            else if (scheduler.StartsWith("SCHEDULER_DISABLED_", StringComparison.OrdinalIgnoreCase))
                checks.Add(new(TaskPreflightStatuses.Warning, "Automation Scheduler", "The MOVEit Task Scheduler is disabled."));
            else
                checks.Add(new(TaskPreflightStatuses.Unknown, "Automation Scheduler", "The MOVEit Task Scheduler status could not be determined."));
        }
        catch (MoveItApiException ex)
        {
            checks.Add(new(TaskPreflightStatuses.Unknown, "Automation Scheduler", $"Scheduler status could not be verified: {ex.Message}"));
        }

        return checks;
    }

    private async Task<IReadOnlyList<TaskPreflightCheck>> CheckConfigurationAsync(JsonObject task, CancellationToken cancellationToken)
    {
        var checks = new List<TaskPreflightCheck>();
        var taskIds = CollectRunTaskIds(task);
        if (taskIds.Count > 0)
        {
            var tasks = (await _tasks.GetTasksAsync(cancellationToken)).ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var id in taskIds)
                checks.Add(tasks.TryGetValue(id, out var called)
                    ? new(TaskPreflightStatuses.Pass, "Run Task Reference", $"Task '{called.Name}' exists.")
                    : new(TaskPreflightStatuses.Fail, "Run Task Reference", $"Referenced Task ID '{id}' was not found in the current Task inventory."));
        }

        var parameterNames = CollectGlobalParameterNames(task);
        if (parameterNames.Count > 0)
        {
            var parameters = (await _globalParameters.GetSnapshotAsync(false, cancellationToken)).Parameters
                .ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var name in parameterNames)
                checks.Add(parameters.ContainsKey(name)
                    ? new(TaskPreflightStatuses.Pass, "Global Parameter", $"Global Parameter '{name}' exists.")
                    : new(TaskPreflightStatuses.Info, "Parameter Reference", $"Parameter '{name}' was not found in the Global Parameter inventory. It may be task/process scoped, so Preflight cannot safely classify it as missing."));
        }

        var runtimeMacroCount = CountRuntimeMacroStrings(task);
        if (runtimeMacroCount > 0)
            checks.Add(new(TaskPreflightStatuses.Info, "Runtime Macros", $"{runtimeMacroCount} configuration value{(runtimeMacroCount == 1 ? string.Empty : "s")} contain MOVEit runtime macros. Preflight does not invent values for runtime-dependent macros."));

        if (taskIds.Count == 0 && parameterNames.Count == 0 && runtimeMacroCount == 0)
            checks.Add(new(TaskPreflightStatuses.Pass, "Configuration", "Configuration checks passed."));
        return checks;
    }

    private async Task<IReadOnlyList<TaskPreflightCheck>> CheckRuntimeAsync(JsonObject task, CancellationToken cancellationToken)
    {
        var checks = new List<TaskPreflightCheck>();
        var id = Read(task, "ID");
        var inventory = await _tasks.GetTasksAsync(cancellationToken);
        var current = inventory.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (current is null)
        {
            checks.Add(new(TaskPreflightStatuses.Unknown, "Runtime State", "The Task was not present in the current MAUI runtime inventory."));
            return checks;
        }

        if (current.Status.Equals("Running", StringComparison.OrdinalIgnoreCase))
            checks.Add(new(TaskPreflightStatuses.Warning, "Runtime State", "Task is currently running."));
        else
            checks.Add(new(TaskPreflightStatuses.Pass, "Runtime State", "Task is not currently running."));

        var scheduled = Read(task, "Scheduled");
        if (scheduled.Equals("INCOMPLETE", StringComparison.OrdinalIgnoreCase))
            checks.Add(new(TaskPreflightStatuses.Fail, "Run Eligibility", "MOVEit currently reports this Task as incomplete."));
        else
            checks.Add(new(TaskPreflightStatuses.Pass, "Run Eligibility", "Task can be started."));

        if (!string.IsNullOrWhiteSpace(current.LastResult))
            checks.Add(new(TaskPreflightStatuses.Info, "Last Result", $"Most recent known result: {current.LastResult}."));
        return checks;
    }

    private static TaskPreflightCheck MapHostTest(HostConnectionTestResult result, string title)
    {
        var status = result.Status switch
        {
            HostConnectionTestService.StatusPassed => TaskPreflightStatuses.Pass,
            HostConnectionTestService.StatusUnsupported or HostConnectionTestService.StatusIncomplete => TaskPreflightStatuses.Unknown,
            _ => TaskPreflightStatuses.Fail
        };
        return new(status, title, result.Message);
    }

    private static string StageStatus(IReadOnlyList<TaskPreflightCheck> checks)
    {
        if (checks.Any(check => check.Status == TaskPreflightStatuses.Fail)) return TaskPreflightStatuses.Fail;
        if (checks.Any(check => check.Status == TaskPreflightStatuses.Warning)) return TaskPreflightStatuses.Warning;
        if (checks.Any(check => check.Status == TaskPreflightStatuses.Unknown)) return TaskPreflightStatuses.Warning;
        return TaskPreflightStatuses.Pass;
    }

    private static string NormalizeStage(string stage)
    {
        var value = (stage ?? string.Empty).Trim().ToLowerInvariant();
        if (!TaskPreflightStages.Ordered.Contains(value, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Preflight stage is not supported.", nameof(stage));
        return value;
    }

    private sealed record HostLocation(string Role, string HostId, string Directory, string FileMask, bool ForceDirectory, bool BlindDownload);

    private static List<HostLocation> CollectHostLocations(JsonNode? node)
    {
        var result = new List<HostLocation>();
        Walk(node);
        return result
            .Where(item => item.HostId.Length > 0)
            .DistinctBy(item => $"{item.Role}|{item.HostId}|{item.Directory}|{item.FileMask}|{item.ForceDirectory}|{item.BlindDownload}", StringComparer.OrdinalIgnoreCase)
            .ToList();

        void Walk(JsonNode? current)
        {
            if (current is JsonObject obj)
            {
                foreach (var pair in obj)
                {
                    if ((pair.Key.Equals("Source", StringComparison.OrdinalIgnoreCase) || pair.Key.Equals("Destination", StringComparison.OrdinalIgnoreCase))
                        && pair.Value is JsonObject endpoint)
                    {
                        var hostId = Read(endpoint, "HostID");
                        if (hostId.Length > 0)
                        {
                            var role = pair.Key.Equals("Source", StringComparison.OrdinalIgnoreCase) ? "Source" : "Destination";
                            result.Add(new HostLocation(
                                role,
                                hostId,
                                FirstNonBlank(ReadAny(endpoint, "FolderName", "Path", "Folder", "DirName", "Directory")),
                                role.Equals("Source", StringComparison.OrdinalIgnoreCase)
                                    ? FirstNonBlank(ReadAny(endpoint, "FileMask", "Mask", "FileName", "Filename", "Files"))
                                    : string.Empty,
                                role.Equals("Destination", StringComparison.OrdinalIgnoreCase) && ReadBool(GetNode(endpoint, "ForceDir")),
                                role.Equals("Source", StringComparison.OrdinalIgnoreCase) && ReadBool(GetNode(endpoint, "BlindDL"))));
                        }
                    }
                    Walk(pair.Value);
                }
            }
            else if (current is JsonArray array)
            {
                foreach (var child in array) Walk(child);
            }
        }
    }

    private static HashSet<string> CollectReferenceValues(JsonNode? node, Func<string, bool> keyPredicate)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Walk(node);
        return result;
        void Walk(JsonNode? current)
        {
            if (current is JsonObject obj)
            {
                foreach (var pair in obj)
                {
                    var key = pair.Key.Replace("_", string.Empty, StringComparison.Ordinal).Trim();
                    if (keyPredicate(key))
                    {
                        var value = NodeString(pair.Value).Trim();
                        if (value.Length > 0 && value != "0") result.Add(value);
                    }
                    Walk(pair.Value);
                }
            }
            else if (current is JsonArray array) foreach (var child in array) Walk(child);
        }
    }

    private static HashSet<string> CollectRunTaskIds(JsonNode? node)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Walk(node);
        return result;
        void Walk(JsonNode? current)
        {
            if (current is JsonObject obj)
            {
                foreach (var pair in obj)
                {
                    if ((pair.Key.Equals("RunTask", StringComparison.OrdinalIgnoreCase) || pair.Key.Equals("MicRunTaskType", StringComparison.OrdinalIgnoreCase))
                        && pair.Value is JsonObject runTask)
                    {
                        var id = Read(runTask, "TaskID");
                        if (id.Length > 0) result.Add(id);
                    }
                    Walk(pair.Value);
                }
            }
            else if (current is JsonArray array) foreach (var child in array) Walk(child);
        }
    }

    internal static HashSet<string> CollectGlobalParameterNames(JsonNode? node)
    {
        // MOVEit parameter macros can use [Parm:Name] or [ParmDisplay:Name]. A name found in the
        // Global Parameter inventory is positively validated; an absent global entry is informational
        // because the value may be task/process scoped.
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Walk(node);
        return result;

        void Walk(JsonNode? current)
        {
            if (current is JsonObject obj) foreach (var pair in obj) Walk(pair.Value);
            else if (current is JsonArray array) foreach (var child in array) Walk(child);
            else
            {
                var text = NodeString(current);
                Collect(text, "[Parm:", 6);
                Collect(text, "[ParmDisplay:", 13);
            }
        }

        void Collect(string text, string prefix, int valueOffset)
        {
            var offset = 0;
            while (offset < text.Length)
            {
                var start = text.IndexOf(prefix, offset, StringComparison.OrdinalIgnoreCase);
                if (start < 0) break;
                var end = text.IndexOf(']', start + valueOffset);
                if (end < 0) break;
                var name = text[(start + valueOffset)..end].Trim();
                if (name.Length > 0) result.Add(name);
                offset = end + 1;
            }
        }
    }

    private static int CountRuntimeMacroStrings(JsonNode? node)
    {
        var count = 0;
        Walk(node);
        return count;
        void Walk(JsonNode? current)
        {
            if (current is JsonObject obj) foreach (var pair in obj) Walk(pair.Value);
            else if (current is JsonArray array) foreach (var child in array) Walk(child);
            else
            {
                var text = NodeString(current);
                if (text.Contains('[') && text.Contains(']')) count++;
            }
        }
    }

    private static bool ContainsKeyCertificateReference(JsonNode? node, AutomationKeyCertificate item, bool referenceContext = false)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj)
            {
                var key = pair.Key.Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).Trim();
                var nextContext = referenceContext
                    || key.Contains("key", StringComparison.OrdinalIgnoreCase)
                    || key.Contains("cert", StringComparison.OrdinalIgnoreCase)
                    || key.Contains("pgp", StringComparison.OrdinalIgnoreCase)
                    || key.Contains("sha1", StringComparison.OrdinalIgnoreCase)
                    || key.Contains("thumbprint", StringComparison.OrdinalIgnoreCase)
                    || key.Contains("fingerprint", StringComparison.OrdinalIgnoreCase);
                if (nextContext && ReferenceMatches(pair.Value, item)) return true;
                if (ContainsKeyCertificateReference(pair.Value, item, nextContext)) return true;
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array) if (ContainsKeyCertificateReference(child, item, referenceContext)) return true;
        }
        else if (referenceContext && ReferenceMatches(node, item)) return true;
        return false;
    }

    private static bool ReferenceMatches(JsonNode? node, AutomationKeyCertificate item)
    {
        var value = NodeString(node).Trim();
        if (value.Length == 0) return false;
        return KeyCertificateCandidates(item)
            .Any(candidate => !string.IsNullOrWhiteSpace(candidate) && value.Equals(candidate, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record ExplicitKeyCertificateReference(string Property, string Value);

    private static List<ExplicitKeyCertificateReference> CollectExplicitKeyCertificateReferences(JsonNode? node)
    {
        var result = new List<ExplicitKeyCertificateReference>();
        Walk(node);
        return result
            .Where(item => item.Value.Length > 0)
            .DistinctBy(item => $"{item.Property}|{item.Value}", StringComparer.OrdinalIgnoreCase)
            .ToList();

        void Walk(JsonNode? current)
        {
            if (current is JsonObject obj)
            {
                foreach (var pair in obj)
                {
                    var key = pair.Key.Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
                    if (key.Equals("ClientKeyID", StringComparison.OrdinalIgnoreCase)
                        || key.Equals("DefClientKeyID", StringComparison.OrdinalIgnoreCase)
                        || key.Equals("ClientCertSHA1", StringComparison.OrdinalIgnoreCase)
                        || key.Equals("DefClientCertSHA1", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = NodeString(pair.Value).Trim();
                        if (value.Length > 0 && value != "0") result.Add(new(pair.Key, value));
                    }
                    Walk(pair.Value);
                }
            }
            else if (current is JsonArray array) foreach (var child in array) Walk(child);
        }
    }

    private static IEnumerable<string> KeyCertificateCandidates(AutomationKeyCertificate item)
    {
        yield return item.Id;
        yield return item.Sha1Thumbprint;
        yield return item.Fingerprint;
        yield return item.Name;
        yield return item.Uid;
        yield return item.IssuedTo;
    }

    private static IReadOnlyList<JsonObject> CollectAdvancedEmailSteps(JsonNode? node)
    {
        var result = new List<JsonObject>();
        Walk(node);
        return result;

        void Walk(JsonNode? current)
        {
            if (current is JsonObject obj)
            {
                foreach (var pair in obj)
                {
                    // MOVEit REST serializes Advanced Task step wrappers using friendly names
                    // such as "Email", "Process", and "Source". The Swagger model names the
                    // same email member MicEmailType, so accept both shapes while recursively
                    // walking nested For/If/When/Otherwise step trees.
                    if (pair.Key.Equals("Email", StringComparison.OrdinalIgnoreCase)
                        || pair.Key.Equals("MicEmailType", StringComparison.OrdinalIgnoreCase))
                    {
                        if (pair.Value is JsonObject email) result.Add(email);
                        else if (pair.Value is JsonArray emailArray) result.AddRange(emailArray.OfType<JsonObject>());
                    }

                    Walk(pair.Value);
                }
            }
            else if (current is JsonArray array)
            {
                foreach (var child in array) Walk(child);
            }
        }
    }

    private static IReadOnlyList<JsonObject> GetObjectList(JsonNode? node)
    {
        if (node is JsonArray array) return array.OfType<JsonObject>().ToList();
        if (node is JsonObject obj) return new[] { obj };
        return Array.Empty<JsonObject>();
    }

    private static bool IsConcreteBrowsePath(string value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0 || (text.Contains('[') && text.Contains(']'))) return false;
        // MOVEit Source folders may contain wildcard or semicolon-delimited folder expressions. Those are
        // valid Task configuration but are not one literal directory that the native Browse endpoint can test.
        return text.IndexOfAny(new[] { '*', '?', ';' }) < 0;
    }

    private static bool IsSafeBrowseMask(string value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length == 0 || !(text.Contains('[') && text.Contains(']'));
    }

    private static string FormatResults(bool failure, bool noAction, bool success)
    {
        var values = new List<string>();
        if (failure) values.Add("Failure");
        if (noAction) values.Add("No Transfer");
        if (success) values.Add("Success");
        return string.Join(" + ", values);
    }

    private static bool TryParseExpiration(string value, out DateTimeOffset date)
    {
        var text = (value ?? string.Empty).Trim();
        return DateTimeOffset.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out date)
            || DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out date);
    }

    private static string Read(JsonObject? obj, string name) => NodeString(GetNode(obj, name)).Trim();
    private static string ReadAny(JsonObject? obj, params string[] names) => names.Select(name => Read(obj, name)).FirstOrDefault(value => value.Length > 0) ?? string.Empty;
    private static JsonNode? GetNode(JsonObject? obj, string name)
    {
        if (obj is null) return null;
        if (obj.TryGetPropertyValue(name, out var exact)) return exact;
        foreach (var pair in obj) if (pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)) return pair.Value;
        return null;
    }
    private static bool ReadBool(JsonNode? node)
    {
        var value = NodeString(node).Trim();
        return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
    private static string NodeString(JsonNode? node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text)) return text ?? string.Empty;
            if (value.TryGetValue<int>(out var intValue)) return intValue.ToString(CultureInfo.InvariantCulture);
            if (value.TryGetValue<long>(out var longValue)) return longValue.ToString(CultureInfo.InvariantCulture);
            if (value.TryGetValue<bool>(out var boolValue)) return boolValue ? "true" : "false";
        }
        return string.Empty;
    }
    private static string FirstNonBlank(params string[] values) => values.Select(value => (value ?? string.Empty).Trim()).FirstOrDefault(value => value.Length > 0) ?? string.Empty;
}

