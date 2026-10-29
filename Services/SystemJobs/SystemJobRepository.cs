// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Persists System Job runtime configuration/state in the existing MAUI metadata tables.

using Microsoft.EntityFrameworkCore;
using MoveItAutomationUI.Services.Metadata;

namespace MoveItAutomationUI.Services.SystemJobs;

public sealed record PersistedSystemJobState(
    string Key,
    bool Enabled,
    string RunTime,
    DateTimeOffset? NextRunUtc,
    DateTimeOffset? LastRunUtc,
    string LastResult,
    string LastMessage);

public interface ISystemJobRepository
{
    Task<IReadOnlyDictionary<string, PersistedSystemJobState>> GetAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);
    Task SaveConfigurationAsync(string key, bool enabled, string runTime, DateTimeOffset? nextRunUtc, CancellationToken cancellationToken = default);
    Task RecordExecutionAsync(string key, DateTimeOffset startedUtc, string result, string message, DateTimeOffset? nextRunUtc, bool updateNextRun, CancellationToken cancellationToken = default);
}

public sealed class SystemJobRepository : ISystemJobRepository
{
    private const string StorageEnvironment = "__MAUI_SYSTEM__";
    private const string ObjectType = "SystemJob";
    private const string EnabledField = "Enabled";
    private const string RunTimeField = "RunTime";
    private const string NextRunUtcField = "NextRunUtc";
    private const string LastRunUtcField = "LastRunUtc";
    private const string LastResultField = "LastResult";
    private const string LastMessageField = "LastMessage";

    private readonly MetadataDbContext _db;

    public SystemJobRepository(MetadataDbContext db) => _db = db;

    public async Task<IReadOnlyDictionary<string, PersistedSystemJobState>> GetAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        var normalizedKeys = (keys ?? Array.Empty<string>())
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedKeys.Length == 0)
            return new Dictionary<string, PersistedSystemJobState>(StringComparer.OrdinalIgnoreCase);

        var rows = await _db.MetadataValues.AsNoTracking()
            .Where(item => item.EnvironmentKey == StorageEnvironment && item.ObjectType == ObjectType && normalizedKeys.Contains(item.ObjectId))
            .Select(item => new { item.ObjectId, item.FieldName, item.Value })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<string, PersistedSystemJobState>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in normalizedKeys)
        {
            var values = rows.Where(row => row.ObjectId.Equals(key, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(row => row.FieldName, row => row.Value, StringComparer.OrdinalIgnoreCase);
            if (values.Count == 0) continue;

            result[key] = new PersistedSystemJobState(
                key,
                ReadBool(values, EnabledField),
                Read(values, RunTimeField),
                ReadDate(values, NextRunUtcField),
                ReadDate(values, LastRunUtcField),
                Read(values, LastResultField),
                Read(values, LastMessageField));
        }
        return result;
    }

    public async Task SaveConfigurationAsync(string key, bool enabled, string runTime, DateTimeOffset? nextRunUtc, CancellationToken cancellationToken = default)
    {
        var normalizedKey = NormalizeKey(key);
        await EnsureObjectAsync(normalizedKey, cancellationToken);
        var rows = await LoadRowsAsync(normalizedKey, cancellationToken);
        Upsert(rows, normalizedKey, EnabledField, enabled ? "true" : "false");
        Upsert(rows, normalizedKey, RunTimeField, runTime);
        Upsert(rows, normalizedKey, NextRunUtcField, nextRunUtc?.ToString("O") ?? string.Empty);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordExecutionAsync(string key, DateTimeOffset startedUtc, string result, string message, DateTimeOffset? nextRunUtc, bool updateNextRun, CancellationToken cancellationToken = default)
    {
        var normalizedKey = NormalizeKey(key);
        await EnsureObjectAsync(normalizedKey, cancellationToken);
        var rows = await LoadRowsAsync(normalizedKey, cancellationToken);
        Upsert(rows, normalizedKey, LastRunUtcField, startedUtc.ToString("O"));
        Upsert(rows, normalizedKey, LastResultField, result ?? string.Empty);
        Upsert(rows, normalizedKey, LastMessageField, Trim(message, 2000));
        if (updateNextRun) Upsert(rows, normalizedKey, NextRunUtcField, nextRunUtc?.ToString("O") ?? string.Empty);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureObjectAsync(string key, CancellationToken cancellationToken)
    {
        var exists = await _db.MetadataObjects.AnyAsync(item =>
            item.EnvironmentKey == StorageEnvironment && item.ObjectType == ObjectType && item.ObjectId == key, cancellationToken);
        if (exists) return;
        _db.MetadataObjects.Add(new MetadataObjectEntity
        {
            EnvironmentKey = StorageEnvironment,
            ObjectType = ObjectType,
            ObjectId = key
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    private Task<List<MetadataValueEntity>> LoadRowsAsync(string key, CancellationToken cancellationToken)
        => _db.MetadataValues.Where(item => item.EnvironmentKey == StorageEnvironment && item.ObjectType == ObjectType && item.ObjectId == key).ToListAsync(cancellationToken);

    private void Upsert(List<MetadataValueEntity> rows, string key, string fieldName, string value)
    {
        var row = rows.FirstOrDefault(item => item.FieldName.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
        if (row is not null)
        {
            row.Value = value ?? string.Empty;
            return;
        }

        row = new MetadataValueEntity
        {
            EnvironmentKey = StorageEnvironment,
            ObjectType = ObjectType,
            ObjectId = key,
            FieldName = fieldName,
            Value = value ?? string.Empty
        };
        rows.Add(row);
        _db.MetadataValues.Add(row);
    }

    private static string NormalizeKey(string key)
    {
        var value = (key ?? string.Empty).Trim();
        if (value.Length == 0) throw new ArgumentException("System Job key is required.", nameof(key));
        return value;
    }

    private static string Read(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;

    private static bool ReadBool(IReadOnlyDictionary<string, string> values, string key)
        => bool.TryParse(Read(values, key), out var value) && value;

    private static DateTimeOffset? ReadDate(IReadOnlyDictionary<string, string> values, string key)
        => DateTimeOffset.TryParse(Read(values, key), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var value) ? value : null;

    private static string Trim(string? value, int max)
    {
        var text = value ?? string.Empty;
        return text.Length <= max ? text : text[..max];
    }
}
