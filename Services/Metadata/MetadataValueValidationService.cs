// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Async validation wrapper for metadata types that depend on MAUI-managed data such as Tags.

using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.Metadata;

public interface IMetadataValueValidationService
{
    Task<Dictionary<string, string>> FilterAndValidateAsync(
        string environmentKey,
        ObjectMetadataDefinition definition,
        IReadOnlyDictionary<string, string>? submitted,
        CancellationToken cancellationToken = default);
}

public sealed class MetadataValueValidationService : IMetadataValueValidationService
{
    private readonly ITagRepository _tags;

    public MetadataValueValidationService(ITagRepository tags)
    {
        _tags = tags;
    }

    public async Task<Dictionary<string, string>> FilterAndValidateAsync(
        string environmentKey,
        ObjectMetadataDefinition definition,
        IReadOnlyDictionary<string, string>? submitted,
        CancellationToken cancellationToken = default)
    {
        var configured = (definition.Fields ?? new List<MetadataFieldDefinition>())
            .Where(field => !string.IsNullOrWhiteSpace(field.Name))
            .ToDictionary(field => field.Name.Trim(), StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (submitted is null) return result;

        foreach (var pair in submitted)
        {
            if (!configured.TryGetValue(pair.Key, out var field)) continue;
            var value = pair.Value ?? string.Empty;
            if (field.Type.Equals("Tags", StringComparison.OrdinalIgnoreCase))
            {
                if (field.Required && string.IsNullOrWhiteSpace(value))
                    throw new ArgumentException($"Metadata field '{(string.IsNullOrWhiteSpace(field.Label) ? field.Name : field.Label)}' is required.");
                result[field.Name.Trim()] = await _tags.NormalizeAssignmentAsync(environmentKey, value, cancellationToken);
                continue;
            }
            result[field.Name.Trim()] = MetadataValueValidation.NormalizeValue(field, value);
        }
        return result;
    }
}
