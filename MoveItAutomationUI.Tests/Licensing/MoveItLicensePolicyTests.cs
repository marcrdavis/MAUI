using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.Licensing;

public sealed class MoveItLicensePolicyTests
{
    [Fact]
    public void GetCapabilities_IsPermissive_WhenSessionIsMissing()
    {
        var result = MoveItLicensePolicy.GetCapabilities(null);
        Assert.True(result.AdvancedTasksLicensed);
        Assert.True(result.CustomScriptsLicensed);
        Assert.True(result.PgpLicensed);
        Assert.Null(result.MaxTasks);
        Assert.Null(result.MaxHosts);
    }

    [Fact]
    public void GetCapabilities_IsPermissive_WhenInfoIsMissing()
    {
        var result = MoveItLicensePolicy.GetCapabilities(new MoveItSessionState());
        Assert.True(result.AdvancedTasksLicensed);
        Assert.True(result.CustomScriptsLicensed);
        Assert.True(result.PgpLicensed);
    }

    [Fact]
    public void GetCapabilities_IsPermissive_WhenLicenseObjectWasNotReturned()
    {
        var session = Session(new MoveItInfo { LicenseInformationAvailable = false });
        var result = MoveItLicensePolicy.GetCapabilities(session);
        Assert.True(result.AdvancedTasksLicensed);
        Assert.True(result.CustomScriptsLicensed);
        Assert.True(result.PgpLicensed);
        Assert.Null(result.MaxTasks);
        Assert.Null(result.MaxHosts);
    }

    [Fact]
    public void GetCapabilities_UsesLicense_WhenAvailable()
    {
        var session = Session(new MoveItInfo
        {
            LicenseInformationAvailable = true,
            License = new MoveItLicenseInfo
            {
                AdvancedTasksLicensed = false,
                CustomScriptsLicensed = true,
                PgpLicensed = false,
                As2Licensed = true,
                BackupHostsLicensed = true,
                MaxTasks = 100,
                MaxHosts = 25
            }
        });

        var result = MoveItLicensePolicy.GetCapabilities(session);
        Assert.False(result.AdvancedTasksLicensed);
        Assert.True(result.CustomScriptsLicensed);
        Assert.False(result.PgpLicensed);
        Assert.True(result.As2Licensed);
        Assert.True(result.BackupHostsLicensed);
        Assert.Equal(100, result.MaxTasks);
        Assert.Equal(25, result.MaxHosts);
    }

    [Fact]
    public void GetCapabilities_DisablesCustomScripts_WhenServerDisablesScripting()
    {
        var session = Session(new MoveItInfo
        {
            LicenseInformationAvailable = true,
            CustomScriptingDisabled = true,
            License = new MoveItLicenseInfo { CustomScriptsLicensed = true }
        });

        Assert.False(MoveItLicensePolicy.GetCapabilities(session).CustomScriptsLicensed);
    }

    [Theory]
    [InlineData("Advanced", true)]
    [InlineData(" advanced ", true)]
    [InlineData("Traditional", false)]
    [InlineData("Sync", false)]
    [InlineData(null, false)]
    public void IsAdvancedTaskType_NormalizesExpectedValues(string? taskType, bool expected)
        => Assert.Equal(expected, MoveItLicensePolicy.IsAdvancedTaskType(taskType));

    [Fact]
    public void AdvancedTaskBlock_IsReturnedOnlyForUnlicensedAdvancedTask()
    {
        var session = Licensed(advanced: false);
        Assert.NotNull(MoveItLicensePolicy.GetAdvancedTaskCreationBlock(session, "Advanced"));
        Assert.Null(MoveItLicensePolicy.GetAdvancedTaskCreationBlock(session, "Traditional"));
    }

    [Fact]
    public void CustomScriptAndPgpBlocks_FollowLicenseCapabilities()
    {
        var session = Licensed(customScripts: false, pgp: false);
        Assert.NotNull(MoveItLicensePolicy.GetCustomScriptBlock(session));
        Assert.NotNull(MoveItLicensePolicy.GetPgpBlock(session));
    }

    [Fact]
    public void CapacityBlock_AllowsExactRemainingCapacity()
    {
        var session = Licensed(maxTasks: 10);
        Assert.Null(MoveItLicensePolicy.GetTaskCapacityBlock(session, currentCount: 8, additionalCount: 2));
    }

    [Fact]
    public void CapacityBlock_BlocksOperationThatWouldExceedLimit()
    {
        var session = Licensed(maxTasks: 10);
        var block = MoveItLicensePolicy.GetTaskCapacityBlock(session, currentCount: 8, additionalCount: 3);
        Assert.NotNull(block);
        Assert.Contains("would exceed", block!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CapacityBlock_BlocksWhenAlreadyAtLimit()
    {
        var session = Licensed(maxHosts: 5);
        var block = MoveItLicensePolicy.GetHostCapacityBlock(session, currentCount: 5);
        Assert.NotNull(block);
        Assert.Contains("reached", block!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CapacityBlock_IgnoresUnlimitedLimit()
    {
        var session = Licensed(maxTasks: int.MaxValue);
        Assert.Null(MoveItLicensePolicy.GetTaskCapacityBlock(session, int.MaxValue - 1, 100));
    }

    [Fact]
    public void CapacityBlock_IgnoresNonPositiveAdditionalCount()
    {
        var session = Licensed(maxTasks: 10);
        Assert.Null(MoveItLicensePolicy.GetTaskCapacityBlock(session, 10, 0));
        Assert.Null(MoveItLicensePolicy.GetTaskCapacityBlock(session, 10, -1));
    }

    [Fact]
    public void IsTaskLimitReached_ChangesAtExactBoundary()
    {
        var session = Licensed(maxTasks: 10);
        Assert.False(MoveItLicensePolicy.IsTaskLimitReached(session, 9));
        Assert.True(MoveItLicensePolicy.IsTaskLimitReached(session, 10));
    }

    [Fact]
    public void IsHostLimitReached_ChangesAtExactBoundary()
    {
        var session = Licensed(maxHosts: 5);
        Assert.False(MoveItLicensePolicy.IsHostLimitReached(session, 4));
        Assert.True(MoveItLicensePolicy.IsHostLimitReached(session, 5));
    }

    [Fact]
    public void LimitReached_IgnoresUnlimitedLimits()
    {
        var session = Licensed(maxTasks: int.MaxValue, maxHosts: int.MaxValue);
        Assert.False(MoveItLicensePolicy.IsTaskLimitReached(session, int.MaxValue));
        Assert.False(MoveItLicensePolicy.IsHostLimitReached(session, int.MaxValue));
    }

    private static MoveItSessionState Session(MoveItInfo info) => new() { Info = info };

    private static MoveItSessionState Licensed(
        bool advanced = true,
        bool customScripts = true,
        bool pgp = true,
        int? maxTasks = null,
        int? maxHosts = null)
        => Session(new MoveItInfo
        {
            LicenseInformationAvailable = true,
            License = new MoveItLicenseInfo
            {
                AdvancedTasksLicensed = advanced,
                CustomScriptsLicensed = customScripts,
                PgpLicensed = pgp,
                MaxTasks = maxTasks,
                MaxHosts = maxHosts
            }
        });
}
