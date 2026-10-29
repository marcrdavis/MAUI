using System.Text;
using MoveItAutomationUI.Features.EnvironmentCopy;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class EnvironmentCopyPgpIdentityTests
{
    [Fact]
    public void PublicFixtureMatchesTheIndependentlyReportedGnuPgFingerprint()
    {
        // Public Ubuntu archive signing key only, exported from the system archive keyring.
        // Its full fingerprint was independently read with gpg --with-colons --list-keys.
        // No private key or user/environment credential is included in this fixture.
        Assert.Equal("790BC7277767219C42C86F933B4FE6ACC0B21F32",EnvironmentCopyKeyIdentity.PgpFingerprint(Fixture()));
    }
    [Fact]
    public void ArmorHeadersAndNewlineConventionsDoNotChangeKeyIdentity()
    {
        var text=Encoding.ASCII.GetString(Fixture()).Replace("\n\n","\nComment: identity regression\n\n",StringComparison.Ordinal).Replace("\n","\r\n",StringComparison.Ordinal);
        Assert.Equal(EnvironmentCopyKeyIdentity.PgpFingerprint(Fixture()),EnvironmentCopyKeyIdentity.PgpFingerprint(Encoding.ASCII.GetBytes(text)));
    }
    [Fact]
    public void VersionSixUsesTheRfcSpecifiedSha256PrefixAndLength()
    {
        // Public packet-format vector: v6, creation time 0, Ed25519, 32-byte public material.
        // Expected digest independently calculated over 9B || uint32be(42) || body.
        var packet=Convert.FromHexString("C62A06000000001B000000200102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F20");
        Assert.Equal("C24ACCC8098528F3F807A3D035A9768D2CF8484AD7D7765514BAABB2B638D616",EnvironmentCopyKeyIdentity.PgpFingerprint(packet));
    }
    [Theory]
    [InlineData("")]
    [InlineData("C60C040000")]
    [InlineData("C6E1040000000001")]
    [InlineData("C506040000000001")]
    [InlineData("C606030000000001")]
    public void TruncatedPartialSecretAndUnsupportedPacketsDoNotBecomeVerifiedIdentities(string hex)
        => Assert.Throws<InvalidOperationException>(()=>EnvironmentCopyKeyIdentity.PgpFingerprint(Convert.FromHexString(hex)));
    [Fact]
    public void MultipleArmoredKeysAreRejectedRatherThanSilentlyComparingOnlyOne()
    {
        var bytes=Fixture();Assert.Throws<InvalidOperationException>(()=>EnvironmentCopyKeyIdentity.PgpFingerprint(bytes.Concat(bytes).ToArray()));
    }
    private static byte[] Fixture()
    {
        using var stream=typeof(EnvironmentCopyPgpIdentityTests).Assembly.GetManifestResourceStream("MoveItAutomationUI.Tests.MoveIt.Fixtures.EnvironmentCopyPublicKey.asc")!;
        using var memory=new MemoryStream();stream.CopyTo(memory);return memory.ToArray();
    }
}
