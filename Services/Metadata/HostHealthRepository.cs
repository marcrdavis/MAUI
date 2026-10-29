// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Persists system-managed Host health values without changing user-editable metadata audit fields.

using Microsoft.EntityFrameworkCore;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.Metadata;

public interface IHostHealthRepository
{
    Task SaveAsync(string environmentKey, string hostId, string health, DateTimeOffset checkedUtc, string detail, CancellationToken cancellationToken = default);
}

public sealed class HostHealthRepository : IHostHealthRepository
{
    private const string ObjectType = "Host";
    private readonly MetadataDbContext _db;

    public HostHealthRepository(MetadataDbContext db) => _db = db;

    public async Task SaveAsync(string environmentKey, string hostId, string health, DateTimeOffset checkedUtc, string detail, CancellationToken cancellationToken = default)
    {
        var scope = MoveItEnvironmentKey.Normalize(environmentKey);
        var id = (hostId ?? string.Empty).Trim();
        if (id.Length == 0) throw new ArgumentException("Host ID is required.", nameof(hostId));

        var objectExists = await _db.MetadataObjects.AnyAsync(item => item.EnvironmentKey == scope && item.ObjectType == ObjectType && item.ObjectId == id, cancellationToken);
        if (!objectExists)
        {
            _db.MetadataObjects.Add(new MetadataObjectEntity { EnvironmentKey = scope, ObjectType = ObjectType, ObjectId = id });
            await _db.SaveChangesAsync(cancellationToken);
        }

        var fieldNames = new[] { HostHealthMetadata.HealthField, HostHealthMetadata.LastCheckUtcField, HostHealthMetadata.DetailField };
        var rows = await _db.MetadataValues
            .Where(item => item.EnvironmentKey == scope && item.ObjectType == ObjectType && item.ObjectId == id && fieldNames.Contains(item.FieldName))
            .ToListAsync(cancellationToken);

        Upsert(rows, scope, id, HostHealthMetadata.HealthField, health);
        Upsert(rows, scope, id, HostHealthMetadata.LastCheckUtcField, checkedUtc.ToString("O"));
        Upsert(rows, scope, id, HostHealthMetadata.DetailField, Trim(detail, 2000));
        await _db.SaveChangesAsync(cancellationToken);
    }

    private void Upsert(List<MetadataValueEntity> rows, string environmentKey, string hostId, string fieldName, string value)
    {
        var row = rows.FirstOrDefault(item => item.FieldName.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
        if (row is not null)
        {
            row.Value = value ?? string.Empty;
            return;
        }
        row = new MetadataValueEntity
        {
            EnvironmentKey = environmentKey,
            ObjectType = ObjectType,
            ObjectId = hostId,
            FieldName = fieldName,
            Value = value ?? string.Empty
        };
        rows.Add(row);
        _db.MetadataValues.Add(row);
    }

    private static string Trim(string? value, int max)
    {
        var text = value ?? string.Empty;
        return text.Length <= max ? text : text[..max];
    }
}
