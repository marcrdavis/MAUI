// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.Metadata;

public interface IConfigurationUndoRepository
{
    // Purpose: Captures a new configuration snapshot while retaining the configured undo history.
    Task<ConfigurationUndoSnapshot> SaveSnapshotAsync(string environmentKey, string objectType, string objectId, string objectName, string username, string objectXml, CancellationToken cancellationToken = default);
    // Purpose: Returns one stored configuration undo snapshot by ID.
    Task<ConfigurationUndoSnapshot?> GetSnapshotAsync(string environmentKey, string objectType, string objectId, string username, string? snapshotId = null, CancellationToken cancellationToken = default);
    // Purpose: Returns the stored configuration undo snapshots for one object and user.
    Task<IReadOnlyList<ConfigurationUndoSnapshot>> GetSnapshotsAsync(string environmentKey, string objectType, string objectId, string username, CancellationToken cancellationToken = default);
    // Purpose: Returns snapshot object ids.
    Task<IReadOnlySet<string>> GetSnapshotObjectIdsAsync(string environmentKey, string objectType, string username, IEnumerable<string> objectIds, CancellationToken cancellationToken = default);
    // Purpose: Deletes snapshots after applying the required guardrails.
    Task DeleteSnapshotsAsync(string environmentKey, string objectType, IEnumerable<string> objectIds, CancellationToken cancellationToken = default);
}

public sealed record ConfigurationUndoSnapshot(
    string SnapshotId,
    string ObjectType,
    string ObjectId,
    string ObjectName,
    string CreatedBy,
    DateTimeOffset CreatedUtc,
    string ObjectXml);

public sealed class ConfigurationUndoRepository : IConfigurationUndoRepository
{
    private const int MaxHistoryEntries = 3;
    private const string EnvelopeFormat = "maui-undo-history-v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly MetadataDbContext _dbContext;

    // Purpose: Initializes configuration undo repository with its required dependencies.
    public ConfigurationUndoRepository(MetadataDbContext dbContext) => _dbContext = dbContext;

    // Stores a three-version history inside the existing undo row. Legacy raw XML rows are read as a one-entry history.
    // No metadata schema migration is required for the multi-level undo feature.
    public async Task<ConfigurationUndoSnapshot> SaveSnapshotAsync(
        string environmentKey,
        string objectType,
        string objectId,
        string objectName,
        string username,
        string objectXml,
        CancellationToken cancellationToken = default)
    {
        var scope = MoveItEnvironmentKey.Normalize(environmentKey);
        var normalizedType = NormalizeRequired(objectType, "Object Type");
        var normalizedId = NormalizeRequired(objectId, "Object ID");
        var normalizedUsername = NormalizeRequired(username, "Username");
        if (string.IsNullOrWhiteSpace(objectXml)) throw new InvalidOperationException("Configuration XML is required for an undo snapshot.");

        var userKey = NormalizeUserKey(normalizedUsername);
        var entity = await _dbContext.ConfigurationUndoSnapshots.SingleOrDefaultAsync(item =>
            item.EnvironmentKey == scope && item.ObjectType == normalizedType && item.ObjectId == normalizedId && item.UsernameKey == userKey,
            cancellationToken);

        var history = entity is null ? new List<HistoryEntry>() : ReadHistory(entity);
        var normalizedXml = NormalizeXmlForComparison(objectXml);
        var existing = history.FirstOrDefault(item => NormalizeXmlForComparison(item.ObjectXml) == normalizedXml);
        if (existing is not null)
        {
            return ToSnapshot(normalizedType, normalizedId, existing);
        }

        var newEntry = new HistoryEntry
        {
            SnapshotId = Guid.NewGuid().ToString("N"),
            ObjectName = (objectName ?? string.Empty).Trim(),
            CreatedBy = normalizedUsername,
            CreatedUtc = DateTimeOffset.UtcNow,
            ObjectXml = objectXml
        };
        history.Insert(0, newEntry);
        if (history.Count > MaxHistoryEntries) history.RemoveRange(MaxHistoryEntries, history.Count - MaxHistoryEntries);

        entity ??= new ConfigurationUndoSnapshotEntity
        {
            EnvironmentKey = scope,
            ObjectType = normalizedType,
            ObjectId = normalizedId,
            UsernameKey = userKey
        };
        if (_dbContext.Entry(entity).State == EntityState.Detached) _dbContext.ConfigurationUndoSnapshots.Add(entity);

        WriteHistory(entity, history);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToSnapshot(normalizedType, normalizedId, newEntry);
    }

    // Purpose: Returns one stored configuration undo snapshot by ID.
    public async Task<ConfigurationUndoSnapshot?> GetSnapshotAsync(
        string environmentKey,
        string objectType,
        string objectId,
        string username,
        string? snapshotId = null,
        CancellationToken cancellationToken = default)
    {
        var snapshots = await GetSnapshotsAsync(environmentKey, objectType, objectId, username, cancellationToken);
        if (snapshots.Count == 0) return null;
        if (string.IsNullOrWhiteSpace(snapshotId)) return snapshots[0];
        return snapshots.FirstOrDefault(item => item.SnapshotId.Equals(snapshotId.Trim(), StringComparison.Ordinal));
    }

    // Purpose: Returns the stored configuration undo snapshots for one object and user.
    public async Task<IReadOnlyList<ConfigurationUndoSnapshot>> GetSnapshotsAsync(
        string environmentKey,
        string objectType,
        string objectId,
        string username,
        CancellationToken cancellationToken = default)
    {
        var scope = MoveItEnvironmentKey.Normalize(environmentKey);
        var normalizedType = NormalizeRequired(objectType, "Object Type");
        var normalizedId = NormalizeRequired(objectId, "Object ID");
        var userKey = NormalizeUserKey(NormalizeRequired(username, "Username"));

        var entity = await _dbContext.ConfigurationUndoSnapshots.AsNoTracking().SingleOrDefaultAsync(item =>
            item.EnvironmentKey == scope && item.ObjectType == normalizedType && item.ObjectId == normalizedId && item.UsernameKey == userKey,
            cancellationToken);
        if (entity is null) return Array.Empty<ConfigurationUndoSnapshot>();

        return ReadHistory(entity)
            .OrderByDescending(item => item.CreatedUtc)
            .Take(MaxHistoryEntries)
            .Select(item => ToSnapshot(normalizedType, normalizedId, item))
            .ToList();
    }

    // Purpose: Returns snapshot object ids.
    public async Task<IReadOnlySet<string>> GetSnapshotObjectIdsAsync(
        string environmentKey,
        string objectType,
        string username,
        IEnumerable<string> objectIds,
        CancellationToken cancellationToken = default)
    {
        var scope = MoveItEnvironmentKey.Normalize(environmentKey);
        var normalizedType = NormalizeRequired(objectType, "Object Type");
        var ids = objectIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ids.Count == 0 || string.IsNullOrWhiteSpace(username)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var userKey = NormalizeUserKey(username);
        var snapshotIds = await _dbContext.ConfigurationUndoSnapshots.AsNoTracking()
            .Where(item => item.EnvironmentKey == scope && item.ObjectType == normalizedType && item.UsernameKey == userKey && ids.Contains(item.ObjectId))
            .Select(item => item.ObjectId)
            .ToListAsync(cancellationToken);
        return snapshotIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    // Purpose: Deletes snapshots after applying the required guardrails.
    public async Task DeleteSnapshotsAsync(string environmentKey, string objectType, IEnumerable<string> objectIds, CancellationToken cancellationToken = default)
    {
        var scope = MoveItEnvironmentKey.Normalize(environmentKey);
        var normalizedType = NormalizeRequired(objectType, "Object Type");
        var ids = objectIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (ids.Count == 0) return;

        var snapshots = await _dbContext.ConfigurationUndoSnapshots
            .Where(item => item.EnvironmentKey == scope && item.ObjectType == normalizedType && ids.Contains(item.ObjectId))
            .ToListAsync(cancellationToken);
        if (snapshots.Count == 0) return;
        _dbContext.ConfigurationUndoSnapshots.RemoveRange(snapshots);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    // Purpose: Reads history.
    private static List<HistoryEntry> ReadHistory(ConfigurationUndoSnapshotEntity entity)
    {
        var payload = (entity.ObjectXml ?? string.Empty).Trim();
        if (payload.StartsWith('{'))
        {
            try
            {
                var envelope = JsonSerializer.Deserialize<HistoryEnvelope>(payload, JsonOptions);
                if (envelope?.Format == EnvelopeFormat && envelope.Entries is { Count: > 0 })
                    return envelope.Entries.Where(IsValid).OrderByDescending(item => item.CreatedUtc).Take(MaxHistoryEntries).ToList();
            }
            catch (JsonException) { /* Legacy/raw XML is handled below. */ }
        }

        if (string.IsNullOrWhiteSpace(entity.ObjectXml)) return new List<HistoryEntry>();
        return new List<HistoryEntry>
        {
            new()
            {
                SnapshotId = $"legacy-{entity.CreatedUtc.UtcDateTime.Ticks}",
                ObjectName = entity.ObjectName,
                CreatedBy = entity.CreatedBy,
                CreatedUtc = entity.CreatedUtc,
                ObjectXml = entity.ObjectXml
            }
        };
    }

    // Purpose: Writes history.
    private static void WriteHistory(ConfigurationUndoSnapshotEntity entity, IReadOnlyList<HistoryEntry> history)
    {
        if (history.Count == 0) throw new InvalidOperationException("Undo history cannot be empty when saving an undo row.");
        var newest = history.OrderByDescending(item => item.CreatedUtc).First();
        entity.ObjectName = newest.ObjectName;
        entity.CreatedBy = newest.CreatedBy;
        entity.CreatedUtc = newest.CreatedUtc;
        entity.ObjectXml = JsonSerializer.Serialize(new HistoryEnvelope { Format = EnvelopeFormat, Entries = history.OrderByDescending(item => item.CreatedUtc).Take(MaxHistoryEntries).ToList() }, JsonOptions);
    }

    // Purpose: Converts the current value to snapshot.
    private static ConfigurationUndoSnapshot ToSnapshot(string objectType, string objectId, HistoryEntry entry)
        => new(entry.SnapshotId, objectType, objectId, entry.ObjectName, entry.CreatedBy, entry.CreatedUtc, entry.ObjectXml);

    // Purpose: Returns whether valid.
    private static bool IsValid(HistoryEntry entry)
        => !string.IsNullOrWhiteSpace(entry.SnapshotId) && !string.IsNullOrWhiteSpace(entry.ObjectXml);

    // Purpose: Normalizes XML for comparison.
    private static string NormalizeXmlForComparison(string xml)
        => (xml ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    // Purpose: Normalizes user key.
    private static string NormalizeUserKey(string username) => username.Trim().ToUpperInvariant();
    // Purpose: Normalizes required.
    private static string NormalizeRequired(string value, string label)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) throw new InvalidOperationException($"{label} is required.");
        return normalized;
    }

    private sealed class HistoryEnvelope
    {
        public string Format { get; set; } = EnvelopeFormat;
        public List<HistoryEntry> Entries { get; set; } = new();
    }

    private sealed class HistoryEntry
    {
        public string SnapshotId { get; set; } = string.Empty;
        public string ObjectName { get; set; } = string.Empty;
        public string CreatedBy { get; set; } = string.Empty;
        public DateTimeOffset CreatedUtc { get; set; }
        public string ObjectXml { get; set; } = string.Empty;
    }
}
