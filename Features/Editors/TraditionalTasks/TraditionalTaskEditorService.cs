using MoveItAutomationUI.Services.MoveIt.Tasks.AdvancedTasks;
// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MoveItAutomationUI.Models;

using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;
using MoveItAutomationUI.Services.MoveIt.Tasks;

namespace MoveItAutomationUI.Services.MoveIt;

public interface ITraditionalTaskEditorService
{
    Task<TraditionalTaskEditorMoveItDefinition> GetDefinitionAsync(string taskId, CancellationToken cancellationToken = default);
}

// Provides the MOVEit-backed read contract for the RC2 Traditional Task editor.
// Persistence is handled separately by the fresh-definition merge Save service.
public sealed class TraditionalTaskEditorService : ITraditionalTaskEditorService
{
    private readonly IMoveItRestClient _restClient;
    private readonly ITaskCrudCoordinator _crud;
    private readonly IMoveItObjectAccessService? _access;

    public TraditionalTaskEditorService(IMoveItRestClient restClient, ITaskCrudCoordinator crud, IMoveItObjectAccessService? access = null)
    {
        _restClient = restClient;
        _crud = crud;
        _access = access;
    }

    public async Task<TraditionalTaskEditorMoveItDefinition> GetDefinitionAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var normalizedTaskId = (taskId ?? string.Empty).Trim();
        if (normalizedTaskId.Length == 0) throw new ArgumentException("Task ID is required.", nameof(taskId));

        // Native configuration reads use MOVEit's edit/snapshot API. Resolve access first:
        // visible read-only Tasks must load without snapshots or edit checkouts.
        var access = _access is null ? null : await _access.GetObjectAsync("Task", normalizedTaskId, cancellationToken);
        var taskRead = _crud.GetDefinitionAsync(normalizedTaskId, cancellationToken);
        var hostsRead = _restClient.GetHostsAsync(cancellationToken);
        var tasksRead = _restClient.GetTasksAsync(cancellationToken);
        var dateListsRead = _restClient.GetDateListsAsync(cancellationToken);
        var customScriptsRead = _restClient.GetCustomScriptsAsync(cancellationToken);
        var standardScriptsRead = _restClient.GetStandardScriptsAsync(cancellationToken);
        var tlsCertificatesRead = _restClient.GetTlsCertificatesAsync(cancellationToken);
        var sshClientKeysRead = _restClient.GetSshKeysAsync(cancellationToken);
        var nativeTaskRead = access?.CanEdit == false ? Task.FromResult<MoveItTaskConfiguration?>(null)
            : ReadNativeAsync();
        async Task<MoveItTaskConfiguration?> ReadNativeAsync() => await _crud.GetNativeConfigurationAsync(normalizedTaskId, cancellationToken);
        await Task.WhenAll(taskRead, hostsRead, tasksRead, dateListsRead, customScriptsRead, standardScriptsRead, tlsCertificatesRead, sshClientKeysRead, nativeTaskRead);

        var task = await taskRead;
        var type = GetTaskType(Read(task, "TT"));
        var isAdvanced = type.Equals("Advanced", StringComparison.OrdinalIgnoreCase);
        var synchronization = type.Equals("Synchronization", StringComparison.OrdinalIgnoreCase);

        var rawHosts = await hostsRead;
        var hostDefinitions = rawHosts
            .Select(UnwrapHost)
            .Where(item => item.Host is not null)
            .Select(item => new RawHost(item.TypeCode, item.Host!, Read(item.Host, "ID")))
            .Where(item => item.Id.Length > 0)
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var sourceHostOptions = BuildHostOptions(rawHosts, synchronization ? SynchronizationTaskDefinitionComposer.HostTypes : TraditionalTaskEditorHostCapabilities.SourceTypeCodes);
        var destinationHostOptions = BuildHostOptions(rawHosts, TraditionalTaskEditorHostCapabilities.DestinationTypeCodes);
        var smtpHosts = BuildSmtpHostOptions(rawHosts);
        var taskOptions = BuildTaskOptions(await tasksRead);
        var dateListOptions = BuildDateListOptions(await dateListsRead);
        var scriptOptions = BuildScriptOptions(await customScriptsRead, await standardScriptsRead);
        var tlsCertificates = BuildTlsCertificateOptions(await tlsCertificatesRead);
        var sshClientKeys = BuildSshClientKeyOptions(await sshClientKeysRead);

        var allHostOptions = sourceHostOptions
            .Concat(destinationHostOptions)
            .GroupBy(host => host.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var scriptOptionMap = scriptOptions
            .GroupBy(script => script.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var allSteps = isAdvanced ? AdvancedTaskDefinition.Flatten(task["steps"]).ToList() : GetStepObjects(task["steps"]).ToList();
        var steps = allSteps.Where(step => !isAdvanced || AdvancedTaskDefinition.Kind(step) is "Source" or "Process" or "Destination")
            .Select((step, index) => ParseStep(step, index, allHostOptions, hostDefinitions, scriptOptionMap))
            .ToList();

        // Schedule editing uses the native Task XML representation deliberately. MOVEit Automation
        // 2025.1 can normalize/collapse the internal Multi=1/Multi=2 overnight pair through the v1
        // Task REST model, while Web Admin and the Windows API store the authoritative pair in Task
        // XML. Reading Schedules from the same native representation used for overnight persistence
        // keeps load/edit/save semantics consistent with MOVEit's native editor.
        var nativeTask = await nativeTaskRead;
        var scheduleDefinitions = nativeTask is null
            ? GetCollectionObjects(GetNode(task, "Schedules"), "Schedule").ToList()
            : TraditionalTaskScheduleXml.ReadSchedules(nativeTask.Xml).ToList();

        var schedules = scheduleDefinitions
            .Select((schedule, index) => ParseSchedule(schedule, index, dateListOptions))
            .ToList();

        var actionDefinitions = isAdvanced ? allSteps.Where(wrapper => AdvancedTaskDefinition.Kind(wrapper) is "Email" or "RunTask").Select(wrapper => {
            var kind = AdvancedTaskDefinition.Kind(wrapper); var action = wrapper[kind]!.DeepClone().AsObject(); action["Type"] = kind == "Email" ? "Email" : "Task"; return action; }).ToList() : GetCollectionObjects(GetNode(task, "NextActions"), "NextAction").ToList();
        var nextActions = actionDefinitions
            .Select((action, index) => ParseNextAction(action, index, smtpHosts, taskOptions))
            .ToList();

        var info = task["Info"] as JsonObject;
        var editorTask = new TraditionalTaskEditorTask(
            Read(task, "ID", normalizedTaskId),
            Read(task, "Name"),
            Read(info, "Description").Trim(),
            Read(info, "Notes").Trim(),
            type,
            ReadFlag(task, "Active"),
            // Match the existing manual Run action. xRun is optional in REST detail
            // responses; the native start endpoint remains the permission authority.
            !Read(task, "Scheduled").Equals("INCOMPLETE", StringComparison.OrdinalIgnoreCase),
            ObjectCheckout.CanEditCheckedOut("Task", normalizedTaskId, ReadNullableFlag(task, "xAlter") ?? true),
            Read(task, "Scheduled"),
            isAdvanced ? allSteps.Count : steps.Count,
            schedules.Count,
            nextActions.Count,
            steps,
            schedules,
            nextActions,
            ParseParameters(task["Parameters"]),
            new TraditionalTaskEditorAdvancedSettings(
                TraditionalTaskDefinitionEditorOperations.ResolveCacheNames(Read(task, "CacheNames")),
                ReadFlag(task, "UseDefStateCaching"),
                ReadNullableInt(task, "StateCacheTime"),
                Read(task, "StateCacheUnit"),
                ReadFlag(task, "AR"),
                ReadNullableInt(task, "NumTaskLogsToKeep")),
            synchronization ? new SynchronizationTaskEditorSettings(
                GetStepObjects(task["steps"]).Select(wrapper => wrapper["Source"] as JsonObject)
                    .Where(source => source is not null).Select(source => new SynchronizationTaskEditorFolderSettings(
                        Read(source, "ID"), ReadNullableInt(source, "SyF"), ReadNullableInt(source, "SyT"),
                        ReadNullableInt(source, "SyDel"), ReadNullableInt(source, "UseLCaseComp"))).ToArray(),
                ReadNullableInt(task, "SameSecs")) : null, isAdvanced ? AdvancedTaskDefinition.Project(task["steps"]) : null);

        editorTask = editorTask with { ResourceGroups = ResourceGroupLookupService.FormatAssignments(task["Group"]) };
        if (_access is not null && access is not null)
        {
            var creation = await _access.GetCreationAsync(ct: cancellationToken);
            editorTask = editorTask with { CanAlter = access.CanEdit, CanRun = editorTask.CanRun && access.CanRun, CanDelete = access.CanDelete, CanCreateTask = creation.CanCreate("Task") };
        }
        return new TraditionalTaskEditorMoveItDefinition(
            editorTask,
            synchronization ? TraditionalTaskEditorHostCapabilities.ClientCapabilities
                .Where(capability => SynchronizationTaskDefinitionComposer.HostTypes.Contains(capability.TypeCode))
                .Select(capability => capability.TypeCode == "FileSystem" ? capability with
                    { SourceWritable = true, SourceWriteProfile = "sync-local", SourceBrowsable = true, SourceBrowseProfile = TraditionalTaskEditorHostCapabilities.ShareBrowseProfile } : capability).ToArray()
                : TraditionalTaskEditorHostCapabilities.ClientCapabilities,
            sourceHostOptions,
            destinationHostOptions,
            scriptOptions,
            smtpHosts,
            taskOptions,
            dateListOptions,
            tlsCertificates,
            sshClientKeys,
            nativeTask is null ? string.Empty : ObjectEditVersion.Create("Task", normalizedTaskId, ObjectEditVersion.Definition(task, nativeTask.Xml)));
    }

    private static TraditionalTaskEditorSchedule ParseSchedule(
        JsonObject schedule,
        int index,
        IReadOnlyList<TraditionalTaskEditorNamedOption> dateListOptions)
    {
        var days = GetNode(schedule, "Days") as JsonObject;
        var dayOfWeek = ReadStringValues(GetNode(days, "DayOfWeek"));
        var dayOfMonth = ReadIntValues(GetNode(days, "DayOfMonth"));

        var dateListReferences = GetDirectObjectList(GetNode(schedule, "DateListRef"))
            .Select(reference =>
            {
                var id = Read(reference, "ID").Trim();
                return new TraditionalTaskEditorScheduleDateListReference(
                    id,
                    ResolveNamedOption(id, dateListOptions, "Date List"),
                    ReadFlag(reference, "Run"));
            })
            .Where(reference => reference.Id.Length > 0)
            .ToList();

        var frequencyNode = GetNode(schedule, "Frequency");
        var intervalNode = frequencyNode is JsonObject frequencyObject
            ? GetNode(frequencyObject, "Interval") ?? frequencyNode
            : frequencyNode;
        var intervals = GetDirectObjectList(intervalNode)
            .Select((interval, intervalIndex) => new TraditionalTaskEditorScheduleInterval(
                intervalIndex,
                Read(interval, "ID"),
                Read(interval, "StartTime"),
                Read(interval, "EndTime"),
                ReadNullableInt(interval, "EveryMinutes") ?? 0))
            .ToList();

        return new TraditionalTaskEditorSchedule(
            index,
            index,
            Read(schedule, "ID"),
            ReadNullableInt(schedule, "Multi") ?? 0,
            dayOfWeek,
            dayOfMonth,
            dateListReferences,
            intervals,
            ReadFlag(schedule, "OnlyUntilFirstSuccess"),
            ReadFlag(schedule, "FailIfNoSuccessInSched"),
            ReadFlag(schedule, "RunEvenIfNotif"));
    }

    private static TraditionalTaskEditorNextAction ParseNextAction(
        JsonObject action,
        int index,
        IReadOnlyList<TraditionalTaskEditorNamedOption> smtpHosts,
        IReadOnlyList<TraditionalTaskEditorNamedOption> taskOptions)
    {
        var type = Read(action, "Type").Trim();
        var hostId = Read(action, "HostID").Trim();
        var taskId = Read(action, "TaskID").Trim();
        return new TraditionalTaskEditorNextAction(
            index,
            index,
            Read(action, "ID"),
            type,
            FirstNonBlank(Read(action, "DoAfter"), "Task"),
            ReadFlag(action, "DoIfFailure"),
            ReadFlag(action, "DoIfNoAction"),
            ReadFlag(action, "DoIfSuccess"),
            ReadFlag(action, "IsHTML"),
            hostId,
            ResolveNamedOption(hostId, smtpHosts, "SMTP Host"),
            Read(action, "AddressTo"),
            Read(action, "Subject"),
            Read(action, "Message"),
            taskId,
            ResolveNamedOption(taskId, taskOptions, "Task"),
            ParseParameters(GetNode(action, "Parameters")), ReadFlag(action,"Wait"));
    }

    private static IReadOnlyList<TraditionalTaskEditorNamedOption> BuildSmtpHostOptions(IReadOnlyList<JsonObject> rawHosts)
        => rawHosts
            .Select(UnwrapHost)
            .Where(item => item.Host is not null && item.TypeCode.Equals("SMTP", StringComparison.OrdinalIgnoreCase))
            .Select(item => new TraditionalTaskEditorNamedOption(Read(item.Host, "ID").Trim(), Read(item.Host, "Name").Trim(), "SMTP"))
            .Where(item => item.Id.Length > 0 && item.Name.Length > 0)
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<TraditionalTaskEditorNamedOption> BuildDateListOptions(IReadOnlyList<JsonObject> dateLists)
        => dateLists
            .Select(item => new TraditionalTaskEditorNamedOption(
                Read(item, "ID").Trim(),
                Read(item, "Name").Trim(),
                "DateList"))
            .Where(item => item.Id.Length > 0 && item.Name.Length > 0)
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<TraditionalTaskEditorNamedOption> BuildTaskOptions(IReadOnlyList<JsonObject> tasks)
        => tasks
            .Select(task => new TraditionalTaskEditorNamedOption(
                Read(task, "ID").Trim(),
                Read(task, "Name").Trim(),
                GetTaskType(Read(task, "TT"))))
            .Where(item => item.Id.Length > 0 && item.Name.Length > 0)
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string ResolveNamedOption(string id, IReadOnlyList<TraditionalTaskEditorNamedOption> options, string fallbackType)
    {
        if (string.IsNullOrWhiteSpace(id)) return string.Empty;
        var match = options.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        return match?.Name ?? $"{fallbackType} (ID {id})";
    }

    private static TraditionalTaskEditorStep ParseStep(
        JsonObject wrapper,
        int index,
        IReadOnlyDictionary<string, TraditionalTaskEditorHostOption> hostOptions,
        IReadOnlyDictionary<string, RawHost> hostDefinitions,
        IReadOnlyDictionary<string, TraditionalTaskEditorNamedOption> scriptOptions)
    {
        if (GetNode(wrapper, "Source") is JsonObject source)
        {
            var parsed = ParseSource(source, hostOptions, hostDefinitions);
            var path = FirstNonBlank(parsed.Path, "/");
            var mask = parsed.FileMask.Length > 0 ? parsed.FileMask : "*.*";
            return new TraditionalTaskEditorStep(
                index,
                "Source",
                parsed.Id,
                "Source",
                $"{parsed.HostName} · {path} · {mask}",
                parsed,
                null,
                null);
        }

        if (GetNode(wrapper, "Process") is JsonObject process)
        {
            var parsed = ParseProcess(process, scriptOptions);
            var subtitle = parsed.ScriptName.Length > 0
                ? $"{parsed.ScriptName} · {(parsed.Run.Equals("Once", StringComparison.OrdinalIgnoreCase) ? "Once after all downloads" : "Per file")}"
                : "Select a Script";
            return new TraditionalTaskEditorStep(
                index,
                "Process",
                parsed.Id,
                "Process",
                subtitle,
                null,
                parsed,
                null);
        }

        if (GetNode(wrapper, "Destination") is JsonObject destination)
        {
            var parsed = ParseDestination(destination, hostOptions, hostDefinitions);
            var path = FirstNonBlank(parsed.Path, "/");
            return new TraditionalTaskEditorStep(
                index,
                "Destination",
                parsed.Id,
                "Destination",
                $"{parsed.HostName} · {path}",
                null,
                null,
                parsed);
        }

        var kind = wrapper.FirstOrDefault(pair => pair.Value is JsonObject).Key;
        var displayKind = string.IsNullOrWhiteSpace(kind) ? "Step" : kind;
        return new TraditionalTaskEditorStep(
            index,
            displayKind,
            string.Empty,
            displayKind,
            "This step type is not mapped in the MAUI editor yet.",
            null,
            null,
            null);
    }

    private static TraditionalTaskEditorProcessStep ParseProcess(
        JsonObject process,
        IReadOnlyDictionary<string, TraditionalTaskEditorNamedOption> scriptOptions)
    {
        var scriptId = Read(process, "ScriptID").Trim();
        scriptOptions.TryGetValue(scriptId, out var script);
        return new TraditionalTaskEditorProcessStep(
            Read(process, "ID"),
            scriptId,
            script?.Name ?? (scriptId.Length > 0 ? $"Script ID {scriptId}" : string.Empty),
            script?.Type ?? string.Empty,
            FirstNonBlank(Read(process, "Run"), "PerFile"),
            ReadFlag(process, "IsDest"),
            ParseParameters(GetNode(process, "Parameters")));
    }

    private static string ReadStepFolder(JsonObject endpoint, string typeCode)
    {
        // MOVEit Transfer exposes the Task editor folder through FolderName. Its Path member is a
        // different server-side value and must not be used as the editable Task folder. This mirrors
        // Task Preflight, which intentionally prefers FolderName before Path for remote endpoints.
        return TraditionalTaskEditorHostCapabilities.NormalizeTypeCode(typeCode).Equals("siLock", StringComparison.OrdinalIgnoreCase)
            ? FirstNonBlank(Read(endpoint, "FolderName"), Read(endpoint, "Path"))
            : FirstNonBlank(Read(endpoint, "Path"), Read(endpoint, "FolderName"));
    }

    private static TraditionalTaskEditorDestinationStep ParseDestination(
        JsonObject destination,
        IReadOnlyDictionary<string, TraditionalTaskEditorHostOption> hostOptions,
        IReadOnlyDictionary<string, RawHost> hostDefinitions)
    {
        var hostId = Read(destination, "HostID");
        var typeCode = TraditionalTaskEditorHostCapabilities.NormalizeTypeCode(Read(destination, "Type"));
        if (hostId == "0" && typeCode.Length == 0) typeCode = "FileSystem";

        hostOptions.TryGetValue(hostId, out var hostOption);
        if (typeCode.Length == 0) typeCode = hostOption?.TypeCode ?? string.Empty;
        var hostType = hostOption?.Type ?? TraditionalTaskEditorHostCapabilities.FriendlyName(typeCode);
        var hostName = hostOption?.Name ?? ResolveHostName(hostId, typeCode, hostOptions);

        hostDefinitions.TryGetValue(hostId, out var rawHost);
        var defaults = ParseHostDefaults(rawHost?.Host, typeCode);
        var useDefaultCredentials = ReadFlag(destination, "UseDefUser", true);
        var isSharePoint = typeCode.Equals("SharePoint", StringComparison.OrdinalIgnoreCase);
        var isGoogleStorage = typeCode.Equals("GoogleStorage", StringComparison.OrdinalIgnoreCase);
        var isSmtp = typeCode.Equals("SMTP", StringComparison.OrdinalIgnoreCase);
        var sharePointAuthenticationMode = ReadNullableInt(destination, "UsingAppAuthentication") ?? defaults.SharePointAuthenticationMode;
        var googleUseOAuth = ReadNullableFlag(destination, "UseOAuth") ?? defaults.GoogleUseOAuth ?? false;
        var emailUseOAuth = isSmtp ? (useDefaultCredentials ? defaults.EmailUseOAuth : ReadNullableFlag(destination, "UseOAuth") ?? false) : null;
        var emailOAuthClientId = isSmtp ? (useDefaultCredentials ? defaults.EmailOAuthClientId : Read(destination, "OAuthClientID")) : string.Empty;
        var emailOAuthScope = isSmtp ? (useDefaultCredentials ? defaults.EmailOAuthScope : Read(destination, "OAuthScope")) : string.Empty;
        var emailOAuthTokenUrl = isSmtp ? (useDefaultCredentials ? defaults.EmailOAuthTokenUrl : Read(destination, "OAuthTokenURL")) : string.Empty;
        // Infer the convenience provider only for the exact Microsoft 365 pattern. Custom
        // Microsoft endpoints/scopes must round-trip without being rewritten as Office 365.
        var emailOAuthTenantId = Hosts.HostMailOAuthSettings.Tenant(new JsonObject { ["OAuthScope"] = emailOAuthScope, ["OAuthTokenURL"] = emailOAuthTokenUrl });
        var emailOAuthClientSecretConfigured = !useDefaultCredentials && isSmtp && emailUseOAuth == true;
        var passwordConfigured = !useDefaultCredentials
            && ((!isSharePoint && !isGoogleStorage && !isSmtp)
                || isSharePoint && sharePointAuthenticationMode == 0
                || isGoogleStorage && !googleUseOAuth
                || isSmtp && emailUseOAuth != true);
        var sharePointClientSecretConfigured = !useDefaultCredentials && isSharePoint && sharePointAuthenticationMode == 1;
        var googleOAuthClientSecretConfigured = !useDefaultCredentials && isGoogleStorage && googleUseOAuth;

        return new TraditionalTaskEditorDestinationStep(
            Read(destination, "ID"),
            hostId,
            hostName,
            typeCode,
            hostType,
            ReadStepFolder(destination, typeCode),
            ReadFlag(destination, "UseDefBucket", true),
            Read(destination, "Bucket"),
            ReadFlag(destination, "UseDefContainer", true),
            Read(destination, "Container"),
            ReadFlag(destination, "UseDefDocumentLibrary", true),
            Read(destination, "DocumentLibrary"),
            Read(destination, "DocumentLibraryGuid"),
            Read(destination, "AddressTo"),
            Read(destination, "Subject"),
            Read(destination, "Message"),
            ReadFlag(destination, "IsHTML"),
            ReadFlag(destination, "UseRelativeSubdirs"),
            ReadFlag(destination, "ForceDir"),
            ReadFlag(destination, "Zip"),
            ReadFlag(destination, "UseOrigName", true),
            Read(destination, "FileName"),
            ReadNullableInt(destination, "OverwriteOrig") ?? 0,
            ReadFlag(destination, "UseDefRetryCount", true),
            ReadNullableInt(destination, "RetryCount"),
            ReadFlag(destination, "UseDefRetryTimeoutSecs", true),
            ReadNullableInt(destination, "RetryTimeoutSecs"),
            ReadFlag(destination, "UseDefWinCopyFileAPI", true),
            ReadNullableFlag(destination, "UseWinCopyFileAPI") ?? defaults.UseWindowsCopyFileApi,
            ReadFlag(destination, "UseDefPreserveFileTimestamp", true),
            ReadNullableFlag(destination, "PreserveFileTimestamp") ?? defaults.PreserveFileTimestamp,
            useDefaultCredentials,
            Read(destination, "Username"),
            passwordConfigured,
            Read(destination, "Account"),
            !useDefaultCredentials && Read(destination, "Account").Length > 0,
            ReadFlag(destination, "UseDefClientCert", true),
            Read(destination, "ClientCertSHA1"),
            Read(destination, "ClientCertStore"),
            Read(destination, "ClientCertSubject"),
            ReadFlag(destination, "UseDefClientKey", true),
            Read(destination, "ClientKeyID"),
            sharePointAuthenticationMode,
            FirstNonBlank(Read(destination, "ClientID"), defaults.SharePointClientId),
            FirstNonBlank(Read(destination, "TenantID"), defaults.SharePointTenantId),
            FirstNonBlank(Read(destination, "RealmID"), defaults.SharePointRealmId),
            sharePointClientSecretConfigured,
            ReadNullableFlag(destination, "UseOAuth") ?? defaults.GoogleUseOAuth,
            FirstNonBlank(Read(destination, "ProjectID"), defaults.GoogleProjectId),
            FirstNonBlank(Read(destination, "OAuthClientID"), defaults.GoogleOAuthClientId),
            googleOAuthClientSecretConfigured,
            emailUseOAuth,
            emailOAuthClientId,
            emailOAuthScope,
            emailOAuthTokenUrl,
            emailOAuthTenantId,
            emailOAuthClientSecretConfigured,
            ReadFlag(destination, "UseDefConnTimeoutSecs", true),
            ReadNullableInt(destination, "ConnTimeoutSecs"),
            ReadFlag(destination, "UseDefDataTimeoutSecs", true),
            ReadNullableInt(destination, "DataTimeoutSecs"),
            Read(destination, "AddCom"),
            Read(destination, "AddComAX"),
            ReadFlag(destination, "UseDefAB", true),
            FirstNonBlank(Read(destination, "AB"), defaults.TransferType),
            ReadFlag(destination, "UseDefPassive", true),
            ReadNullableFlag(destination, "Passive") ?? defaults.Passive,
            ReadFlag(destination, "UseDefXS", true),
            ReadNullableFlag(destination, "XS") ?? defaults.UseXSha1,
            ReadFlag(destination, "UseDefBlindUL", true),
            ReadNullableFlag(destination, "BlindUL") ?? defaults.BlindUpload,
            ReadFlag(destination, "UseDefRsm", true),
            ReadNullableFlag(destination, "Rsm") ?? defaults.Resume,
            ReadFlag(destination, "UseDefReuseSSL", true),
            ReadNullableFlag(destination, "ReuseSSL") ?? defaults.ReuseTlsSession,
            ReadFlag(destination, "UseDefSetFileAttr", true),
            ReadNullableFlag(destination, "SetFileAttr") ?? defaults.SetFileAttributes,
            FirstNonBlank(Read(destination, "DestFileAttr"), defaults.DestinationFileAttributes),
            defaults);
    }

    private static TraditionalTaskEditorSourceStep ParseSource(
        JsonObject source,
        IReadOnlyDictionary<string, TraditionalTaskEditorHostOption> hostOptions,
        IReadOnlyDictionary<string, RawHost> hostDefinitions)
    {
        var hostId = Read(source, "HostID");
        var sourceTypeCode = TraditionalTaskEditorHostCapabilities.NormalizeTypeCode(Read(source, "Type"));
        if (hostId == "0" && sourceTypeCode.Length == 0) sourceTypeCode = "FileSystem";

        hostOptions.TryGetValue(hostId, out var hostOption);
        if (sourceTypeCode.Length == 0) sourceTypeCode = hostOption?.TypeCode ?? string.Empty;
        var hostType = hostOption?.Type ?? TraditionalTaskEditorHostCapabilities.FriendlyName(sourceTypeCode);
        var hostName = hostOption?.Name ?? ResolveHostName(hostId, sourceTypeCode, hostOptions);

        hostDefinitions.TryGetValue(hostId, out var rawHost);
        var defaults = ParseHostDefaults(rawHost?.Host, sourceTypeCode);
        var specialFilters = ParseSpecialFilters(GetNode(source, "Criteria"));
        var useDefaultCredentials = ReadFlag(source, "UseDefUser", true);
        var isSharePoint = sourceTypeCode.Equals("SharePoint", StringComparison.OrdinalIgnoreCase);
        var isGoogleStorage = sourceTypeCode.Equals("GoogleStorage", StringComparison.OrdinalIgnoreCase);
        var isPop3 = sourceTypeCode.Equals("POP3", StringComparison.OrdinalIgnoreCase);
        var sharePointAuthenticationMode = ReadNullableInt(source, "UsingAppAuthentication") ?? defaults.SharePointAuthenticationMode;
        var googleUseOAuth = ReadNullableFlag(source, "UseOAuth") ?? defaults.GoogleUseOAuth ?? false;
        var emailUseOAuth = useDefaultCredentials
            ? defaults.EmailUseOAuth
            : ReadNullableFlag(source, "UseOAuth") ?? false;
        var emailOAuthClientId = useDefaultCredentials ? defaults.EmailOAuthClientId : Read(source, "OAuthClientID");
        var emailOAuthScope = useDefaultCredentials ? defaults.EmailOAuthScope : Read(source, "OAuthScope");
        var emailOAuthTokenUrl = useDefaultCredentials ? defaults.EmailOAuthTokenUrl : Read(source, "OAuthTokenURL");
        var emailOAuthTenantId = Hosts.HostMailOAuthSettings.Tenant(new JsonObject { ["OAuthScope"] = emailOAuthScope, ["OAuthTokenURL"] = emailOAuthTokenUrl });
        var passwordConfigured = !useDefaultCredentials
            && ((!isSharePoint && !isGoogleStorage && !isPop3)
                || isSharePoint && sharePointAuthenticationMode == 0
                || isGoogleStorage && !googleUseOAuth
                || isPop3 && emailUseOAuth != true);
        var sharePointClientSecretConfigured = !useDefaultCredentials && isSharePoint && sharePointAuthenticationMode == 1;
        var googleOAuthClientSecretConfigured = !useDefaultCredentials && isGoogleStorage && googleUseOAuth;
        var emailOAuthClientSecretConfigured = !useDefaultCredentials && isPop3 && emailUseOAuth == true;

        return new TraditionalTaskEditorSourceStep(
            Read(source, "ID"),
            hostId,
            hostName,
            sourceTypeCode,
            hostType,
            ReadStepFolder(source, sourceTypeCode),
            ReadFlag(source, "UseDefBucket", true),
            Read(source, "Bucket"),
            ReadFlag(source, "UseDefContainer", true),
            Read(source, "Container"),
            ReadFlag(source, "UseDefDocumentLibrary", true),
            Read(source, "DocumentLibrary"),
            Read(source, "DocumentLibraryGuid"),
            Read(source, "FileMask"),
            Read(source, "ExFo"),
            ReadFlag(source, "SearchSubdirs"),
            Read(source, "ExFile"),
            specialFilters is not null,
            specialFilters,
            ReadFlag(source, "NewFilesOnly"),
            ReadFlag(source, "Unzip"),
            ReadFlag(source, "RetryIfNoFiles"),
            ReadNullableInt(source, "DeleteOrig") ?? 0,
            Read(source, "RenameTo"),
            ReadFlag(source, "DelRename"),
            ReadFlag(source, "UseDefRetryCount", true),
            ReadNullableInt(source, "RetryCount"),
            ReadFlag(source, "UseDefRetryTimeoutSecs", true),
            ReadNullableInt(source, "RetryTimeoutSecs"),
            ReadFlag(source, "UseDefRescanSecs", true),
            ReadNullableInt(source, "RescanSecs"),
            ReadFlag(source, "UDMxFi", true),
            ReadNullableInt(source, "MxFi"),
            ReadFlag(source, "UDMxBy", true),
            ReadNullableLong(source, "MxBy"),
            ReadFlag(source, "UseDefWinCopyFileAPI", true),
            ReadNullableFlag(source, "UseWinCopyFileAPI") ?? defaults.UseWindowsCopyFileApi,
            useDefaultCredentials,
            Read(source, "Username"),
            passwordConfigured,
            Read(source, "Account"),
            !useDefaultCredentials && Read(source, "Account").Length > 0,
            ReadFlag(source, "UseDefClientCert", true),
            Read(source, "ClientCertSHA1"),
            Read(source, "ClientCertStore"),
            Read(source, "ClientCertSubject"),
            ReadFlag(source, "UseDefClientKey", true),
            Read(source, "ClientKeyID"),
            sharePointAuthenticationMode,
            FirstNonBlank(Read(source, "ClientID"), defaults.SharePointClientId),
            FirstNonBlank(Read(source, "TenantID"), defaults.SharePointTenantId),
            FirstNonBlank(Read(source, "RealmID"), defaults.SharePointRealmId),
            sharePointClientSecretConfigured,
            ReadNullableFlag(source, "UseOAuth") ?? defaults.GoogleUseOAuth,
            FirstNonBlank(Read(source, "ProjectID"), defaults.GoogleProjectId),
            FirstNonBlank(Read(source, "OAuthClientID"), defaults.GoogleOAuthClientId),
            googleOAuthClientSecretConfigured,
            emailUseOAuth,
            emailOAuthClientId,
            emailOAuthScope,
            emailOAuthTokenUrl,
            emailOAuthTenantId,
            emailOAuthClientSecretConfigured,
            ReadFlag(source, "UseDefSort", true),
            Read(source, "Sort"),
            ReadFlag(source, "UseDefConnTimeoutSecs", true),
            ReadNullableInt(source, "ConnTimeoutSecs"),
            ReadFlag(source, "UseDefDataTimeoutSecs", true),
            ReadNullableInt(source, "DataTimeoutSecs"),
            Read(source, "AddCom"),
            Read(source, "AddComAX"),
            ReadFlag(source, "UseDefAB", true),
            FirstNonBlank(Read(source, "AB"), defaults.TransferType),
            ReadFlag(source, "UseDefPassive", true),
            ReadNullableFlag(source, "Passive") ?? defaults.Passive,
            ReadFlag(source, "UseDefXS", true),
            ReadNullableFlag(source, "XS") ?? defaults.UseXSha1,
            ReadFlag(source, "UseDefBlindDL", true),
            ReadNullableFlag(source, "BlindDL") ?? defaults.BlindDownload,
            ReadFlag(source, "UseDefRsm", true),
            ReadNullableFlag(source, "Rsm") ?? defaults.Resume,
            ReadFlag(source, "UseDefReuseSSL", true),
            ReadNullableFlag(source, "ReuseSSL") ?? defaults.ReuseTlsSession,
            ReadFlag(source, "UseDefMD5Get", true),
            FirstNonBlank(Read(source, "MD5Get"), defaults.Md5Mode),
            ReadFlag(source, "UseDefMD5File", true),
            FirstNonBlank(Read(source, "MD5File"), defaults.Md5FileName),
            ReadFlag(source, "UseDefDLInteg", true),
            FirstNonBlank(Read(source, "DLInteg"), defaults.DataIntegrityRule),
            defaults, ReadFlag(source, "UseDefPreserveFileTimestamp", true), ReadFlag(source, "PreserveFileTimestamp"));
    }

    private static TraditionalTaskEditorHostDefaults ParseHostDefaults(JsonObject? host, string? typeCode)
    {
        var normalizedType = TraditionalTaskEditorHostCapabilities.NormalizeTypeCode(typeCode);
        var isSharePoint = normalizedType.Equals("SharePoint", StringComparison.OrdinalIgnoreCase);
        var isGoogleStorage = normalizedType.Equals("GoogleStorage", StringComparison.OrdinalIgnoreCase);
        var isEmail = normalizedType.Equals("POP3", StringComparison.OrdinalIgnoreCase)
            || normalizedType.Equals("SMTP", StringComparison.OrdinalIgnoreCase);

        // MOVEit reuses names such as UseOAuth/OAuthClientID across unrelated Host schemas. Keep
        // protocol-specific defaults isolated so hidden state from one Host profile cannot be
        // interpreted as configured state for another profile in the client editor.
        return new(
            ReadNullableInt(host, "DefRetryCount"),
            ReadNullableInt(host, "DefRetryTimeoutSecs"),
            ReadNullableInt(host, "DefRescanSecs"),
            ReadNullableInt(host, "DefMxFi"),
            ReadNullableLong(host, "DefMxBy"),
            ReadNullableFlag(host, "DefWinCopyFileAPI"),
            ReadNullableFlag(host, "DefPreserveFileTimestamp"),
            Read(host, "DefUsername"),
            Read(host, "DefClientCertSHA1"),
            Read(host, "DefClientCertStore"),
            Read(host, "DefClientCertSubject"),
            Read(host, "DefClientKeyID"),
            ReadNullableFlag(host, "DefSetFileAttr"),
            Read(host, "DefDestFileAttr"),
            Read(host, "DefSort"),
            ReadNullableInt(host, "DefConnTimeoutSecs"),
            ReadNullableInt(host, "DefDataTimeoutSecs"),
            Read(host, "DefAB"),
            ReadNullableFlag(host, "DefPassive"),
            ReadNullableFlag(host, "DefXS"),
            ReadNullableFlag(host, "DefBlindDL"),
            ReadNullableFlag(host, "DefBlindUL"),
            ReadNullableFlag(host, "DefRsm"),
            ReadNullableFlag(host, "DefReuseSSL"),
            Read(host, "DefMD5Get"),
            Read(host, "DefMD5File"),
            Read(host, "DefBucket"),
            Read(host, "DefContainer"),
            Read(host, "DefDocumentLibrary"),
            Read(host, "DefDocumentLibraryGuid"),
            isSharePoint ? ReadNullableInt(host, "UsingAppAuthentication") : null,
            isSharePoint ? Read(host, "ClientID") : string.Empty,
            isSharePoint ? Read(host, "TenantID") : string.Empty,
            isSharePoint ? Read(host, "RealmID") : string.Empty,
            isGoogleStorage ? ReadNullableFlag(host, "UseOAuth") : null,
            isGoogleStorage ? Read(host, "ProjectID") : string.Empty,
            isGoogleStorage ? Read(host, "OAuthClientID") : string.Empty,
            isEmail ? ReadNullableFlag(host, "UseOAuth") : null,
            isEmail ? Read(host, "OAuthClientID") : string.Empty,
            isEmail ? Read(host, "OAuthScope") : string.Empty,
            isEmail ? Read(host, "OAuthTokenURL") : string.Empty,
            isEmail ? Hosts.HostMailOAuthSettings.Tenant(host ?? new JsonObject()) : string.Empty,
            Read(host, "DefDLInteg"));
    }

    private static IReadOnlyList<TraditionalTaskEditorHostOption> BuildHostOptions(
        IReadOnlyList<JsonObject> rawHosts,
        IReadOnlySet<string> allowedTypes)
    {
        var local = rawHosts.Select(UnwrapHost).FirstOrDefault(item => item.TypeCode == "FileSystem" && Read(item.Host, "ID") == "0").Host;
        var result = new List<TraditionalTaskEditorHostOption>
        {
            new("0", "Local File System", "FileSystem", "Local File System", string.Empty, ParseHostDefaults(local, "FileSystem"))
        };

        foreach (var wrapper in rawHosts)
        {
            var (typeCode, host) = UnwrapHost(wrapper);
            if (host is null || !allowedTypes.Contains(typeCode) || typeCode.Equals("FileSystem", StringComparison.OrdinalIgnoreCase)) continue;
            var id = Read(host, "ID").Trim();
            var name = Read(host, "Name").Trim();
            if (id.Length == 0 || name.Length == 0) continue;

            result.Add(new TraditionalTaskEditorHostOption(
                id,
                name,
                typeCode,
                TraditionalTaskEditorHostCapabilities.FriendlyName(typeCode),
                FirstNonBlank(
                    Read(host, "Destination"), Read(host, "Host"), Read(host, "UNC"), Read(host, "StorageAccount"),
                    Read(host, "SiteUrl"), Read(host, "SiteURL"), Read(host, "S3AlternativeEndpoint"),
                    Read(host, "DefBucket"), Read(host, "DefContainer"), Read(host, "Bucket"), Read(host, "Container")),
                ParseHostDefaults(host, typeCode)));
        }

        return result
            .GroupBy(host => host.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(host => host.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<TraditionalTaskEditorTlsCertificateOption> BuildTlsCertificateOptions(
        IReadOnlyList<JsonObject> certificates)
        => certificates
            .Select(certificate => new TraditionalTaskEditorTlsCertificateOption(
                FirstNonBlank(Read(certificate, "Name"), Read(certificate, "IssuedTo"), Read(certificate, "Sha1Thumbprint")),
                Read(certificate, "Sha1Thumbprint"),
                FirstNonBlank(Read(certificate, "Store"), "My"),
                Read(certificate, "ExpDate")))
            .Where(certificate => certificate.Sha1Thumbprint.Length > 0)
            .GroupBy(certificate => certificate.Sha1Thumbprint.Replace(" ", string.Empty), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(certificate => certificate.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<TraditionalTaskEditorNamedOption> BuildSshClientKeyOptions(
        IReadOnlyList<JsonObject> keys)
        => keys
            .Select(key => new TraditionalTaskEditorNamedOption(
                Read(key, "ID").Trim(),
                FirstNonBlank(Read(key, "Name"), Read(key, "ID")).Trim(),
                "SSH Client Key"))
            .Where(key => key.Id.Length > 0 && key.Name.Length > 0)
            .GroupBy(key => key.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(key => key.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static IReadOnlyList<TraditionalTaskEditorNamedOption> BuildScriptOptions(
        IReadOnlyList<JsonObject> customScripts,
        IReadOnlyList<JsonObject> standardScripts)
    {
        var result = new List<TraditionalTaskEditorNamedOption>();

        result.AddRange(customScripts.Select(script => new TraditionalTaskEditorNamedOption(
            Read(script, "ID").Trim(),
            Read(script, "Name").Trim(),
            "Custom Script", Read(script, "Description"))));

        result.AddRange(standardScripts.Select(script => new TraditionalTaskEditorNamedOption(
            Read(script, "ID").Trim(),
            Read(script, "Name").Trim(),
            "Standard Script", Read(script, "Description"))));

        return result
            .Where(script => script.Id.Length > 0 && script.Name.Length > 0)
            .GroupBy(script => script.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(script => script.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolveHostName(string hostId, string typeCode, IReadOnlyDictionary<string, TraditionalTaskEditorHostOption> hostOptions)
    {
        if (hostOptions.TryGetValue(hostId, out var host)) return host.Name;
        if (hostId == "0") return "Local File System";
        var friendly = TraditionalTaskEditorHostCapabilities.FriendlyName(typeCode);
        return hostId.Length > 0 ? $"{friendly} (ID {hostId})" : friendly;
    }

    private static (string TypeCode, JsonObject? Host) UnwrapHost(JsonObject wrapper)
    {
        foreach (var key in TraditionalTaskEditorHostCapabilities.WrapperTypeCodes)
        {
            if (GetNode(wrapper, key) is JsonObject typedHost)
                return (TraditionalTaskEditorHostCapabilities.NormalizeTypeCode(key), typedHost);
        }

        if (Read(wrapper, "ID").Length > 0) return (TraditionalTaskEditorHostCapabilities.NormalizeTypeCode(Read(wrapper, "Type")), wrapper);
        var first = wrapper.FirstOrDefault(pair => pair.Value is JsonObject);
        return first.Value is JsonObject host ? (TraditionalTaskEditorHostCapabilities.NormalizeTypeCode(first.Key), host) : (string.Empty, null);
    }

    private static IReadOnlyList<JsonObject> GetStepObjects(JsonNode? root)
    {
        if (root is JsonArray array) return array.OfType<JsonObject>().ToList();
        if (root is not JsonObject obj) return Array.Empty<JsonObject>();
        if (GetNode(obj, "Step") is JsonArray steps) return steps.OfType<JsonObject>().ToList();
        if (GetNode(obj, "Step") is JsonObject single) return new[] { single };
        return obj.Select(pair => pair.Value).OfType<JsonObject>().ToList();
    }

    private static string GetTaskType(string taskType) => taskType switch
    {
        "COS" => "Advanced",
        "Sy" => "Synchronization",
        _ => "Traditional"
    };

    private static TraditionalTaskEditorSpecialFilters? ParseSpecialFilters(JsonNode? root)
    {
        if (root is null) return null;
        if (root is not JsonObject criteria)
            return new TraditionalTaskEditorSpecialFilters("all", Array.Empty<TraditionalTaskEditorSpecialFilterCriterion>(), true);
        if (criteria.Count == 0) return null;

        var andNode = GetNode(criteria, "And");
        var orNode = GetNode(criteria, "Or");
        var matchMode = orNode is not null && andNode is null ? "any" : "all";
        var groupNode = andNode ?? orNode ?? criteria;
        var comparisons = GetCriteriaComparisons(groupNode);
        if (comparisons.Count == 0)
            return new TraditionalTaskEditorSpecialFilters(matchMode, Array.Empty<TraditionalTaskEditorSpecialFilterCriterion>(), true);

        var filters = new List<TraditionalTaskEditorSpecialFilterCriterion>();
        var unsupported = andNode is not null && orNode is not null
            || !HasOnlyProperties(criteria, "And", "Or", "comp", "a", "test", "b")
            || comparisons.Count > 6;

        if (groupNode is JsonObject groupObject && !ReferenceEquals(groupObject, criteria))
            unsupported |= !HasOnlyProperties(groupObject, "comp");

        foreach (var comparison in comparisons)
        {
            unsupported |= !HasOnlyProperties(comparison, "a", "test", "b");
            if (TryParseSpecialFilterCriterion(comparison, out var filter)) filters.Add(filter);
            else unsupported = true;
        }

        return new TraditionalTaskEditorSpecialFilters(matchMode, filters, unsupported);
    }

    private static bool HasOnlyProperties(JsonObject value, params string[] allowed)
    {
        var names = new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase);
        return value.All(pair => names.Contains(pair.Key));
    }

    private static IReadOnlyList<JsonObject> GetCriteriaComparisons(JsonNode? groupNode)
    {
        if (groupNode is JsonObject group)
        {
            var compNode = GetNode(group, "comp");
            if (compNode is JsonArray array) return array.OfType<JsonObject>().ToList();
            if (compNode is JsonObject single) return new[] { single };
            if (GetNode(group, "a") is not null && GetNode(group, "test") is not null && GetNode(group, "b") is not null)
                return new[] { group };
        }
        if (groupNode is JsonArray directArray) return directArray.OfType<JsonObject>().ToList();
        return Array.Empty<JsonObject>();
    }

    private static bool TryParseSpecialFilterCriterion(JsonObject comparison, out TraditionalTaskEditorSpecialFilterCriterion filter)
    {
        filter = new TraditionalTaskEditorSpecialFilterCriterion(string.Empty, string.Empty, string.Empty, string.Empty);
        var left = Read(comparison, "a").Trim();
        var test = Read(comparison, "test").Trim().ToUpperInvariant();
        var right = Read(comparison, "b").Trim();

        if (left.Equals("[FileSize]", StringComparison.OrdinalIgnoreCase))
        {
            var op = test switch
            {
                "NEQ" => "eq",
                "NNE" => "ne",
                "NGT" => "gt",
                "NGE" => "ge",
                "NLT" => "lt",
                "NLE" => "le",
                _ => string.Empty
            };
            if (op.Length == 0 || !long.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes) || bytes < 0)
                return false;

            var (value, unit) = FriendlyFileSize(bytes);
            filter = new TraditionalTaskEditorSpecialFilterCriterion("fileSize", op, value, unit);
            return true;
        }

        if (left.Equals("[FileDateStamp]", StringComparison.OrdinalIgnoreCase) && (test == "DGT" || test == "DLT"))
        {
            var match = Regex.Match(right, @"^\[DateSubtract\(\[Now\],(?<value>\d+)(?<unit>[HDMY])\)\]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success) return false;
            var unit = match.Groups["unit"].Value.ToUpperInvariant() switch
            {
                "H" => "hours",
                "D" => "days",
                "M" => "months",
                "Y" => "years",
                _ => string.Empty
            };
            if (unit.Length == 0) return false;
            filter = new TraditionalTaskEditorSpecialFilterCriterion(
                "fileModified",
                test == "DGT" ? "newer" : "older",
                match.Groups["value"].Value,
                unit);
            return true;
        }

        return false;
    }

    private static (string Value, string Unit) FriendlyFileSize(long bytes)
    {
        var units = new (long Multiplier, string Unit)[]
        {
            (1024L * 1024L * 1024L, "GB"),
            (1024L * 1024L, "MB"),
            (1024L, "KB")
        };
        foreach (var (multiplier, unit) in units)
        {
            if (bytes >= multiplier && bytes % multiplier == 0)
                return ((bytes / multiplier).ToString(CultureInfo.InvariantCulture), unit);
        }
        return (bytes.ToString(CultureInfo.InvariantCulture), "B");
    }

    private static IReadOnlyList<TraditionalTaskEditorParameter> ParseParameters(JsonNode? root)
    {
        if (root is null) return Array.Empty<TraditionalTaskEditorParameter>();

        // MOVEit responses have used both the documented { any:[{name,value,secure}] } shape
        // and a keyed response shape such as { ParameterName:{ value, DisplayValue } }.
        // Normalize both without manufacturing values for secure/suppressed parameters.
        if (root is JsonObject rootObject)
        {
            // A captured native Web Admin Task PUT uses { Parameters: { Parameter: [...] } }.
            // The public REST model has also exposed { any:[...] }, and older responses may be keyed.
            // Read all known shapes; write serialization is handled separately by the save service.
            var parameterItems = GetNode(rootObject, "Parameter");
            if (parameterItems is not null) return ParseParameterItems(parameterItems);

            var any = GetNode(rootObject, "any");
            if (any is not null) return ParseParameterItems(any);

            var keyed = new List<TraditionalTaskEditorParameter>();
            foreach (var pair in rootObject)
            {
                if (pair.Value is not JsonObject parameter) continue;
                var name = FirstNonBlank(Read(parameter, "name"), pair.Key).Trim();
                if (name.Length == 0) continue;
                var displayValue = FirstNonBlank(Read(parameter, "displayValue"), Read(parameter, "DisplayValue"), Read(parameter, "countedDisplayValue"));
                var secure = ReadFlag(parameter, "secure") || displayValue.Equals("(suppressed)", StringComparison.OrdinalIgnoreCase);
                var value = secure ? "(suppressed)" : FirstNonBlank(Read(parameter, "value"), displayValue);
                keyed.Add(new TraditionalTaskEditorParameter(name, value, secure));
            }
            return keyed;
        }

        return ParseParameterItems(root);
    }

    private static IReadOnlyList<TraditionalTaskEditorParameter> ParseParameterItems(JsonNode? root)
    {
        IEnumerable<JsonObject> items = root switch
        {
            JsonArray array => array.OfType<JsonObject>(),
            JsonObject single => new[] { single },
            _ => Array.Empty<JsonObject>()
        };

        return items
            .Select(parameter =>
            {
                var name = FirstNonBlank(Read(parameter, "Name"), Read(parameter, "name")).Trim();
                var displayValue = FirstNonBlank(Read(parameter, "displayValue"), Read(parameter, "DisplayValue"), Read(parameter, "countedDisplayValue"));
                var secure = ReadFlag(parameter, "secure") || displayValue.Equals("(suppressed)", StringComparison.OrdinalIgnoreCase);
                var value = secure ? "(suppressed)" : FirstNonBlank(Read(parameter, "value"), displayValue);
                return new TraditionalTaskEditorParameter(name, value, secure);
            })
            .Where(parameter => parameter.Name.Length > 0)
            .ToList();
    }



    private static IReadOnlyList<JsonObject> GetDirectObjectList(JsonNode? root)
    {
        if (root is JsonArray array) return array.OfType<JsonObject>().ToList();
        if (root is JsonObject single) return new[] { single };
        return Array.Empty<JsonObject>();
    }

    private static IReadOnlyList<string> ReadStringValues(JsonNode? root)
    {
        if (root is JsonArray array)
            return array.Select(item => NodeString(item)).Select(value => value.Trim()).Where(value => value.Length > 0).ToList();

        var single = NodeString(root).Trim();
        return single.Length == 0 ? Array.Empty<string>() : new[] { single };
    }

    private static IReadOnlyList<int> ReadIntValues(JsonNode? root)
        => ReadStringValues(root)
            .Select(value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? (int?)parsed : null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();

    private static IReadOnlyList<JsonObject> GetCollectionObjects(JsonNode? root, string itemProperty)
    {
        if (root is null) return Array.Empty<JsonObject>();
        if (root is JsonArray directArray) return directArray.OfType<JsonObject>().ToList();
        if (root is not JsonObject obj) return Array.Empty<JsonObject>();
        var items = GetNode(obj, itemProperty);
        if (items is JsonArray array) return array.OfType<JsonObject>().ToList();
        if (items is JsonObject single) return new[] { single };
        return Array.Empty<JsonObject>();
    }

    private static JsonNode? GetNode(JsonObject? obj, string name)
    {
        if (obj is null) return null;
        if (obj.TryGetPropertyValue(name, out var direct)) return direct;
        foreach (var pair in obj)
            if (pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)) return pair.Value;
        return null;
    }

    private static bool ReadFlag(JsonObject? obj, string name, bool fallback = false)
        => ReadNullableFlag(obj, name) ?? fallback;

    private static bool? ReadNullableFlag(JsonObject? obj, string name)
    {
        var node = GetNode(obj, name);
        if (node is null) return null;
        var value = NodeString(node).Trim();
        if (value.Equals("1", StringComparison.OrdinalIgnoreCase) || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.Equals("0", StringComparison.OrdinalIgnoreCase) || value.Equals("false", StringComparison.OrdinalIgnoreCase) || value.Equals("no", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }

    private static int? ReadNullableInt(JsonObject? obj, string name)
        => int.TryParse(Read(obj, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static long? ReadNullableLong(JsonObject? obj, string name)
        => long.TryParse(Read(obj, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static string Read(JsonObject? obj, string name, string fallback = "")
    {
        var node = GetNode(obj, name);
        return node is null ? fallback : NodeString(node, fallback);
    }

    // Reads a MOVEit JSON scalar without using exception-driven type conversion.
    // JsonNode.GetValue<string>() throws InvalidOperationException for the numeric and Boolean
    // values MOVEit legitimately returns throughout Task/Host definitions; the editor reads many
    // such values at once, so using GetValue<string>() as a probe caused repeated first-chance
    // System.Text.Json exceptions while opening a Task.
    private static string NodeString(JsonNode? node, string fallback = "")
    {
        if (node is null) return fallback;
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text)) return text ?? fallback;
            if (value.TryGetValue<int>(out var intValue)) return intValue.ToString(CultureInfo.InvariantCulture);
            if (value.TryGetValue<long>(out var longValue)) return longValue.ToString(CultureInfo.InvariantCulture);
            if (value.TryGetValue<decimal>(out var decimalValue)) return decimalValue.ToString(CultureInfo.InvariantCulture);
            if (value.TryGetValue<double>(out var doubleValue)) return doubleValue.ToString(CultureInfo.InvariantCulture);
            if (value.TryGetValue<bool>(out var boolValue)) return boolValue ? "true" : "false";
        }

        // Read() is for scalar MOVEit properties. Do not flatten object/array JSON into a field
        // value if a server version returns an unexpected shape.
        return fallback;
    }

    private static string FirstNonBlank(params string[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private sealed record RawHost(string TypeCode, JsonObject Host, string Id);
}

