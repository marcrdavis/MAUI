// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Stores reusable Traditional Task notification Subject and Message Body templates in MAUI metadata.

using Microsoft.EntityFrameworkCore;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Services.Metadata;

public sealed record TaskNotificationTemplate(
    string Id,
    string Name,
    string Subject,
    string Message,
    string? Format);

public sealed class TaskNotificationTemplateSaveRequest
{
    public string Name { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Format { get; set; }
    public string TemplateId { get; set; } = string.Empty;
    public bool OverwriteExisting { get; set; }
}

public sealed class TaskNotificationTemplateRenameRequest
{
    public string TemplateId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class TaskNotificationTemplateDeleteRequest
{
    public string TemplateId { get; set; } = string.Empty;
}

public interface ITaskNotificationTemplateService
{
    Task<IReadOnlyList<TaskNotificationTemplate>> GetCurrentAsync(CancellationToken cancellationToken = default);
    Task<TaskNotificationTemplate> SaveCurrentAsync(TaskNotificationTemplateSaveRequest request, CancellationToken cancellationToken = default);
    Task<TaskNotificationTemplate> RenameCurrentAsync(string templateId, string name, CancellationToken cancellationToken = default);
    Task DeleteCurrentAsync(string templateId, CancellationToken cancellationToken = default);
}

public sealed class TaskNotificationTemplateService : ITaskNotificationTemplateService
{
    private const string ObjectType = "TaskNotificationTemplate";
    private const string NameField = "Name";
    private const string NameKeyField = "NameKey";
    private const string SubjectField = "Subject";
    private const string MessageField = "Message";
    private const string FormatField = "Format";
    private readonly MetadataDbContext _db;
    private readonly IMoveItSessionService _sessionService;

    public TaskNotificationTemplateService(MetadataDbContext db, IMoveItSessionService sessionService)
    {
        _db = db;
        _sessionService = sessionService;
    }

    public async Task<IReadOnlyList<TaskNotificationTemplate>> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var session = RequireSession();
        var environmentKey = MoveItEnvironmentKey.Normalize(session.GetEnvironmentKey());
        var objects = await _db.MetadataObjects.AsNoTracking()
            .Where(item => item.EnvironmentKey == environmentKey && item.ObjectType == ObjectType)
            .Select(item => item.ObjectId)
            .ToListAsync(cancellationToken);
        if (objects.Count == 0) return Array.Empty<TaskNotificationTemplate>();

        var values = await _db.MetadataValues.AsNoTracking()
            .Where(item => item.EnvironmentKey == environmentKey && item.ObjectType == ObjectType && objects.Contains(item.ObjectId))
            .ToListAsync(cancellationToken);

        return values
            .GroupBy(item => item.ObjectId, StringComparer.OrdinalIgnoreCase)
            .Select(group => ToTemplate(group.Key, group.ToDictionary(item => item.FieldName, item => item.Value, StringComparer.OrdinalIgnoreCase)))
            .Where(template => template is not null)
            .Cast<TaskNotificationTemplate>()
            .OrderBy(template => template.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<TaskNotificationTemplate> SaveCurrentAsync(TaskNotificationTemplateSaveRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var session = RequireSession();
        var environmentKey = MoveItEnvironmentKey.Normalize(session.GetEnvironmentKey());
        var subject = (request.Subject ?? string.Empty).Trim();
        var message = request.Message ?? string.Empty;
        var format = NormalizeRequestedFormat(request.Format);
        if (subject.Length == 0) throw new ArgumentException("Subject is required before saving a template.");
        if (message.Trim().Length == 0) throw new ArgumentException("Message Body is required before saving a template.");

        if (request.OverwriteExisting)
        {
            var templateId = (request.TemplateId ?? string.Empty).Trim();
            if (templateId.Length == 0) throw new ArgumentException("Select a notification template to overwrite.");

            var metadataObject = await FindObjectAsync(environmentKey, templateId, cancellationToken)
                ?? throw new InvalidOperationException("The selected notification template no longer exists.");
            var values = await GetTrackedValuesAsync(environmentKey, templateId, cancellationToken);
            var currentTemplate = ToTemplate(templateId, values.ToDictionary(item => item.FieldName, item => item.Value, StringComparer.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("The selected notification template is missing its name.");
            // Empty Name preserves the existing Task Notifications overwrite contract.
            // Configuration supplies Name so rename/content changes commit atomically.
            var updatedName = string.IsNullOrWhiteSpace(request.Name) ? currentTemplate.Name : ValidateName(request.Name);
            await EnsureUniqueNameAsync(environmentKey, NormalizeName(updatedName), templateId, updatedName, cancellationToken);
            SetValue(values, environmentKey, templateId, NameField, updatedName);
            SetValue(values, environmentKey, templateId, NameKeyField, NormalizeName(updatedName));
            SetValue(values, environmentKey, templateId, SubjectField, subject);
            SetValue(values, environmentKey, templateId, MessageField, message);
            if (format is not null) SetValue(values, environmentKey, templateId, FormatField, format);

            Touch(metadataObject, session.Username);
            await _db.SaveChangesAsync(cancellationToken);
            return currentTemplate with { Name = updatedName, Subject = subject, Message = message, Format = format ?? currentTemplate.Format };
        }

        var name = ValidateName(request.Name);
        var nameKey = NormalizeName(name);
        await EnsureUniqueNameAsync(environmentKey, nameKey, null, name, cancellationToken);

        var id = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        _db.MetadataObjects.Add(new MetadataObjectEntity
        {
            EnvironmentKey = environmentKey,
            ObjectType = ObjectType,
            ObjectId = id,
            MetadataCreatedUtc = now,
            MetadataCreatedBy = session.Username,
            MetadataModifiedUtc = now,
            MetadataModifiedBy = session.Username
        });

        var templateValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [NameField] = name,
            [NameKeyField] = nameKey,
            [SubjectField] = subject,
            [MessageField] = message
        };
        if (format is not null) templateValues[FormatField] = format;

        foreach (var pair in templateValues)
        {
            _db.MetadataValues.Add(new MetadataValueEntity
            {
                EnvironmentKey = environmentKey,
                ObjectType = ObjectType,
                ObjectId = id,
                FieldName = pair.Key,
                Value = pair.Value
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new TaskNotificationTemplate(id, name, subject, message, format);
    }

    public async Task<TaskNotificationTemplate> RenameCurrentAsync(string templateId, string name, CancellationToken cancellationToken = default)
    {
        var session = RequireSession();
        var environmentKey = MoveItEnvironmentKey.Normalize(session.GetEnvironmentKey());
        templateId = (templateId ?? string.Empty).Trim();
        if (templateId.Length == 0) throw new ArgumentException("Template ID is required.");
        name = ValidateName(name);
        var nameKey = NormalizeName(name);

        var metadataObject = await FindObjectAsync(environmentKey, templateId, cancellationToken)
            ?? throw new InvalidOperationException("The selected notification template no longer exists.");
        await EnsureUniqueNameAsync(environmentKey, nameKey, templateId, name, cancellationToken);
        var values = await GetTrackedValuesAsync(environmentKey, templateId, cancellationToken);
        SetValue(values, environmentKey, templateId, NameField, name);
        SetValue(values, environmentKey, templateId, NameKeyField, nameKey);
        Touch(metadataObject, session.Username);
        await _db.SaveChangesAsync(cancellationToken);

        return ToTemplate(templateId, values.ToDictionary(item => item.FieldName, item => item.Value, StringComparer.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The notification template could not be read after it was renamed.");
    }

    public async Task DeleteCurrentAsync(string templateId, CancellationToken cancellationToken = default)
    {
        var session = RequireSession();
        var environmentKey = MoveItEnvironmentKey.Normalize(session.GetEnvironmentKey());
        templateId = (templateId ?? string.Empty).Trim();
        if (templateId.Length == 0) throw new ArgumentException("Template ID is required.");

        var metadataObject = await FindObjectAsync(environmentKey, templateId, cancellationToken)
            ?? throw new InvalidOperationException("The selected notification template no longer exists.");
        var values = await GetTrackedValuesAsync(environmentKey, templateId, cancellationToken);
        if (values.Count > 0) _db.MetadataValues.RemoveRange(values);
        _db.MetadataObjects.Remove(metadataObject);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private MoveItSessionState RequireSession()
        => _sessionService.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");

    private Task<MetadataObjectEntity?> FindObjectAsync(string environmentKey, string templateId, CancellationToken cancellationToken)
        => _db.MetadataObjects.FirstOrDefaultAsync(item =>
            item.EnvironmentKey == environmentKey && item.ObjectType == ObjectType && item.ObjectId == templateId,
            cancellationToken);

    private Task<List<MetadataValueEntity>> GetTrackedValuesAsync(string environmentKey, string templateId, CancellationToken cancellationToken)
        => _db.MetadataValues.Where(item =>
            item.EnvironmentKey == environmentKey && item.ObjectType == ObjectType && item.ObjectId == templateId)
            .ToListAsync(cancellationToken);

    private async Task EnsureUniqueNameAsync(string environmentKey, string nameKey, string? excludedTemplateId, string displayName, CancellationToken cancellationToken)
    {
        var duplicate = await _db.MetadataValues.AsNoTracking().AnyAsync(item =>
            item.EnvironmentKey == environmentKey &&
            item.ObjectType == ObjectType &&
            item.FieldName == NameKeyField &&
            item.Value == nameKey &&
            (excludedTemplateId == null || item.ObjectId != excludedTemplateId),
            cancellationToken);
        if (duplicate) throw new InvalidOperationException($"A notification template named '{displayName}' already exists.");
    }

    private void SetValue(List<MetadataValueEntity> values, string environmentKey, string templateId, string fieldName, string value)
    {
        var item = values.FirstOrDefault(existing => existing.FieldName.Equals(fieldName, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            item = new MetadataValueEntity
            {
                EnvironmentKey = environmentKey,
                ObjectType = ObjectType,
                ObjectId = templateId,
                FieldName = fieldName,
                Value = value
            };
            values.Add(item);
            _db.MetadataValues.Add(item);
        }
        else
        {
            item.Value = value;
        }
    }

    private static void Touch(MetadataObjectEntity metadataObject, string username)
    {
        metadataObject.MetadataModifiedUtc = DateTimeOffset.UtcNow;
        metadataObject.MetadataModifiedBy = username;
    }

    private static string ValidateName(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0) throw new ArgumentException("Template Name is required.");
        if (trimmed.Length > 100) throw new ArgumentException("Template Name cannot exceed 100 characters.");
        return trimmed;
    }

    private static TaskNotificationTemplate? ToTemplate(string id, IReadOnlyDictionary<string, string> values)
    {
        if (!values.TryGetValue(NameField, out var name) || string.IsNullOrWhiteSpace(name)) return null;
        return new TaskNotificationTemplate(id, name.Trim(), Get(values, SubjectField), Get(values, MessageField), GetStoredFormat(values));
    }

    public static string? NormalizeRequestedFormat(string? format)
    {
        var normalized = (format ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0) return null;
        if (normalized is "html" or "text") return normalized;
        throw new ArgumentException("Notification template Format must be HTML or Text.");
    }

    private static string? GetStoredFormat(IReadOnlyDictionary<string, string> values)
    {
        if (!values.TryGetValue(FormatField, out var value)) return null;
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized is "html" or "text" ? normalized : null;
    }

    private static string Get(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;

    private static string NormalizeName(string name) => name.Trim().ToUpperInvariant();
}
