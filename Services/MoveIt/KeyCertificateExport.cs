using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.MoveIt;

public sealed record KeyCertificateExportFile(byte[] Bytes, string FileName);

// Shared native export composition and download normalization. No SSH export contract is supplied.
internal static class KeyCertificateExport
{
    internal static bool CanIncludePrivateKey(AutomationKeyCertificate item)
        => item.Kind == KeyCertificateKind.Tls || item.Kind == KeyCertificateKind.Pgp &&
            (item.PublicPrivate.Equals("Private", StringComparison.OrdinalIgnoreCase)
             || item.PublicPrivate.Equals("Pair", StringComparison.OrdinalIgnoreCase));

    internal static string FileStem(AutomationKeyCertificate item)
    {
        var name = Regex.Replace(item.Name, "[<>:\"/\\\\|?*\\x00-\\x1f]", "_").Trim().Trim('.');
        if (name.Length > 120) name = name[..120];
        return name.Length > 0 ? name : item.Kind == KeyCertificateKind.Tls ? "certificate" : "key";
    }

    internal static JsonObject Compose(AutomationKeyCertificate item, KeyCertificateExportRequest request)
    {
        if (item.Kind == KeyCertificateKind.Ssh) throw new InvalidOperationException("SSH key export is not supported.");
        if (request.IncludePrivateKey && !CanIncludePrivateKey(item))
            throw new InvalidOperationException("This PGP key has no private key to export.");
        if (item.Kind == KeyCertificateKind.Pgp)
            return new JsonObject { ["id"] = item.Id, ["filename"] = FileStem(item), ["exportsecret"] = request.IncludePrivateKey };

        if (string.IsNullOrWhiteSpace(item.Store) || string.IsNullOrWhiteSpace(item.Sha1Thumbprint))
            throw new InvalidOperationException("The TLS certificate export identity is incomplete. Refresh Keys and Certificates and try again.");
        if (request.IncludePrivateKey && string.IsNullOrEmpty(request.Password))
            throw new InvalidOperationException("Enter an export password for the TLS private key.");
        if (request.IncludePrivateKey && !string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
            throw new InvalidOperationException("The export passwords do not match.");
        return new JsonObject
        {
            ["store"] = item.Store, ["issuer"] = item.Issuer, ["serialnum"] = item.SerialNumber,
            ["sha1"] = item.Sha1Thumbprint, ["subject"] = item.IssuedTo, ["filename"] = FileStem(item),
            ["exportprivatekey"] = request.IncludePrivateKey, ["password"] = request.IncludePrivateKey ? request.Password : ""
        };
    }

    internal static KeyCertificateExportFile Download(AutomationKeyCertificate item, bool privateKey, byte[] bytes)
    {
        if (bytes.Length == 0) throw new MoveItApiException("MOVEit returned an empty export.");
        string? text = null;
        try { text = new UTF8Encoding(false, true).GetString(bytes).Trim(); }
        catch (DecoderFallbackException) { /* Binary DER/PKCS#12 or PGP packet data stays byte-for-byte intact. */ }
        if (item.Kind == KeyCertificateKind.Tls && bytes[0] != 0x30 && text?.StartsWith("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal) != true)
        {
            try
            {
                var encoded = text?.StartsWith('"') == true ? JsonSerializer.Deserialize<string>(text) : text;
                bytes = Convert.FromBase64String(encoded ?? "");
                if (bytes.Length == 0 || bytes[0] != 0x30) throw new FormatException();
            }
            catch (Exception ex) when (ex is FormatException or JsonException)
            { throw new MoveItApiException("MOVEit did not return a valid TLS certificate download."); }
        }
        else if (text?.StartsWith('{') == true || text?.StartsWith('[') == true || text?.StartsWith('<') == true)
            throw new MoveItApiException("MOVEit did not return a key or certificate download.");
        var extension = item.Kind == KeyCertificateKind.Tls ? privateKey ? ".pfx" : ".cer"
            : text?.StartsWith("-----BEGIN PGP ", StringComparison.Ordinal) == true ? ".asc" : ".pgp";
        return new(bytes, FileStem(item) + extension);
    }
}
