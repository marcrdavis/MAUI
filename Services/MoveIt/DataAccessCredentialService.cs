// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Stores and resolves the optional per-server credentials used by unattended MAUI data-access operations.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;

namespace MoveItAutomationUI.Services.MoveIt;

public sealed record DataAccessCredential(
    string ServerKey,
    string EnvironmentKey,
    string DisplayName,
    string Username,
    string Password);

public sealed record DataAccessCredentialTestResult(bool Success, string Message);

public interface IDataAccessCredentialProtector
{
    string Protect(string clearText);
    string Unprotect(string protectedValue);
}

public sealed class DataAccessCredentialProtector : IDataAccessCredentialProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MoveItAutomationUI|DataAccessCredentials|v1");

    public string Protect(string clearText)
    {
        if (string.IsNullOrEmpty(clearText)) return string.Empty;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("MAUI Data Access credential protection requires Windows DPAPI.");
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
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("MAUI Data Access credential protection requires Windows DPAPI.");
        var protectedBytes = Convert.FromBase64String(protectedValue);
        var clearBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.LocalMachine);
        try { return Encoding.UTF8.GetString(clearBytes); }
        finally { CryptographicOperations.ZeroMemory(clearBytes); }
    }
}

public interface IDataAccessCredentialService
{
    bool IsConfigured(string serverKey, out string username);
    bool IsEnvironmentConfigured(string environmentKey);
    DataAccessCredential GetRequired(string serverKey);
    IReadOnlyList<DataAccessCredential> GetConfiguredForEnvironment(string environmentKey);
    Task<string> SaveAsync(string serverKey, string username, string password, CancellationToken cancellationToken = default);
    Task<string> ClearAsync(string serverKey, CancellationToken cancellationToken = default);
    Task<DataAccessCredentialTestResult> TestAsync(string serverKey, CancellationToken cancellationToken = default);
}

public sealed class DataAccessCredentialService : IDataAccessCredentialService
{
    private readonly IConfiguration _configuration;
    private readonly IMoveItServerCatalog _servers;
    private readonly IAppSettingsDocumentStore _appSettings;
    private readonly AppSettingsWriteCoordinator _writeCoordinator;
    private readonly IAppSettingsConfigurationSnapshotReader _snapshots;
    private readonly IDataAccessCredentialProtector _protector;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DataAccessCredentialService> _logger;

    public DataAccessCredentialService(
        IConfiguration configuration,
        IMoveItServerCatalog servers,
        IAppSettingsDocumentStore appSettings,
        AppSettingsWriteCoordinator writeCoordinator,
        IAppSettingsConfigurationSnapshotReader snapshots,
        IDataAccessCredentialProtector protector,
        IServiceScopeFactory scopeFactory,
        ILogger<DataAccessCredentialService> logger)
    {
        _configuration = configuration;
        _servers = servers;
        _appSettings = appSettings;
        _writeCoordinator = writeCoordinator;
        _snapshots = snapshots;
        _protector = protector;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public bool IsConfigured(string serverKey, out string username)
    {
        username = string.Empty;
        var server = _servers.FindByKey(serverKey);
        if (server is null || !HasConfiguredCredentials(server)) return false;
        username = server.DataAccessUsername!.Trim();
        return true;
    }

    public bool IsEnvironmentConfigured(string environmentKey)
    {
        var environment = MoveItEnvironmentKey.Normalize(environmentKey);
        return _servers.GetServers().Any(server =>
            MoveItEnvironmentKey.Resolve(server).Equals(environment, StringComparison.OrdinalIgnoreCase)
            && HasConfiguredCredentials(server));
    }

    public DataAccessCredential GetRequired(string serverKey)
    {
        var activeServer = _servers.FindByKey(serverKey)
            ?? throw new InvalidOperationException($"MOVEit server '{serverKey}' is not configured in MAUI.");
        return CreateCredential(activeServer);
    }

    private DataAccessCredential CreateCredential(MoveItServerDefinition configured)
    {
        // Keep the endpoint and protected credentials from the same MOVEit catalog snapshot.
        if (!HasConfiguredCredentials(configured))
            throw new InvalidOperationException($"Data Access Credentials are not configured for MOVEit server '{GetDisplayName(configured)}'.");

        string password;
        try { password = _protector.Unprotect(configured.DataAccessProtectedPassword!); }
        catch (Exception ex) when (ex is CryptographicException or FormatException or PlatformNotSupportedException)
        {
            throw new InvalidOperationException($"The saved Data Access password for MOVEit server '{GetDisplayName(configured)}' cannot be decrypted on this server. Re-enter and save the credentials.", ex);
        }

        return new DataAccessCredential(
            configured.Key,
            MoveItEnvironmentKey.Resolve(configured),
            GetDisplayName(configured),
            configured.DataAccessUsername!.Trim(),
            password);
    }

    public IReadOnlyList<DataAccessCredential> GetConfiguredForEnvironment(string environmentKey)
    {
        var environment = MoveItEnvironmentKey.Normalize(environmentKey);
        var result = new List<DataAccessCredential>();
        Exception? lastCredentialError = null;
        var configuredCount = 0;
        var defaultServer = (_configuration.GetSection(UiOptions.SectionName).Get<UiOptions>() ?? new UiOptions()).DefaultServer;
        foreach (var server in _servers.GetServers()
            .Where(server => MoveItEnvironmentKey.Resolve(server).Equals(environment, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(server => server.Key.Equals(defaultServer, StringComparison.OrdinalIgnoreCase)))
        {
            if (!HasConfiguredCredentials(server)) continue;
            configuredCount++;
            try
            {
                result.Add(CreateCredential(server));
            }
            catch (InvalidOperationException ex)
            {
                lastCredentialError = ex;
                _logger.LogWarning(ex, "Data Access Credentials for MOVEit server {ServerKey} could not be loaded; another configured server in environment {EnvironmentKey} will be used if available.", server.Key, environment);
            }
        }

        if (result.Count == 0 && configuredCount > 0 && lastCredentialError is not null)
            throw new InvalidOperationException($"Data Access Credentials are configured for environment '{environment}', but none of the saved passwords could be loaded. Re-enter the credentials for at least one active server.", lastCredentialError);

        return result;
    }

    public async Task<string> SaveAsync(string serverKey, string username, string password, CancellationToken cancellationToken = default)
    {
        serverKey = (serverKey ?? string.Empty).Trim();
        username = (username ?? string.Empty).Trim();
        password ??= string.Empty;
        if (serverKey.Length == 0) throw new ArgumentException("MOVEit Server is required.");
        if (username.Length == 0) throw new ArgumentException("Data Access Username is required.");

        await _writeCoordinator.Gate.WaitAsync(cancellationToken);
        try
        {
            var root = await _appSettings.ReadLiveAsync(cancellationToken);
            var serverNode = FindServerNode(root, serverKey)
                ?? throw new ArgumentException($"MOVEit server '{serverKey}' is not configured in MAUI.");
            var protectedPassword = serverNode[nameof(MoveItServerDefinition.DataAccessProtectedPassword)]?.GetValue<string>() ?? string.Empty;
            if (password.Length > 0) protectedPassword = _protector.Protect(password);
            else if (protectedPassword.Length == 0) throw new ArgumentException("Data Access Password is required when configuring this server for the first time.");

            serverNode[nameof(MoveItServerDefinition.DataAccessUsername)] = username;
            serverNode[nameof(MoveItServerDefinition.DataAccessProtectedPassword)] = protectedPassword;
            await _appSettings.WriteLiveAsync(root, cancellationToken);
            _logger.LogInformation("Data Access Credentials were configured for MOVEit server {ServerKey}. Username={DataAccessUsername}.", serverKey, username);
            return _snapshots.GetRevision(root);
        }
        finally
        {
            password = string.Empty;
            _writeCoordinator.Gate.Release();
        }
    }

    public async Task<string> ClearAsync(string serverKey, CancellationToken cancellationToken = default)
    {
        serverKey = (serverKey ?? string.Empty).Trim();
        if (serverKey.Length == 0) throw new ArgumentException("MOVEit Server is required.");

        await _writeCoordinator.Gate.WaitAsync(cancellationToken);
        try
        {
            var root = await _appSettings.ReadLiveAsync(cancellationToken);
            var serverNode = FindServerNode(root, serverKey)
                ?? throw new ArgumentException($"MOVEit server '{serverKey}' is not configured in MAUI.");
            serverNode.Remove(nameof(MoveItServerDefinition.DataAccessUsername));
            serverNode.Remove(nameof(MoveItServerDefinition.DataAccessProtectedPassword));
            await _appSettings.WriteLiveAsync(root, cancellationToken);
            _logger.LogInformation("Data Access Credentials were cleared for MOVEit server {ServerKey}.", serverKey);
            return _snapshots.GetRevision(root);
        }
        finally
        {
            _writeCoordinator.Gate.Release();
        }
    }

    public async Task<DataAccessCredentialTestResult> TestAsync(string serverKey, CancellationToken cancellationToken = default)
    {
        var credential = GetRequired(serverKey);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var restClient = scope.ServiceProvider.GetRequiredService<IMoveItRestClient>();
            var state = await restClient.AuthenticateAsync(credential.ServerKey, credential.Username, credential.Password, cancellationToken);
            var info = await restClient.GetInfoAsync(state, cancellationToken);
            if (!info.IsAdmin)
                throw new InvalidOperationException($"Data Access user '{credential.Username}' authenticated to {credential.DisplayName}, but MOVEit Administrator access is required for MAUI backups, scheduled reports, and System Jobs.");

            _logger.LogInformation("Data Access Credentials tested successfully for MOVEit server {ServerKey}.", credential.ServerKey);
            return new DataAccessCredentialTestResult(true, $"Credentials accepted by {credential.DisplayName}; MOVEit Administrator access confirmed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is MoveItAuthenticationException or MoveItApiException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Data Access credential test failed for MOVEit server {ServerKey}.", credential.ServerKey);
            return new DataAccessCredentialTestResult(false, ex.Message);
        }
    }

    private static JsonObject? FindServerNode(JsonObject root, string serverKey)
    {
        if (root[MoveItOptions.SectionName] is not JsonObject moveIt || moveIt[nameof(MoveItOptions.Servers)] is not JsonArray servers)
            throw new InvalidOperationException("MOVEit server configuration is missing from appsettings.json.");
        return servers.OfType<JsonObject>().FirstOrDefault(server =>
            string.Equals(server[nameof(MoveItServerDefinition.Key)]?.GetValue<string>()?.Trim(), serverKey, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasConfiguredCredentials(MoveItServerDefinition server)
        => !string.IsNullOrWhiteSpace(server.DataAccessUsername)
            && !string.IsNullOrWhiteSpace(server.DataAccessProtectedPassword);

    private static string GetDisplayName(MoveItServerDefinition server)
        => string.IsNullOrWhiteSpace(server.DisplayName) ? server.Key : server.DisplayName.Trim();


}
