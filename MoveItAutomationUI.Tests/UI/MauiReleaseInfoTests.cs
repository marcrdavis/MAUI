using System.Reflection;
using System.Xml.Linq;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Tests.UI;

public sealed class MauiReleaseInfoTests
{
    [Theory]
    [InlineData("0.17.420+build.20261018", "20261018")]
    [InlineData("0.17.420+build.20261018.abcdef012345", "20261018")]
    [InlineData("0.17.420-RC2+build.20261018", "20261018")]
    [InlineData("0.17.420+build.20240229", "20240229")]
    [InlineData("0.17.420+build.20260229", "Unknown")]
    [InlineData("0.17.420+build.20261301", "Unknown")]
    [InlineData("0.17.420+build.20261018extra", "Unknown")]
    [InlineData("0.17.420+build.2026101", "Unknown")]
    [InlineData("0.17.420+build.", "Unknown")]
    [InlineData("0.17.420", "Unknown")]
    [InlineData(null, "Unknown")]
    public void BuildReadsReleaseDateWithoutInventingADate(string? version, string expected)
        => Assert.Equal(expected, MauiReleaseInfo.ReadBuild(version));

    [Fact]
    public void DisplayedBuildMatchesTheCompiledProjectMetadata()
    {
        var information = typeof(MauiReleaseInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        Assert.NotNull(information);
        Assert.NotEqual("Unknown", MauiReleaseInfo.Build);
        Assert.Equal(MauiReleaseInfo.ReadBuild(information), MauiReleaseInfo.Build);

        // Check the actual project release input as well, so a stale displayed
        // constant cannot pass just because the test duplicated that constant.
        var root = FindSourceRoot();
        var project = XDocument.Load(Path.Combine(root, "MoveItAutomationUI.csproj"));
        var projectInformation = project.Descendants("InformationalVersion").Single().Value;
        Assert.Equal(MauiReleaseInfo.ReadBuild(projectInformation), MauiReleaseInfo.Build);
    }

    private static string FindSourceRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "MoveItAutomationUI.csproj"))) return directory.FullName;
        throw new DirectoryNotFoundException("MAUI source root not found.");
    }
}
