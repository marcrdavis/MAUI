using System.Text.Json.Nodes;

namespace MoveItAutomationUI.Services.MoveIt.Hosts;

public sealed partial class HostDefinitionSaveService
{
    // Field-only information edits share the editor's checked-out typed v0 PUT lifecycle.
    // Keep the existing Named revision contract for Information; full editor revisions
    // still compare JSON/native XML through their separate entry point.
    public async Task UpdateBasicDetailsAsync(string hostId, string name, string description, CancellationToken cancellationToken = default)
    {
        var id = (hostId ?? "").Trim(); var hostName = (name ?? "").Trim();
        if (id.Length == 0) throw new ArgumentException("Host ID is required.", nameof(hostId));
        if (hostName.Length is 0 or > 254) throw new InvalidOperationException("Host Name must contain 1–254 characters.");
        var (type, current, wrapper) = await ReadHostForSaveAsync(id, cancellationToken);
        if (type.Length == 0) throw new InvalidOperationException("The Host type is unavailable or cannot be edited.");
        VerifyIdentity(current, id, type, type);
        ValidateLocalFileSystemName(current, id, type, hostName);
        EnsureInformationPermission(wrapper, current);
        var captured = HostCapturedProfiles.TryGet(type, out _);
        if (!captured && type is not ("Share" or "siLock"))
        {
            // Intentional retained transport for unverified types, never a failed-REST fallback.
            await _comClient.UpdateHostBasicDetailsAsync(id, hostName, description ?? "", cancellationToken);
            return;
        }
        await WriteCheckedOutHostAsync(id, type, host =>
        {
            ValidateLocalFileSystemName(host, id, type, hostName);
            ApplyCommonFields(host, hostName, description ?? "");
            if (captured) HostCapturedProfiles.PreserveUnsubmittedSecrets(host, new JsonObject(), type);
            else if (HostDefinitionJson.GetNode(host, "DefPassword") is not null) Set(host, "DefPassword", null);
        }, new JsonObject(), Array.Empty<string>(), cancellationToken, informationOnly: true);
    }

    private static JsonObject InformationRevision(JsonObject host) => ObjectEditVersion.Named(
        HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(host, "Name")),
        HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(host, "Desc")));

    private static void EnsureInformationPermission(JsonObject wrapper, JsonObject host)
    {
        // Match Host inventory semantics: an omitted flag is not a denial.
        var permission = HostDefinitionJson.GetNode(host, "xAddEditDelete") ?? HostDefinitionJson.GetNode(wrapper, "xAddEditDelete");
        if (permission is null) return;
        if (!TryBool(HostDefinitionJson.ReadNode(permission).Trim(), out var allowed))
            throw new InvalidOperationException("The MOVEit Host edit permission could not be verified.");
        if (!allowed) throw new InvalidOperationException("The current MOVEit user cannot edit this Host.");
    }
}
