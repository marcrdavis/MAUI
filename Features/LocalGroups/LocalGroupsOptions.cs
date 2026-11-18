using System.Text.RegularExpressions;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;

namespace MoveItAutomationUI.Features.LocalGroups;

public sealed class LocalGroupsOptions
{
    public const string SectionName = "MoveItLocalGroups";
    public SettingDefinition<bool> Enabled { get; set; } = new()
    {
        Name = "MOVEit User Management", Value = false, Type = "Boolean", Experimental = true,
        Description = "Manage Automation server local groups and local/domain user or group membership. Requires the optional standalone MAUI Local Groups Windows Service installed and running on each managed Automation server.",
        PossibleValues = [false, true]
    };
    public List<LocalGroupsAgent> Agents { get; set; } = [];
}

public sealed class LocalGroupsAgent
{
    public string ServerKey { get; set; } = "";
    public string CertificateSha256 { get; set; } = "";
    public string ConnectionBinding { get; set; } = "";
}

public sealed class LocalGroupsSettings
{
    public static LocalGroupsSettings From(IConfiguration configuration)
    {
        var options = configuration.GetSection(LocalGroupsOptions.SectionName).Get<LocalGroupsOptions>() ?? new();
        return new() { Enabled = options.Enabled.Value, Agents = options.Agents };
    }
    public bool Enabled { get; set; }
    public List<LocalGroupsAgent> Agents { get; set; } = [];

    public void NormalizeAndValidate(IEnumerable<string> serverKeys)
    {
        ArgumentNullException.ThrowIfNull(Agents);
        if (Agents.Any(agent => agent is null)) throw new ArgumentException("User Management registrations cannot be null.");
        var known = serverKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var certificates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Agents = Agents.Where(a => !string.IsNullOrWhiteSpace(a.CertificateSha256)).ToList();
        foreach (var agent in Agents)
        {
            agent.ServerKey = (agent.ServerKey ?? "").Trim();
            agent.CertificateSha256 = Regex.Replace(agent.CertificateSha256 ?? "", @"[\s:-]", "").ToUpperInvariant();
            if (!known.Contains(agent.ServerKey) || !keys.Add(agent.ServerKey)) throw new ArgumentException("Each User Management agent must identify one configured MOVEit server.");
            if (!Regex.IsMatch(agent.CertificateSha256, @"\A[0-9A-F]{64}\z") || !certificates.Add(agent.CertificateSha256))
                throw new ArgumentException("Each Automation server requires a distinct 64-character SHA-256 client certificate fingerprint.");
        }
        if (Enabled && Agents.Count == 0)
            throw new ArgumentException("Register at least one Automation server client certificate before enabling MOVEit User Management.");
    }
}

public sealed class LocalGroupsConfiguration(IAppSettingsConfigurationSnapshotReader snapshots)
{
    public LocalGroupsOptions Read() => snapshots.Read().Configuration.GetSection(LocalGroupsOptions.SectionName).Get<LocalGroupsOptions>() ?? new();
    public LocalGroupsAgent RequireSessionAgent(MoveItAutomationUI.Services.MoveIt.MoveItSessionState state)
    {
        var agent = RequireAgent(state.ServerKey);
        if (!agent.ConnectionBinding.Equals(LocalGroupsBinding.ForSession(state), StringComparison.Ordinal))
            throw new InvalidOperationException("The MOVEit connection changed after this service registration. Disable MOVEit User Management, clear this server's registration and Save, then register the correct server, re-enable the feature, and sign in again.");
        return agent;
    }
    public LocalGroupsAgent RequireAgent(string serverKey)
    {
        var options = Read();
        if (!options.Enabled.Value) throw new InvalidOperationException("MOVEit User Management is disabled.");
        return options.Agents.SingleOrDefault(agent => string.Equals(agent.ServerKey, serverKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Configure the User Management service certificate for the current Automation server in Experimental settings.");
    }
}
