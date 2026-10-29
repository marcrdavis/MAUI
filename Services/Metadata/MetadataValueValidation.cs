// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Shared validation/normalization for configured MAUI metadata values.

using System.Net.Mail;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.Metadata;

public static class MetadataValueValidation
{
    // Filters submitted metadata to configured fields and validates type-specific values.
    public static Dictionary<string, string> FilterAndValidate(
        ObjectMetadataDefinition definition,
        IReadOnlyDictionary<string, string>? submitted)
    {
        var configured = (definition.Fields ?? new List<MetadataFieldDefinition>())
            .Where(field => !string.IsNullOrWhiteSpace(field.Name))
            .ToDictionary(field => field.Name.Trim(), StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (submitted is null) return result;

        foreach (var pair in submitted)
        {
            if (!configured.TryGetValue(pair.Key, out var field)) continue;
            result[field.Name.Trim()] = NormalizeValue(field, pair.Value ?? string.Empty);
        }

        return result;
    }

    // Validates and normalizes one metadata value. Non-Email types retain their existing value semantics.
    public static string NormalizeValue(MetadataFieldDefinition field, string value)
    {
        var type = (field.Type ?? string.Empty).Trim();
        if (!type.Equals("Email", StringComparison.OrdinalIgnoreCase)) return value ?? string.Empty;

        var label = string.IsNullOrWhiteSpace(field.Label) ? field.Name : field.Label;
        if (string.IsNullOrWhiteSpace(value))
        {
            if (field.Required) throw new ArgumentException($"Metadata field '{label}' is required.");
            return string.Empty;
        }

        if (TryNormalizeEmailList(value, out var normalized)) return normalized;

        throw new ArgumentException($"Metadata field '{label}' must contain valid email address(es) separated by commas or semicolons.");
    }

    // Accepts one or more plain email addresses separated by commas and/or semicolons.
    public static bool TryNormalizeEmailList(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return true;

        var addresses = value
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => item.Length > 0)
            .ToArray();

        if (addresses.Length == 0) return true;

        foreach (var address in addresses)
        {
            if (!MailAddress.TryCreate(address, out var parsed)
                || !string.Equals(parsed.Address, address, StringComparison.OrdinalIgnoreCase))
            {
                normalized = string.Empty;
                return false;
            }
        }

        normalized = string.Join("; ", addresses);
        return true;
    }
}
