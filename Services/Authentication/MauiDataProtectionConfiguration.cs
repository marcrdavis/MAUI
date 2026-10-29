// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Manages the persistent Windows Data Protection key ring for IIS installations.

using Microsoft.AspNetCore.DataProtection;

namespace MoveItAutomationUI.Services.Authentication;

public static class MauiDataProtectionConfiguration
{
    public const string ApplicationName = "MoveItAutomationUI";
    public const string KeyFolderName = "DataProtection-Keys";

    public static string GetKeyDirectory(string contentRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRootPath);
        return Path.Combine(contentRootPath, "Data", KeyFolderName);
    }

    public static void AddMauiDataProtection(IServiceCollection services, string contentRootPath)
    {
        ArgumentNullException.ThrowIfNull(services);

        var protection = services.AddDataProtection().SetApplicationName(ApplicationName);
        if (!OperatingSystem.IsWindows())
        {
            // Preserve platform defaults for non-Windows development and test environments.
            // PersistKeysToFileSystem disables automatic key encryption unless explicitly restored.
            return;
        }

        // The IIS worker needs Modify on Data, NOT the application root or a loaded user profile.
        // Fail startup if this directory cannot be opened; never fall back to ephemeral/cleartext keys.
        var directory = GetKeyDirectory(contentRootPath);
        Directory.CreateDirectory(directory);
        protection.PersistKeysToFileSystem(new DirectoryInfo(directory))
            .ProtectKeysWithDpapi(protectToLocalMachine: true);
    }
}
