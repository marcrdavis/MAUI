// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Coordinates destructive Metadata field removal so definitions and stored values remain consistent.

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;

namespace MoveItAutomationUI.Services.Configuration;

public sealed record MetadataFieldDeletionDefinition(
    string ResourceKey,
    string ResourceLabel,
    string ObjectType,
    MetadataFieldDefinition Field);

public sealed record MetadataFieldDeletedValue(
    string EnvironmentKey,
    string ObjectType,
    string ObjectId,
    string FieldName,
    string Value);

public sealed record MetadataFieldDeletionPlan(
    string OperationId,
    string Username,
    IReadOnlyList<MetadataFieldDeletionDefinition> Fields)
{
    public bool HasChanges => Fields.Count > 0;
}

public sealed record MetadataFieldDeletionExecution(
    MetadataFieldDeletionPlan Plan,
    IReadOnlyList<MetadataFieldDeletedValue> Values,
    string UndoSnapshotId);

public interface IMetadataFieldDeletionService
{
    // Identifies persisted metadata definitions that will be removed by the pending Settings save.
    MetadataFieldDeletionPlan Prepare(JsonObject currentRoot, ConfigurationSettingsSaveRequest request, string username);

    // Captures a recovery snapshot and deletes all stored values for the removed field definitions.
    Task<MetadataFieldDeletionExecution?> ApplyAsync(MetadataFieldDeletionPlan plan, CancellationToken cancellationToken = default);

    // Restores a previously applied deletion when the surrounding Settings save fails.
    Task RollbackAsync(MetadataFieldDeletionExecution execution, CancellationToken cancellationToken = default);
}

public sealed class MetadataFieldDeletionService : IMetadataFieldDeletionService
{
    private const string UndoEnvironmentKey = "GLOBAL";
    private const string UndoObjectType = "MetadataFieldDeletion";
    private const string UndoPayloadFormat = "maui-metadata-field-delete-v1";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MetadataFieldDeletionService> _logger;

    public MetadataFieldDeletionService(
        IServiceScopeFactory scopeFactory,
        ILogger<MetadataFieldDeletionService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    // Compares the live configuration with the validated request. Only previously persisted fields can be deleted.
    public MetadataFieldDeletionPlan Prepare(JsonObject currentRoot, ConfigurationSettingsSaveRequest request, string username)
    {
        ArgumentNullException.ThrowIfNull(currentRoot);
        ArgumentNullException.ThrowIfNull(request);

        JsonObject metadata;
        if (!currentRoot.TryGetPropertyValue(MetadataUiOptions.SectionName, out var metadataNode) || metadataNode is null)
        {
            // An older installation may predate the Metadata section. There are no persisted
            // metadata definitions to delete in that case; the Settings mutator will create the
            // current section safely if/when the administrator saves it.
            metadata = new JsonObject();
        }
        else
        {
            metadata = metadataNode as JsonObject
                ?? throw new InvalidOperationException("The Metadata configuration section exists but is not a JSON object.");
        }

        var deleted = new List<MetadataFieldDeletionDefinition>();

        foreach (var resource in request.MetadataResources)
        {
            if (metadata[resource.Key] is not JsonObject resourceNode) continue;
            var persisted = resourceNode["Fields"]?.Deserialize<List<MetadataFieldDefinition>>(JsonOptions)
                ?? new List<MetadataFieldDefinition>();
            var retainedNames = resource.Fields
                .Select(field => field.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var managedTagsField in persisted.Where(field => field.Type.Equals("Tags", StringComparison.OrdinalIgnoreCase)))
            {
                var submitted = resource.Fields.FirstOrDefault(field => field.Name.Equals(managedTagsField.Name, StringComparison.OrdinalIgnoreCase));
                if (submitted is null || !ManagedFieldEquals(managedTagsField, submitted))
                    throw new InvalidOperationException($"The MAUI-managed Tags metadata field '{managedTagsField.Name}' cannot be modified or removed.");
            }

            var descriptor = MetadataResourceCatalog.GetByConfigurationKey(resource.Key);
            foreach (var field in persisted)
            {
                if (string.IsNullOrWhiteSpace(field.Name) || retainedNames.Contains(field.Name)) continue;
                deleted.Add(new MetadataFieldDeletionDefinition(
                    resource.Key,
                    descriptor.Label,
                    descriptor.ObjectType,
                    CloneField(field)));
            }
        }

        return new MetadataFieldDeletionPlan(
            Guid.NewGuid().ToString("N"),
            string.IsNullOrWhiteSpace(username) ? "unknown" : username.Trim(),
            deleted);
    }


    // MAUI-managed metadata definitions are immutable through Configuration Settings.
    private static bool ManagedFieldEquals(MetadataFieldDefinition persisted, MetadataFieldDefinition submitted)
    {
        static string Normalize(string? value) => value?.Trim() ?? string.Empty;

        return Normalize(persisted.Name).Equals(Normalize(submitted.Name), StringComparison.Ordinal)
            && Normalize(persisted.Label).Equals(Normalize(submitted.Label), StringComparison.Ordinal)
            && Normalize(persisted.Description).Equals(Normalize(submitted.Description), StringComparison.Ordinal)
            && Normalize(persisted.Type).Equals(Normalize(submitted.Type), StringComparison.OrdinalIgnoreCase)
            && persisted.Required == submitted.Required
            && (persisted.PossibleValues ?? new List<string>()).Select(Normalize)
                .SequenceEqual((submitted.PossibleValues ?? new List<string>()).Select(Normalize), StringComparer.Ordinal);
    }

    // Captures the exact values being removed and deletes them in the same metadata-database transaction.
    public async Task<MetadataFieldDeletionExecution?> ApplyAsync(
        MetadataFieldDeletionPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.HasChanges) return null;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();
        var undoRepository = scope.ServiceProvider.GetRequiredService<IConfigurationUndoRepository>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var rowsToDelete = new List<MetadataValueEntity>();
        foreach (var field in plan.Fields)
        {
            var rows = await db.MetadataValues
                .Where(value => value.ObjectType == field.ObjectType && value.FieldName == field.Field.Name)
                .ToListAsync(cancellationToken);
            rowsToDelete.AddRange(rows);
        }

        var deletedValues = rowsToDelete
            .Select(value => new MetadataFieldDeletedValue(
                value.EnvironmentKey,
                value.ObjectType,
                value.ObjectId,
                value.FieldName,
                value.Value))
            .ToArray();

        var payload = new MetadataFieldDeletionUndoPayload
        {
            Format = UndoPayloadFormat,
            OperationId = plan.OperationId,
            Fields = plan.Fields.ToArray(),
            Values = deletedValues
        };

        var undo = await undoRepository.SaveSnapshotAsync(
            UndoEnvironmentKey,
            UndoObjectType,
            plan.OperationId,
            BuildUndoName(plan.Fields),
            plan.Username,
            JsonSerializer.Serialize(payload, JsonOptions),
            cancellationToken);

        if (rowsToDelete.Count > 0)
        {
            db.MetadataValues.RemoveRange(rowsToDelete);
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Deleted {FieldCount} MAUI metadata field definition(s) and {ValueCount} stored value(s). Recovery snapshot {UndoSnapshotId} was created for operation {OperationId}.",
            plan.Fields.Count,
            deletedValues.Length,
            undo.SnapshotId,
            plan.OperationId);

        return new MetadataFieldDeletionExecution(plan, deletedValues, undo.SnapshotId);
    }

    // Restores deleted database values and removes the transient recovery snapshot if the outer save is rolled back.
    public async Task RollbackAsync(
        MetadataFieldDeletionExecution execution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();
        var undoRepository = scope.ServiceProvider.GetRequiredService<IConfigurationUndoRepository>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        foreach (var value in execution.Values)
        {
            var exists = await db.MetadataValues.AnyAsync(item =>
                item.EnvironmentKey == value.EnvironmentKey
                && item.ObjectType == value.ObjectType
                && item.ObjectId == value.ObjectId
                && item.FieldName == value.FieldName,
                cancellationToken);
            if (exists) continue;

            db.MetadataValues.Add(new MetadataValueEntity
            {
                EnvironmentKey = value.EnvironmentKey,
                ObjectType = value.ObjectType,
                ObjectId = value.ObjectId,
                FieldName = value.FieldName,
                Value = value.Value
            });
        }

        if (execution.Values.Count > 0) await db.SaveChangesAsync(cancellationToken);
        await undoRepository.DeleteSnapshotsAsync(
            UndoEnvironmentKey,
            UndoObjectType,
            new[] { execution.Plan.OperationId },
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogWarning(
            "Rolled back MAUI metadata field deletion operation {OperationId} after the surrounding Configuration save failed.",
            execution.Plan.OperationId);
    }

    private static MetadataFieldDefinition CloneField(MetadataFieldDefinition field) => new()
    {
        Name = field.Name,
        Label = field.Label,
        Description = field.Description,
        Type = field.Type,
        PossibleValues = field.PossibleValues?.ToList() ?? new List<string>(),
        Required = field.Required
    };

    private static string BuildUndoName(IReadOnlyList<MetadataFieldDeletionDefinition> fields)
    {
        if (fields.Count == 1)
        {
            var field = fields[0];
            return $"Deleted {field.ResourceLabel} metadata field {field.Field.Name}";
        }
        return $"Deleted {fields.Count} metadata fields";
    }

    private sealed class MetadataFieldDeletionUndoPayload
    {
        public string Format { get; set; } = UndoPayloadFormat;
        public string OperationId { get; set; } = string.Empty;
        public IReadOnlyList<MetadataFieldDeletionDefinition> Fields { get; set; } = Array.Empty<MetadataFieldDeletionDefinition>();
        public IReadOnlyList<MetadataFieldDeletedValue> Values { get; set; } = Array.Empty<MetadataFieldDeletedValue>();
    }
}
