// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Globalization;
using MoveItAutomationUI.Services.MoveIt.Resources.DateLists;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Controllers;

public sealed partial class ResourcesController
{
    // Purpose: Renders the Date Lists workspace shell immediately; inventory loads behind the shared Loading shell.
    public IActionResult DateLists()
    {
        try
        {
            _ = RequireSession();
            return View("DateLists", new DateListsPageViewModel
            {
                UiDateListRefreshIntervalSeconds = Math.Max(0, _dateListBehaviorOptions.UiRefreshIntervalSeconds.Value),
                LastRefreshUtc = null,
                Grid = BuildEmptyDateListGrid()
            });
        }
        catch (MoveItAuthenticationException ex)
        {
            TempData["LoginError"] = ex.Message;
            return RedirectToAction("Login", "Account");
        }
    }

    // Purpose: Performs the initial asynchronous Date List inventory load without forcing a new MOVEit read.
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> InitialDateListsGrid(bool preferCached = false, CancellationToken cancellationToken = default)
    {
        try
        {
            var initial = preferCached
                ? await _dateListService.GetInitialSnapshotAsync(cancellationToken)
                : new InitialCacheSnapshot<DateListCacheSnapshot>(await _dateListService.GetSnapshotAsync(false, cancellationToken), RefreshRecommended: false);
            return await BuildDateListsGridJsonAsync(initial.Snapshot, cancellationToken, initial.RefreshRecommended);
        }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Refreshes Date List grid data through the shared cache contract; forceRefresh bypasses cache age.
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> RefreshDateListsGrid(CancellationToken cancellationToken = default, bool forceRefresh = false)
    {
        try
        {
            var snapshot = await _dateListService.GetSnapshotAsync(forceRefresh, cancellationToken);
            return await BuildDateListsGridJsonAsync(snapshot, cancellationToken);
        }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Builds the shared Date List grid payload from one already-resolved inventory snapshot.
    private async Task<JsonResult> BuildDateListsGridJsonAsync(DateListCacheSnapshot snapshot, CancellationToken cancellationToken, bool refreshRecommended = false)
    {
        var session = RequireSession();
        var environmentKey = session.GetEnvironmentKey();
        var ids = snapshot.DateLists.Select(item => item.Id).ToList();
        await _metadataRepository.EnsureObjectsAsync(environmentKey, DateListObjectType, ids, cancellationToken);
        var metadata = await _metadataRepository.GetObjectMetadataBatchAsync(environmentKey, DateListObjectType, ids, cancellationToken);
        var undo = await _dateListUndoService.GetAvailableIdsAsync(session.Username, ids, cancellationToken);
        var rows = snapshot.DateLists
            .Select(item => BuildDateListGridRow(item, metadata.TryGetValue(item.Id, out var itemMetadata) ? itemMetadata : null, undo.Contains(item.Id)))
            .Select(GridClientContract.FromRow)
            .ToList();
        return Json(new { success = true, refreshedUtc = snapshot.RefreshedUtc, rows, refreshRecommended });
    }

    // Purpose: Creates date list after validating the supplied request.
    [HttpPost, ValidateAntiForgeryToken]
    [MoveItObjectPermission("DateList", "Create", "")]
    public async Task<IActionResult> CreateDateList([FromBody] DateListCreateRequest? request,CancellationToken cancellationToken)
    {
        if(request is null)return BadRequest(new{message="Date List creation request is required."}); var session=_sessionService.Get(); if(session is null)return MauiControllerExceptionMapper.SessionExpired();
        try{var created=await _dateListService.CreateAsync(request.Name,request.Description,cancellationToken);await _metadataRepository.RecordObjectCreatedAsync(session.GetEnvironmentKey(),DateListObjectType,created.Id,session.Username,cancellationToken);return Json(new{success=true,dateList=new{id=created.Id,name=created.Name}});}
        catch(DateListWriteUncertainException ex) { return Conflict(new { message = ex.Message, dateListId = ex.DateListId, moveItSaved = true }); }
        catch(DateListNameAlreadyExistsException ex){return Conflict(new{message=ex.Message});}
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Exports date list in the MAUI portable format.
    [HttpGet]
    [MoveItObjectPermission("DateList", "Edit", "dateListId")]
    public async Task<IActionResult> ExportDateList(string dateListId, CancellationToken cancellationToken = default)
    {
        try { _ = RequireSession(); var export = await _resourcePortabilityService.ExportDateListAsync(dateListId, cancellationToken); return File(export.Content, "application/zip", export.FileName); }
        catch (Exception ex) when (ex is InvalidOperationException or MoveItApiException or MoveItComException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Exports selected Date Lists in one same-type MAUI portable package.
    [HttpPost, ValidateAntiForgeryToken]
    [MoveItObjectPermission("DateList", "Edit", "DateListIds")]
    public async Task<IActionResult> ExportDateLists([FromBody] BulkDateListRequest? request, CancellationToken cancellationToken = default)
    {
        if (request?.DateListIds is null || request.DateListIds.Count == 0) return BadRequest(new { message = "Select at least one Date List to export." });
        try { _ = RequireSession(); var export = await _resourcePortabilityService.ExportDateListsAsync(request.DateListIds, cancellationToken); return File(export.Content, "application/zip", export.FileName); }
        catch (Exception ex) when (ex is InvalidOperationException or MoveItApiException or MoveItComException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Validates import date list and returns the information required before execution.
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(MaxResourceImportBytes + (1024 * 1024))]
    [MoveItObjectPermission("DateList", "Import", "")]
    public async Task<IActionResult> PreflightImportDateList([FromForm] ResourceImportPreflightRequest? request, CancellationToken cancellationToken = default)
    {
        var validation = ValidateResourceImportFile(request?.File, "Date List"); if (validation is not null) return validation;
        try { _ = RequireSession(); using var memory = new MemoryStream(); await request!.File!.CopyToAsync(memory, cancellationToken); var result = await _resourcePortabilityService.PreflightDateListImportAsync(memory.ToArray(), request.Mode, cancellationToken); return Json(new { success = true, result }); }
        catch (Exception ex) when (ex is InvalidOperationException or MoveItApiException or MoveItComException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Imports date list after validating the package and target.
    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(MaxResourceImportBytes + (1024 * 1024))]
    [MoveItObjectPermission("DateList", "Import", "")]
    public async Task<IActionResult> ImportDateList([FromForm] ResourceImportRequest? request, CancellationToken cancellationToken = default)
    {
        var validation = ValidateResourceImportFile(request?.File, "Date List"); if (validation is not null) return validation; var session = _sessionService.Get(); if (session is null) return MauiControllerExceptionMapper.SessionExpired();
        try { using var memory = new MemoryStream(); await request!.File!.CopyToAsync(memory, cancellationToken); var result = await _resourcePortabilityService.ImportDateListAsync(memory.ToArray(), request.Mode, request.AcknowledgeUnsupportedMetadata, session.Username, cancellationToken); return Json(new { success = true, id = result.Id, name = result.Name, created = result.Created, warning = result.Warning, itemCount = result.ItemCount, importedCount = result.ImportedCount, createdCount = result.CreatedCount, overwrittenCount = result.OverwrittenCount, failedCount = result.FailedCount, items = result.Items }); }
        catch (DateListNameAlreadyExistsException ex) { return Conflict(new { message = ex.Message }); }
        catch (Exception ex) when (ex is InvalidOperationException or MoveItApiException or MoveItComException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Returns Date List details, metadata, and dependency information.
    [HttpGet]
    public async Task<IActionResult> DateListDetails(string id,CancellationToken cancellationToken)
    {
        if(string.IsNullOrWhiteSpace(id))return BadRequest(new{message="Date List ID is required."});
        try{var session=RequireSession();var itemTask=_dateListService.GetAsync(id,cancellationToken);var taskRefsTask=_dateListService.GetAssignedTasksAsync(id,cancellationToken);var metadataTask=_metadataRepository.GetObjectMetadataAsync(session.GetEnvironmentKey(),DateListObjectType,id.Trim(),cancellationToken);await Task.WhenAll(itemTask,taskRefsTask,metadataTask);var item=await itemTask;var metadata=await metadataTask;return Json(new{editVersion=ObjectEditVersion.Create("DateList",item.Id,ObjectEditVersion.Named(item.Name,item.Description)),dateList=new{id=item.Id,name=item.Name,description=item.Description,resourceGroups=item.Group,entryCount=item.EntryCount},metadata=metadata.Values,fields=_metadataOptions.DateLists.Fields,audit=new{createdUtc=metadata.CreatedUtc,createdBy=metadata.CreatedBy,modifiedUtc=metadata.ModifiedUtc,modifiedBy=metadata.ModifiedBy},tasks=(await taskRefsTask).Select(t=>new{id=t.Id,name=t.Name})});}
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Saves date list details after validating the supplied values.
    [HttpPost, ValidateAntiForgeryToken]
    [MoveItObjectPermission("DateList", "Edit", "id")]
    public async Task<IActionResult> SaveDateListDetails(string id,[FromBody] DateListDetailsUpdateRequest? request,CancellationToken cancellationToken)
    {
        if(string.IsNullOrWhiteSpace(id))return BadRequest(new{message="Date List ID is required."});if(request is null)return BadRequest(new{message="Date List update request is required."});var session=_sessionService.Get();if(session is null)return MauiControllerExceptionMapper.SessionExpired();
        try
        {
            using var edit = ObjectEditVersion.Require("DateList", id.Trim(), request.EditVersion);
            var values = await _metadataValueValidation.FilterAndValidateAsync(session.GetEnvironmentKey(), _metadataOptions.DateLists, request.Metadata, cancellationToken);
            await _dateListService.UpdateBasicDetailsAsync(id, request.Name ?? "", request.Description ?? "", cancellationToken);
            await _metadataRepository.SaveObjectMetadataAsync(session.GetEnvironmentKey(),DateListObjectType,id.Trim(),values,session.Username,cancellationToken);await _metadataRepository.RecordObjectConfigurationModifiedAsync(session.GetEnvironmentKey(),DateListObjectType,id.Trim(),session.Username,cancellationToken);return Json(new{success=true});
        }
        catch (DateListWriteUncertainException ex) { return Conflict(new { message = ex.Message, moveItSaved = true }); }
        catch (ObjectEditConflictException ex) { return Conflict(new { message = ex.Message, editConflict = true, moveItSaved = false }); }
        catch (ObjectCheckoutReleaseException ex) { return Conflict(new { message = ex.Message, moveItSaved = true }); }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Captures a Date List undo snapshot before opening the native editor.
    [HttpPost, ValidateAntiForgeryToken]
    [MoveItObjectPermission("DateList", "Edit", "DateListId")]
    public async Task<IActionResult> PrepareDateListEdit([FromBody] DateListActionRequest? request,CancellationToken cancellationToken)
    {var id=(request?.DateListId??"").Trim();if(id.Length==0)return BadRequest(new{message="Date List ID is required."});var session=_sessionService.Get();if(session is null)return MauiControllerExceptionMapper.SessionExpired();try{var s=await NativeEditorSnapshotRetry.CaptureAsync(token=>_dateListUndoService.CaptureAsync(id,session.Username,token),"DateList",id,_logger,cancellationToken);return Json(new{success=true,dateListId=s.Id,dateListName=s.Name,snapshotCreatedUtc=s.CreatedUtc});}catch (Exception ex) when (ex is MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }}

    // Purpose: Validates undo date list and returns the information required before execution.
    [HttpPost, ValidateAntiForgeryToken]
    [MoveItObjectPermission("DateList", "Edit", "DateListId")]
    public async Task<IActionResult> PreflightUndoDateList([FromBody] DateListActionRequest? request,CancellationToken cancellationToken)
    {var id=(request?.DateListId??"").Trim();var session=_sessionService.Get();if(id.Length==0)return BadRequest(new{message="Date List ID is required."});if(session is null)return MauiControllerExceptionMapper.SessionExpired();var r=await _dateListUndoService.GetHistoryAsync(id,session.Username,cancellationToken);return Json(new{success=r.CanUndo,dateListId=r.ObjectId,dateListName=r.ObjectName,snapshots=r.Snapshots.Select(item=>new{snapshotId=item.SnapshotId,snapshotCreatedUtc=item.CreatedUtc,snapshotCreatedBy=item.CreatedBy,canRestore=item.CanRestore,message=item.Message}),message=r.Message});}

    // Purpose: Restores the selected Date List undo snapshot.
    [HttpPost, ValidateAntiForgeryToken]
    [MoveItObjectPermission("DateList", "Edit", "DateListId")]
    public async Task<IActionResult> UndoDateList([FromBody] DateListActionRequest? request,CancellationToken cancellationToken)
    {var id=(request?.DateListId??"").Trim();var session=_sessionService.Get();if(id.Length==0)return BadRequest(new{message="Date List ID is required."});if(session is null)return MauiControllerExceptionMapper.SessionExpired();try{var snapshotId=(request?.SnapshotId??string.Empty).Trim();if(snapshotId.Length==0)return BadRequest(new{message="Undo snapshot is required."});var r=await _dateListUndoService.RestoreAsync(id,session.Username,snapshotId,cancellationToken);return Json(new{success=true,dateListId=r.Id,dateListName=r.Name,warning=r.Warning});}catch (Exception ex) when (ex is MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }}

    // Purpose: Marks date list edited in the current MAUI state.
    [HttpPost, ValidateAntiForgeryToken]
    [MoveItObjectPermission("DateList", "Edit", "DateListId")]
    public async Task<IActionResult> MarkDateListEdited([FromBody] DateListActionRequest? request,CancellationToken cancellationToken)
    {var id=(request?.DateListId??"").Trim();var session=_sessionService.Get();if(id.Length==0)return BadRequest(new{message="Date List ID is required."});if(session is null)return MauiControllerExceptionMapper.SessionExpired();await _metadataRepository.RecordObjectConfigurationModifiedAsync(session.GetEnvironmentKey(),DateListObjectType,id,session.Username,cancellationToken);return Json(new{success=true});}

    // Purpose: Validates delete date lists and returns the information required before execution.
    [HttpPost, ValidateAntiForgeryToken]
    [MoveItObjectPermission("DateList", "Delete", "DateListIds")]
    public async Task<IActionResult> PreflightDeleteDateLists([FromBody] BulkDateListRequest? request,CancellationToken cancellationToken)
    {if(request?.DateListIds is null||request.DateListIds.Count==0)return BadRequest(new{message="Select at least one Date List."});var r=await _dateListService.PreflightDeleteAsync(request.DateListIds,cancellationToken);var blockers=r.Where(x=>!x.CanDelete).ToList();return Json(new{success=blockers.Count==0, blockers=blockers.Select(x=>new{dateListId=x.DateListId,dateListName=x.DateListName,taskReferences=x.TaskReferences.Select(t=>new{id=t.Id,name=t.Name}),message=x.Message}), items=r.Select(x=>new{id=x.DateListId,name=x.DateListName,canDelete=x.CanDelete,message=x.Message}),message=blockers.Count==0?string.Empty:string.Join(" ",blockers.Select(x=>x.Message))});}

    // Purpose: Deletes date lists after applying the required guardrails.
    [HttpPost, ValidateAntiForgeryToken]
    [MoveItObjectPermission("DateList", "Delete", "DateListIds")]
    public async Task<IActionResult> DeleteDateLists([FromBody] BulkDateListRequest? request,CancellationToken cancellationToken)
    {
        if(request?.DateListIds is null||request.DateListIds.Count==0)return BadRequest(new{message="Select at least one Date List."});
        var session=_sessionService.Get();if(session is null)return MauiControllerExceptionMapper.SessionExpired();
        try
        {
            var recycleCapture=await _recycleBinService.CaptureForDeletionAsync(DateListObjectType,request.DateListIds,session.Username,request.DeleteWithoutRecycle,cancellationToken);
            if(recycleCapture.NeedsConfirmation&&!request.DeleteWithoutRecycle)
                return StatusCode(StatusCodes.Status409Conflict,new{code="RecycleCaptureFailed",message="MAUI could not create a recovery copy for one or more selected Date Lists. Delete anyway? Date Lists deleted without a recovery copy cannot be restored from the Recycle Bin.",failures=recycleCapture.Failures.Select(item=>new{id=item.ObjectId,message=item.Message})});

            var r=await _dateListService.DeleteAsync(request.DateListIds,cancellationToken);
            var deleted=r.Items.Where(x=>x.Success).Select(x=>x.Id).ToList();
            var deletedIdSet=deleted.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var unusedRecycleIds=recycleCapture.RecycleIdsByObjectId.Where(pair=>!deletedIdSet.Contains(pair.Key)).Select(pair=>pair.Value).ToList();
            if(unusedRecycleIds.Count>0)
            {
                try{await _recycleBinService.DiscardCapturedAsync(unusedRecycleIds,cancellationToken);}
                catch(Exception ex) when(ex is not OperationCanceledException){_logger.LogWarning(ex,"MAUI could not remove unused Recycle Bin snapshots after one or more Date List deletes failed.");}
            }

            var cleanupWarnings=new List<string>();
            if(deleted.Count>0)
            {
                try{await _metadataRepository.DeleteObjectsAsync(session.GetEnvironmentKey(),DateListObjectType,deleted,cancellationToken);}
                catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested){throw;}
                catch(Exception ex){_logger.LogError(ex,"Date Lists were deleted in MOVEit but MAUI metadata cleanup failed for Date List IDs {DateListIds}.",string.Join(",",deleted));cleanupWarnings.Add("MAUI could not remove all local Date List metadata.");}
                try{await _dateListUndoService.DeleteSnapshotsAsync(deleted,cancellationToken);}
                catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested){throw;}
                catch(Exception ex){_logger.LogError(ex,"Date Lists were deleted in MOVEit but MAUI undo cleanup failed for Date List IDs {DateListIds}.",string.Join(",",deleted));cleanupWarnings.Add("MAUI could not remove all local Date List undo snapshots.");}
            }
            var warning=cleanupWarnings.Count==0?string.Empty:$"The MOVEit Date List deletion succeeded for one or more Date Lists, but {string.Join(" ",cleanupWarnings)} Review the MAUI application log if stale local records remain.";
            return Json(new{success=r.Items.All(x=>x.Success),deleted=deleted.Count,succeeded=r.Items.Count(x=>x.Success),skipped=r.Items.Count(x=>x.Skipped),failed=r.Items.Count(x=>!x.Success&&!x.Skipped),warning,items=r.Items.Select(x=>new{id=x.Id,name=x.Name,success=x.Success,skipped=x.Skipped,message=x.Message}),message=string.Join(" ",r.Items.Where(x=>!x.Success).Select(x=>x.Message))});
        }
        catch (Exception ex) when (ex is MoveItApiException or MoveItComException or InvalidOperationException or ArgumentException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // Purpose: Builds date list grid.
    private GridViewModel BuildDateListGrid(GridOptions options,IReadOnlyList<GridRow> rows)=>new(){GridId="datelists",EntityName="Date Lists",SearchPlaceholder="Search date list names, descriptions, and metadata",Columns=BuildDateListColumns(),Rows=rows,Options=options,SearchColumns=options.SearchColumns,FitColumnsToGrid=_gridBehaviorOptions.FitColumnsToGrid.Value,SelectRowsBeyondGridSize=_gridBehaviorOptions.SelectRowsBeyondGridSize.Value,DefaultPageSize=_gridBehaviorOptions.DefaultRowSize.Value,PageSizeOptions=_gridBehaviorOptions.DefaultRowSize.PossibleValues};
    // Purpose: Builds date list grid options.
    private GridOptions BuildDateListGridOptions(GridOptions source)
    {
        var metadataKeys = MetadataGridSupport.GetFieldColumnKeys(_metadataOptions.DateLists).ToList();
        return new GridOptions
        {
            DefaultColumns = source.DefaultColumns.Count > 0 ? source.DefaultColumns.ToList() : new List<string> { "Name", "Description" },
            AvailableColumns = (source.AvailableColumns.Count > 0 ? source.AvailableColumns : new List<string> { "Id", "Name", "Description", "Group" })
                .Concat(new[] { "Group" })
                .Concat(MetadataGridSupport.GetAvailableColumnKeys(_metadataOptions.DateLists))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            BulkActions = source.BulkActions.Count > 0 ? source.BulkActions.ToList() : new List<string> { "Delete Date Lists" },
            AddActions = (source.AddActions.Count > 0 ? source.AddActions : new List<string> { "New Date List", "Import Date Lists" })
                .Where(action => !string.IsNullOrWhiteSpace(action))
                .Select(action => action.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            SearchColumns = (source.SearchColumns.Count > 0 ? source.SearchColumns : new List<string> { "Name", "Description", "Group" })
                .Concat(new[] { "Group" })
                .Concat(metadataKeys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    // Purpose: Builds date list columns.
    private IReadOnlyList<GridColumn> BuildDateListColumns()
    {
        var columns = new List<GridColumn>
        {
            new() { Key = "Id", Label = "Date List ID" },
            new() { Key = "Name", Label = "Date List Name", DefaultWidth = 300 },
            new() { Key = "Description", Label = "Description", DefaultWidth = 300 },
            new() { Key = "Group", Label = "Resource Group" }
        };
        MetadataGridSupport.AddColumns(columns, _metadataOptions.DateLists);
        return columns;
    }
    // Purpose: Builds date list grid row.
    private GridRow BuildDateListGridRow(AutomationDateList item, ObjectMetadataSnapshot? metadata, bool hasUndo)
    {
        var values = new Dictionary<string, string>
        {
            ["Id"] = item.Id,
            ["Name"] = item.Name,
            ["Description"] = item.Description,
            ["Group"] = item.Group
        };
        var raw = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase)
        {
            ["CanEditDelete"] = item.CanEditDelete ? "1" : "0",
            ["HasUndo"] = hasUndo ? "1" : "0"
        };
        MetadataGridSupport.AddValues(values, raw, _metadataOptions.DateLists, metadata, FormatDate);
        return new GridRow
        {
            Id = item.Id,
            InfoActionUrl = Url.Action(nameof(DateListDetails), new { id = item.Id }) ?? string.Empty,
            Values = values,
            RawValues = raw
        };
    }
    // Purpose: Builds empty date list grid.
    private GridViewModel BuildEmptyDateListGrid(){var o=BuildDateListGridOptions(_uiOptions.Grids.TryGetValue("datelists",out var g)?g:new GridOptions());return BuildDateListGrid(o,Array.Empty<GridRow>());}
}
