// Project: MAUI - MOVEit Automation UI
// Regression tests for safe, browser-readable antiforgery failure categories.
using Microsoft.AspNetCore.Antiforgery;
using MoveItAutomationUI.Services.Authentication;

namespace MoveItAutomationUI.Tests.Authentication;

public sealed class MauiAntiforgeryFailureTests
{
    [Theory]
    [InlineData("The antiforgery token could not be decrypted.", MauiAntiforgeryFailure.Decryption)]
    [InlineData("The required antiforgery cookie is not present.", MauiAntiforgeryFailure.MissingCookie)]
    [InlineData("The required antiforgery form field __RequestVerificationToken is not present.", MauiAntiforgeryFailure.MissingForm)]
    [InlineData("The antiforgery cookie token and request token do not match.", MauiAntiforgeryFailure.TokenPair)]
    [InlineData("The provided antiforgery token was meant for a different claims-based user than the current user.", MauiAntiforgeryFailure.Identity)]
    [InlineData("Unrecognized framework validation error", MauiAntiforgeryFailure.Other)]
    public void ClassifierReturnsAllowlistedCode(string exceptionText, string expected)
    {
        Assert.Equal(expected, MauiAntiforgeryFailure.Classify(new AntiforgeryValidationException(exceptionText)));
        var message = Assert.IsType<string>(MauiAntiforgeryFailure.GetPublicMessage(expected));
        Assert.Contains(expected, message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AF-SECRET")]
    [InlineData("<script>alert(1)</script>")]
    public void UnknownQueryCodesDoNotBecomeMessages(string? queryValue)
    {
        Assert.Null(MauiAntiforgeryFailure.GetPublicMessage(queryValue));
    }
}
