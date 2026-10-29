// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// System Job that tests every visible MOVEit Host and persists system-managed health metadata.

using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Services.SystemJobs;

public interface IHostHealthCheckJob
{
    Task<MauiSystemJobExecutionOutcome> ExecuteAsync(CancellationToken cancellationToken = default);
}

public sealed class HostHealthCheckJob : IHostHealthCheckJob
{
    private readonly IMoveItServerCatalog _servers;
    private readonly IDataAccessCredentialService _credentials;
    private readonly IMoveItRestClient _restClient;
    private readonly IMoveItSessionService _sessionService;
    private readonly IAutomationHostService _hostService;
    private readonly IHostConnectionTestService _hostTestService;
    private readonly IHostHealthService _hostHealthService;
    private readonly ILogger<HostHealthCheckJob> _logger;

    public HostHealthCheckJob(
        IMoveItServerCatalog servers,
        IDataAccessCredentialService credentials,
        IMoveItRestClient restClient,
        IMoveItSessionService sessionService,
        IAutomationHostService hostService,
        IHostConnectionTestService hostTestService,
        IHostHealthService hostHealthService,
        ILogger<HostHealthCheckJob> logger)
    {
        _servers = servers;
        _credentials = credentials;
        _restClient = restClient;
        _sessionService = sessionService;
        _hostService = hostService;
        _hostTestService = hostTestService;
        _hostHealthService = hostHealthService;
        _logger = logger;
    }

    public async Task<MauiSystemJobExecutionOutcome> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var environments = _servers.GetServers()
            .Select(MoveItEnvironmentKey.Resolve)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (environments.Length == 0)
            throw new InvalidOperationException("No active MOVEit environments are configured for Host Health Check.");

        var total = 0;
        var healthy = 0;
        var unhealthy = 0;
        var unknown = 0;
        var environmentFailures = new List<string>();

        foreach (var environmentKey in environments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MoveItSessionState? state = null;
            try
            {
                var configuredCredentials = _credentials.GetConfiguredForEnvironment(environmentKey);
                if (configuredCredentials.Count == 0)
                    throw new InvalidOperationException($"Data Access Credentials are not configured for any active MOVEit server in environment '{environmentKey}'.");

                state = await AuthenticateEnvironmentAsync(environmentKey, configuredCredentials, cancellationToken);
                using (_sessionService.PushBackgroundSession(state))
                {
                    var hosts = (await _hostService.GetHostSnapshotAsync(true, cancellationToken)).Hosts;
                    foreach (var host in hosts)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        total++;
                        var checkedUtc = DateTimeOffset.UtcNow;

                        if (string.Equals(host.Name, "(Default)", StringComparison.Ordinal))
                        {
                            healthy++;
                            await _hostHealthService.SaveAsync(
                                environmentKey,
                                host.Id,
                                HostHealthStates.Healthy,
                                checkedUtc,
                                "Built-in Local File System Host; connection testing is not required.",
                                cancellationToken);
                            continue;
                        }

                        if (!_hostTestService.SupportsHostType(host.TypeCode))
                        {
                            unknown++;
                            await _hostHealthService.SaveAsync(
                                environmentKey,
                                host.Id,
                                HostHealthStates.Unknown,
                                checkedUtc,
                                $"MAUI does not currently have a connection-test pattern for Host type '{host.Type}'.",
                                cancellationToken);
                            continue;
                        }

                        try
                        {
                            var test = await _hostTestService.TestHostAsync(host.Id, cancellationToken);
                            var health = await _hostHealthService.SaveConnectionTestResultAsync(
                                environmentKey,
                                test,
                                checkedUtc,
                                cancellationToken);

                            if (health == HostHealthStates.Healthy) healthy++;
                            else if (health == HostHealthStates.Unknown) unknown++;
                            else unhealthy++;
                        }
                        catch (MoveItAuthenticationException)
                        {
                            throw;
                        }
                        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            unhealthy++;
                            _logger.LogWarning(ex, "Unexpected Host Health test failure for Host {HostId} ({HostName}) in environment {EnvironmentKey}.", host.Id, host.Name, environmentKey);
                            await _hostHealthService.SaveAsync(environmentKey, host.Id, HostHealthStates.Unhealthy, checkedUtc, ex.Message, cancellationToken);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                environmentFailures.Add($"{environmentKey}: {ex.Message}");
                _logger.LogError(ex, "Host Health Check could not process MOVEit environment {EnvironmentKey}; remaining environments will still be checked.", environmentKey);
            }
            finally
            {
                // Background session state exists only for this execution path, but clear retained credentials promptly.
                if (state is not null)
                {
                    state.Password = string.Empty;
                    state.AccessToken = string.Empty;
                    state.RefreshToken = string.Empty;
                }
            }
        }

        var summary = $"Completed. {total} Hosts evaluated: {healthy} Healthy, {unhealthy} Unhealthy, {unknown} Unknown.";
        if (environmentFailures.Count == 0)
            return MauiSystemJobExecutionOutcome.Successful(summary);

        return MauiSystemJobExecutionOutcome.Failed(
            $"{summary} {environmentFailures.Count} environment(s) could not be processed: {string.Join(" | ", environmentFailures)}");
    }

    private async Task<MoveItSessionState> AuthenticateEnvironmentAsync(
        string environmentKey,
        IReadOnlyList<DataAccessCredential> credentials,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        foreach (var credential in credentials)
        {
            try
            {
                var state = await _restClient.AuthenticateAsync(credential.ServerKey, credential.Username, credential.Password, cancellationToken);
                var info = await _restClient.GetInfoAsync(state, cancellationToken);
                if (!info.IsAdmin)
                {
                    lastError = new InvalidOperationException($"Data Access user '{credential.Username}' authenticated to {credential.DisplayName}, but MOVEit Administrator access is required for Host Health Check.");
                    _logger.LogWarning("Data Access user {Username} on server {ServerKey} is not a MOVEit Administrator; another configured node in environment {EnvironmentKey} will be tried if available.", credential.Username, credential.ServerKey, environmentKey);
                    continue;
                }

                state.Info = info;
                return state;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is MoveItAuthenticationException or MoveItApiException or HttpRequestException or TaskCanceledException)
            {
                lastError = ex;
                _logger.LogWarning(ex, "Unable to establish Host Health background session through MOVEit server {ServerKey}; another configured node in environment {EnvironmentKey} will be tried if available.", credential.ServerKey, environmentKey);
            }
        }

        throw new InvalidOperationException($"Host Health Check could not authenticate to MOVEit environment '{environmentKey}' using the configured Data Access Credentials.", lastError);
    }
}
