using Microsoft.Extensions.Configuration;
using MoveItAutomationUI.Services.Reporting;

namespace MoveItAutomationUI.Tests.Reporting;

public sealed class OutboundReportEmailReadinessTests
{
    [Fact]
    public void MissingSmtpHost_UsesConciseReportReadinessMessage()
    {
        // Password protection is never accessed when the SMTP host is absent.
        var service = new OutboundReportEmailService(new ConfigurationBuilder().Build(), null!);
        var status = service.GetConfigurationStatus();

        Assert.False(status.IsConfigured);
        Assert.Equal(
            "Unable to run report because outbound email has not been configured. Configure SMTP under Configuration > Settings > SMTP Configuration. Scheduled reports can still be saved and will remain pending until outbound email is configured.",
            status.Message);
    }

    [Fact]
    public void IncompleteSmtpConfiguration_PreservesSpecificDiagnostic()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Smtp:Host"] = "smtp.example.test"
            })
            .Build();
        var service = new OutboundReportEmailService(configuration, null!);
        var status = service.GetConfigurationStatus();

        Assert.False(status.IsConfigured);
        Assert.Contains("SMTP From Address is not configured.", status.Message);
    }
}
