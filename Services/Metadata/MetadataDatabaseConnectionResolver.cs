// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Resolves installation-friendly SQLite paths without changing the value persisted in appsettings.json.

using Microsoft.Data.Sqlite;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.Metadata;

public static class MetadataDatabaseConnectionResolver
{
    // Purpose: Resolves a configured metadata connection string, including application-relative SQLite paths.
    public static string Resolve(
        string provider,
        string connectionString,
        string contentRootPath,
        string? webRootPath = null,
        bool ensureSqliteDirectory = false)
    {
        provider = provider?.Trim() ?? string.Empty;
        connectionString = connectionString?.Trim() ?? string.Empty;
        if (connectionString.Length == 0) return connectionString;

        if (!provider.Equals(SetupDatabaseProviders.SQLite, StringComparison.OrdinalIgnoreCase))
            return connectionString;

        var builder = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = builder.DataSource?.Trim() ?? string.Empty;
        if (dataSource.Length == 0)
            throw new ArgumentException("SQLite connection string must include a Data Source.");

        // The explicit :memory: data source is safe because it never creates a persistent file.
        if (dataSource.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
            return builder.ConnectionString;

        // Persistent SQLite file: URIs bypass normal path canonicalization and can otherwise place the
        // metadata database beneath wwwroot. MAUI does not require persistent URI filenames, so reject
        // them rather than attempting to interpret every SQLite URI/path combination.
        if (dataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Persistent SQLite file: URI data sources are not supported. Use Data\\maui.db or another normal filesystem path outside wwwroot.");

        var resolvedPath = Path.IsPathRooted(dataSource)
            ? Path.GetFullPath(dataSource)
            : Path.GetFullPath(Path.Combine(contentRootPath, dataSource));

        // MAUI serves static files from wwwroot. Never allow the metadata database to be placed under
        // that tree even if an administrator manually enters such a path.
        if (!string.IsNullOrWhiteSpace(webRootPath) && IsWithinDirectory(resolvedPath, webRootPath))
        {
            throw new InvalidOperationException("The SQLite database cannot be stored under wwwroot. Choose Data\\maui.db or another non-web-accessible path.");
        }

        if (ensureSqliteDirectory)
        {
            var directory = Path.GetDirectoryName(resolvedPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        }

        builder.DataSource = resolvedPath;
        return builder.ConnectionString;
    }

    // Purpose: Returns whether within directory.
    private static bool IsWithinDirectory(string filePath, string directoryPath)
    {
        var fullFilePath = Path.GetFullPath(filePath);
        var fullDirectoryPath = Path.GetFullPath(directoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        return fullFilePath.StartsWith(fullDirectoryPath, StringComparison.OrdinalIgnoreCase);
    }
}
