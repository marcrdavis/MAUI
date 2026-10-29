// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI

using MoveItAutomationUI.Services.Metadata;

namespace MoveItAutomationUI.Tests.Notifications;

public sealed class TaskNotificationTemplateFormatTests
{
    [Theory]
    [InlineData("html", "html")]
    [InlineData("HTML", "html")]
    [InlineData(" text ", "text")]
    [InlineData("TEXT", "text")]
    public void NormalizeRequestedFormat_NormalizesSupportedValues(string input, string expected)
        => Assert.Equal(expected, TaskNotificationTemplateService.NormalizeRequestedFormat(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeRequestedFormat_AllowsMissingLegacyFormat(string? input)
        => Assert.Null(TaskNotificationTemplateService.NormalizeRequestedFormat(input));

    [Theory]
    [InlineData("richtext")]
    [InlineData("plain")]
    [InlineData("xml")]
    public void NormalizeRequestedFormat_RejectsUnsupportedValues(string input)
        => Assert.Throws<ArgumentException>(() => TaskNotificationTemplateService.NormalizeRequestedFormat(input));
}
