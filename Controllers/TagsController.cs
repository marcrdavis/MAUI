// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Central Tag registry API used by Configuration and metadata editors.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Controllers;

[Authorize]
public sealed class TagsController : Controller
{
    private readonly ITagRepository _tags;
    private readonly IMoveItSessionService _sessions;
    private readonly ILogger<TagsController> _logger;

    public TagsController(ITagRepository tags, IMoveItSessionService sessions, ILogger<TagsController> logger)
    {
        _tags = tags;
        _sessions = sessions;
        _logger = logger;
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> List([FromQuery] bool includeUsage = false, CancellationToken cancellationToken = default)
    {
        var session = _sessions.Get();
        if (session is null) return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        var tags = await _tags.GetTagsAsync(session.GetEnvironmentKey(), includeUsage, cancellationToken);
        return Json(new { success = true, tags = tags.Select(ToClient) });
    }

    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromBody] TagSaveRequest? request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequest(new { message = "Tag request is required." });
        var session = _sessions.Get();
        if (session is null) return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        try
        {
            var created = await _tags.CreateAsync(session.GetEnvironmentKey(), request.Name, request.Color, session.Username, cancellationToken);
            _logger.LogInformation("MAUI Tag {TagName} was created by {Username}.", created.Name, session.Username);
            return Json(new { success = true, tag = ToClient(created) });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update([FromBody] TagSaveRequest? request, CancellationToken cancellationToken)
    {
        if (request is null) return BadRequest(new { message = "Tag request is required." });
        var session = _sessions.Get();
        if (session is null) return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        try
        {
            var updated = await _tags.UpdateAsync(session.GetEnvironmentKey(), request.Id, request.Name, request.Color, session.Username, cancellationToken);
            _logger.LogInformation("MAUI Tag {TagName} was updated by {Username}.", updated.Name, session.Username);
            return Json(new { success = true, tag = ToClient(updated) });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    [Authorize(Policy = MauiSecurityPolicies.Admin)]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete([FromBody] TagDeleteRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Id)) return BadRequest(new { message = "Tag ID is required." });
        var session = _sessions.Get();
        if (session is null) return Unauthorized(new { message = "MOVEit session expired. Please sign in again." });
        try
        {
            await _tags.DeleteAsync(session.GetEnvironmentKey(), request.Id, cancellationToken);
            _logger.LogInformation("MAUI Tag {TagId} was deleted by {Username}.", request.Id, session.Username);
            return Json(new { success = true });
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
    }

    private static object ToClient(TagDefinitionRecord tag) => new
    {
        tag.Id,
        tag.Name,
        tag.Color,
        tag.UseCount,
        tag.CreatedUtc,
        tag.CreatedBy,
        tag.ModifiedUtc,
        tag.ModifiedBy
    };
}

public sealed class TagSaveRequest
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
}

public sealed class TagDeleteRequest
{
    public string Id { get; set; } = string.Empty;
}
