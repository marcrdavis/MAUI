// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Verifies the shared Portable export passphrase policy used by native portability and full configuration exports.

using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class ExportPassphrasePolicyTests
{
    [Fact]
    public void ExactSixteenCharacterPassphraseMeetingAllClassesIsValid()
    {
        var error = PortabilitySupport.PassphrasePolicyError("Aa1!bcdefghijklm", "Export passphrase");

        Assert.Equal(string.Empty, error);
    }

    [Theory]
    [InlineData("Aa1!short", "at least 16 characters")]
    [InlineData("aa1!bcdefghijklmn", "uppercase")]
    [InlineData("AA1!BCDEFGHIJKLMN", "lowercase")]
    [InlineData("Aa!!bcdefghijklmn", "digit")]
    [InlineData("Aa11bcdefghijklmn", "special character")]
    public void MissingPolicyRequirementReturnsSpecificMessage(string passphrase, string expectedText)
    {
        var error = PortabilitySupport.PassphrasePolicyError(passphrase, "Export passphrase");

        Assert.Contains(expectedText, error, StringComparison.OrdinalIgnoreCase);
    }
}
