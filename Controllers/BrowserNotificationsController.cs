// Records client-visible operational messages in the existing MAUI application log.
// This is client-reported feedback, not an authorization or native MOVEit audit record.
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MoveItAutomationUI.Services.Logging;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Controllers;

[Authorize]
public sealed class BrowserNotificationsController(MauiApplicationLogChannel channel, IMoveItSessionService sessions) : Controller
{
    public sealed record Notification(string? Message, string? Kind, string? Page, string? Origin);

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(32768)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Record([FromBody] Notification? request)
    {
        var session = sessions.Get();
        if (session is null) return Unauthorized();
        if (request is null || !ModelState.IsValid || string.IsNullOrWhiteSpace(request.Message)) return BadRequest();
        var kind = request.Kind?.ToLowerInvariant() ?? "info";
        var level = kind == "error" ? LogLevel.Error : kind == "warning" ? LogLevel.Warning : LogLevel.Information;
        var page = request.Page ?? string.Empty;
        if (!page.StartsWith('/') || page.StartsWith("//", StringComparison.Ordinal)) page = string.Empty;
        page = page.Split(new[] { '?', '#' })[0];
        if (page.Length > 512) page = page[..512];
        // Use the existing channel directly so Info notices are captured even if the ordinary
        // ILogger minimum is Warning. Identity/environment come from the server session only.
        channel.TryWrite(new MauiApplicationLogRecord(DateTime.UtcNow, level,
            "MoveItAutomationUI.BrowserNotifications", new EventId(1001, "BrowserNotification"),
            session.Username, session.GetEnvironmentKey(), page,
            Activity.Current?.TraceId.ToString() ?? HttpContext.TraceIdentifier,
            Sanitize(request.Message), string.Empty,
            JsonSerializer.Serialize(new { ClientReported = true, Origin = request.Origin == "page" ? "page" : "modal" })));
        return NoContent();
    }

    internal static string Sanitize(string value)
    {
        value = value.Length > 4000 ? value[..4000] : value;
        value = Regex.Replace(value,
            """((?:password|passwd|pwd|client_?secret|secret|access_?token|refresh_?token|token|authorization|api_?key|access_?key)"?\s*[:=]\s*)(?:"[^"]*"|'[^']*'|(?:Bearer|Basic)\s+[A-Za-z0-9._~+/=\-]+|[^\s,;}]+)""",
            "$1[redacted]", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
        value = Regex.Replace(value, @"\bBearer\s+[A-Za-z0-9._~+/=\-]+", "Bearer [redacted]",
            RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
        return string.Concat(value.Select(character => char.IsControl(character) ? ' ' : character)).Trim();
    }
}
