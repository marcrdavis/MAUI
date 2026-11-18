using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Features.LocalGroups;

// A certificate registration cannot silently follow a connection key to another MOVEit target.
internal static class LocalGroupsBinding
{
    internal static string Compute(string key, string environment, string url, string host)
    {
        var endpoint = url.Trim().TrimEnd('/');
        var effectiveHost = string.IsNullOrWhiteSpace(host) && Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ? uri.Host : host.Trim();
        var scope = string.IsNullOrWhiteSpace(environment) ? key : environment;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\0",
            key.Trim().ToUpperInvariant(), MoveItEnvironmentKey.Normalize(scope), endpoint.ToUpperInvariant(), effectiveHost.ToUpperInvariant()))));
    }

    internal static string ForSession(MoveItSessionState state)
        => Compute(state.ServerKey, state.GetEnvironmentKey(), state.WebAdminUrl, state.ServerHost);

    internal static void BindRegistrations(LocalGroupsSettings requested, JsonObject document,
        IReadOnlyList<ConfigurationServerSaveRequest> servers)
    {
        var prior = document[LocalGroupsOptions.SectionName]?.Deserialize<LocalGroupsOptions>(LocalGroupsProtocol.Json)?.Agents ?? [];
        var definitions = servers.ToDictionary(server => server.Key, StringComparer.OrdinalIgnoreCase);
        var oldServers = document[MoveItOptions.SectionName]?.Deserialize<MoveItOptions>(LocalGroupsProtocol.Json)?.Servers ?? [];
        foreach (var agent in requested.Agents)
        {
            var same = prior.FirstOrDefault(old => old.ServerKey.Equals(agent.ServerKey, StringComparison.OrdinalIgnoreCase)
                && old.CertificateSha256.Equals(agent.CertificateSha256, StringComparison.OrdinalIgnoreCase));
            // Ignore all client-supplied binding values. An unchanged registration retains its
            // original scope; changing a connection requires explicit removal/re-registration.
            if (same is not null && same.ConnectionBinding.Length > 0) { agent.ConnectionBinding = same.ConnectionBinding; continue; }
            var target = definitions[agent.ServerKey];
            var previous = same is null ? null : oldServers.FirstOrDefault(server => server.Key.Equals(agent.ServerKey, StringComparison.OrdinalIgnoreCase));
            agent.ConnectionBinding = previous is null
                ? Compute(target.Key, target.EnvironmentKey, target.WebAdminUrl, target.ServerHost)
                : Compute(previous.Key, previous.EnvironmentKey, previous.WebAdminUrl, previous.ServerHost);
        }
    }
}
