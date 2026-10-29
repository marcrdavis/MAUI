// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Commits Factory Reset under the shared settings gate and requests graceful shutdown.

using System.Text;
using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.Authentication;
using MoveItAutomationUI.Services.Configuration;

namespace MoveItAutomationUI.Services.Lifecycle;

public sealed class MauiFactoryResetState
{
    private int _isResetting;
    public bool IsResetting => Volatile.Read(ref _isResetting) != 0;
    public bool TryBegin() => Interlocked.CompareExchange(ref _isResetting, 1, 0) == 0;
    public void Cancel() => Interlocked.Exchange(ref _isResetting, 0);
}

public sealed class MauiFactoryResetService
{
    public const string RequiredConfirmation = "RESET";

    private readonly IWebHostEnvironment _environment;
    private readonly IAppSettingsDocumentStore _documents;
    private readonly AppSettingsWriteCoordinator _writes;
    private readonly MauiFactoryResetState _state;
    private readonly IApplicationRestartService _restart;
    private readonly IMauiApplicationSessionService _sessions;
    private readonly ILogger<MauiFactoryResetService> _logger;

    public MauiFactoryResetService(
        IWebHostEnvironment environment,
        IAppSettingsDocumentStore documents,
        AppSettingsWriteCoordinator writes,
        MauiFactoryResetState state,
        IApplicationRestartService restart,
        IMauiApplicationSessionService sessions,
        ILogger<MauiFactoryResetService> logger)
    {
        _environment = environment;
        _documents = documents;
        _writes = writes;
        _state = state;
        _restart = restart;
        _sessions = sessions;
        _logger = logger;
    }

    public async Task ResetAsync(HttpContext context, string? confirmation, CancellationToken cancellationToken)
    {
        if (!string.Equals(confirmation, RequiredConfirmation, StringComparison.Ordinal))
            throw new ArgumentException("Enter RESET exactly to confirm the factory reset.", nameof(confirmation));
        if (!_state.TryBegin())
            throw new InvalidOperationException("A factory reset is already in progress.");

        var gateHeld = false;
        var markerCreated = false;
        JsonObject? previous = null;
        var marker = MauiFactoryResetRecovery.MarkerPath(_environment.ContentRootPath);
        try
        {
            // Never start a destructive change if another lifecycle operation is stopping the host.
            if (_restart.IsRestartScheduled)
                throw new InvalidOperationException("An application restart is already scheduled. Try again after the restart.");

            await _writes.Gate.WaitAsync(cancellationToken);
            gateHeld = true;
            if (_restart.IsRestartScheduled)
                throw new InvalidOperationException("An application restart is already scheduled. Try again after the restart.");

            previous = await _documents.ReadLiveAsync(cancellationToken);
            var data = MauiFactoryResetRecovery.DataDirectory(_environment.ContentRootPath);
            // Verify filesystem links before writing the reset marker or changing configuration.
            // The same validation runs again at startup before deletion.
            MauiFactoryResetRecovery.CheckDeletionTree(data);
            if (File.Exists(marker))
                throw new InvalidOperationException("A factory reset is already pending; no changes were made.");

            using (var markerFile = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                markerCreated = true; // Includes a partial write, which rollback must remove.
                var bytes = new UTF8Encoding(false).GetBytes(MauiFactoryResetRecovery.MarkerContents);
                markerFile.Write(bytes);
                markerFile.Flush(flushToDisk: true);
            }

            // Existing document store implements backup, validation and in-place write for
            // restrictive IIS ACLs. Do not activate this new {} snapshot in the live process.
            await _documents.WriteLiveAsync(new JsonObject(), cancellationToken: CancellationToken.None, publishRuntime: false);

            // Explicit local logout; prior auth and antiforgery cookies are also invalidated
            // once Data Protection keys are removed during the next application startup.
            await _sessions.SignOutAsync(context);
            context.Response.Cookies.Delete(
                MauiAuthenticationDefaults.AntiforgeryCookieName,
                new CookieOptions { Path = context.Request.PathBase.HasValue ? context.Request.PathBase.Value : "/" });

            // ScheduleRestart is the irreversible hand-off to the background host shutdown.
            // Log beforehand: nothing that can throw should run after shutdown is queued,
            // otherwise the catch block could mistakenly attempt rollback during shutdown.
            _logger.LogWarning("MAUI Factory Reset prepared by {User}. Requesting shutdown; Data will be purged before the next startup.",
                context.User.Identity?.Name ?? "unknown");
            if (!_restart.ScheduleRestart("MAUI Factory Reset"))
                throw new InvalidOperationException("MAUI could not schedule shutdown; factory reset has been rolled back.");

            // Intentionally retain the shared write gate until host shutdown. Otherwise an
            // in-flight Settings update could recreate the configuration after reset commit.
            gateHeld = false;
        }
        catch
        {
            // Never discard the recovery marker unless the prior configuration is
            // verified on disk. Even if WriteLiveAsync throws, an interrupted disk
            // write could have left {} (or a damaged document) behind.
            var restored = false;
            if (previous is not null)
            {
                try
                {
                    var current = await _documents.ReadLiveAsync(CancellationToken.None);
                    restored = JsonNode.DeepEquals(previous, current);
                    if (!restored)
                    {
                        await _documents.WriteLiveAsync(previous, cancellationToken: CancellationToken.None, publishRuntime: false);
                        var persisted = await _documents.ReadLiveAsync(CancellationToken.None);
                        restored = JsonNode.DeepEquals(previous, persisted);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogCritical(ex, "Factory Reset rollback could not verify/restore appsettings.json. Pending marker retained for recovery.");
                }
            }
            if (markerCreated && restored)
            {
                try { File.Delete(marker); }
                catch (IOException ex) { _logger.LogError(ex, "Unable to remove prepared Factory Reset marker."); }
                catch (UnauthorizedAccessException ex) { _logger.LogError(ex, "Unable to remove prepared Factory Reset marker."); }
            }
            _state.Cancel();
            throw;
        }
        finally
        {
            if (gateHeld) _writes.Gate.Release();
        }
    }
}

public sealed class MauiFactoryResetAccessMiddleware
{
    private readonly RequestDelegate _next;
    public MauiFactoryResetAccessMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context, MauiFactoryResetState state)
    {
        if (!state.IsResetting) return _next(context);
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers["Retry-After"] = "5";
        return context.Response.WriteAsync("MAUI is resetting and restarting.");
    }
}
