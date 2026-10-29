// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using Microsoft.EntityFrameworkCore;

namespace MoveItAutomationUI.Services.Metadata;

public interface IRecycleBinRepository
{
    // Purpose: Adds range to the current MAUI state.
    Task AddRangeAsync(IEnumerable<RecycleBinItemEntity> items, CancellationToken cancellationToken = default);
    // Purpose: Returns the visible Recycle Bin records for the current environment.
    Task<IReadOnlyList<RecycleBinItemEntity>> GetVisibleAsync(string environmentKey, string username, bool isAdmin, CancellationToken cancellationToken = default);
    // Purpose: Returns by ids.
    Task<IReadOnlyList<RecycleBinItemEntity>> GetByIdsAsync(string environmentKey, IEnumerable<string> ids, string username, bool isAdmin, CancellationToken cancellationToken = default);
    // Purpose: Deletes by ids after applying the required guardrails.
    Task DeleteByIdsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default);
    // Purpose: Permanently removes all selected Recycle Bin records.
    Task<int> EmptyAsync(string environmentKey, CancellationToken cancellationToken = default);
    // Purpose: Purges older than according to the configured retention rules.
    Task<int> PurgeOlderThanAsync(string environmentKey, DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default);
}

public sealed class RecycleBinRepository : IRecycleBinRepository
{
    private readonly MetadataDbContext _db;
    // Purpose: Initializes recycle bin repository with its required dependencies.
    public RecycleBinRepository(MetadataDbContext db) => _db = db;

    // Purpose: Adds range to the current MAUI state.
    public async Task AddRangeAsync(IEnumerable<RecycleBinItemEntity> items, CancellationToken cancellationToken = default)
    {
        var values = items.ToList();
        if (values.Count == 0) return;
        await _db.RecycleBinItems.AddRangeAsync(values, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    // Purpose: Returns the visible Recycle Bin records for the current environment.
    public async Task<IReadOnlyList<RecycleBinItemEntity>> GetVisibleAsync(string environmentKey, string username, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var env = MoveItAutomationUI.Models.MoveItEnvironmentKey.Normalize(environmentKey);
        var userKey = NormalizeUser(username);
        var query = _db.RecycleBinItems.AsNoTracking().Where(item => item.EnvironmentKey == env);
        if (!isAdmin) query = query.Where(item => item.DeletedByKey == userKey);

        // SQLite cannot translate ordering/comparison operations over DateTimeOffset. Keep the
        // environment/user filters in SQL, then sort the small, retention-limited Recycle Bin result set in memory.
        var items = await query.ToListAsync(cancellationToken);
        return items.OrderByDescending(item => item.DeletedUtc).ToList();
    }

    // Purpose: Returns by ids.
    public async Task<IReadOnlyList<RecycleBinItemEntity>> GetByIdsAsync(string environmentKey, IEnumerable<string> ids, string username, bool isAdmin, CancellationToken cancellationToken = default)
    {
        var env = MoveItAutomationUI.Models.MoveItEnvironmentKey.Normalize(environmentKey);
        var selected = ids.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (selected.Count == 0) return Array.Empty<RecycleBinItemEntity>();
        var userKey = NormalizeUser(username);
        var query = _db.RecycleBinItems.AsNoTracking().Where(item => item.EnvironmentKey == env && selected.Contains(item.Id));
        if (!isAdmin) query = query.Where(item => item.DeletedByKey == userKey);
        return await query.ToListAsync(cancellationToken);
    }

    // Purpose: Deletes by ids after applying the required guardrails.
    public async Task DeleteByIdsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        var selected = ids.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (selected.Count == 0) return;
        var items = await _db.RecycleBinItems.Where(item => selected.Contains(item.Id)).ToListAsync(cancellationToken);
        if (items.Count == 0) return;
        _db.RecycleBinItems.RemoveRange(items);
        await _db.SaveChangesAsync(cancellationToken);
    }

    // Purpose: Permanently removes all selected Recycle Bin records.
    public async Task<int> EmptyAsync(string environmentKey, CancellationToken cancellationToken = default)
    {
        var env = MoveItAutomationUI.Models.MoveItEnvironmentKey.Normalize(environmentKey);
        var items = await _db.RecycleBinItems.Where(item => item.EnvironmentKey == env).ToListAsync(cancellationToken);
        if (items.Count == 0) return 0;
        _db.RecycleBinItems.RemoveRange(items);
        await _db.SaveChangesAsync(cancellationToken);
        return items.Count;
    }

    // Purpose: Purges older than according to the configured retention rules.
    public async Task<int> PurgeOlderThanAsync(string environmentKey, DateTimeOffset cutoffUtc, CancellationToken cancellationToken = default)
    {
        var env = MoveItAutomationUI.Models.MoveItEnvironmentKey.Normalize(environmentKey);

        // SQLite cannot translate DateTimeOffset comparisons. Filter by environment in SQL and
        // apply the retention cutoff in memory. The Recycle Bin is intentionally short-lived/small.
        var environmentItems = await _db.RecycleBinItems.Where(item => item.EnvironmentKey == env).ToListAsync(cancellationToken);
        var items = environmentItems.Where(item => item.DeletedUtc < cutoffUtc).ToList();
        if (items.Count == 0) return 0;
        _db.RecycleBinItems.RemoveRange(items);
        await _db.SaveChangesAsync(cancellationToken);
        return items.Count;
    }

    // Purpose: Normalizes user.
    private static string NormalizeUser(string? username) => (username ?? string.Empty).Trim().ToUpperInvariant();
}
