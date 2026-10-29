using Microsoft.Data.Sqlite;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;

namespace MoveItAutomationUI.Tests.Metadata;

public sealed class MetadataDatabaseSafetyTests
{
    [Fact]
    public void Resolve_ReturnsBlankConnectionStringUnchanged()
        => Assert.Equal(string.Empty, MetadataDatabaseConnectionResolver.Resolve(SetupDatabaseProviders.SQLite, "   ", Path.GetTempPath()));

    [Fact]
    public void Resolve_LeavesSqlServerConnectionStringAsNonSqliteInput()
    {
        const string connection = "Server=db01;Database=MAUI;Integrated Security=true";
        Assert.Equal(connection, MetadataDatabaseConnectionResolver.Resolve(SetupDatabaseProviders.SqlServer, connection, Path.GetTempPath()));
    }

    [Fact]
    public void Resolve_ResolvesRelativeSqlitePathUnderContentRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "maui-tests", Guid.NewGuid().ToString("N"));
        var resolved = MetadataDatabaseConnectionResolver.Resolve(SetupDatabaseProviders.SQLite, "Data Source=Data/maui.db", root);
        var builder = new SqliteConnectionStringBuilder(resolved);

        Assert.Equal(Path.GetFullPath(Path.Combine(root, "Data", "maui.db")), builder.DataSource);
    }

    [Fact]
    public void Resolve_PreservesSqliteMemoryDatabase()
    {
        var resolved = MetadataDatabaseConnectionResolver.Resolve(SetupDatabaseProviders.SQLite, "Data Source=:memory:", Path.GetTempPath());
        var builder = new SqliteConnectionStringBuilder(resolved);
        Assert.Equal(":memory:", builder.DataSource);
    }

    [Fact]
    public void Resolve_RejectsDatabaseUnderWebRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "maui-tests", Guid.NewGuid().ToString("N"));
        var webRoot = Path.Combine(root, "wwwroot");
        Assert.Throws<InvalidOperationException>(() => MetadataDatabaseConnectionResolver.Resolve(
            SetupDatabaseProviders.SQLite,
            "Data Source=wwwroot/maui.db",
            root,
            webRoot));
    }


    [Theory]
    [InlineData("Data Source=file:wwwroot/probe.txt")]
    [InlineData("Data Source=file:C:/inetpub/wwwroot/probe.txt")]
    [InlineData("Data Source=file:relative.db")]
    public void Resolve_RejectsPersistentSqliteFileUris(string connectionString)
    {
        var root = Path.Combine(Path.GetTempPath(), "maui-tests", Guid.NewGuid().ToString("N"));
        var webRoot = Path.Combine(root, "wwwroot");
        var ex = Assert.Throws<InvalidOperationException>(() => MetadataDatabaseConnectionResolver.Resolve(
            SetupDatabaseProviders.SQLite,
            connectionString,
            root,
            webRoot));
        Assert.Contains("file:", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TargetGuard_AllowsEmptyDatabase()
        => MetadataDatabaseTargetGuard.EnsureSafeForMauiInitialization(new DatabaseConnectionTestResult { IsEmpty = true });

    [Fact]
    public void TargetGuard_AllowsExistingMauiDatabase()
        => MetadataDatabaseTargetGuard.EnsureSafeForMauiInitialization(new DatabaseConnectionTestResult { IsMauiDatabase = true, UserTableCount = 12 });

    [Fact]
    public void TargetGuard_RejectsPopulatedNonMauiDatabase()
    {
        var result = new DatabaseConnectionTestResult { IsEmpty = false, IsMauiDatabase = false, UserTableCount = 4 };
        var exception = Assert.Throws<InvalidOperationException>(() => MetadataDatabaseTargetGuard.EnsureSafeForMauiInitialization(result));
        Assert.Contains("4 user table", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
