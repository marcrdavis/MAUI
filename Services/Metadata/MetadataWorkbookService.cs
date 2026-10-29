// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Canonical XLSX import/export for bulk MAUI metadata maintenance.

using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Services.Metadata;

public interface IMetadataWorkbookService
{
    Task<MetadataWorkbookExport> ExportAsync(MoveItSessionState session, string objectType, CancellationToken cancellationToken = default);
    Task<MetadataImportPreview> PreviewImportAsync(MoveItSessionState session, byte[] workbookBytes, string fileName, CancellationToken cancellationToken = default);
    Task<MetadataImportApplyResult> ApplyImportAsync(MoveItSessionState session, byte[] workbookBytes, string fileName, CancellationToken cancellationToken = default);
}

public sealed record MetadataWorkbookExport(byte[] Content, string FileName, string ObjectType, int ObjectCount, int FieldCount);
public sealed record MetadataImportIssue(int? RowNumber, string Message);
public sealed record MetadataImportChange(
    int RowNumber,
    string ObjectId,
    string ObjectName,
    string FieldName,
    string FieldLabel,
    string CurrentValue,
    string NewValue,
    bool ClearsValue);

public sealed record MetadataImportPreview(
    string FileName,
    string ObjectType,
    string ObjectTypeLabel,
    string EnvironmentKey,
    int RowsRead,
    int MatchedRows,
    int UnchangedRows,
    int ObjectsWithChanges,
    int ChangeCount,
    IReadOnlyList<MetadataImportChange> Changes,
    bool ChangesTruncated,
    IReadOnlyList<MetadataImportIssue> Errors,
    IReadOnlyList<MetadataImportIssue> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}

public sealed record MetadataImportApplyResult(string ObjectType, int ObjectsChanged, int ChangesApplied, string UndoSnapshotId);

public sealed class MetadataWorkbookService : IMetadataWorkbookService
{
    private const string WorkbookFormat = "MAUI-Metadata-Workbook";
    private const int WorkbookSchemaVersion = 1;
    private const string VisibleSheetName = "Metadata";
    private const string MetadataSheetName = "_MAUI";
    private const string ObjectIdColumn = "__ObjectId";
    private const string ObjectNameColumn = "__ObjectName";
    private const string ClearToken = "<CLEAR>";
    private const string BulkUndoObjectType = "MetadataImport";
    private const string BulkUndoObjectId = "Bulk";
    private const int MaxWorkbookBytes = 10 * 1024 * 1024;
    private const int MaxWorkbookRows = 25000;
    private const int MaxPreviewChanges = 1000;
    private const int MaxPreviewIssues = 500;

    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IAutomationTaskService _tasks;
    private readonly IMoveItObjectAccessService? _access;
    private readonly IAutomationHostService _hosts;
    private readonly IAutomationScriptService _scripts;
    private readonly IAutomationDateListService _dateLists;
    private readonly IAutomationKeyCertificateService _keysAndCertificates;
    private readonly IAutomationGlobalParameterService _globalParameters;
    private readonly IMetadataRepository _metadata;
    private readonly ITagRepository _tags;
    private readonly IConfigurationUndoRepository _undoRepository;
    private readonly MetadataUiOptions _options;
    private readonly UiOptions _uiOptions;
    private readonly ILogger<MetadataWorkbookService> _logger;

    public MetadataWorkbookService(
        IAutomationTaskService tasks,
        IAutomationHostService hosts,
        IAutomationScriptService scripts,
        IAutomationDateListService dateLists,
        IAutomationKeyCertificateService keysAndCertificates,
        IAutomationGlobalParameterService globalParameters,
        IMetadataRepository metadata,
        ITagRepository tags,
        IConfigurationUndoRepository undoRepository,
        IOptionsSnapshot<MetadataUiOptions> options,
        IOptionsSnapshot<UiOptions> uiOptions,
        ILogger<MetadataWorkbookService> logger, IMoveItObjectAccessService? access = null)
    {
        _tasks = tasks;
        _access = access;
        _hosts = hosts;
        _scripts = scripts;
        _dateLists = dateLists;
        _keysAndCertificates = keysAndCertificates;
        _globalParameters = globalParameters;
        _metadata = metadata;
        _tags = tags;
        _undoRepository = undoRepository;
        _options = options.Value;
        _uiOptions = uiOptions.Value;
        _logger = logger;
    }

    // Exports the current authoritative object inventory and MAUI metadata into the canonical workbook format.
    public async Task<MetadataWorkbookExport> ExportAsync(MoveItSessionState session, string objectType, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var type = ResolveObjectType(objectType);
        var fields = GetConfiguredFields(type);
        if (fields.Count == 0) throw new InvalidOperationException($"No custom metadata fields are configured for {type.Label}.");

        var objects = await LoadObjectsAsync(type, forceRefresh: true, cancellationToken);
        if (_access is not null)
            foreach (var item in objects) await _access.RequireAsync(type.Singular, item.Id, "Edit", cancellationToken);
        var environmentKey = session.GetEnvironmentKey();
        var metadata = await _metadata.GetObjectMetadataBatchAsync(environmentKey, type.Singular, objects.Select(item => item.Id), cancellationToken);
        var exportedUtc = DateTimeOffset.UtcNow;
        var bytes = BuildWorkbook(type, environmentKey, exportedUtc, objects, fields, metadata);
        var fileName = $"MAUI_{type.Label}_Metadata_{exportedUtc:yyyyMMdd_HHmmss}.xlsx";

        _logger.LogInformation("Exported MAUI {ObjectType} metadata workbook for {EnvironmentKey}: {ObjectCount} objects, {FieldCount} fields.",
            type.Singular, environmentKey, objects.Count, fields.Count);

        return new MetadataWorkbookExport(bytes, fileName, type.Singular, objects.Count, fields.Count);
    }

    // Parses and fully validates a workbook without modifying MAUI metadata.
    public async Task<MetadataImportPreview> PreviewImportAsync(MoveItSessionState session, byte[] workbookBytes, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var plan = await BuildImportPlanAsync(session, workbookBytes, fileName, cancellationToken);
        return ToPreview(plan);
    }

    // Revalidates the uploaded workbook, captures one bulk undo snapshot, then applies the complete change set in one DB save.
    public async Task<MetadataImportApplyResult> ApplyImportAsync(MoveItSessionState session, byte[] workbookBytes, string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var plan = await BuildImportPlanAsync(session, workbookBytes, fileName, cancellationToken);
        if (plan.Errors.Count > 0)
        {
            var first = plan.Errors[0];
            var suffix = plan.Errors.Count > 1 ? $" ({plan.Errors.Count} validation errors total.)" : string.Empty;
            throw new InvalidOperationException($"{first.Message}{suffix}");
        }
        if (plan.Changes.Count == 0)
            return new MetadataImportApplyResult(plan.Type.Singular, 0, 0, string.Empty);

        if (_access is not null)
        {
            var creation = await _access.GetCreationAsync(true, cancellationToken);
            foreach (var id in plan.Changes.Select(change => change.ObjectId).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var creationType = plan.Type.Singular == "KeyCertificate" ? id.Split('|', 2)[0].ToUpperInvariant() : plan.Type.Singular;
                if (!creation.CanCreate(creationType)) throw new MoveItObjectAccessException("Your MOVEit account cannot import this object type.");
                await _access.RequireAsync(plan.Type.Singular, id, "Edit", cancellationToken);
            }
        }

        var oldValuesByObject = plan.Changes
            .GroupBy(change => change.ObjectId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyDictionary<string, string>)group.ToDictionary(change => change.FieldName, change => change.CurrentValue, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        var undoPayload = new MetadataImportUndoPayload
        {
            Format = "maui-metadata-import-undo-v1",
            OperationId = Guid.NewGuid().ToString("N"),
            ObjectType = plan.Type.Singular,
            FileName = plan.FileName,
            Values = oldValuesByObject.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase)
        };

        var undo = await _undoRepository.SaveSnapshotAsync(
            session.GetEnvironmentKey(),
            BulkUndoObjectType,
            BulkUndoObjectId,
            $"{plan.Type.Singular} Metadata Import",
            session.Username,
            JsonSerializer.Serialize(undoPayload, JsonOptions),
            cancellationToken);

        var newValuesByObject = plan.Changes
            .GroupBy(change => change.ObjectId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyDictionary<string, string>)group.ToDictionary(change => change.FieldName, change => change.NewValue, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        await _metadata.SaveObjectMetadataBatchAsync(
            session.GetEnvironmentKey(),
            plan.Type.Singular,
            newValuesByObject,
            session.Username,
            cancellationToken);

        _logger.LogInformation(
            "Imported MAUI {ObjectType} metadata from {FileName} for {EnvironmentKey}: {ObjectCount} objects changed, {ChangeCount} field changes. Undo snapshot {UndoSnapshotId}.",
            plan.Type.Singular,
            plan.FileName,
            session.GetEnvironmentKey(),
            newValuesByObject.Count,
            plan.Changes.Count,
            undo.SnapshotId);

        return new MetadataImportApplyResult(plan.Type.Singular, newValuesByObject.Count, plan.Changes.Count, undo.SnapshotId);
    }

    private async Task<MetadataImportPlan> BuildImportPlanAsync(MoveItSessionState session, byte[] workbookBytes, string fileName, CancellationToken cancellationToken)
    {
        ValidateWorkbookBytes(workbookBytes, fileName);
        var parsed = ParseWorkbook(workbookBytes);
        var errors = new List<MetadataImportIssue>();
        var warnings = new List<MetadataImportIssue>();

        if (!parsed.Metadata.TryGetValue("Format", out var format) || !format.Equals(WorkbookFormat, StringComparison.Ordinal))
            errors.Add(new MetadataImportIssue(null, "The workbook is not a recognized MAUI metadata workbook."));

        if (!parsed.Metadata.TryGetValue("SchemaVersion", out var schemaValue)
            || !int.TryParse(schemaValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var schemaVersion)
            || schemaVersion != WorkbookSchemaVersion)
            errors.Add(new MetadataImportIssue(null, $"The workbook metadata schema is not supported. Expected version {WorkbookSchemaVersion}."));

        parsed.Metadata.TryGetValue("ObjectType", out var importedObjectType);
        MetadataObjectType type;
        try
        {
            type = ResolveObjectType(importedObjectType ?? string.Empty);
        }
        catch (InvalidOperationException ex)
        {
            errors.Add(new MetadataImportIssue(null, ex.Message));
            type = ResolveObjectType("Task");
        }

        var environmentKey = session.GetEnvironmentKey();
        parsed.Metadata.TryGetValue("EnvironmentKey", out var importedEnvironment);
        var importedEnvironmentKey = string.Empty;
        if (!string.IsNullOrWhiteSpace(importedEnvironment))
        {
            try
            {
                importedEnvironmentKey = MoveItEnvironmentKey.Normalize(importedEnvironment);
            }
            catch (ArgumentException)
            {
                importedEnvironmentKey = string.Empty;
            }
        }
        if (importedEnvironmentKey.Length == 0
            || !importedEnvironmentKey.Equals(environmentKey, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new MetadataImportIssue(null,
                $"This workbook belongs to MOVEit environment '{(string.IsNullOrWhiteSpace(importedEnvironment) ? "(missing)" : importedEnvironment)}', not the current environment '{environmentKey}'."));
        }

        var configuredFields = GetConfiguredFields(type);
        var configuredByName = configuredFields.ToDictionary(field => field.Name, StringComparer.OrdinalIgnoreCase);
        if (configuredFields.Count == 0)
            errors.Add(new MetadataImportIssue(null, $"No custom metadata fields are currently configured for {type.Label}."));

        var fieldHeaders = parsed.Headers
            .Where(header => !header.Equals(ObjectIdColumn, StringComparison.OrdinalIgnoreCase)
                             && !header.Equals(ObjectNameColumn, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var duplicate in parsed.Headers.GroupBy(header => header, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
            errors.Add(new MetadataImportIssue(null, $"Workbook column '{duplicate.Key}' appears more than once."));

        if (!parsed.Headers.Contains(ObjectIdColumn, StringComparer.OrdinalIgnoreCase))
            errors.Add(new MetadataImportIssue(null, "The workbook is missing its MOVEit ID identity column."));
        if (!parsed.Headers.Contains(ObjectNameColumn, StringComparer.OrdinalIgnoreCase))
            errors.Add(new MetadataImportIssue(null, "The workbook is missing its object Name identity column."));

        foreach (var header in fieldHeaders)
        {
            if (!configuredByName.ContainsKey(header))
                errors.Add(new MetadataImportIssue(null, $"Metadata field '{header}' is not configured for {type.Label} in this MAUI instance."));
        }

        foreach (var exportedField in parsed.FieldDefinitions)
        {
            // A user may intentionally remove a metadata column before import. Only validate
            // the exported definition for columns that are still present in the visible import sheet.
            if (!fieldHeaders.Contains(exportedField.Name, StringComparer.OrdinalIgnoreCase)) continue;
            if (!configuredByName.TryGetValue(exportedField.Name, out var current)) continue;
            var currentType = NormalizeFieldType(current.Type);
            if (!currentType.Equals(NormalizeFieldType(exportedField.Type), StringComparison.OrdinalIgnoreCase))
                errors.Add(new MetadataImportIssue(null,
                    $"Metadata field '{exportedField.Name}' was exported as type '{exportedField.Type}' but is currently configured as '{currentType}'."));
        }

        if (parsed.Rows.Count > MaxWorkbookRows)
            errors.Add(new MetadataImportIssue(null, $"The workbook contains {parsed.Rows.Count:N0} data rows. The maximum supported import is {MaxWorkbookRows:N0} rows."));

        if (errors.Count > 0)
            return new MetadataImportPlan(fileName, type, environmentKey, parsed.Rows.Count, 0, 0, errors, warnings, new List<MetadataImportChange>());

        var objects = await LoadObjectsAsync(type, forceRefresh: true, cancellationToken);
        var objectsById = objects.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var objectsByName = objects
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        var currentMetadata = await _metadata.GetObjectMetadataBatchAsync(environmentKey, type.Singular, objects.Select(item => item.Id), cancellationToken);

        var matchedRows = 0;
        var unchangedRows = 0;
        var seenObjectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changes = new List<MetadataImportChange>();

        foreach (var row in parsed.Rows)
        {
            if (row.Values.Values.All(string.IsNullOrWhiteSpace)) continue;

            var importedId = GetValue(row.Values, ObjectIdColumn).Trim();
            var importedName = GetValue(row.Values, ObjectNameColumn).Trim();
            MetadataWorkbookObject? target = null;

            if (importedId.Length > 0)
            {
                if (!objectsById.TryGetValue(importedId, out target))
                {
                    AddIssue(errors, new MetadataImportIssue(row.RowNumber, $"MOVEit ID '{importedId}' was not found in the current {type.Label} inventory."));
                    continue;
                }
            }
            else if (importedName.Length > 0)
            {
                if (!objectsByName.TryGetValue(importedName, out var matches) || matches.Count == 0)
                {
                    AddIssue(errors, new MetadataImportIssue(row.RowNumber, $"{type.Singular} '{importedName}' was not found."));
                    continue;
                }
                if (matches.Count > 1)
                {
                    AddIssue(errors, new MetadataImportIssue(row.RowNumber, $"{type.Singular} name '{importedName}' is ambiguous. Restore its MOVEit ID before importing."));
                    continue;
                }
                target = matches[0];
            }
            else
            {
                AddIssue(errors, new MetadataImportIssue(row.RowNumber, "The row does not contain a MOVEit ID or Name."));
                continue;
            }

            if (!seenObjectIds.Add(target.Id))
            {
                AddIssue(errors, new MetadataImportIssue(row.RowNumber, $"{type.Singular} '{target.Name}' appears more than once in the workbook."));
                continue;
            }

            matchedRows++;
            if (importedName.Length > 0 && !importedName.Equals(target.Name, StringComparison.Ordinal))
                AddIssue(warnings, new MetadataImportIssue(row.RowNumber,
                    $"Workbook name '{importedName}' no longer matches MOVEit ID {target.Id}; current name '{target.Name}' will be used."));

            currentMetadata.TryGetValue(target.Id, out var snapshot);
            var rowChanged = false;

            foreach (var field in configuredFields)
            {
                if (!row.Values.TryGetValue(field.Name, out var importedRaw)) continue;
                if (string.IsNullOrWhiteSpace(importedRaw)) continue; // Blank intentionally means no change.

                var clearRequested = importedRaw.Trim().Equals(ClearToken, StringComparison.OrdinalIgnoreCase);
                if (clearRequested && field.Required)
                {
                    AddIssue(errors, new MetadataImportIssue(row.RowNumber, $"Metadata field '{DisplayLabel(field)}' is required and cannot be cleared."));
                    continue;
                }

                string newValue;
                if (clearRequested)
                {
                    newValue = string.Empty;
                }
                else if (field.Type.Equals("Tags", StringComparison.OrdinalIgnoreCase))
                {
                    try { newValue = await _tags.NormalizeAssignmentAsync(environmentKey, importedRaw, cancellationToken); }
                    catch (ArgumentException ex)
                    {
                        AddIssue(errors, new MetadataImportIssue(row.RowNumber, $"Metadata field '{DisplayLabel(field)}' contains an invalid Tag assignment: {ex.Message}"));
                        continue;
                    }
                }
                else if (!TryNormalizeValue(field, importedRaw, out newValue, out var validationMessage))
                {
                    AddIssue(errors, new MetadataImportIssue(row.RowNumber, validationMessage));
                    continue;
                }

                var currentValue = snapshot is not null && snapshot.Values.TryGetValue(field.Name, out var existingValue)
                    ? existingValue ?? string.Empty
                    : string.Empty;
                if (string.Equals(currentValue, newValue, StringComparison.Ordinal)) continue;

                rowChanged = true;
                changes.Add(new MetadataImportChange(
                    row.RowNumber,
                    target.Id,
                    target.Name,
                    field.Name,
                    DisplayLabel(field),
                    currentValue,
                    newValue,
                    clearRequested));
            }

            if (!rowChanged) unchangedRows++;
        }

        return new MetadataImportPlan(fileName, type, environmentKey, parsed.Rows.Count, matchedRows, unchangedRows, errors, warnings, changes);
    }

    private MetadataImportPreview ToPreview(MetadataImportPlan plan)
    {
        var changes = plan.Changes.Take(MaxPreviewChanges).ToArray();
        return new MetadataImportPreview(
            plan.FileName,
            plan.Type.Singular,
            plan.Type.Label,
            plan.EnvironmentKey,
            plan.RowsRead,
            plan.MatchedRows,
            plan.UnchangedRows,
            plan.Changes.Select(item => item.ObjectId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            plan.Changes.Count,
            changes,
            plan.Changes.Count > changes.Length,
            plan.Errors.Take(MaxPreviewIssues).ToArray(),
            plan.Warnings.Take(MaxPreviewIssues).ToArray());
    }

    private static void AddIssue(ICollection<MetadataImportIssue> collection, MetadataImportIssue issue)
    {
        if (collection.Count < MaxPreviewIssues) collection.Add(issue);
    }

    private static string GetValue(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;

    private static bool TryNormalizeValue(MetadataFieldDefinition field, string importedRaw, out string normalized, out string message)
    {
        normalized = importedRaw;
        message = string.Empty;
        var type = NormalizeFieldType(field.Type);
        var label = DisplayLabel(field);

        if (type.Equals("Email", StringComparison.OrdinalIgnoreCase))
        {
            if (MetadataValueValidation.TryNormalizeEmailList(importedRaw, out normalized)) return true;
            message = $"Metadata field '{label}' must contain valid email address(es) separated by commas or semicolons.";
            return false;
        }

        if (type.Equals("List", StringComparison.OrdinalIgnoreCase))
        {
            var value = importedRaw.Trim();
            var match = (field.PossibleValues ?? new List<string>()).FirstOrDefault(item => item.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                message = $"Metadata field '{label}' contains '{value}', which is not an allowed value.";
                return false;
            }
            normalized = match;
            return true;
        }

        if (type.Equals("Boolean", StringComparison.OrdinalIgnoreCase))
        {
            var value = importedRaw.Trim();
            if (value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase) || value == "1")
            {
                normalized = "true";
                return true;
            }
            if (value.Equals("false", StringComparison.OrdinalIgnoreCase) || value.Equals("no", StringComparison.OrdinalIgnoreCase) || value == "0")
            {
                normalized = "false";
                return true;
            }
            message = $"Metadata field '{label}' must contain True or False.";
            return false;
        }

        if (type.Equals("Numeric", StringComparison.OrdinalIgnoreCase))
        {
            var value = importedRaw.Trim();
            if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                && !decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out number))
            {
                message = $"Metadata field '{label}' must contain a numeric value.";
                return false;
            }
            normalized = number.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (type.Equals("Date", StringComparison.OrdinalIgnoreCase))
        {
            var value = importedRaw.Trim();
            if (DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date)
                || DateOnly.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out date))
            {
                normalized = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                return true;
            }

            // Excel can persist a pasted/date-formatted value as its numeric serial even when
            // the MAUI template originally formatted the column as text. Accept a valid Excel date serial.
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)
                && serial >= 1 && serial <= 2958465)
            {
                try
                {
                    normalized = DateOnly.FromDateTime(DateTime.FromOADate(serial)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    return true;
                }
                catch (ArgumentException) { /* Fall through to the validation message. */ }
            }

            message = $"Metadata field '{label}' must contain a valid date.";
            return false;
        }

        normalized = importedRaw;
        return true;
    }

    private async Task<IReadOnlyList<MetadataWorkbookObject>> LoadObjectsAsync(MetadataObjectType type, bool forceRefresh, CancellationToken cancellationToken)
    {
        IReadOnlyList<MetadataWorkbookObject> objects = type.Singular switch
        {
            "Task" => (await _tasks.GetTaskSnapshotAsync(forceRefresh, cancellationToken)).Tasks
                .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                .Select(item => new MetadataWorkbookObject(item.Id, item.Name))
                .ToArray(),
            "Host" => (await _hosts.GetHostSnapshotAsync(forceRefresh, cancellationToken)).Hosts
                .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                .Select(item => new MetadataWorkbookObject(item.Id, item.Name))
                .ToArray(),
            "Script" => (await _scripts.GetScriptSnapshotAsync(forceRefresh, cancellationToken)).Scripts
                .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                .Select(item => new MetadataWorkbookObject(item.Id, item.Name))
                .ToArray(),
            "DateList" => (await _dateLists.GetSnapshotAsync(forceRefresh, cancellationToken)).DateLists
                .Where(item => !string.IsNullOrWhiteSpace(item.Id))
                .Select(item => new MetadataWorkbookObject(item.Id, item.Name))
                .ToArray(),
            "KeyCertificate" => (await _keysAndCertificates.GetSnapshotAsync(forceRefresh, cancellationToken)).Items
                .Where(item => !string.IsNullOrWhiteSpace(item.RowId))
                .Select(item => new MetadataWorkbookObject(item.RowId, item.Name))
                .ToArray(),
            "GlobalParameter" => (await _globalParameters.GetSnapshotAsync(forceRefresh, cancellationToken)).Parameters
                .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                .Select(item => new MetadataWorkbookObject(item.Name, item.Name))
                .ToArray(),
            _ => throw new InvalidOperationException($"Metadata workbook object type '{type.Singular}' is not supported.")
        };

        return objects
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlyList<MetadataFieldDefinition> GetConfiguredFields(MetadataObjectType type)
    {
        var definition = MetadataResourceCatalog.GetByObjectType(type.Singular).GetDefinition(_options);
        return MetadataGridSupport.GetConfiguredFields(definition)
            .Select(field => new MetadataFieldDefinition
            {
                Name = field.Name.Trim(),
                Label = string.IsNullOrWhiteSpace(field.Label) ? field.Name.Trim() : field.Label.Trim(),
                Description = field.Description ?? string.Empty,
                Type = NormalizeFieldType(field.Type),
                PossibleValues = (field.PossibleValues ?? new List<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToList(),
                Required = field.Required
            })
            .ToArray();
    }

    private static MetadataObjectType ResolveObjectType(string value)
    {
        var resource = MetadataResourceCatalog.Resolve(value);
        return new MetadataObjectType(resource.ObjectType, resource.Label);
    }

    private static string NormalizeFieldType(string type)
    {
        var normalized = (type ?? string.Empty).Trim();
        if (normalized.Equals("Multiline", StringComparison.OrdinalIgnoreCase)) return "MultilineString";
        if (normalized.Equals("Number", StringComparison.OrdinalIgnoreCase)) return "Numeric";
        return normalized.Length == 0 ? "String" : normalized;
    }

    private static string DisplayLabel(MetadataFieldDefinition field)
        => string.IsNullOrWhiteSpace(field.Label) ? field.Name : field.Label;

    private byte[] BuildWorkbook(
        MetadataObjectType type,
        string environmentKey,
        DateTimeOffset exportedUtc,
        IReadOnlyList<MetadataWorkbookObject> objects,
        IReadOnlyList<MetadataFieldDefinition> fields,
        IReadOnlyDictionary<string, ObjectMetadataSnapshot> metadata)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteXmlEntry(archive, "[Content_Types].xml", BuildContentTypes());
            WriteXmlEntry(archive, "_rels/.rels", BuildRootRelationships());
            WriteXmlEntry(archive, "xl/workbook.xml", BuildWorkbookDefinition());
            WriteXmlEntry(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRelationships());
            WriteXmlEntry(archive, "xl/styles.xml", BuildStyles());
            WriteXmlEntry(archive, "xl/worksheets/sheet1.xml", BuildDataSheet(objects, fields, metadata));
            WriteXmlEntry(archive, "xl/worksheets/sheet2.xml", BuildMetadataSheet(type, environmentKey, exportedUtc, fields));
        }
        return memory.ToArray();
    }

    private static XDocument BuildContentTypes()
        => new(new XElement(ContentTypeNs + "Types",
            new XElement(ContentTypeNs + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(ContentTypeNs + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            new XElement(ContentTypeNs + "Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
            new XElement(ContentTypeNs + "Override", new XAttribute("PartName", "/xl/worksheets/sheet1.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")),
            new XElement(ContentTypeNs + "Override", new XAttribute("PartName", "/xl/worksheets/sheet2.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")),
            new XElement(ContentTypeNs + "Override", new XAttribute("PartName", "/xl/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"))));

    private static XDocument BuildRootRelationships()
        => new(new XElement(PackageRelationshipNs + "Relationships",
            new XElement(PackageRelationshipNs + "Relationship",
                new XAttribute("Id", "rId1"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                new XAttribute("Target", "xl/workbook.xml"))));

    private static XDocument BuildWorkbookDefinition()
        => new(new XElement(SpreadsheetNs + "workbook",
            new XAttribute(XNamespace.Xmlns + "r", RelationshipNs),
            new XElement(SpreadsheetNs + "bookViews", new XElement(SpreadsheetNs + "workbookView")),
            new XElement(SpreadsheetNs + "sheets",
                new XElement(SpreadsheetNs + "sheet", new XAttribute("name", VisibleSheetName), new XAttribute("sheetId", "1"), new XAttribute(RelationshipNs + "id", "rId1")),
                new XElement(SpreadsheetNs + "sheet", new XAttribute("name", MetadataSheetName), new XAttribute("sheetId", "2"), new XAttribute("state", "veryHidden"), new XAttribute(RelationshipNs + "id", "rId2")))));

    private static XDocument BuildWorkbookRelationships()
        => new(new XElement(PackageRelationshipNs + "Relationships",
            new XElement(PackageRelationshipNs + "Relationship", new XAttribute("Id", "rId1"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"), new XAttribute("Target", "worksheets/sheet1.xml")),
            new XElement(PackageRelationshipNs + "Relationship", new XAttribute("Id", "rId2"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"), new XAttribute("Target", "worksheets/sheet2.xml")),
            new XElement(PackageRelationshipNs + "Relationship", new XAttribute("Id", "rId3"), new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"), new XAttribute("Target", "styles.xml"))));

    private static XDocument BuildStyles()
        => new(new XElement(SpreadsheetNs + "styleSheet",
            new XElement(SpreadsheetNs + "fonts", new XAttribute("count", "2"),
                new XElement(SpreadsheetNs + "font", new XElement(SpreadsheetNs + "sz", new XAttribute("val", "11")), new XElement(SpreadsheetNs + "name", new XAttribute("val", "Calibri"))),
                new XElement(SpreadsheetNs + "font", new XElement(SpreadsheetNs + "b"), new XElement(SpreadsheetNs + "sz", new XAttribute("val", "11")), new XElement(SpreadsheetNs + "name", new XAttribute("val", "Calibri")))),
            new XElement(SpreadsheetNs + "fills", new XAttribute("count", "2"),
                new XElement(SpreadsheetNs + "fill", new XElement(SpreadsheetNs + "patternFill", new XAttribute("patternType", "none"))),
                new XElement(SpreadsheetNs + "fill", new XElement(SpreadsheetNs + "patternFill", new XAttribute("patternType", "gray125")))),
            new XElement(SpreadsheetNs + "borders", new XAttribute("count", "1"), new XElement(SpreadsheetNs + "border")),
            new XElement(SpreadsheetNs + "cellStyleXfs", new XAttribute("count", "1"), new XElement(SpreadsheetNs + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "0"), new XAttribute("fillId", "0"), new XAttribute("borderId", "0"))),
            new XElement(SpreadsheetNs + "cellXfs", new XAttribute("count", "3"),
                new XElement(SpreadsheetNs + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "0"), new XAttribute("fillId", "0"), new XAttribute("borderId", "0"), new XAttribute("xfId", "0")),
                new XElement(SpreadsheetNs + "xf", new XAttribute("numFmtId", "49"), new XAttribute("fontId", "0"), new XAttribute("fillId", "0"), new XAttribute("borderId", "0"), new XAttribute("xfId", "0"), new XAttribute("applyNumberFormat", "1")),
                new XElement(SpreadsheetNs + "xf", new XAttribute("numFmtId", "49"), new XAttribute("fontId", "1"), new XAttribute("fillId", "0"), new XAttribute("borderId", "0"), new XAttribute("xfId", "0"), new XAttribute("applyNumberFormat", "1"), new XAttribute("applyFont", "1"))),
            new XElement(SpreadsheetNs + "cellStyles", new XAttribute("count", "1"), new XElement(SpreadsheetNs + "cellStyle", new XAttribute("name", "Normal"), new XAttribute("xfId", "0"), new XAttribute("builtinId", "0")))));

    private static XDocument BuildDataSheet(
        IReadOnlyList<MetadataWorkbookObject> objects,
        IReadOnlyList<MetadataFieldDefinition> fields,
        IReadOnlyDictionary<string, ObjectMetadataSnapshot> metadata)
    {
        var headers = new List<string> { ObjectIdColumn, ObjectNameColumn };
        headers.AddRange(fields.Select(field => field.Name));
        var labels = new List<string> { "MOVEit ID", "Name" };
        labels.AddRange(fields.Select(DisplayLabel));
        var lastColumn = ColumnName(headers.Count);
        var lastRow = Math.Max(2, objects.Count + 2);

        var sheetData = new XElement(SpreadsheetNs + "sheetData");
        var stableHeader = new XElement(SpreadsheetNs + "row", new XAttribute("r", "1"), new XAttribute("hidden", "1"));
        for (var index = 0; index < headers.Count; index++) stableHeader.Add(CreateInlineStringCell(index + 1, 1, headers[index], styleIndex: 1));
        sheetData.Add(stableHeader);

        var visibleHeader = new XElement(SpreadsheetNs + "row", new XAttribute("r", "2"));
        for (var index = 0; index < labels.Count; index++) visibleHeader.Add(CreateInlineStringCell(index + 1, 2, labels[index], styleIndex: 2));
        sheetData.Add(visibleHeader);

        for (var objectIndex = 0; objectIndex < objects.Count; objectIndex++)
        {
            var rowNumber = objectIndex + 3;
            var item = objects[objectIndex];
            metadata.TryGetValue(item.Id, out var snapshot);
            var row = new XElement(SpreadsheetNs + "row", new XAttribute("r", rowNumber.ToString(CultureInfo.InvariantCulture)));
            row.Add(CreateInlineStringCell(1, rowNumber, item.Id, 1));
            row.Add(CreateInlineStringCell(2, rowNumber, item.Name, 1));
            for (var fieldIndex = 0; fieldIndex < fields.Count; fieldIndex++)
            {
                var field = fields[fieldIndex];
                var value = snapshot is not null && snapshot.Values.TryGetValue(field.Name, out var currentValue) ? currentValue ?? string.Empty : string.Empty;
                row.Add(CreateInlineStringCell(fieldIndex + 3, rowNumber, value, 1));
            }
            sheetData.Add(row);
        }

        var columns = new XElement(SpreadsheetNs + "cols",
            new XElement(SpreadsheetNs + "col", new XAttribute("min", "1"), new XAttribute("max", "1"), new XAttribute("width", "16"), new XAttribute("customWidth", "1")),
            new XElement(SpreadsheetNs + "col", new XAttribute("min", "2"), new XAttribute("max", "2"), new XAttribute("width", "34"), new XAttribute("customWidth", "1")));
        if (fields.Count > 0)
            columns.Add(new XElement(SpreadsheetNs + "col", new XAttribute("min", "3"), new XAttribute("max", (fields.Count + 2).ToString(CultureInfo.InvariantCulture)), new XAttribute("width", "24"), new XAttribute("customWidth", "1")));

        return new XDocument(new XElement(SpreadsheetNs + "worksheet",
            new XElement(SpreadsheetNs + "dimension", new XAttribute("ref", $"A1:{lastColumn}{lastRow}")),
            new XElement(SpreadsheetNs + "sheetViews",
                new XElement(SpreadsheetNs + "sheetView", new XAttribute("workbookViewId", "0"),
                    new XElement(SpreadsheetNs + "pane", new XAttribute("ySplit", "2"), new XAttribute("topLeftCell", "A3"), new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")))),
            columns,
            sheetData,
            new XElement(SpreadsheetNs + "autoFilter", new XAttribute("ref", $"A2:{lastColumn}{lastRow}"))));
    }

    private XDocument BuildMetadataSheet(MetadataObjectType type, string environmentKey, DateTimeOffset exportedUtc, IReadOnlyList<MetadataFieldDefinition> fields)
    {
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "Format", WorkbookFormat },
            new[] { "SchemaVersion", WorkbookSchemaVersion.ToString(CultureInfo.InvariantCulture) },
            new[] { "ObjectType", type.Singular },
            new[] { "EnvironmentKey", environmentKey },
            new[] { "ExportedUtc", exportedUtc.ToString("O", CultureInfo.InvariantCulture) },
            new[] { "ApplicationVersion", _uiOptions.Version },
            new[] { "ClearToken", ClearToken },
            Array.Empty<string>(),
            new[] { "FieldName", "FieldType", "Required", "Label" }
        };
        rows.AddRange(fields.Select(field => (IReadOnlyList<string>)new[]
        {
            field.Name,
            NormalizeFieldType(field.Type),
            field.Required ? "true" : "false",
            DisplayLabel(field)
        }));

        var sheetData = new XElement(SpreadsheetNs + "sheetData");
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var rowNumber = rowIndex + 1;
            var row = new XElement(SpreadsheetNs + "row", new XAttribute("r", rowNumber.ToString(CultureInfo.InvariantCulture)));
            for (var colIndex = 0; colIndex < rows[rowIndex].Count; colIndex++)
                row.Add(CreateInlineStringCell(colIndex + 1, rowNumber, rows[rowIndex][colIndex], rowIndex == 8 ? 2 : 1));
            sheetData.Add(row);
        }
        return new XDocument(new XElement(SpreadsheetNs + "worksheet", sheetData));
    }

    private static XElement CreateInlineStringCell(int columnIndex, int rowNumber, string value, int styleIndex)
    {
        var text = new XElement(SpreadsheetNs + "t", value ?? string.Empty);
        if (!string.IsNullOrEmpty(value) && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])))
            text.SetAttributeValue(XNamespace.Xml + "space", "preserve");
        return new XElement(SpreadsheetNs + "c",
            new XAttribute("r", $"{ColumnName(columnIndex)}{rowNumber}"),
            new XAttribute("t", "inlineStr"),
            new XAttribute("s", styleIndex.ToString(CultureInfo.InvariantCulture)),
            new XElement(SpreadsheetNs + "is", text));
    }

    private static void WriteXmlEntry(ZipArchive archive, string path, XDocument document)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        document.Save(writer, SaveOptions.DisableFormatting);
    }

    private static ParsedWorkbook ParseWorkbook(byte[] bytes)
    {
        using var memory = new MemoryStream(bytes, writable: false);
        using var archive = new ZipArchive(memory, ZipArchiveMode.Read, leaveOpen: false);
        var workbook = LoadXml(archive, "xl/workbook.xml");
        var workbookRels = LoadXml(archive, "xl/_rels/workbook.xml.rels");
        var sharedStrings = ReadSharedStrings(archive);
        var sheetTargets = workbookRels.Root?
            .Elements(PackageRelationshipNs + "Relationship")
            .Where(item => (string?)item.Attribute("Id") is not null)
            .ToDictionary(item => (string)item.Attribute("Id")!, item => NormalizeWorksheetTarget((string?)item.Attribute("Target") ?? string.Empty), StringComparer.Ordinal)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);

        string? dataSheetPath = null;
        string? metadataSheetPath = null;
        foreach (var sheet in workbook.Descendants(SpreadsheetNs + "sheet"))
        {
            var name = (string?)sheet.Attribute("name") ?? string.Empty;
            var relationshipId = (string?)sheet.Attribute(RelationshipNs + "id") ?? string.Empty;
            if (!sheetTargets.TryGetValue(relationshipId, out var target)) continue;
            if (name.Equals(VisibleSheetName, StringComparison.OrdinalIgnoreCase)) dataSheetPath = target;
            if (name.Equals(MetadataSheetName, StringComparison.OrdinalIgnoreCase)) metadataSheetPath = target;
        }
        if (dataSheetPath is null || metadataSheetPath is null)
            throw new InvalidOperationException("The workbook is missing the required MAUI Metadata worksheets.");

        var dataSheet = LoadXml(archive, dataSheetPath);
        var metadataSheet = LoadXml(archive, metadataSheetPath);
        var metadata = ReadKeyValueMetadata(metadataSheet, sharedStrings);
        var fieldDefinitions = ReadFieldDefinitions(metadataSheet, sharedStrings);
        var rowsByNumber = ReadRows(dataSheet, sharedStrings);
        if (!rowsByNumber.TryGetValue(1, out var stableHeaderCells))
            throw new InvalidOperationException("The workbook is missing its MAUI metadata column identifiers.");

        var orderedHeaders = stableHeaderCells
            .OrderBy(pair => pair.Key)
            .Select(pair => pair.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        var headerByColumn = stableHeaderCells
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value, EqualityComparer<int>.Default);

        var dataRows = new List<ParsedWorkbookRow>();
        foreach (var pair in rowsByNumber.Where(pair => pair.Key >= 3).OrderBy(pair => pair.Key))
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cell in pair.Value)
            {
                if (headerByColumn.TryGetValue(cell.Key, out var header)) values[header] = cell.Value ?? string.Empty;
            }
            dataRows.Add(new ParsedWorkbookRow(pair.Key, values));
        }

        return new ParsedWorkbook(metadata, fieldDefinitions, orderedHeaders, dataRows);
    }

    private static Dictionary<string, string> ReadKeyValueMetadata(XDocument sheet, IReadOnlyList<string> sharedStrings)
    {
        var rows = ReadRows(sheet, sharedStrings);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var row = 1; row <= 7; row++)
        {
            if (!rows.TryGetValue(row, out var cells)) continue;
            if (!cells.TryGetValue(1, out var key) || string.IsNullOrWhiteSpace(key)) continue;
            cells.TryGetValue(2, out var value);
            result[key.Trim()] = value ?? string.Empty;
        }
        return result;
    }

    private static IReadOnlyList<ExportedFieldDefinition> ReadFieldDefinitions(XDocument sheet, IReadOnlyList<string> sharedStrings)
    {
        var rows = ReadRows(sheet, sharedStrings);
        var result = new List<ExportedFieldDefinition>();
        foreach (var pair in rows.Where(pair => pair.Key >= 10).OrderBy(pair => pair.Key))
        {
            if (!pair.Value.TryGetValue(1, out var name) || string.IsNullOrWhiteSpace(name)) continue;
            pair.Value.TryGetValue(2, out var type);
            pair.Value.TryGetValue(3, out var required);
            pair.Value.TryGetValue(4, out var label);
            result.Add(new ExportedFieldDefinition(name.Trim(), type?.Trim() ?? string.Empty, string.Equals(required?.Trim(), "true", StringComparison.OrdinalIgnoreCase), label?.Trim() ?? string.Empty));
        }
        return result;
    }

    private static SortedDictionary<int, Dictionary<int, string>> ReadRows(XDocument sheet, IReadOnlyList<string> sharedStrings)
    {
        var result = new SortedDictionary<int, Dictionary<int, string>>();
        foreach (var row in sheet.Descendants(SpreadsheetNs + "row"))
        {
            if (!int.TryParse((string?)row.Attribute("r"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rowNumber)) continue;
            var cells = new Dictionary<int, string>();
            foreach (var cell in row.Elements(SpreadsheetNs + "c"))
            {
                var reference = (string?)cell.Attribute("r") ?? string.Empty;
                var columnIndex = ParseColumnIndex(reference);
                if (columnIndex <= 0) continue;
                cells[columnIndex] = ReadCellValue(cell, sharedStrings);
            }
            result[rowNumber] = cells;
        }
        return result;
    }

    private static string ReadCellValue(XElement cell, IReadOnlyList<string> sharedStrings)
    {
        if (cell.Element(SpreadsheetNs + "f") is not null)
            throw new InvalidOperationException("Formula cells are not supported in MAUI metadata workbooks. Paste values before importing.");

        var cellType = ((string?)cell.Attribute("t") ?? string.Empty).Trim();
        if (cellType.Equals("inlineStr", StringComparison.OrdinalIgnoreCase))
            return string.Concat(cell.Descendants(SpreadsheetNs + "t").Select(item => item.Value));

        var value = cell.Element(SpreadsheetNs + "v")?.Value ?? string.Empty;
        if (cellType.Equals("s", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedIndex)
            && sharedIndex >= 0 && sharedIndex < sharedStrings.Count)
            return sharedStrings[sharedIndex];
        if (cellType.Equals("b", StringComparison.OrdinalIgnoreCase)) return value == "1" ? "true" : "false";
        return value;
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return Array.Empty<string>();
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        return document.Root?
            .Elements(SpreadsheetNs + "si")
            .Select(item => string.Concat(item.Descendants(SpreadsheetNs + "t").Select(text => text.Value)))
            .ToArray() ?? Array.Empty<string>();
    }

    private static XDocument LoadXml(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path) ?? throw new InvalidOperationException($"The workbook is missing required part '{path}'.");
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static string NormalizeWorksheetTarget(string target)
    {
        var normalized = (target ?? string.Empty).Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)) return normalized;
        return $"xl/{normalized}";
    }

    private static int ParseColumnIndex(string cellReference)
    {
        var index = 0;
        foreach (var character in cellReference)
        {
            if (!char.IsLetter(character)) break;
            index = index * 26 + (char.ToUpperInvariant(character) - 'A' + 1);
        }
        return index;
    }

    private static string ColumnName(int index)
    {
        if (index <= 0) throw new ArgumentOutOfRangeException(nameof(index));
        var value = index;
        var builder = new StringBuilder();
        while (value > 0)
        {
            value--;
            builder.Insert(0, (char)('A' + value % 26));
            value /= 26;
        }
        return builder.ToString();
    }

    private static void ValidateWorkbookBytes(byte[] bytes, string fileName)
    {
        if (bytes is null || bytes.Length == 0) throw new InvalidOperationException("Select a metadata workbook to import.");
        if (bytes.Length > MaxWorkbookBytes) throw new InvalidOperationException($"Metadata workbooks cannot exceed {MaxWorkbookBytes / 1024 / 1024} MB.");
        if (!Path.GetExtension(fileName ?? string.Empty).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Metadata import requires an .xlsx workbook exported by MAUI.");
    }

    private sealed record MetadataObjectType(string Singular, string Label);
    private sealed record MetadataWorkbookObject(string Id, string Name);
    private sealed record ExportedFieldDefinition(string Name, string Type, bool Required, string Label);
    private sealed record ParsedWorkbookRow(int RowNumber, IReadOnlyDictionary<string, string> Values);
    private sealed record ParsedWorkbook(
        IReadOnlyDictionary<string, string> Metadata,
        IReadOnlyList<ExportedFieldDefinition> FieldDefinitions,
        IReadOnlyList<string> Headers,
        IReadOnlyList<ParsedWorkbookRow> Rows);
    private sealed record MetadataImportPlan(
        string FileName,
        MetadataObjectType Type,
        string EnvironmentKey,
        int RowsRead,
        int MatchedRows,
        int UnchangedRows,
        IReadOnlyList<MetadataImportIssue> Errors,
        IReadOnlyList<MetadataImportIssue> Warnings,
        IReadOnlyList<MetadataImportChange> Changes);

    private sealed class MetadataImportUndoPayload
    {
        public string Format { get; set; } = string.Empty;
        public string OperationId { get; set; } = string.Empty;
        public string ObjectType { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public Dictionary<string, Dictionary<string, string>> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

