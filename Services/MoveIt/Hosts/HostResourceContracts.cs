using System.Globalization;
using System.Text.Json.Nodes;

namespace MoveItAutomationUI.Services.MoveIt.Hosts;

public sealed record HostTlsCertificateOption(string Store, string Subject, string Thumbprint, string Expires);
public sealed record HostDocumentLibraryOption(string Name, string Guid);
public sealed record HostDocumentLibraryRequest(string HostId, JsonObject Configuration);

internal static class HostResourceContracts
{
    public static readonly string[] CertificateFields = { "DefClientCertSHA1", "DefClientCertStore", "DefClientCertSubject" };
    public static readonly string[] LibraryFields = { "DefDocumentLibrary", "DefDocumentLibraryGuid" };
    internal static string Text(JsonObject host, string key) => HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(host, key)).Trim();
    internal static string Thumbprint(string text) => string.Concat(text.Where(ch => !char.IsWhiteSpace(ch))).ToUpperInvariant();

    public static IReadOnlyList<HostTlsCertificateOption> Certificates(JsonObject response)
        => Objects(HostDefinitionJson.GetNode(HostDefinitionJson.GetNode(response, "Certs") as JsonObject, "Cert"))
            .Where(cert => !Text(cert, "xIsExpired").Equals("true", StringComparison.OrdinalIgnoreCase) && Text(cert, "xIsExpired") != "1")
            .Select(cert => new HostTlsCertificateOption(Text(cert, "Store"), Text(cert, "IssuedTo"), Text(cert, "SHA1Thumbprint"), Text(cert, "ExpDate")))
            .Where(cert => cert.Store.Length > 0 && cert.Subject.Length > 0 && cert.Thumbprint.Length > 0)
            .OrderBy(cert => cert.Subject, StringComparer.OrdinalIgnoreCase).ToArray();

    public static IReadOnlyList<HostDocumentLibraryOption> Libraries(JsonObject response)
        => Objects(HostDefinitionJson.GetNode(HostDefinitionJson.GetNode(response, "Buckets") as JsonObject, "Bucket"))
            .Select(item => new HostDocumentLibraryOption(Text(item, "Name"), Text(item, "GUID")))
            .Where(item => item.Name.Length > 0 && Guid.TryParse(item.Guid, out _))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    private static IEnumerable<JsonObject> Objects(JsonNode? node)
        => node is JsonArray array ? array.OfType<JsonObject>() : node is JsonObject obj ? new[] { obj } : Array.Empty<JsonObject>();

    public static void ValidateAtomicFields(JsonObject configuration)
    {
        foreach (var fields in new[] { CertificateFields, LibraryFields })
            if (fields.Any(configuration.ContainsKey) && !fields.All(configuration.ContainsKey))
                throw new InvalidOperationException("Select the complete certificate or document library reference before saving.");
        if (LibraryFields.All(configuration.ContainsKey))
        {
            var name = Text(configuration, LibraryFields[0]);
            var guid = Text(configuration, LibraryFields[1]);
            if ((name.Length == 0) != (guid.Length == 0) || (guid.Length > 0 && !Guid.TryParse(guid, out _)))
                throw new InvalidOperationException("A document library requires both its name and a valid GUID, or clear both values.");
        }
    }

    public static void ValidateCertificate(JsonObject configuration, IReadOnlyList<HostTlsCertificateOption> certificates)
    {
        if (!CertificateFields.Any(configuration.ContainsKey)) return;
        ValidateAtomicFields(configuration);
        if (CertificateFields.All(key => Text(configuration, key).Length == 0)) return;
        if (!certificates.Any(cert => cert.Store.Equals(Text(configuration, "DefClientCertStore"), StringComparison.OrdinalIgnoreCase)
            && cert.Subject.Equals(Text(configuration, "DefClientCertSubject"), StringComparison.Ordinal)
            && Thumbprint(cert.Thumbprint) == Thumbprint(Text(configuration, "DefClientCertSHA1"))))
            throw new InvalidOperationException("The selected TLS certificate is no longer available or valid. Refresh the certificate list.");
    }
}

// Shared captured SharePoint browse composition for Host editors, Tasks and Quick Transfer.
internal static class HostSharePointBrowseContract
{
    public static JsonObject Build(string hostId, JsonObject host, string directory = "", string mask = "",
        string documentLibrary = "", string documentLibraryGuid = "", string taskId = "", string stepId = "", JsonObject? replacements = null)
    {
        string Text(string key) => HostResourceContracts.Text(host, key);
        bool Flag(string key) => Text(key) is "1" || new[] { "true", "yes", "on" }.Contains(Text(key), StringComparer.OrdinalIgnoreCase);
        int Number(string key) => int.TryParse(Text(key), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;
        var mode = Number("UsingAppAuthentication");
        if (mode is not (1 or 2)) throw new InvalidOperationException("SharePoint Browse requires App Access or Certificate Access.");
        var required = mode == 1 ? new[] { "TenantName", "ClientID", "RealmID" }
            : new[] { "TenantName", "ClientID", "TenantID", "DefClientCertStore", "DefClientCertSubject", "DefClientCertSHA1" };
        if (required.Any(key => Text(key).Length == 0)) throw new InvalidOperationException("Complete the selected SharePoint authentication settings before browsing.");
        var login = new JsonObject
        {
            ["secure"] = Flag("Secure"), ["conntimeoutsecs"] = Number("DefConnTimeoutSecs"), ["datatimeoutsecs"] = Number("DefDataTimeoutSecs"),
            ["proxytype"] = "None", ["proxypassword"] = null, ["tenantname"] = Text("TenantName"), ["authenticationType"] = mode,
            ["clientid"] = Text("ClientID"),
            ["proxyignorecerterrors"] = Flag("ProxyIgnoreCertErrors")
        };
        if (mode == 1)
        {
            login["realmid"] = Text("RealmID");
            login["clientsecret"] = HostBrowseSettings.Replacement(replacements, "ClientSecret");
        }
        else
        {
            login["tenantid"] = Text("TenantID");
            login["clientcertstore"] = Text("DefClientCertStore");
            login["clientcertsubject"] = Text("DefClientCertSubject");
            login["clientcertsha1"] = Text("DefClientCertSHA1");
        }
        HostBrowseSettings.ApplyProxy(login, host, "SharePoint", replacements);
        return new JsonObject
        {
            ["showfiles"] = documentLibrary.Length > 0, ["mask"] = mask, ["hostid"] = hostId, ["logininfo"] = login,
            ["dirname"] = directory, ["taskid"] = taskId.Length == 0 ? null : taskId, ["stepid"] = stepId.Length == 0 ? null : stepId,
            ["documentlibrary"] = documentLibrary, ["documentlibraryguid"] = documentLibraryGuid.Length > 0 ? documentLibraryGuid : Text("DefDocumentLibraryGuid"),
            ["site"] = Text("Site"), ["siteurlprefix"] = Text("SiteUrlPrefix")
        };
    }
}
