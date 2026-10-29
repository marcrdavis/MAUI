// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

public static class SetupDatabaseProviders
{
    public const string SQLite = "SQLite";
    public const string SqlServer = "SqlServer";
}

public static class SetupSqlAuthenticationModes
{
    public const string Integrated = "Integrated";
    public const string Sql = "Sql";
}

public sealed class InitialSetupViewModel
{
    public string ApplicationName { get; init; } = "MAUI";
    public string ApplicationLogo { get; init; } = "/images/automation-mark.svg";
    public string ApplicationVersion { get; init; } = string.Empty;
    public string DefaultDisplayName { get; init; } = "MOVEit Automation";
    public string DefaultEnvironmentKey { get; init; } = string.Empty;
    public string DefaultWebAdminUrl { get; init; } = string.Empty;
    public string DatabaseProvider { get; init; } = SetupDatabaseProviders.SQLite;
    public string SqliteDatabaseFile { get; init; } = @"Data\maui.db";
    public bool AllowInvalidServerCertificates { get; init; }
    public int TargetSchemaVersion { get; init; }
}

public sealed class InitialSetupRequest
{
    public SetupMoveItRequest MoveIt { get; set; } = new();
    public SetupDatabaseRequest Database { get; set; } = new();
}

public sealed class SetupMoveItRequest
{
    public string DisplayName { get; set; } = string.Empty;
    public string EnvironmentKey { get; set; } = string.Empty;
    public string WebAdminUrl { get; set; } = string.Empty;
    public bool AllowInvalidServerCertificates { get; set; }
}

public sealed class SetupDatabaseRequest
{
    public string Provider { get; set; } = SetupDatabaseProviders.SQLite;
    public string SqliteDatabaseFile { get; set; } = @"Data\maui.db";
    public string SqlServer { get; set; } = string.Empty;
    public string SqlDatabase { get; set; } = string.Empty;
    public string SqlAuthentication { get; set; } = SetupSqlAuthenticationModes.Integrated;
    public string SqlUsername { get; set; } = string.Empty;
    public string SqlPassword { get; set; } = string.Empty;
    public bool SqlEncrypt { get; set; } = true;
    public bool SqlTrustServerCertificate { get; set; }
}

public sealed class DatabaseConnectionTestRequest
{
    public string Provider { get; set; } = SetupDatabaseProviders.SQLite;
    public string ConnectionString { get; set; } = string.Empty;
}

public sealed class InitialSetupCompletionResult
{
    public string ServerKey { get; init; } = string.Empty;
    public string EnvironmentKey { get; init; } = string.Empty;
    public string DatabaseProvider { get; init; } = string.Empty;
    public bool RestartRequired { get; init; }
}
