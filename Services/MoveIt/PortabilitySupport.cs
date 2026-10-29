// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Services.MoveIt;

/// <summary>
/// Shared validation and naming helpers used by the Task and Host portable import/export workflows.
/// Feature-specific package parsing and MOVEit operations remain in their owning services.
/// </summary>
internal static class PortabilitySupport
{
    private static readonly HashSet<string> ReservedWindowsNames = new(
        new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" },
        StringComparer.OrdinalIgnoreCase);

    // Purpose: Generates an available MOVEit object copy name using the existing Task/Host naming convention.
    public static string SuggestCopyName(string original, IEnumerable<string> names, string fallbackPrefix)
    {
        var used = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var baseName = original.Length > 249 ? original[..249] : original;
        for (var i = 1; i <= 999; i++)
        {
            var suffix = i == 1 ? "_COPY" : $"_COPY{i}";
            var prefix = baseName.Length + suffix.Length <= 254 ? baseName : baseName[..Math.Max(1, 254 - suffix.Length)];
            var candidate = prefix + suffix;
            if (!used.Contains(candidate)) return candidate;
        }

        return $"{fallbackPrefix}_{Guid.NewGuid():N}"[..37];
    }

    // Purpose: Builds a Windows-safe .maui export filename using the existing Task/Host rules.
    public static string BuildSafeFileName(string objectName, string objectId, string fallbackPrefix)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var clean = new string((objectName ?? string.Empty)
            .Select(character => invalid.Contains(character) || char.IsControl(character) ? '_' : character)
            .ToArray())
            .Trim()
            .TrimEnd('.', ' ');

        var deviceBaseName = clean.Split('.', 2)[0];
        if (clean.Length == 0 || ReservedWindowsNames.Contains(deviceBaseName)) clean = $"{fallbackPrefix}_{objectId}";
        if (clean.Length > 180) clean = clean[..180].TrimEnd('.', ' ');
        return clean + ".maui";
    }


    // Purpose: Returns the shared Portable export passphrase-policy error, or an empty string when valid.
    public static string PassphrasePolicyError(string? passphrase, string label)
    {
        var value = passphrase ?? string.Empty;
        if (value.Length < 16) return $"{label} must be at least 16 characters long.";
        if (!value.Any(character => character >= 'A' && character <= 'Z')) return $"{label} must contain at least 1 uppercase letter.";
        if (!value.Any(character => character >= 'a' && character <= 'z')) return $"{label} must contain at least 1 lowercase letter.";
        if (!value.Any(character => character >= '0' && character <= '9')) return $"{label} must contain at least 1 digit.";
        if (!value.Any(character => !char.IsLetterOrDigit(character) && !char.IsWhiteSpace(character))) return $"{label} must contain at least 1 special character.";
        return string.Empty;
    }

    // Purpose: Normalizes a required MOVEit object ID while preserving the existing validation message format.
    public static string NormalizeId(string? id, string objectLabel)
    {
        var value = (id ?? string.Empty).Trim();
        if (value.Length == 0) throw new InvalidOperationException($"{objectLabel} ID is required.");
        return value;
    }

    // Purpose: Normalizes a required MOVEit object name while preserving the existing validation limits.
    public static string NormalizeName(string? name, string objectLabel)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length == 0) throw new InvalidOperationException($"{objectLabel} Name is required.");
        if (value.Length > 254) throw new InvalidOperationException($"{objectLabel} Name cannot exceed 254 characters.");
        return value;
    }

    // Purpose: Normalizes Create/Overwrite import mode while preserving Task/Host-specific validation text.
    public static string NormalizeMode(string? mode, string objectLabel)
    {
        var value = (mode ?? string.Empty).Trim();
        if (value.Equals("overwrite", StringComparison.OrdinalIgnoreCase)) return "overwrite";
        if (value.Length == 0 || value.Equals("create", StringComparison.OrdinalIgnoreCase)) return "create";
        throw new InvalidOperationException($"Import mode must be Create New {objectLabel} or Overwrite Existing {objectLabel}.");
    }

    // Purpose: Appends one warning sentence without changing existing whitespace behavior.
    public static string AppendWarning(string existing, string warning)
    {
        if (string.IsNullOrWhiteSpace(existing)) return warning;
        if (string.IsNullOrWhiteSpace(warning)) return existing;
        return existing.TrimEnd() + " " + warning.Trim();
    }
}
