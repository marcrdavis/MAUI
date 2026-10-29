// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using Microsoft.EntityFrameworkCore;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.Metadata;

public interface IMetadataRepository
{
    // Purpose: Ensure objects.
    Task EnsureObjectsAsync(string environmentKey, string objectType, IEnumerable<string> objectIds, CancellationToken cancellationToken = default);
    // Purpose: Retrieve object metadata.
    Task<ObjectMetadataSnapshot> GetObjectMetadataAsync(string environmentKey, string objectType, string objectId, CancellationToken cancellationToken = default);
    // Purpose: Retrieve object metadata batch.
    Task<IReadOnlyDictionary<string, ObjectMetadataSnapshot>> GetObjectMetadataBatchAsync(string environmentKey, string objectType, IEnumerable<string> objectIds, CancellationToken cancellationToken = default);
    // Purpose: Save object metadata.
    Task SaveObjectMetadataAsync(string environmentKey, string objectType, string objectId, IReadOnlyDictionary<string, string> values, string username, CancellationToken cancellationToken = default);
    // Saves a validated metadata change set for multiple objects in one database operation.
    Task SaveObjectMetadataBatchAsync(string environmentKey, string objectType, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> valuesByObject, string username, CancellationToken cancellationToken = default);
    // Purpose: Record object created.
    Task RecordObjectCreatedAsync(string environmentKey, string objectType, string objectId, string username, CancellationToken cancellationToken = default);
    // Records an actual MOVEit configuration modification while also updating the visible Modified audit fields.
    Task RecordObjectConfigurationModifiedAsync(string environmentKey, string objectType, string objectId, string username, CancellationToken cancellationToken = default);
    // Records actual MOVEit configuration modifications for multiple objects in one database save.
    Task RecordObjectsConfigurationModifiedAsync(string environmentKey, string objectType, IEnumerable<string> objectIds, string username, CancellationToken cancellationToken = default);
    // Returns the internal configuration-modification audit used by Undo safety checks.
    Task<ObjectConfigurationAudit> GetObjectConfigurationAuditAsync(string environmentKey, string objectType, string objectId, CancellationToken cancellationToken = default);
    // Purpose: Assign object to folder.
    Task AssignObjectToFolderAsync(string environmentKey, string objectType, string objectId, int? folderId, string username, CancellationToken cancellationToken = default);
    // Purpose: Assign objects to folder.
    Task AssignObjectsToFolderAsync(string environmentKey, string objectType, IEnumerable<string> objectIds, int? folderId, string username, CancellationToken cancellationToken = default);
    // Purpose: Retrieve folders.
    Task<IReadOnlyList<MetadataFolderRecord>> GetFoldersAsync(string environmentKey, string objectType, CancellationToken cancellationToken = default);
    // Purpose: Create folder.
    Task<MetadataFolderRecord> CreateFolderAsync(string environmentKey, string objectType, string name, int? parentId, CancellationToken cancellationToken = default);
    // Creates missing root folders as one transaction, preserving any same-named folder in this scope.
    Task<MetadataFolderCreationResult> CreateMissingRootFoldersAsync(string environmentKey, string objectType, IEnumerable<string> names, CancellationToken cancellationToken = default);
    // Purpose: Rename folder.
    Task RenameFolderAsync(string environmentKey, string objectType, int folderId, string name, CancellationToken cancellationToken = default);
    // Purpose: Move folder.
    Task MoveFolderAsync(string environmentKey, string objectType, int folderId, int? parentId, CancellationToken cancellationToken = default);
    // Deletes a folder only when no environment-scoped child folders or task assignments remain.
    Task DeleteFolderAsync(string environmentKey, string objectType, int folderId, CancellationToken cancellationToken = default);
    // Purpose: Delete objects.
    Task DeleteObjectsAsync(string environmentKey, string objectType, IEnumerable<string> objectIds, CancellationToken cancellationToken = default);
    // Moves MAUI metadata/audit state when a MOVEit object whose natural key is its name is renamed.
    Task RenameObjectIdAsync(string environmentKey, string objectType, string oldObjectId, string newObjectId, CancellationToken cancellationToken = default);
}

public sealed record MetadataFolderRecord(int Id, int? ParentId, string Name);
public sealed record MetadataFolderCreationResult(int Created, int Existing);
public sealed record ObjectConfigurationAudit(DateTimeOffset? ModifiedUtc, string ModifiedBy);

public sealed record ObjectMetadataSnapshot(
    int? FolderId,
    IReadOnlyDictionary<string, string> Values,
    DateTimeOffset? CreatedUtc,
    string CreatedBy,
    DateTimeOffset? ModifiedUtc,
    string ModifiedBy);

public sealed class MetadataRepository : IMetadataRepository
{
    private readonly MetadataDbContext _dbContext;

    // Receives the provider-neutral EF Core context used by SQLite and SQL Server alike.
    public MetadataRepository(MetadataDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    // Ensures local rows exist for discovered MOVEit objects without maintaining discovery timestamps.
    public async Task EnsureObjectsAsync(string environmentKey, string objectType, IEnumerable<string> objectIds, CancellationToken cancellationToken = default)
    {
        var scope = MoveItEnvironmentKey.Normalize(environmentKey);
        var ids = objectIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ids.Count == 0) return;

        var existingIds = await _dbContext.MetadataObjects
            .Where(item => item.EnvironmentKey == scope && item.ObjectType == objectType && ids.Contains(item.ObjectId))
            .Select(item => item.ObjectId)
            .ToListAsync(cancellationToken);
        var existing = existingIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var id in ids)
        {
            if (existing.Contains(id)) continue;
            _dbContext.MetadataObjects.Add(new MetadataObjectEntity
            {
                EnvironmentKey = scope,
                ObjectType = objectType,
                ObjectId = id
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    // Loads dynamic field values plus local audit/tracking data for one MOVEit object.
    public async Task<ObjectMetadataSnapshot> GetObjectMetadataAsync(string environmentKey, string objectType, string objectId, CancellationToken cancellationToken = default)
    {
        var scope = MoveItEnvironmentKey.Normalize(environmentKey);
        var entity = await EnsureObjectAsync(scope, objectType, objectId, cancellationToken);
        var valueRows = await _dbContext.MetadataValues
            .AsNoTracking()
            .Where(item => item.EnvironmentKey == scope && item.ObjectType == objectType && item.ObjectId == objectId)
            .Select(item => new { item.FieldName, item.Value })
            .ToListAsync(cancellationToken);
        var values = valueRows.ToDictionary(item => item.FieldName, item => item.Value, StringComparer.OrdinalIgnoreCase);

        return new ObjectMetadataSnapshot(
            entity.FolderId,
            values,
            entity.MetadataCreatedUtc,
            entity.MetadataCreatedBy,
            entity.MetadataModifiedUtc,
            entity.MetadataModifiedBy);
    }

    // Loads metadata values, audit information, and folder assignments for a task set without per-task database calls.
    public async Task<IReadOnlyDictionary<string, ObjectMetadataSnapshot>> GetObjectMetadataBatchAsync(
        string environmentKey,
        string objectType,
        IEnumerable<string> objectIds,
        CancellationToken cancellationToken = default)
    {
        var scope = MoveItEnvironmentKey.Normalize(environmentKey);
        var ids = objectIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ids.Count == 0)
            return new Dictionary<string, ObjectMetadataSnapshot>(StringComparer.OrdinalIgnoreCase);

        var objectRows = await _dbContext.MetadataObjects
            .AsNoTracking()
            .Where(item => item.EnvironmentKey == scope && item.ObjectType == objectType && ids.Contains(item.ObjectId))
            .Select(item => new
            {
                item.ObjectId,
                item.FolderId,
                item.MetadataCreatedUtc,
                item.MetadataCreatedBy,
                item.MetadataModifiedUtc,
                item.MetadataModifiedBy
            })
            .ToListAsync(cancellationToken);

        var valueRows = await _dbContext.MetadataValues
            .AsNoTracking()
            .Where(item => item.EnvironmentKey == scope && item.ObjectType == objectType && ids.Contains(item.ObjectId))
            .Select(item => new { item.ObjectId, item.FieldName, item.Value })
            .ToListAsync(cancellationToken);

        var objectsById = objectRows.ToDictionary(item => item.ObjectId, StringComparer.OrdinalIgnoreCase);
        var valuesById = valueRows
            .GroupBy(item => item.ObjectId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyDictionary<string, string>)group.ToDictionary(
                    item => item.FieldName,
                    item => item.Value,
                    StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, ObjectMetadataSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            objectsById.TryGetValue(id, out var objectRow);
            valuesById.TryGetValue(id, out var values);

            result[id] = new ObjectMetadataSnapshot(
                objectRow?.FolderId,
                values ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                objectRow?.MetadataCreatedUtc,
                objectRow?.MetadataCreatedBy ?? string.Empty,
                objectRow?.MetadataModifiedUtc,
                objectRow?.MetadataModifiedBy ?? string.Empty);
        }

        return result;
    }

    // Saves configured dynamic field values and record the MAUI modification audit.
    public async Task SaveObjectMetadataAsync(string environmentKey, string objectType, string objectId, IReadOnlyDictionary<string, string> values, string username, CancellationToken cancellationToken = default)
    {
        var scope = MoveItEnvironmentKey.Normalize(environmentKey);
        var entity = await EnsureObjectAsync(scope, objectType, objectId, cancellationToken);
        var existingRows = await _dbContext.MetadataValues
            .Where(item => item.EnvironmentKey == scope && item.ObjectType == objectType && item.ObjectId == objectId)
            .ToListAsync(cancellationToken);
        var existing = existingRows.ToDictionary(item => item.FieldName, StringComparer.OrdinalIgnoreCase);

        foreach (var pair in values)
        {
            if (existing.TryGetValue(pair.Key, out var row)) row.Value = pair.Value ?? string.Empty;
            else
            {
                _dbContext.MetadataValues.Add(new MetadataValueEntity
                {
                    EnvironmentKey = scope,
                    ObjectType = objectType,
                    ObjectId = objectId,
                    FieldName = pair.Key,
                    Value = pair.Value ?? string.Empty
                });
            }
        }

        entity.MetadataModifiedUtc = DateTimeOffset.UtcNow;
        entity.MetadataModifiedBy = username;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    // Saves a validated metadata change set for multiple objects in one SaveChanges call so a bulk import does not leave a partially-applied batch.
    public async Task SaveObjectMetadataBatchAsync(
        string environmentKey,
        string objectType,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> valuesByObject,
        string username,
        CancellationToken cancellationToken = default)
    {
        if (valuesByObject is null || valuesByObject.Count == 0) return;

        var scope = MoveItEnvironmentKey.Normalize(environmentKey);
        var normalizedType = (objectType ?? string.Empty).Trim();
        if (normalizedType.Length == 0) throw new ArgumentException("Object Type is required.", nameof(objectType));
        var normalizedUsername = (username ?? string.Empty).Trim();
        if (normalizedUsername.Length == 0) throw new ArgumentException("Username is required.", nameof(username));

        var objectIds = valuesByObject.Keys
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (objectIds.Count == 0) return;

        var objectRows = await _dbContext.MetadataObjects
            .Where(item => item.EnvironmentKey == scope && item.ObjectType == normalizedType && objectIds.Contains(item.ObjectId))
            .ToListAsync(cancellationToken);
        var objectsById = objectRows.ToDictionary(item => item.ObjectId, StringComparer.OrdinalIgnoreCase);

        foreach (var objectId in objectIds)
        {
            if (objectsById.ContainsKey(objectId)) continue;
            var entity = new MetadataObjectEntity
            {
                EnvironmentKey = scope,
                ObjectType = normalizedType,
                ObjectId = objectId
            };
            _dbContext.MetadataObjects.Add(entity);
            objectsById[objectId] = entity;
        }

        var existingRows = await _dbContext.MetadataValues
            .Where(item => item.EnvironmentKey == scope && item.ObjectType == normalizedType && objectIds.Contains(item.ObjectId))
            .ToListAsync(cancellationToken);
        var existing = existingRows.ToDictionary(
            item => $"{item.ObjectId}\u001f{item.FieldName}",
            item => item,
            StringComparer.OrdinalIgnoreCase);

        var now = DateTimeOffset.UtcNow;
        foreach (var objectId in objectIds)
        {
            if (!valuesByObject.TryGetValue(objectId, out var values) || values is null || values.Count == 0) continue;
            foreach (var pair in values)
            {
                var fieldName = (pair.Key ?? string.Empty).Trim();
                if (fieldName.Length == 0) continue;
                var key = $"{objectId}\u001f{fieldName}";
                if (existing.TryGetValue(key, out var row))
                {
                    row.Value = pair.Value ?? string.Empty;
                }
                else
                {
                    var added = new MetadataValueEntity
                    {
                        EnvironmentKey = scope,
                        ObjectType = normalizedType,
                        ObjectId = objectId,
                        FieldName = fieldName,
                        Value = pair.Value ?? string.Empty
                    };
                    _dbContext.MetadataValues.Add(added);
                    existing[key] = added;
                }
            }

            var objectRow = objectsById[objectId];
            objectRow.MetadataModifiedUtc = now;
            objectRow.MetadataModifiedBy = normalizedUsername;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    // Records that MAUI created the underlying MOVEit object. Created is set only once; Modified is initialized with it.
    public async Task RecordObjectCreatedAsync(string environmentKey, string objectType, string objectId, string username, CancellationToken cancellationToken = default)
    {
        var scope = MoveItEnvironmentKey.Normalize(environmentKey);
        var entity = await EnsureObjectAsync(scope, objectType, objectId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (entity.MetadataCreatedUtc is null)
        {
            entity.MetadataCreatedUtc = now;
            entity.MetadataCreatedBy = username;
        }
        entity.MetadataModifiedUtc = now;
        entity.MetadataModifiedBy = username;
        entity.ConfigurationModifiedUtc = now;
        entity.ConfigurationModifiedBy = username;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    // Records a MOVEit configuration change separately from MAUI-only metadata/folder changes.
    public async Task RecordObjectConfigurationModifiedAsync(string environmentKey, string objectType, string objectId, string username, CancellationToken cancellationToken = default)
    {
        var scope = MoveItEnvironmentKey.Normalize(environmentKey);
        var entity = await EnsureObjectAsync(scope, objectType, objectId, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        entity.MetadataModifiedUtc = now;
        entity.MetadataModifiedBy = username;
        entity.ConfigurationModifiedUtc = now;
        entity.ConfigurationModifiedBy = username;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    // Records the same MOVEit configuration modification timestamp/user for a batch of objects.
    public async Task RecordObjectsConfigurationModifiedAsync(string environmentKey, string objectType, IEnumerable<string> objectIds, string username, CancellationToken cancellationToken = default)
    {
        var scope = MoveItEnvironmentKey.Normalize(environmentKey);
        var ids = objectIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ids.Count == 0) return;

        var existingRows = await _dbContext.MetadataObjects
            .Where(item => item.EnvironmentKey == scope && item.ObjectType == objectType && ids.Contains(item.ObjectId))
            .ToListAsync(cancellationToken);
        var existing = existingRows.ToDictionary(item => item.ObjectId, StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow;

        foreach (var id in ids)
        {
            if (!existing.TryGetValue(id, out var entity))
            {
                entity = new MetadataObjectEntity
                {
                    EnvironmentKey = scope,
                    ObjectType = objectType,
                    ObjectId = id
                };
                _dbContext.MetadataObjects.Add(entity);
            }

            entity.MetadataModifiedUtc = now;
            entity.MetadataModifiedBy = username;
            entity.ConfigurationModifiedUtc = now;
            entity.ConfigurationModifiedBy = username;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    // Loads the internal configuration audit without exposing it as user-editable task metadata.
    public async Task<ObjectConfigurationAudit> GetObjectConfigurationAuditAsync(string environmentKey, string objectType, string objectId, CancellationToken cancellationToken = default)
    {
        var scope = MoveItEnvironmentKey.Normalize(environmentKey);
        var entity = await EnsureObjectAsync(scope, objectType, objectId, cancellationToken);
        return new ObjectConfigurationAudit(entity.ConfigurationModifiedUtc, entity.ConfigurationModifiedBy);
    }

    // Moves one object into a task hierarchy folder, or to Root when folderId is null.
    public async Task AssignObjectToFolderAsync(string environmentKey, string objectType, string objectId, int? folderId, string username, CancellationToken cancellationToken = default)
    {
        var scope = MoveItEnvironmentKey.Normalize(environmentKey);
        if (folderId.HasValue) await RequireFolderAsync(scope, objectType, folderId.Value, cancellationToken);
        var entity = await EnsureObjectAsync(scope, objectType, objectId, cancellationToken);
        entity.FolderId = folderId;
        entity.MetadataModifiedUtc = DateTimeOffset.UtcNow;
        entity.MetadataModifiedBy = username;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    // Moves multiple objects as one local hierarchy operation so a multi-select drag requires only one database save.
    public async Task AssignObjectsToFolderAsync(string environmentKey, string objectType, IEnumerable<string> objectIds, int? folderId, string username, CancellationToken cancellationToken = default)
    {
        var scope = MoveItEnvironmentKey.Normalize(environmentKey);
        var ids = objectIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ids.Count == 0) return;

        if (folderId.HasValue) await RequireFolderAsync(scope, objectType, folderId.Value, cancellationToken);

        var existingRows = await _dbContext.MetadataObjects
            .Where(item => item.EnvironmentKey == scope && item.ObjectType == objectType && ids.Contains(item.ObjectId))
            .ToListAsync(cancellationToken);
        var existing = existingRows.ToDictionary(item => item.ObjectId, StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.UtcNow;

        foreach (var id in ids)
        {
            if (!existing.TryGetValue(id, out var entity))
            {
                entity = new MetadataObjectEntity
                {
                    EnvironmentKey = scope,
                    ObjectType = objectType,
                    ObjectId = id
                };
                _dbContext.MetadataObjects.Add(entity);
            }

            entity.FolderId = folderId;
            entity.MetadataModifiedUtc = now;
            entity.MetadataModifiedBy = username;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    // Returns all folders for one object type; hierarchy ordering is constructed by the UI so providers behave identically.
    public async Task<IReadOnlyList<MetadataFolderRecord>> GetFoldersAsync(string environmentKey, string objectType, CancellationToken cancellationToken = default)
    {
        var scope = MoveItEnvironmentKey.Normalize(environmentKey);
        return await _dbContext.MetadataFolders
            .AsNoTracking()
            .Where(item => item.EnvironmentKey == scope && item.ObjectType == objectType)
            .OrderBy(item => item.Name)
            .Select(item => new MetadataFolderRecord(item.Id, item.ParentId, item.Name))
            .ToListAsync(cancellationToken);
    }

    // Creates a hierarchy folder after validating its optional parent belongs to the same object type.
    public async Task<MetadataFolderRecord> CreateFolderAsync(string environmentKey, string objectType, string name, int? parentId, CancellationToken cancellationToken = default)
    {
        var scope = MoveItEnvironmentKey.Normalize(environmentKey);
        var normalizedName = NormalizeFolderName(name);
        if (parentId.HasValue) await RequireFolderAsync(scope, objectType, parentId.Value, cancellationToken);

        var duplicate = await _dbContext.MetadataFolders.AnyAsync(
            item => item.EnvironmentKey == scope && item.ObjectType == objectType && item.ParentId == parentId && item.Name == normalizedName,
            cancellationToken);
        if (duplicate) throw new InvalidOperationException("A folder with that name already exists at the selected level.");

        var entity = new MetadataFolderEntity { EnvironmentKey = scope, ObjectType = objectType, ParentId = parentId, Name = normalizedName };
        _dbContext.MetadataFolders.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new MetadataFolderRecord(entity.Id, entity.ParentId, entity.Name);
    }

    // Resource Group seeding is additive only. Compare in memory so SQLite and SQL Server
    // agree on case-insensitive names, including folders already present below the root.
    public async Task<MetadataFolderCreationResult> CreateMissingRootFoldersAsync(string environmentKey, string objectType, IEnumerable<string> names, CancellationToken cancellationToken = default)
    {
        var scope = MoveItEnvironmentKey.Normalize(environmentKey);
        // Validate the whole input before tracking inserts; never truncate a Resource Group name.
        var requested = names.Select(NormalizeFolderName).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        if (requested.Length == 0) return new(0, 0);

        // Serialize the read/insert decision at the database, including concurrent button clicks.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var existing = (await _dbContext.MetadataFolders.AsNoTracking()
            .Where(folder => folder.EnvironmentKey == scope && folder.ObjectType == objectType)
            .Select(folder => folder.Name).ToListAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var created = 0;
        foreach (var name in requested)
        {
            if (!existing.Add(name)) continue;
            _dbContext.MetadataFolders.Add(new MetadataFolderEntity { EnvironmentKey = scope, ObjectType = objectType, Name = name, ParentId = null });
            created++;
        }
        if (created > 0) await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(created, requested.Length - created);
    }

    // Renames one folder without changing its position in the hierarchy.
    public async Task RenameFolderAsync(string environmentKey, string objectType, int folderId, string name, CancellationToken cancellationToken = default)
    {
        var scope = MoveItEnvironmentKey.Normalize(environmentKey);
        var folder = await RequireFolderAsync(scope, objectType, folderId, cancellationToken);
        var normalizedName = NormalizeFolderName(name);
        var duplicate = await _dbContext.MetadataFolders.AnyAsync(
            item => item.EnvironmentKey == scope && item.ObjectType == objectType && item.ParentId == folder.ParentId && item.Id != folderId && item.Name == normalizedName,
            cancellationToken);
        if (duplicate) throw new InvalidOperationException("A folder with that name already exists at the selected level.");

        folder.Name = normalizedName;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    // Moves a folder while preventing cycles such as placing a parent beneath one of its descendants.
    public async Task MoveFolderAsync(string environmentKey, string objectType, int folderId, int? parentId, CancellationToken cancellationToken = default)
    {
        var scope = MoveItEnvironmentKey.Normalize(environmentKey);
        var folder = await RequireFolderAsync(scope, objectType, folderId, cancellationToken);
        if (parentId == folderId) throw new InvalidOperationException("A folder cannot be moved into itself.");
        if (parentId.HasValue)
        {
            await RequireFolderAsync(scope, objectType, parentId.Value, cancellationToken);
            if (await IsDescendantAsync(scope, objectType, parentId.Value, folderId, cancellationToken))
                throw new InvalidOperationException("A folder cannot be moved beneath one of its own descendants.");
        }

        var duplicate = await _dbContext.MetadataFolders.AnyAsync(
            item => item.EnvironmentKey == scope
                && item.ObjectType == objectType
                && item.ParentId == parentId
                && item.Id != folderId
                && item.Name == folder.Name,
            cancellationToken);
        if (duplicate) throw new InvalidOperationException("A folder with that name already exists at the selected level.");

        folder.ParentId = parentId;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }


    // Moves one local metadata identity while preserving metadata values and audit timestamps.
    // Global Parameters use their name as the natural MAUI object key, so a rename must move this state explicitly.
    public async Task RenameObjectIdAsync(string environmentKey, string objectType, string oldObjectId, string newObjectId, CancellationToken cancellationToken = default)
    {
        var scope = MoveItEnvironmentKey.Normalize(environmentKey);
        var oldId = (oldObjectId ?? string.Empty).Trim();
        var newId = (newObjectId ?? string.Empty).Trim();
        if (oldId.Length == 0 || newId.Length == 0) throw new ArgumentException("Both old and new object IDs are required.");
        if (oldId.Equals(newId, StringComparison.Ordinal)) return;

        var oldEntity = await _dbContext.MetadataObjects.SingleOrDefaultAsync(
            item => item.EnvironmentKey == scope && item.ObjectType == objectType && item.ObjectId == oldId,
            cancellationToken);
        if (oldEntity is null) return;

        var oldValues = await _dbContext.MetadataValues
            .Where(item => item.EnvironmentKey == scope && item.ObjectType == objectType && item.ObjectId == oldId)
            .ToListAsync(cancellationToken);
        var targetValues = await _dbContext.MetadataValues
            .Where(item => item.EnvironmentKey == scope && item.ObjectType == objectType && item.ObjectId == newId)
            .ToListAsync(cancellationToken);
        var targetEntity = await _dbContext.MetadataObjects.SingleOrDefaultAsync(
            item => item.EnvironmentKey == scope && item.ObjectType == objectType && item.ObjectId == newId,
            cancellationToken);

        if (targetValues.Count > 0) _dbContext.MetadataValues.RemoveRange(targetValues);
        if (targetEntity is not null) _dbContext.MetadataObjects.Remove(targetEntity);

        var replacement = new MetadataObjectEntity
        {
            EnvironmentKey = oldEntity.EnvironmentKey,
            ObjectType = oldEntity.ObjectType,
            ObjectId = newId,
            FolderId = oldEntity.FolderId,
            MetadataCreatedUtc = oldEntity.MetadataCreatedUtc,
            MetadataCreatedBy = oldEntity.MetadataCreatedBy,
            MetadataModifiedUtc = oldEntity.MetadataModifiedUtc,
            MetadataModifiedBy = oldEntity.MetadataModifiedBy,
            ConfigurationModifiedUtc = oldEntity.ConfigurationModifiedUtc,
            ConfigurationModifiedBy = oldEntity.ConfigurationModifiedBy
        };
        _dbContext.MetadataObjects.Add(replacement);
        foreach (var value in oldValues)
        {
            _dbContext.MetadataValues.Add(new MetadataValueEntity
            {
                EnvironmentKey = value.EnvironmentKey,
                ObjectType = value.ObjectType,
                ObjectId = newId,
                FieldName = value.FieldName,
                Value = value.Value
            });
        }

        if (oldValues.Count > 0) _dbContext.MetadataValues.RemoveRange(oldValues);
        _dbContext.MetadataObjects.Remove(oldEntity);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    // Removes MAUI-only tracking, folder assignment, and extended metadata after MOVEit confirms deletion.
    public async Task DeleteObjectsAsync(string environmentKey, string objectType, IEnumerable<string> objectIds, CancellationToken cancellationToken = default)
    {
        var scope = MoveItEnvironmentKey.Normalize(environmentKey);
        var ids = objectIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ids.Count == 0) return;

        var values = await _dbContext.MetadataValues.Where(item => item.EnvironmentKey == scope && item.ObjectType == objectType && ids.Contains(item.ObjectId)).ToListAsync(cancellationToken);
        var objects = await _dbContext.MetadataObjects.Where(item => item.EnvironmentKey == scope && item.ObjectType == objectType && ids.Contains(item.ObjectId)).ToListAsync(cancellationToken);
        _dbContext.MetadataValues.RemoveRange(values);
        _dbContext.MetadataObjects.RemoveRange(objects);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    // Deletes a folder only when the environment-scoped metadata store contains no child folders and no task assignments.
    // Local assignment rows are authoritative for this check so tasks hidden by the current user's MOVEit permissions still protect the folder.
    public async Task DeleteFolderAsync(string environmentKey, string objectType, int folderId, CancellationToken cancellationToken = default)
    {
        var scope = MoveItEnvironmentKey.Normalize(environmentKey);
        await RequireFolderAsync(scope, objectType, folderId, cancellationToken);

        var hasChildFolders = await _dbContext.MetadataFolders.AnyAsync(
            item => item.EnvironmentKey == scope && item.ObjectType == objectType && item.ParentId == folderId,
            cancellationToken);
        var hasAssignedObjects = await _dbContext.MetadataObjects.AnyAsync(
            item => item.EnvironmentKey == scope && item.ObjectType == objectType && item.FolderId == folderId,
            cancellationToken);

        if (hasChildFolders && hasAssignedObjects)
        {
            throw new InvalidOperationException(
                "This folder cannot be deleted because it contains subfolders and assigned tasks. Assigned tasks may include tasks that are not visible to the current user.");
        }

        if (hasChildFolders)
        {
            throw new InvalidOperationException(
                "This folder cannot be deleted because it contains one or more subfolders.");
        }

        if (hasAssignedObjects)
        {
            throw new InvalidOperationException(
                "This folder cannot be deleted because it contains one or more assigned tasks. Assigned tasks may include tasks that are not visible to the current user.");
        }

        var folder = await _dbContext.MetadataFolders.SingleAsync(
            item => item.EnvironmentKey == scope && item.ObjectType == objectType && item.Id == folderId,
            cancellationToken);
        _dbContext.MetadataFolders.Remove(folder);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    // Ensures an object-tracking row exists before metadata or hierarchy changes are applied.
    private async Task<MetadataObjectEntity> EnsureObjectAsync(string environmentKey, string objectType, string objectId, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.MetadataObjects.FindAsync(new object[] { environmentKey, objectType, objectId }, cancellationToken);
        if (entity is not null) return entity;

        entity = new MetadataObjectEntity
        {
            EnvironmentKey = environmentKey,
            ObjectType = objectType,
            ObjectId = objectId
        };
        _dbContext.MetadataObjects.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return entity;
    }

    // Loads one folder and verify it belongs to the requested hierarchy type.
    private async Task<MetadataFolderEntity> RequireFolderAsync(string environmentKey, string objectType, int folderId, CancellationToken cancellationToken)
        => await _dbContext.MetadataFolders.SingleOrDefaultAsync(item => item.EnvironmentKey == environmentKey && item.ObjectType == objectType && item.Id == folderId, cancellationToken)
           ?? throw new InvalidOperationException("The selected folder no longer exists.");

    // Walks parent links upward to determine whether candidateId is inside ancestorId's subtree.
    private async Task<bool> IsDescendantAsync(string environmentKey, string objectType, int candidateId, int ancestorId, CancellationToken cancellationToken)
    {
        var currentId = (int?)candidateId;
        while (currentId.HasValue)
        {
            if (currentId.Value == ancestorId) return true;
            currentId = await _dbContext.MetadataFolders
                .Where(item => item.EnvironmentKey == environmentKey && item.ObjectType == objectType && item.Id == currentId.Value)
                .Select(item => item.ParentId)
                .SingleOrDefaultAsync(cancellationToken);
        }
        return false;
    }

    // Applies a small amount of validation so hierarchy names remain readable and provider-safe.
    private static string NormalizeFolderName(string name)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length == 0) throw new InvalidOperationException("Folder name is required.");
        if (value.Length > 200) throw new InvalidOperationException("Folder name cannot exceed 200 characters.");
        return value;
    }
}
