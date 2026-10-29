using System.Xml.Linq;

namespace MoveItAutomationUI.Services.MoveIt.Hosts;

// Verify only MAUI-owned fields; MOVEit may add/default unrelated native fields.
// Error text never includes a stored or submitted credential.
internal static class HostDefinitionVerification
{
    private static readonly string[] Attributes =
    {
        "Host", "Port", "UNC", "Secure", "IgnoreCertProbs", "DefUsername", "DefPassword", "DefClientKeyID",
        "ScriptPath", "DefAddressFrom", "DefBucket", "DefContainer", "DefDLInteg", "TenantName", "Site",
        "SiteUrlPrefix", "ClientID", "TenantID", "DefDocumentLibrary", "DefDocumentLibraryGuid",
        "DefClientCertStore", "DefClientCertSubject", "DefClientCertSHA1"
    };

    public static void VerifyCreate(string intendedXml, string savedXml)
        => Verify(null, XElement.Parse(intendedXml), XElement.Parse(savedXml));

    public static void VerifyUpdate(string originalXml, string intendedXml, string savedXml)
        => Verify(XElement.Parse(originalXml), XElement.Parse(intendedXml), XElement.Parse(savedXml));

    private static void Verify(XElement? original, XElement intended, XElement saved)
    {
        foreach (var attribute in Attributes)
        {
            var expected = intended.Attribute(attribute)?.Value;
            if (expected is null) continue;
            if (original is not null && string.Equals(original.Attribute(attribute)?.Value, expected, StringComparison.Ordinal)) continue;
            // Native XML can omit an empty credential/default attribute.
            if (!string.Equals(expected, saved.Attribute(attribute)?.Value ?? "", StringComparison.Ordinal))
                throw new InvalidOperationException("MOVEit accepted the Host write, but a submitted connection field could not be verified. Review the Host before retrying.");
        }
        var expectedDescription = Description(intended);
        if ((original is null || !string.Equals(Description(original), expectedDescription, StringComparison.Ordinal))
            && !string.Equals(expectedDescription, Description(saved), StringComparison.Ordinal))
            throw new InvalidOperationException("MOVEit accepted the Host write, but the submitted description could not be verified. Review the Host before retrying.");
    }

    private static string Description(XElement host)
        => host.Attributes().FirstOrDefault(x => IsDescription(x.Name.LocalName))?.Value
            ?? host.Elements().FirstOrDefault(x => IsDescription(x.Name.LocalName))?.Value ?? "";
    private static bool IsDescription(string name)
        => name.Equals("Desc", StringComparison.OrdinalIgnoreCase) || name.Equals("Description", StringComparison.OrdinalIgnoreCase);

    // MOVEit Windows API documents 4015 as "This section is not checked out".
    // An already-unlocked Host has reached the desired cleanup state.
    public static bool IsCheckoutReleased(bool successful, int? errorCode)
        => ObjectCheckout.IsReleased(successful, errorCode);

    // Shared cleanup outcome for COM updates and REST editor saves.
    public static void EnsureCheckoutReleased(bool saved, Exception? releaseFailure, string hostId)
    {
        if (saved && releaseFailure is not null)
            throw new HostPartialSaveException(
                "MOVEit accepted the Host update, but MAUI could not release the Host checkout. Review the Host in MOVEit and reload it before making more changes.",
                releaseFailure, hostId);
    }
}
