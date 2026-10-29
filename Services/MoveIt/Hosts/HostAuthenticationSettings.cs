using System.Text.Json.Nodes;

namespace MoveItAutomationUI.Services.MoveIt.Hosts;

// Editor commands are translated here; only captured native credential fields enter a PUT.
internal static class HostAuthenticationSettings
{
    internal const string UncModeKey = "UncAuthenticationMode";
    internal static string UncMode(JsonObject host)
        => HostResourceContracts.Text(host, "DefUsername").Length == 0 ? "run-as" : "credentials";

    internal static bool HasPasswordReplacement(JsonObject patch)
    {
        var value = HostDefinitionJson.GetNode(patch, "DefPassword");
        if (value is null) return false;
        if (value is not JsonValue json || !json.TryGetValue<string>(out var password))
            throw new InvalidOperationException("A replacement password must be text.");
        return password.Length > 0;
    }

    internal static void ApplyPassword(JsonObject host, JsonObject patch)
    {
        if (HasPasswordReplacement(patch)) SetNative(host, "DefPassword", HostDefinitionJson.GetNode(patch, "DefPassword")!.DeepClone());
    }

    internal static void PreservePassword(JsonObject host, JsonObject patch)
    {
        // GET may contain stored ciphertext. Null preserves it; never submit ciphertext as a new password.
        if (!HasPasswordReplacement(patch)) SetNative(host, "DefPassword", null);
    }

    internal static void ApplyUnc(JsonObject host, JsonObject patch)
    {
        var mode = patch.ContainsKey(UncModeKey) ? HostResourceContracts.Text(patch, UncModeKey) : UncMode(host);
        if (mode is not ("run-as" or "credentials")) throw new InvalidOperationException("Select a supported UNC authentication mode.");
        if (mode == "run-as")
        {
            if (HostResourceContracts.Text(patch, "DefUsername").Length > 0 || HasPasswordReplacement(patch))
                throw new InvalidOperationException("Specified credentials do not apply when MOVEit Automation Run-As credentials are selected.");
            Remove(host, "DefUsername"); Remove(host, "DefPassword");
            return;
        }
        if (HostResourceContracts.Text(host, "DefUsername").Length == 0)
            throw new InvalidOperationException("Username is required when using specified UNC credentials.");
        ApplyPassword(host, patch);
    }

    internal static bool UncModeChanged(JsonObject before, JsonObject after, JsonObject patch)
        => patch.ContainsKey(UncModeKey) && UncMode(before) != UncMode(after);

    internal static void VerifyUnc(JsonObject expected, JsonObject actual, JsonObject patch)
    {
        if (patch.ContainsKey(UncModeKey) && UncMode(expected) != UncMode(actual))
            throw new InvalidOperationException("MOVEit did not persist the requested UNC authentication mode.");
    }

    private static void Remove(JsonObject host, string key)
    {
        foreach (var name in host.Select(pair => pair.Key).Where(name => name.Equals(key, StringComparison.OrdinalIgnoreCase)).ToArray()) host.Remove(name);
    }
    internal static void SetNative(JsonObject host, string key, JsonNode? value)
    {
        var actual = host.Select(pair => pair.Key).FirstOrDefault(name => name.Equals(key, StringComparison.OrdinalIgnoreCase)) ?? key;
        Remove(host, key);
        host[actual] = value;
    }
}

// Microsoft 365 is a UI helper over the same native OAuth scope/token URL contract as Custom.
internal static class HostMailOAuthSettings
{
    internal const string ProviderKey = "OAuthProvider";
    internal const string TenantKey = "OAuthTenantID";
    internal const string Scope = "https://outlook.office365.com/.default";
    internal static bool IsField(string key) => key is ProviderKey or TenantKey;
    internal static string Tenant(JsonObject host)
    {
        if (HostResourceContracts.Text(host, "OAuthScope") != Scope
            || !Uri.TryCreate(HostResourceContracts.Text(host, "OAuthTokenURL"), UriKind.Absolute, out var url)
            || url.Scheme != "https" || !url.IsDefaultPort || url.Host != "login.microsoftonline.com"
            || url.Query.Length > 0 || url.Fragment.Length > 0 || url.UserInfo.Length > 0) return "";
        var parts = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 4 && parts[1] == "oauth2" && parts[2] == "v2.0" && parts[3] == "token"
            && ValidTenant(parts[0]) ? parts[0] : "";
    }
    internal static string Provider(JsonObject host) => Tenant(host).Length > 0
        || HostResourceContracts.Text(host, "OAuthScope").Length == 0 && HostResourceContracts.Text(host, "OAuthTokenURL").Length == 0 ? "microsoft365" : "custom";
    private static bool ValidTenant(string tenant) => tenant.Length > 0 && char.IsAsciiLetterOrDigit(tenant[0])
        && char.IsAsciiLetterOrDigit(tenant[^1]) && tenant.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '.');

    internal static void Apply(JsonObject host, JsonObject patch)
    {
        if (!patch.Any(pair => IsField(pair.Key))) return;
        if (HostResourceContracts.Text(host, "UseOAuth") is not ("1" or "true"))
            throw new InvalidOperationException("OAuth provider settings apply only when OAuth authentication is enabled.");
        var provider = patch.ContainsKey(ProviderKey) ? HostResourceContracts.Text(patch, ProviderKey) : Provider(host);
        if (provider is not ("custom" or "microsoft365")) throw new InvalidOperationException("Select a supported OAuth provider.");
        if (provider == "custom")
        {
            if (patch.ContainsKey(TenantKey)) throw new InvalidOperationException("Tenant ID applies only to Microsoft Office 365 OAuth.");
            return;
        }
        var tenant = patch.ContainsKey(TenantKey) ? HostResourceContracts.Text(patch, TenantKey) : Tenant(host);
        if (!ValidTenant(tenant)) throw new InvalidOperationException("Enter a Microsoft Office 365 Tenant ID or tenant domain without URL separators.");
        HostAuthenticationSettings.SetNative(host, "OAuthScope", JsonValue.Create(Scope));
        HostAuthenticationSettings.SetNative(host, "OAuthTokenURL", JsonValue.Create($"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token"));
        HostAuthenticationSettings.SetNative(host, "Secure", JsonValue.Create(1));
    }

    internal static void Verify(JsonObject expected, JsonObject actual, JsonObject patch)
    {
        if (!patch.Any(pair => IsField(pair.Key))) return;
        foreach (var key in new[] { "OAuthScope", "OAuthTokenURL", "Secure" })
            if (key == "Secure" ? (HostResourceContracts.Text(expected, key) is "1" or "true") != (HostResourceContracts.Text(actual, key) is "1" or "true")
                : HostResourceContracts.Text(expected, key) != HostResourceContracts.Text(actual, key))
                throw new InvalidOperationException("MOVEit did not persist the requested mail OAuth settings.");
    }

    internal static bool HasChanges(JsonObject before, JsonObject after, JsonObject patch)
        => patch.Any(pair => IsField(pair.Key)) && new[] { "OAuthScope", "OAuthTokenURL", "Secure" }
            .Any(key => HostResourceContracts.Text(before, key) != HostResourceContracts.Text(after, key));
}
