// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using Microsoft.AspNetCore.Authorization;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Services.Security;

public sealed class MauiAdminRequirement : IAuthorizationRequirement
{
    public MauiAdminRequirement(bool allowMoveItAdmin = false) => AllowMoveItAdmin = allowMoveItAdmin;
    public bool AllowMoveItAdmin { get; }
}

public sealed class MauiAdminAuthorizationHandler : AuthorizationHandler<MauiAdminRequirement>
{
    private readonly IMauiSecurityService _securityService;
    private readonly IMoveItSessionService _sessionService;

    // Purpose: Initializes MAUI admin authorization handler with its required dependencies.
    public MauiAdminAuthorizationHandler(IMauiSecurityService securityService, IMoveItSessionService sessionService)
    {
        _securityService = securityService;
        _sessionService = sessionService;
    }

    // Purpose: Evaluates the MAUI Admin authorization requirement.
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, MauiAdminRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && _sessionService.IsAuthenticated
            && (_securityService.IsAdmin(context.User.Identity.Name)
                || requirement.AllowMoveItAdmin && _sessionService.Get()?.Info?.IsAdmin == true))
        {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}

