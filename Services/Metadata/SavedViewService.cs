// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.EntityFrameworkCore;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Services.Metadata;

public interface ISavedViewService
{
    // Purpose: Returns the saved grid views owned by the current user.
    Task<IReadOnlyList<SavedViewSummary>> GetCurrentAsync(CancellationToken cancellationToken = default);
    // Purpose: Returns current by id.
    Task<SavedViewDetail?> GetCurrentByIdAsync(string id, CancellationToken cancellationToken = default);
    // Purpose: Saves current after validating the supplied values.
    Task<SavedViewSaveResult> SaveCurrentAsync(SavedViewSaveRequest request, CancellationToken cancellationToken = default);
    // Purpose: Deletes current after applying the required guardrails.
    Task<bool> DeleteCurrentAsync(string id, CancellationToken cancellationToken = default);
}

public sealed class SavedViewService : ISavedViewService
{
    private const int CurrentSchemaVersion = 1;
    private readonly MetadataDbContext _db;
    private readonly IMoveItSessionService _sessionService;
    private readonly ILogger<SavedViewService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

    // Purpose: Initializes saved view service with its required dependencies.
    public SavedViewService(MetadataDbContext db, IMoveItSessionService sessionService, ILogger<SavedViewService> logger)
    {
        _db = db;
        _sessionService = sessionService;
        _logger = logger;
    }

    // Purpose: Returns the saved grid views owned by the current user.
    public async Task<IReadOnlyList<SavedViewSummary>> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var session = _sessionService.Get();
        if (session is null || string.IsNullOrWhiteSpace(session.Username)) return Array.Empty<SavedViewSummary>();

        var env = MoveItEnvironmentKey.Normalize(session.GetEnvironmentKey());
        var usernameKey = NormalizeUsername(session.Username);
        try
        {
            var rows = await _db.SavedViews.AsNoTracking()
                .Where(item => item.EnvironmentKey == env && item.UsernameKey == usernameKey)
                .OrderBy(item => item.ViewName)
                .ThenBy(item => item.GridKey)
                .ToListAsync(cancellationToken);
            return rows.Select(ToSummary).Where(item => item is not null).Cast<SavedViewSummary>().ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to load saved grid views for {Username} in environment {EnvironmentKey}.", session.Username, env);
            return Array.Empty<SavedViewSummary>();
        }
    }

    // Purpose: Returns current by id.
    public async Task<SavedViewDetail?> GetCurrentByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var session = RequireSession();
        var env = MoveItEnvironmentKey.Normalize(session.GetEnvironmentKey());
        var usernameKey = NormalizeUsername(session.Username);
        var cleanId = NormalizeId(id);

        var row = await _db.SavedViews.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == cleanId && item.EnvironmentKey == env && item.UsernameKey == usernameKey, cancellationToken);
        if (row is null) return null;

        var summary = ToSummary(row);
        if (summary is null) return null;
        SavedGridViewState? state;
        try
        {
            state = JsonSerializer.Deserialize<SavedGridViewState>(row.ViewStateJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Saved view {SavedViewId} contains invalid state JSON.", row.Id);
            return new SavedViewDetail(summary, null, "The saved view data is damaged and could not be applied. The normal grid has been loaded instead.");
        }

        if (state is null || state.SchemaVersion != CurrentSchemaVersion)
            return new SavedViewDetail(summary, null, "The saved view uses an unsupported schema and could not be applied. The normal grid has been loaded instead.");
        try
        {
            return new SavedViewDetail(summary, ValidateState(state));
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Saved view {SavedViewId} contains invalid state data.", row.Id);
            return new SavedViewDetail(summary, null, "The saved view data is invalid and could not be applied. The normal grid has been loaded instead.");
        }
    }

    // Purpose: Saves current after validating the supplied values.
    public async Task<SavedViewSaveResult> SaveCurrentAsync(SavedViewSaveRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var session = RequireSession();
        var env = MoveItEnvironmentKey.Normalize(session.GetEnvironmentKey());
        var username = session.Username.Trim();
        var usernameKey = NormalizeUsername(username);
        var grid = ValidateGrid(request.GridKey);
        var viewName = ValidateViewName(request.ViewName);
        var description = ValidateDescription(request.Description);
        var viewNameKey = NormalizeViewName(viewName);
        var state = ValidateState(request.State);
        var requestedId = string.IsNullOrWhiteSpace(request.Id) ? string.Empty : NormalizeId(request.Id);

        if (requestedId.Length > 0)
        {
            var current = await _db.SavedViews.SingleOrDefaultAsync(
                item => item.Id == requestedId && item.EnvironmentKey == env && item.UsernameKey == usernameKey,
                cancellationToken);
            if (current is null) throw new ArgumentException("The saved view being updated was not found.");
            if (!current.GridKey.Equals(grid.GridKey, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The saved view belongs to a different grid.");

            var collision = await _db.SavedViews.AsNoTracking().SingleOrDefaultAsync(
                item => item.EnvironmentKey == env
                    && item.UsernameKey == usernameKey
                    && item.ViewNameKey == viewNameKey
                    && item.Id != requestedId,
                cancellationToken);
            if (collision is not null)
            {
                return new SavedViewSaveResult(
                    collision.GridKey.Equals(grid.GridKey, StringComparison.OrdinalIgnoreCase)
                        ? SavedViewSaveStatus.Conflict
                        : SavedViewSaveStatus.ConflictOtherGrid,
                    ExistingView: ToSummary(collision));
            }

            var modifiedUtc = DateTimeOffset.UtcNow;
            var serializedState = JsonSerializer.Serialize(state, JsonOptions);
            if (current.ViewNameKey.Equals(viewNameKey, StringComparison.OrdinalIgnoreCase))
            {
                current.Username = username;
                current.ViewName = viewName;
                current.Description = description;
                current.ViewStateJson = serializedState;
                current.ModifiedUtc = modifiedUtc;
                await _db.SaveChangesAsync(cancellationToken);
                return new SavedViewSaveResult(SavedViewSaveStatus.Saved, View: ToSummary(current));
            }

            // Renaming an active view intentionally gives it a new identity. Add the replacement
            // and remove the old row in one SaveChanges call so a failure cannot lose the original view.
            var replacement = new SavedViewEntity
            {
                Id = Guid.NewGuid().ToString("D"),
                EnvironmentKey = env,
                UsernameKey = usernameKey,
                Username = username,
                GridKey = grid.GridKey,
                ViewName = viewName,
                ViewNameKey = viewNameKey,
                Description = description,
                ViewStateJson = serializedState,
                CreatedUtc = modifiedUtc,
                ModifiedUtc = modifiedUtc
            };
            _db.SavedViews.Add(replacement);
            _db.SavedViews.Remove(current);
            await _db.SaveChangesAsync(cancellationToken);
            return new SavedViewSaveResult(SavedViewSaveStatus.Saved, View: ToSummary(replacement));
        }

        var existing = await _db.SavedViews
            .SingleOrDefaultAsync(item => item.EnvironmentKey == env && item.UsernameKey == usernameKey && item.ViewNameKey == viewNameKey, cancellationToken);

        if (existing is not null && !request.Overwrite)
        {
            var existingSummary = ToSummary(existing);
            return new SavedViewSaveResult(
                existing.GridKey.Equals(grid.GridKey, StringComparison.OrdinalIgnoreCase) ? SavedViewSaveStatus.Conflict : SavedViewSaveStatus.ConflictOtherGrid,
                ExistingView: existingSummary);
        }

        if (existing is not null && !existing.GridKey.Equals(grid.GridKey, StringComparison.OrdinalIgnoreCase))
        {
            return new SavedViewSaveResult(SavedViewSaveStatus.ConflictOtherGrid, ExistingView: ToSummary(existing));
        }

        var now = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(state, JsonOptions);
        var isNew = existing is null;
        if (existing is null)
        {
            existing = new SavedViewEntity
            {
                Id = Guid.NewGuid().ToString("D"),
                EnvironmentKey = env,
                UsernameKey = usernameKey,
                Username = username,
                GridKey = grid.GridKey,
                ViewName = viewName,
                ViewNameKey = viewNameKey,
                Description = description,
                ViewStateJson = json,
                CreatedUtc = now,
                ModifiedUtc = now
            };
            _db.SavedViews.Add(existing);
        }
        else
        {
            existing.Username = username;
            existing.ViewName = viewName;
            existing.ViewNameKey = viewNameKey;
            existing.Description = description;
            existing.ViewStateJson = json;
            existing.ModifiedUtc = now;
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (isNew)
        {
            // A second tab can race the initial uniqueness check. Re-read the globally unique view name
            // and surface the same deterministic conflict behavior rather than leaking a database error.
            _db.Entry(existing).State = EntityState.Detached;
            var collision = await _db.SavedViews.AsNoTracking().SingleOrDefaultAsync(
                item => item.EnvironmentKey == env && item.UsernameKey == usernameKey && item.ViewNameKey == viewNameKey,
                cancellationToken);
            if (collision is not null)
            {
                var collisionSummary = ToSummary(collision);
                return new SavedViewSaveResult(
                    collision.GridKey.Equals(grid.GridKey, StringComparison.OrdinalIgnoreCase) ? SavedViewSaveStatus.Conflict : SavedViewSaveStatus.ConflictOtherGrid,
                    ExistingView: collisionSummary);
            }
            throw;
        }
        return new SavedViewSaveResult(SavedViewSaveStatus.Saved, View: ToSummary(existing));
    }

    // Purpose: Deletes current after applying the required guardrails.
    public async Task<bool> DeleteCurrentAsync(string id, CancellationToken cancellationToken = default)
    {
        var session = RequireSession();
        var env = MoveItEnvironmentKey.Normalize(session.GetEnvironmentKey());
        var usernameKey = NormalizeUsername(session.Username);
        var cleanId = NormalizeId(id);
        var row = await _db.SavedViews.SingleOrDefaultAsync(
            item => item.Id == cleanId && item.EnvironmentKey == env && item.UsernameKey == usernameKey,
            cancellationToken);
        if (row is null) return false;
        _db.SavedViews.Remove(row);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    // Purpose: Returns the required session.
    private MoveItSessionState RequireSession()
        => _sessionService.Get() ?? throw new InvalidOperationException("MOVEit session expired. Please sign in again.");

    // Purpose: Validates grid.
    private static SavedViewGridDefinition ValidateGrid(string? gridKey)
    {
        if (!SavedViewGridRegistry.TryGet(gridKey, out var definition))
            throw new ArgumentException("This grid does not support saved views.", nameof(gridKey));
        return definition;
    }

    // Purpose: Validates view name.
    private static string ValidateViewName(string? value)
    {
        var name = (value ?? string.Empty).Trim();
        if (name.Length == 0) throw new ArgumentException("View Name is required.");
        if (name.Length > 100) throw new ArgumentException("View Name cannot exceed 100 characters.");
        if (name.Any(char.IsControl)) throw new ArgumentException("View Name contains invalid characters.");
        if (SavedViewGridRegistry.IsReservedName(name)) throw new ArgumentException($"'{name}' is reserved by MAUI. Choose another View Name.");
        return name;
    }

    // Purpose: Validates description.
    private static string ValidateDescription(string? value)
    {
        var description = (value ?? string.Empty).Trim();
        if (description.Length > 500) throw new ArgumentException("Description cannot exceed 500 characters.");
        if (description.Any(ch => char.IsControl(ch) && ch is not '\r' and not '\n' and not '\t'))
            throw new ArgumentException("Description contains invalid characters.");
        return description;
    }

    // Purpose: Validates state.
    private static SavedGridViewState ValidateState(SavedGridViewState? state)
    {
        if (state is null) throw new ArgumentException("The grid view state is required.");
        if (state.SchemaVersion != CurrentSchemaVersion) throw new ArgumentException("The saved-view schema version is not supported.");
        if (state.Columns is null || state.Columns.Count == 0 || state.Columns.Count > 200) throw new ArgumentException("The saved view contains an invalid column definition.");

        var normalized = NormalizeState(state);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var orders = new HashSet<int>();
        foreach (var column in normalized.Columns)
        {
            if (column.Key.Length == 0 || column.Key.Length > 128) throw new ArgumentException("The saved view contains an invalid column key.");
            if (!keys.Add(column.Key)) throw new ArgumentException($"The saved view contains duplicate column '{column.Key}'.");
            if (column.Order < 0 || column.Order >= 200 || !orders.Add(column.Order)) throw new ArgumentException("The saved view contains an invalid column order.");
            if (column.Width.HasValue && (column.Width.Value < 40 || column.Width.Value > 2000)) throw new ArgumentException($"The saved width for '{column.Key}' is outside the supported range.");
        }
        if (!normalized.Columns.Any(column => column.Visible)) throw new ArgumentException("A saved view must keep at least one column visible.");

        if (normalized.Sort is not null)
        {
            if (!keys.Contains(normalized.Sort.Column)) throw new ArgumentException("The saved sort column is not part of the saved view.");
            if (normalized.Sort.Direction != "asc" && normalized.Sort.Direction != "desc") throw new ArgumentException("The saved sort direction is invalid.");
        }

        if (normalized.Filters.Count > 200) throw new ArgumentException("The saved view contains too many filters.");
        foreach (var filter in normalized.Filters)
        {
            if (!keys.Contains(filter.Key)) throw new ArgumentException($"Filter column '{filter.Key}' is not part of the saved view.");
            if (filter.Value.Length > 1000) throw new ArgumentException($"The filter for '{filter.Key}' is too long.");
        }

        return normalized;
    }

    // Purpose: Normalizes state.
    private static SavedGridViewState NormalizeState(SavedGridViewState state)
    {
        var columns = (state.Columns ?? new List<SavedGridViewColumnState>())
            .Select((column, index) => new SavedGridViewColumnState
            {
                Key = (column.Key ?? string.Empty).Trim(),
                Visible = column.Visible,
                Order = column.Order >= 0 ? column.Order : index,
                Width = column.Width is null ? null : Math.Round(column.Width.Value, 2)
            })
            .OrderBy(column => column.Order)
            .ToList();

        SavedGridViewSortState? sort = null;
        if (!string.IsNullOrWhiteSpace(state.Sort?.Column) && !string.IsNullOrWhiteSpace(state.Sort?.Direction))
        {
            sort = new SavedGridViewSortState
            {
                Column = state.Sort.Column.Trim(),
                Direction = state.Sort.Direction.Trim().ToLowerInvariant()
            };
        }

        var filters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in state.Filters ?? new Dictionary<string, string>())
        {
            var key = (pair.Key ?? string.Empty).Trim();
            if (key.Length == 0) continue;
            filters[key] = pair.Value ?? string.Empty;
        }

        return new SavedGridViewState { SchemaVersion = CurrentSchemaVersion, Columns = columns, Sort = sort, Filters = filters };
    }

    // Purpose: Converts the current value to summary.
    private static SavedViewSummary? ToSummary(SavedViewEntity row)
    {
        if (!SavedViewGridRegistry.TryGet(row.GridKey, out var grid)) return null;
        return new SavedViewSummary(row.Id, grid.GridKey, grid.Label, row.ViewName, row.Description ?? string.Empty, grid.Controller, grid.Action, row.CreatedUtc, row.ModifiedUtc);
    }

    // Purpose: Normalizes username.
    private static string NormalizeUsername(string? username)
    {
        var value = username?.Trim() ?? string.Empty;
        if (value.Length == 0) throw new ArgumentException("Username is required.", nameof(username));
        return value.ToUpperInvariant();
    }

    // Purpose: Normalizes view name.
    private static string NormalizeViewName(string viewName) => viewName.Trim().ToUpperInvariant();

    // Purpose: Normalizes ID.
    private static string NormalizeId(string? id)
    {
        var value = id?.Trim() ?? string.Empty;
        if (!Guid.TryParse(value, out var parsed)) throw new ArgumentException("The saved view identifier is invalid.", nameof(id));
        return parsed.ToString("D");
    }
}
