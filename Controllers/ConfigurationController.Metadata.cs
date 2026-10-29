// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Metadata workbook endpoints for the Configuration workspace.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Controllers;

public sealed partial class ConfigurationController
{
    // Exports one object type's current MAUI metadata to the canonical Excel workbook used for bulk maintenance.
    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ExportMetadata(string objectType, CancellationToken cancellationToken)
    {
        var session = _sessionService.Get();
        if (session is null) return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });

        try
        {
            var result = await _metadataWorkbookService.ExportAsync(session, objectType, cancellationToken);
            return File(result.Content,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                result.FileName);
        }
        catch (MoveItAuthenticationException)
        {
            return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to export MAUI {ObjectType} metadata for {UserName}.", objectType, session.Username);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "The metadata workbook could not be exported. Review Configuration > Logs for details." });
        }
    }

    // Validates a MAUI metadata workbook and returns its proposed field-level changes without saving anything.
    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxMetadataImportBytes + 1024 * 1024)]
    public async Task<IActionResult> PreviewMetadataImport(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "Select a metadata workbook to import." });
        if (file.Length > MaxMetadataImportBytes) return BadRequest(new { message = "Metadata workbooks cannot exceed 10 MB." });
        var session = _sessionService.Get();
        if (session is null) return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });

        try
        {
            using var memory = new MemoryStream();
            await file.CopyToAsync(memory, cancellationToken);
            var preview = await _metadataWorkbookService.PreviewImportAsync(session, memory.ToArray(), file.FileName, cancellationToken);
            return Json(new
            {
                success = preview.IsValid,
                preview.FileName,
                preview.ObjectType,
                preview.ObjectTypeLabel,
                preview.EnvironmentKey,
                preview.RowsRead,
                preview.MatchedRows,
                preview.UnchangedRows,
                preview.ObjectsWithChanges,
                preview.ChangeCount,
                preview.ChangesTruncated,
                changes = preview.Changes.Select(change => new
                {
                    change.RowNumber,
                    change.ObjectId,
                    change.ObjectName,
                    change.FieldName,
                    change.FieldLabel,
                    change.CurrentValue,
                    change.NewValue,
                    change.ClearsValue
                }),
                errors = preview.Errors.Select(issue => new { issue.RowNumber, issue.Message }),
                warnings = preview.Warnings.Select(issue => new { issue.RowNumber, issue.Message })
            });
        }
        catch (MoveItAuthenticationException)
        {
            return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        }
        catch (Exception ex) when (ex is MoveItApiException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to preview MAUI metadata import {FileName} for {UserName}.", file.FileName, session.Username);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "The metadata workbook could not be validated. Review Configuration > Logs for details." });
        }
    }

    // Revalidates and applies a complete metadata workbook change set after the administrator accepts the preview.
    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxMetadataImportBytes + 1024 * 1024)]
    public async Task<IActionResult> ApplyMetadataImport(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "Select a metadata workbook to import." });
        if (file.Length > MaxMetadataImportBytes) return BadRequest(new { message = "Metadata workbooks cannot exceed 10 MB." });
        var session = _sessionService.Get();
        if (session is null) return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });

        try
        {
            using var memory = new MemoryStream();
            await file.CopyToAsync(memory, cancellationToken);
            var result = await _metadataWorkbookService.ApplyImportAsync(session, memory.ToArray(), file.FileName, cancellationToken);
            var message = result.ChangesApplied == 0
                ? "No metadata changes were found in the workbook."
                : $"Metadata import saved. {result.ObjectsChanged} objects and {result.ChangesApplied} fields were updated.";
            return Json(new
            {
                success = true,
                result.ObjectType,
                result.ObjectsChanged,
                result.ChangesApplied,
                result.UndoSnapshotId,
                message
            });
        }
        catch (MoveItAuthenticationException)
        {
            return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        }
        catch (Exception ex) when (ex is MoveItApiException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to apply MAUI metadata import {FileName} for {UserName}.", file.FileName, session.Username);
            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "The metadata import could not be saved. Review Configuration > Logs for details." });
        }
    }
}
