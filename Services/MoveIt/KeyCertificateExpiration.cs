// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Globalization;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.MoveIt;

/// <summary>
/// Shared expiration rules for TLS certificates and PGP keys.
/// Date-only values remain valid through the displayed expiration date;
/// timestamp values expire at their actual instant. SSH keys are not evaluated.
/// </summary>
public static class KeyCertificateExpiration
{
    // Purpose: Returns whether the key or certificate is expired at the supplied reference time.
    public static bool IsExpired(AutomationKeyCertificate item)
    {
        // PGP inventory exposes MOVEit's own expiration result; use the date rule when it is absent.
        if (item.Kind == KeyCertificateKind.Pgp && item.NativeIsExpired.HasValue)
            return item.NativeIsExpired.Value;
        if ((item.Kind != KeyCertificateKind.Tls && item.Kind != KeyCertificateKind.Pgp)
            || string.IsNullOrWhiteSpace(item.ExpirationDate))
        {
            return false;
        }

        var value = item.ExpirationDate.Trim();
        var styles = DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal;
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, styles, out var expiration)
            && !DateTimeOffset.TryParse(value, CultureInfo.CurrentCulture, styles, out expiration))
        {
            return false;
        }

        var includesTime = value.Contains(':') || value.Contains("T", StringComparison.OrdinalIgnoreCase);
        return includesTime
            ? expiration <= DateTimeOffset.Now
            : expiration.Date < DateTimeOffset.Now.Date;
    }
}
