// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using Microsoft.Extensions.Caching.Memory;
using MoveItAutomationUI.Services.Concurrency;

namespace MoveItAutomationUI.Services.MoveIt;

/// <summary>
/// Initial-navigation cache result. A cached snapshot can be rendered immediately while RefreshRecommended
/// tells the browser to reconcile it through the grid's established refresh path.
/// </summary>
public sealed record InitialCacheSnapshot<TSnapshot>(TSnapshot Snapshot, bool RefreshRecommended);

/// <summary>
/// Shared cache-scope and refresh helpers used by MOVEit inventory services.
/// </summary>
internal static class MoveItServiceCache
{
    private static readonly KeyedAsyncLock RefreshLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CacheResidence = TimeSpan.FromHours(12);

    // Builds a cache key scoped to the authenticated MOVEit server and username.
    public static string UserScopedKey(string prefix, IMoveItSessionService sessions)
    {
        var session = sessions.Get() ?? throw new MoveItAuthenticationException("MOVEit session is not authenticated.");
        return $"{prefix}{session.ServerKey}:{session.Username}";
    }

    // Evaluates the standard cache-refresh setting; zero disables age-based refresh while the entry remains resident in memory.
    public static bool IsFresh(DateTimeOffset refreshedUtc, int refreshSeconds)
    {
        var seconds = Math.Max(0, refreshSeconds);
        return seconds == 0 || DateTimeOffset.UtcNow - refreshedUtc < TimeSpan.FromSeconds(seconds);
    }

    // Returns any existing cache immediately for navigation, even when stale. When no cache exists,
    // the supplied normal resolver populates one synchronously because there is nothing useful to render yet.
    public static async Task<InitialCacheSnapshot<TSnapshot>> ResolveInitialAsync<TEntry, TSnapshot>(
        IMemoryCache cache,
        string cacheKey,
        int refreshSeconds,
        Func<TEntry, DateTimeOffset> refreshedUtc,
        Func<TEntry, TSnapshot> snapshotFactory,
        Func<CancellationToken, Task<TSnapshot>> populateMissing,
        CancellationToken cancellationToken)
        where TEntry : class
    {
        if (cache.TryGetValue<TEntry>(cacheKey, out var cached) && cached is not null)
        {
            return new InitialCacheSnapshot<TSnapshot>(
                snapshotFactory(cached),
                RefreshRecommended: !IsFresh(refreshedUtc(cached), refreshSeconds));
        }

        var populated = await populateMissing(cancellationToken);
        return new InitialCacheSnapshot<TSnapshot>(populated, RefreshRecommended: false);
    }

    // Resolves one inventory through the common MAUI cache contract. Normal callers reuse a fresh cache;
    // stale/missing caches refresh from MOVEit; forceRefresh bypasses age while still coalescing concurrent requests.
    public static async Task<TEntry> ResolveAsync<TEntry>(
        IMemoryCache cache,
        string cacheKey,
        int refreshSeconds,
        bool forceRefresh,
        Func<TEntry, DateTimeOffset> refreshedUtc,
        Func<CancellationToken, Task<TEntry>> refreshFactory,
        CancellationToken cancellationToken)
        where TEntry : class
    {
        if (!forceRefresh
            && cache.TryGetValue<TEntry>(cacheKey, out var cached)
            && cached is not null
            && IsFresh(refreshedUtc(cached), refreshSeconds))
        {
            return cached;
        }

        var refreshRequestedUtc = DateTimeOffset.UtcNow;
        using var refreshLease = await RefreshLocks.AcquireAsync(cacheKey, cancellationToken);

        if (cache.TryGetValue<TEntry>(cacheKey, out cached) && cached is not null)
        {
            if (!forceRefresh && IsFresh(refreshedUtc(cached), refreshSeconds)) return cached;
            if (forceRefresh && refreshedUtc(cached) >= refreshRequestedUtc) return cached;
        }

        var refreshed = await refreshFactory(cancellationToken);
        cache.Set(cacheKey, refreshed, new MemoryCacheEntryOptions { SlidingExpiration = CacheResidence });
        return refreshed;
    }
}
