// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Controllers;

public sealed partial class ResourcesController
{
    // Purpose: Renders the Global Parameters workspace shell immediately; inventory loads behind the shared Loading shell.
    public IActionResult GlobalParameters()
    {
        try
        {
            _ = RequireSession();
            return View("GlobalParameters", new GlobalParametersPageViewModel
            {
                UiGlobalParameterRefreshIntervalSeconds = Math.Max(0, _globalParameterBehaviorOptions.UiRefreshIntervalSeconds.Value),
                LastRefreshUtc = null,
                Grid = BuildEmptyGlobalParameterGrid()
            });
        }
        catch (MoveItAuthenticationException ex)
        {
            TempData["LoginError"] = ex.Message;
            return RedirectToAction("Login", "Account");
        }
    }

    // Purpose: Performs the initial asynchronous Global Parameter inventory load without forcing a new MOVEit read.
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> InitialGlobalParametersGrid(bool preferCached = false, CancellationToken cancellationToken = default)
    {
        try
        {
            var initial = preferCached
                ? await _globalParameterService.GetInitialSnapshotAsync(cancellationToken)
                : new InitialCacheSnapshot<GlobalParameterCacheSnapshot>(await _globalParameterService.GetSnapshotAsync(false, cancellationToken), RefreshRecommended: false);
            return await BuildGlobalParametersGridJsonAsync(initial.Snapshot, cancellationToken, initial.RefreshRecommended);
        }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Refreshes Global Parameter grid data through the shared cache contract; forceRefresh bypasses cache age.
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> RefreshGlobalParametersGrid(CancellationToken cancellationToken = default, bool forceRefresh = false)
    {
        try
        {
            var snapshot = await _globalParameterService.GetSnapshotAsync(forceRefresh, cancellationToken);
            return await BuildGlobalParametersGridJsonAsync(snapshot, cancellationToken);
        }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Builds the shared Global Parameter grid payload from one already-resolved inventory snapshot.
    private async Task<JsonResult> BuildGlobalParametersGridJsonAsync(GlobalParameterCacheSnapshot snapshot, CancellationToken cancellationToken, bool refreshRecommended = false)
    {
        var session = RequireSession();
        var environmentKey = session.GetEnvironmentKey();
        var names = snapshot.Parameters.Select(item => item.Name).ToList();
        await _metadataRepository.EnsureObjectsAsync(environmentKey, GlobalParameterObjectType, names, cancellationToken);
        var metadata = await _metadataRepository.GetObjectMetadataBatchAsync(environmentKey, GlobalParameterObjectType, names, cancellationToken);
        var rows = snapshot.Parameters
            .Select(item => BuildGlobalParameterGridRow(item, metadata.TryGetValue(item.Name, out var value) ? value : null))
            .Select(GridClientContract.FromRow)
            .ToList();
        return Json(new { success = true, refreshedUtc = snapshot.RefreshedUtc, rows, refreshRecommended });
    }

    // Purpose: Creates global parameter after validating the supplied request.
    [HttpPost, ValidateAntiForgeryToken]
    [MoveItObjectPermission("GlobalParameter", "Create", "")]
    public async Task<IActionResult> CreateGlobalParameter([FromBody] GlobalParameterCreateRequest? request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequest(new { message = "Global Parameter creation request is required." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();

        try
        {
            var created = await _globalParameterService.CreateAsync(request.Name, request.Value, request.Secure, cancellationToken);
            await _metadataRepository.RecordObjectCreatedAsync(session.GetEnvironmentKey(), GlobalParameterObjectType, created.Name, session.Username, cancellationToken);
            return Json(new { success = true, parameter = new { name = created.Name, secure = created.Secure } });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Returns Global Parameter details and MAUI metadata.
    [HttpGet]
    [MoveItObjectPermission("GlobalParameter", "Edit", "name")]
    public async Task<IActionResult> GlobalParameterDetails(string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { message = "Global Parameter Name is required." });
        try
        {
            var session = RequireSession();
            var itemTask = _globalParameterService.GetAsync(name, cancellationToken);
            var tasksTask = _globalParameterService.GetAssignedTasksAsync(name, cancellationToken);
            var metadataTask = _metadataRepository.GetObjectMetadataAsync(session.GetEnvironmentKey(), GlobalParameterObjectType, name.Trim(), cancellationToken);
            await Task.WhenAll(itemTask, metadataTask, tasksTask);
            var item = await itemTask;
            var edit = await _globalParameterService.GetEditSnapshotAsync(name, cancellationToken);
            var metadata = await metadataTask;

            return Json(new
            {
                tasks = (await tasksTask).Select(task => new { id = task.Id, name = task.Name }),
                editVersion = edit.EditVersion,
                parameter = new
                {
                    name = edit.Name,
                    value = edit.Value,
                    displayValue = item.Secure ? item.GridValue : item.Value,
                    secure = edit.Secure,
                    resourceGroups = item.Group
                },
                metadata = metadata.Values,
                fields = _metadataOptions.GlobalParameters.Fields,
                audit = new
                {
                    createdUtc = metadata.CreatedUtc,
                    createdBy = metadata.CreatedBy,
                    modifiedUtc = metadata.ModifiedUtc,
                    modifiedBy = metadata.ModifiedBy
                }
            });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Saves global parameter details after validating the supplied values.
    [HttpPost, ValidateAntiForgeryToken]
    [MoveItObjectPermission("GlobalParameter", "Edit", "OriginalName")]
    public async Task<IActionResult> SaveGlobalParameterDetails([FromBody] GlobalParameterDetailsUpdateRequest? request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequest(new { message = "Global Parameter update request is required." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();
        var originalName = (request.OriginalName ?? string.Empty).Trim();
        if (originalName.Length == 0) return BadRequest(new { message = "Original Global Parameter Name is required." });

        try
        {
            var environmentKey = session.GetEnvironmentKey();
            var metadata = await _metadataValueValidation.FilterAndValidateAsync(environmentKey, _metadataOptions.GlobalParameters, request.Metadata, cancellationToken);
            using var edit = ObjectEditVersion.Require("GlobalParameter", originalName, request.EditVersion);
            var saved = await _globalParameterService.UpdateAsync(
                originalName,
                request.Name,
                request.Value,
                request.Secure,
                request.PreserveExistingSecureValue,
                cancellationToken);
            if (!originalName.Equals(saved.Name, StringComparison.Ordinal))
                await _metadataRepository.RenameObjectIdAsync(environmentKey, GlobalParameterObjectType, originalName, saved.Name, cancellationToken);

            if (metadata.Count > 0)
                await _metadataRepository.SaveObjectMetadataAsync(environmentKey, GlobalParameterObjectType, saved.Name, metadata, session.Username, cancellationToken);
            await _metadataRepository.RecordObjectConfigurationModifiedAsync(environmentKey, GlobalParameterObjectType, saved.Name, session.Username, cancellationToken);

            return Json(new { success = true, parameter = new { name = saved.Name, secure = saved.Secure } });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Saves global parameter metadata after validating the supplied values.
    [HttpPost, ValidateAntiForgeryToken]
    [MoveItObjectPermission("GlobalParameter", "Edit", "Name")]
    public async Task<IActionResult> SaveGlobalParameterMetadata([FromBody] GlobalParameterMetadataUpdateRequest? request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequest(new { message = "Global Parameter metadata update request is required." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0) return BadRequest(new { message = "Global Parameter Name is required." });

        try
        {
            // Confirm the MOVEit object still exists before creating/updating MAUI-only metadata.
            await _globalParameterService.GetAsync(name, cancellationToken);
            var metadata = await _metadataValueValidation.FilterAndValidateAsync(session.GetEnvironmentKey(), _metadataOptions.GlobalParameters, request.Metadata, cancellationToken);

            await _metadataRepository.SaveObjectMetadataAsync(
                session.GetEnvironmentKey(),
                GlobalParameterObjectType,
                name,
                metadata,
                session.Username,
                cancellationToken);

            return Json(new { success = true });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Validates delete global parameter and returns the information required before execution.
    [HttpPost, ValidateAntiForgeryToken]
    [MoveItObjectPermission("GlobalParameter", "Delete", "Name")]
    public async Task<IActionResult> PreflightDeleteGlobalParameter([FromBody] GlobalParameterActionRequest? request, CancellationToken cancellationToken)
    {
        var name = (request?.Name ?? string.Empty).Trim();
        if (name.Length == 0) return BadRequest(new { message = "Global Parameter Name is required." });
        try
        {
            var result = (await _globalParameterService.PreflightDeleteAsync(new[] { name }, allowSecure: true, cancellationToken)).Single();
            return Json(new { success = result.CanDelete, item = new { name = result.Name, secure = result.Secure, canDelete = result.CanDelete, message = result.Message }, message = result.Message });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Deletes global parameter after applying the required guardrails.
    [HttpPost, ValidateAntiForgeryToken]
    [MoveItObjectPermission("GlobalParameter", "Delete", "Name")]
    public async Task<IActionResult> DeleteGlobalParameter([FromBody] GlobalParameterActionRequest? request, CancellationToken cancellationToken)
    {
        var name = (request?.Name ?? string.Empty).Trim();
        if (name.Length == 0) return BadRequest(new { message = "Global Parameter Name is required." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();
        try
        {
            var result = await _globalParameterService.DeleteAsync(new[] { name }, allowSecure: true, cancellationToken);
            var deleted = result.Items.Where(item => item.Success).Select(item => item.Name).ToList();
            if (deleted.Count > 0)
                await _metadataRepository.DeleteObjectsAsync(session.GetEnvironmentKey(), GlobalParameterObjectType, deleted, cancellationToken);
            var item = result.Items.Single();
            return Json(new { success = item.Success, name = item.Name, skipped = item.Skipped, message = item.Message });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Validates delete global parameters and returns the information required before execution.
    [HttpPost, ValidateAntiForgeryToken]
    [MoveItObjectPermission("GlobalParameter", "Delete", "Names")]
    public async Task<IActionResult> PreflightDeleteGlobalParameters([FromBody] BulkGlobalParameterRequest? request, CancellationToken cancellationToken)
    {
        if (request?.Names is null || request.Names.Count == 0) return BadRequest(new { message = "Select at least one Global Parameter." });
        try
        {
            var results = await _globalParameterService.PreflightDeleteAsync(request.Names, allowSecure: true, cancellationToken);
            var blockers = results.Where(item => !item.CanDelete).ToList();
            return Json(new
            {
                success = blockers.Count == 0,
                items = results.Select(item => new { name = item.Name, secure = item.Secure, canDelete = item.CanDelete, message = item.Message }),
                message = blockers.Count == 0 ? string.Empty : string.Join(" ", blockers.Select(item => item.Message))
            });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Deletes global parameters after applying the required guardrails.
    [HttpPost, ValidateAntiForgeryToken]
    [MoveItObjectPermission("GlobalParameter", "Delete", "Names")]
    public async Task<IActionResult> DeleteGlobalParameters([FromBody] BulkGlobalParameterRequest? request, CancellationToken cancellationToken)
    {
        if (request?.Names is null || request.Names.Count == 0) return BadRequest(new { message = "Select at least one Global Parameter." });
        var session = _sessionService.Get();
        if (session is null) return MauiControllerExceptionMapper.SessionExpired();
        try
        {
            var result = await _globalParameterService.DeleteAsync(request.Names, allowSecure: true, cancellationToken);
            var deleted = result.Items.Where(item => item.Success).Select(item => item.Name).ToList();
            if (deleted.Count > 0)
                await _metadataRepository.DeleteObjectsAsync(session.GetEnvironmentKey(), GlobalParameterObjectType, deleted, cancellationToken);
            return Json(new
            {
                success = result.Failed == 0 && result.Skipped == 0,
                succeeded = result.Succeeded,
                skipped = result.Skipped,
                failed = result.Failed,
                items = result.Items.Select(item => new { name = item.Name, success = item.Success, skipped = item.Skipped, message = item.Message }),
                message = string.Join(" ", result.Items.Where(item => !item.Success).Select(item => item.Message))
            });
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Builds global parameter grid.
    private GridViewModel BuildGlobalParameterGrid(GridOptions options, IReadOnlyList<GridRow> rows) => new()
    {
        GridId = "globalparameters",
        EntityName = "Global Parameters",
        SearchPlaceholder = "Search parameter names and values",
        Columns = BuildGlobalParameterColumns(),
        Rows = rows,
        Options = options,
        SearchColumns = options.SearchColumns,
        FitColumnsToGrid = _gridBehaviorOptions.FitColumnsToGrid.Value,
        SelectRowsBeyondGridSize = _gridBehaviorOptions.SelectRowsBeyondGridSize.Value,
        DefaultPageSize = _gridBehaviorOptions.DefaultRowSize.Value,
        PageSizeOptions = _gridBehaviorOptions.DefaultRowSize.PossibleValues
    };

    // Purpose: Builds global parameter grid options.
    private GridOptions BuildGlobalParameterGridOptions(GridOptions source)
    {
        var metadataColumns = MetadataGridSupport.GetFieldColumnKeys(_metadataOptions.GlobalParameters).ToList();
        var actualColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Name", "Value", "Secure", "Group", "MetadataCreatedOn", "MetadataCreatedBy", "MetadataModifiedOn", "MetadataModifiedBy"
        };
        foreach (var column in metadataColumns) actualColumns.Add(column);

        var defaults = source.DefaultColumns.Count > 0 ? source.DefaultColumns.ToList() : new List<string> { "Name", "Value" };
        defaults = defaults.Where(actualColumns.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (defaults.Count == 0) defaults.AddRange(new[] { "Name", "Value" });

        var available = (source.AvailableColumns.Count > 0
                ? source.AvailableColumns
                : new List<string> { "Name", "Value", "Secure", "Group" })
            .Concat(new[] { "Group" })
            .Concat(MetadataGridSupport.GetAvailableColumnKeys(_metadataOptions.GlobalParameters))
            .Where(actualColumns.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var search = (source.SearchColumns.Count > 0 ? source.SearchColumns : new List<string> { "Name", "Value", "Group" })
            .Concat(new[] { "Group" })
            .Concat(metadataColumns)
            .Where(actualColumns.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new GridOptions
        {
            DefaultColumns = defaults,
            AvailableColumns = available,
            BulkActions = source.BulkActions.Count > 0 ? source.BulkActions.ToList() : new List<string> { "Delete Global Parameters" },
            AddActions = source.AddActions.Count > 0 ? source.AddActions.ToList() : new List<string> { "New Parameter" },
            SearchColumns = search
        };
    }

    // Purpose: Builds global parameter columns.
    private IReadOnlyList<GridColumn> BuildGlobalParameterColumns()
    {
        var columns = new List<GridColumn>
        {
            new() { Key = "Name", Label = "Parameter Name", DefaultWidth = 350 },
            new() { Key = "Value", Label = "Value" }
        };
        columns.Add(new GridColumn { Key = "Secure", Label = "Secure", Type = "boolean" });
        columns.Add(new GridColumn { Key = "Group", Label = "Resource Group" });
        MetadataGridSupport.AddColumns(columns, _metadataOptions.GlobalParameters);
        return columns;
    }

    // Purpose: Builds global parameter grid row.
    private GridRow BuildGlobalParameterGridRow(AutomationGlobalParameter item, ObjectMetadataSnapshot? metadata)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = item.Name,
            ["Value"] = item.GridValue,
            ["Secure"] = item.Secure ? "Yes" : "No",
            ["Group"] = item.Group
        };
        var raw = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase)
        {
            ["Secure"] = item.Secure ? "1" : "0",
            ["Selectable"] = "1"
        };

        MetadataGridSupport.AddValues(values, raw, _metadataOptions.GlobalParameters, metadata, FormatDate);

        return new GridRow
        {
            Id = item.Name,
            InfoActionUrl = Url.Action(nameof(GlobalParameterDetails), new { name = item.Name }) ?? string.Empty,
            Values = values,
            RawValues = raw
        };
    }

    // Purpose: Builds empty global parameter grid.
    private GridViewModel BuildEmptyGlobalParameterGrid()
    {
        var options = BuildGlobalParameterGridOptions(_uiOptions.Grids.TryGetValue("globalparameters", out var configured) ? configured : new GridOptions());
        return BuildGlobalParameterGrid(options, Array.Empty<GridRow>());
    }
}
