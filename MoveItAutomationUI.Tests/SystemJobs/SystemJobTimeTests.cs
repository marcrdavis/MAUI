using MoveItAutomationUI.Services.SystemJobs;

namespace MoveItAutomationUI.Tests.SystemJobs;

public sealed class SystemJobTimeTests
{
    [Theory]
    [InlineData("00:00")]
    [InlineData("01:00")]
    [InlineData("13:45")]
    [InlineData("23:59")]
    [InlineData(" 06:00 ")]
    public void NormalizeRunTime_AcceptsTwentyFourHourTime(string input)
        => Assert.Equal(input.Trim(), SystemJobService.NormalizeRunTime(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("6:00")]
    [InlineData("06:00 AM")]
    [InlineData("24:00")]
    [InlineData("12:60")]
    public void NormalizeRunTime_RejectsInvalidFormat(string? input)
        => Assert.Throws<ArgumentException>(() => SystemJobService.NormalizeRunTime(input));
}
