using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class HostAuthenticationSettingsTests
{
    [Fact]
    public void PasswordPreservationSuppressesEveryCasingWithoutDuplicatingNativeFields()
    {
        var host = new JsonObject { ["defPassword"] = "PRIVATE_CIPHER", ["DefPassword"] = "SECOND_CIPHER" };
        HostAuthenticationSettings.PreservePassword(host, new JsonObject());
        Assert.Single(host);
        Assert.Null(host["defPassword"]);
        Assert.DoesNotContain("CIPHER", host.ToJsonString());
        HostAuthenticationSettings.ApplyPassword(host, new JsonObject { ["DefPassword"] = "  literal\\x41  " });
        Assert.Single(host);
        Assert.Equal("  literal\\x41  ", host["defPassword"]!.GetValue<string>());
        Assert.Throws<InvalidOperationException>(() => HostAuthenticationSettings.ApplyPassword(host, new JsonObject { ["DefPassword"] = 123 }));
    }

    [Theory]
    [InlineData("https://login.microsoftonline.com/tenantid1/oauth2/v2.0/token", "microsoft365", "tenantid1")]
    [InlineData("https://login.microsoftonline.com/contoso.onmicrosoft.com/oauth2/v2.0/token", "microsoft365", "contoso.onmicrosoft.com")]
    [InlineData("https://identity.example.test/tenant/oauth2/v2.0/token", "custom", "")]
    [InlineData("https://login.microsoftonline.com/tenant/oauth2/v2.0/token?extra=value", "custom", "")]
    [InlineData("http://login.microsoftonline.com/tenant/oauth2/v2.0/token", "custom", "")]
    public void MailProviderIsInferredFromTheNativeScopeAndExactTokenEndpoint(string url, string provider, string tenant)
    {
        var host = new JsonObject { ["OAuthScope"] = HostMailOAuthSettings.Scope, ["OAuthTokenURL"] = url };
        Assert.Equal(provider, HostMailOAuthSettings.Provider(host));
        Assert.Equal(tenant, HostMailOAuthSettings.Tenant(host));
    }

    [Fact]
    public void Office365ReadbackAcceptsNativeBooleanTlsAndRejectsChangedDerivedSettings()
    {
        var patch = new JsonObject { [HostMailOAuthSettings.ProviderKey] = "microsoft365", [HostMailOAuthSettings.TenantKey] = "tenantid1" };
        var expected = new JsonObject { ["UseOAuth"] = 1 };
        HostMailOAuthSettings.Apply(expected, patch);
        var actual = expected.DeepClone().AsObject(); actual["Secure"] = true;
        HostMailOAuthSettings.Verify(expected, actual, patch);
        actual["OAuthTokenURL"] = "https://different.example.test/token";
        Assert.Throws<InvalidOperationException>(() => HostMailOAuthSettings.Verify(expected, actual, patch));
        expected["UseOAuth"] = 0;
        Assert.Throws<InvalidOperationException>(() => HostMailOAuthSettings.Apply(expected, patch));
    }
}
