// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Allowlisted, non-secret recovery diagnostics for anonymous Login antiforgery failures.

using System.Security.Cryptography;
using Microsoft.AspNetCore.Antiforgery;

namespace MoveItAutomationUI.Services.Authentication;

public static class MauiAntiforgeryFailure
{
    public const string Decryption = "AF-DECRYPT";
    public const string MissingCookie = "AF-COOKIE";
    public const string MissingForm = "AF-FORM";
    public const string TokenPair = "AF-PAIR";
    public const string Identity = "AF-IDENTITY";
    public const string Other = "AF-OTHER";

    public static string Classify(AntiforgeryValidationException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var message = exception.Message ?? string.Empty;
        if (Contains(message, "could not be decrypted")
            || Contains(message, "cannot be decrypted")
            || HasCryptographicInnerException(exception))
            return Decryption;
        if (Contains(message, "cookie") && Contains(message, "not present")) return MissingCookie;
        if (Contains(message, "form field") && Contains(message, "not present")) return MissingForm;
        if (Contains(message, "different claims-based user") || Contains(message, "different user")) return Identity;
        if (Contains(message, "do not match") || Contains(message, "does not match")
            || Contains(message, "cookie token and request token")) return TokenPair;
        return Other;
    }

    // The query parameter is never interpreted as arbitrary UI text or an exception message.
    public static string? GetPublicMessage(string? code) => code switch
    {
        Decryption => "The server could not read the protected sign-in token (AF-DECRYPT). A fresh form has been loaded. If this repeats, contact your administrator.",
        MissingCookie => "The sign-in security cookie was missing (AF-COOKIE). A fresh form has been loaded. Please try again.",
        MissingForm => "The sign-in form security field was missing (AF-FORM). A fresh form has been loaded. Please try again.",
        TokenPair => "The sign-in form and security cookie did not match (AF-PAIR). A fresh form has been loaded. Please try again.",
        Identity => "The sign-in form was created for a different browser login identity (AF-IDENTITY). A fresh form has been loaded. Please try again.",
        Other => "The sign-in form could not be verified (AF-OTHER). A fresh form has been loaded. If this repeats, contact your administrator.",
        _ => null
    };

    private static bool Contains(string source, string fragment) =>
        source.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    private static bool HasCryptographicInnerException(Exception exception)
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is CryptographicException) return true;
        }
        return false;
    }
}
