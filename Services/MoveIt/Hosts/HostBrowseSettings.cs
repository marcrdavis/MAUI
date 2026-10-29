using System.Globalization;
using System.Text.Json.Nodes;

namespace MoveItAutomationUI.Services.MoveIt.Hosts;

// Shared by editor, Host resource picker and operational browse callers. The existing
// v0 envelopes are extended with Host editor fields; stored protected secrets stay null
// so MOVEit resolves them using hostid. Only explicit picker replacements may be sent.
internal static class HostBrowseSettings
{
    public static IEnumerable<string> Fields(string type) => new[] { "ProxyHost", "ProxyPort", "ProxyUsername", "ProxyIgnoreCertErrors" }
        .Concat(type == "GoogleStorage" ? new[] { "UseOAuth", "ProjectID", "OAuthClientID" }
            : type == "SharePoint" ? new[] { "UsingAppAuthentication", "RealmID", "TenantID" }
            : type == "S3" ? new[] { "AuthMethod", "IsNonAmazonEndPoint", "S3AlternativeEndpoint" } : Array.Empty<string>());

    public static void ApplyProxy(JsonObject login, JsonObject host, string type, JsonObject? replacements = null)
    {
        var requested = HostResourceContracts.Text(host, "ProxyType");
        if (requested.Length == 0) requested = "None";
        if (!HostCapturedProfiles.TryGet(type, out var profile)) throw new InvalidOperationException("This Host has no supported proxy profile.");
        var choices = profile.First(field => field.Key == "ProxyType").Choices!;
        var selected = choices.FirstOrDefault(choice => choice.Value.Equals(requested, StringComparison.OrdinalIgnoreCase))?.Value
            ?? throw new InvalidOperationException("The saved proxy type is not supported by this Host profile.");
        login["proxytype"] = selected;
        if (selected == "None") return;
        var proxyHost = HostResourceContracts.Text(host, "ProxyHost");
        if (proxyHost.Length == 0 || !int.TryParse(HostResourceContracts.Text(host, "ProxyPort"), NumberStyles.None,
            CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
            throw new InvalidOperationException("An enabled Browse proxy requires a hostname and port from 1 through 65535.");
        login["proxyhost"] = proxyHost;
        login["proxyport"] = port;
        login["proxyusername"] = HostResourceContracts.Text(host, "ProxyUsername");
        login["proxypassword"] = Replacement(replacements, "ProxyPassword");
        if (type == "SharePoint") login["proxyignorecerterrors"] = selected == "HTTPS" && Flag(host, "ProxyIgnoreCertErrors");
    }

    public static void ApplyGoogleAuthentication(JsonObject request, JsonObject host)
    {
        var oauth = Flag(host, "UseOAuth");
        if (oauth && (HostResourceContracts.Text(host, "ProjectID").Length == 0 || HostResourceContracts.Text(host, "OAuthClientID").Length == 0))
            throw new InvalidOperationException("Google Storage OAuth Browse requires Project ID and OAuth Client ID.");
        request["useoauth"] = oauth;
        request["projectid"] = oauth ? HostResourceContracts.Text(host, "ProjectID") : null;
        request["oauthclientid"] = oauth ? HostResourceContracts.Text(host, "OAuthClientID") : null;
        // Both browse callers use saved hostid. Its private key is resolved by MOVEit, not copied from GET.
        request["oauthclientsecret"] = null;
        if (oauth && request["logininfo"] is JsonObject login)
        {
            login["username"] = null;
            login["password"] = null;
        }
    }

    public static bool Flag(JsonObject host, string key)
        => new[] { "1", "true", "yes", "on" }.Contains(HostResourceContracts.Text(host, key), StringComparer.OrdinalIgnoreCase);

    public static string? Replacement(JsonObject? configuration, string key)
    {
        var value = HostDefinitionJson.GetNode(configuration, key);
        // Preserve whitespace in a replacement credential; never read persisted secret fields.
        var text = value is JsonValue scalar && scalar.TryGetValue<string>(out var supplied) ? supplied : null;
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
