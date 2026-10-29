// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Protects the global SMTP password with Windows DPAPI LocalMachine scope.

using System.Security.Cryptography;
using System.Text;

namespace MoveItAutomationUI.Services.Reporting;

public interface ISmtpSecretProtector
{
    string Protect(string clearText);
    string Unprotect(string protectedValue);
}

public sealed class SmtpSecretProtector : ISmtpSecretProtector
{
    // Keep the established entropy so an SMTP password saved by the first reporting build remains decryptable.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MoveItAutomationUI|OutboundReports|v1");

    public string Protect(string clearText)
    {
        if (string.IsNullOrEmpty(clearText)) return string.Empty;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("MAUI SMTP credential protection requires Windows DPAPI.");
        var bytes = Encoding.UTF8.GetBytes(clearText);
        try
        {
            var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.LocalMachine);
            return Convert.ToBase64String(protectedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public string Unprotect(string protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue)) return string.Empty;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("MAUI SMTP credential protection requires Windows DPAPI.");
        var protectedBytes = Convert.FromBase64String(protectedValue);
        var clearBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.LocalMachine);
        try { return Encoding.UTF8.GetString(clearBytes); }
        finally { CryptographicOperations.ZeroMemory(clearBytes); }
    }
}
