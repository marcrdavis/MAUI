using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.Licensing;

public sealed class MoveItLicenseCapacityTests
{
    [Fact]
    public void ForTasks_ReturnsNull_WhenLimitIsUnavailable()
        => Assert.Null(MoveItLicenseCapacity.ForTasks(new MoveItSessionState(), 10));

    [Fact]
    public void ForTasks_ReturnsNull_WhenMoreThanWarningThresholdRemains()
        => Assert.Null(MoveItLicenseCapacity.ForTasks(Session(maxTasks: 100), 74));

    [Fact]
    public void ForTasks_ReturnsWarning_WhenExactlyTwentyFiveRemain()
    {
        var result = MoveItLicenseCapacity.ForTasks(Session(maxTasks: 100), 75);
        Assert.NotNull(result);
        Assert.Equal("warning", result!.Severity);
        Assert.Equal(25, result.Remaining);
    }

    [Fact]
    public void ForHosts_ReturnsDanger_WhenLimitReached()
    {
        var result = MoveItLicenseCapacity.ForHosts(Session(maxHosts: 20), 20);
        Assert.NotNull(result);
        Assert.Equal("danger", result!.Severity);
        Assert.Equal(0, result.Remaining);
    }

    [Fact]
    public void ForHosts_ReturnsDanger_WhenLimitExceeded()
    {
        var result = MoveItLicenseCapacity.ForHosts(Session(maxHosts: 20), 23);
        Assert.NotNull(result);
        Assert.Equal("danger", result!.Severity);
        Assert.Equal(23, result.Current);
        Assert.Equal(0, result.Remaining);
        Assert.Contains("exceeded", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Capacity_NormalizesNegativeCurrentCountToZero()
    {
        var result = MoveItLicenseCapacity.ForTasks(Session(maxTasks: 20), -4);
        Assert.NotNull(result);
        Assert.Equal(0, result!.Current);
        Assert.Equal(20, result.Remaining);
    }

    [Fact]
    public void Capacity_IgnoresUnlimitedValue()
        => Assert.Null(MoveItLicenseCapacity.ForTasks(Session(maxTasks: int.MaxValue), int.MaxValue));

    private static MoveItSessionState Session(int? maxTasks = null, int? maxHosts = null)
        => new()
        {
            Info = new MoveItInfo
            {
                LicenseInformationAvailable = true,
                License = new MoveItLicenseInfo { MaxTasks = maxTasks, MaxHosts = maxHosts }
            }
        };
}
