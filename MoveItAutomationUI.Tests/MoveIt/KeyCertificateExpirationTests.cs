using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class KeyCertificateExpirationTests
{
    [Theory]
    [InlineData(true, "2099-12-31")]
    [InlineData(false, "2000-01-01")]
    public void IsExpired_UsesNativePgpResultWhenProvided(bool expired, string expiration)
    {
        var item = new AutomationKeyCertificate { Kind = KeyCertificateKind.Pgp, ExpirationDate = expiration, NativeIsExpired = expired };
        Assert.Equal(expired, KeyCertificateExpiration.IsExpired(item));
    }

    [Fact]
    public void IsExpired_ReturnsTrueForOldTlsCertificate()
    {
        var item = new AutomationKeyCertificate { Kind = KeyCertificateKind.Tls, ExpirationDate = "2000-01-01" };
        Assert.True(KeyCertificateExpiration.IsExpired(item));
    }

    [Fact]
    public void IsExpired_ReturnsFalseForFuturePgpKey()
    {
        var item = new AutomationKeyCertificate { Kind = KeyCertificateKind.Pgp, ExpirationDate = "2099-12-31" };
        Assert.False(KeyCertificateExpiration.IsExpired(item));
    }

    [Fact]
    public void IsExpired_DoesNotEvaluateSshKeys()
    {
        var item = new AutomationKeyCertificate { Kind = KeyCertificateKind.Ssh, ExpirationDate = "2000-01-01" };
        Assert.False(KeyCertificateExpiration.IsExpired(item));
    }

    [Fact]
    public void IsExpired_TreatsInvalidExpirationAsUnknownNotExpired()
    {
        var item = new AutomationKeyCertificate { Kind = KeyCertificateKind.Tls, ExpirationDate = "not-a-date" };
        Assert.False(KeyCertificateExpiration.IsExpired(item));
    }
}
