// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

public static class MauiSecurityRoles
{
    public const string Admin = "Admin";
    public const string User = "User";

    // Purpose: Returns whether a role value is one of the supported MAUI roles.
    public static bool IsValid(string? role) =>
        string.Equals(role, Admin, StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, User, StringComparison.OrdinalIgnoreCase);

    // Purpose: Normalizes a role value to Admin or User.
    public static string Normalize(string? role) =>
        string.Equals(role, Admin, StringComparison.OrdinalIgnoreCase) ? Admin : User;
}

public static class MauiSecurityPolicies
{
    public const string Admin = "MauiAdmin";
    public const string TemplateAdmin = "TemplateAdmin";
}

// Represents one MAUI authorization identity stored in the metadata database.
// MOVEit remains responsible for authentication; this record controls MAUI Admin/User authorization only.
public sealed class MauiSecurityUser
{
    public const int UsernameMaxLength = 128;
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = MauiSecurityRoles.User;
    public DateTimeOffset? LastLoginUtc { get; set; }
}

