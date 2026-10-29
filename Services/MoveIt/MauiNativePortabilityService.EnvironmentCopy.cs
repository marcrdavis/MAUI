using System.Text.Json.Nodes;
using System.Xml.Linq;
using MoveItAutomationUI.Features.EnvironmentCopy;
using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Services.MoveIt;

public sealed record NativeEnvironmentCopyRequest(string Type, string SourceId, string Xml, string Passphrase, string Name,
    string TargetId, string ExpectedRevision, IReadOnlyList<string>? Groups, bool PreserveCredentials, string DestinationPortableXml = "");
public sealed record NativeEnvironmentCopyResult(string Id, JsonObject Definition);
public interface IEnvironmentCopyNativeTransfer
{
    Task<NativeEnvironmentCopyResult> CopyAsync(NativeEnvironmentCopyRequest request, Func<string, Task> recordKnownId, CancellationToken ct = default);
    Task<string> CopyKeyAsync(string type, string sourceId, string xml, string passphrase, IReadOnlyList<string> groups, Func<string, Task> recordKnownId, CancellationToken ct = default);
}

// Reuses the existing protected native export/encode/import boundary. No editor PUT writer is duplicated.
public sealed partial class MauiNativePortabilityService : IEnvironmentCopyNativeTransfer
{
    public async Task<NativeEnvironmentCopyResult> CopyAsync(NativeEnvironmentCopyRequest request, Func<string, Task> recordKnownId, CancellationToken ct = default)
    {
        if (request.Type is not ("Task" or "Host")) throw new ArgumentException("Native copy supports Task and Host definitions.");
        var session = RequireSession();
        var creating = request.TargetId.Length == 0;
        using var server = ObjectCheckout.ForServer(session.WebAdminUrl);
        using var creation = creating ? request.Type == "Task"
            ? await MoveItLicensePolicy.AcquireTaskCreationAsync(session, ct)
            : await MoveItLicensePolicy.AcquireHostCreationAsync(session, ct) : null;

        async Task<NativeEnvironmentCopyResult> Commit(CancellationToken token)
        {
            var inventory = await LoadDestinationInventoryAsync(token);
            if (creating)
            {
                var exists = request.Type == "Task" ? inventory.Tasks.Any(x => x.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase))
                    : inventory.Hosts.Any(x => x.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase));
                if (exists) throw new InvalidOperationException("The destination name now exists. Analyze and resolve again before copying.");
                var block = request.Type == "Task" ? MoveItLicensePolicy.GetTaskCapacityBlock(session, inventory.Tasks.Count, 1)
                    : MoveItLicensePolicy.GetHostCapacityBlock(session, inventory.Hosts.Count, 1);
                if (!string.IsNullOrEmpty(block)) throw new InvalidOperationException(block);
            }
            else
            {
                // Recheck the reviewed definition only after the shared checkout has been acquired.
                var current = request.Type == "Task" ? (await _taskCrud.GetNativeConfigurationAsync(request.TargetId, token)).Xml
                    : (await _hosts.GetHostConfigurationSnapshotAsync(request.TargetId, token)).Xml;
                var revision = ObjectEditVersion.Create("EnvironmentCopy", request.Type + "|" + request.TargetId, EnvironmentCopyRules.RevisionXml(current));
                if (revision != request.ExpectedRevision) throw new InvalidOperationException("The destination changed after review. Analyze and resolve it again before overwriting.");
                if (request.Type == "Task" && inventory.Tasks.Any(x => x.Id == request.TargetId && x.Status.Equals("Running", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("The destination Task is running and cannot be overwritten.");
            }

            var document = XDocument.Parse(request.Xml);
            var objects = EnumerateNativeXmlObjects(document);
            if (objects.Count != 1 || objects[0].NativeObjectType != request.Type || objects[0].SourceId != (creating ? request.SourceId : request.TargetId))
                throw new InvalidOperationException("The protected copy contains an unexpected object. No import was submitted.");
            if (request.PreserveCredentials && !creating && request.Type == "Host")
                PreserveHostCredentials(document, XDocument.Parse(request.DestinationPortableXml));
            var decoded = await DecodeNativeImportAsync(SerializeCopyDocument(document), request.Passphrase, token);
            var imported = EnumerateImportObjects(decoded);
            if (imported.Count != 1 || imported[0].ObjectType != request.Type)
                throw new InvalidOperationException("MOVEit decoded unexpected dependencies. No import was submitted.");
            var obj = imported[0];
            if (request.Type == "Task")
            {
                obj.Node["Active"] = 0;
                var block = MoveItLicensePolicy.GetAdvancedTaskCreationBlock(session, TaskTypeFromCode(GetString(obj.Node, "TT")));
                if (!string.IsNullOrEmpty(block)) throw new InvalidOperationException(block);
            }
            if (creating) ApplyRenameResolution(obj.Node, request.Name);
            else
            {
                // Encode must identify exactly the reviewed target; a different name/ID collision is never approved implicitly.
                var target = request.Type == "Task" ? inventory.Tasks.Where(x => x.Id == request.TargetId).Select(x => x.Name).SingleOrDefault()
                    : inventory.Hosts.Where(x => x.Id == request.TargetId).Select(x => x.Name).SingleOrDefault();
                if (target != request.Name || obj.SourceId != request.TargetId || obj.SourceName != request.Name)
                    throw new InvalidOperationException("The destination identity no longer matches the reviewed overwrite target.");
                var nameCollision = request.Type == "Task" ? inventory.Tasks.Any(x => x.Id != request.TargetId && x.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase))
                    : inventory.Hosts.Any(x => x.Id != request.TargetId && x.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase));
                if (nameCollision) throw new InvalidOperationException("The destination name is ambiguous. No import was submitted.");
            }
            if (request.Groups is not null) obj.Node["Group"] = new JsonArray(request.Groups.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
            await CommitNativeImportAsync(decoded, request.Passphrase, CancellationToken.None);
            // Native config/import does not return per-object IDs. Resolve the unique reviewed name once; never replay the import.
            var savedInventory = await LoadDestinationInventoryAsync(CancellationToken.None);
            var id = creating ? request.Type == "Task"
                ? savedInventory.Tasks.SingleOrDefault(x => x.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase))?.Id
                : savedInventory.Hosts.SingleOrDefault(x => x.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase))?.Id
                : request.TargetId;
            if (string.IsNullOrEmpty(id)) throw new NativePortabilityImportOutcomeUnknownException("The native copy completed, but its destination ID could not be confirmed. Inspect the destination before retrying.", new InvalidOperationException());
            await recordKnownId(id);
            var saved = request.Type == "Task" ? await _taskCrud.GetDefinitionAsync(id, CancellationToken.None)
                : HostDefinitionJson.UnwrapHost(await _rest.GetHostAsync(id, CancellationToken.None)).Host;
            EnvironmentCopyVerification.Verify(obj.Node, saved, request.Type, id, request.Name);
            return new(id, saved);
        }
        if (creating) return await Commit(ct);
        return await ObjectCheckout.ExecuteAsync(request.Type, request.TargetId,
            async token => { if (request.Type == "Task") await _rest.CheckoutTaskV0Async(request.TargetId, token); else _ = await _rest.CheckoutHostV0Async(request.TargetId, token); },
            Commit, token => request.Type == "Task" ? _rest.ReleaseTaskCheckoutV0Async(request.TargetId, token) : _rest.ReleaseHostCheckoutV0Async(request.TargetId, token), ct);
    }

    public async Task<string> CopyKeyAsync(string type, string sourceId, string xml, string passphrase, IReadOnlyList<string> groups, Func<string, Task> recordKnownId, CancellationToken ct = default)
    {
        if (!IsKeyType(type)) throw new ArgumentException("Choose a supported key/certificate type.");
        var kind = KeyKindFromObjectType(type);
        var inventory = (await _keys.GetSnapshotAsync(true, ct)).Items;
        var decoded = await DecodeNativeImportAsync(IsolateCopyObject(xml, type, sourceId), passphrase, ct);
        var imported = EnumerateImportObjects(decoded);
        if (imported.Count != 1 || imported[0].ObjectType != type || imported[0].SourceId != sourceId)
            throw new InvalidOperationException("MOVEit decoded an unexpected key dependency. No import was submitted.");
        var obj = imported[0];
        if (inventory.Any(x => x.Kind == kind && (x.Id.Equals(sourceId, StringComparison.OrdinalIgnoreCase) || x.Name.Equals(obj.SourceName, StringComparison.OrdinalIgnoreCase)))
            || ReadDuplicates(decoded).Count > 0) throw new InvalidOperationException("A key ID/name conflict now exists. Resolve this key again before copying.");
        obj.Node["Group"] = new JsonArray(groups.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
        await CommitNativeImportAsync(decoded, passphrase, CancellationToken.None);
        var added = (await _keys.GetSnapshotAsync(true, CancellationToken.None)).Items.Where(x => x.Kind == kind && !inventory.Any(old => old.RowId == x.RowId)).ToArray();
        var saved = added.SingleOrDefault(x => x.Id.Equals(sourceId, StringComparison.OrdinalIgnoreCase))
            ?? added.SingleOrDefault(x => x.Name.Equals(obj.SourceName, StringComparison.OrdinalIgnoreCase));
        if (saved is null) throw new NativePortabilityImportOutcomeUnknownException("MOVEit accepted the key import, but its destination ID could not be confirmed. Inspect Keys and Certificates before retrying.", new InvalidOperationException());
        await recordKnownId(saved.RowId); return saved.RowId;
    }

    internal static string IsolateCopyObject(string nativeXml, string type, string id)
    {
        var document = ParseExportDocument(nativeXml); var root = document.Root!;
        var containers = new HashSet<string>(["Tasks", "Hosts", "Scripts", "DateLists", "SSHClientKeys", "SSLClientCerts", "PGPKeys"], StringComparer.Ordinal);
        foreach (var container in root.Elements().ToArray())
        {
            if (!containers.Contains(container.Name.LocalName)) { container.Remove(); continue; }
            foreach (var element in container.Elements().ToArray())
            {
                var actualType = container.Name.LocalName == "Hosts" ? "Host" : element.Name.LocalName;
                if (actualType != type || (string?)element.Attribute("ID") != id) element.Remove();
            }
        }
        var remaining = root.Elements().SelectMany(x => x.Elements()).ToArray();
        if (remaining.Length != 1 || (type != "Host" && remaining[0].Name.LocalName != type) || (string?)remaining[0].Attribute("ID") != id)
            throw new InvalidOperationException("The protected native export did not contain the requested object.");
        return SerializeCopyDocument(document);
    }
    internal static string RewriteCopyObject(string xml, string type, string oldId, string newId, string name,
        IReadOnlyDictionary<string, string> mappings, IReadOnlyList<string>? groups)
    {
        var document = XDocument.Parse(IsolateCopyObject(xml, type, oldId));
        var container = document.Root!.Element(type == "Task" ? "Tasks" : "Hosts")!;
        var element = container.Elements().Single();
        element.ReplaceWith(XElement.Parse(EnvironmentCopyRules.Rewrite(element.ToString(), newId, name, mappings, groups, disableTask: type == "Task")));
        return SerializeCopyDocument(document);
    }
    private static string SerializeCopyDocument(XDocument document) => document.ToString(SaveOptions.DisableFormatting)
        .Replace("<Tasks />", "<Tasks></Tasks>", StringComparison.Ordinal).Replace("<Tasks/>", "<Tasks></Tasks>", StringComparison.Ordinal);
    private static void PreserveHostCredentials(XDocument source, XDocument destination)
    {
        var from = destination.Root?.Element("Hosts")?.Elements().SingleOrDefault() ?? throw new InvalidOperationException("Destination protected credentials are unavailable.");
        var to = source.Root!.Element("Hosts")!.Elements().Single();
        if (from.Name != to.Name) throw new InvalidOperationException("Credentials cannot be retained across different Host types.");
        static bool Credential(string name) => EnvironmentCopyRules.IsSecret(name) || name is "DefUsername" or "Username" or "DefAccount" or "Account" or "ClientID" or "OAuthClientID" or "TenantID" or "OAuthTenantID" or "UseOAuth" or "OAuthScope" or "OAuthTokenURL"
            or "DefClientKeyID" or "DefClientCertSHA1" or "DefClientCertStore" or "DefClientCertSubject";
        to.Attributes().Where(a => Credential(a.Name.LocalName)).Remove();
        foreach (var attribute in from.Attributes().Where(a => Credential(a.Name.LocalName))) to.Add(new XAttribute(attribute));
        to.Elements().Where(e => Credential(e.Name.LocalName)).Remove();
        foreach (var element in from.Elements().Where(e => Credential(e.Name.LocalName))) to.Add(new XElement(element));
    }
}
