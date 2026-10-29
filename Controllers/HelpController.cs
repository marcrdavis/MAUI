// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Controllers;

[Authorize]
public sealed class HelpController : Controller
{
    private readonly UiOptions _ui;
    private readonly IMoveItSessionService _sessionService;

    // Purpose: Initializes help controller with its required dependencies.
    public HelpController(IOptionsSnapshot<UiOptions> uiOptions, IMoveItSessionService sessionService)
    {
        _ui = uiOptions.Value;
        _sessionService = sessionService;
    }

    // Displays application help and project information.
    public IActionResult Index() => View();

    // Displays MAUI and current MOVEit Automation build/license/session information captured at login.
    public IActionResult About()
    {
        var session = _sessionService.Get();
        var model = new AboutViewModel
        {
            ApplicationName = _ui.ApplicationName,
            ApplicationLogo = _ui.ApplicationLogo,
            ApplicationVersion = _ui.Version,
            ServerDisplayName = session?.ServerDisplayName ?? string.Empty,
            WebAdminUrl = session?.WebAdminUrl ?? string.Empty,
            MoveIt = session?.Info
        };
        return View(model);
    }
}
