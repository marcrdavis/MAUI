// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Shared provider selection and first-run provisioning for the MAUI metadata database.

using Microsoft.EntityFrameworkCore;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Upgrade;

namespace MoveItAutomationUI.Services.Metadata;

public static class MetadataDatabaseProvider
{
    // Purpose: Normalizes and validates a supported metadata database provider name.
    public static string Normalize(string? provider)
    {
        var value = provider?.Trim() ?? string.Empty;
        if (value.Equals(SetupDatabaseProviders.SQLite, StringComparison.OrdinalIgnoreCase)) return SetupDatabaseProviders.SQLite;
        if (value.Equals(SetupDatabaseProviders.SqlServer, StringComparison.OrdinalIgnoreCase)) return SetupDatabaseProviders.SqlServer;
        throw new ArgumentException("Database Provider must be SQLite or SqlServer.");
    }

    // Purpose: Configures EF Core for the selected metadata provider using the shared connection resolver.
    public static void Configure(
        DbContextOptionsBuilder options,
        string provider,
        string connectionString,
        string contentRootPath,
        string? webRootPath,
        bool ensureSqliteDirectory)
    {
        var rawProvider = provider?.Trim() ?? string.Empty;
        try
        {
            provider = Normalize(rawProvider);
        }
        catch (ArgumentException ex)
        {
            // Preserve the startup/runtime configuration error contract used before this refactor.
            throw new InvalidOperationException($"Unsupported MetadataDatabase provider '{rawProvider}'. Use SQLite or SqlServer.", ex);
        }

        var runtimeConnectionString = MetadataDatabaseConnectionResolver.Resolve(
            provider,
            connectionString,
            contentRootPath,
            webRootPath,
            ensureSqliteDirectory);

        if (provider.Equals(SetupDatabaseProviders.SQLite, StringComparison.OrdinalIgnoreCase))
        {
            options.UseSqlite(runtimeConnectionString);
            return;
        }

        options.UseSqlServer(runtimeConnectionString);
    }
}

public static class MetadataDatabaseTargetGuard
{
    // Purpose: Rejects a populated database unless it carries MAUI's authoritative schema marker.
    public static void EnsureSafeForMauiInitialization(DatabaseConnectionTestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.SafeForMauiInitialization) return;

        throw new InvalidOperationException(
            $"The selected database contains {result.UserTableCount} user table(s) but does not appear to be a MAUI metadata database. " +
            "Choose an empty database or an existing MAUI metadata database.");
    }
}

public interface IMetadataDatabaseProvisioner
{
    // Purpose: Provisions an empty database to the manifest-required schema or validates an existing current database.
    Task<int> ProvisionRequiredSchemaAsync(string provider, string connectionString, CancellationToken cancellationToken = default);
}

public sealed class MetadataDatabaseProvisioner : IMetadataDatabaseProvisioner
{
    private readonly IWebHostEnvironment _environment;
    private readonly IMauiUpgradeManifestProvider _upgradeManifestProvider;
    private readonly ILoggerFactory _loggerFactory;

    // Purpose: Initializes metadata database provisioning with environment and release-manifest dependencies.
    public MetadataDatabaseProvisioner(
        IWebHostEnvironment environment,
        IMauiUpgradeManifestProvider upgradeManifestProvider,
        ILoggerFactory loggerFactory)
    {
        _environment = environment;
        _upgradeManifestProvider = upgradeManifestProvider;
        _loggerFactory = loggerFactory;
    }

    // Purpose: Provisions an empty database to the manifest-required schema or validates an existing current database.
    public async Task<int> ProvisionRequiredSchemaAsync(
        string provider,
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        provider = MetadataDatabaseProvider.Normalize(provider);

        var optionsBuilder = new DbContextOptionsBuilder<MetadataDbContext>();
        MetadataDatabaseProvider.Configure(
            optionsBuilder,
            provider,
            connectionString,
            _environment.ContentRootPath,
            _environment.WebRootPath,
            ensureSqliteDirectory: true);

        await using var db = new MetadataDbContext(optionsBuilder.Options);
        var schemaManager = new MauiDatabaseSchemaManager(
            db,
            new MauiDatabaseSchemaState(),
            _loggerFactory.CreateLogger<MauiDatabaseSchemaManager>());
        var manifest = _upgradeManifestProvider.Manifest;
        var inspection = await schemaManager.InspectAsync(manifest, cancellationToken);

        if (!inspection.IsEmpty && inspection.Version != manifest.RequiredSchemaVersion)
        {
            throw new InvalidOperationException(
                $"The selected MAUI database is schema version {inspection.Version}, but this installation requires schema version {manifest.RequiredSchemaVersion}. " +
                "Initial Setup will not upgrade an existing database; use an empty database or a database already at the required schema version.");
        }

        var finalVersion = inspection.IsEmpty
            ? await schemaManager.ApplyRequiredMigrationsAsync(manifest, cancellationToken)
            : inspection.Version;

        if (finalVersion != manifest.RequiredSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Database provisioning ended at schema version {finalVersion}; expected version {manifest.RequiredSchemaVersion}.");
        }

        await ValidateRuntimeMappingsAsync(db, cancellationToken);
        return finalVersion;
    }

    // Purpose: Exercises every runtime EF mapping against the provisioned physical schema.
    private static async Task ValidateRuntimeMappingsAsync(MetadataDbContext db, CancellationToken cancellationToken)
    {
        // Take(1) selects the mapped columns without requiring the tables to contain data.
        await db.MetadataFolders.AsNoTracking().Take(1).ToListAsync(cancellationToken);
        await db.MetadataObjects.AsNoTracking().Take(1).ToListAsync(cancellationToken);
        await db.MetadataValues.AsNoTracking().Take(1).ToListAsync(cancellationToken);
        await db.ConfigurationUndoSnapshots.AsNoTracking().Take(1).ToListAsync(cancellationToken);
        await db.RecycleBinItems.AsNoTracking().Take(1).ToListAsync(cancellationToken);
        await db.UserPreferences.AsNoTracking().Take(1).ToListAsync(cancellationToken);
        await db.UserGridPreferences.AsNoTracking().Take(1).ToListAsync(cancellationToken);
        await db.SecurityUsers.AsNoTracking().Take(1).ToListAsync(cancellationToken);
        await db.SavedViews.AsNoTracking().Take(1).ToListAsync(cancellationToken);
        await db.OutboundReports.AsNoTracking().Take(1).ToListAsync(cancellationToken);
        await db.OutboundReportHistory.AsNoTracking().Take(1).ToListAsync(cancellationToken);
    }
}
