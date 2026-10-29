using System.Text.Json.Nodes;

namespace MoveItAutomationUI.Services.MoveIt.Hosts;

// UI commands are translated to the supplied native fields; they never enter a REST PUT.
internal static class HostCloudAuthenticationSettings
{
    internal const string OneDriveModeKey = "OneDriveAuthenticationMode";
    internal const string S3ProfileKey = "S3ProfileName";
    internal static bool IsCommand(string key) => key is OneDriveModeKey or S3ProfileKey;
    internal static string Text(JsonObject host, string key) => HostResourceContracts.Text(host, key);
    internal static int S3Method(JsonObject host)
    {
        var text = Text(host, "AuthMethod");
        if (text.Length == 0) return 0; // Older Hosts omit the access-key default.
        if (int.TryParse(text, out var mode) && mode is >= 0 and <= 2) return mode;
        throw new InvalidOperationException("This Amazon S3 authentication method is not supported by MAUI.");
    }
    internal static string OneDriveMode(JsonObject host)
        => HostResourceContracts.CertificateFields.Any(key => Text(host, key).Length > 0)
            ? "certificate" : host.Any(pair => pair.Key.Equals("OAuthClientSecret", StringComparison.OrdinalIgnoreCase)) ? "client-secret" : "certificate";
    internal static string Value(JsonObject host, string key) => key switch
    {
        OneDriveModeKey => OneDriveMode(host),
        "AuthMethod" => S3Method(host).ToString(System.Globalization.CultureInfo.InvariantCulture),
        S3ProfileKey => S3Method(host) == 1 ? Text(host, "DefUsername") : "",
        _ => Text(host, key)
    };
    private static void Remove(JsonObject host, string key)
    {
        foreach (var name in host.Select(x => x.Key).Where(x => x.Equals(key, StringComparison.OrdinalIgnoreCase)).ToArray()) host.Remove(name);
    }

    internal static void ApplyModes(JsonObject host, JsonObject patch, string type)
    {
        if (type == "OneDrive")
        {
            var previousMode = OneDriveMode(host);
            var mode = patch.ContainsKey(OneDriveModeKey) ? Text(patch, OneDriveModeKey) : OneDriveMode(host);
            if (mode is not ("certificate" or "client-secret")) throw new InvalidOperationException("Select a supported OneDrive authentication method.");
            if (mode == "client-secret" && previousMode != mode && Text(patch, "OAuthClientSecret").Length == 0)
                throw new InvalidOperationException("Enter a Client Secret when switching to Client Secret Authentication.");
            if (mode == "client-secret")
            {
                foreach (var key in HostResourceContracts.CertificateFields) Remove(host, key);
                if (!host.ContainsKey("OAuthClientSecret")) host["OAuthClientSecret"] = null;
            }
            else Remove(host, "OAuthClientSecret");
            HostAuthenticationSettings.SetNative(host, "UseOAuth", JsonValue.Create(1));
            HostAuthenticationSettings.SetNative(host, "DefPassword", null);
        }
        if (type != "S3") return;
        var before = S3Method(host);
        var modeAfter = patch.ContainsKey("AuthMethod") ? S3Method(patch) : before;
        if (before != modeAfter && modeAfter == 0 && !HostAuthenticationSettings.HasPasswordReplacement(patch))
            throw new InvalidOperationException("Enter a Secret Access Key when switching to Access Keys.");
        if (patch.ContainsKey("AuthMethod")) HostAuthenticationSettings.SetNative(host, "AuthMethod", JsonValue.Create(modeAfter));
        if (modeAfter != before)
        {
            Remove(host, "DefUsername"); Remove(host, "DefPassword");
        }
        if (modeAfter != 0)
        {
            Remove(host, "DefPassword"); Remove(host, "IsNonAmazonEndPoint"); Remove(host, "S3AlternativeEndpoint");
            if (modeAfter == 2) Remove(host, "DefUsername");
        }
        if (patch.ContainsKey(S3ProfileKey))
        {
            if (modeAfter != 1) throw new InvalidOperationException("An AWS profile name applies only to AWS Config File authentication.");
            HostAuthenticationSettings.SetNative(host, "DefUsername", JsonValue.Create(Text(patch, S3ProfileKey)));
        }
    }

    internal static bool HasChanges(JsonObject before, JsonObject after, JsonObject patch, string type)
        => type == "OneDrive" && patch.ContainsKey(OneDriveModeKey) && OneDriveMode(before) != OneDriveMode(after)
            || type == "S3" && patch.ContainsKey(S3ProfileKey) && Text(before, "DefUsername") != Text(after, "DefUsername");

    internal static void Verify(JsonObject expected, JsonObject actual, JsonObject patch, string type)
    {
        if (type == "OneDrive" && patch.ContainsKey(OneDriveModeKey) && OneDriveMode(expected) != OneDriveMode(actual))
            throw new InvalidOperationException("MOVEit did not persist the selected OneDrive authentication method.");
        if (type == "S3" && (patch.ContainsKey("AuthMethod") && S3Method(expected) != S3Method(actual)
            || patch.ContainsKey(S3ProfileKey) && Text(expected, "DefUsername") != Text(actual, "DefUsername")))
            throw new InvalidOperationException("MOVEit did not persist the requested Amazon S3 authentication settings.");
    }

    internal static void ApplyS3Browse(JsonObject request, JsonObject host)
    {
        var mode = S3Method(host);
        if (HostDefinitionJson.GetNode(host, "AuthMethod") is not null) request["authmethod"] = mode;
        request["endpoint"] = mode == 0 && HostBrowseSettings.Flag(host, "IsNonAmazonEndPoint") ? Text(host, "S3AlternativeEndpoint") : "";
        if (mode == 2 && request["logininfo"] is JsonObject login) login["username"] = "";
    }

    internal static JsonObject OneDriveBrowse(string hostId, JsonObject host, string directory = "", string mask = "", bool showFiles = false, string taskId = "", string stepId = "")
    {
        var login = new JsonObject { ["secure"] = true, ["username"] = Text(host, "OAuthClientID"), ["password"] = null,
            ["conntimeoutsecs"] = ReadTimeout(host, "DefConnTimeoutSecs"), ["datatimeoutsecs"] = ReadTimeout(host, "DefDataTimeoutSecs") };
        HostBrowseSettings.ApplyProxy(login, host, "OneDrive");
        var request = new JsonObject { ["showfiles"] = showFiles, ["mask"] = mask, ["hostid"] = hostId, ["logininfo"] = login,
            ["dirname"] = directory, ["taskid"] = taskId.Length == 0 ? null : taskId, ["stepid"] = stepId.Length == 0 ? null : stepId,
            [""] = "", ["tenantid"] = Text(host, "TenantID"), ["username"] = Text(host, "DefUsername") };
        if (OneDriveMode(host) == "certificate")
            foreach (var key in HostResourceContracts.CertificateFields)
                request[key[3..].ToLowerInvariant()] = Text(host, key);
        return request; // Saved client secrets are resolved by hostid, as in the supplied capture.
    }
    private static int ReadTimeout(JsonObject host, string key) => int.TryParse(Text(host, key), out var value) ? value : 60;
}
