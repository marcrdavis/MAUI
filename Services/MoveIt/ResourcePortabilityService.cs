// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Portable Script/Date List import/export using the common MAUI multi-item package format.

using System.Xml.Linq;
using MoveItAutomationUI.Services.Metadata;

namespace MoveItAutomationUI.Services.MoveIt;

public interface IResourcePortabilityService
{
    Task<ResourceExportFile> ExportScriptAsync(string scriptId, CancellationToken cancellationToken = default);
    Task<ResourceExportFile> ExportScriptsAsync(IReadOnlyList<string> scriptIds, CancellationToken cancellationToken = default);
    Task<ResourceImportPreflight> PreflightScriptImportAsync(byte[] fileBytes, string mode, CancellationToken cancellationToken = default);
    Task<ResourceImportResult> ImportScriptAsync(byte[] fileBytes, string mode, bool acknowledgeUnsupportedMetadata, string username, CancellationToken cancellationToken = default);
    Task<ResourceExportFile> ExportDateListAsync(string dateListId, CancellationToken cancellationToken = default);
    Task<ResourceExportFile> ExportDateListsAsync(IReadOnlyList<string> dateListIds, CancellationToken cancellationToken = default);
    Task<ResourceImportPreflight> PreflightDateListImportAsync(byte[] fileBytes, string mode, CancellationToken cancellationToken = default);
    Task<ResourceImportResult> ImportDateListAsync(byte[] fileBytes, string mode, bool acknowledgeUnsupportedMetadata, string username, CancellationToken cancellationToken = default);
}

public sealed record ResourceExportFile(string FileName, byte[] Content);
public sealed record ResourceImportTarget(string Id, string Name, string Type, bool CanEdit);
public sealed record ResourceImportItemPreflight(
    string SourceId,
    string SourceName,
    string ResourceType,
    string SuggestedName,
    bool SuggestedNameAvailable,
    string PlannedAction,
    string TargetId,
    MetadataImportSummary Metadata,
    string Message);
public sealed record ResourceImportPreflight(
    bool Valid,
    string SourceId,
    string SourceName,
    string ResourceType,
    DateTimeOffset? ExportedUtc,
    string SuggestedName,
    bool SuggestedNameAvailable,
    IReadOnlyList<ResourceImportTarget> Targets,
    MetadataImportSummary Metadata,
    string Message,
    int ItemCount,
    bool IsMultiItem,
    int CreateCount,
    int OverwriteCount,
    IReadOnlyList<ResourceImportItemPreflight> Items);
public sealed record ResourceImportItemResult(string SourceName, string Id, string Name, bool Success, bool Created, string Warning, string Error);
public sealed record ResourceImportResult(
    string Id,
    string Name,
    bool Created,
    string Warning,
    int ItemCount,
    int ImportedCount,
    int CreatedCount,
    int OverwrittenCount,
    int FailedCount,
    IReadOnlyList<ResourceImportItemResult> Items);

public sealed class ResourcePortabilityService : IResourcePortabilityService
{
    private readonly IAutomationScriptService _scripts;
    private readonly IMoveItObjectAccessService? _access;
    private readonly IAutomationDateListService _dateLists;
    private readonly IScriptUndoService _scriptUndo;
    private readonly IDateListUndoService _dateListUndo;
    private readonly IMetadataRepository _metadata;
    private readonly IMauiMetadataPortabilityService _portableMetadata;
    private readonly IMoveItSessionService _session;
    private readonly ILogger<ResourcePortabilityService> _logger;

    public ResourcePortabilityService(IAutomationScriptService scripts, IAutomationDateListService dateLists,
        IScriptUndoService scriptUndo, IDateListUndoService dateListUndo, IMetadataRepository metadata,
        IMauiMetadataPortabilityService portableMetadata, IMoveItSessionService session, ILogger<ResourcePortabilityService> logger, IMoveItObjectAccessService? access = null)
    {
        _scripts = scripts;
        _access = access;
        _dateLists = dateLists;
        _scriptUndo = scriptUndo;
        _dateListUndo = dateListUndo;
        _metadata = metadata;
        _portableMetadata = portableMetadata;
        _session = session;
        _logger = logger;
    }

    public Task<ResourceExportFile> ExportScriptAsync(string scriptId, CancellationToken cancellationToken = default) =>
        ExportScriptsAsync(new[] { NormalizeId(scriptId, "Script") }, cancellationToken);

    public async Task<ResourceExportFile> ExportScriptsAsync(IReadOnlyList<string> scriptIds, CancellationToken ct = default)
    {
        var ids = NormalizeIds(scriptIds, "Script");
        var session = RequireSession();
        var items = new List<XElement>(ids.Count);
        string firstName = string.Empty;
        string firstId = string.Empty;
        foreach (var id in ids)
        {
            if (_access is not null) await _access.RequireAsync("Script", id, "Edit", ct);
            var script = await _scripts.GetScriptAsync(id, ct);
            if (script.IsBuiltIn) throw new InvalidOperationException($"Built-in MOVEit Script '{script.Name}' cannot be exported as a portable MAUI custom Script.");
            var snapshot = await _scripts.GetScriptConfigurationSnapshotAsync(id, ct);
            var root = ParseConfiguration(snapshot.Xml, "Script", "Script");
            var metadata = await _portableMetadata.BuildExportElementAsync(session.GetEnvironmentKey(), "Script", snapshot.Id, ct);
            items.Add(MauiPortablePackage.BuildItem(snapshot.Id, snapshot.Name, snapshot.LanguageCode, root, metadata));
            if (firstName.Length == 0) { firstName = snapshot.Name; firstId = snapshot.Id; }
        }
        var bytes = MauiPortablePackage.Build("Script", items, DateTimeOffset.UtcNow);
        var fileName = ids.Count == 1 ? PortabilitySupport.BuildSafeFileName(firstName, firstId, "Script") : MauiPortablePackage.BuildBulkFileName("Script", ids.Count);
        return new ResourceExportFile(fileName, bytes);
    }

    public async Task<ResourceImportPreflight> PreflightScriptImportAsync(byte[] bytes, string mode, CancellationToken ct = default)
    {
        var session = RequireSession();
        var packages = ParseScriptPackages(bytes);
        var normalizedMode = NormalizeMode(mode);
        if (_access is not null)
            foreach (var package in packages) await _access.RequireXmlCreationAsync("Script", package.Root, ct);
        var inventory = (await _scripts.GetScriptSnapshotAsync(true, ct)).Scripts;
        var customTargets = inventory.Where(item => !item.IsBuiltIn).OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => new ResourceImportTarget(item.Id, item.Name, item.LanguageCode, item.CanEditDelete)).ToList();
        var usedNames = new HashSet<string>(inventory.Select(item => item.Name), StringComparer.OrdinalIgnoreCase);
        var isMulti = packages.Count > 1;
        var plans = new List<ResourceImportItemPreflight>(packages.Count);

        var plannedOverwriteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packages)
        {
            var available = !usedNames.Contains(package.Name);
            var suggested = package.Name;
            var action = normalizedMode == "overwrite" ? "overwrite" : "create";
            var plannedTarget = string.Empty;
            var message = string.Empty;

            if (normalizedMode == "overwrite")
            {
                var existing = inventory.FirstOrDefault(item => item.Name.Equals(package.Name, StringComparison.OrdinalIgnoreCase));
                var target = customTargets.FirstOrDefault(item => item.Name.Equals(package.Name, StringComparison.OrdinalIgnoreCase));
                if (target is null)
                {
                    available = false;
                    message = existing is not null
                        ? $"Script '{package.Name}' exists but is built-in or otherwise cannot be overwritten."
                        : $"Script '{package.Name}' does not exist. Update existing Scripts requires an existing Script with the same name as the export file.";
                }
                else if (!plannedOverwriteNames.Add(package.Name))
                {
                    available = false;
                    message = $"The package contains more than one Script named '{package.Name}'. Each exported Script name must be unique for Update existing Scripts.";
                }
                else
                {
                    plannedTarget = target.Id;
                    available = true;
                    if (!target.CanEdit) message = $"MOVEit reports that the current user cannot edit Script '{target.Name}'.";
                    else if (!target.Type.Equals(package.Language, StringComparison.OrdinalIgnoreCase))
                        message = $"The exported Script language is {FriendlyLanguage(package.Language)}, but '{target.Name}' is {FriendlyLanguage(target.Type)}. MAUI does not overwrite a Script with a different language.";
                }
            }
            else
            {
                if (!available) message = $"Script '{package.Name}' already exists. Choose Update existing Scripts to update the same-name Script.";
                else usedNames.Add(package.Name);
            }

            plans.Add(new ResourceImportItemPreflight(package.Id, package.Name, FriendlyLanguage(package.Language), suggested, available,
                action, plannedTarget, await _portableMetadata.ValidateAsync(session.GetEnvironmentKey(), package.Metadata, "Script", ct), message));
        }

        return BuildPreflight(packages[0].Id, packages[0].Name, FriendlyLanguage(packages[0].Language), packages[0].ExportedUtc,
            customTargets, plans, packages.Count);
    }

    public async Task<ResourceImportResult> ImportScriptAsync(byte[] bytes, string mode, bool ackMetadata, string username, CancellationToken ct = default)
    {
        var packages = ParseScriptPackages(bytes);
        var normalizedMode = NormalizeMode(mode);
        var preflight = await PreflightScriptImportAsync(bytes, normalizedMode, ct);
        ValidateMetadataAck(preflight, ackMetadata, "Script");
        if (!string.IsNullOrWhiteSpace(preflight.Message)) throw new InvalidOperationException(preflight.Message);

        if (packages.Count == 1)
        {
            var plan = preflight.Items[0];
            var item = await ImportOneScriptAsync(packages[0], plan.PlannedAction, plan.SuggestedName, plan.TargetId, username, plan.Metadata, ct);
            return BuildResult(new[] { item });
        }

        var results = new List<ResourceImportItemResult>(packages.Count);
        for (var i = 0; i < packages.Count; i++)
        {
            var package = packages[i];
            var plan = preflight.Items[i];
            try
            {
                results.Add(await ImportOneScriptAsync(package, plan.PlannedAction, plan.SuggestedName,
                    plan.PlannedAction == "overwrite" ? plan.TargetId : string.Empty, username, plan.Metadata, ct));
            }
            catch (OperationCanceledException) { throw; }
            catch (MoveItAuthenticationException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bulk Script import failed for exported Script {ScriptName}.", package.Name);
                results.Add(new ResourceImportItemResult(package.Name, string.Empty, plan.SuggestedName, false, plan.PlannedAction == "create", string.Empty, ex.Message));
            }
        }
        return BuildResult(results);
    }

    private async Task<ResourceImportItemResult> ImportOneScriptAsync(ScriptPackage package, string mode, string name, string targetId,
        string username, MetadataImportSummary metadataSummary, CancellationToken ct)
    {
        var session = RequireSession();
        var env = session.GetEnvironmentKey();
        var warning = MetadataIgnoredWarning(metadataSummary);
        if (mode.Equals("create", StringComparison.OrdinalIgnoreCase))
        {
            MoveItCreatedScript? shell = null;
            try
            {
                shell = await _scripts.CreateScriptAsync(name, package.Language, ct, package.Root.Elements().FirstOrDefault(element => element.Name.LocalName == "Group")?.Value);
                var restored = await _scripts.RestoreScriptConfigurationAsync(shell.Id, RewriteIdentity(package.Root, shell.Id, name).ToString(SaveOptions.DisableFormatting), ct);
                warning = AppendWarning(warning, await RecordCreatedAsync(env, "Script", restored.Id, username, ct));
                warning = await ApplyMetadataAsync(env, "Script", restored.Id, package.Metadata, username, warning, ct);
                return new ResourceImportItemResult(package.Name, restored.Id, restored.Name, true, true, warning, string.Empty);
            }
            catch (Exception ex) when (shell is not null && ex is not OperationCanceledException)
            {
                try { await _scripts.DeleteScriptAsync(shell!.Id, ct); }
                catch (Exception cleanup) when (cleanup is not OperationCanceledException)
                { _logger.LogError(cleanup, "Unable to remove temporary Script {ScriptId} after import failure.", shell!.Id); throw new InvalidOperationException($"The Script import failed and MAUI could not remove temporary Script ID {shell!.Id}. Original error: {ex.Message}", ex); }
                throw;
            }
        }

        var id = NormalizeId(targetId, "Script");
        if (_access is not null) await _access.RequireAsync("Script", id, "Edit", ct);
        var live = (await _scripts.GetScriptSnapshotAsync(true, ct)).Scripts;
        if (live.Any(item => !item.Id.Equals(id, StringComparison.OrdinalIgnoreCase) && item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new ScriptNameAlreadyExistsException(name);
        var target = live.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException("The same-name destination Script no longer exists or is not visible.");
        if (target.IsBuiltIn || !target.CanEditDelete) throw new InvalidOperationException($"MOVEit reports that Script '{target.Name}' cannot be edited.");
        if (!target.LanguageCode.Equals(package.Language, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"The exported Script language is {FriendlyLanguage(package.Language)}, but '{target.Name}' is {FriendlyLanguage(target.LanguageCode)}. MAUI does not overwrite a Script with a different language.");
        await _scriptUndo.CaptureAsync(id, username, ct);
        var restoredTarget = await _scripts.RestoreScriptConfigurationAsync(id, RewriteIdentity(package.Root, id, name).ToString(SaveOptions.DisableFormatting), ct);
        warning = AppendWarning(warning, await RecordModifiedAsync(env, "Script", restoredTarget.Id, username, ct));
        warning = await ApplyMetadataAsync(env, "Script", restoredTarget.Id, package.Metadata, username, warning, ct);
        return new ResourceImportItemResult(package.Name, restoredTarget.Id, restoredTarget.Name, true, false, warning, string.Empty);
    }

    public Task<ResourceExportFile> ExportDateListAsync(string dateListId, CancellationToken cancellationToken = default) =>
        ExportDateListsAsync(new[] { NormalizeId(dateListId, "Date List") }, cancellationToken);

    public async Task<ResourceExportFile> ExportDateListsAsync(IReadOnlyList<string> dateListIds, CancellationToken ct = default)
    {
        var ids = NormalizeIds(dateListIds, "Date List");
        var session = RequireSession();
        var items = new List<XElement>(ids.Count);
        string firstName = string.Empty;
        string firstId = string.Empty;
        foreach (var id in ids)
        {
            if (_access is not null) await _access.RequireAsync("DateList", id, "Edit", ct);
            var snapshot = await _dateLists.GetConfigurationSnapshotAsync(id, ct);
            var root = ParseConfiguration(snapshot.Xml, "DateList", "Date List");
            var metadata = await _portableMetadata.BuildExportElementAsync(session.GetEnvironmentKey(), "DateList", snapshot.Id, ct);
            items.Add(MauiPortablePackage.BuildItem(snapshot.Id, snapshot.Name, "Date List", root, metadata));
            if (firstName.Length == 0) { firstName = snapshot.Name; firstId = snapshot.Id; }
        }
        var bytes = MauiPortablePackage.Build("DateList", items, DateTimeOffset.UtcNow);
        var fileName = ids.Count == 1 ? PortabilitySupport.BuildSafeFileName(firstName, firstId, "Date List") : MauiPortablePackage.BuildBulkFileName("DateList", ids.Count);
        return new ResourceExportFile(fileName, bytes);
    }

    public async Task<ResourceImportPreflight> PreflightDateListImportAsync(byte[] bytes, string mode, CancellationToken ct = default)
    {
        var session = RequireSession();
        var packages = ParseDateListPackages(bytes);
        var normalizedMode = NormalizeMode(mode);
        var inventory = (await _dateLists.GetSnapshotAsync(true, ct)).DateLists;
        var targets = inventory.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => new ResourceImportTarget(item.Id, item.Name, "Date List", item.CanEditDelete)).ToList();
        var usedNames = new HashSet<string>(inventory.Select(item => item.Name), StringComparer.OrdinalIgnoreCase);
        var isMulti = packages.Count > 1;
        var plans = new List<ResourceImportItemPreflight>(packages.Count);

        var plannedOverwriteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packages)
        {
            var available = !usedNames.Contains(package.Name);
            var suggested = package.Name;
            var action = normalizedMode == "overwrite" ? "overwrite" : "create";
            var plannedTarget = string.Empty;
            var message = string.Empty;
            if (normalizedMode == "overwrite")
            {
                var target = targets.FirstOrDefault(item => item.Name.Equals(package.Name, StringComparison.OrdinalIgnoreCase));
                if (target is null)
                {
                    available = false;
                    message = $"Date List '{package.Name}' does not exist. Update existing Date Lists requires an existing Date List with the same name as the export file.";
                }
                else if (!plannedOverwriteNames.Add(package.Name))
                {
                    available = false;
                    message = $"The package contains more than one Date List named '{package.Name}'. Each exported Date List name must be unique for Update existing Date Lists.";
                }
                else
                {
                    plannedTarget = target.Id;
                    available = true;
                    if (!target.CanEdit) message = $"MOVEit reports that the current user cannot edit Date List '{target.Name}'.";
                }
            }
            else
            {
                if (!available) message = $"Date List '{package.Name}' already exists. Choose Update existing Date Lists to update the same-name Date List.";
                else usedNames.Add(package.Name);
            }
            plans.Add(new ResourceImportItemPreflight(package.Id, package.Name, "Date List", suggested, available, action, plannedTarget,
                await _portableMetadata.ValidateAsync(session.GetEnvironmentKey(), package.Metadata, "DateList", ct), message));
        }

        return BuildPreflight(packages[0].Id, packages[0].Name, "Date List", packages[0].ExportedUtc, targets, plans, packages.Count);
    }

    public async Task<ResourceImportResult> ImportDateListAsync(byte[] bytes, string mode, bool ackMetadata, string username, CancellationToken ct = default)
    {
        var packages = ParseDateListPackages(bytes);
        var normalizedMode = NormalizeMode(mode);
        var preflight = await PreflightDateListImportAsync(bytes, normalizedMode, ct);
        ValidateMetadataAck(preflight, ackMetadata, "Date List");
        if (!string.IsNullOrWhiteSpace(preflight.Message)) throw new InvalidOperationException(preflight.Message);

        if (packages.Count == 1)
        {
            var plan = preflight.Items[0];
            var item = await ImportOneDateListAsync(packages[0], plan.PlannedAction, plan.SuggestedName, plan.TargetId, username, plan.Metadata, ct);
            return BuildResult(new[] { item });
        }

        var results = new List<ResourceImportItemResult>(packages.Count);
        for (var i = 0; i < packages.Count; i++)
        {
            var package = packages[i];
            var plan = preflight.Items[i];
            try
            {
                results.Add(await ImportOneDateListAsync(package, plan.PlannedAction, plan.SuggestedName,
                    plan.PlannedAction == "overwrite" ? plan.TargetId : string.Empty, username, plan.Metadata, ct));
            }
            catch (OperationCanceledException) { throw; }
            catch (MoveItAuthenticationException) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bulk Date List import failed for exported Date List {DateListName}.", package.Name);
                results.Add(new ResourceImportItemResult(package.Name, string.Empty, plan.SuggestedName, false, plan.PlannedAction == "create", string.Empty, ex.Message));
            }
        }
        return BuildResult(results);
    }

    private async Task<ResourceImportItemResult> ImportOneDateListAsync(DateListPackage package, string mode, string name, string targetId,
        string username, MetadataImportSummary metadataSummary, CancellationToken ct)
    {
        var session = RequireSession();
        var env = session.GetEnvironmentKey();
        var warning = MetadataIgnoredWarning(metadataSummary);
        if (mode.Equals("create", StringComparison.OrdinalIgnoreCase))
        {
            MoveItCreatedDateList? shell = null;
            try
            {
                shell = await _dateLists.CreateAsync(name, string.Empty, ct);
                var restored = await _dateLists.RestoreConfigurationAsync(shell.Id, RewriteIdentity(package.Root, shell.Id, name).ToString(SaveOptions.DisableFormatting), ct);
                warning = AppendWarning(warning, await RecordCreatedAsync(env, "DateList", restored.Id, username, ct));
                warning = await ApplyMetadataAsync(env, "DateList", restored.Id, package.Metadata, username, warning, ct);
                return new ResourceImportItemResult(package.Name, restored.Id, restored.Name, true, true, warning, string.Empty);
            }
            catch (Exception ex) when (shell is not null && ex is not OperationCanceledException)
            {
                try { await _dateLists.DeleteAsync(new[] { shell!.Id }, ct); }
                catch (Exception cleanup) when (cleanup is not OperationCanceledException)
                { _logger.LogError(cleanup, "Unable to remove temporary Date List {DateListId} after import failure.", shell!.Id); throw new InvalidOperationException($"The Date List import failed and MAUI could not remove temporary Date List ID {shell!.Id}. Original error: {ex.Message}", ex); }
                throw;
            }
        }

        var id = NormalizeId(targetId, "Date List");
        var live = (await _dateLists.GetSnapshotAsync(true, ct)).DateLists;
        if (live.Any(item => !item.Id.Equals(id, StringComparison.OrdinalIgnoreCase) && item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new DateListNameAlreadyExistsException(name);
        var target = live.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException("The same-name destination Date List no longer exists or is not visible.");
        if (!target.CanEditDelete) throw new InvalidOperationException($"MOVEit reports that Date List '{target.Name}' cannot be edited.");
        await _dateListUndo.CaptureAsync(id, username, ct);
        var restoredTarget = await _dateLists.RestoreConfigurationAsync(id, RewriteIdentity(package.Root, id, name).ToString(SaveOptions.DisableFormatting), ct);
        warning = AppendWarning(warning, await RecordModifiedAsync(env, "DateList", restoredTarget.Id, username, ct));
        warning = await ApplyMetadataAsync(env, "DateList", restoredTarget.Id, package.Metadata, username, warning, ct);
        return new ResourceImportItemResult(package.Name, restoredTarget.Id, restoredTarget.Name, true, false, warning, string.Empty);
    }

    private ResourceImportPreflight BuildPreflight(string sourceId, string sourceName, string type, DateTimeOffset? exportedUtc,
        IReadOnlyList<ResourceImportTarget> targets, IReadOnlyList<ResourceImportItemPreflight> plans, int itemCount)
    {
        var first = plans[0];
        var aggregate = AggregateMetadata(plans.Select(item => (item.SourceName, item.Metadata)));
        var blockers = plans.Count(item => !string.IsNullOrWhiteSpace(item.Message));
        var message = itemCount > 1 && blockers > 0 ? $"{blockers} of {itemCount} items cannot be imported. Review the item validation details." : first.Message;
        return new ResourceImportPreflight(true, sourceId, sourceName, type, exportedUtc, first.SuggestedName, first.SuggestedNameAvailable,
            targets, aggregate, message, itemCount, itemCount > 1, plans.Count(item => item.PlannedAction == "create" && string.IsNullOrWhiteSpace(item.Message)), plans.Count(item => item.PlannedAction == "overwrite" && string.IsNullOrWhiteSpace(item.Message)), plans);
    }

    private static ResourceImportResult BuildResult(IReadOnlyList<ResourceImportItemResult> results)
    {
        var successes = results.Where(item => item.Success).ToList();
        var first = successes.FirstOrDefault() ?? results.FirstOrDefault();
        var warning = string.Join(" ", successes.Select(item => item.Warning).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase));
        return new ResourceImportResult(first?.Id ?? string.Empty, first?.Name ?? string.Empty, first?.Created ?? false, warning,
            results.Count, successes.Count, successes.Count(item => item.Created), successes.Count(item => !item.Created), results.Count(item => !item.Success), results);
    }

    private IReadOnlyList<ScriptPackage> ParseScriptPackages(byte[] bytes)
    {
        var package = MauiPortablePackage.Parse(bytes, "Script", "Script");
        var parsed = new List<ScriptPackage>(package.Items.Count);
        foreach (var item in package.Items)
        {
            if (!item.Configuration.Name.LocalName.Equals("Script", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"MAUI package item '{item.Name}' does not contain a MOVEit Script configuration.");
            var name = ((string?)item.Configuration.Attribute("Name") ?? string.Empty).Trim();
            var language = ((string?)item.Configuration.Attribute("Lang") ?? item.Type).Trim();
            if (!name.Equals(item.Name, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"MAUI package item '{item.Name}' does not match the Script name stored in its configuration.");
            if (language.Length == 0) throw new InvalidOperationException($"MAUI package item '{item.Name}' does not contain a Script language.");
            if (item.Type.Length > 0 && !item.Type.Equals(language, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"MAUI package item '{item.Name}' has an inconsistent Script language.");
            var id = item.SourceId.Length > 0 ? item.SourceId : ((string?)item.Configuration.Attribute("ID") ?? string.Empty).Trim();
            parsed.Add(new ScriptPackage(id, item.Name, language, package.ExportedUtc, item.Configuration, _portableMetadata.ParseFromEnvelope(item.Envelope, "Script")));
        }
        return parsed;
    }

    private IReadOnlyList<DateListPackage> ParseDateListPackages(byte[] bytes)
    {
        var package = MauiPortablePackage.Parse(bytes, "DateList", "Date List");
        var parsed = new List<DateListPackage>(package.Items.Count);
        foreach (var item in package.Items)
        {
            if (!item.Configuration.Name.LocalName.Equals("DateList", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"MAUI package item '{item.Name}' does not contain a MOVEit Date List configuration.");
            var name = ((string?)item.Configuration.Attribute("Name") ?? string.Empty).Trim();
            if (!name.Equals(item.Name, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"MAUI package item '{item.Name}' does not match the Date List name stored in its configuration.");
            var id = item.SourceId.Length > 0 ? item.SourceId : ((string?)item.Configuration.Attribute("ID") ?? string.Empty).Trim();
            parsed.Add(new DateListPackage(id, item.Name, package.ExportedUtc, item.Configuration, _portableMetadata.ParseFromEnvelope(item.Envelope, "DateList")));
        }
        return parsed;
    }

    private static XElement ParseConfiguration(string xml, string expectedRoot, string label)
    {
        try
        {
            var value = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
            if (!value.Name.LocalName.Equals(expectedRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"The configuration object is not a MOVEit {label}.");
            return value;
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex) { throw new InvalidOperationException($"The {label} configuration contains invalid XML.", ex); }
    }

    private static XElement RewriteIdentity(XElement source, string id, string name)
    {
        var value = new XElement(source);
        value.SetAttributeValue("ID", id);
        value.SetAttributeValue("Name", name);
        return value;
    }

    private static string NormalizeMode(string? mode)
    {
        var value = (mode ?? "create").Trim().ToLowerInvariant();
        return value is "create" or "overwrite" ? value : throw new InvalidOperationException("Import mode must be Create or Overwrite.");
    }

    private static string NormalizeName(string? name, string label) => PortabilitySupport.NormalizeName(name, label);
    private static string NormalizeId(string? id, string label) => PortabilitySupport.NormalizeId(id, label);
    private static string FriendlyLanguage(string? language) => (language ?? string.Empty).Equals("PS1", StringComparison.OrdinalIgnoreCase) ? "PowerShell" : (language ?? string.Empty).Equals("VBScript", StringComparison.OrdinalIgnoreCase) ? "VBScript" : (language ?? string.Empty).Trim();

    private static void ValidateMetadataAck(ResourceImportPreflight preflight, bool acknowledgement, string label)
    {
        if (preflight.Metadata.RequiresAcknowledgement && !acknowledgement)
            throw new InvalidOperationException($"The export contains MAUI custom metadata fields that are not compatible with the current {label} metadata definition. Review the warnings and explicitly acknowledge that unsupported metadata will be ignored before importing.");
    }

    private static string MetadataIgnoredWarning(MetadataImportSummary summary) => summary.Issues.Count > 0 ? $"{summary.Issues.Count} unsupported custom metadata field(s) were ignored during import." : string.Empty;

    private async Task<string> ApplyMetadataAsync(string env, string type, string id, PortableMetadataPackage package, string user, string warning, CancellationToken ct)
    {
        try { await _portableMetadata.ApplyCompatibleAsync(env, type, id, package, user, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { _logger.LogError(ex, "Imported {Type} {Id} succeeded but custom metadata could not be saved.", type, id); warning = AppendWarning(warning, $"The {FriendlyObject(type)} was imported, but its MAUI custom metadata could not be saved."); }
        return warning;
    }

    private async Task<string> RecordCreatedAsync(string env, string type, string id, string user, CancellationToken ct)
    {
        try { await _metadata.RecordObjectCreatedAsync(env, type, id, user, ct); return string.Empty; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { _logger.LogError(ex, "Imported {Type} {Id} creation audit failed.", type, id); return $"The {FriendlyObject(type)} was imported, but MAUI could not fully update its local audit metadata."; }
    }

    private async Task<string> RecordModifiedAsync(string env, string type, string id, string user, CancellationToken ct)
    {
        try { await _metadata.RecordObjectConfigurationModifiedAsync(env, type, id, user, ct); return string.Empty; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { _logger.LogError(ex, "Imported {Type} {Id} modification audit failed.", type, id); return $"The {FriendlyObject(type)} was overwritten and an Undo version was saved, but MAUI could not fully update its local audit metadata."; }
    }

    private static string FriendlyObject(string type) => type.Equals("DateList", StringComparison.OrdinalIgnoreCase) ? "Date List" : type;
    private static string AppendWarning(string first, string second) => PortabilitySupport.AppendWarning(first, second);

    private static MetadataImportSummary AggregateMetadata(IEnumerable<(string Name, MetadataImportSummary Summary)> items)
    {
        var list = items.ToList();
        var issues = list.SelectMany(item => item.Summary.Issues.Select(issue => $"{item.Name}: {issue}")).ToList();
        return new MetadataImportSummary(MauiMetadataPortabilityService.SchemaVersion, list.Sum(item => item.Summary.ExportedFields),
            list.Sum(item => item.Summary.CompatibleFields), issues, issues.Count > 0);
    }

    private static IReadOnlyList<string> NormalizeIds(IEnumerable<string>? ids, string label)
    {
        var normalized = (ids ?? Array.Empty<string>()).Select(value => (value ?? string.Empty).Trim()).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (normalized.Count == 0) throw new InvalidOperationException($"Select at least one {label} to export.");
        if (normalized.Count > MauiPortablePackage.MaxItems) throw new InvalidOperationException($"A MAUI export can contain at most {MauiPortablePackage.MaxItems} {label} items.");
        return normalized;
    }

    private MoveItSessionState RequireSession() => _session.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
    private sealed record ScriptPackage(string Id, string Name, string Language, DateTimeOffset? ExportedUtc, XElement Root, PortableMetadataPackage Metadata);
    private sealed record DateListPackage(string Id, string Name, DateTimeOffset? ExportedUtc, XElement Root, PortableMetadataPackage Metadata);
}

