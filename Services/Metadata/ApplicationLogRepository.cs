// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Reads MAUI application ILogger events stored in the metadata database.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.Metadata;

public interface IApplicationLogRepository
{
    Task<IReadOnlyList<ApplicationLogEntity>> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class ApplicationLogRepository : IApplicationLogRepository
{
    private readonly MetadataDbContext _db;
    private readonly ApplicationLogOptions _options;

    public ApplicationLogRepository(MetadataDbContext db, IOptionsSnapshot<ApplicationLogOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    // Returns retained application-log rows newest first. The cutoff is also applied here so a
    // newly-shortened retention setting takes effect in the grid before the hourly purge runs.
    public async Task<IReadOnlyList<ApplicationLogEntity>> GetAsync(CancellationToken cancellationToken = default)
    {
        var retentionDays = Math.Clamp(
            _options.RetentionDays.Value,
            ApplicationLogOptions.MinimumRetentionDays,
            ApplicationLogOptions.MaximumRetentionDays);
        var cutoffUtc = DateTime.UtcNow.AddDays(-retentionDays);

        return await _db.ApplicationLogs
            .AsNoTracking()
            .Where(item => item.TimestampUtc >= cutoffUtc)
            .OrderByDescending(item => item.TimestampUtc)
            .ThenByDescending(item => item.Id)
            .ToListAsync(cancellationToken);
    }
}
