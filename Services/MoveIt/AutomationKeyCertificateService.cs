// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.MoveIt;

public interface IAutomationKeyCertificateService
{
    // Returns any existing key/certificate cache immediately for initial navigation and indicates whether it should be reconciled in the background.
    Task<InitialCacheSnapshot<KeyCertificateCacheSnapshot>> GetInitialSnapshotAsync(CancellationToken cancellationToken = default);
    // Purpose: Returns the combined key and certificate inventory with refresh timestamp.
    Task<KeyCertificateCacheSnapshot> GetSnapshotAsync(bool forceRefresh = false, CancellationToken cancellationToken = default);
    // Purpose: Returns one normalized key or certificate resource by row ID.
    Task<AutomationKeyCertificate> GetAsync(string rowId, CancellationToken cancellationToken = default);
    // Purpose: Returns the Task and Host references that use the selected key or certificate.
    Task<KeyCertificateDependencies> GetDependenciesAsync(string rowId, CancellationToken cancellationToken = default);
    // Purpose: Creates SSH key after validating the supplied request.
    Task<AutomationKeyCertificate> CreateSshKeyAsync(string name, string keyType, int keySize, CancellationToken cancellationToken = default, string? resourceGroup = null);
    // Purpose: Creates TLS certificate after validating the supplied request.
    Task<AutomationKeyCertificate> CreateTlsCertificateAsync(string name, string emailAddress, string country, string state, string organization, string organizationalUnit, int expiresAfter, string expireUnit, int keySize, CancellationToken cancellationToken = default, string? resourceGroup = null);
    // Purpose: Creates PGP key after validating the supplied request.
    Task<AutomationKeyCertificate> CreatePgpKeyAsync(string name, string emailAddress, string keyType, int keyLength, string hashAlgorithm, int expiresAfter, string expireUnit, string password, string confirmPassword, CancellationToken cancellationToken = default, string? resourceGroup = null);
    // Purpose: Imports the supplied SSH key, TLS certificate, or PGP key material.
    Task<AutomationKeyCertificate> ImportAsync(string type, string name, string fileName, byte[] fileBytes, string password, CancellationToken cancellationToken = default, string? resourceGroup = null);
    Task<KeyCertificateExportFile> ExportAsync(KeyCertificateExportRequest request, CancellationToken cancellationToken = default);
    // Purpose: Validates delete and returns the information required before execution.
    Task<IReadOnlyList<KeyCertificateDeletePreflightResult>> PreflightDeleteAsync(IEnumerable<string> rowIds, CancellationToken cancellationToken = default);
    // Purpose: Deletes the requested supported key or certificate resource.
    Task<KeyCertificateBulkDeleteResult> DeleteAsync(IEnumerable<string> rowIds, CancellationToken cancellationToken = default);
}

public sealed record KeyCertificateCacheSnapshot(IReadOnlyList<AutomationKeyCertificate> Items, DateTimeOffset RefreshedUtc);
public sealed record KeyCertificateDependencies(IReadOnlyList<KeyCertificateTaskReference> Tasks, IReadOnlyList<KeyCertificateHostReference> Hosts);
public sealed record KeyCertificateDeletePreflightResult(bool CanDelete, string RowId, string Name, string Type, IReadOnlyList<KeyCertificateTaskReference> Tasks, IReadOnlyList<KeyCertificateHostReference> Hosts, string Message);
public sealed record KeyCertificateBulkDeleteItemResult(string RowId, string Name, string Type, bool Success, bool Skipped, string Message);
public sealed record KeyCertificateBulkDeleteResult(IReadOnlyList<KeyCertificateBulkDeleteItemResult> Items)
{
    public int Succeeded => Items.Count(item => item.Success);
    public int Skipped => Items.Count(item => item.Skipped);
    public int Failed => Items.Count(item => !item.Success && !item.Skipped);
}

public sealed class SshKeyNameAlreadyExistsException : InvalidOperationException
{
    // Purpose: Initializes the duplicate-name exception for an SSH client key.
    public SshKeyNameAlreadyExistsException(string name) : base($"An SSH Client Key named '{name}' already exists. Enter a unique Name.") { }
}

public sealed class AutomationKeyCertificateService : IAutomationKeyCertificateService
{
    private const string CachePrefix = "MoveItAutomationUI:KeysAndCertificates:";
    private readonly IMoveItRestClient _rest;
    private readonly IMoveItObjectAccessService? _access;
    private readonly IMoveItComClient _com;
    private readonly IMoveItSessionService _session;
    private readonly IMemoryCache _cache;
    private readonly KeyCertificateBehaviorOptions _options;

    // Purpose: Initializes automation key certificate service with its required dependencies.
    public AutomationKeyCertificateService(IMoveItRestClient rest, IMoveItComClient com, IMoveItSessionService session, IMemoryCache cache, IOptionsSnapshot<KeyCertificateBehaviorOptions> options, IMoveItObjectAccessService? access = null)
    {
        _rest = rest; _com = com; _session = session; _cache = cache; _options = options.Value; _access = access;
    }

    // Returns the initial Keys & Certificates snapshot through the shared cached-inventory navigation contract.
    public async Task<InitialCacheSnapshot<KeyCertificateCacheSnapshot>> GetInitialSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var key = CacheKey();
        return await MoveItServiceCache.ResolveInitialAsync<KeyCertificateCacheEntry, KeyCertificateCacheSnapshot>(
            _cache,
            key,
            _options.CacheRefreshSeconds.Value,
            entry => entry.RefreshedUtc,
            entry => new KeyCertificateCacheSnapshot(entry.Items, entry.RefreshedUtc),
            token => GetSnapshotAsync(false, token),
            cancellationToken);
    }

    // Purpose: Returns the combined key and certificate inventory with refresh timestamp.
    public async Task<KeyCertificateCacheSnapshot> GetSnapshotAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        var key = CacheKey();
        var entry = await MoveItServiceCache.ResolveAsync<KeyCertificateCacheEntry>(
            _cache, key, _options.CacheRefreshSeconds.Value, forceRefresh,
            value => value.RefreshedUtc,
            async token =>
            {
                var capabilities = MoveItLicensePolicy.GetCapabilities(_session.Get());
                var sshTask = _rest.GetSshKeysAsync(token);
                var tlsTask = _rest.GetTlsCertificatesAsync(token);
                Task<IReadOnlyList<JsonObject>>? pgpTask = capabilities.PgpLicensed
                    ? _rest.GetPgpKeysAsync(token)
                    : null;
                if (pgpTask is null) await Task.WhenAll(sshTask, tlsTask);
                else await Task.WhenAll(sshTask, pgpTask, tlsTask);

                IEnumerable<AutomationKeyCertificate> items = (await sshTask).Select(ParseSsh)
                    .Concat((await tlsTask).Select(ParseTls));
                if (pgpTask is not null) items = items.Concat((await pgpTask).Select(ParsePgp));

                var normalized = items
                    .Where(item => item.RowId.Length > 0)
                    .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.Type, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return new KeyCertificateCacheEntry(normalized, DateTimeOffset.UtcNow);
            },
            cancellationToken);
        return new(entry.Items, entry.RefreshedUtc);
    }

    // Purpose: Returns one normalized key or certificate resource by row ID.
    public async Task<AutomationKeyCertificate> GetAsync(string rowId, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeRowId(rowId);
        var inventory = (await GetSnapshotAsync(false, cancellationToken)).Items;
        var item = inventory.FirstOrDefault(value => value.RowId.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (item is not null) return item;
        inventory = (await GetSnapshotAsync(true, cancellationToken)).Items;
        return inventory.FirstOrDefault(value => value.RowId.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            ?? throw new MoveItApiException("This key or certificate no longer exists or is no longer visible to the current MOVEit user.");
    }

    public async Task<KeyCertificateExportFile> ExportAsync(KeyCertificateExportRequest request, CancellationToken cancellationToken = default)
    {
        var rowId = NormalizeRowId(request.RowId);
        if (_access is not null) await _access.RequireAsync("KeyCertificate", rowId, "Edit", cancellationToken);
        var item = (await GetSnapshotAsync(true, cancellationToken)).Items.FirstOrDefault(value => value.RowId.Equals(rowId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("This key or certificate is no longer available. Refresh the grid and try again.");
        if (!item.CanEditDelete && _session.Get()?.Info?.IsAdmin != true)
            throw new MoveItObjectAccessException("Your MOVEit account does not permit exporting this key or certificate.");
        if (item.Kind == KeyCertificateKind.Pgp && !MoveItLicensePolicy.GetCapabilities(_session.Get()).PgpLicensed)
            throw new InvalidOperationException("PGP is not licensed on this MOVEit server.");
        var body = KeyCertificateExport.Compose(item, request);
        var bytes = item.Kind == KeyCertificateKind.Tls
            ? await _rest.ExportTlsCertificateV0Async(body, cancellationToken)
            : await _rest.ExportPgpKeyV0Async(body, cancellationToken);
        return KeyCertificateExport.Download(item, request.IncludePrivateKey, bytes);
    }

    // Purpose: Returns the Task and Host references that use the selected key or certificate.
    public async Task<KeyCertificateDependencies> GetDependenciesAsync(string rowId, CancellationToken cancellationToken = default)
    {
        var item = await GetAsync(rowId, cancellationToken);
        var taskDefinitionsTask = _rest.GetTaskKeyCertificateReferenceDefinitionsAsync(cancellationToken);
        if (item.Kind == KeyCertificateKind.Pgp)
        {
            var taskDefinitions = await taskDefinitionsTask;
            return BuildDependencies(item, taskDefinitions, Array.Empty<JsonObject>());
        }

        var hostDefinitionsTask = _rest.GetHostKeyCertificateReferenceDefinitionsAsync(cancellationToken);
        await Task.WhenAll(taskDefinitionsTask, hostDefinitionsTask);
        return BuildDependencies(item, await taskDefinitionsTask, await hostDefinitionsTask);
    }

    // Purpose: Creates SSH key after validating the supplied request.
    public async Task<AutomationKeyCertificate> CreateSshKeyAsync(string name, string keyType, int keySize, CancellationToken cancellationToken = default, string? resourceGroup = null)
    {
        if (_access is not null) resourceGroup = await _access.RequireCreationAsync("SSH", resourceGroup, cancellationToken);
        var normalizedName = (name ?? string.Empty).Trim();
        if (normalizedName.Length == 0) throw new InvalidOperationException("SSH Key Name is required.");
        if (normalizedName.Length > 254) throw new InvalidOperationException("SSH Key Name cannot exceed 254 characters.");
        var normalizedType = NormalizeSshKeyType(keyType);
        ValidateSshKeySize(normalizedType, keySize);
        if (await _rest.SshKeyNameExistsAsync(normalizedName, cancellationToken)) throw new SshKeyNameAlreadyExistsException(normalizedName);

        await _rest.CreateSshKeyV0Async(normalizedName, normalizedType, keySize, cancellationToken, resourceGroup);
        Invalidate();

        // Web Admin's internal v0 create response is not part of the public REST contract. Verify through v1.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var ssh = await _rest.GetSshKeysAsync(cancellationToken);
            var created = ssh.Select(ParseSsh).FirstOrDefault(item => item.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase));
            if (created is not null) return created;
            if (attempt < 9) await Task.Delay(250, cancellationToken);
        }
        throw new MoveItApiException($"MOVEit accepted creation of SSH Client Key '{normalizedName}', but the key was not returned by the v1 SSH inventory for verification.");
    }

    // Purpose: Creates TLS certificate after validating the supplied request.
    public async Task<AutomationKeyCertificate> CreateTlsCertificateAsync(
        string name,
        string emailAddress,
        string country,
        string state,
        string organization,
        string organizationalUnit,
        int expiresAfter,
        string expireUnit,
        int keySize,
        CancellationToken cancellationToken = default, string? resourceGroup = null)
    {
        if (_access is not null) resourceGroup = await _access.RequireCreationAsync("TLS", resourceGroup, cancellationToken);
        var normalizedName = NormalizeTlsSubjectValue(name, "TLS Certificate Name", required: true);
        var normalizedEmail = NormalizeTlsSubjectValue(emailAddress, "Email Address", required: false);
        var normalizedCountry = NormalizeTlsSubjectValue(country, "Country", required: false);
        var normalizedState = NormalizeTlsSubjectValue(state, "State", required: false);
        var normalizedOrganization = NormalizeTlsSubjectValue(organization, "Organization", required: false);
        var normalizedOrganizationalUnit = NormalizeTlsSubjectValue(organizationalUnit, "Organizational Unit", required: false);

        if (expiresAfter < 1 || expiresAfter > 12)
            throw new InvalidOperationException("Expires After must be a value from 1 through 12.");

        var normalizedExpireUnit = NormalizeTlsExpireUnit(expireUnit);
        ValidateTlsKeySize(keySize);

        var today = DateTime.Today;
        var expiration = normalizedExpireUnit == "Years" ? today.AddYears(expiresAfter) : today.AddMonths(expiresAfter);
        var expirationDate = expiration.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var subject = BuildTlsSubject(normalizedName, normalizedEmail, normalizedCountry, normalizedState, normalizedOrganization, normalizedOrganizationalUnit);

        // Capture the live TLS identities before creation so verification cannot accidentally return an
        // older certificate with the same subject/name. SHA-1 thumbprints make newly-created certs unique.
        var existing = (await _rest.GetTlsCertificatesAsync(cancellationToken)).Select(ParseTls).Where(item => item.RowId.Length > 0).ToList();
        var existingIds = existing.Select(item => item.RowId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        await _rest.CreateTlsCertificateV0Async(subject, expirationDate, keySize, cancellationToken, resourceGroup);
        Invalidate();

        // The internal v0 create response is not a public contract. Verify the newly-created certificate
        // through the v1 inventory, just as SSH creation is verified through its v1 read endpoint.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var tls = (await _rest.GetTlsCertificatesAsync(cancellationToken))
                .Select(ParseTls)
                .Where(item => item.RowId.Length > 0 && !existingIds.Contains(item.RowId))
                .ToList();

            var created = tls.FirstOrDefault(item => TlsCertificateMatches(item, normalizedName, subject));
            if (created is not null) return created;
            if (tls.Count == 1) return tls[0];
            if (attempt < 9) await Task.Delay(250, cancellationToken);
        }

        throw new MoveItApiException($"MOVEit accepted creation of TLS Certificate '{normalizedName}', but the certificate was not returned by the v1 TLS inventory for verification.");
    }

    // Purpose: Creates PGP key after validating the supplied request.
    public async Task<AutomationKeyCertificate> CreatePgpKeyAsync(
        string name,
        string emailAddress,
        string keyType,
        int keyLength,
        string hashAlgorithm,
        int expiresAfter,
        string expireUnit,
        string password,
        string confirmPassword,
        CancellationToken cancellationToken = default, string? resourceGroup = null)
    {
        if (_access is not null) resourceGroup = await _access.RequireCreationAsync("PGP", resourceGroup, cancellationToken);
        var normalizedName = NormalizePgpText(name, "PGP Key Name", required: true);
        var normalizedEmail = NormalizePgpText(emailAddress, "Email Address", required: false);
        var normalizedKeyType = NormalizePgpKeyType(keyType);
        ValidatePgpKeyLength(keyLength);
        var normalizedHashAlgorithm = NormalizePgpHashAlgorithm(hashAlgorithm);

        if (expiresAfter < 1 || expiresAfter > 12)
            throw new InvalidOperationException("Expires After must be a value from 1 through 12.");

        var normalizedExpireUnit = NormalizePgpExpireUnit(expireUnit);
        var normalizedPassword = password ?? string.Empty;
        var normalizedConfirmPassword = confirmPassword ?? string.Empty;
        if (!string.Equals(normalizedPassword, normalizedConfirmPassword, StringComparison.Ordinal))
            throw new InvalidOperationException("Password and Confirm Password must match.");

        var uid = normalizedEmail.Length > 0 ? $"{normalizedName} <{normalizedEmail}>" : normalizedName;

        // Capture the live PGP identities first so verification cannot accidentally return an older key
        // with the same friendly name or UID.
        var existing = (await _rest.GetPgpKeysAsync(cancellationToken)).Select(ParsePgp).Where(item => item.RowId.Length > 0).ToList();
        var existingIds = existing.Select(item => item.RowId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        await _rest.CreatePgpKeyV0Async(
            uid,
            normalizedKeyType,
            keyLength,
            normalizedHashAlgorithm,
            expiresAfter,
            normalizedExpireUnit,
            normalizedPassword,
            cancellationToken, resourceGroup);

        Invalidate();

        // The internal v0 response is not a public REST contract. Verify through the v1 inventory and
        // return the persistent MOVEit identity used by the grid/native editor.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var pgp = (await _rest.GetPgpKeysAsync(cancellationToken))
                .Select(ParsePgp)
                .Where(item => item.RowId.Length > 0 && !existingIds.Contains(item.RowId))
                .ToList();

            var created = pgp.FirstOrDefault(item => PgpKeyMatches(item, normalizedName, uid));
            if (created is not null) return created;
            if (pgp.Count == 1) return pgp[0];
            if (attempt < 9) await Task.Delay(250, cancellationToken);
        }

        throw new MoveItApiException($"MOVEit accepted creation of PGP Key '{normalizedName}', but the key was not returned by the v1 PGP inventory for verification.");
    }

    // Purpose: Imports the supplied SSH key, TLS certificate, or PGP key material.
    public async Task<AutomationKeyCertificate> ImportAsync(
        string type,
        string name,
        string fileName,
        byte[] fileBytes,
        string password,
        CancellationToken cancellationToken = default, string? resourceGroup = null)
    {
        if (_access is not null) resourceGroup = await _access.RequireCreationAsync((type ?? "").Trim().ToUpperInvariant(), resourceGroup, cancellationToken);
        var normalizedType = (type ?? string.Empty).Trim().ToUpperInvariant();
        var safeFileName = Path.GetFileName(fileName ?? string.Empty);
        if (fileBytes is null || fileBytes.Length == 0) throw new InvalidOperationException("Select a non-empty key or certificate file to import.");
        if (safeFileName.Length == 0) throw new InvalidOperationException("The uploaded file name is invalid.");

        return normalizedType switch
        {
            "SSH" => await ImportSshAsync(name, safeFileName, fileBytes, password, cancellationToken, resourceGroup),
            "TLS" => await ImportTlsAsync(safeFileName, fileBytes, password, cancellationToken, resourceGroup),
            "PGP" => await ImportPgpAsync(safeFileName, fileBytes, password, cancellationToken, resourceGroup),
            _ => throw new InvalidOperationException("Import Type must be SSH Key, TLS Certificate, or PGP Key.")
        };
    }

    // Purpose: Imports SSH.
    private async Task<AutomationKeyCertificate> ImportSshAsync(
        string name,
        string fileName,
        byte[] fileBytes,
        string password,
        CancellationToken cancellationToken, string? resourceGroup = null)
    {
        var normalizedName = (name ?? string.Empty).Trim();
        if (normalizedName.Length == 0) throw new InvalidOperationException("Name is required when importing an SSH Key.");
        if (normalizedName.Length > 254) throw new InvalidOperationException("SSH Key Name cannot exceed 254 characters.");
        if (!Path.GetExtension(fileName).Equals(".key", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SSH Key imports require a .key file.");
        if (await _rest.SshKeyNameExistsAsync(normalizedName, cancellationToken)) throw new SshKeyNameAlreadyExistsException(normalizedName);

        string privateKey;
        try
        {
            privateKey = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(fileBytes);
            if (privateKey.Length > 0 && privateKey[0] == '\uFEFF') privateKey = privateKey[1..];
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidOperationException("The SSH .key file must contain UTF-8 text key material.", ex);
        }
        if (string.IsNullOrWhiteSpace(privateKey)) throw new InvalidOperationException("The SSH .key file is empty.");

        await _rest.ImportSshKeyV0Async(normalizedName, password ?? string.Empty, privateKey, cancellationToken, resourceGroup);
        Invalidate();

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var created = (await _rest.GetSshKeysAsync(cancellationToken)).Select(ParseSsh)
                .FirstOrDefault(item => item.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase));
            if (created is not null) return created;
            if (attempt < 9) await Task.Delay(250, cancellationToken);
        }

        throw new MoveItApiException($"MOVEit accepted import of SSH Client Key '{normalizedName}', but the key was not returned by the v1 SSH inventory for verification.");
    }

    // Purpose: Imports TLS.
    private async Task<AutomationKeyCertificate> ImportTlsAsync(
        string fileName,
        byte[] fileBytes,
        string password,
        CancellationToken cancellationToken, string? resourceGroup = null)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not ".pfx" and not ".p12" and not ".cer")
            throw new InvalidOperationException("TLS Certificate imports require a .pfx, .p12, or .cer file.");

        var existingIds = (await _rest.GetTlsCertificatesAsync(cancellationToken)).Select(ParseTls)
            .Where(item => item.RowId.Length > 0)
            .Select(item => item.RowId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var isPublicOnly = extension == ".cer";
        await _rest.ImportTlsCertificateV0Async(fileName, fileBytes, isPublicOnly, password ?? string.Empty, cancellationToken, resourceGroup);
        Invalidate();

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var added = (await _rest.GetTlsCertificatesAsync(cancellationToken)).Select(ParseTls)
                .Where(item => item.RowId.Length > 0 && !existingIds.Contains(item.RowId))
                .ToList();
            if (added.Count > 0) return added[0];
            if (attempt < 9) await Task.Delay(250, cancellationToken);
        }

        throw new MoveItApiException($"MOVEit accepted import of TLS Certificate '{fileName}', but no new certificate was returned by the v1 TLS inventory for verification.");
    }

    // Purpose: Imports PGP.
    private async Task<AutomationKeyCertificate> ImportPgpAsync(
        string fileName,
        byte[] fileBytes,
        string password,
        CancellationToken cancellationToken, string? resourceGroup = null)
    {
        if (!Path.GetExtension(fileName).Equals(".asc", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("PGP Key imports require an .asc file.");

        var existingIds = (await _rest.GetPgpKeysAsync(cancellationToken)).Select(ParsePgp)
            .Where(item => item.RowId.Length > 0)
            .Select(item => item.RowId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await _rest.ImportPgpKeyV0Async(fileBytes, password ?? string.Empty, cancellationToken, resourceGroup);
        Invalidate();

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var added = (await _rest.GetPgpKeysAsync(cancellationToken)).Select(ParsePgp)
                .Where(item => item.RowId.Length > 0 && !existingIds.Contains(item.RowId))
                .ToList();
            if (added.Count > 0) return added[0];
            if (attempt < 9) await Task.Delay(250, cancellationToken);
        }

        throw new MoveItApiException($"MOVEit accepted import of PGP Key '{fileName}', but no new key was returned by the v1 PGP inventory for verification.");
    }

    // Purpose: Validates delete and returns the information required before execution.
    public async Task<IReadOnlyList<KeyCertificateDeletePreflightResult>> PreflightDeleteAsync(IEnumerable<string> rowIds, CancellationToken cancellationToken = default)
    {
        var ids = NormalizeRowIds(rowIds);
        var inventoryTask = GetSnapshotAsync(true, cancellationToken);
        var taskDefinitionsTask = _rest.GetTaskKeyCertificateReferenceDefinitionsAsync(cancellationToken);
        var hostDefinitionsTask = _rest.GetHostKeyCertificateReferenceDefinitionsAsync(cancellationToken);
        await Task.WhenAll(inventoryTask, taskDefinitionsTask, hostDefinitionsTask);
        var inventory = (await inventoryTask).Items;
        var taskDefinitions = await taskDefinitionsTask;
        var hostDefinitions = await hostDefinitionsTask;
        var results = new List<KeyCertificateDeletePreflightResult>();

        foreach (var rowId in ids)
        {
            var item = inventory.FirstOrDefault(value => value.RowId.Equals(rowId, StringComparison.OrdinalIgnoreCase));
            if (item is null)
            {
                results.Add(new(false, rowId, rowId, "Key/Certificate", Array.Empty<KeyCertificateTaskReference>(), Array.Empty<KeyCertificateHostReference>(), "The key or certificate no longer exists or is no longer visible."));
                continue;
            }
            if (!item.CanEditDelete)
            {
                results.Add(new(false, item.RowId, item.Name, item.Type, Array.Empty<KeyCertificateTaskReference>(), Array.Empty<KeyCertificateHostReference>(), $"MOVEit reports that the current user cannot delete '{item.Name}'."));
                continue;
            }
            var deps = BuildDependencies(item, taskDefinitions, hostDefinitions);
            if (deps.Tasks.Count > 0 || deps.Hosts.Count > 0)
            {
                var blockers = new List<string>();
                if (deps.Tasks.Count > 0) blockers.Add($"{deps.Tasks.Count} Task{(deps.Tasks.Count == 1 ? string.Empty : "s")}");
                if (deps.Hosts.Count > 0) blockers.Add($"{deps.Hosts.Count} Host{(deps.Hosts.Count == 1 ? string.Empty : "s")}");
                results.Add(new(false, item.RowId, item.Name, item.Type, deps.Tasks, deps.Hosts, $"Cannot delete '{item.Name}' because it is referenced by {string.Join(" and ", blockers)}. Remove those references first."));
                continue;
            }
            results.Add(new(true, item.RowId, item.Name, item.Type, deps.Tasks, deps.Hosts, string.Empty));
        }
        return results;
    }

    // Purpose: Deletes the requested supported key or certificate resource.
    public async Task<KeyCertificateBulkDeleteResult> DeleteAsync(IEnumerable<string> rowIds, CancellationToken cancellationToken = default)
    {
        var preflight = await PreflightDeleteAsync(rowIds, cancellationToken);
        var output = new List<KeyCertificateBulkDeleteItemResult>();
        var anyDeleted = false;
        foreach (var item in preflight)
        {
            if (!item.CanDelete)
            {
                output.Add(new(item.RowId, item.Name, item.Type, false, true, item.Message));
                continue;
            }
            try
            {
                var target = await GetAsync(item.RowId, cancellationToken);
                switch (target.Kind)
                {
                    case KeyCertificateKind.Ssh:
                        await _com.DeleteSshClientKeyAsync(target.Id, cancellationToken);
                        break;
                    case KeyCertificateKind.Pgp:
                        await _rest.DeletePgpKeyV0Async(target.Id, cancellationToken);
                        break;
                    case KeyCertificateKind.Tls:
                        // The public v1 GET identifies TLS certs by SHA1 thumbprint, so use that identity first.
                        var tlsIdentity = target.Sha1Thumbprint.Length > 0 ? target.Sha1Thumbprint : target.Id;
                        await _rest.DeleteTlsCertificateV0Async(tlsIdentity, cancellationToken);
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported key/certificate type '{target.Type}'.");
                }
                anyDeleted = true;
                output.Add(new(item.RowId, item.Name, item.Type, true, false, string.Empty));
            }
            catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
            {
                output.Add(new(item.RowId, item.Name, item.Type, false, false, ex.Message));
            }
        }
        if (anyDeleted) Invalidate();
        return new(output);
    }

    // Purpose: Builds dependencies.
    private static KeyCertificateDependencies BuildDependencies(AutomationKeyCertificate item, IReadOnlyList<JsonObject> tasks, IReadOnlyList<JsonObject> hosts)
    {
        var taskRefs = tasks.Where(task => ContainsReference(task, item))
            .Select(task => new KeyCertificateTaskReference(Read(task, "ID"), Read(task, "Name")))
            .Where(task => task.Id.Length > 0)
            .GroupBy(task => task.Id, StringComparer.OrdinalIgnoreCase).Select(group => group.First())
            .OrderBy(task => task.Name, StringComparer.OrdinalIgnoreCase).ToList();

        var hostRefs = item.Kind == KeyCertificateKind.Pgp
            ? new List<KeyCertificateHostReference>()
            : hosts.Select(UnwrapHost)
                .Where(host => ContainsReference(host.Payload, item))
                .Select(host => new KeyCertificateHostReference(Read(host.Payload, "ID"), Read(host.Payload, "Name"), host.Type))
                .Where(host => host.Id.Length > 0)
                .GroupBy(host => host.Id, StringComparer.OrdinalIgnoreCase).Select(group => group.First())
                .OrderBy(host => host.Name, StringComparer.OrdinalIgnoreCase).ToList();

        return new(taskRefs, hostRefs);
    }

    // Purpose: Returns whether a Task or Host definition contains the requested key/certificate reference.
    private static bool ContainsReference(JsonNode? node, AutomationKeyCertificate item, bool referenceContext = false)
    {
        if (node is JsonObject obj)
        {
            foreach (var pair in obj)
            {
                var key = NormalizePropertyName(pair.Key);
                var nextContext = referenceContext || LooksLikeKeyCertificateProperty(key);
                if (nextContext && ScalarMatches(pair.Value, item)) return true;
                if (ContainsReference(pair.Value, item, nextContext)) return true;
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var child in array)
                if (ContainsReference(child, item, referenceContext)) return true;
        }
        else if (referenceContext && ScalarMatches(node, item)) return true;
        return false;
    }

    // Purpose: Returns whether a scalar reference value matches the requested resource identity.
    private static bool ScalarMatches(JsonNode? node, AutomationKeyCertificate item)
    {
        var value = NodeString(node).Trim();
        if (value.Length == 0) return false;
        foreach (var candidate in ReferenceCandidates(item))
            if (candidate.Length > 0 && value.Equals(candidate, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // Purpose: Returns candidate identifiers used when matching key/certificate references.
    private static IEnumerable<string> ReferenceCandidates(AutomationKeyCertificate item)
    {
        yield return item.Id;
        yield return item.Sha1Thumbprint;
        yield return item.Fingerprint;
        yield return item.Name;
        yield return item.Uid;
        yield return item.IssuedTo;
    }

    // Purpose: Returns whether a property name appears to hold a key/certificate reference.
    private static bool LooksLikeKeyCertificateProperty(string key)
        => key.Contains("key", StringComparison.OrdinalIgnoreCase)
           || key.Contains("cert", StringComparison.OrdinalIgnoreCase)
           || key.Contains("pgp", StringComparison.OrdinalIgnoreCase)
           || key.Contains("sha1", StringComparison.OrdinalIgnoreCase)
           || key.Contains("thumbprint", StringComparison.OrdinalIgnoreCase)
           || key.Contains("fingerprint", StringComparison.OrdinalIgnoreCase);

    // Purpose: Normalizes property name.
    private static string NormalizePropertyName(string value) => (value ?? string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).Trim();

    // Purpose: Unwraps the native Host payload before dependency inspection.
    private static (string Type, JsonObject Payload) UnwrapHost(JsonObject wrapper)
    {
        if (Read(wrapper, "ID").Length > 0) return (Read(wrapper, "Type"), wrapper);
        foreach (var pair in wrapper)
            if (pair.Value is JsonObject payload) return (pair.Key, payload);
        return (string.Empty, wrapper);
    }

    // Purpose: Parses SSH.
    private static AutomationKeyCertificate ParseSsh(JsonObject raw)
    {
        var id = Read(raw, "ID");
        return new AutomationKeyCertificate
        {
            RowId = BuildRowId(KeyCertificateKind.Ssh, id), Kind = KeyCertificateKind.Ssh, Id = id,
            Name = First(Read(raw, "Name"), id), Type = "SSH Client Key", Fingerprint = Read(raw, "Fingerprint"),
            PublicKeyOpenSsh = Read(raw, "PublicKeyOpenSSH"), PublicKeySsh = Read(raw, "PublicKeySSH"),
            Group = ReadGroups(GetNode(raw, "Group")), CanEditDelete = ReadPermission(raw)
        };
    }

    // Purpose: Parses PGP.
    private static AutomationKeyCertificate ParsePgp(JsonObject raw)
    {
        var id = Read(raw, "ID");
        var uid = Read(raw, "uid");
        return new AutomationKeyCertificate
        {
            RowId = BuildRowId(KeyCertificateKind.Pgp, id), Kind = KeyCertificateKind.Pgp, Id = id,
            Name = First(Read(raw, "Name"), uid, id), Type = "PGP Key", ExpirationDate = Read(raw, "Expires"),
            NativeIsExpired = Read(raw, "Expired").ToLowerInvariant() switch { "1" or "true" => true, "0" or "false" => false, _ => null },
            Fingerprint = Read(raw, "Fingerprint"), KeyType = Read(raw, "KeyType"), KeySize = Read(raw, "KeyLength"),
            Status = Read(raw, "Status"), CreatedDate = Read(raw, "Created"), PublicPrivate = Read(raw, "PubPriv"),
            SymmetricAlgorithm = Read(raw, "SymAlg"), Uid = uid, Group = ReadGroups(GetNode(raw, "Group")), CanEditDelete = ReadPermission(raw)
        };
    }

    // Purpose: Parses TLS.
    private static AutomationKeyCertificate ParseTls(JsonObject raw)
    {
        var id = Read(raw, "ID");
        var thumbprint = Read(raw, "Sha1Thumbprint");
        var serialNumber = Read(raw, "SerialNum");
        var identity = First(id, thumbprint, serialNumber);
        return new AutomationKeyCertificate
        {
            RowId = BuildRowId(KeyCertificateKind.Tls, identity), Kind = KeyCertificateKind.Tls, Id = identity,
            Name = First(Read(raw, "Name"), Read(raw, "IssuedTo"), identity), Type = "TLS Certificate", ExpirationDate = Read(raw, "ExpDate"),
            IssuedTo = Read(raw, "IssuedTo"), Issuer = Read(raw, "Issuer"), SerialNumber = serialNumber,
            Sha1Thumbprint = thumbprint, Store = Read(raw, "Store"), ValidFromDate = Read(raw, "ValidFromDate"),
            Group = ReadGroups(GetNode(raw, "Group")), CanEditDelete = ReadPermission(raw)
        };
    }

    // Purpose: Reads permission.
    private static bool ReadPermission(JsonObject raw)
    {
        var permission = GetNode(raw, "xAddEditDelete");
        return permission is null || ReadBool(permission);
    }

    // Purpose: Reads bool.
    private static bool ReadBool(JsonNode? node) { var value = NodeString(node).Trim(); return value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase); }

    // Key/certificate payload casing differs between several 2025.x REST resources/builds. Keep this
    // parser case-insensitive even though the fields= query parameter itself is case-sensitive.
    private static string Read(JsonObject raw, string name) => NodeString(GetNode(raw, name)).Trim();

    // Purpose: Returns node.
    private static JsonNode? GetNode(JsonObject raw, string name)
    {
        if (raw.TryGetPropertyValue(name, out var exact)) return exact;
        foreach (var pair in raw)
            if (pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)) return pair.Value;
        return null;
    }
    // Purpose: Converts a JSON node to its normalized string representation.
    private static string NodeString(JsonNode? node)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text)) return text ?? string.Empty;
            if (value.TryGetValue<int>(out var integer)) return integer.ToString();
            if (value.TryGetValue<long>(out var longValue)) return longValue.ToString();
            if (value.TryGetValue<bool>(out var boolean)) return boolean ? "true" : "false";
        }
        return node?.ToJsonString() ?? string.Empty;
    }
    // Purpose: Reads groups.
    private static string ReadGroups(JsonNode? node) => node is JsonArray array ? string.Join(", ", array.Select(NodeString).Where(value => value.Length > 0)) : NodeString(node);
    // Purpose: Returns the first nonblank value from the supplied candidates.
    private static string First(params string[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    // Purpose: Builds row ID.
    private static string BuildRowId(KeyCertificateKind kind, string id) => id.Trim().Length == 0 ? string.Empty : $"{kind.ToString().ToLowerInvariant()}|{id.Trim()}";
    // Purpose: Normalizes row ID.
    private static string NormalizeRowId(string? rowId) { var value = (rowId ?? string.Empty).Trim(); if (value.Length == 0 || !value.Contains('|')) throw new ArgumentException("Key/Certificate identity is required."); return value; }
    // Purpose: Normalizes row ids.
    private static IReadOnlyList<string> NormalizeRowIds(IEnumerable<string>? rowIds) { var result = (rowIds ?? Array.Empty<string>()).Select(value => (value ?? string.Empty).Trim()).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); if (result.Count == 0) throw new ArgumentException("Select at least one key or certificate."); return result; }
    // Purpose: Normalizes TLS subject value.
    private static string NormalizeTlsSubjectValue(string? value, string fieldName, bool required)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (required && normalized.Length == 0) throw new InvalidOperationException($"{fieldName} is required.");
        if (normalized.Length > 254) throw new InvalidOperationException($"{fieldName} cannot exceed 254 characters.");
        return normalized;
    }

    // Purpose: Normalizes TLS expire unit.
    private static string NormalizeTlsExpireUnit(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Equals("Years", StringComparison.OrdinalIgnoreCase)) return "Years";
        if (normalized.Equals("Months", StringComparison.OrdinalIgnoreCase)) return "Months";
        throw new InvalidOperationException("Expiration unit must be Years or Months.");
    }

    // Purpose: Validates TLS key size.
    private static void ValidateTlsKeySize(int size)
    {
        if (size is not (1024 or 2048 or 3072 or 4096))
            throw new InvalidOperationException("TLS Certificate Key Size must be 1024, 2048, 3072, or 4096.");
    }

    // Purpose: Builds TLS subject.
    private static string BuildTlsSubject(string name, string emailAddress, string country, string state, string organization, string organizationalUnit)
    {
        var components = new List<string> { $"CN={EscapeDistinguishedNameValue(name)}" };
        AddTlsSubjectComponent(components, "E", emailAddress);
        AddTlsSubjectComponent(components, "C", country);
        AddTlsSubjectComponent(components, "S", state);
        AddTlsSubjectComponent(components, "O", organization);
        AddTlsSubjectComponent(components, "OU", organizationalUnit);
        return string.Join(",", components);
    }

    // Purpose: Adds TLS subject component.
    private static void AddTlsSubjectComponent(ICollection<string> components, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) components.Add($"{key}={EscapeDistinguishedNameValue(value)}");
    }

    // Purpose: Escapes distinguished name value.
    private static string EscapeDistinguishedNameValue(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) return string.Empty;

        var escaped = new System.Text.StringBuilder(normalized.Length + 8);
        for (var i = 0; i < normalized.Length; i++)
        {
            var character = normalized[i];
            if (character is ',' or '+' or '"' or '\\' or '<' or '>' or ';' or '=') escaped.Append('\\');
            if (i == 0 && character == '#') escaped.Append('\\');
            escaped.Append(character);
        }
        return escaped.ToString();
    }

    // Purpose: Returns whether a TLS certificate matches the requested resource identity.
    private static bool TlsCertificateMatches(AutomationKeyCertificate item, string name, string subject)
    {
        if (item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
        if (item.IssuedTo.Equals(subject, StringComparison.OrdinalIgnoreCase)) return true;
        return item.IssuedTo.Contains($"CN={name}", StringComparison.OrdinalIgnoreCase);
    }

    // Purpose: Normalizes PGP text.
    private static string NormalizePgpText(string? value, string fieldName, bool required)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (required && normalized.Length == 0) throw new InvalidOperationException($"{fieldName} is required.");
        if (normalized.Length > 254) throw new InvalidOperationException($"{fieldName} cannot exceed 254 characters.");
        return normalized;
    }

    // Purpose: Normalizes PGP key type.
    private static string NormalizePgpKeyType(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Equals("RSA", StringComparison.OrdinalIgnoreCase)) return "RSA";
        if (normalized.Equals("DSS", StringComparison.OrdinalIgnoreCase)) return "DSS";
        throw new InvalidOperationException("PGP Format must be RSA or DSS.");
    }

    // Purpose: Validates PGP key length.
    private static void ValidatePgpKeyLength(int keyLength)
    {
        if (keyLength is not (1024 or 2048 or 4096))
            throw new InvalidOperationException("PGP Key Length must be 1024, 2048, or 4096.");
    }

    // Purpose: Normalizes PGP hash algorithm.
    private static string NormalizePgpHashAlgorithm(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Equals("SHA256", StringComparison.OrdinalIgnoreCase)) return "sha256";
        if (normalized.Equals("SHA512", StringComparison.OrdinalIgnoreCase)) return "sha512";
        throw new InvalidOperationException("PGP Signing Algorithm must be SHA256 or SHA512.");
    }

    // Purpose: Normalizes PGP expire unit.
    private static string NormalizePgpExpireUnit(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Equals("Years", StringComparison.OrdinalIgnoreCase)) return "y";
        if (normalized.Equals("Months", StringComparison.OrdinalIgnoreCase)) return "m";
        throw new InvalidOperationException("Expiration unit must be Years or Months.");
    }

    // Purpose: Returns whether a PGP key matches the requested resource identity.
    private static bool PgpKeyMatches(AutomationKeyCertificate item, string name, string uid)
    {
        if (item.Uid.Equals(uid, StringComparison.OrdinalIgnoreCase)) return true;
        if (item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return true;
        return item.Name.Equals(uid, StringComparison.OrdinalIgnoreCase);
    }

    // Purpose: Normalizes SSH key type.
    private static string NormalizeSshKeyType(string? value)
    {
        var type = (value ?? string.Empty).Trim();
        if (type.Equals("RSA", StringComparison.OrdinalIgnoreCase)) return "RSA";
        if (type.Equals("DSA", StringComparison.OrdinalIgnoreCase)) return "DSA";
        if (type.Equals("ECDSA", StringComparison.OrdinalIgnoreCase)) return "ECDSA";
        if (type.Equals("Ed25519", StringComparison.OrdinalIgnoreCase)) return "Ed25519";
        throw new InvalidOperationException("Key Type must be RSA, DSA, ECDSA, or Ed25519.");
    }
    // Purpose: Validates SSH key size.
    private static void ValidateSshKeySize(string type, int size)
    {
        var valid = type switch
        {
            "RSA" or "DSA" => new[] { 1024, 2048, 3072, 4096 },
            "ECDSA" => new[] { 256, 384, 521 },
            "Ed25519" => new[] { 255 },
            _ => Array.Empty<int>()
        };
        if (!valid.Contains(size)) throw new InvalidOperationException($"Key Size {size} is not valid for {type}.");
    }
    // Purpose: Returns whether the key/certificate cache entry is still fresh.
    // Purpose: Builds the key/certificate cache key for the current MOVEit session.
    private string CacheKey() => MoveItServiceCache.UserScopedKey(CachePrefix, _session);
    // Purpose: Invalidates the key/certificate cache for the current MOVEit session.
    private void Invalidate() => _cache.Remove(CacheKey());
    private sealed record KeyCertificateCacheEntry(IReadOnlyList<AutomationKeyCertificate> Items, DateTimeOffset RefreshedUtc);
}

