// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using Microsoft.EntityFrameworkCore;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.Metadata;

public sealed class MetadataDbContext : DbContext
{
    // Initializes the metadata EF Core context with its configured database options.
    public MetadataDbContext(DbContextOptions<MetadataDbContext> options) : base(options) { }

    public DbSet<MetadataObjectEntity> MetadataObjects => Set<MetadataObjectEntity>();
    public DbSet<MetadataValueEntity> MetadataValues => Set<MetadataValueEntity>();
    public DbSet<MetadataFolderEntity> MetadataFolders => Set<MetadataFolderEntity>();
    public DbSet<ConfigurationUndoSnapshotEntity> ConfigurationUndoSnapshots => Set<ConfigurationUndoSnapshotEntity>();
    public DbSet<RecycleBinItemEntity> RecycleBinItems => Set<RecycleBinItemEntity>();
    public DbSet<UserPreferenceEntity> UserPreferences => Set<UserPreferenceEntity>();
    public DbSet<UserGridPreferenceEntity> UserGridPreferences => Set<UserGridPreferenceEntity>();
    public DbSet<SavedViewEntity> SavedViews => Set<SavedViewEntity>();
    public DbSet<OutboundReportEntity> OutboundReports => Set<OutboundReportEntity>();
    public DbSet<OutboundReportHistoryEntity> OutboundReportHistory => Set<OutboundReportHistoryEntity>();
    public DbSet<ApplicationLogEntity> ApplicationLogs => Set<ApplicationLogEntity>();
    public DbSet<SecurityUserEntity> SecurityUsers => Set<SecurityUserEntity>();

    // Defines environment-scoped keys, relationships, and uniqueness constraints for provider-neutral metadata storage.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MetadataObjectEntity>(entity =>
        {
            entity.HasKey(item => new { item.EnvironmentKey, item.ObjectType, item.ObjectId });
            entity.Property(item => item.EnvironmentKey).HasMaxLength(MoveItEnvironmentKey.MaxLength);
            entity.Property(item => item.ObjectType).HasMaxLength(64);
            entity.Property(item => item.ObjectId).HasMaxLength(256);
            entity.HasOne<MetadataFolderEntity>()
                .WithMany()
                .HasForeignKey(item => item.FolderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MetadataValueEntity>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.EnvironmentKey).HasMaxLength(MoveItEnvironmentKey.MaxLength);
            entity.Property(item => item.ObjectType).HasMaxLength(64);
            entity.Property(item => item.ObjectId).HasMaxLength(256);
            entity.Property(item => item.FieldName).HasMaxLength(128);
            entity.HasIndex(item => new { item.EnvironmentKey, item.ObjectType, item.ObjectId, item.FieldName }).IsUnique();
        });

        modelBuilder.Entity<MetadataFolderEntity>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.EnvironmentKey).HasMaxLength(MoveItEnvironmentKey.MaxLength);
            entity.Property(item => item.ObjectType).HasMaxLength(64);
            entity.Property(item => item.Name).HasMaxLength(200);
            entity.HasIndex(item => new { item.EnvironmentKey, item.ObjectType, item.ParentId, item.Name }).IsUnique();
            entity.HasOne<MetadataFolderEntity>()
                .WithMany()
                .HasForeignKey(item => item.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ConfigurationUndoSnapshotEntity>(entity =>
        {
            entity.HasKey(item => new { item.EnvironmentKey, item.ObjectType, item.ObjectId, item.UsernameKey });
            entity.Property(item => item.EnvironmentKey).HasMaxLength(MoveItEnvironmentKey.MaxLength);
            entity.Property(item => item.ObjectType).HasMaxLength(64);
            entity.Property(item => item.ObjectId).HasMaxLength(256);
            entity.Property(item => item.UsernameKey).HasMaxLength(128);
            entity.Property(item => item.ObjectName).HasMaxLength(254);
            entity.Property(item => item.CreatedBy).HasMaxLength(128);
            entity.Property(item => item.ObjectXml).IsRequired();
            entity.HasIndex(item => new { item.EnvironmentKey, item.ObjectType, item.CreatedUtc });
        });

        modelBuilder.Entity<RecycleBinItemEntity>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(36);
            entity.Property(item => item.EnvironmentKey).HasMaxLength(MoveItEnvironmentKey.MaxLength);
            entity.Property(item => item.ObjectType).HasMaxLength(64);
            entity.Property(item => item.OriginalObjectId).HasMaxLength(256);
            entity.Property(item => item.ObjectName).HasMaxLength(254);
            entity.Property(item => item.DeletedBy).HasMaxLength(128);
            entity.Property(item => item.DeletedByKey).HasMaxLength(128);
            entity.Property(item => item.ObjectXml).IsRequired();
            entity.HasIndex(item => new { item.EnvironmentKey, item.DeletedUtc });
            entity.HasIndex(item => new { item.EnvironmentKey, item.DeletedByKey, item.DeletedUtc });
        });

        modelBuilder.Entity<UserPreferenceEntity>(entity =>
        {
            entity.HasKey(item => new { item.EnvironmentKey, item.UsernameKey });
            entity.Property(item => item.EnvironmentKey).HasMaxLength(MoveItEnvironmentKey.MaxLength);
            entity.Property(item => item.UsernameKey).HasMaxLength(128);
            entity.Property(item => item.Username).HasMaxLength(128);
            entity.Property(item => item.Theme).HasMaxLength(16);
            entity.Property(item => item.StartPage).HasMaxLength(32);
            entity.HasIndex(item => item.Username);
        });

        modelBuilder.Entity<UserGridPreferenceEntity>(entity =>
        {
            entity.HasKey(item => new { item.EnvironmentKey, item.UsernameKey, item.GridKey });
            entity.Property(item => item.EnvironmentKey).HasMaxLength(MoveItEnvironmentKey.MaxLength);
            entity.Property(item => item.UsernameKey).HasMaxLength(128);
            entity.Property(item => item.Username).HasMaxLength(128);
            entity.Property(item => item.GridKey).HasMaxLength(64);
            entity.Property(item => item.LayoutJson).IsRequired();
            entity.HasIndex(item => new { item.EnvironmentKey, item.Username });
        });

        modelBuilder.Entity<SavedViewEntity>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(36);
            entity.Property(item => item.EnvironmentKey).HasMaxLength(MoveItEnvironmentKey.MaxLength);
            entity.Property(item => item.UsernameKey).HasMaxLength(128);
            entity.Property(item => item.Username).HasMaxLength(128);
            entity.Property(item => item.GridKey).HasMaxLength(64);
            entity.Property(item => item.ViewName).HasMaxLength(100);
            entity.Property(item => item.ViewNameKey).HasMaxLength(100);
            entity.Property(item => item.Description).HasMaxLength(500);
            entity.Property(item => item.ViewStateJson).IsRequired();
            entity.HasIndex(item => new { item.EnvironmentKey, item.UsernameKey, item.ViewNameKey }).IsUnique();
            entity.HasIndex(item => new { item.EnvironmentKey, item.UsernameKey, item.GridKey, item.ViewName });
        });


        modelBuilder.Entity<OutboundReportEntity>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasMaxLength(36);
            entity.Property(item => item.EnvironmentKey).HasMaxLength(MoveItEnvironmentKey.MaxLength);
            entity.Property(item => item.OwnerUsernameKey).HasMaxLength(128);
            entity.Property(item => item.OwnerUsername).HasMaxLength(128);
            entity.Property(item => item.GridKey).HasMaxLength(64);
            entity.Property(item => item.Title).HasMaxLength(200);
            entity.Property(item => item.Description).HasMaxLength(1000);
            entity.Property(item => item.Recipients).HasMaxLength(4000);
            entity.Property(item => item.ViewStateJson).IsRequired();
            entity.Property(item => item.SourceContextJson).IsRequired();
            entity.Property(item => item.ScheduleJson).IsRequired();
            entity.Property(item => item.LastStatus).HasMaxLength(32);
            entity.Property(item => item.LastMessage).HasMaxLength(2000);
            entity.HasIndex(item => new { item.EnvironmentKey, item.Enabled, item.NextRunUtc });
            entity.HasIndex(item => new { item.EnvironmentKey, item.OwnerUsernameKey, item.Title });
        });

        modelBuilder.Entity<OutboundReportHistoryEntity>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ReportId).HasMaxLength(36);
            entity.Property(item => item.EnvironmentKey).HasMaxLength(MoveItEnvironmentKey.MaxLength);
            entity.Property(item => item.Title).HasMaxLength(200);
            entity.Property(item => item.Status).HasMaxLength(32);
            entity.Property(item => item.Message).HasMaxLength(2000);
            entity.HasIndex(item => item.StartedUtc);
            entity.HasIndex(item => new { item.ReportId, item.StartedUtc });
        });

        modelBuilder.Entity<ApplicationLogEntity>(entity =>
        {
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Level).HasMaxLength(16);
            entity.Property(item => item.Category).HasMaxLength(256);
            entity.Property(item => item.EventName).HasMaxLength(128);
            entity.Property(item => item.Username).HasMaxLength(128);
            entity.Property(item => item.EnvironmentKey).HasMaxLength(MoveItEnvironmentKey.MaxLength);
            entity.Property(item => item.RequestPath).HasMaxLength(512);
            entity.Property(item => item.TraceId).HasMaxLength(64);
            entity.HasIndex(item => item.TimestampUtc);
            entity.HasIndex(item => new { item.Level, item.TimestampUtc });
            entity.HasIndex(item => new { item.EnvironmentKey, item.TimestampUtc });
        });

        modelBuilder.Entity<SecurityUserEntity>(entity =>
        {
            entity.HasKey(item => item.UsernameKey);
            entity.Property(item => item.UsernameKey).HasMaxLength(128);
            entity.Property(item => item.Username).HasMaxLength(128);
            entity.Property(item => item.Role).HasMaxLength(16);
            entity.Property(item => item.CreatedBy).HasMaxLength(128);
            entity.Property(item => item.ModifiedBy).HasMaxLength(128);
            entity.HasIndex(item => item.Role);
        });
    }
}

public sealed class MetadataObjectEntity
{
    public string EnvironmentKey { get; set; } = string.Empty;
    public string ObjectType { get; set; } = string.Empty;
    public string ObjectId { get; set; } = string.Empty;
    public int? FolderId { get; set; }
    public DateTimeOffset? MetadataCreatedUtc { get; set; }
    public string MetadataCreatedBy { get; set; } = string.Empty;
    public DateTimeOffset? MetadataModifiedUtc { get; set; }
    public string MetadataModifiedBy { get; set; } = string.Empty;
    public DateTimeOffset? ConfigurationModifiedUtc { get; set; }
    public string ConfigurationModifiedBy { get; set; } = string.Empty;
}

public sealed class MetadataValueEntity
{
    public long Id { get; set; }
    public string EnvironmentKey { get; set; } = string.Empty;
    public string ObjectType { get; set; } = string.Empty;
    public string ObjectId { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed class MetadataFolderEntity
{
    public int Id { get; set; }
    public string EnvironmentKey { get; set; } = string.Empty;
    public string ObjectType { get; set; } = string.Empty;
    public int? ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class ConfigurationUndoSnapshotEntity
{
    public string EnvironmentKey { get; set; } = string.Empty;
    public string ObjectType { get; set; } = string.Empty;
    public string ObjectId { get; set; } = string.Empty;
    public string UsernameKey { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public string ObjectXml { get; set; } = string.Empty;
}

public sealed class RecycleBinItemEntity
{
    public string Id { get; set; } = string.Empty;
    public string EnvironmentKey { get; set; } = string.Empty;
    public string ObjectType { get; set; } = string.Empty;
    public string OriginalObjectId { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public string DeletedBy { get; set; } = string.Empty;
    public string DeletedByKey { get; set; } = string.Empty;
    public DateTimeOffset DeletedUtc { get; set; }
    public string ObjectXml { get; set; } = string.Empty;
}

public sealed class UserPreferenceEntity
{
    public string EnvironmentKey { get; set; } = string.Empty;
    public string UsernameKey { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Theme { get; set; } = UserPreferenceThemes.Light;
    public string StartPage { get; set; } = UserPreferenceStartPages.Dashboard;
    public int GridPageSize { get; set; } = 50;
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ModifiedUtc { get; set; }
}


public sealed class UserGridPreferenceEntity
{
    public string EnvironmentKey { get; set; } = string.Empty;
    public string UsernameKey { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string GridKey { get; set; } = string.Empty;
    public string LayoutJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ModifiedUtc { get; set; }
}

public sealed class SavedViewEntity
{
    public string Id { get; set; } = string.Empty;
    public string EnvironmentKey { get; set; } = string.Empty;
    public string UsernameKey { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string GridKey { get; set; } = string.Empty;
    public string ViewName { get; set; } = string.Empty;
    public string ViewNameKey { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ViewStateJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ModifiedUtc { get; set; }
}

public sealed class ApplicationLogEntity
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int EventId { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string EnvironmentKey { get; set; } = string.Empty;
    public string RequestPath { get; set; } = string.Empty;
    public string TraceId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Exception { get; set; } = string.Empty;
    public string PropertiesJson { get; set; } = string.Empty;
}


public sealed class SecurityUserEntity
{
    public string UsernameKey { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = MauiSecurityRoles.User;
    public DateTimeOffset? LastLoginUtc { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset ModifiedUtc { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;
}

public sealed class OutboundReportEntity
{
    public string Id { get; set; } = string.Empty;
    public string EnvironmentKey { get; set; } = string.Empty;
    public string OwnerUsernameKey { get; set; } = string.Empty;
    public string OwnerUsername { get; set; } = string.Empty;
    public string GridKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Recipients { get; set; } = string.Empty;
    public string ViewStateJson { get; set; } = string.Empty;
    public string SourceContextJson { get; set; } = string.Empty;
    public string ScheduleJson { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public DateTimeOffset? NextRunUtc { get; set; }
    public DateTimeOffset? LastRunUtc { get; set; }
    public string LastStatus { get; set; } = string.Empty;
    public string LastMessage { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ModifiedUtc { get; set; }
}

public sealed class OutboundReportHistoryEntity
{
    public long Id { get; set; }
    public string ReportId { get; set; } = string.Empty;
    public string EnvironmentKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset CompletedUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public int RowCount { get; set; }
    public int RecipientCount { get; set; }
    public string Message { get; set; } = string.Empty;
}

