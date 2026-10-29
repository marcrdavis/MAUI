using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Tests.Configuration;

public sealed class MoveItEnvironmentKeyTests
{
    [Theory]
    [InlineData("prod", "PROD")]
    [InlineData("  Qa-East  ", "QA-EAST")]
    [InlineData("DEV_01", "DEV_01")]
    public void Normalize_TrimsAndUppercases(string input, string expected)
        => Assert.Equal(expected, MoveItEnvironmentKey.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_RejectsMissingValue(string? input)
        => Assert.Throws<ArgumentException>(() => MoveItEnvironmentKey.Normalize(input));

    [Fact]
    public void Normalize_RejectsValueOverMaximumLength()
        => Assert.Throws<ArgumentException>(() => MoveItEnvironmentKey.Normalize(new string('A', MoveItEnvironmentKey.MaxLength + 1)));

    [Fact]
    public void Resolve_UsesEnvironmentKeyWhenPresent()
    {
        var server = new MoveItServerDefinition { Key = "NODE41", EnvironmentKey = "prod" };
        Assert.Equal("PROD", MoveItEnvironmentKey.Resolve(server));
    }

    [Fact]
    public void Resolve_FallsBackToServerKey()
    {
        var server = new MoveItServerDefinition { Key = "NODE41", EnvironmentKey = " " };
        Assert.Equal("NODE41", MoveItEnvironmentKey.Resolve(server));
    }
}
