// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Data;
using Microsoft.EntityFrameworkCore;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.Configuration;

namespace MoveItAutomationUI.Services.Security;

public interface IMauiSecurityService
{
    // Purpose: Returns MAUI authorization users from the metadata database.
    IReadOnlyList<MauiSecurityUser> GetUsers();
    // Purpose: Returns whether the supplied username currently has the MAUI Admin role.
    bool IsAdmin(string? username);
    // Purpose: Returns the current database-backed MAUI role for a username.
    string GetRole(string? username);
    // Purpose: Records a successful MOVEit login and enrolls a previously unseen MAUI user.
    Task<MauiSecurityUser> RecordSuccessfulLoginAsync(string username, CancellationToken cancellationToken = default);
    // Purpose: Persists administrator-edited MAUI authorization users while preserving concurrent first-login enrollment.
    Task SaveUsersAsync(
        IReadOnlyList<ConfigurationSecurityUserSaveRequest> requestedUsers,
        IReadOnlyCollection<string> originalUsernames,
        string modifiedBy,
        CancellationToken cancellationToken = default);
}

public sealed class MauiSecurityService : IMauiSecurityService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MauiSetupState _setupState;
    private readonly ILogger<MauiSecurityService> _logger;

    // Purpose: Initializes database-backed MAUI authorization services. Security data is unavailable until
    // Initial Setup has provisioned the metadata schema and activated the configuration in-process.
    public MauiSecurityService(
        IServiceScopeFactory scopeFactory,
        MauiSetupState setupState,
        ILogger<MauiSecurityService> logger)
    {
        _scopeFactory = scopeFactory;
        _setupState = setupState;
        _logger = logger;
    }

    // Purpose: Returns the current MAUI authorization list directly from the database.
    public IReadOnlyList<MauiSecurityUser> GetUsers()
    {
        // A stale authentication cookie can still be presented while a brand-new installation is on /Setup.
        // Never touch SecurityUsers until Initial Setup has provisioned and activated the database.
        if (!_setupState.IsConfiguredAtStartup) return Array.Empty<MauiSecurityUser>();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();
        return db.SecurityUsers
            .AsNoTracking()
            .OrderBy(user => user.Username)
            .ToArray()
            .Select(ToModel)
            .ToArray();
    }

    // Purpose: Returns whether the supplied username currently has the MAUI Admin role.
    public bool IsAdmin(string? username) =>
        _setupState.IsConfiguredAtStartup
        && string.Equals(GetRole(username), MauiSecurityRoles.Admin, StringComparison.OrdinalIgnoreCase);

    // Purpose: Resolves authorization from SecurityUsers. Unknown users have User privileges until enrolled.
    public string GetRole(string? username)
    {
        if (!_setupState.IsConfiguredAtStartup) return MauiSecurityRoles.User;

        var usernameKey = MauiUsername.GetKey(username);
        if (usernameKey.Length == 0) return MauiSecurityRoles.User;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();
        var role = MatchingUsers(db, usernameKey)
            .AsNoTracking()
            .Select(user => user.Role)
            .FirstOrDefault();
        return MauiSecurityRoles.Normalize(role);
    }

    // Purpose: Records LastLoginUtc and enrolls a new user. The first MAUI user is the bootstrap Admin;
    // every later unseen MOVEit user starts as User. MOVEit administrator status does not alter MAUI roles.
    public async Task<MauiSecurityUser> RecordSuccessfulLoginAsync(string username, CancellationToken cancellationToken = default)
    {
        EnsureSecurityDatabaseAvailable();
        username = MauiUsername.Normalize(username);
        if (username.Length == 0) throw new ArgumentException("A MOVEit username is required to register MAUI security access.", nameof(username));
        if (username.Length > MauiSecurityUser.UsernameMaxLength)
            throw new ArgumentException($"MOVEit usernames used by MAUI Security cannot exceed {MauiSecurityUser.UsernameMaxLength} characters.", nameof(username));

        var usernameKey = MauiUsername.GetKey(username);
        var now = DateTimeOffset.UtcNow;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var entity = await MatchingUsers(db, usernameKey).FirstOrDefaultAsync(cancellationToken);
        var created = false;
        var initialAdmin = false;
        if (entity is null)
        {
            initialAdmin = !await db.SecurityUsers.AnyAsync(cancellationToken);
            created = true;
            entity = new SecurityUserEntity
            {
                UsernameKey = usernameKey,
                Username = username,
                Role = initialAdmin ? MauiSecurityRoles.Admin : MauiSecurityRoles.User,
                LastLoginUtc = now,
                CreatedUtc = now,
                CreatedBy = username,
                ModifiedUtc = now,
                ModifiedBy = username
            };
            db.SecurityUsers.Add(entity);
        }
        else
        {
            entity.Username = username;
            entity.Role = MauiSecurityRoles.Normalize(entity.Role);
            entity.LastLoginUtc = now;
            entity.ModifiedUtc = now;
            entity.ModifiedBy = username;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (created)
        {
            _logger.LogInformation(
                initialAdmin
                    ? "Registered first MAUI user {Username} as Admin."
                    : "Automatically registered MOVEit user {Username} in MAUI with User role.",
                username);
        }
        _logger.LogInformation("Recorded successful MAUI login for {Username}; role {Role}.", entity.Username, entity.Role);
        return ToModel(entity);
    }

    // Purpose: Applies the Security editor to the database without allowing a stale Settings page to remove
    // users that were automatically enrolled after that page was opened. At least one Admin must always remain.
    public async Task SaveUsersAsync(
        IReadOnlyList<ConfigurationSecurityUserSaveRequest> requestedUsers,
        IReadOnlyCollection<string> originalUsernames,
        string modifiedBy,
        CancellationToken cancellationToken = default)
    {
        EnsureSecurityDatabaseAvailable();
        ArgumentNullException.ThrowIfNull(requestedUsers);
        ArgumentNullException.ThrowIfNull(originalUsernames);
        modifiedBy = string.IsNullOrWhiteSpace(modifiedBy) ? "unknown" : MauiUsername.Normalize(modifiedBy);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var currentUsers = await db.SecurityUsers.ToListAsync(cancellationToken);
        var currentByKey = currentUsers.ToDictionary(user => user.UsernameKey, StringComparer.OrdinalIgnoreCase);
        var originalKeys = originalUsernames
            .Select(MauiUsername.GetKey)
            .Where(key => key.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var requestedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var retainedUsers = new HashSet<SecurityUserEntity>();
        var now = DateTimeOffset.UtcNow;

        foreach (var requested in requestedUsers)
        {
            var username = MauiUsername.Normalize(requested.Username);
            if (username.Length == 0) throw new ArgumentException("Every MAUI security entry requires a MOVEit Username.");
            if (username.Length > MauiSecurityUser.UsernameMaxLength)
                throw new ArgumentException($"MOVEit usernames used by MAUI Security cannot exceed {MauiSecurityUser.UsernameMaxLength} characters.");
            var usernameKey = MauiUsername.GetKey(username);
            if (!requestedKeys.Add(usernameKey))
                throw new ArgumentException($"MOVEit username '{username}' is duplicated in Security.");
            if (!MauiSecurityRoles.IsValid(requested.Role))
                throw new ArgumentException($"Security role '{requested.Role}' is not valid. Use User or Admin.");

            if (currentByKey.TryGetValue(usernameKey, out var existing)
                || currentByKey.TryGetValue(usernameKey.Replace('\\', '/'), out existing))
            {
                retainedUsers.Add(existing);
                existing.Username = username;
                existing.Role = MauiSecurityRoles.Normalize(requested.Role);
                existing.ModifiedUtc = now;
                existing.ModifiedBy = modifiedBy;
            }
            else
            {
                var added = new SecurityUserEntity
                {
                    UsernameKey = usernameKey,
                    Username = username,
                    Role = MauiSecurityRoles.Normalize(requested.Role),
                    CreatedUtc = now,
                    CreatedBy = modifiedBy,
                    ModifiedUtc = now,
                    ModifiedBy = modifiedBy
                };
                db.SecurityUsers.Add(added);
                currentUsers.Add(added);
                retainedUsers.Add(added);
                currentByKey[usernameKey] = added;
            }
        }

        // A row present when the page opened but omitted from the submitted editor was deliberately removed.
        // Rows enrolled after the page opened are preserved. An administrator can also explicitly remove
        // a legacy slash alias while retaining the canonical account; login itself never deletes accounts.
        foreach (var current in currentUsers.ToArray())
        {
            if (originalKeys.Contains(MauiUsername.GetKey(current.UsernameKey)) && !retainedUsers.Contains(current))
            {
                db.SecurityUsers.Remove(current);
                currentUsers.Remove(current);
            }
        }

        var finalUsers = currentUsers
            .Where(user => db.Entry(user).State != EntityState.Deleted)
            .ToArray();
        if (finalUsers.Length == 0 || !finalUsers.Any(user => MauiSecurityRoles.Normalize(user.Role) == MauiSecurityRoles.Admin))
        {
            throw new ArgumentException("At least one MAUI Admin must remain configured.");
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _logger.LogInformation("MAUI Security users were updated by {Username}; {UserCount} users configured.", modifiedBy, finalUsers.Length);
    }


    // Purpose: Prevents database-backed MAUI authorization from being used before Initial Setup has created
    // the metadata schema. Normal application routes cannot reach this state, but the explicit guard keeps
    // lifecycle/public pages and future callers from accidentally querying an unprovisioned database.
    private void EnsureSecurityDatabaseAvailable()
    {
        if (!_setupState.IsConfiguredAtStartup)
        {
            throw new InvalidOperationException(
                "MAUI Security is unavailable until Initial Setup has completed and the application has restarted.");
        }
    }

    // Recognize keys written before separator normalization without changing primary keys or assigned roles.
    // If both forms already exist, the canonical backslash account wins; never combine/elevate roles.
    private static IOrderedQueryable<SecurityUserEntity> MatchingUsers(MetadataDbContext db, string usernameKey)
    {
        var legacyKey = usernameKey.Replace('\\', '/');
        return db.SecurityUsers.Where(user => user.UsernameKey == usernameKey || user.UsernameKey == legacyKey)
            .OrderBy(user => user.UsernameKey == usernameKey ? 0 : 1);
    }

    // Purpose: Maps the persistence entity to the public security model.
    private static MauiSecurityUser ToModel(SecurityUserEntity entity) => new()
    {
        Username = entity.Username,
        Role = MauiSecurityRoles.Normalize(entity.Role),
        LastLoginUtc = entity.LastLoginUtc
    };
}
