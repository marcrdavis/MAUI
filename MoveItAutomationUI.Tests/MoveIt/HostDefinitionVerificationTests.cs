using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class HostDefinitionVerificationTests
{
    [Theory]
    [InlineData("Host", "new.example.com", "old.example.com")]
    [InlineData("DefPassword", "NEW_CIPHER", "OLD_CIPHER")]
    [InlineData("DefClientKeyID", "2", "1")]
    public void CreationRejectsUnpersistedConnectionAndCredentialFields(string field, string intended, string actual)
    {
        var error = Assert.Throws<InvalidOperationException>(() => HostDefinitionVerification.VerifyCreate(
            $"<FTP {field}=\"{intended}\" />", $"<FTP {field}=\"{actual}\" />"));
        Assert.DoesNotContain(intended, error.Message);
        Assert.DoesNotContain(actual, error.Message);
    }

    [Fact]
    public void UpdateRejectsUnpersistedPasswordWithoutExposingCiphertext()
    {
        var error = Assert.Throws<InvalidOperationException>(() => HostDefinitionVerification.VerifyUpdate(
            "<FTP DefPassword=\"OLD_SECRET\" />", "<FTP DefPassword=\"NEW_SECRET\" />", "<FTP DefPassword=\"OLD_SECRET\" />"));
        Assert.DoesNotContain("OLD_SECRET", error.Message);
        Assert.DoesNotContain("NEW_SECRET", error.Message);
    }

    [Fact]
    public void DescriptionVerificationSupportsNativeAttributeAndElementForms()
    {
        HostDefinitionVerification.VerifyCreate("<FTP><Desc>New</Desc></FTP>", "<FTP Description=\"New\" />");
        HostDefinitionVerification.VerifyUpdate("<FTP Desc=\"Old\" />", "<FTP Desc=\"New\" />", "<FTP><Desc>New</Desc></FTP>");
        Assert.Throws<InvalidOperationException>(() => HostDefinitionVerification.VerifyUpdate(
            "<FTP Desc=\"Old\" />", "<FTP Desc=\"New\" />", "<FTP Desc=\"Old\" />"));
    }

    [Fact]
    public void UnrelatedServerFieldsDoNotCauseVerificationFailures()
    {
        HostDefinitionVerification.VerifyUpdate("<FTP Host=\"old\" Counter=\"1\" />",
            "<FTP Host=\"new\" Counter=\"1\" />", "<FTP Host=\"new\" Counter=\"99\" />");
    }

    [Fact]
    public void ReleaseFailureAfterCommitCarriesHostIdentityAndNeverReportsSuccess()
    {
        var failure = new InvalidOperationException("Native release failed");
        var error = Assert.Throws<HostPartialSaveException>(() => HostDefinitionVerification.EnsureCheckoutReleased(true, failure, "42"));
        Assert.Equal("42", error.HostId);
        Assert.Same(failure, error.InnerException);
        HostDefinitionVerification.EnsureCheckoutReleased(false, failure, "42"); // Preserve the original failed-operation exception.
        HostDefinitionVerification.EnsureCheckoutReleased(true, null, "42");
    }

    [Theory]
    [InlineData(true, 0, true)]
    [InlineData(false, 4015, true)]
    [InlineData(false, 2850, false)]
    [InlineData(false, 0, false)]
    public void NativeCheckoutReleaseDistinguishesAlreadyUnlockedFromActualFailure(bool successful, int code, bool expected)
        => Assert.Equal(expected, HostDefinitionVerification.IsCheckoutReleased(successful, code));

    [Theory]
    [InlineData("none")]
    [InlineData("off")]
    [InlineData("0")]
    public void FtpsCannotExplicitlySelectPlaintextForCreateOrUpdate(string mode)
    {
        var values = new Dictionary<string, string> { ["NAME"] = "Host", ["HOST_TYPE"] = "FTPS",
            ["HOST"] = "example.com", ["SECURE"] = mode };
        Assert.Equal("SECURE", Assert.Throws<ArgumentException>(() => HostBasicDefinitionProfile.Parse(values)).ParamName);
        Assert.Equal("SECURE", Assert.Throws<ArgumentException>(() => HostBasicDefinitionProfile.ParseUpdate(values)).ParamName);
        values["HOST_TYPE"] = "FTP";
        Assert.Equal(mode, HostBasicDefinitionProfile.Parse(values).Secure);
    }

    [Fact]
    public void BlankFtpsUpdateStillPreservesExistingMode()
    {
        var fields = new Dictionary<string, string> { ["NAME"] = "Host", ["HOST_TYPE"] = "FTPS", ["SECURE"] = "" };
        var patch = HostBasicDefinitionProfile.ParseUpdate(fields);
        Assert.Contains("Secure=\"tls-p\"", HostBasicDefinitionProfile.ComposeUpdate(
            "<FTP ID=\"42\" Name=\"Host\" Secure=\"tls-p\" />", patch, "", ""));
    }
}
