// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Concurrency;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Services.Metadata;

public interface IUserPreferencesService
{
    // Purpose: Returns the application defaults used when no user preference record exists.
    UserPreferencesSnapshot GetDefaults();
    // Purpose: Returns the persisted preferences for one environment and username.
    Task<UserPreferencesSnapshot> GetAsync(string environmentKey, string username, CancellationToken cancellationToken = default);
    // Purpose: Returns the preferences for the currently authenticated user and environment.
    Task<UserPreferencesSnapshot> GetCurrentAsync(CancellationToken cancellationToken = default);
    // Purpose: Saves one user preference snapshot after normalization and validation.
    Task<UserPreferencesSnapshot> SaveAsync(string environmentKey, string username, UserPreferencesSaveRequest request, CancellationToken cancellationToken = default);
    // Purpose: Saves current after validating the supplied values.
    Task<UserPreferencesSnapshot> SaveCurrentAsync(UserPreferencesSaveRequest request, CancellationToken cancellationToken = default);
    // Purpose: Updates current theme while preserving the applicable guardrails.
    Task<UserPreferencesSnapshot> UpdateCurrentThemeAsync(string theme, CancellationToken cancellationToken = default);
    // Purpose: Updates current grid page size while preserving the applicable guardrails.
    Task<UserPreferencesSnapshot> UpdateCurrentGridPageSizeAsync(int gridPageSize, CancellationToken cancellationToken = default);
    // Purpose: Returns one persisted grid column layout for the current user and environment.
    Task<UserGridLayoutSnapshot?> GetCurrentGridLayoutAsync(string gridKey, CancellationToken cancellationToken = default);
    // Purpose: Saves one grid column layout for the current user and environment.
    Task<UserGridLayoutSnapshot> SaveCurrentGridLayoutAsync(string gridKey, UserGridLayoutState layout, CancellationToken cancellationToken = default);
    // Purpose: Removes one persisted grid column layout so configuration defaults apply again.
    Task DeleteCurrentGridLayoutAsync(string gridKey, CancellationToken cancellationToken = default);
    // Editor preferences are persisted using the existing per-user JSON preference store.
    Task<TraditionalTaskEditorPreferences> GetCurrentTraditionalTaskEditorPreferencesAsync(CancellationToken cancellationToken = default);
    Task<TraditionalTaskEditorPreferences> SaveCurrentTraditionalTaskEditorPreferencesAsync(TraditionalTaskEditorPreferences preferences, CancellationToken cancellationToken = default);
    Task<HostEditorPreferences> GetCurrentHostEditorPreferencesAsync(CancellationToken cancellationToken = default);
    Task<HostEditorPreferences> SaveCurrentHostEditorPreferencesAsync(HostEditorPreferences preferences, CancellationToken cancellationToken = default);
    Task<ScriptEditorPreferences> GetCurrentScriptEditorPreferencesAsync(CancellationToken cancellationToken = default);
    Task<ScriptEditorPreferences> SaveCurrentScriptEditorPreferencesAsync(ScriptEditorPreferences preferences, CancellationToken cancellationToken = default);
    Task<DateListEditorPreferences> GetCurrentDateListEditorPreferencesAsync(CancellationToken cancellationToken = default);
    Task<DateListEditorPreferences> SaveCurrentDateListEditorPreferencesAsync(DateListEditorPreferences preferences, CancellationToken cancellationToken = default);
    // Purpose: Returns grid page size choices.
    IReadOnlyList<int> GetGridPageSizeChoices();
}

public sealed class UserPreferencesService : IUserPreferencesService
{
    private readonly MetadataDbContext _db;
    private readonly IMoveItSessionService _sessionService;
    private readonly UserPreferencesOptions _options;
    private readonly GridBehaviorOptions _gridOptions;
    private readonly ILogger<UserPreferencesService> _logger;
    private static readonly JsonSerializerOptions GridLayoutJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly KeyedAsyncLock PreferenceWriteLocks = new(StringComparer.OrdinalIgnoreCase);

    // Purpose: Initializes user preferences service with its required dependencies.
    public UserPreferencesService(
        MetadataDbContext db,
        IMoveItSessionService sessionService,
        IOptionsSnapshot<UserPreferencesOptions> options,
        IOptionsSnapshot<GridBehaviorOptions> gridOptions,
        ILogger<UserPreferencesService> logger)
    {
        _db = db;
        _sessionService = sessionService;
        _options = options.Value;
        _gridOptions = gridOptions.Value;
        _logger = logger;
    }

    // Purpose: Returns the application defaults used when no user preference record exists.
    public UserPreferencesSnapshot GetDefaults()
    {
        var theme = NormalizeTheme(_options.Theme.Value);
        var startPage = NormalizeStartPage(_options.StartPage.Value);
        var gridPageSize = NormalizeGridPageSize(_gridOptions.DefaultRowSize.Value);
        return new UserPreferencesSnapshot(theme, startPage, gridPageSize);
    }

    // Purpose: Returns the persisted preferences for one environment and username.
    public async Task<UserPreferencesSnapshot> GetAsync(string environmentKey, string username, CancellationToken cancellationToken = default)
    {
        var env = MoveItEnvironmentKey.Normalize(environmentKey);
        var usernameKey = NormalizeUsername(username);
        UserPreferenceEntity? row;
        try
        {
            row = await _db.UserPreferences
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.EnvironmentKey == env && item.UsernameKey == usernameKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to load MAUI user preferences for {Username} in environment {EnvironmentKey}; using configured defaults.", username, env);
            return GetDefaults();
        }

        if (row is null) return GetDefaults();

        return new UserPreferencesSnapshot(
            NormalizeTheme(row.Theme),
            NormalizeStartPage(row.StartPage),
            NormalizeGridPageSize(row.GridPageSize),
            row.CreatedUtc,
            row.ModifiedUtc);
    }

    // Purpose: Returns the preferences for the currently authenticated user and environment.
    public async Task<UserPreferencesSnapshot> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var session = _sessionService.Get();
        if (session is null || string.IsNullOrWhiteSpace(session.Username)) return GetDefaults();
        return await GetAsync(session.GetEnvironmentKey(), session.Username, cancellationToken);
    }

    // Purpose: Saves one user preference snapshot after normalization and validation.
    public async Task<UserPreferencesSnapshot> SaveAsync(
        string environmentKey,
        string username,
        UserPreferencesSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var env = MoveItEnvironmentKey.Normalize(environmentKey);
        var cleanUsername = username?.Trim() ?? string.Empty;
        if (cleanUsername.Length == 0) throw new ArgumentException("Username is required.", nameof(username));
        var usernameKey = NormalizeUsername(cleanUsername);

        var theme = ValidateTheme(request.Theme);
        var startPage = ValidateStartPage(request.StartPage);
        var gridPageSize = ValidateGridPageSize(request.GridPageSize);
        using var preferenceWriteLease = await AcquirePreferenceWriteLockAsync(env, usernameKey, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var row = await _db.UserPreferences
            .SingleOrDefaultAsync(item => item.EnvironmentKey == env && item.UsernameKey == usernameKey, cancellationToken);

        if (row is null)
        {
            row = new UserPreferenceEntity
            {
                EnvironmentKey = env,
                UsernameKey = usernameKey,
                Username = cleanUsername,
                Theme = theme,
                StartPage = startPage,
                GridPageSize = gridPageSize,
                CreatedUtc = now,
                ModifiedUtc = now
            };
            _db.UserPreferences.Add(row);
        }
        else
        {
            row.Username = cleanUsername;
            row.Theme = theme;
            row.StartPage = startPage;
            row.GridPageSize = gridPageSize;
            row.ModifiedUtc = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new UserPreferencesSnapshot(row.Theme, row.StartPage, row.GridPageSize, row.CreatedUtc, row.ModifiedUtc);
    }

    // Purpose: Saves current after validating the supplied values.
    public async Task<UserPreferencesSnapshot> SaveCurrentAsync(UserPreferencesSaveRequest request, CancellationToken cancellationToken = default)
    {
        var session = RequireSession();
        return await SaveAsync(session.GetEnvironmentKey(), session.Username, request, cancellationToken);
    }

    // Purpose: Updates current theme while preserving the applicable guardrails.
    public async Task<UserPreferencesSnapshot> UpdateCurrentThemeAsync(string theme, CancellationToken cancellationToken = default)
    {
        var current = await GetCurrentAsync(cancellationToken);
        return await SaveCurrentAsync(new UserPreferencesSaveRequest
        {
            Theme = theme,
            StartPage = current.StartPage,
            GridPageSize = current.GridPageSize
        }, cancellationToken);
    }

    // Purpose: Updates current grid page size while preserving the applicable guardrails.
    public async Task<UserPreferencesSnapshot> UpdateCurrentGridPageSizeAsync(int gridPageSize, CancellationToken cancellationToken = default)
    {
        var current = await GetCurrentAsync(cancellationToken);
        return await SaveCurrentAsync(new UserPreferencesSaveRequest
        {
            Theme = current.Theme,
            StartPage = current.StartPage,
            GridPageSize = gridPageSize
        }, cancellationToken);
    }

    // Purpose: Returns one persisted grid column layout for the current user and environment.
    public async Task<UserGridLayoutSnapshot?> GetCurrentGridLayoutAsync(string gridKey, CancellationToken cancellationToken = default)
    {
        var session = _sessionService.Get();
        if (session is null || string.IsNullOrWhiteSpace(session.Username)) return null;

        var env = MoveItEnvironmentKey.Normalize(session.GetEnvironmentKey());
        var usernameKey = NormalizeUsername(session.Username);
        var normalizedGridKey = NormalizeGridKey(gridKey);

        UserGridPreferenceEntity? row;
        try
        {
            row = await _db.UserGridPreferences
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.EnvironmentKey == env
                    && item.UsernameKey == usernameKey
                    && item.GridKey == normalizedGridKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to load MAUI grid layout preference {GridKey} for {Username} in environment {EnvironmentKey}; using grid defaults.", normalizedGridKey, session.Username, env);
            return null;
        }

        if (row is null) return null;

        try
        {
            var layout = JsonSerializer.Deserialize<UserGridLayoutState>(row.LayoutJson, GridLayoutJsonOptions);
            if (layout is null || layout.SchemaVersion != UserGridLayoutSchema.CurrentVersion)
            {
                _logger.LogWarning("Ignoring unsupported MAUI grid layout preference {GridKey} for {Username} in environment {EnvironmentKey}.", normalizedGridKey, session.Username, env);
                return null;
            }

            return new UserGridLayoutSnapshot(row.GridKey, layout, row.CreatedUtc, row.ModifiedUtc);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Ignoring damaged MAUI grid layout preference {GridKey} for {Username} in environment {EnvironmentKey}.", normalizedGridKey, session.Username, env);
            return null;
        }
    }

    // Purpose: Saves one grid column layout for the current user and environment.
    public async Task<UserGridLayoutSnapshot> SaveCurrentGridLayoutAsync(
        string gridKey,
        UserGridLayoutState layout,
        CancellationToken cancellationToken = default)
    {
        var session = RequireSession();
        var env = MoveItEnvironmentKey.Normalize(session.GetEnvironmentKey());
        var cleanUsername = session.Username.Trim();
        var usernameKey = NormalizeUsername(cleanUsername);
        var normalizedGridKey = NormalizeGridKey(gridKey);
        var normalizedLayout = ValidateGridLayout(layout);
        var layoutJson = JsonSerializer.Serialize(normalizedLayout, GridLayoutJsonOptions);
        using var preferenceWriteLease = await AcquirePreferenceWriteLockAsync(env, usernameKey, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var row = await _db.UserGridPreferences
            .SingleOrDefaultAsync(item => item.EnvironmentKey == env
                && item.UsernameKey == usernameKey
                && item.GridKey == normalizedGridKey, cancellationToken);

        if (row is null)
        {
            row = new UserGridPreferenceEntity
            {
                EnvironmentKey = env,
                UsernameKey = usernameKey,
                Username = cleanUsername,
                GridKey = normalizedGridKey,
                LayoutJson = layoutJson,
                CreatedUtc = now,
                ModifiedUtc = now
            };
            _db.UserGridPreferences.Add(row);
        }
        else
        {
            row.Username = cleanUsername;
            row.LayoutJson = layoutJson;
            row.ModifiedUtc = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new UserGridLayoutSnapshot(row.GridKey, normalizedLayout, row.CreatedUtc, row.ModifiedUtc);
    }

    // Purpose: Removes one persisted grid column layout so configuration defaults apply again.
    public async Task DeleteCurrentGridLayoutAsync(string gridKey, CancellationToken cancellationToken = default)
    {
        var session = RequireSession();
        var env = MoveItEnvironmentKey.Normalize(session.GetEnvironmentKey());
        var usernameKey = NormalizeUsername(session.Username);
        var normalizedGridKey = NormalizeGridKey(gridKey);

        using var preferenceWriteLease = await AcquirePreferenceWriteLockAsync(env, usernameKey, cancellationToken);
        var row = await _db.UserGridPreferences
            .SingleOrDefaultAsync(item => item.EnvironmentKey == env
                && item.UsernameKey == usernameKey
                && item.GridKey == normalizedGridKey, cancellationToken);
        if (row is null) return;

        _db.UserGridPreferences.Remove(row);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private const string TraditionalTaskEditorPreferenceKey = "editor.traditional-task";
    private const string HostEditorPreferenceKey = "editor.host";
    public Task<TraditionalTaskEditorPreferences> GetCurrentTraditionalTaskEditorPreferencesAsync(CancellationToken cancellationToken = default)
        => GetCurrentEditorPreferencesAsync(TraditionalTaskEditorPreferenceKey, new TraditionalTaskEditorPreferences(false), cancellationToken);
    public Task<TraditionalTaskEditorPreferences> SaveCurrentTraditionalTaskEditorPreferencesAsync(TraditionalTaskEditorPreferences preferences, CancellationToken cancellationToken = default)
        => SaveCurrentEditorPreferencesAsync(TraditionalTaskEditorPreferenceKey, preferences, cancellationToken);
    public Task<HostEditorPreferences> GetCurrentHostEditorPreferencesAsync(CancellationToken cancellationToken = default)
        => GetCurrentEditorPreferencesAsync(HostEditorPreferenceKey, new HostEditorPreferences(false), cancellationToken);
    public Task<HostEditorPreferences> SaveCurrentHostEditorPreferencesAsync(HostEditorPreferences preferences, CancellationToken cancellationToken = default)
        => SaveCurrentEditorPreferencesAsync(HostEditorPreferenceKey, preferences, cancellationToken);

    private const string DateListEditorPreferenceKey = "editor.date-list";
    public Task<DateListEditorPreferences> GetCurrentDateListEditorPreferencesAsync(CancellationToken cancellationToken = default)
        => GetCurrentEditorPreferencesAsync(DateListEditorPreferenceKey, new DateListEditorPreferences(false), cancellationToken);
    public Task<DateListEditorPreferences> SaveCurrentDateListEditorPreferencesAsync(DateListEditorPreferences preferences, CancellationToken cancellationToken = default)
        => SaveCurrentEditorPreferencesAsync(DateListEditorPreferenceKey, preferences, cancellationToken);

    private const string ScriptEditorPreferenceKey = "editor.script";
    public Task<ScriptEditorPreferences> GetCurrentScriptEditorPreferencesAsync(CancellationToken cancellationToken = default)
        => GetCurrentEditorPreferencesAsync(ScriptEditorPreferenceKey, new ScriptEditorPreferences(false), cancellationToken);
    public Task<ScriptEditorPreferences> SaveCurrentScriptEditorPreferencesAsync(ScriptEditorPreferences preferences, CancellationToken cancellationToken = default)
        => SaveCurrentEditorPreferencesAsync(ScriptEditorPreferenceKey, preferences, cancellationToken);

    private async Task<T> GetCurrentEditorPreferencesAsync<T>(string key, T fallback, CancellationToken cancellationToken) where T : class
    {
        var session = _sessionService.Get();
        if (session is null || string.IsNullOrWhiteSpace(session.Username)) return fallback;
        var env = MoveItEnvironmentKey.Normalize(session.GetEnvironmentKey());
        var usernameKey = NormalizeUsername(session.Username);
        try
        {
            var row = await _db.UserGridPreferences.AsNoTracking().SingleOrDefaultAsync(item => item.EnvironmentKey == env
                && item.UsernameKey == usernameKey && item.GridKey == key, cancellationToken);
            if (row is null) return fallback;
            return JsonSerializer.Deserialize<T>(row.LayoutJson, GridLayoutJsonOptions)
                ?? fallback;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to load editor preferences for {Username}; using defaults.", session.Username);
            return fallback;
        }
    }

    private async Task<T> SaveCurrentEditorPreferencesAsync<T>(string key, T preferences, CancellationToken cancellationToken) where T : class
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var session = RequireSession();
        var env = MoveItEnvironmentKey.Normalize(session.GetEnvironmentKey());
        var username = session.Username.Trim();
        var usernameKey = NormalizeUsername(username);
        var json = JsonSerializer.Serialize(preferences, GridLayoutJsonOptions);
        using var preferenceWriteLease = await AcquirePreferenceWriteLockAsync(env, usernameKey, cancellationToken);
        var row = await _db.UserGridPreferences.SingleOrDefaultAsync(item => item.EnvironmentKey == env
            && item.UsernameKey == usernameKey && item.GridKey == key, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (row is null)
        {
            row = new UserGridPreferenceEntity
            {
                EnvironmentKey = env,
                UsernameKey = usernameKey,
                Username = username,
                GridKey = key,
                LayoutJson = json,
                CreatedUtc = now,
                ModifiedUtc = now
            };
            _db.UserGridPreferences.Add(row);
        }
        else
        {
            row.Username = username;
            row.LayoutJson = json;
            row.ModifiedUtc = now;
        }
        await _db.SaveChangesAsync(cancellationToken);
        return preferences;
    }

    // Purpose: Returns grid page size choices.
    public IReadOnlyList<int> GetGridPageSizeChoices()
    {
        var configured = _gridOptions.DefaultRowSize.PossibleValues
            .Where(value => value > 0)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();
        if (configured.Length > 0) return configured;
        return GridBehaviorOptions.SupportedDefaultRowSizes;
    }

    // Purpose: Serializes preference writes for one environment/user across concurrent HTTP requests.
    // Page-size and layout updates can originate from separate requests; serializing them prevents
    // intermittent SQLite write contention without blocking unrelated users or environments.
    private static ValueTask<IDisposable> AcquirePreferenceWriteLockAsync(
        string environmentKey,
        string usernameKey,
        CancellationToken cancellationToken)
        => PreferenceWriteLocks.AcquireAsync($"{environmentKey}\n{usernameKey}", cancellationToken);

    // Purpose: Returns the required session.
    private MoveItSessionState RequireSession()
        => _sessionService.Get() ?? throw new InvalidOperationException("MOVEit session expired. Please sign in again.");

    // Purpose: Validates theme.
    private string ValidateTheme(string? value)
    {
        var candidate = value?.Trim() ?? string.Empty;
        var allowed = _options.Theme.PossibleValues.Count > 0
            ? _options.Theme.PossibleValues.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
            : new[] { UserPreferenceThemes.Light, UserPreferenceThemes.Dark };
        var matched = allowed.FirstOrDefault(item => item.Equals(candidate, StringComparison.OrdinalIgnoreCase));
        if (matched is null)
            throw new ArgumentException("Theme is not valid.");
        return UserPreferenceThemes.Normalize(matched);
    }

    // Purpose: Validates start page.
    private string ValidateStartPage(string? value)
    {
        var candidate = value?.Trim() ?? string.Empty;
        var allowed = _options.StartPage.PossibleValues.Count > 0
            ? _options.StartPage.PossibleValues.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
            : UserPreferenceStartPages.Allowed;
        var matched = allowed.FirstOrDefault(item => item.Equals(candidate, StringComparison.OrdinalIgnoreCase));
        if (matched is null)
            throw new ArgumentException("Start Page is not valid.");
        return UserPreferenceStartPages.Normalize(matched);
    }

    // Purpose: Validates grid page size.
    private int ValidateGridPageSize(int value)
    {
        var choices = GetGridPageSizeChoices();
        if (!choices.Contains(value)) throw new ArgumentException("Grid Page Size is not valid.");
        return value;
    }

    // Purpose: Normalizes theme.
    private string NormalizeTheme(string? value)
    {
        var candidate = UserPreferenceThemes.Normalize(value);
        var configured = _options.Theme.PossibleValues.Count > 0
            ? _options.Theme.PossibleValues.Select(UserPreferenceThemes.Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(new[] { UserPreferenceThemes.Light, UserPreferenceThemes.Dark }, StringComparer.OrdinalIgnoreCase);
        return configured.Contains(candidate) ? candidate : UserPreferenceThemes.Light;
    }

    // Purpose: Normalizes start page.
    private string NormalizeStartPage(string? value)
    {
        var candidate = UserPreferenceStartPages.Normalize(value);
        var configured = _options.StartPage.PossibleValues.Count > 0
            ? _options.StartPage.PossibleValues.Select(UserPreferenceStartPages.Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : UserPreferenceStartPages.Allowed.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return configured.Contains(candidate) ? candidate : UserPreferenceStartPages.Dashboard;
    }

    // Purpose: Normalizes grid page size.
    private int NormalizeGridPageSize(int value)
    {
        var choices = GetGridPageSizeChoices();
        if (choices.Contains(value)) return value;
        if (choices.Contains(_gridOptions.DefaultRowSize.Value)) return _gridOptions.DefaultRowSize.Value;
        return choices.FirstOrDefault(50);
    }

    // Purpose: Validates and normalizes one persisted grid column layout.
    private static UserGridLayoutState ValidateGridLayout(UserGridLayoutState? layout)
    {
        if (layout is null) throw new ArgumentException("Grid layout is required.");
        if (layout.SchemaVersion != UserGridLayoutSchema.CurrentVersion)
            throw new ArgumentException("Grid layout schema is not supported.");

        var order = NormalizeColumnKeys(layout.ColumnOrder, "column order", requireAtLeastOne: true);
        var orderSet = order.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var visible = NormalizeColumnKeys(layout.VisibleColumns, "visible columns", requireAtLeastOne: true);
        if (visible.Any(key => !orderSet.Contains(key)))
            throw new ArgumentException("Visible grid columns must be present in the column order.");

        var widths = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in layout.Widths ?? new Dictionary<string, double>())
        {
            var key = NormalizeColumnKey(pair.Key);
            if (!orderSet.Contains(key)) throw new ArgumentException($"Grid width column '{key}' is not present in the column order.");
            if (!double.IsFinite(pair.Value) || pair.Value <= 0 || pair.Value > 4000)
                throw new ArgumentException($"Grid width for '{key}' is outside the supported range.");
            widths[key] = Math.Round(pair.Value, 2);
        }

        return new UserGridLayoutState
        {
            SchemaVersion = UserGridLayoutSchema.CurrentVersion,
            VisibleColumns = visible,
            ColumnOrder = order,
            Widths = widths
        };
    }

    // Purpose: Validates one list of persisted grid column identifiers.
    private static List<string> NormalizeColumnKeys(IEnumerable<string>? values, string label, bool requireAtLeastOne)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in values ?? Array.Empty<string>())
        {
            var key = NormalizeColumnKey(raw);
            if (!seen.Add(key)) throw new ArgumentException($"Grid {label} contains duplicate column '{key}'.");
            result.Add(key);
            if (result.Count > 200) throw new ArgumentException($"Grid {label} contains too many columns.");
        }

        if (requireAtLeastOne && result.Count == 0) throw new ArgumentException($"Grid {label} must contain at least one column.");
        return result;
    }

    // Purpose: Validates one persisted grid column identifier.
    private static string NormalizeColumnKey(string? value)
    {
        var key = value?.Trim() ?? string.Empty;
        if (key.Length == 0 || key.Length > 128) throw new ArgumentException("Grid column identifiers must contain between 1 and 128 characters.");
        return key;
    }

    // Purpose: Normalizes a grid identifier for environment/user-scoped preference storage.
    private static string NormalizeGridKey(string? gridKey)
    {
        var value = gridKey?.Trim() ?? string.Empty;
        if (value.Length == 0 || value.Length > 64) throw new ArgumentException("Grid identifier must contain between 1 and 64 characters.", nameof(gridKey));
        return value.ToLowerInvariant();
    }

    // Purpose: Normalizes username.
    private static string NormalizeUsername(string? username)
    {
        var value = username?.Trim() ?? string.Empty;
        if (value.Length == 0) throw new ArgumentException("Username is required.", nameof(username));
        return value.ToUpperInvariant();
    }
}

