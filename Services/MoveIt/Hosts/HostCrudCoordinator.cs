using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.MoveIt.Hosts;

// An editor patch represents explicit values, including clearing nonsecret fields.
// Spreadsheet updates keep their separate sparse, blank-means-preserve contract.
public sealed record HostDefinitionSaveRequest(string Name, string Description, JsonObject Configuration);
public sealed record HostDefinitionSaveResult(bool Changed, string HostName, string TypeCode, string UndoSnapshotId = "");

public sealed class HostPartialSaveException : InvalidOperationException
{
    public string HostId { get; }
    public string VerificationStage { get; }
    public string VerificationReason { get; }
    public HostPartialSaveException(string message, Exception innerException, string hostId = "", string stage = "", string reason = "")
        : base(message + (stage.Length > 0 ? $" Save stage: {stage} ({reason})." : ""), innerException)
        => (HostId, VerificationStage, VerificationReason) = (hostId, stage, reason);
}

/// <summary>
/// Single object-level Host CRUD boundary. COM owns native XML operations; the shared
/// save policy owns captured v0 REST editor patches. Feature callers never choose a
/// different transport as a retry after an uncertain write.
/// </summary>
public interface IHostCrudCoordinator
{
    Task<IReadOnlyList<JsonObject>> GetDefinitionsAsync(CancellationToken cancellationToken = default);
    Task<JsonObject> GetDefinitionAsync(string hostId, CancellationToken cancellationToken = default);
    Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken = default);
    Task<MoveItCreatedHost> CreateShellAsync(string name, string type, CancellationToken cancellationToken = default, string? unc = null, string? resourceGroup = null);
    Task<MoveItCreatedHost> CloneAsync(string hostId, string name, CancellationToken cancellationToken = default);
    Task<MoveItCreatedHost> CreateConfiguredAsync(HostBasicDefinition definition, CancellationToken cancellationToken = default);
    Task<MoveItHostConfiguration> UpdateConfiguredAsync(string hostId, HostBasicUpdate update, CancellationToken cancellationToken = default);
    Task UpdateBasicDetailsAsync(string hostId, string name, string description, CancellationToken cancellationToken = default);
    Task<MoveItHostConfiguration> GetNativeConfigurationAsync(string hostId, CancellationToken cancellationToken = default);
    Task<MoveItHostConfiguration> RestoreNativeConfigurationAsync(string hostId, string xml, CancellationToken cancellationToken = default);
    Task DeleteAsync(string hostId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HostSshClientKeyOption>> GetSshClientKeysAsync(CancellationToken cancellationToken = default);
    Task<HostSshAlgorithmCatalog> GetSshAlgorithmsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HostTlsCertificateOption>> GetTlsCertificatesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HostDocumentLibraryOption>> BrowseDocumentLibrariesAsync(HostDocumentLibraryRequest request, CancellationToken cancellationToken = default);
    Task<string> RetrieveSshHostFingerprintAsync(HostFingerprintRequest request, CancellationToken cancellationToken = default);
    Task<HostDefinitionSaveResult> SaveDefinitionAsync(string hostId, HostDefinitionSaveRequest request,
        string username, HostCacheSnapshot liveSnapshot, CancellationToken cancellationToken = default);
}

public sealed class HostCrudCoordinator : IHostCrudCoordinator
{
    private readonly IMoveItRestClient _rest;
    private readonly IMoveItObjectAccessService? _access;
    private readonly IMoveItComClient _com;
    private readonly IHostDefinitionSaveService _saves;
    private readonly IMoveItSessionService? _sessions;
    private readonly IOptionsSnapshot<SshAlgorithmOptions>? _algorithmDefaults;
    public HostCrudCoordinator(IMoveItRestClient rest, IMoveItComClient com, IHostDefinitionSaveService saves,
        IOptionsSnapshot<SshAlgorithmOptions>? algorithmDefaults = null, IMoveItObjectAccessService? access = null, IMoveItSessionService? sessions = null)
        => (_rest, _com, _saves, _algorithmDefaults, _access, _sessions) = (rest, com, saves, algorithmDefaults, access, sessions);

    public Task<IReadOnlyList<JsonObject>> GetDefinitionsAsync(CancellationToken cancellationToken = default)
        => _rest.GetHostsAsync(cancellationToken);
    public Task<JsonObject> GetDefinitionAsync(string hostId, CancellationToken cancellationToken = default)
        => _rest.GetHostAsync(Id(hostId), cancellationToken);
    public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken = default)
        => _rest.HostNameExistsAsync(name, cancellationToken);
    public async Task<MoveItCreatedHost> CreateShellAsync(string name, string type, CancellationToken cancellationToken = default, string? unc = null, string? resourceGroup = null)
    {
        if (_access is not null) resourceGroup = await _access.RequireCreationAsync("Host", resourceGroup, cancellationToken);
        if (string.Equals(type, "OneDrive", StringComparison.OrdinalIgnoreCase)) MoveItFeatureSupport.Require(_sessions?.Get()?.Info, MoveItFeature.OneDriveHosts);
        return await HostV0Creation.CreateAsync(_rest, name, type, cancellationToken, unc, _algorithmDefaults?.Value, resourceGroup);
    }
    public async Task<MoveItCreatedHost> CloneAsync(string hostId, string name, CancellationToken cancellationToken = default)
    {
        var id = Id(hostId);
        var source = await GetNativeConfigurationAsync(id, cancellationToken);
        var definition = HostCloneDefinition.Read(source.Xml, id);
        if (_access is not null)
        {
            await _access.RequireAsync("Host", id, "Edit", cancellationToken);
            await _access.RequireXmlCreationAsync("Host", definition, cancellationToken);
        }
        return await _com.CreateHostFromConfigurationAsync(name, source.Xml, cancellationToken);
    }
    public async Task<MoveItCreatedHost> CreateConfiguredAsync(HostBasicDefinition definition, CancellationToken cancellationToken = default)
    {
        if (_access is not null) await _access.RequireCreationAsync("Host", definition.ResourceGroup, cancellationToken);
        return await _com.CreateConfiguredHostAsync(definition, cancellationToken);
    }
    public async Task<MoveItHostConfiguration> UpdateConfiguredAsync(string hostId, HostBasicUpdate update, CancellationToken cancellationToken = default)
    {
        if (_access is not null) await _access.RequireAsync("Host", Id(hostId), "Edit", cancellationToken);
        return await _com.UpdateConfiguredHostAsync(Id(hostId), update, cancellationToken);
    }
    public async Task UpdateBasicDetailsAsync(string hostId, string name, string description, CancellationToken cancellationToken = default)
    {
        if (_access is not null) await _access.RequireAsync("Host", Id(hostId), "Edit", cancellationToken);
        await _saves.UpdateBasicDetailsAsync(Id(hostId), name, description, cancellationToken);
    }
    public Task<MoveItHostConfiguration> GetNativeConfigurationAsync(string hostId, CancellationToken cancellationToken = default)
        => _com.GetHostConfigurationAsync(Id(hostId), cancellationToken);
    public async Task<MoveItHostConfiguration> RestoreNativeConfigurationAsync(string hostId, string xml, CancellationToken cancellationToken = default)
    {
        if (_access is not null) await _access.RequireAsync("Host", Id(hostId), "Edit", cancellationToken);
        return await _com.RestoreHostConfigurationAsync(Id(hostId), xml, cancellationToken);
    }
    public async Task DeleteAsync(string hostId, CancellationToken cancellationToken = default)
    {
        if (_access is not null) await _access.RequireAsync("Host", Id(hostId), "Delete", cancellationToken);
        await _com.DeleteHostAsync(Id(hostId), cancellationToken);
    }
    public Task<HostSshAlgorithmCatalog> GetSshAlgorithmsAsync(CancellationToken cancellationToken = default)
        => HostSshAlgorithms.LoadAsync(_rest, cancellationToken, _algorithmDefaults?.Value);
    public async Task<IReadOnlyList<HostSshClientKeyOption>> GetSshClientKeysAsync(CancellationToken cancellationToken = default)
        => HostSshSecurity.MapKeys(await _rest.GetSshKeysAsync(cancellationToken));
    public async Task<string> RetrieveSshHostFingerprintAsync(HostFingerprintRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _rest.GetSshHostFingerprintV0Async(HostSshSecurity.BuildFingerprintQuery(request), cancellationToken);
        var fingerprint = HostDefinitionJson.ReadNode(HostDefinitionJson.GetNode(response, "SSHFingerprint")).Trim();
        if (fingerprint.Length == 0) throw new InvalidOperationException("MOVEit did not return an SSH Host fingerprint.");
        return fingerprint;
    }
    public async Task<IReadOnlyList<HostTlsCertificateOption>> GetTlsCertificatesAsync(CancellationToken cancellationToken = default)
        => HostResourceContracts.Certificates(await _rest.GetHostTlsCertificatesV0Async(cancellationToken));
    public async Task<IReadOnlyList<HostDocumentLibraryOption>> BrowseDocumentLibrariesAsync(HostDocumentLibraryRequest request, CancellationToken cancellationToken = default)
    {
        var id = Id(request.HostId);
        var (type, raw) = HostDefinitionJson.UnwrapHost(await _rest.GetHostAsync(id, cancellationToken));
        if (type != "SharePoint" || HostResourceContracts.Text(raw, "ID") != id) throw new InvalidOperationException("The requested SharePoint Host is unavailable.");
        var draft = (JsonObject)raw.DeepClone();
        var configuration = request.Configuration ?? new JsonObject();
        HostResourceContracts.ValidateAtomicFields(configuration);
        HostCapturedProfiles.Apply(draft, configuration, type);
        if (HostResourceContracts.CertificateFields.Any(configuration.ContainsKey))
            HostResourceContracts.ValidateCertificate(configuration, await GetTlsCertificatesAsync(cancellationToken));
        var payload = HostSharePointBrowseContract.Build(id, draft, replacements: configuration);
        return HostResourceContracts.Libraries(await _rest.BrowseSharePointHostV0Async(payload, cancellationToken));
    }
    public async Task<HostDefinitionSaveResult> SaveDefinitionAsync(string hostId, HostDefinitionSaveRequest request,
        string username, HostCacheSnapshot liveSnapshot, CancellationToken cancellationToken = default)
    {
        if (_access is not null) await _access.RequireAsync("Host", Id(hostId), "Edit", cancellationToken);
        return await _saves.SaveDefinitionAsync(Id(hostId), request, username, liveSnapshot, cancellationToken);
    }

    private static string Id(string hostId)
        => string.IsNullOrWhiteSpace(hostId) ? throw new ArgumentException("Host ID is required.", nameof(hostId)) : hostId.Trim();
}
