using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Features.EnvironmentCopy;

public sealed class EnvironmentCopyKeyIdentity(IAutomationKeyCertificateService keys)
{
    public async Task<string> FingerprintAsync(string id, CancellationToken ct)
    {
        var key = await keys.GetAsync(id, ct);
        var fingerprint = EnvironmentCopyObjects.KeyFingerprint(key);
        if (fingerprint.Length > 0) return fingerprint;
        if (key.Kind != Models.KeyCertificateKind.Pgp) throw new InvalidOperationException("A verified key fingerprint is unavailable.");
        var exported = await keys.ExportAsync(new() { RowId = id, IncludePrivateKey = false }, ct);
        return PgpFingerprint(exported.Bytes);
    }
    // RFC 9580 §4.2 and §5.5.4.2/3: hash the primary public-key packet body with its version-specific prefix.
    // https://www.rfc-editor.org/rfc/rfc9580.html#section-5.5.4
    // This reads public packets only. It neither imports/decrypts private keys nor verifies signatures.
    internal static string PgpFingerprint(byte[] export)
    {
        if (export.Length is 0 or > 10 * 1024 * 1024) throw Unsupported();
        byte[] packets = export;
        var text = Encoding.UTF8.GetString(export).TrimStart('\ufeff');
        if (text.TrimStart().StartsWith("-----BEGIN PGP PUBLIC KEY BLOCK-----", StringComparison.Ordinal))
        {
            if (text.IndexOf("-----BEGIN PGP PUBLIC KEY BLOCK-----", text.IndexOf("-----BEGIN", StringComparison.Ordinal) + 10, StringComparison.Ordinal) >= 0) throw Unsupported();
            var lines = text.Replace("\r", "").Trim().Split('\n');
            var body = new StringBuilder(); var started = false;
            foreach (var raw in lines.Skip(1))
            {
                var line = raw.Trim();
                if (line.StartsWith("-----END", StringComparison.Ordinal)) break;
                if (!started && (line.Length == 0 || line.Contains(':'))) { if (line.Length == 0) started = true; continue; }
                if (line.StartsWith('=')) continue; // optional armor CRC24 is not part of the packet body/fingerprint
                started = true; body.Append(line);
            }
            try { packets = Convert.FromBase64String(body.ToString()); } catch (FormatException) { throw Unsupported(); }
        }
        var offset = 0; string? fingerprint = null;
        while (offset < packets.Length)
        {
            var header = packets[offset++]; if ((header & 0x80) == 0) throw Unsupported();
            var modern = (header & 0x40) != 0; var tag = modern ? header & 63 : (header >> 2) & 15;
            int length;
            if (modern)
            {
                if (offset >= packets.Length) throw Unsupported(); var first = packets[offset++];
                if (first < 192) length = first;
                else if (first < 224) { if (offset >= packets.Length) throw Unsupported(); length = ((first - 192) << 8) + packets[offset++] + 192; }
                else if (first == 255) { length = ReadLength(packets, ref offset, 4); }
                else throw Unsupported(); // partial-body public-key packets are not offered as verified identities
            }
            else length = (header & 3) switch { 0 => ReadLength(packets, ref offset, 1), 1 => ReadLength(packets, ref offset, 2), 2 => ReadLength(packets, ref offset, 4), _ => packets.Length - offset };
            if (length < 0 || length > packets.Length - offset || tag is 5 or 7) throw Unsupported();
            if (tag == 6)
            {
                if (fingerprint is not null || length < 6) throw Unsupported();
                var version = packets[offset]; byte[] prefix;
                if (version == 4 && length <= ushort.MaxValue) { prefix = new byte[3]; prefix[0] = 0x99; BinaryPrimitives.WriteUInt16BigEndian(prefix.AsSpan(1), (ushort)length); }
                else if (version == 6) { prefix = new byte[5]; prefix[0] = 0x9b; BinaryPrimitives.WriteUInt32BigEndian(prefix.AsSpan(1), (uint)length); }
                else throw Unsupported();
                using var hash = IncrementalHash.CreateHash(version == 4 ? HashAlgorithmName.SHA1 : HashAlgorithmName.SHA256);
                hash.AppendData(prefix); hash.AppendData(packets, offset, length); fingerprint = Convert.ToHexString(hash.GetHashAndReset());
            }
            offset += length;
        }
        return fingerprint ?? throw Unsupported();
    }
    private static int ReadLength(byte[] bytes, ref int offset, int count)
    {
        if (count > bytes.Length - offset) throw Unsupported(); uint value = 0;
        for (var i = 0; i < count; i++) value = (value << 8) | bytes[offset++];
        if (value > int.MaxValue) throw Unsupported(); return (int)value;
    }
    private static InvalidOperationException Unsupported() => new("The public PGP export could not be verified as one supported public key. No copy was submitted. Inspect this key in MOVEit before retrying.");
}
