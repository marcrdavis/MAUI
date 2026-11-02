using System.Text.Json.Nodes;

namespace MoveItAutomationUI.Services.MoveIt.Hosts;

public sealed partial class HostDefinitionSaveService
{
    public Task ValidateRenameAsync(string hostId, string name, string originalName, CancellationToken cancellationToken = default)
        => ValidateRenameCoreAsync(hostId, name, originalName, cancellationToken);

    private async Task<string> ValidateRenameCoreAsync(string hostId, string name, string originalName, CancellationToken cancellationToken)
    {
        var id = RequireRenameId(hostId); var hostName = RequireRenameName(name);
        var (type, current, wrapper) = await ReadHostForSaveAsync(id, cancellationToken);
        if (type.Length == 0) throw new InvalidOperationException("The Host type is unavailable or cannot be edited.");
        VerifyIdentity(current, id, type, type);
        ValidateLocalFileSystemName(current, id, type, hostName);
        EnsureInformationPermission(wrapper, current);
        RequireOriginalName(current, originalName);
        // The existing Host exact-name check is case insensitive. A case-only rename
        // matches this same Host and therefore does not represent a name collision.
        if (!HostResourceContracts.Text(current, "Name").Equals(hostName, StringComparison.OrdinalIgnoreCase)
            && await _restClient.HostNameExistsAsync(hostName, cancellationToken))
            throw new HostNameAlreadyExistsException(hostName);
        return type;
    }

    public async Task RenameAsync(string hostId, string name, string originalName, CancellationToken cancellationToken = default)
    {
        var id = RequireRenameId(hostId); var hostName = RequireRenameName(name);
        var type = await ValidateRenameCoreAsync(id, hostName, originalName, cancellationToken);
        var captured = HostCapturedProfiles.TryGet(type, out _);
        if (!captured && type is not ("Share" or "siLock"))
        {
            await _comClient.RenameHostAsync(id, hostName, originalName, cancellationToken);
            return;
        }
        await WriteCheckedOutHostAsync(id, type, host =>
        {
            ValidateLocalFileSystemName(host, id, type, hostName);
            RequireOriginalName(host, originalName);
            Set(host, "Name", hostName);
            if (captured) HostCapturedProfiles.PreserveUnsubmittedSecrets(host, new JsonObject(), type);
            else if (HostDefinitionJson.GetNode(host, "DefPassword") is not null) Set(host, "DefPassword", null);
        }, new JsonObject(), Array.Empty<string>(), cancellationToken, informationOnly: true);
    }

    private static string RequireRenameId(string hostId)
    {
        var id = (hostId ?? "").Trim();
        if (id.Length == 0) throw new ArgumentException("Host ID is required.", nameof(hostId));
        if (id == "0") throw new InvalidOperationException("The built-in Local File System Host cannot be renamed.");
        return id;
    }

    private static string RequireRenameName(string name)
    {
        var value = (name ?? "").Trim();
        if (value.Length is 0 or > 254) throw new InvalidOperationException("Host Name must contain 1–254 characters.");
        return value;
    }

    private static void RequireOriginalName(JsonObject host, string originalName)
    {
        if (!HostResourceContracts.Text(host, "Name").Equals((originalName ?? "").Trim(), StringComparison.Ordinal))
            throw new ObjectEditConflictException("Host");
    }

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
