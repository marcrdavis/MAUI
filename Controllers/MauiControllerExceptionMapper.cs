// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Controllers;

/// <summary>
/// Centralizes the standard JSON response contract for expected MOVEit/controller API exceptions.
/// Endpoint-specific conflicts, cleanup failures, partial results, and navigation redirects remain local.
/// </summary>
internal static class MauiControllerExceptionMapper
{
    internal const string SessionExpiredMessage = "MOVEit session expired. Please sign in again.";

    // Preserves the established MAUI JSON shape: authentication failures are 401; expected API/validation failures are 400.
    internal static IActionResult Map(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is ObjectCheckoutRejectedException checkout) return new ConflictObjectResult(checkout.Feedback());

        if (exception is ObjectEditConflictException or ObjectCheckoutReleaseException) return new ConflictObjectResult(new { message = exception.Message, editConflict = true, moveItSaved = false });

        return exception is MoveItAuthenticationException
            ? SessionExpired()
            : BadRequest(exception);
    }

    internal static UnauthorizedObjectResult SessionExpired() =>
        new(new { message = SessionExpiredMessage });

    private static BadRequestObjectResult BadRequest(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new BadRequestObjectResult(new { message = exception.Message });
    }
}
