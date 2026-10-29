// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Inspects, initializes, and applies declarative sequential migrations to the MAUI metadata database.

using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.Metadata;

public sealed class MauiDatabaseSchemaState
{
    private readonly object _sync = new();

    public int CurrentVersion { get; private set; }
    public int TargetVersion { get; private set; } = MauiDatabaseSchemaManager.InitialSchemaVersion;
    public string Provider { get; private set; } = string.Empty;
    public bool Initialized { get; private set; }

    // Purpose: Writes the current MAUI schema version record after a migration step commits.
    internal void Set(int currentVersion, int targetVersion, string provider, bool initialized = true)
    {
        lock (_sync)
        {
            CurrentVersion = currentVersion;
            TargetVersion = targetVersion;
            Provider = provider;
            Initialized = initialized;
        }
    }
}

public sealed class MauiDatabaseSchemaInspection
{
    public int Version { get; init; }
    public bool IsEmpty { get; init; }
    public bool HasVersionTable { get; init; }
    public bool IsRecognizedMauiDatabase { get; init; }
    public string Provider { get; init; } = string.Empty;
}

public sealed class MauiDatabaseSchemaManager
{
    public const int InitialSchemaVersion = 1;

    private readonly MetadataDbContext _db;
    private readonly MauiDatabaseSchemaState _state;
    private readonly ILogger<MauiDatabaseSchemaManager> _logger;

    // Purpose: Initializes MAUI database schema manager with its required dependencies.
    public MauiDatabaseSchemaManager(
        MetadataDbContext db,
        MauiDatabaseSchemaState state,
        ILogger<MauiDatabaseSchemaManager> logger)
    {
        _db = db;
        _state = state;
        _logger = logger;
    }

    // Inspection is intentionally read-only. An empty database is schema 0. Any non-empty MAUI
    // database must carry its own MauiSchemaVersion record; there is no alternate inferred-schema path.
    public async Task<MauiDatabaseSchemaInspection> InspectAsync(
        MauiUpgradeManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var targetVersion = manifest.RequiredSchemaVersion;
        var provider = _db.Database.ProviderName ?? string.Empty;
        var userTableCount = await CountUserTablesAsync(cancellationToken);
        if (userTableCount == 0)
        {
            _state.Set(0, targetVersion, provider, initialized: false);
            return new MauiDatabaseSchemaInspection
            {
                Version = 0,
                IsEmpty = true,
                HasVersionTable = false,
                IsRecognizedMauiDatabase = true,
                Provider = provider
            };
        }

        var hasVersionTable = await TableExistsAsync("MauiSchemaVersion", cancellationToken);
        if (!hasVersionTable)
        {
            throw new InvalidOperationException(
                "The selected database is not empty and does not contain a MAUI schema version record. " +
                "Use an empty database or a database created by MAUI's manifest-driven schema process.");
        }

        var version = await ReadVersionAsync(cancellationToken);
        if (version < InitialSchemaVersion)
            throw new InvalidOperationException("The MAUI database schema version record is invalid.");

        // Validate the physical tables declared by every migration already applied. The expected-table
        // list comes from upgrade.json so schema ownership remains in one place.
        if (version <= targetVersion)
            await ValidateDeclaredTablesAsync(manifest, version, cancellationToken);

        _state.Set(version, targetVersion, provider);
        return new MauiDatabaseSchemaInspection
        {
            Version = version,
            IsEmpty = false,
            HasVersionTable = true,
            IsRecognizedMauiDatabase = true,
            Provider = provider
        };
    }

    // Applies the same cumulative manifest path used by future upgrades. A fresh database is version 0,
    // so initial setup executes 0->1 (and any later sequential steps) rather than using EF EnsureCreated.
    public async Task<int> ApplyRequiredMigrationsAsync(
        MauiUpgradeManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var inspection = await InspectAsync(manifest, cancellationToken);
        var currentVersion = inspection.Version;

        if (currentVersion > manifest.RequiredSchemaVersion)
        {
            throw new InvalidOperationException(
                $"The MAUI database schema is version {currentVersion}, but this package supports schema version {manifest.RequiredSchemaVersion}.");
        }

        while (currentVersion < manifest.RequiredSchemaVersion)
        {
            var migration = manifest.SchemaMigrations.SingleOrDefault(item =>
                item.FromVersion == currentVersion && item.ToVersion == currentVersion + 1)
                ?? throw new InvalidOperationException(
                    $"upgrade.json does not contain the required schema migration {currentVersion}->{currentVersion + 1}.");

            await ApplyMigrationAsync(migration, manifest.RequiredSchemaVersion, cancellationToken);
            currentVersion = migration.ToVersion;
        }

        await ValidateDeclaredTablesAsync(manifest, currentVersion, cancellationToken);
        return currentVersion;
    }

    // Purpose: Applies migration to the current MAUI state.
    public async Task ApplyMigrationAsync(
        MauiSchemaMigrationDefinition migration,
        int targetVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(migration);
        if (migration.FromVersion < 0 || migration.ToVersion != migration.FromVersion + 1)
            throw new InvalidOperationException($"Schema migration {migration.FromVersion}->{migration.ToVersion} is not sequential.");

        var current = await ReadCurrentVersionForMigrationAsync(cancellationToken);
        if (current != migration.FromVersion)
            throw new InvalidOperationException($"Schema migration expected version {migration.FromVersion}, but the database is at version {current}.");

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var operation in migration.Operations)
            {
                await ApplyOperationAsync(operation, cancellationToken);
            }

            if (!await TableExistsAsync("MauiSchemaVersion", cancellationToken))
            {
                throw new InvalidOperationException(
                    $"Schema migration {migration.FromVersion}->{migration.ToVersion} did not create the required MauiSchemaVersion table.");
            }

            await WriteVersionAsync(migration.ToVersion, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _state.Set(migration.ToVersion, targetVersion, _db.Database.ProviderName ?? string.Empty);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        _logger.LogInformation(
            "Applied MAUI database migration {FromVersion}->{ToVersion}: {Description}",
            migration.FromVersion,
            migration.ToVersion,
            migration.Description);
    }

    // Purpose: Reads current version for migration.
    private async Task<int> ReadCurrentVersionForMigrationAsync(CancellationToken cancellationToken)
    {
        var userTableCount = await CountUserTablesAsync(cancellationToken);
        if (userTableCount == 0) return 0;
        if (!await TableExistsAsync("MauiSchemaVersion", cancellationToken))
        {
            throw new InvalidOperationException(
                "The selected database is partially initialized or was not created by MAUI: MauiSchemaVersion is missing.");
        }

        var version = await ReadVersionAsync(cancellationToken);
        if (version < InitialSchemaVersion)
            throw new InvalidOperationException("The MAUI database schema version record is invalid.");
        return version;
    }

    // Purpose: Validates declared tables.
    private async Task ValidateDeclaredTablesAsync(
        MauiUpgradeManifest manifest,
        int throughVersion,
        CancellationToken cancellationToken)
    {
        var expectedTables = manifest.SchemaMigrations
            .Where(migration => migration.ToVersion <= throughVersion)
            .SelectMany(migration => migration.Operations)
            .Where(operation => operation.Operation.Equals("CreateTable", StringComparison.OrdinalIgnoreCase))
            .Select(operation => RequireIdentifier(operation.Table, "Table"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var table in expectedTables)
        {
            if (!await TableExistsAsync(table, cancellationToken))
            {
                throw new InvalidOperationException(
                    $"MAUI schema version {throughVersion} is incomplete: required table '{table}' is missing.");
            }
        }
    }

    // Purpose: Applies operation.
    private async Task ApplyOperationAsync(MauiSchemaOperationDefinition operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var kind = operation.Operation?.Trim() ?? string.Empty;
        if (kind.Equals("CreateTable", StringComparison.OrdinalIgnoreCase))
        {
            await CreateTableAsync(operation, cancellationToken);
            return;
        }
        if (kind.Equals("AddColumn", StringComparison.OrdinalIgnoreCase))
        {
            await AddColumnAsync(operation, cancellationToken);
            return;
        }
        if (kind.Equals("RenameColumn", StringComparison.OrdinalIgnoreCase))
        {
            await RenameColumnAsync(operation, cancellationToken);
            return;
        }
        if (kind.Equals("DropColumn", StringComparison.OrdinalIgnoreCase))
        {
            await DropColumnAsync(operation, cancellationToken);
            return;
        }
        if (kind.Equals("CreateIndex", StringComparison.OrdinalIgnoreCase))
        {
            await CreateIndexAsync(operation, cancellationToken);
            return;
        }
        if (kind.Equals("DropIndex", StringComparison.OrdinalIgnoreCase))
        {
            await DropIndexAsync(operation, cancellationToken);
            return;
        }
        if (kind.Equals("ExecuteSql", StringComparison.OrdinalIgnoreCase))
        {
            await ExecuteProviderSqlAsync(operation, cancellationToken);
            return;
        }

        throw new InvalidOperationException($"Unsupported schema operation '{operation.Operation}'.");
    }

    // Purpose: Creates table.
    private async Task CreateTableAsync(MauiSchemaOperationDefinition operation, CancellationToken cancellationToken)
    {
        var table = RequireIdentifier(operation.Table, "Table");
        if (operation.Columns.Count == 0)
            throw new InvalidOperationException($"CreateTable for '{table}' requires at least one column.");
        if (await TableExistsAsync(table, cancellationToken)) return;

        var definitions = operation.Columns
            .Select(column => BuildColumnDefinition(column, includePrimaryKey: true))
            .ToList();

        if (operation.PrimaryKey is not null)
        {
            var primaryKeyColumns = operation.PrimaryKey.Columns
                .Select(column => RequireIdentifier(column, "PrimaryKey.Columns"))
                .ToArray();
            if (primaryKeyColumns.Length == 0)
                throw new InvalidOperationException($"CreateTable for '{table}' has an empty PrimaryKey definition.");

            var constraintName = string.IsNullOrWhiteSpace(operation.PrimaryKey.Name)
                ? $"PK_{table}"
                : RequireIdentifier(operation.PrimaryKey.Name, "PrimaryKey.Name");
            definitions.Add(
                $"CONSTRAINT {Quote(constraintName)} PRIMARY KEY ({string.Join(", ", primaryKeyColumns.Select(Quote))})");
        }

        foreach (var foreignKey in operation.ForeignKeys)
        {
            var foreignKeyColumns = foreignKey.Columns
                .Select(column => RequireIdentifier(column, "ForeignKeys.Columns"))
                .ToArray();
            var principalColumns = foreignKey.PrincipalColumns
                .Select(column => RequireIdentifier(column, "ForeignKeys.PrincipalColumns"))
                .ToArray();
            if (foreignKeyColumns.Length == 0 || foreignKeyColumns.Length != principalColumns.Length)
            {
                throw new InvalidOperationException(
                    $"CreateTable for '{table}' has an invalid foreign-key column mapping.");
            }

            var principalTable = RequireIdentifier(foreignKey.PrincipalTable, "ForeignKeys.PrincipalTable");
            var constraintName = string.IsNullOrWhiteSpace(foreignKey.Name)
                ? $"FK_{table}_{principalTable}_{string.Join("_", foreignKeyColumns)}"
                : RequireIdentifier(foreignKey.Name, "ForeignKeys.Name");
            var onDelete = MapDeleteBehavior(foreignKey.OnDelete);
            definitions.Add(
                $"CONSTRAINT {Quote(constraintName)} FOREIGN KEY ({string.Join(", ", foreignKeyColumns.Select(Quote))}) " +
                $"REFERENCES {Quote(principalTable)} ({string.Join(", ", principalColumns.Select(Quote))}) ON DELETE {onDelete}");
        }

        var sql = $"CREATE TABLE {Quote(table)} ({string.Join(", ", definitions)});";
        await ExecuteValidatedSchemaSqlAsync(sql, cancellationToken);
    }

    // Purpose: Adds column.
    private async Task AddColumnAsync(MauiSchemaOperationDefinition operation, CancellationToken cancellationToken)
    {
        var table = RequireIdentifier(operation.Table, "Table");
        var column = operation.Column ?? throw new InvalidOperationException($"AddColumn for '{table}' requires Column.");
        var name = RequireIdentifier(column.Name, "Column.Name");
        if (!await TableExistsAsync(table, cancellationToken))
            throw new InvalidOperationException($"Cannot add column '{name}' because table '{table}' does not exist.");
        if (await ColumnExistsAsync(table, name, cancellationToken)) return;
        if (column.PrimaryKey || column.AutoIncrement)
            throw new InvalidOperationException("AddColumn does not support adding a primary-key or auto-increment column to an existing table.");

        var sql = $"ALTER TABLE {Quote(table)} ADD {BuildColumnDefinition(column, includePrimaryKey: false)};";
        await ExecuteValidatedSchemaSqlAsync(sql, cancellationToken);
    }

    // Purpose: Renames a database column using provider-appropriate DDL.
    private async Task RenameColumnAsync(MauiSchemaOperationDefinition operation, CancellationToken cancellationToken)
    {
        var table = RequireIdentifier(operation.Table, "Table");
        var oldName = RequireIdentifier(operation.Name, "Name");
        var newName = RequireIdentifier(operation.NewName, "NewName");
        if (await ColumnExistsAsync(table, newName, cancellationToken)) return;
        if (!await ColumnExistsAsync(table, oldName, cancellationToken))
            throw new InvalidOperationException($"Cannot rename '{table}.{oldName}' because the source column does not exist.");

        if (IsSqlite())
        {
            var sql = $"ALTER TABLE {Quote(table)} RENAME COLUMN {Quote(oldName)} TO {Quote(newName)};";
            await ExecuteValidatedSchemaSqlAsync(sql, cancellationToken);
            return;
        }
        if (IsSqlServer())
        {
            var fullName = $"{table}.{oldName}".Replace("'", "''", StringComparison.Ordinal);
            var target = newName.Replace("'", "''", StringComparison.Ordinal);
            var sql = $"EXEC sp_rename N'{fullName}', N'{target}', N'COLUMN';";
            await ExecuteValidatedSchemaSqlAsync(sql, cancellationToken);
            return;
        }
        throw UnsupportedProvider();
    }

    // Purpose: Drops a database column using provider-appropriate DDL.
    private async Task DropColumnAsync(MauiSchemaOperationDefinition operation, CancellationToken cancellationToken)
    {
        var table = RequireIdentifier(operation.Table, "Table");
        var name = RequireIdentifier(operation.Name, "Name");
        if (!await ColumnExistsAsync(table, name, cancellationToken)) return;
        var sql = $"ALTER TABLE {Quote(table)} DROP COLUMN {Quote(name)};";
        await ExecuteValidatedSchemaSqlAsync(sql, cancellationToken);
    }

    // Purpose: Creates index.
    private async Task CreateIndexAsync(MauiSchemaOperationDefinition operation, CancellationToken cancellationToken)
    {
        var table = RequireIdentifier(operation.Table, "Table");
        var name = RequireIdentifier(operation.Name, "Name");
        var columns = operation.IndexColumns.Select(column => RequireIdentifier(column, "IndexColumns")).ToArray();
        if (columns.Length == 0) throw new InvalidOperationException($"CreateIndex '{name}' requires at least one IndexColumns entry.");
        if (await IndexExistsAsync(table, name, cancellationToken)) return;
        var unique = operation.Unique ? "UNIQUE " : string.Empty;
        var sql = $"CREATE {unique}INDEX {Quote(name)} ON {Quote(table)} ({string.Join(", ", columns.Select(Quote))});";
        await ExecuteValidatedSchemaSqlAsync(sql, cancellationToken);
    }

    // Purpose: Drops a database index when it exists.
    private async Task DropIndexAsync(MauiSchemaOperationDefinition operation, CancellationToken cancellationToken)
    {
        var table = RequireIdentifier(operation.Table, "Table");
        var name = RequireIdentifier(operation.Name, "Name");
        if (!await IndexExistsAsync(table, name, cancellationToken)) return;
        var sql = IsSqlServer()
            ? $"DROP INDEX {Quote(name)} ON {Quote(table)};"
            : $"DROP INDEX {Quote(name)};";
        await ExecuteValidatedSchemaSqlAsync(sql, cancellationToken);
    }

    // Generated schema SQL reaches this method only after every identifier has passed RequireIdentifier
    // and provider-specific identifier quoting has been applied. Identifiers cannot be SQL parameters.
    private Task ExecuteValidatedSchemaSqlAsync(string sql, CancellationToken cancellationToken)
        => _db.Database.ExecuteSqlRawAsync(sql, cancellationToken);

    // Purpose: Executes provider-specific migration SQL inside the schema migration transaction.
    private async Task ExecuteProviderSqlAsync(MauiSchemaOperationDefinition operation, CancellationToken cancellationToken)
    {
        var sql = IsSqlite()
            ? operation.SqliteSql
            : IsSqlServer()
                ? operation.SqlServerSql
                : throw UnsupportedProvider();

        if (string.IsNullOrWhiteSpace(sql))
            throw new InvalidOperationException($"ExecuteSql does not define SQL for metadata provider '{_db.Database.ProviderName}'.");

        await _db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }

    // Purpose: Builds column definition.
    private string BuildColumnDefinition(MauiSchemaColumnDefinition column, bool includePrimaryKey)
    {
        var name = RequireIdentifier(column.Name, "Column.Name");
        var builder = new StringBuilder();
        builder.Append(Quote(name)).Append(' ').Append(MapColumnType(column));

        if (includePrimaryKey && column.PrimaryKey)
        {
            if (column.AutoIncrement)
            {
                if (IsSqlite()) builder.Append(" PRIMARY KEY AUTOINCREMENT");
                else if (IsSqlServer()) builder.Append(" IDENTITY(1,1) PRIMARY KEY");
                else throw UnsupportedProvider();
            }
            else
            {
                builder.Append(" PRIMARY KEY");
            }
        }
        else
        {
            builder.Append(column.Nullable ? " NULL" : " NOT NULL");
        }

        if (column.DefaultValue is not null)
        {
            builder.Append(" DEFAULT ").Append(ToSqlLiteral(column.DefaultValue));
        }

        return builder.ToString();
    }

    // Purpose: Maps column type.
    private string MapColumnType(MauiSchemaColumnDefinition column)
    {
        var type = column.Type?.Trim() ?? string.Empty;
        if (type.Equals("String", StringComparison.OrdinalIgnoreCase) || type.Equals("Json", StringComparison.OrdinalIgnoreCase))
        {
            if (IsSqlite()) return "TEXT";
            if (IsSqlServer()) return column.Length is > 0 ? $"nvarchar({column.Length.Value})" : "nvarchar(max)";
        }
        if (type.Equals("Integer", StringComparison.OrdinalIgnoreCase)) return IsSqlite() ? "INTEGER" : "int";
        if (type.Equals("Long", StringComparison.OrdinalIgnoreCase)) return IsSqlite() ? "INTEGER" : "bigint";
        if (type.Equals("Boolean", StringComparison.OrdinalIgnoreCase)) return IsSqlite() ? "INTEGER" : "bit";
        if (type.Equals("DateTime", StringComparison.OrdinalIgnoreCase)) return IsSqlite() ? "TEXT" : "datetimeoffset";
        if (type.Equals("UtcDateTime", StringComparison.OrdinalIgnoreCase)) return IsSqlite() ? "TEXT" : "datetime2";
        if (type.Equals("Decimal", StringComparison.OrdinalIgnoreCase)) return IsSqlite() ? "NUMERIC" : "decimal(18,4)";
        if (type.Equals("Double", StringComparison.OrdinalIgnoreCase)) return IsSqlite() ? "REAL" : "float";
        if (type.Equals("Guid", StringComparison.OrdinalIgnoreCase)) return IsSqlite() ? "TEXT" : "uniqueidentifier";
        if (type.Equals("Binary", StringComparison.OrdinalIgnoreCase)) return IsSqlite() ? "BLOB" : "varbinary(max)";
        throw new InvalidOperationException($"Unsupported declarative column type '{column.Type}'.");
    }

    // Purpose: Maps delete behavior.
    private string MapDeleteBehavior(string? behavior)
    {
        var value = behavior?.Trim() ?? string.Empty;
        if (value.Length == 0 || value.Equals("NoAction", StringComparison.OrdinalIgnoreCase)) return "NO ACTION";
        if (value.Equals("Restrict", StringComparison.OrdinalIgnoreCase)) return IsSqlServer() ? "NO ACTION" : "RESTRICT";
        if (value.Equals("Cascade", StringComparison.OrdinalIgnoreCase)) return "CASCADE";
        if (value.Equals("SetNull", StringComparison.OrdinalIgnoreCase)) return "SET NULL";
        throw new InvalidOperationException($"Unsupported foreign-key delete behavior '{behavior}'.");
    }

    // Purpose: Converts the current value to SQL literal.
    private string ToSqlLiteral(JsonNode value)
    {
        if (value is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<bool>(out var boolean)) return boolean ? "1" : "0";
            if (jsonValue.TryGetValue<int>(out var integer)) return integer.ToString(CultureInfo.InvariantCulture);
            if (jsonValue.TryGetValue<long>(out var longValue)) return longValue.ToString(CultureInfo.InvariantCulture);
            if (jsonValue.TryGetValue<double>(out var doubleValue)) return doubleValue.ToString(CultureInfo.InvariantCulture);
            if (jsonValue.TryGetValue<decimal>(out var decimalValue)) return decimalValue.ToString(CultureInfo.InvariantCulture);
            if (jsonValue.TryGetValue<string>(out var text)) return $"'{(text ?? string.Empty).Replace("'", "''", StringComparison.Ordinal)}'";
        }
        throw new InvalidOperationException("Schema DefaultValue must be a JSON scalar value.");
    }

    // Purpose: Counts non-system tables in the selected metadata database.
    private async Task<int> CountUserTablesAsync(CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
            if (IsSqlite())
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
            else if (IsSqlServer())
                command.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped = 0;";
            else
                throw UnsupportedProvider();
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

    // Purpose: Returns whether the requested table exists.
    private async Task<bool> TableExistsAsync(string tableName, CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
            if (IsSqlite())
            {
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
                AddParameter(command, "$name", tableName);
            }
            else if (IsSqlServer())
            {
                command.CommandText = "SELECT CASE WHEN OBJECT_ID(@name, N'U') IS NULL THEN 0 ELSE 1 END;";
                AddParameter(command, "@name", tableName);
            }
            else throw UnsupportedProvider();
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0;
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

    // Purpose: Returns whether the requested column exists.
    private async Task<bool> ColumnExistsAsync(string tableName, string columnName, CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
            if (IsSqlite())
            {
                command.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{tableName.Replace("'", "''", StringComparison.Ordinal)}') WHERE name = $name;";
                AddParameter(command, "$name", columnName);
            }
            else if (IsSqlServer())
            {
                command.CommandText = "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(@table) AND name = @column;";
                AddParameter(command, "@table", tableName);
                AddParameter(command, "@column", columnName);
            }
            else throw UnsupportedProvider();
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0;
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

    // Purpose: Returns whether the requested index exists.
    private async Task<bool> IndexExistsAsync(string tableName, string indexName, CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
            if (IsSqlite())
            {
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name=$name;";
                AddParameter(command, "$name", indexName);
            }
            else if (IsSqlServer())
            {
                command.CommandText = "SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(@table) AND name = @name;";
                AddParameter(command, "@table", tableName);
                AddParameter(command, "@name", indexName);
            }
            else throw UnsupportedProvider();
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0;
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

    // Purpose: Reads version.
    private async Task<int> ReadVersionAsync(CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = _db.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText = "SELECT Version FROM MauiSchemaVersion WHERE Id = 1";
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return value is null or DBNull ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }
    }

    // Purpose: Writes version.
    private async Task WriteVersionAsync(int version, CancellationToken cancellationToken)
    {
        if (IsSqlite())
        {
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO MauiSchemaVersion (Id, Version, UpdatedUtc)
                VALUES (1, {version}, {DateTimeOffset.UtcNow})
                ON CONFLICT(Id) DO UPDATE SET Version = excluded.Version, UpdatedUtc = excluded.UpdatedUtc;
                """, cancellationToken);
            return;
        }
        if (IsSqlServer())
        {
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                IF EXISTS (SELECT 1 FROM [MauiSchemaVersion] WHERE [Id] = 1)
                    UPDATE [MauiSchemaVersion] SET [Version] = {version}, [UpdatedUtc] = {DateTimeOffset.UtcNow} WHERE [Id] = 1;
                ELSE
                    INSERT INTO [MauiSchemaVersion] ([Id], [Version], [UpdatedUtc]) VALUES (1, {version}, {DateTimeOffset.UtcNow});
                """, cancellationToken);
            return;
        }
        throw UnsupportedProvider();
    }

    // Purpose: Adds parameter.
    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    // Purpose: Returns whether sqlite.
    private bool IsSqlite() => (_db.Database.ProviderName ?? string.Empty).Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
    // Purpose: Returns whether SQL server.
    private bool IsSqlServer() => (_db.Database.ProviderName ?? string.Empty).Contains("SqlServer", StringComparison.OrdinalIgnoreCase);

    // Purpose: Quotes an identifier for the active database provider.
    private string Quote(string identifier)
    {
        identifier = RequireIdentifier(identifier, nameof(identifier));
        return IsSqlServer() ? $"[{identifier}]" : $"\"{identifier}\"";
    }

    // Purpose: Returns the required identifier.
    private static string RequireIdentifier(string? value, string field)
    {
        var result = value?.Trim() ?? string.Empty;
        if (result.Length == 0) throw new InvalidOperationException($"Schema operation field '{field}' is required.");
        if (!result.All(ch => char.IsLetterOrDigit(ch) || ch == '_'))
            throw new InvalidOperationException($"Schema identifier '{result}' contains unsupported characters.");
        return result;
    }

    // Purpose: Builds the standard error for an unsupported metadata database provider.
    private InvalidOperationException UnsupportedProvider() =>
        new($"Unsupported metadata database provider '{_db.Database.ProviderName}'.");
}
