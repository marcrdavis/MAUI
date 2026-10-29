// Project: MAUI - MOVEit Automation UI
// File-based Factory Reset recovery tests. No external database or IIS server required.

using System.Text.Json;
using MoveItAutomationUI.Services.Lifecycle;

namespace MoveItAutomationUI.Tests.Lifecycle;

public sealed class MauiFactoryResetRecoveryTests
{
    [Fact]
    public void NoMarker_DoesNotDeleteExistingDataEvenWhenUnconfigured()
    {
        using var site = new TemporarySite("{}");
        site.WriteData("Backups/archive.zip", "existing");
        MauiFactoryResetRecovery.CompletePendingReset(site.Root);
        Assert.True(File.Exists(site.DataFile("Backups/archive.zip")));
    }

    [Fact]
    public void PreparedButNotCommitted_LeavesDataAndConfiguredAppSettingsIntact()
    {
        using var site = new TemporarySite("{\"MoveIt\":{\"Servers\":[]}}");
        site.WriteData("maui.db", "original sqlite");
        site.Prepare();
        MauiFactoryResetRecovery.CompletePendingReset(site.Root);
        Assert.True(File.Exists(site.DataFile("maui.db")));
        Assert.False(File.Exists(site.Marker));
        using var settings = JsonDocument.Parse(File.ReadAllText(site.Settings));
        Assert.True(settings.RootElement.TryGetProperty("MoveIt", out _));
    }

    [Fact]
    public void CommittedReset_DeletesDataIncludingKeysAndSqlite_LeavesExternalSqlServerUntouched()
    {
        using var site = new TemporarySite("{}");
        site.WriteData("maui.db", "sqlite");
        site.WriteData("maui.db-wal", "sqlite journal");
        site.WriteData("DataProtection-Keys/key.xml", "protected key");
        site.WriteData("Backups/backup.zip", "backup");
        site.WriteData("ConfigurationRecovery/appsettings.lastgood.json", "backup settings");
        site.Prepare();
        var externalDb = Path.Combine(site.Root, "external-sql-server-sentinel.txt");
        File.WriteAllText(externalDb, "SQL Server must remain unchanged");

        MauiFactoryResetRecovery.CompletePendingReset(site.Root);

        Assert.True(Directory.Exists(site.DataPath));
        Assert.Empty(Directory.EnumerateFileSystemEntries(site.DataPath));
        Assert.Equal("SQL Server must remain unchanged", File.ReadAllText(externalDb));
        Assert.Equal("{}", File.ReadAllText(site.Settings));
        // A subsequent restart has no marker and must not touch newly initialized data.
        site.WriteData("DataProtection-Keys/new-key.xml", "new key");
        MauiFactoryResetRecovery.CompletePendingReset(site.Root);
        Assert.True(File.Exists(site.DataFile("DataProtection-Keys/new-key.xml")));
    }

    [Fact]
    public void CommittedReset_RemovesRc2QuickTransferCleanupState()
    {
        using var site = new TemporarySite("{}");
        site.WriteData("quick-transfer-cleanup.json", "persisted RC2 cleanup state");
        site.WriteData("quick-transfer-cleanup.123.tmp", "stale temporary state");
        site.Prepare();

        MauiFactoryResetRecovery.CompletePendingReset(site.Root);

        Assert.Empty(Directory.EnumerateFileSystemEntries(site.DataPath));
        Assert.False(File.Exists(site.Marker));
    }

    [Fact]
    public void InterruptedCommittedPurge_ResumesOnNextStartupAndRemovesMarkerLast()
    {
        using var site = new TemporarySite("{}");
        site.WriteData("maui.db", "sqlite already deleted on first attempt");
        site.WriteData("Backups/archive.zip", "remaining backup");
        site.WriteData("DataProtection-Keys/key.xml", "remaining key");
        site.Prepare();
        File.Delete(site.DataFile("maui.db")); // Simulate a shutdown during the purge.

        MauiFactoryResetRecovery.CompletePendingReset(site.Root);

        Assert.Empty(Directory.EnumerateFileSystemEntries(site.DataPath));
        Assert.False(File.Exists(site.Marker));
        Assert.Equal("{}", File.ReadAllText(site.Settings));
    }

    [Fact]
    public void UnknownMarker_FailsClosedWithoutDeletingData()
    {
        using var site = new TemporarySite("{}");
        site.WriteData("maui.db", "preserve");
        File.WriteAllText(site.Marker, "not a MAUI marker");
        Assert.Throws<InvalidOperationException>(() => MauiFactoryResetRecovery.CompletePendingReset(site.Root));
        Assert.True(File.Exists(site.DataFile("maui.db")));
    }

    [Fact]
    public void NonEmptySettingsFromAnotherDeployment_PreventsUnintendedDataDeletion()
    {
        using var site = new TemporarySite("{\"MetadataDatabase\":{\"Provider\":\"SqlServer\"}}");
        site.WriteData("Backups/archive.zip", "keep");
        site.Prepare();
        MauiFactoryResetRecovery.CompletePendingReset(site.Root);
        Assert.True(File.Exists(site.DataFile("Backups/archive.zip")));
        Assert.False(File.Exists(site.Marker));
    }

    [Fact]
    public void InvalidSettings_DoesNotDeleteDataOrMarker()
    {
        using var site = new TemporarySite("{ not valid json");
        site.Prepare();
        site.WriteData("maui.db", "preserve");
        Assert.ThrowsAny<JsonException>(() => MauiFactoryResetRecovery.CompletePendingReset(site.Root));
        Assert.True(File.Exists(site.DataFile("maui.db")));
        Assert.True(File.Exists(site.Marker));
    }

    [Theory]
    [InlineData("reset")]
    [InlineData("RESET ")]
    [InlineData("")]
    public void ConfirmationGuard_RequiresExactWord(string invalid)
    {
        Assert.NotEqual(MauiFactoryResetService.RequiredConfirmation, invalid);
    }

    private sealed class TemporarySite : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"maui-factory-reset-{Guid.NewGuid():N}");
        public string Settings => Path.Combine(Root, "appsettings.json");
        public string DataPath => Path.Combine(Root, "Data");
        public string Marker => MauiFactoryResetRecovery.MarkerPath(Root);

        public TemporarySite(string configuration)
        {
            Directory.CreateDirectory(DataPath);
            File.WriteAllText(Settings, configuration);
        }

        public string DataFile(string relative) => Path.Combine(DataPath, relative.Replace('/', Path.DirectorySeparatorChar));
        public void WriteData(string relative, string content)
        {
            var path = DataFile(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }
        public void Prepare() => File.WriteAllText(Marker, MauiFactoryResetRecovery.MarkerContents);

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
