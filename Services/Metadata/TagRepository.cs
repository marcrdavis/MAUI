// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Central registry and assignment validation for MAUI metadata Tags.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.Metadata;

public sealed record TagDefinitionRecord(
    string Id,
    string Name,
    string Color,
    int UseCount,
    DateTimeOffset? CreatedUtc,
    string CreatedBy,
    DateTimeOffset? ModifiedUtc,
    string ModifiedBy);

public interface ITagRepository
{
    Task<IReadOnlyList<TagDefinitionRecord>> GetTagsAsync(string environmentKey, bool includeUseCounts = false, CancellationToken cancellationToken = default);
    Task<TagDefinitionRecord> CreateAsync(string environmentKey, string name, string color, string username, CancellationToken cancellationToken = default);
    Task<TagDefinitionRecord> UpdateAsync(string environmentKey, string id, string name, string color, string username, CancellationToken cancellationToken = default);
    Task DeleteAsync(string environmentKey, string id, CancellationToken cancellationToken = default);
    Task<string> NormalizeAssignmentAsync(string environmentKey, string? value, CancellationToken cancellationToken = default);
}

public sealed class TagRepository : ITagRepository
{
    internal const string TagObjectType = "__MAUI_TagDefinition";
    internal const string NameField = "__MAUI_Name";
    internal const string ColorField = "__MAUI_Color";
    private const int MaxTagNameLength = 75;

    private readonly MetadataDbContext _db;
    private readonly MetadataUiOptions _metadataOptions;

    public TagRepository(MetadataDbContext db, IOptionsSnapshot<MetadataUiOptions> metadataOptions)
    {
        _db = db;
        _metadataOptions = metadataOptions.Value;
    }

    public async Task<IReadOnlyList<TagDefinitionRecord>> GetTagsAsync(string environmentKey, bool includeUseCounts = false, CancellationToken cancellationToken = default)
    {
        var scope = MoveItEnvironmentKey.Normalize(environmentKey);
        var definitions = await LoadDefinitionsAsync(scope, cancellationToken);
        if (!includeUseCounts)
            return definitions.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();

        var useCounts = await GetUseCountsAsync(scope, definitions.Select(item => item.Name), cancellationToken);
        return definitions
            .Select(item => item with { UseCount = useCounts.TryGetValue(item.Name, out var count) ? count : 0 })
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<TagDefinitionRecord> CreateAsync(string environmentKey, string name, string color, string username, CancellationToken cancellationToken = default)
    {
        var scope = MoveItEnvironmentKey.Normalize(environmentKey);
        var normalizedName = NormalizeName(name);
        var normalizedColor = NormalizeColor(color);
        var existing = await LoadDefinitionsAsync(scope, cancellationToken);
        if (existing.Any(item => item.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Tag '{normalizedName}' already exists.");

        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        _db.MetadataObjects.Add(new MetadataObjectEntity
        {
            EnvironmentKey = scope,
            ObjectType = TagObjectType,
            ObjectId = id,
            MetadataCreatedUtc = now,
            MetadataCreatedBy = username,
            MetadataModifiedUtc = now,
            MetadataModifiedBy = username
        });
        _db.MetadataValues.AddRange(
            CreateValue(scope, id, NameField, normalizedName),
            CreateValue(scope, id, ColorField, normalizedColor));
        await _db.SaveChangesAsync(cancellationToken);
        return new TagDefinitionRecord(id, normalizedName, normalizedColor, 0, now, username, now, username);
    }

    public async Task<TagDefinitionRecord> UpdateAsync(string environmentKey, string id, string name, string color, string username, CancellationToken cancellationToken = default)
    {
        var scope = MoveItEnvironmentKey.Normalize(environmentKey);
        var tagId = (id ?? string.Empty).Trim();
        if (tagId.Length == 0) throw new ArgumentException("Tag ID is required.");
        var normalizedName = NormalizeName(name);
        var normalizedColor = NormalizeColor(color);

        var definitionObject = await _db.MetadataObjects.SingleOrDefaultAsync(item =>
            item.EnvironmentKey == scope && item.ObjectType == TagObjectType && item.ObjectId == tagId, cancellationToken)
            ?? throw new ArgumentException("The selected Tag no longer exists.");
        var rows = await _db.MetadataValues.Where(item =>
            item.EnvironmentKey == scope && item.ObjectType == TagObjectType && item.ObjectId == tagId).ToListAsync(cancellationToken);
        var nameRow = rows.FirstOrDefault(item => item.FieldName == NameField)
            ?? throw new InvalidOperationException("The selected Tag is missing its Name value.");
        var colorRow = rows.FirstOrDefault(item => item.FieldName == ColorField)
            ?? throw new InvalidOperationException("The selected Tag is missing its Color value.");
        var oldName = nameRow.Value;

        var duplicate = await LoadDefinitionsAsync(scope, cancellationToken);
        if (duplicate.Any(item => !item.Id.Equals(tagId, StringComparison.OrdinalIgnoreCase)
                                  && item.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Tag '{normalizedName}' already exists.");

        if (!oldName.Equals(normalizedName, StringComparison.Ordinal))
            await RenameAssignmentsAsync(scope, oldName, normalizedName, cancellationToken);

        nameRow.Value = normalizedName;
        colorRow.Value = normalizedColor;
        definitionObject.MetadataModifiedUtc = DateTimeOffset.UtcNow;
        definitionObject.MetadataModifiedBy = username;
        await _db.SaveChangesAsync(cancellationToken);

        var counts = await GetUseCountsAsync(scope, new[] { normalizedName }, cancellationToken);
        return new TagDefinitionRecord(
            tagId,
            normalizedName,
            normalizedColor,
            counts.TryGetValue(normalizedName, out var count) ? count : 0,
            definitionObject.MetadataCreatedUtc,
            definitionObject.MetadataCreatedBy,
            definitionObject.MetadataModifiedUtc,
            definitionObject.MetadataModifiedBy);
    }

    public async Task DeleteAsync(string environmentKey, string id, CancellationToken cancellationToken = default)
    {
        var scope = MoveItEnvironmentKey.Normalize(environmentKey);
        var tagId = (id ?? string.Empty).Trim();
        if (tagId.Length == 0) throw new ArgumentException("Tag ID is required.");
        var definitions = await LoadDefinitionsAsync(scope, cancellationToken);
        var tag = definitions.FirstOrDefault(item => item.Id.Equals(tagId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("The selected Tag no longer exists.");
        var counts = await GetUseCountsAsync(scope, new[] { tag.Name }, cancellationToken);
        if (counts.TryGetValue(tag.Name, out var count) && count > 0)
            throw new ArgumentException($"Tag '{tag.Name}' cannot be deleted because it is assigned to {count:N0} item(s).");

        var values = await _db.MetadataValues.Where(item =>
            item.EnvironmentKey == scope && item.ObjectType == TagObjectType && item.ObjectId == tagId).ToListAsync(cancellationToken);
        var definitionObject = await _db.MetadataObjects.SingleOrDefaultAsync(item =>
            item.EnvironmentKey == scope && item.ObjectType == TagObjectType && item.ObjectId == tagId, cancellationToken);
        if (values.Count > 0) _db.MetadataValues.RemoveRange(values);
        if (definitionObject is not null) _db.MetadataObjects.Remove(definitionObject);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<string> NormalizeAssignmentAsync(string environmentKey, string? value, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var scope = MoveItEnvironmentKey.Normalize(environmentKey);
        var definitions = await LoadDefinitionsAsync(scope, cancellationToken);
        var canonical = definitions.ToDictionary(item => item.Name, item => item.Name, StringComparer.OrdinalIgnoreCase);
        var submitted = SplitAssignment(value);
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in submitted)
        {
            if (!canonical.TryGetValue(tag, out var canonicalName))
                throw new ArgumentException($"Tag '{tag}' does not exist. Create it in Configuration > Settings > Tags before assigning it.");
            if (seen.Add(canonicalName)) result.Add(canonicalName);
        }
        return string.Join("; ", result);
    }

    private async Task<IReadOnlyList<TagDefinitionRecord>> LoadDefinitionsAsync(string scope, CancellationToken cancellationToken)
    {
        var objects = await _db.MetadataObjects.AsNoTracking()
            .Where(item => item.EnvironmentKey == scope && item.ObjectType == TagObjectType)
            .ToListAsync(cancellationToken);
        if (objects.Count == 0) return Array.Empty<TagDefinitionRecord>();
        var ids = objects.Select(item => item.ObjectId).ToArray();
        var values = await _db.MetadataValues.AsNoTracking()
            .Where(item => item.EnvironmentKey == scope && item.ObjectType == TagObjectType && ids.Contains(item.ObjectId))
            .ToListAsync(cancellationToken);
        var valuesByObject = values.GroupBy(item => item.ObjectId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToDictionary(item => item.FieldName, item => item.Value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

        var result = new List<TagDefinitionRecord>();
        foreach (var item in objects)
        {
            if (!valuesByObject.TryGetValue(item.ObjectId, out var itemValues)) continue;
            if (!itemValues.TryGetValue(NameField, out var name) || string.IsNullOrWhiteSpace(name)) continue;
            var color = itemValues.TryGetValue(ColorField, out var storedColor) ? storedColor : "#808080";
            result.Add(new TagDefinitionRecord(item.ObjectId, name.Trim(), NormalizeColor(color), 0,
                item.MetadataCreatedUtc, item.MetadataCreatedBy, item.MetadataModifiedUtc, item.MetadataModifiedBy));
        }
        return result;
    }

    private async Task<Dictionary<string, int>> GetUseCountsAsync(string scope, IEnumerable<string> tagNames, CancellationToken cancellationToken)
    {
        var names = tagNames.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var counts = names.ToDictionary(item => item, _ => 0, StringComparer.OrdinalIgnoreCase);
        if (names.Length == 0) return counts;
        var fieldMap = GetTagFieldMap();
        if (fieldMap.Count == 0) return counts;
        var fieldNames = fieldMap.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var rows = await _db.MetadataValues.AsNoTracking()
            .Where(item => item.EnvironmentKey == scope && fieldNames.Contains(item.FieldName))
            .Select(item => new { item.ObjectType, item.FieldName, item.Value })
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            if (!fieldMap.TryGetValue(row.ObjectType, out var expectedField)
                || !expectedField.Equals(row.FieldName, StringComparison.OrdinalIgnoreCase)) continue;
            var assigned = SplitAssignment(row.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
                if (assigned.Contains(name)) counts[name]++;
        }
        return counts;
    }

    private async Task RenameAssignmentsAsync(string scope, string oldName, string newName, CancellationToken cancellationToken)
    {
        var fieldMap = GetTagFieldMap();
        if (fieldMap.Count == 0) return;
        var fieldNames = fieldMap.Values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var rows = await _db.MetadataValues
            .Where(item => item.EnvironmentKey == scope && fieldNames.Contains(item.FieldName))
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            if (!fieldMap.TryGetValue(row.ObjectType, out var expectedField)
                || !expectedField.Equals(row.FieldName, StringComparison.OrdinalIgnoreCase)) continue;
            var assigned = SplitAssignment(row.Value);
            if (!assigned.Any(item => item.Equals(oldName, StringComparison.OrdinalIgnoreCase))) continue;
            var rebuilt = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in assigned)
            {
                var candidate = value.Equals(oldName, StringComparison.OrdinalIgnoreCase) ? newName : value;
                if (seen.Add(candidate)) rebuilt.Add(candidate);
            }
            row.Value = string.Join("; ", rebuilt);
        }
    }

    private Dictionary<string, string> GetTagFieldMap()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var resource in MetadataResourceCatalog.Resources)
        {
            var fields = resource.GetDefinition(_metadataOptions).Fields
                .Where(field => (field.Type ?? string.Empty).Equals("Tags", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(field.Name))
                .ToArray();
            if (fields.Length > 1) throw new InvalidOperationException($"Only one Tags metadata field may be configured for {resource.Label}.");
            if (fields.Length == 1) result[resource.ObjectType] = fields[0].Name.Trim();
        }
        return result;
    }

    private static string[] SplitAssignment(string? value)
        => (value ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => item.Length > 0)
            .ToArray();

    private static string NormalizeName(string? name)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length == 0) throw new ArgumentException("Tag Name is required.");
        if (value.Length > MaxTagNameLength) throw new ArgumentException($"Tag Name cannot exceed {MaxTagNameLength} characters.");
        if (value.Contains(';')) throw new ArgumentException("Tag Name cannot contain a semicolon because semicolons separate assigned Tags.");
        if (value.Any(char.IsControl)) throw new ArgumentException("Tag Name cannot contain control characters.");
        return value;
    }

    private static string NormalizeColor(string? color)
    {
        var value = (color ?? string.Empty).Trim();
        if (value.Length == 0) value = "#3E8AC7";
        if (!value.StartsWith('#')) value = "#" + value;
        if (value.Length != 7 || value.Skip(1).Any(ch => !Uri.IsHexDigit(ch)))
            throw new ArgumentException("Tag Color must be a six-digit hexadecimal color such as #3E8AC7.");
        return value.ToUpperInvariant();
    }

    private static MetadataValueEntity CreateValue(string scope, string objectId, string fieldName, string value) => new()
    {
        EnvironmentKey = scope,
        ObjectType = TagObjectType,
        ObjectId = objectId,
        FieldName = fieldName,
        Value = value
    };
}
