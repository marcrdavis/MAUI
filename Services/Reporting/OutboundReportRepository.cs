// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Stores scheduled outbound report definitions and delivery history in the MAUI metadata database.
// Interactive definition queries/mutations are owner-scoped for normal MAUI Users and unrestricted for Admins;
// background Reports System Job queries intentionally remain global so every due definition can execute.

using System.Net.Mail;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Services.Reporting;

public interface IOutboundReportRepository
{
    Task<OutboundReportEntity> CreateAsync(CreateOutboundReportRequest request, ReportScheduleDefinition schedule, DateTimeOffset nextRunUtc, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OutboundReportSummary>> GetAllAsync(string username, bool includeAllOwners, CancellationToken cancellationToken = default);
    Task<OutboundReportEntity?> GetAsync(string id, string username, bool includeAllOwners, CancellationToken cancellationToken = default);
    Task<OutboundReportEntity> UpdateAsync(UpdateOutboundReportRequest request, ReportScheduleDefinition schedule, DateTimeOffset? nextRunUtc, string username, bool includeAllOwners, CancellationToken cancellationToken = default);
    Task<OutboundReportEntity> UpdateViewAsync(UpdateOutboundReportViewRequest request, string username, bool includeAllOwners, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, string username, bool includeAllOwners, CancellationToken cancellationToken = default);
    Task<OutboundReportEntity?> SetEnabledAsync(string id, bool enabled, DateTimeOffset? nextRunUtc, string username, bool includeAllOwners, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OutboundReportEntity>> GetDueAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OutboundReportHistorySummary>> GetHistoryAsync(string reportId, string username, bool includeAllOwners, int maximum = 50, CancellationToken cancellationToken = default);
    Task RecordExecutionAsync(OutboundReportEntity report, DateTimeOffset startedUtc, OutboundReportExecutionResult result, DateTimeOffset? nextRunUtc, bool keepEnabled, int recipientCount, bool updateScheduleState, CancellationToken cancellationToken = default);
    Task RecordAdHocExecutionAsync(string environmentKey, string title, DateTimeOffset startedUtc, OutboundReportExecutionResult result, int recipientCount, CancellationToken cancellationToken = default);
}

public sealed class OutboundReportRepository : IOutboundReportRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly MetadataDbContext _db;
    private readonly IMoveItSessionService _sessionService;

    public OutboundReportRepository(MetadataDbContext db, IMoveItSessionService sessionService)
    {
        _db = db;
        _sessionService = sessionService;
    }

    public async Task<OutboundReportEntity> CreateAsync(CreateOutboundReportRequest request, ReportScheduleDefinition schedule, DateTimeOffset nextRunUtc, CancellationToken cancellationToken = default)
    {
        var session = _sessionService.Get() ?? throw new MoveItAuthenticationException("MOVEit session expired. Please sign in again.");
        var environmentKey = MoveItEnvironmentKey.Normalize(session.GetEnvironmentKey());
        var now = DateTimeOffset.UtcNow;
        var title = ValidateTitle(request.Title);
        var recipients = NormalizeRecipients(request.Recipients);
        ValidateViewState(request.ViewState);
        ValidateSourceContext(request.SourceContext, request.GridKey);

        var entity = new OutboundReportEntity
        {
            Id = Guid.NewGuid().ToString("D"),
            EnvironmentKey = environmentKey,
            OwnerUsername = session.Username.Trim(),
            OwnerUsernameKey = NormalizeUsername(session.Username),
            GridKey = ValidateGridKey(request.GridKey),
            Title = title,
            Description = ValidateDescription(request.Description),
            Recipients = string.Join(";", recipients),
            ViewStateJson = JsonSerializer.Serialize(request.ViewState, JsonOptions),
            SourceContextJson = JsonSerializer.Serialize(OutboundReportSourceRoutes.Normalize(request.GridKey, request.SourceContext), JsonOptions),
            ScheduleJson = JsonSerializer.Serialize(schedule, JsonOptions),
            Enabled = true,
            NextRunUtc = nextRunUtc,
            LastStatus = "Pending",
            LastMessage = string.Empty,
            CreatedUtc = now,
            ModifiedUtc = now
        };
        _db.OutboundReports.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<IReadOnlyList<OutboundReportSummary>> GetAllAsync(string username, bool includeAllOwners, CancellationToken cancellationToken = default)
    {
        var rows = await ApplyOwnerScope(_db.OutboundReports.AsNoTracking(), username, includeAllOwners)
            .OrderBy(item => item.Title)
            .ThenBy(item => item.EnvironmentKey)
            .ToListAsync(cancellationToken);
        return rows.Select(ToSummary).ToArray();
    }

    public Task<OutboundReportEntity?> GetAsync(string id, string username, bool includeAllOwners, CancellationToken cancellationToken = default)
    {
        var cleanId = NormalizeId(id);
        return ApplyOwnerScope(_db.OutboundReports, username, includeAllOwners)
            .SingleOrDefaultAsync(item => item.Id == cleanId, cancellationToken);
    }

    public async Task<OutboundReportEntity> UpdateAsync(UpdateOutboundReportRequest request, ReportScheduleDefinition schedule, DateTimeOffset? nextRunUtc, string username, bool includeAllOwners, CancellationToken cancellationToken = default)
    {
        var id = NormalizeId(request.Id);
        var entity = await ApplyOwnerScope(_db.OutboundReports, username, includeAllOwners)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new ArgumentException("The Report no longer exists or you do not have access to it.");
        entity.Title = ValidateTitle(request.Title);
        entity.Description = ValidateDescription(request.Description);
        entity.Recipients = string.Join(";", NormalizeRecipients(request.Recipients));
        entity.ScheduleJson = JsonSerializer.Serialize(schedule, JsonOptions);
        entity.Enabled = request.Enabled;
        entity.NextRunUtc = request.Enabled ? nextRunUtc : null;
        entity.ModifiedUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<OutboundReportEntity> UpdateViewAsync(UpdateOutboundReportViewRequest request, string username, bool includeAllOwners, CancellationToken cancellationToken = default)
    {
        var id = NormalizeId(request.Id);
        var entity = await ApplyOwnerScope(_db.OutboundReports, username, includeAllOwners)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new ArgumentException("The Report no longer exists or you do not have access to it.");

        var gridKey = ValidateGridKey(request.GridKey);
        if (!entity.GridKey.Equals(gridKey, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The Report belongs to a different grid and cannot be updated from this page.");

        ValidateViewState(request.ViewState);
        ValidateSourceContext(request.SourceContext, gridKey);
        entity.ViewStateJson = JsonSerializer.Serialize(request.ViewState, JsonOptions);
        entity.SourceContextJson = JsonSerializer.Serialize(OutboundReportSourceRoutes.Normalize(gridKey, request.SourceContext), JsonOptions);
        entity.ModifiedUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<bool> DeleteAsync(string id, string username, bool includeAllOwners, CancellationToken cancellationToken = default)
    {
        var cleanId = NormalizeId(id);
        var entity = await ApplyOwnerScope(_db.OutboundReports, username, includeAllOwners)
            .SingleOrDefaultAsync(item => item.Id == cleanId, cancellationToken);
        if (entity is null) return false;
        _db.OutboundReports.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<OutboundReportEntity?> SetEnabledAsync(string id, bool enabled, DateTimeOffset? nextRunUtc, string username, bool includeAllOwners, CancellationToken cancellationToken = default)
    {
        var cleanId = NormalizeId(id);
        var entity = await ApplyOwnerScope(_db.OutboundReports, username, includeAllOwners)
            .SingleOrDefaultAsync(item => item.Id == cleanId, cancellationToken);
        if (entity is null) return null;
        entity.Enabled = enabled;
        entity.NextRunUtc = enabled ? nextRunUtc : null;
        entity.ModifiedUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<IReadOnlyList<OutboundReportEntity>> GetDueAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {

        // SQLite cannot translate ordering/comparison operations over DateTimeOffset. Keep the
        // simple enabled/pending predicates in SQL, then evaluate due time and order the small
        // saved-report set in memory. This also makes an idle scheduler cycle a normal empty result.
        var candidates = await _db.OutboundReports.AsNoTracking()
            .Where(item => item.Enabled && item.NextRunUtc != null)
            .ToListAsync(cancellationToken);

        return candidates
            .Where(item => item.NextRunUtc!.Value <= utcNow)
            .OrderBy(item => item.NextRunUtc!.Value)
            .ToList();
    }

    public async Task<IReadOnlyList<OutboundReportHistorySummary>> GetHistoryAsync(string reportId, string username, bool includeAllOwners, int maximum = 50, CancellationToken cancellationToken = default)
    {
        var id = NormalizeId(reportId);
        maximum = Math.Clamp(maximum, 1, 200);

        var visible = await ApplyOwnerScope(_db.OutboundReports.AsNoTracking(), username, includeAllOwners)
            .AnyAsync(item => item.Id == id, cancellationToken);
        if (!visible)
            throw new ArgumentException("The Report no longer exists or you do not have access to it.");

        // Keep DateTimeOffset ordering out of the SQLite query for the same reason as GetDueAsync.
        var rows = await _db.OutboundReportHistory.AsNoTracking()
            .Where(item => item.ReportId == id)
            .ToListAsync(cancellationToken);

        return rows
            .OrderByDescending(item => item.StartedUtc)
            .Take(maximum)
            .Select(item => new OutboundReportHistorySummary(item.Id, item.ReportId, item.EnvironmentKey, item.Title, item.StartedUtc, item.CompletedUtc, item.Status, item.RowCount, item.RecipientCount, item.Message))
            .ToList();
    }

    public async Task RecordExecutionAsync(OutboundReportEntity report, DateTimeOffset startedUtc, OutboundReportExecutionResult result, DateTimeOffset? nextRunUtc, bool keepEnabled, int recipientCount, bool updateScheduleState, CancellationToken cancellationToken = default)
    {
        var entity = await _db.OutboundReports.SingleOrDefaultAsync(item => item.Id == report.Id, cancellationToken);
        if (entity is not null)
        {
            entity.LastRunUtc = result.CompletedUtc;
            entity.LastStatus = result.Success ? "Success" : "Failed";
            entity.LastMessage = Trim(result.Message, 2000);

            // Scheduled execution may advance Enabled/NextRunUtc only when the definition has not
            // changed since the scheduler loaded it. Administrator edits made during execution win.
            if (updateScheduleState && entity.ModifiedUtc == report.ModifiedUtc)
            {
                entity.Enabled = keepEnabled;
                entity.NextRunUtc = keepEnabled ? nextRunUtc : null;
                entity.ModifiedUtc = result.CompletedUtc;
            }
        }

        _db.OutboundReportHistory.Add(new OutboundReportHistoryEntity
        {
            ReportId = report.Id,
            EnvironmentKey = report.EnvironmentKey,
            Title = Trim(report.Title, 200),
            StartedUtc = startedUtc,
            CompletedUtc = result.CompletedUtc,
            Status = result.Success ? "Success" : "Failed",
            RowCount = result.RowCount,
            RecipientCount = recipientCount,
            Message = Trim(result.Message, 2000)
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordAdHocExecutionAsync(string environmentKey, string title, DateTimeOffset startedUtc, OutboundReportExecutionResult result, int recipientCount, CancellationToken cancellationToken = default)
    {
        _db.OutboundReportHistory.Add(new OutboundReportHistoryEntity
        {
            ReportId = string.Empty,
            EnvironmentKey = MoveItEnvironmentKey.Normalize(environmentKey),
            Title = Trim(title, 200),
            StartedUtc = startedUtc,
            CompletedUtc = result.CompletedUtc,
            Status = result.Success ? "Success" : "Failed",
            RowCount = result.RowCount,
            RecipientCount = recipientCount,
            Message = Trim(result.Message, 2000)
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    private OutboundReportSummary ToSummary(OutboundReportEntity entity)
    {
        ReportScheduleDefinition schedule;
        try { schedule = JsonSerializer.Deserialize<ReportScheduleDefinition>(entity.ScheduleJson, JsonOptions) ?? new ReportScheduleDefinition(); }
        catch { schedule = new ReportScheduleDefinition(); }
        OutboundReportGridRegistry.TryGetLabel(entity.GridKey, out var label);
        return new OutboundReportSummary(
            entity.Id, entity.EnvironmentKey, entity.OwnerUsername, entity.GridKey, label ?? entity.GridKey,
            entity.Title, entity.Description, SplitRecipients(entity.Recipients), entity.Enabled, schedule,
            entity.NextRunUtc, entity.LastRunUtc, entity.LastStatus, entity.LastMessage, entity.CreatedUtc, entity.ModifiedUtc);
    }

    public static IReadOnlyList<string> NormalizeRecipients(string value)
    {
        var recipients = SplitRecipients(value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (recipients.Length == 0) throw new ArgumentException("Enter at least one email recipient.");
        if (recipients.Length > 100) throw new ArgumentException("A report can contain at most 100 recipients.");
        foreach (var recipient in recipients)
        {
            try { _ = new MailAddress(recipient); }
            catch (FormatException) { throw new ArgumentException($"'{recipient}' is not a valid email address."); }
        }
        return recipients;
    }

    private static IReadOnlyList<string> SplitRecipients(string value)
        => (value ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(item => item.Length > 0).ToArray();

    private static string ValidateGridKey(string value)
    {
        var grid = (value ?? string.Empty).Trim();
        if (!OutboundReportGridRegistry.TryGetLabel(grid, out _)) throw new ArgumentException("This grid does not support scheduled reports.");
        return grid.ToLowerInvariant();
    }

    private static string ValidateTitle(string value)
    {
        var title = (value ?? string.Empty).Trim();
        if (title.Length == 0) throw new ArgumentException("Report Title is required.");
        if (title.Length > 200) throw new ArgumentException("Report Title cannot exceed 200 characters.");
        return title;
    }

    private static string ValidateDescription(string value)
    {
        var description = (value ?? string.Empty).Trim();
        if (description.Length > 1000) throw new ArgumentException("Report Description cannot exceed 1000 characters.");
        return description;
    }

    private static void ValidateViewState(ReportGridViewState state)
    {
        if (state is null || state.SchemaVersion != 1) throw new ArgumentException("The report grid view is invalid.");
        if (state.Columns is null || state.Columns.Count == 0 || state.Columns.Count > 200) throw new ArgumentException("The report must contain at least one visible column.");
        if (state.Columns.Any(column => string.IsNullOrWhiteSpace(column.Key) || string.IsNullOrWhiteSpace(column.Label))) throw new ArgumentException("The report contains an invalid column definition.");
        if (state.Columns.Any(column => column.Width.HasValue && (column.Width.Value < 40 || column.Width.Value > 2000))) throw new ArgumentException("The report contains an invalid column width.");
        if ((state.Filters?.Count ?? 0) > 200 || (state.FilterTypes?.Count ?? 0) > 200) throw new ArgumentException("The report contains too many column filters.");
    }

    private static void ValidateSourceContext(ReportSourceContext context, string gridKey)
    {
        _ = OutboundReportSourceRoutes.Normalize(gridKey, context);
        if ((context.Query?.Count ?? 0) > 50)
            throw new ArgumentException("The report source contains too many query parameters.");
    }

    private static string NormalizeId(string value)
    {
        if (!Guid.TryParse((value ?? string.Empty).Trim(), out var id)) throw new ArgumentException("The report identifier is invalid.");
        return id.ToString("D");
    }

    // Purpose: Applies MAUI Report ownership at the persistence boundary. Admin callers can see all
    // definitions; normal MAUI Users are restricted to definitions they created. Keeping this filter
    // in the repository prevents a crafted Report id from bypassing the Reports grid's UI filtering.
    private static IQueryable<OutboundReportEntity> ApplyOwnerScope(
        IQueryable<OutboundReportEntity> query,
        string username,
        bool includeAllOwners)
    {
        if (includeAllOwners) return query;
        var usernameKey = NormalizeUsername(username);
        if (usernameKey.Length == 0) return query.Where(_ => false);
        return query.Where(item => item.OwnerUsernameKey == usernameKey);
    }

    private static string NormalizeUsername(string value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static string Trim(string value, int max)
    {
        var text = value ?? string.Empty;
        return text.Length <= max ? text : text[..max];
    }
}
