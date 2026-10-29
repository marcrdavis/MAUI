// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Finishes a committed factory reset before Data Protection, metadata DB, or hosted jobs start.

using System.Text.Json;

namespace MoveItAutomationUI.Services.Lifecycle;

public static class MauiFactoryResetRecovery
{
    public const string MarkerFileName = ".factory-reset.pending";
    public const string MarkerContents = "MAUI-FACTORY-RESET-v1";

    public static string DataDirectory(string contentRootPath)
        => Path.Combine(Path.GetFullPath(contentRootPath), "Data");

    public static string MarkerPath(string contentRootPath)
        => Path.Combine(DataDirectory(contentRootPath), MarkerFileName);

    // Called during process startup, before any MAUI service can open a file under Data.
    // The marker is created BEFORE appsettings.json is cleared. A crash before that commit
    // retains the original configuration/data; a crash afterward resumes the reset here.
    public static void CompletePendingReset(string contentRootPath)
    {
        var marker = MarkerPath(contentRootPath);
        if (!File.Exists(marker)) return;

        if (!string.Equals(File.ReadAllText(marker).Trim(), MarkerContents, StringComparison.Ordinal))
            throw new InvalidOperationException("Unrecognized MAUI factory reset marker. No data was deleted.");

        var settingsPath = Path.Combine(Path.GetFullPath(contentRootPath), "appsettings.json");
        // Do not purge data until the appsettings.json reset commit is verifiably complete.
        // If the host stopped after the marker was prepared but BEFORE that commit, roll
        // back only the marker and start with the original configured installation.
        if (!IsEmptySettingsDocument(settingsPath))
        {
            File.Delete(marker);
            return;
        }

        var dataPath = DataDirectory(contentRootPath);
        // Fail closed on any junction/symlink/reparse point anywhere within the deletion
        // tree, instead of following links to files outside MAUI's installation.
        CheckDeletionTree(dataPath);

        // Delete the marker LAST. An interruption during cleanup leaves it in place so
        // the next startup can complete deletion rather than starting with partial data.
        foreach (var child in Directory.EnumerateFileSystemEntries(dataPath))
        {
            if (string.Equals(child, marker, StringComparison.OrdinalIgnoreCase)) continue;
            DeleteEntry(child);
        }
        File.Delete(marker);
        // Preserve the now-empty Data *directory*: restricted IIS identities can Modify
        // Data but may not have permission to recreate a directory under the app root.
        // Data Protection will create its fresh key subdirectory during regular startup.
    }

    internal static bool IsEmptySettingsDocument(string settingsPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
        return document.RootElement.ValueKind == JsonValueKind.Object
            && !document.RootElement.EnumerateObject().Any();
    }

    public static void CheckDeletionTree(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("MAUI Data contains a filesystem link; factory reset was halted before deleting data.");
        if (!Directory.Exists(path)) return;
        foreach (var child in Directory.EnumerateFileSystemEntries(path))
            CheckDeletionTree(child);
    }

    private static void DeleteEntry(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        else File.Delete(path);
    }
}
