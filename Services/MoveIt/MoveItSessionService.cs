// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.Authentication;

namespace MoveItAutomationUI.Services.MoveIt;

public interface IMoveItSessionService
{
    bool IsAuthenticated { get; }
    // Purpose: Retrieve the current server-side MOVEit session state.
    MoveItSessionState? Get();
    // Purpose: Create a server-side MOVEit session and return its opaque identifier.
    string Create(MoveItSessionState state);
    // Purpose: Update the current server-side MOVEit session state.
    void Update(MoveItSessionState state);
    // Returns whether a browser principal still references a live server-side MOVEit session.
    bool HasLiveBrowserSession(ClaimsPrincipal principal);
    // Atomically merges refreshed token values into the latest server-side session state.
    void UpdateTokens(string accessToken, string refreshToken, DateTimeOffset accessTokenExpiresUtc);
    // Atomically dismisses the shared System Message for the current session.
    bool DismissSystemMessage();
    // Records deliberate browser activity for the current authenticated session.
    bool RecordUserActivity();
    // Purpose: Clear the current server-side MOVEit session state.
    void Clear();
    // Purpose: Temporarily supplies an isolated MOVEit session to background work without creating a browser session.
    IDisposable PushBackgroundSession(MoveItSessionState state);
}

public sealed class MoveItSessionState
{
    // Used only to merge cleanup token renewal into an existing live browser session.
    // It never permits recreation of an expired or logged-out session.
    public string CleanupOwnerSessionId { get; set; } = string.Empty;
    public string ServerKey { get; set; } = string.Empty;
    public string EnvironmentKey { get; set; } = string.Empty;
    public string ServerDisplayName { get; set; } = string.Empty;
    public string WebAdminUrl { get; set; } = string.Empty;
    public string ServerHost { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;

    // Keeps the password only in server memory for the active MAUI session so COM/native-editor authentication
    // and REST token recovery can use it. It is never written to a browser cookie or appsettings.json.
    public string Password { get; set; } = string.Empty;

    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTimeOffset AccessTokenExpiresUtc { get; set; }

    // Tracks the last deliberate MAUI browser activity independently of background grid/status refresh requests.
    // The server checks this timestamp whenever browser MOVEit session state is read so background traffic cannot
    // extend an otherwise idle session indefinitely.
    public DateTimeOffset LastUserActivityUtc { get; set; }

    // Tracks dismissal of the shared System Message only for this authenticated MAUI session.
    public bool SystemMessageDismissed { get; set; }

    // Captures the native /api/v0/info response once at successful login so authorization, About,
    // licensing, diagnostics, and carefully-scoped server-time handling can share one authoritative snapshot.
    public MoveItInfo? Info { get; set; }

    // Returns the normalized metadata scope and safely falls back to ServerKey for sessions created before EnvironmentKey existed.
    public string GetEnvironmentKey()
        => MoveItEnvironmentKey.Normalize(string.IsNullOrWhiteSpace(EnvironmentKey) ? ServerKey : EnvironmentKey);
}

public sealed class MoveItSessionService : IMoveItSessionService
{
    private static readonly AsyncLocal<MoveItSessionState?> BackgroundSession = new();
    private static readonly AsyncLocal<string?> BackgroundTokenBaseline = new();
    public const string SessionIdClaimType = "MoveItAutomationUI.SessionId";
    private const string CacheKeyPrefix = "MoveItAutomationUI:MoveItSession:";
    private static readonly ConcurrentDictionary<string, object> SessionMutationLocks = new(StringComparer.Ordinal);

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IDistributedCache _cache;
    private readonly ConfigurationStartupState _configuration;
    private readonly IShortCheckoutRecovery? _checkoutRecovery;

    // Stores MOVEit credentials/tokens server-side. The browser receives only an opaque session identifier in the
    // encrypted ASP.NET authentication cookie; MOVEit credentials never leave the server-side cache.
    // Initializes server-side MOVEit session storage and browser-session access.
    public MoveItSessionService(
        IHttpContextAccessor httpContextAccessor,
        IDistributedCache cache,
        ConfigurationStartupState configuration, IShortCheckoutRecovery? checkoutRecovery = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _cache = cache;
        _configuration = configuration;
        _checkoutRecovery = checkoutRecovery;
    }

    public bool IsAuthenticated =>
        BackgroundSession.Value is { AccessToken.Length: > 0 }
        || (_httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true
            && Get() is { AccessToken.Length: > 0 });

    // Returns the MOVEit state associated with the authenticated browser session.
    public MoveItSessionState? Get()
    {
        if (BackgroundSession.Value is { } backgroundState)
        {
            RefreshCleanupTokens(backgroundState);
            return backgroundState;
        }
        var principal = _httpContextAccessor.HttpContext?.User;
        return principal is null ? null : GetLiveBrowserState(principal);
    }

    public bool HasLiveBrowserSession(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return GetLiveBrowserState(principal) is { AccessToken.Length: > 0 };
    }

    private MoveItSessionState? GetLiveBrowserState(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true) return null;
        var sessionId = principal.FindFirst(SessionIdClaimType)?.Value ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sessionId)) return null;
        return ReadLiveState(sessionId);
    }

    private MoveItSessionState? ReadLiveState(string sessionId)
    {
        var json = _cache.GetString(BuildCacheKey(sessionId));
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var state = JsonSerializer.Deserialize<MoveItSessionState>(json);
            if (state is null) return null;

            var idleLifetime = MauiAuthenticationDefaults.GetSessionLifetime(_configuration.IdleSessionTimeoutMinutes);
            if (state.LastUserActivityUtc == default || DateTimeOffset.UtcNow - state.LastUserActivityUtc >= idleLifetime)
            {
                _cache.Remove(BuildCacheKey(sessionId));
                return null;
            }

            return state;
        }
        catch
        {
            _cache.Remove(BuildCacheKey(sessionId));
            return null;
        }
    }

    // Creates a new server-side MOVEit state record and return the opaque ID stored in the authentication cookie.
    public string Create(MoveItSessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var sessionId = Guid.NewGuid().ToString("N");
        _cache.SetString(BuildCacheKey(sessionId), JsonSerializer.Serialize(state), BuildCacheOptions());
        return sessionId;
    }

    // Replaces broad session state only after merging concurrency-sensitive fields from the latest cached copy.
    // New code should prefer the narrow atomic mutation methods below.
    public void Update(MoveItSessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (BackgroundSession.Value is not null)
        {
            BackgroundSession.Value = state;
            return;
        }

        var sessionId = GetCurrentSessionId();
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new InvalidOperationException("No authenticated application session is available to update MOVEit state.");

        var gate = SessionMutationLocks.GetOrAdd(sessionId, static _ => new object());
        lock (gate)
        {
            var latest = ReadLiveState(sessionId)
                ?? throw new InvalidOperationException("No authenticated application session is available to update MOVEit state.");

            if (latest.LastUserActivityUtc > state.LastUserActivityUtc)
                state.LastUserActivityUtc = latest.LastUserActivityUtc;
            if (latest.SystemMessageDismissed)
                state.SystemMessageDismissed = true;
            if (latest.AccessTokenExpiresUtc > state.AccessTokenExpiresUtc)
            {
                state.AccessToken = latest.AccessToken;
                state.RefreshToken = latest.RefreshToken;
                state.AccessTokenExpiresUtc = latest.AccessTokenExpiresUtc;
            }

            _cache.SetString(BuildCacheKey(sessionId), JsonSerializer.Serialize(state), BuildCacheOptions());
        }
    }

    // Advances the authoritative idle timestamp only for deliberate browser activity.
    // The mutation is atomic against token refresh and other session updates.
    public bool RecordUserActivity()
    {
        if (BackgroundSession.Value is not null) return true;
        return MutateCurrentState(state => state.LastUserActivityUtc = DateTimeOffset.UtcNow);
    }

    public void UpdateTokens(string accessToken, string refreshToken, DateTimeOffset accessTokenExpiresUtc)
    {
        if (BackgroundSession.Value is { } background)
        {
            background.AccessToken = accessToken ?? string.Empty;
            background.RefreshToken = refreshToken ?? string.Empty;
            background.AccessTokenExpiresUtc = accessTokenExpiresUtc;
            BackgroundSession.Value = background;
            if (!string.IsNullOrEmpty(background.CleanupOwnerSessionId))
                MergeCleanupTokens(background);
            return;
        }

        if (!MutateCurrentState(state =>
            {
                state.AccessToken = accessToken ?? string.Empty;
                state.RefreshToken = refreshToken ?? string.Empty;
                state.AccessTokenExpiresUtc = accessTokenExpiresUtc;
            }))
        {
            throw new InvalidOperationException("No authenticated application session is available to update MOVEit tokens.");
        }
    }

    // A checkout operation may outlive a browser request that rotated its tokens. Read
    // only tokens from that same live login; never restore a password, rights or idle time.
    private void RefreshCleanupTokens(MoveItSessionState background)
    {
        var id = background.CleanupOwnerSessionId;
        if (string.IsNullOrEmpty(id)) return;
        var gate = SessionMutationLocks.GetOrAdd(id, static _ => new object());
        lock (gate)
        {
            var latest = ReadLiveState(id);
            if (latest is null || latest.Username != background.Username || latest.WebAdminUrl != background.WebAdminUrl
                || latest.ServerKey != background.ServerKey || latest.EnvironmentKey != background.EnvironmentKey) return;
            background.AccessToken = latest.AccessToken;
            background.RefreshToken = latest.RefreshToken;
            background.AccessTokenExpiresUtc = latest.AccessTokenExpiresUtc;
            BackgroundTokenBaseline.Value = latest.AccessToken;
        }
    }

    private void MergeCleanupTokens(MoveItSessionState background)
    {
        var id = background.CleanupOwnerSessionId;
        var gate = SessionMutationLocks.GetOrAdd(id, static _ => new object());
        lock (gate)
        {
            var latest = ReadLiveState(id);
            if (latest is null || latest.Username != background.Username || latest.WebAdminUrl != background.WebAdminUrl
                || latest.ServerKey != background.ServerKey || latest.EnvironmentKey != background.EnvironmentKey) return;
            // Expiry duration is not a token revision. A forced refresh can legitimately
            // return a shorter lifetime; only replace the token this operation observed.
            if (latest.AccessToken != BackgroundTokenBaseline.Value && latest.AccessToken != background.AccessToken) return;
            latest.AccessToken = background.AccessToken;
            latest.RefreshToken = background.RefreshToken;
            latest.AccessTokenExpiresUtc = background.AccessTokenExpiresUtc;
            _cache.SetString(BuildCacheKey(id), JsonSerializer.Serialize(latest), BuildCacheOptions());
            BackgroundTokenBaseline.Value = background.AccessToken;
        }
    }

    public bool DismissSystemMessage()
    {
        if (BackgroundSession.Value is { } background)
        {
            background.SystemMessageDismissed = true;
            BackgroundSession.Value = background;
            return true;
        }
        return MutateCurrentState(state => state.SystemMessageDismissed = true);
    }


    private bool MutateCurrentState(Action<MoveItSessionState> mutation)
    {
        var sessionId = GetCurrentSessionId();
        if (string.IsNullOrWhiteSpace(sessionId)) return false;
        var gate = SessionMutationLocks.GetOrAdd(sessionId, static _ => new object());
        lock (gate)
        {
            var state = ReadLiveState(sessionId);
            if (state is null) return false;
            mutation(state);
            _cache.SetString(BuildCacheKey(sessionId), JsonSerializer.Serialize(state), BuildCacheOptions());
            return true;
        }
    }

    // Removes MOVEit credentials/tokens for the currently authenticated browser session.
    public void Clear()
    {
        // Disposing a Data Access connection must clear only its isolated session,
        // even while an authenticated browser request is present in IHttpContextAccessor.
        if (BackgroundSession.Value is { } background)
        {
            background.Password = ""; background.AccessToken = ""; background.RefreshToken = "";
            BackgroundSession.Value = null; BackgroundTokenBaseline.Value = null;
            return;
        }
        var sessionId = GetCurrentSessionId();
        if (string.IsNullOrWhiteSpace(sessionId)) return;

        // Serialize logout/session removal with token, activity, and message mutations so a
        // concurrent writer cannot recreate a session after it has been cleared.
        var gate = SessionMutationLocks.GetOrAdd(sessionId, static _ => new object());
        lock (gate)
        {
            _cache.Remove(BuildCacheKey(sessionId));
            SessionMutationLocks.TryRemove(sessionId, out _);
        }
    }

    // Temporarily binds an unattended authenticated state to the current async execution flow only.
    public IDisposable PushBackgroundSession(MoveItSessionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var previous = BackgroundSession.Value;
        var previousToken = BackgroundTokenBaseline.Value;
        BackgroundSession.Value = state;
        BackgroundTokenBaseline.Value = state.AccessToken;
        return new BackgroundSessionScope(previous, previousToken, _checkoutRecovery is null ? null : ObjectCheckout.ForRecovery(_checkoutRecovery, state));
    }

    private sealed class BackgroundSessionScope : IDisposable
    {
        private readonly MoveItSessionState? _previous;
        private readonly string? _previousToken;
        private bool _disposed;
        private readonly IDisposable? _checkoutScope;

        public BackgroundSessionScope(MoveItSessionState? previous, string? previousToken, IDisposable? checkoutScope)
        { _previous = previous; _previousToken = previousToken; _checkoutScope = checkoutScope; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            BackgroundSession.Value = _previous;
            BackgroundTokenBaseline.Value = _previousToken;
            _checkoutScope?.Dispose();
        }
    }

    // Uses the same configured sliding idle lifetime as the browser authentication cookie.
    private DistributedCacheEntryOptions BuildCacheOptions() => new()
    {
        SlidingExpiration = MauiAuthenticationDefaults.GetSessionLifetime(_configuration.IdleSessionTimeoutMinutes)
    };

    // Reads the opaque session identifier from the authenticated principal created by the MAUI application-session service.
    private string GetCurrentSessionId()
        => _httpContextAccessor.HttpContext?.User.FindFirst(SessionIdClaimType)?.Value ?? string.Empty;

    // Purpose: Build cache key.
    private static string BuildCacheKey(string sessionId) => CacheKeyPrefix + sessionId;
}
