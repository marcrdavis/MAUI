using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Controllers;

public sealed partial class ResourcesController
{
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [MoveItObjectPermission("KeyCertificate", "Edit", "id")]
    public async Task<IActionResult> KeyCertificateExportOptions(string id, CancellationToken cancellationToken)
    {
        var block = BlockUnlicensedPgpRows([id]);
        if (block is not null) return block;
        try
        {
            var item = await _keyCertificateService.GetAsync(id, cancellationToken);
            if (item.Kind == KeyCertificateKind.Ssh) return BadRequest(new { message = "SSH key export is not supported." });
            return Json(new { rowId = item.RowId, name = item.Name, type = item.Type,
                isTls = item.Kind == KeyCertificateKind.Tls, publicPrivate = item.PublicPrivate,
                canIncludePrivateKey = KeyCertificateExport.CanIncludePrivateKey(item) });
        }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException)
        { return MauiControllerExceptionMapper.Map(ex); }
    }

    [HttpPost, ValidateAntiForgeryToken, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [MoveItObjectPermission("KeyCertificate", "Edit", "RowId")]
    public async Task<IActionResult> ExportKeyCertificate([FromBody] KeyCertificateExportRequest? request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequest(new { message = "Select a Key or Certificate to export." });
        var block = BlockUnlicensedPgpRows([request.RowId]);
        if (block is not null) return block;
        try
        {
            var download = await _keyCertificateService.ExportAsync(request, cancellationToken);
            Response.Headers["Cache-Control"] = "no-store";
            Response.Headers["Pragma"] = "no-cache";
            return File(download.Bytes, "application/octet-stream", download.FileName);
        }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException)
        { return MauiControllerExceptionMapper.Map(ex); }
    }
}
