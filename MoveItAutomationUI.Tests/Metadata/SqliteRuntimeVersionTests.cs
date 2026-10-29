using Microsoft.Data.Sqlite;

namespace MoveItAutomationUI.Tests.Metadata;

public sealed class SqliteRuntimeVersionTests
{
    [Fact]
    public void BundledSqliteRuntime_IsAtLeastSecurityFixedVersion()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "select sqlite_version();";
        var raw = Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

        Assert.True(Version.TryParse(raw, out var parsed), $"Unable to parse bundled SQLite runtime version '{raw}'.");
        var actual = parsed ?? throw new InvalidOperationException($"Unable to parse bundled SQLite runtime version '{raw}'.");
        Assert.True(actual.CompareTo(new Version(3, 50, 2)) >= 0, $"Bundled SQLite runtime {actual} is older than security-fixed SQLite 3.50.2.");
    }
}
