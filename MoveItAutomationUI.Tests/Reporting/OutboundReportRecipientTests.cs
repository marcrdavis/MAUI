using MoveItAutomationUI.Services.Reporting;

namespace MoveItAutomationUI.Tests.Reporting;

public sealed class OutboundReportRecipientTests
{
    [Fact]
    public void NormalizeRecipients_AcceptsCommaAndSemicolonSeparators()
    {
        var result = OutboundReportRepository.NormalizeRecipients("a@example.com; b@example.com,c@example.com");
        Assert.Equal(new[] { "a@example.com", "b@example.com", "c@example.com" }, result);
    }

    [Fact]
    public void NormalizeRecipients_RemovesDuplicatesIgnoringCase()
    {
        var result = OutboundReportRepository.NormalizeRecipients("User@example.com;user@example.com");
        Assert.Single(result);
    }

    [Fact]
    public void NormalizeRecipients_RejectsEmptyList()
        => Assert.Throws<ArgumentException>(() => OutboundReportRepository.NormalizeRecipients(" ; , "));

    [Fact]
    public void NormalizeRecipients_RejectsInvalidAddress()
        => Assert.Throws<ArgumentException>(() => OutboundReportRepository.NormalizeRecipients("valid@example.com;not-an-address"));

    [Fact]
    public void NormalizeRecipients_RejectsMoreThanOneHundredUniqueRecipients()
    {
        var value = string.Join(';', Enumerable.Range(1, 101).Select(i => $"user{i}@example.com"));
        Assert.Throws<ArgumentException>(() => OutboundReportRepository.NormalizeRecipients(value));
    }
}
