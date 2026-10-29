using MoveItAutomationUI.Services.Metadata;

namespace MoveItAutomationUI.Tests.Metadata;

public sealed class MetadataDatabaseProviderTests
{
    [Theory]
    [InlineData("SQLite", "SQLite")]
    [InlineData(" sqlite ", "SQLite")]
    [InlineData("SqlServer", "SqlServer")]
    [InlineData("sqlserver", "SqlServer")]
    public void Normalize_ReturnsCanonicalProvider(string input, string expected)
        => Assert.Equal(expected, MetadataDatabaseProvider.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("PostgreSQL")]
    public void Normalize_RejectsUnsupportedProvider(string? input)
        => Assert.Throws<ArgumentException>(() => MetadataDatabaseProvider.Normalize(input));
}
