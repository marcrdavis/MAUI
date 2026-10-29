// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Models;

public enum KeyCertificateKind
{
    Ssh,
    Pgp,
    Tls
}

public sealed class AutomationKeyCertificate
{
    public string RowId { get; init; } = string.Empty;
    public KeyCertificateKind Kind { get; init; }
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string ExpirationDate { get; init; } = string.Empty;
    public bool? NativeIsExpired { get; init; }
    public string Fingerprint { get; init; } = string.Empty;
    public string KeyType { get; init; } = string.Empty;
    public string KeySize { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string CreatedDate { get; init; } = string.Empty;
    public string PublicPrivate { get; init; } = string.Empty;
    public string SymmetricAlgorithm { get; init; } = string.Empty;
    public string Uid { get; init; } = string.Empty;
    public string IssuedTo { get; init; } = string.Empty;
    public string Issuer { get; init; } = string.Empty;
    public string SerialNumber { get; init; } = string.Empty;
    public string Sha1Thumbprint { get; init; } = string.Empty;
    public string Store { get; init; } = string.Empty;
    public string ValidFromDate { get; init; } = string.Empty;
    public string PublicKeyOpenSsh { get; init; } = string.Empty;
    public string PublicKeySsh { get; init; } = string.Empty;
    public string Group { get; init; } = string.Empty;
    public bool CanEditDelete { get; init; } = true;
}

public sealed record KeyCertificateTaskReference(string Id, string Name);
public sealed record KeyCertificateHostReference(string Id, string Name, string Type);
