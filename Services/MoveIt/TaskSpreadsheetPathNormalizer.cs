// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Path normalization adapted from the proven ImportTasksFromCSV project.

using System;

namespace MoveItAutomationUI.Services.MoveIt
{
    /// <summary>
    /// Normalizes imported MOVEit path fields so the folder separator is consistent
    /// and the path has the expected leading separator for the resolved host style.
    /// </summary>
    internal static class TaskSpreadsheetPathNormalizer
    {
        public static string NormalizeImportedPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value ?? string.Empty;

            var trimmed = value.Trim();
            if (IsFullUncPath(trimmed))
                return NormalizeFullUncPath(trimmed);

            if (trimmed.StartsWith("\\", StringComparison.Ordinal))
                return NormalizeShareRelativePath(trimmed);

            return NormalizeForwardSlashPath(trimmed);
        }

        public static string NormalizeForShareHost(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value ?? string.Empty;

            var trimmed = value.Trim();
            if (IsFullUncPath(trimmed))
                return NormalizeFullUncPath(trimmed);

            return NormalizeShareRelativePath(trimmed);
        }

        public static string NormalizeForNonShareHost(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value ?? string.Empty;

            var trimmed = value.Trim();
            if (IsFullUncPath(trimmed))
                return NormalizeFullUncPath(trimmed);

            return NormalizeForwardSlashPath(trimmed);
        }

        private static bool IsFullUncPath(string value)
        {
            var trimmed = value ?? string.Empty;
            return trimmed.StartsWith(@"\\", StringComparison.Ordinal) ||
                   trimmed.StartsWith("//", StringComparison.Ordinal);
        }

        private static string NormalizeFullUncPath(string value)
        {
            var body = ReplaceAllSeparators(value, '\\').TrimStart('\\');
            return @"\\" + body;
        }

        private static string NormalizeShareRelativePath(string value)
        {
            var body = ReplaceAllSeparators(value, '\\').TrimStart('\\');
            return "\\" + body;
        }

        private static string NormalizeForwardSlashPath(string value)
        {
            var body = ReplaceAllSeparators(value, '/').TrimStart('/');
            return "/" + body;
        }

        private static string ReplaceAllSeparators(string value, char separator)
        {
            var normalized = (value ?? string.Empty).Trim()
                .Replace('\\', separator)
                .Replace('/', separator);

            // Collapse repeated separators inside the path, while preserving the
            // leading UNC designator separately in NormalizeFullUncPath.
            var doubled = new string(separator, 2);
            var single = separator.ToString();
            while (normalized.Contains(doubled))
                normalized = normalized.Replace(doubled, single);

            return normalized;
        }
    }
}
