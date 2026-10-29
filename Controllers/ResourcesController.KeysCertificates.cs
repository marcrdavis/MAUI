// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Controllers;

public sealed partial class ResourcesController
{
    // Purpose: Renders the Keys & Certificates workspace shell immediately; inventory loads behind the shared Loading shell.
    public IActionResult KeysAndCerts()
    {
        try
        {
            var session = RequireSession();
            var capabilities = MoveItLicensePolicy.GetCapabilities(session);
            ViewData["PgpLicensed"] = capabilities.PgpLicensed;
            return View("KeysAndCerts", new KeysAndCertificatesPageViewModel
            {
                PgpLicensed = capabilities.PgpLicensed,
                UiKeyCertificateRefreshIntervalSeconds = Math.Max(0, _keyCertificateBehaviorOptions.UiRefreshIntervalSeconds.Value),
                LastRefreshUtc = null,
                Grid = BuildEmptyKeyCertificateGrid()
            });
        }
        catch (MoveItAuthenticationException ex)
        {
            TempData["LoginError"] = ex.Message;
            return RedirectToAction("Login", "Account");
        }
    }

    // Purpose: Performs the initial asynchronous key/certificate inventory load without forcing a new MOVEit read.
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> InitialKeysAndCertificatesGrid(bool preferCached = false, CancellationToken cancellationToken = default)
    {
        try
        {
            var initial = preferCached
                ? await _keyCertificateService.GetInitialSnapshotAsync(cancellationToken)
                : new InitialCacheSnapshot<KeyCertificateCacheSnapshot>(await _keyCertificateService.GetSnapshotAsync(false, cancellationToken), RefreshRecommended: false);
            return await BuildKeysAndCertificatesGridJsonAsync(initial.Snapshot, cancellationToken, initial.RefreshRecommended);
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Refreshes Keys & Certificates grid data through the shared cache contract; forceRefresh bypasses cache age.
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> RefreshKeysAndCertificatesGrid(CancellationToken cancellationToken = default, bool forceRefresh = false)
    {
        try
        {
            var snapshot = await _keyCertificateService.GetSnapshotAsync(forceRefresh, cancellationToken);
            return await BuildKeysAndCertificatesGridJsonAsync(snapshot, cancellationToken);
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Builds the shared Keys & Certificates grid payload from one already-resolved inventory snapshot.
    private async Task<JsonResult> BuildKeysAndCertificatesGridJsonAsync(KeyCertificateCacheSnapshot snapshot, CancellationToken cancellationToken, bool refreshRecommended = false)
    {
        var session = RequireSession();
        var environmentKey = session.GetEnvironmentKey();
        var capabilities = MoveItLicensePolicy.GetCapabilities(session);
        var items = capabilities.PgpLicensed
            ? snapshot.Items
            : snapshot.Items.Where(item => item.Kind != KeyCertificateKind.Pgp).ToList();
        var ids = items.Select(item => item.RowId).ToList();
        await _metadataRepository.EnsureObjectsAsync(environmentKey, KeyCertificateObjectType, ids, cancellationToken);
        var metadata = await _metadataRepository.GetObjectMetadataBatchAsync(environmentKey, KeyCertificateObjectType, ids, cancellationToken);
        var rows = items
            .Select(item => BuildKeyCertificateGridRow(item, metadata.TryGetValue(item.RowId, out var itemMetadata) ? itemMetadata : null))
            .Select(GridClientContract.FromRow)
            .ToList();
        return Json(new { success = true, refreshedUtc = snapshot.RefreshedUtc, rows, refreshRecommended });
    }

    // Purpose: Creates SSH key after validating the supplied request.
    [HttpPost, ValidateAntiForgeryToken]
    [MoveItObjectPermission("SSH", "Create", "")]
    public async Task<IActionResult> CreateSshKey([FromBody] SshKeyCreateRequest? request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequest(new { message = "SSH Key creation request is required." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();
        try
        {
            var created = await _keyCertificateService.CreateSshKeyAsync(request.Name, request.KeyType, request.KeySize, cancellationToken, request.ResourceGroup);
            try { await _metadataRepository.RecordObjectCreatedAsync(session.GetEnvironmentKey(), KeyCertificateObjectType, created.RowId, session.Username, cancellationToken); }
            catch (Exception ex) { _logger.LogError(ex, "SSH key {KeyId} was created but MAUI could not record creation metadata.", created.Id); }
            return Json(new { success = true, item = new { rowId = created.RowId, id = created.Id, name = created.Name, type = created.Type } });
        }
        catch (SshKeyNameAlreadyExistsException ex) { return Conflict(new { message = ex.Message }); }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Creates TLS certificate after validating the supplied request.
    [HttpPost, ValidateAntiForgeryToken]
    [MoveItObjectPermission("TLS", "Create", "")]
    public async Task<IActionResult> CreateTlsCertificate([FromBody] TlsCertificateCreateRequest? request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequest(new { message = "TLS Certificate creation request is required." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();
        try
        {
            var created = await _keyCertificateService.CreateTlsCertificateAsync(
                request.Name,
                request.EmailAddress,
                request.Country,
                request.State,
                request.Organization,
                request.OrganizationalUnit,
                request.ExpiresAfter,
                request.ExpireUnit,
                request.KeySize,
                cancellationToken, request.ResourceGroup);

            try { await _metadataRepository.RecordObjectCreatedAsync(session.GetEnvironmentKey(), KeyCertificateObjectType, created.RowId, session.Username, cancellationToken); }
            catch (Exception ex) { _logger.LogError(ex, "TLS certificate {CertificateId} was created but MAUI could not record creation metadata.", created.Id); }

            return Json(new { success = true, item = new { rowId = created.RowId, id = created.Id, name = created.Name, type = created.Type, sha1Thumbprint = created.Sha1Thumbprint } });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Creates PGP key after validating the supplied request.
    [HttpPost, ValidateAntiForgeryToken]
    [MoveItObjectPermission("PGP", "Create", "")]
    public async Task<IActionResult> CreatePgpKey([FromBody] PgpKeyCreateRequest? request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequest(new { message = "PGP Key creation request is required." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();
        var licenseBlock = MoveItLicensePolicy.GetPgpBlock(session);
        if (!string.IsNullOrWhiteSpace(licenseBlock)) return StatusCode(StatusCodes.Status403Forbidden, new { message = licenseBlock });

        try
        {
            var created = await _keyCertificateService.CreatePgpKeyAsync(
                request.Name,
                request.EmailAddress,
                request.KeyType,
                request.KeyLength,
                request.HashAlgorithm,
                request.ExpiresAfter,
                request.ExpireUnit,
                request.Password,
                request.ConfirmPassword,
                cancellationToken, request.ResourceGroup);

            try { await _metadataRepository.RecordObjectCreatedAsync(session.GetEnvironmentKey(), KeyCertificateObjectType, created.RowId, session.Username, cancellationToken); }
            catch (Exception ex) { _logger.LogError(ex, "PGP key {KeyId} was created but MAUI could not record creation metadata.", created.Id); }

            return Json(new { success = true, item = new { rowId = created.RowId, id = created.Id, name = created.Name, type = created.Type, uid = created.Uid } });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Imports key certificate after validating the package and target.
    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(12 * 1024 * 1024)]
    [MoveItObjectPermission("KeyImport", "Create", "")]
    public async Task<IActionResult> ImportKeyCertificate([FromForm] KeyCertificateImportRequest? request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequest(new { message = "Key/Certificate import request is required." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();
        if (request.File is null || request.File.Length == 0) return BadRequest(new { message = "Select a key or certificate file to import." });
        if (request.File.Length > 10 * 1024 * 1024) return BadRequest(new { message = "The import file cannot exceed 10 MB." });
        if (request.Type.Equals("PGP", StringComparison.OrdinalIgnoreCase))
        {
            var licenseBlock = MoveItLicensePolicy.GetPgpBlock(session);
            if (!string.IsNullOrWhiteSpace(licenseBlock)) return StatusCode(StatusCodes.Status403Forbidden, new { message = licenseBlock });
        }

        try
        {
            byte[] fileBytes;
            await using (var input = request.File.OpenReadStream())
            await using (var buffer = new MemoryStream())
            {
                await input.CopyToAsync(buffer, cancellationToken);
                fileBytes = buffer.ToArray();
            }

            var imported = await _keyCertificateService.ImportAsync(
                request.Type,
                request.Name,
                request.File.FileName,
                fileBytes,
                request.Password,
                cancellationToken, request.ResourceGroup);

            try
            {
                await _metadataRepository.RecordObjectCreatedAsync(session.GetEnvironmentKey(), KeyCertificateObjectType, imported.RowId, session.Username, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Imported key/certificate {ResourceId} was created in MOVEit but MAUI could not record creation metadata.", imported.Id);
            }

            return Json(new
            {
                success = true,
                item = new { rowId = imported.RowId, id = imported.Id, name = imported.Name, type = imported.Type }
            });
        }
        catch (SshKeyNameAlreadyExistsException ex) { return Conflict(new { message = ex.Message }); }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Returns key/certificate details, metadata, and dependencies.
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> KeyCertificateDetails(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { message = "Key/Certificate identity is required." });
        var currentSession = _sessionService.Get();
        if (currentSession is null) return MauiControllerExceptionMapper.SessionExpired();
        if (!MoveItLicensePolicy.GetCapabilities(currentSession).PgpLicensed && id.Trim().StartsWith("pgp|", StringComparison.OrdinalIgnoreCase))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = MoveItLicensePolicy.GetPgpBlock(currentSession) });
        try
        {
            var session = RequireSession();
            var itemTask = _keyCertificateService.GetAsync(id, cancellationToken);
            var dependencyTask = _keyCertificateService.GetDependenciesAsync(id, cancellationToken);
            var metadataTask = _metadataRepository.GetObjectMetadataAsync(session.GetEnvironmentKey(), KeyCertificateObjectType, id.Trim(), cancellationToken);
            await Task.WhenAll(itemTask, dependencyTask, metadataTask);
            var item = await itemTask;
            var deps = await dependencyTask;
            var metadata = await metadataTask;
            if (!MoveItLicensePolicy.GetCapabilities(session).PgpLicensed && item.Kind == KeyCertificateKind.Pgp)
            {
                var licenseBlock = MoveItLicensePolicy.GetPgpBlock(session);
                return StatusCode(StatusCodes.Status403Forbidden, new { message = licenseBlock });
            }
            return Json(new
            {
                item = new
                {
                    rowId = item.RowId, id = item.Id, name = item.Name, type = item.Type,
                    resourceGroups = item.Group, publicPrivate = item.PublicPrivate,
                    createdDate = item.CreatedDate, expirationDate = item.ExpirationDate,
                    isExpired = KeyCertificateExpiration.IsExpired(item), symmetricAlgorithm = item.SymmetricAlgorithm,
                    keyType = item.KeyType, keySize = item.KeySize, fingerprint = item.Fingerprint,
                    publicKeySsh = item.PublicKeySsh, publicKeyOpenSsh = item.PublicKeyOpenSsh,
                    validFromDate = item.ValidFromDate, sha1Thumbprint = item.Sha1Thumbprint, serialNumber = item.SerialNumber
                },
                metadata = metadata.Values,
                fields = _metadataOptions.KeysAndCertificates.Fields,
                audit = new { createdUtc = metadata.CreatedUtc, createdBy = metadata.CreatedBy, modifiedUtc = metadata.ModifiedUtc, modifiedBy = metadata.ModifiedBy },
                tasks = deps.Tasks.Select(task => new { id = task.Id, name = task.Name }),
                hosts = deps.Hosts.Select(host => new { id = host.Id, name = host.Name, type = host.Type })
            });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Saves key certificate metadata after validating the supplied values.
    [HttpPost, ValidateAntiForgeryToken]
    [MoveItObjectPermission("KeyCertificate", "Edit", "RowId")]
    public async Task<IActionResult> SaveKeyCertificateMetadata([FromBody] KeyCertificateMetadataUpdateRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RowId)) return BadRequest(new { message = "Key/Certificate identity is required." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();
        if (!MoveItLicensePolicy.GetCapabilities(session).PgpLicensed && request.RowId.Trim().StartsWith("pgp|", StringComparison.OrdinalIgnoreCase))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = MoveItLicensePolicy.GetPgpBlock(session) });
        try
        {
            var item = await _keyCertificateService.GetAsync(request.RowId, cancellationToken);
            if (!MoveItLicensePolicy.GetCapabilities(session).PgpLicensed && item.Kind == KeyCertificateKind.Pgp)
            {
                var licenseBlock = MoveItLicensePolicy.GetPgpBlock(session);
                return StatusCode(StatusCodes.Status403Forbidden, new { message = licenseBlock });
            }
            var values = await _metadataValueValidation.FilterAndValidateAsync(session.GetEnvironmentKey(), _metadataOptions.KeysAndCertificates, request.Metadata, cancellationToken);
            await _metadataRepository.SaveObjectMetadataAsync(session.GetEnvironmentKey(), KeyCertificateObjectType, item.RowId, values, session.Username, cancellationToken);
            return Json(new { success = true });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Validates delete key certificate and returns the information required before execution.
    [HttpPost, ValidateAntiForgeryToken]
    [MoveItObjectPermission("KeyCertificate", "Delete", "RowId")]
    public async Task<IActionResult> PreflightDeleteKeyCertificate([FromBody] KeyCertificateActionRequest? request, CancellationToken cancellationToken)
    {
        var rowId = (request?.RowId ?? string.Empty).Trim();
        if (rowId.Length == 0) return BadRequest(new { message = "Key/Certificate identity is required." });
        var pgpBlock = BlockUnlicensedPgpRows(new[] { rowId });
        if (pgpBlock is not null) return pgpBlock;
        try
        {
            var result = (await _keyCertificateService.PreflightDeleteAsync(new[] { rowId }, cancellationToken)).Single();
            return Json(new { success = result.CanDelete, item = ToDeletePreflight(result), message = result.Message });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Deletes key certificate after applying the required guardrails.
    [HttpPost, ValidateAntiForgeryToken]
    [MoveItObjectPermission("KeyCertificate", "Delete", "RowId")]
    public async Task<IActionResult> DeleteKeyCertificate([FromBody] KeyCertificateActionRequest? request, CancellationToken cancellationToken)
    {
        var rowId = (request?.RowId ?? string.Empty).Trim();
        if (rowId.Length == 0) return BadRequest(new { message = "Key/Certificate identity is required." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();
        var pgpBlock = BlockUnlicensedPgpRows(new[] { rowId });
        if (pgpBlock is not null) return pgpBlock;
        try
        {
            var result = await _keyCertificateService.DeleteAsync(new[] { rowId }, cancellationToken);
            var deleted = result.Items.Where(item => item.Success).Select(item => item.RowId).ToList();
            if (deleted.Count > 0) await _metadataRepository.DeleteObjectsAsync(session.GetEnvironmentKey(), KeyCertificateObjectType, deleted, cancellationToken);
            var item = result.Items.Single();
            return Json(new { success = item.Success, rowId = item.RowId, name = item.Name, type = item.Type, skipped = item.Skipped, message = item.Message });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Validates delete key certificates and returns the information required before execution.
    [HttpPost, ValidateAntiForgeryToken]
    [MoveItObjectPermission("KeyCertificate", "Delete", "RowIds")]
    public async Task<IActionResult> PreflightDeleteKeyCertificates([FromBody] BulkKeyCertificateRequest? request, CancellationToken cancellationToken)
    {
        if (request?.RowIds is null || request.RowIds.Count == 0) return BadRequest(new { message = "Select at least one Key or Certificate." });
        var pgpBlock = BlockUnlicensedPgpRows(request.RowIds);
        if (pgpBlock is not null) return pgpBlock;
        try
        {
            var results = await _keyCertificateService.PreflightDeleteAsync(request.RowIds, cancellationToken);
            var blockers = results.Where(item => !item.CanDelete).ToList();
            return Json(new { success = blockers.Count == 0, items = results.Select(ToDeletePreflight), message = blockers.Count == 0 ? string.Empty : string.Join(" ", blockers.Select(item => item.Message)) });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Deletes key certificates after applying the required guardrails.
    [HttpPost, ValidateAntiForgeryToken]
    [MoveItObjectPermission("KeyCertificate", "Delete", "RowIds")]
    public async Task<IActionResult> DeleteKeyCertificates([FromBody] BulkKeyCertificateRequest? request, CancellationToken cancellationToken)
    {
        if (request?.RowIds is null || request.RowIds.Count == 0) return BadRequest(new { message = "Select at least one Key or Certificate." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();
        var pgpBlock = BlockUnlicensedPgpRows(request.RowIds);
        if (pgpBlock is not null) return pgpBlock;
        try
        {
            var result = await _keyCertificateService.DeleteAsync(request.RowIds, cancellationToken);
            var deleted = result.Items.Where(item => item.Success).Select(item => item.RowId).ToList();
            if (deleted.Count > 0) await _metadataRepository.DeleteObjectsAsync(session.GetEnvironmentKey(), KeyCertificateObjectType, deleted, cancellationToken);
            return Json(new { success = result.Failed == 0 && result.Skipped == 0, succeeded = result.Succeeded, skipped = result.Skipped, failed = result.Failed, items = result.Items.Select(item => new { rowId = item.RowId, name = item.Name, type = item.Type, success = item.Success, skipped = item.Skipped, message = item.Message }), message = string.Join(" ", result.Items.Where(item => !item.Success).Select(item => item.Message)) });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Converts the current value to delete preflight.
    // Prevents direct/API access to PGP resources when the connected MOVEit license does not include PGP.
    private IActionResult? BlockUnlicensedPgpRows(IEnumerable<string> rowIds)
    {
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();
        if (MoveItLicensePolicy.GetCapabilities(session).PgpLicensed) return null;

        var containsPgp = rowIds
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Any(value => value.StartsWith("pgp|", StringComparison.OrdinalIgnoreCase));
        if (!containsPgp) return null;

        var licenseBlock = MoveItLicensePolicy.GetPgpBlock(session);
        return StatusCode(StatusCodes.Status403Forbidden, new { message = licenseBlock });
    }

    private static object ToDeletePreflight(KeyCertificateDeletePreflightResult item) => new
    {
        rowId = item.RowId, name = item.Name, type = item.Type, canDelete = item.CanDelete, message = item.Message,
        tasks = item.Tasks.Select(task => new { id = task.Id, name = task.Name }),
        hosts = item.Hosts.Select(host => new { id = host.Id, name = host.Name, type = host.Type })
    };

    // Purpose: Builds key certificate grid.
    private GridViewModel BuildKeyCertificateGrid(GridOptions options, IReadOnlyList<GridRow> rows) => new()
    {
        GridId = "keysandcertificates",
        EntityName = "Keys & Certificates",
        SearchPlaceholder = "Search names, types, fingerprints, certificate subjects, issuers, and metadata",
        Columns = BuildKeyCertificateColumns(), Rows = rows, Options = options, SearchColumns = options.SearchColumns,
        FitColumnsToGrid = _gridBehaviorOptions.FitColumnsToGrid.Value,
        SelectRowsBeyondGridSize = _gridBehaviorOptions.SelectRowsBeyondGridSize.Value,
        DefaultPageSize = _gridBehaviorOptions.DefaultRowSize.Value,
        PageSizeOptions = _gridBehaviorOptions.DefaultRowSize.PossibleValues
    };

    // Purpose: Builds key certificate grid options.
    private GridOptions BuildKeyCertificateGridOptions(GridOptions source)
    {
        var metadata = MetadataGridSupport.GetFieldColumnKeys(_metadataOptions.KeysAndCertificates).ToList();
        var native = new[] { "Id", "Name", "Type", "ExpirationDate", "Fingerprint", "KeyType", "KeySize", "Status", "CreatedDate", "PublicPrivate", "SymmetricAlgorithm", "Uid", "IssuedTo", "Issuer", "SerialNumber", "Sha1Thumbprint", "Store", "ValidFromDate", "PublicKeyOpenSsh", "PublicKeySsh", "Group" };
        var pgpLicensed = MoveItLicensePolicy.GetCapabilities(_sessionService.Get()).PgpLicensed;
        return new GridOptions
        {
            DefaultColumns = source.DefaultColumns.Count > 0 ? source.DefaultColumns.ToList() : new List<string> { "Name", "Type", "ExpirationDate" },
            AvailableColumns = (source.AvailableColumns.Count > 0 ? source.AvailableColumns : native.ToList()).Concat(new[] { "Group" }).Concat(MetadataGridSupport.GetAvailableColumnKeys(_metadataOptions.KeysAndCertificates)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            BulkActions = source.BulkActions.Count > 0 ? source.BulkActions.ToList() : new List<string> { "Delete Keys/Certificates" },
            AddActions = (source.AddActions.Count > 0 ? source.AddActions : new List<string> { "New SSH Key", "New TLS Certificate", "New PGP Key" })
                .Where(action => pgpLicensed || !action.Equals("New PGP Key", StringComparison.OrdinalIgnoreCase))
                .ToList(),
            SearchColumns = (source.SearchColumns.Count > 0 ? source.SearchColumns : new List<string> { "Name", "Type", "Fingerprint", "Uid", "IssuedTo", "Issuer", "Group" }).Concat(new[] { "Group" }).Concat(metadata).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    // Purpose: Builds key certificate columns.
    private IReadOnlyList<GridColumn> BuildKeyCertificateColumns()
    {
        var columns = new List<GridColumn>
        {
            new() { Key="Id", Label="ID" }, new() { Key="Name", Label="Name", DefaultWidth=350 }, new() { Key="Type", Label="Type", DefaultWidth=150 }, new() { Key="ExpirationDate", Label="Expiration Date", Type="datetime" },
            new() { Key="Fingerprint", Label="Fingerprint" }, new() { Key="KeyType", Label="Key Type" }, new() { Key="KeySize", Label="Key Size", Type="number" }, new() { Key="Status", Label="Status", DefaultWidth=150 },
            new() { Key="CreatedDate", Label="Created Date", Type="datetime" }, new() { Key="PublicPrivate", Label="Public / Private" }, new() { Key="SymmetricAlgorithm", Label="Symmetric Algorithm" }, new() { Key="Uid", Label="UID" },
            new() { Key="IssuedTo", Label="Issued To" }, new() { Key="Issuer", Label="Issuer" }, new() { Key="SerialNumber", Label="Serial Number" }, new() { Key="Sha1Thumbprint", Label="SHA-1 Thumbprint" },
            new() { Key="Store", Label="Store" }, new() { Key="ValidFromDate", Label="Valid From Date", Type="datetime" }, new() { Key="PublicKeyOpenSsh", Label="Public Key (OpenSSH)" }, new() { Key="PublicKeySsh", Label="Public Key (SSH)" }, new() { Key="Group", Label="Resource Group" }
        };
        MetadataGridSupport.AddColumns(columns, _metadataOptions.KeysAndCertificates);
        return columns;
    }

    // Purpose: Builds key certificate grid row.
    private GridRow BuildKeyCertificateGridRow(AutomationKeyCertificate item, ObjectMetadataSnapshot? metadata)
    {
        var values = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Id"]=item.Id,["Name"]=item.Name,["Type"]=item.Type,["ExpirationDate"]=item.ExpirationDate,["Fingerprint"]=item.Fingerprint,["KeyType"]=item.KeyType,["KeySize"]=item.KeySize,["Status"]=item.Status,["CreatedDate"]=item.CreatedDate,["PublicPrivate"]=item.PublicPrivate,["SymmetricAlgorithm"]=item.SymmetricAlgorithm,["Uid"]=item.Uid,["IssuedTo"]=item.IssuedTo,["Issuer"]=item.Issuer,["SerialNumber"]=item.SerialNumber,["Sha1Thumbprint"]=item.Sha1Thumbprint,["Store"]=item.Store,["ValidFromDate"]=item.ValidFromDate,["PublicKeyOpenSsh"]=item.PublicKeyOpenSsh,["PublicKeySsh"]=item.PublicKeySsh,["Group"]=item.Group
        };
        // MOVEit's native editor uses the TLS SHA-1 thumbprint and the persistent key ID for SSH/PGP.
        var nativeEditorId = item.Kind == KeyCertificateKind.Tls && !string.IsNullOrWhiteSpace(item.Sha1Thumbprint) ? item.Sha1Thumbprint : item.Id;
        var raw = new Dictionary<string,string>(values,StringComparer.OrdinalIgnoreCase) { ["CanEditDelete"] = item.CanEditDelete ? "1" : "0", ["Kind"] = item.Kind.ToString(), ["NativeEditorId"] = nativeEditorId, ["IsExpiredCertificate"] = KeyCertificateExpiration.IsExpired(item) ? "1" : "0" };
        MetadataGridSupport.AddValues(values, raw, _metadataOptions.KeysAndCertificates, metadata, FormatDate);
        return new GridRow { Id=item.RowId, InfoActionUrl=Url.Action(nameof(KeyCertificateDetails), new { id=item.RowId })??string.Empty, Values=values, RawValues=raw };
    }

    // Purpose: Builds empty key certificate grid.
    private GridViewModel BuildEmptyKeyCertificateGrid()
    {
        var options = BuildKeyCertificateGridOptions(_uiOptions.Grids.TryGetValue("keysandcertificates", out var configured) ? configured : new GridOptions());
        return BuildKeyCertificateGrid(options, Array.Empty<GridRow>());
    }
}

