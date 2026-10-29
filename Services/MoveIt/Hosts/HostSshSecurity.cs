using System.Globalization;
using System.Text.Json.Nodes;

namespace MoveItAutomationUI.Services.MoveIt.Hosts;

public sealed record HostSshClientKeyOption(string Id, string Name);
public sealed record HostFingerprintRequest(string HostId, string Host, int Port, string KexAlgs = "",
    string ProxyType = "None", string ProxyHost = "", int? ProxyPort = null, string ProxyUsername = "");

internal static class HostSshSecurity
{
    // Captured GET contract. No credentials are appended to the query string.
    public static IReadOnlyDictionary<string, string> BuildFingerprintQuery(HostFingerprintRequest request)
    {
        var host = (request.Host ?? "").Trim();
        if (host.Length == 0 || host.Contains('\r') || host.Contains('\n') || request.Port < 1 || request.Port > 65535)
            throw new InvalidOperationException("Enter a valid SFTP hostname and port before retrieving the Host key.");
        var proxy = new[] { "None", "SOCKS4", "SOCKS5", "WEBStandard" }
            .FirstOrDefault(value => value.Equals((request.ProxyType ?? "None").Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("This proxy type is not supported for SFTP Host key retrieval.");
        var query = new Dictionary<string, string>
        {
            ["host"] = host, ["port"] = request.Port.ToString(CultureInfo.InvariantCulture),
            ["kexAlgs"] = (request.KexAlgs ?? "").Replace("\r", "").Replace("\n", "").Trim(), ["proxyType"] = proxy
        };
        if (proxy != "None")
        {
            if (string.IsNullOrWhiteSpace(request.ProxyHost) || request.ProxyPort is null or < 1 or > 65535)
                throw new InvalidOperationException("Enter a proxy hostname and valid port before retrieving the Host key.");
            query["proxyHost"] = request.ProxyHost.Trim();
            query["proxyPort"] = request.ProxyPort.Value.ToString(CultureInfo.InvariantCulture);
            query["proxyUsername"] = request.ProxyUsername ?? "";
        }
        return query;
    }

    public static IReadOnlyList<HostSshClientKeyOption> MapKeys(IReadOnlyList<JsonObject> keys)
        => keys.Select(key => new HostSshClientKeyOption(
                HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(key, "ID")).Trim(),
                HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(key, "Name"))))
            .Where(key => key.Id.Length > 0).OrderBy(key => key.Name, StringComparer.OrdinalIgnoreCase).ToArray();
}
