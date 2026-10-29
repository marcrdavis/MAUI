// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Seeds the standard MAUI Tag registry only for a pristine metadata database during Initial Setup.

using Microsoft.EntityFrameworkCore;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.Metadata;

public interface IInitialTagSeedService
{
    Task<int> SeedDefaultsIfPristineAsync(
        string provider,
        string connectionString,
        string environmentKey,
        CancellationToken cancellationToken = default);
}

public sealed class InitialTagSeedService : IInitialTagSeedService
{
    private static readonly IReadOnlyList<(string Name, string Color)> DefaultTags = new[]
    {
        ("DEV", "#2563EB"),
        ("QA", "#7C3AED"),
        ("STAGING", "#D97706"),
        ("PROD", "#DC2626"),
        ("Inbound", "#0F766E"),
        ("Outbound", "#EA580C"),
        ("Internal", "#475569"),
        ("External", "#0891B2"),
        ("GDPR", "#9F1239"),
        ("CCPA", "#4338CA")
    };

    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<InitialTagSeedService> _logger;

    public InitialTagSeedService(IWebHostEnvironment environment, ILogger<InitialTagSeedService> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public async Task<int> SeedDefaultsIfPristineAsync(
        string provider,
        string connectionString,
        string environmentKey,
        CancellationToken cancellationToken = default)
    {
        var scope = MoveItEnvironmentKey.Normalize(environmentKey);
        var optionsBuilder = new DbContextOptionsBuilder<MetadataDbContext>();
        MetadataDatabaseProvider.Configure(
            optionsBuilder,
            provider,
            connectionString,
            _environment.ContentRootPath,
            _environment.WebRootPath,
            ensureSqliteDirectory: true);

        await using var db = new MetadataDbContext(optionsBuilder.Options);

        // Setup can be retried after the database was provisioned but before appsettings.json was
        // successfully written. Existing Tag definitions mean the seed has already run; do not
        // duplicate or overwrite them.
        if (await db.MetadataObjects.AsNoTracking().AnyAsync(item =>
                item.EnvironmentKey == scope && item.ObjectType == TagRepository.TagObjectType,
                cancellationToken))
        {
            return 0;
        }

        // Defaults belong only to a genuinely new/unused MAUI metadata database. A database that
        // already contains MAUI data may have intentionally managed its own Tag registry and must
        // never be modified as a side effect of attaching a new application installation to it.
        if (await HasExistingMauiDataAsync(db, cancellationToken))
        {
            _logger.LogInformation(
                "Initial Tag seed skipped for environment {EnvironmentKey} because the MAUI metadata database already contains application data.",
                scope);
            return 0;
        }

        var now = DateTimeOffset.UtcNow;
        const string createdBy = "Initial Setup";
        foreach (var (name, color) in DefaultTags)
        {
            var id = Guid.NewGuid().ToString("N");
            db.MetadataObjects.Add(new MetadataObjectEntity
            {
                EnvironmentKey = scope,
                ObjectType = TagRepository.TagObjectType,
                ObjectId = id,
                MetadataCreatedUtc = now,
                MetadataCreatedBy = createdBy,
                MetadataModifiedUtc = now,
                MetadataModifiedBy = createdBy
            });
            db.MetadataValues.AddRange(
                new MetadataValueEntity
                {
                    EnvironmentKey = scope,
                    ObjectType = TagRepository.TagObjectType,
                    ObjectId = id,
                    FieldName = TagRepository.NameField,
                    Value = name
                },
                new MetadataValueEntity
                {
                    EnvironmentKey = scope,
                    ObjectType = TagRepository.TagObjectType,
                    ObjectId = id,
                    FieldName = TagRepository.ColorField,
                    Value = color
                });
        }

        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Seeded {TagCount} default MAUI Tags for new environment {EnvironmentKey} during Initial Setup.",
            DefaultTags.Count,
            scope);
        return DefaultTags.Count;
    }

    private static async Task<bool> HasExistingMauiDataAsync(MetadataDbContext db, CancellationToken cancellationToken)
    {
        if (await db.MetadataObjects.AsNoTracking().AnyAsync(cancellationToken)) return true;
        if (await db.MetadataValues.AsNoTracking().AnyAsync(cancellationToken)) return true;
        if (await db.MetadataFolders.AsNoTracking().AnyAsync(cancellationToken)) return true;
        if (await db.ConfigurationUndoSnapshots.AsNoTracking().AnyAsync(cancellationToken)) return true;
        if (await db.RecycleBinItems.AsNoTracking().AnyAsync(cancellationToken)) return true;
        if (await db.UserPreferences.AsNoTracking().AnyAsync(cancellationToken)) return true;
        if (await db.UserGridPreferences.AsNoTracking().AnyAsync(cancellationToken)) return true;
        if (await db.SavedViews.AsNoTracking().AnyAsync(cancellationToken)) return true;
        if (await db.OutboundReports.AsNoTracking().AnyAsync(cancellationToken)) return true;
        if (await db.OutboundReportHistory.AsNoTracking().AnyAsync(cancellationToken)) return true;
        if (await db.ApplicationLogs.AsNoTracking().AnyAsync(cancellationToken)) return true;
        if (await db.SecurityUsers.AsNoTracking().AnyAsync(cancellationToken)) return true;
        return false;
    }
}
