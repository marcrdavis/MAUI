// Project: MAUI - MOVEit Automation UI
// Upgrade restart requirements, persisted completion, and the existing restart endpoint.
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Backup;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.Lifecycle;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.Upgrade;

namespace MoveItAutomationUI.Tests.Lifecycle;

public sealed class MauiUpgradeRestartTests
{
    [Fact]
    public void ManifestRestartRequirementIsOptionalAndDefaultsToFalse()
    {
        Assert.False(new MauiUpgradeManifest().RestartRequired);
        Assert.False(JsonSerializer.Deserialize<MauiUpgradeManifest>("{}")!.RestartRequired);
        Assert.False(JsonSerializer.Deserialize<MauiUpgradeManifest>("{\"RestartRequired\":false}")!.RestartRequired);
        Assert.True(JsonSerializer.Deserialize<MauiUpgradeManifest>("{\"RestartRequired\":true}")!.RestartRequired);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task CompletedUpgradeCombinesManifestAndDetectedRestartRequirements(bool releaseRestart, bool configurationRestart)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new MetadataDbContext(new DbContextOptionsBuilder<MetadataDbContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE MauiSchemaVersion (Id INTEGER PRIMARY KEY, Version INTEGER NOT NULL); INSERT INTO MauiSchemaVersion VALUES (1, 1);");
        var manifest = new MauiUpgradeManifest { TargetVersion = "0.17.387", RestartRequired = releaseRestart };
        var state = new MauiUpgradeState();
        state.Initialize("0.17.386", manifest, 1, 1);
        var store = new MemoryStore();
        var service = new MauiUpgradeService(new ManifestProvider(manifest), state,
            new MauiDatabaseSchemaManager(db, new MauiDatabaseSchemaState(), NullLogger<MauiDatabaseSchemaManager>.Instance),
            new AppSettingsWriteCoordinator(), store, new UnusedBackup(), new RestartDetector(configurationRestart),
            NullLogger<MauiUpgradeService>.Instance);

        var result = await service.ExecuteAsync("admin", _ => Task.CompletedTask);
        var expectedRestart = releaseRestart || configurationRestart;
        Assert.Equal(expectedRestart, result.RestartRequired);
        Assert.Equal(expectedRestart, state.PendingRestart);
        Assert.Equal(expectedRestart, state.IsPending);
        Assert.Equal(expectedRestart, service.GetViewModel().PendingRestart);
        Assert.Equal((releaseRestart ? 1 : 0) + (configurationRestart ? 1 : 0), result.RestartReasons.Count);
        Assert.Equal("0.17.387", store.Saved![UiOptions.SectionName]!["Version"]!.GetValue<string>());
        Assert.Equal(1, store.Writes);

        var restart = new FakeRestart();
        var controller = new UpgradeController(service, state, restart, NullLogger<UpgradeController>.Instance);
        Assert.False(restart.IsRestartScheduled, "Completing the upgrade must not automatically restart the host.");
        if (expectedRestart)
        {
            Assert.IsType<OkObjectResult>(controller.Restart());
            Assert.True(restart.IsRestartScheduled);
            Assert.IsType<OkObjectResult>(controller.Restart());
            Assert.Equal(1, restart.Accepted);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync("admin", _ => Task.CompletedTask));
        }
        else
        {
            Assert.IsType<ConflictObjectResult>(controller.Restart());
            Assert.False(restart.IsRestartScheduled);
        }

        // The version was committed before restart; reloading the same release must not require another upgrade.
        var nextProcess = new MauiUpgradeState();
        nextProcess.Initialize("0.17.387", manifest, 1, 1);
        Assert.False(nextProcess.IsPending);
        Assert.False(nextProcess.PendingRestart);
    }

    private sealed class ManifestProvider(MauiUpgradeManifest manifest) : IMauiUpgradeManifestProvider
    {
        public MauiUpgradeManifest Manifest => manifest;
        public string ManifestPath => "upgrade.json";
    }

    private sealed class RestartDetector(bool required) : IConfigurationRestartDetector
    {
        public IReadOnlyList<string> GetRestartReasons(JsonObject root) => required ? new[] { "Startup configuration changed." } : Array.Empty<string>();
        public IReadOnlyList<string> GetRestartReasons(MoveItOptions moveIt, MetadataDatabaseOptions database) => throw new NotSupportedException();
    }

    private sealed class FakeRestart : IApplicationRestartService
    {
        public bool IsRestartScheduled { get; private set; }
        public int Accepted { get; private set; }
        public bool ScheduleRestart(string reason)
        {
            if (IsRestartScheduled) return false;
            IsRestartScheduled = true;
            Accepted++;
            return true;
        }
    }

    private sealed class MemoryStore : IAppSettingsDocumentStore
    {
        public string LivePath => Path.Combine(Path.GetTempPath(), "maui-upgrade-test", "appsettings.json");
        public string TemplatePath => throw new NotSupportedException();
        public bool LiveExists => true;
        public JsonObject? Saved { get; private set; }
        public int Writes { get; private set; }
        public JsonObject ReadTemplate() => throw new NotSupportedException();
        public JsonObject ReadLive() => new() { [UiOptions.SectionName] = new JsonObject { ["Version"] = "0.17.386" } };
        public Task<JsonObject> ReadLiveAsync(CancellationToken cancellationToken = default) => Task.FromResult(ReadLive());
        public Task<JsonObject> ReadSeedAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task WriteLiveAsync(JsonObject root, CancellationToken cancellationToken = default, bool publishRuntime = true)
        {
            Saved = root.DeepClone().AsObject();
            Writes++;
            return Task.CompletedTask;
        }
        public string CreateLiveBackup(string label) => LivePath + ".backup";
    }

    private sealed class UnusedBackup : IMauiMetadataDatabaseBackupService
    {
        public string Provider => SetupDatabaseProviders.SQLite;
        public bool IsSqlite => true;
        public Task BackupSqliteAsync(string destinationPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ValidateSqliteRestoreAsync(string sourcePath, string targetConnectionString, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RestoreSqliteAsync(string sourcePath, string targetConnectionString, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
