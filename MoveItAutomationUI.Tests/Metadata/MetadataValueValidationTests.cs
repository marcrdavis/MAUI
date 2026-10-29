using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;

namespace MoveItAutomationUI.Tests.Metadata;

public sealed class MetadataValueValidationTests
{
    [Fact]
    public void TryNormalizeEmailList_NormalizesCommaAndSemicolonSeparators()
    {
        var ok = MetadataValueValidation.TryNormalizeEmailList("a@example.com, b@example.com;c@example.com", out var normalized);
        Assert.True(ok);
        Assert.Equal("a@example.com; b@example.com; c@example.com", normalized);
    }

    [Fact]
    public void TryNormalizeEmailList_AllowsBlankValue()
    {
        Assert.True(MetadataValueValidation.TryNormalizeEmailList(" ", out var normalized));
        Assert.Equal(string.Empty, normalized);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("Display Name <user@example.com>")]
    public void TryNormalizeEmailList_RejectsNonPlainEmailAddresses(string input)
    {
        Assert.False(MetadataValueValidation.TryNormalizeEmailList(input, out var normalized));
        Assert.Equal(string.Empty, normalized);
    }

    [Fact]
    public void NormalizeValue_RequiresRequiredEmailField()
    {
        var field = EmailField(required: true);
        Assert.Throws<ArgumentException>(() => MetadataValueValidation.NormalizeValue(field, ""));
    }

    [Fact]
    public void NormalizeValue_AllowsEmptyOptionalEmailField()
    {
        var field = EmailField(required: false);
        Assert.Equal(string.Empty, MetadataValueValidation.NormalizeValue(field, ""));
    }

    [Fact]
    public void NormalizeValue_PreservesNonEmailValue()
    {
        var field = new MetadataFieldDefinition { Name = "Region", Type = "String" };
        Assert.Equal("  East  ", MetadataValueValidation.NormalizeValue(field, "  East  "));
    }

    private static MetadataFieldDefinition EmailField(bool required)
        => new() { Name = "OwnerEmail", Label = "Owner Email", Type = "Email", Required = required };
}
