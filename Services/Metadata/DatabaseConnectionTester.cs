// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Verifies that a configured MAUI metadata database is reachable and that MAUI can create schema objects.

using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.Metadata;

public interface IDatabaseConnectionTester
{
    // Purpose: Tests a proposed metadata database connection and required schema-write capability.
    Task<DatabaseConnectionTestResult> TestAsync(string provider, string connectionString, CancellationToken cancellationToken = default);
}

public sealed class DatabaseConnectionTestResult
{
    public string Provider { get; init; } = string.Empty;
    public string Database { get; init; } = string.Empty;
    public string DataSource { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public int UserTableCount { get; init; }
    public bool IsEmpty { get; init; }
    public bool IsMauiDatabase { get; init; }
    public bool SafeForMauiInitialization => IsEmpty || IsMauiDatabase;
}

public sealed class DatabaseConnectionTester : IDatabaseConnectionTester
{
    private readonly IWebHostEnvironment _environment;

    // Purpose: Initializes database connection tester with its required dependencies.
    public DatabaseConnectionTester(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    // Purpose: Tests a proposed metadata database connection and required schema-write capability.
    public async Task<DatabaseConnectionTestResult> TestAsync(
        string provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        provider = MetadataDatabaseProvider.Normalize(provider);
        connectionString = connectionString?.Trim() ?? string.Empty;
        if (connectionString.Length == 0) throw new ArgumentException("Database Connection String is required.");

        if (provider.Equals(SetupDatabaseProviders.SQLite, StringComparison.OrdinalIgnoreCase))
        {
            var resolvedConnectionString = MetadataDatabaseConnectionResolver.Resolve(
                provider, connectionString, _environment.ContentRootPath, _environment.WebRootPath, ensureSqliteDirectory: true);
            await using var connection = new SqliteConnection(resolvedConnectionString);
            await OpenAndVerifySchemaWriteAsync(connection, isSqlServer: false, cancellationToken);
            var inspection = await InspectTargetAsync(connection, isSqlServer: false, cancellationToken);
            return CreateResult(SetupDatabaseProviders.SQLite, connection, inspection);
        }

        if (provider.Equals(SetupDatabaseProviders.SqlServer, StringComparison.OrdinalIgnoreCase))
        {
            await using var connection = new SqlConnection(connectionString);
            await OpenAndVerifySchemaWriteAsync(connection, isSqlServer: true, cancellationToken);
            var inspection = await InspectTargetAsync(connection, isSqlServer: true, cancellationToken);
            return CreateResult(SetupDatabaseProviders.SqlServer, connection, inspection);
        }

        throw new InvalidOperationException($"Unsupported MetadataDatabase provider '{provider}'.");
    }


    // Purpose: Creates result.
    private static DatabaseConnectionTestResult CreateResult(
        string provider,
        DbConnection connection,
        DatabaseTargetInspection inspection)
    {
        return new DatabaseConnectionTestResult
        {
            Provider = provider,
            Database = connection.Database,
            DataSource = connection.DataSource,
            UserTableCount = inspection.UserTableCount,
            IsEmpty = inspection.IsEmpty,
            IsMauiDatabase = inspection.IsMauiDatabase,
            Message = "Database connection successful."
        };
    }

    // Purpose: Inspects whether the target database is empty, MAUI-owned, or unsafe to initialize.
    private static async Task<DatabaseTargetInspection> InspectTargetAsync(
        DbConnection connection,
        bool isSqlServer,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = isSqlServer
            ? "SELECT [name] FROM sys.tables WHERE is_ms_shipped = 0 ORDER BY [name];"
            : "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";

        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(0)) tables.Add(reader.GetString(0));
        }

        // MauiSchemaVersion is the authoritative marker for a manifest-managed MAUI database.
        // Complete Setup performs the deeper manifest/table validation before accepting the target.
        var isMauiDatabase = tables.Contains("MauiSchemaVersion");
        return new DatabaseTargetInspection(tables.Count, tables.Count == 0, isMauiDatabase);
    }

    private sealed record DatabaseTargetInspection(int UserTableCount, bool IsEmpty, bool IsMauiDatabase);

    // Purpose: Opens and verify schema write.
    private static async Task OpenAndVerifySchemaWriteAsync(
        DbConnection connection,
        bool isSqlServer,
        CancellationToken cancellationToken)
    {
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var tableName = $"__MauiSchemaTest_{Guid.NewGuid():N}";

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = isSqlServer
            ? $"CREATE TABLE [{tableName}] ([Id] int NOT NULL);"
            : $"CREATE TABLE [{tableName}] ([Id] INTEGER NOT NULL);";

        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.RollbackAsync(cancellationToken);
    }
}
