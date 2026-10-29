// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Controllers;

public sealed partial class ResourcesController
{
    private static bool GroupsAndPermissionsEnabled(IConfiguration configuration)
        => bool.TryParse(configuration["Editors:UseMauiGroupsAndPermissionsEditor:Value"], out var enabled) && enabled;

    public IActionResult GroupsAndPermissions([FromServices] IConfiguration configuration)
    {
        // Read the same live configuration as navigation and the other editor preferences.
        // An unbound IOptionsSnapshot would silently return the default false after enabling the setting.
        if (!GroupsAndPermissionsEnabled(configuration)) return NotFound();
        try
        {
            if (RequireSession().Info?.IsAdmin != true) return Forbid();
            return View("GroupsAndPermissions", new GroupsAndPermissionsPageViewModel(
                _gridBehaviorOptions.DefaultRowSize.Value, _gridBehaviorOptions.DefaultRowSize.PossibleValues));
        }
        catch (MoveItAuthenticationException ex)
        {
            TempData["LoginError"] = ex.Message;
            return RedirectToAction("Login", "Account");
        }
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ResourceGroupPermissions([FromServices] IResourceGroupPermissionsService service,
        [FromServices] IConfiguration configuration, bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!GroupsAndPermissionsEnabled(configuration)) return NotFound();
        try
        {
            if (RequireSession().Info?.IsAdmin != true) return Forbid();
            var snapshot = await service.GetSnapshotAsync(forceRefresh, cancellationToken);
            return Json(new { success = true, groups = snapshot.Groups, refreshedUtc = snapshot.RefreshedUtc });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException)
        {
            return MauiControllerExceptionMapper.Map(ex);
        }
    }

    // These grid/editor endpoints require a live MOVEit administrator; Task bulk Group operations retain their existing authorization.
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ResourceGroupEditor([FromServices] IResourceGroupManagementService service,
        [FromServices] IConfiguration configuration, string? name, CancellationToken cancellationToken = default)
    {
        if (!GroupsAndPermissionsEnabled(configuration)) return NotFound();
        try
        {
            if (RequireSession().Info?.IsAdmin != true) return Forbid();
            var setup = await service.PrepareEditorAsync(name, cancellationToken);
            return Json(new { success = true, setup.Editor, setup.AvailableItems });
        }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException) { return MauiControllerExceptionMapper.Map(ex); }
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> PreflightDeleteResourceGroup([FromServices] IResourceGroupManagementService service,
        [FromServices] IConfiguration configuration, string name, CancellationToken cancellationToken = default)
    {
        if (!GroupsAndPermissionsEnabled(configuration)) return NotFound();
        try
        {
            if (RequireSession().Info?.IsAdmin != true) return Forbid();
            return Json(new { success = true, group = await service.PrepareDeleteAsync(name, cancellationToken) });
        }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException) { return MauiControllerExceptionMapper.Map(ex); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteResourceGroup([FromServices] IResourceGroupManagementService service,
        [FromServices] IConfiguration configuration, [FromBody] ResourceGroupDeleteRequest? request, CancellationToken cancellationToken = default)
    {
        if (!GroupsAndPermissionsEnabled(configuration)) return NotFound();
        try
        {
            if (RequireSession().Info?.IsAdmin != true) return Forbid();
            if (request is null) return BadRequest(new { message = "Select the Resource Group to delete." });
            await service.DeleteAsync(request, cancellationToken);
            return Json(new { success = true });
        }
        catch (ResourceGroupSaveUnconfirmedException ex) { return Conflict(new { message = ex.Message, saveUnconfirmed = true }); }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException) { return MauiControllerExceptionMapper.Map(ex); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolveResourceGroupMembers([FromServices] IResourceGroupManagementService service,
        [FromServices] IConfiguration configuration, [FromBody] ResourceGroupMembersRequest? request, CancellationToken cancellationToken = default)
    {
        if (!GroupsAndPermissionsEnabled(configuration)) return NotFound();
        try
        {
            if (RequireSession().Info?.IsAdmin != true) return Forbid();
            if (request is null) return BadRequest(new { message = "Resource Group members are required." });
            return Json(new { success = true, items = await service.ResolveMembersAsync(request.Members, cancellationToken) });
        }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException) { return MauiControllerExceptionMapper.Map(ex); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveResourceGroup([FromServices] IResourceGroupManagementService service,
        [FromServices] IConfiguration configuration, [FromBody] ResourceGroupEditorSaveRequest? request, CancellationToken cancellationToken = default)
    {
        if (!GroupsAndPermissionsEnabled(configuration)) return NotFound();
        try
        {
            if (RequireSession().Info?.IsAdmin != true) return Forbid();
            if (request is null) return BadRequest(new { message = "Resource Group data is required." });
            return Json(new { success = true, editor = await service.SaveEditorAsync(request, cancellationToken) });
        }
        catch (ResourceGroupSaveUnconfirmedException ex) { return Conflict(new { message = ex.Message, saveUnconfirmed = true }); }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException) { return MauiControllerExceptionMapper.Map(ex); }
    }
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ResourceGroupUserGroups([FromServices] IResourceGroupPermissionsService service,
        [FromServices] IConfiguration configuration, string resourceGroupName, string type = "local", CancellationToken cancellationToken = default)
    {
        if (!GroupsAndPermissionsEnabled(configuration)) return NotFound();
        try
        {
            if (RequireSession().Info?.IsAdmin != true) return Forbid();
            return Json(new { success = true, setup = await service.PrepareUserGroupsAsync(resourceGroupName, type, cancellationToken) });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException) { return MauiControllerExceptionMapper.Map(ex); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddResourceGroupUserGroups([FromServices] IResourceGroupPermissionsService service,
        [FromServices] IConfiguration configuration, [FromBody] ResourceGroupUserGroupsRequest? request, CancellationToken cancellationToken = default)
    {
        if (!GroupsAndPermissionsEnabled(configuration)) return NotFound();
        try
        {
            if (RequireSession().Info?.IsAdmin != true) return Forbid();
            if (request is null) return BadRequest(new { message = "Select the User Groups to add." });
            return Json(new { success = true, result = await service.AddUserGroupsAsync(request, cancellationToken) });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException) { return MauiControllerExceptionMapper.Map(ex); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveResourceGroupUserGroup([FromServices] IResourceGroupPermissionsService service,
        [FromServices] IConfiguration configuration, [FromBody] ResourceGroupRemoveUserGroupRequest? request, CancellationToken cancellationToken = default)
    {
        if (!GroupsAndPermissionsEnabled(configuration)) return NotFound();
        try
        {
            if (RequireSession().Info?.IsAdmin != true) return Forbid();
            if (request is null) return BadRequest(new { message = "Select the User Group assignment to remove." });
            return Json(new { success = true, result = await service.RemoveUserGroupAsync(request, cancellationToken) });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException) { return MauiControllerExceptionMapper.Map(ex); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveResourceGroupPermissions([FromServices] IResourceGroupPermissionsService service,
        [FromServices] IConfiguration configuration, [FromBody] ResourceGroupPermissionsSaveRequest? request, CancellationToken cancellationToken = default)
    {
        if (!GroupsAndPermissionsEnabled(configuration)) return NotFound();
        try
        {
            if (RequireSession().Info?.IsAdmin != true) return Forbid();
            if (request is null) return BadRequest(new { message = "Permission changes are required." });
            return Json(new { success = true, result = await service.SavePermissionsAsync(request, cancellationToken) });
        }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (Exception ex) when (ex is MoveItApiException or InvalidOperationException or ArgumentException) { return MauiControllerExceptionMapper.Map(ex); }
    }
}
