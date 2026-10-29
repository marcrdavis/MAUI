using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Configuration;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.Security;

namespace MoveItAutomationUI.Tests.Security;

public sealed class MauiUsernameTests
{
    [Theory]
    [InlineData(null, "", "")]
    [InlineData("   ", "", "")]
    [InlineData(" DOMAIN/Username ", "DOMAIN\\Username", "DOMAIN\\USERNAME")]
    [InlineData("domain\\username", "domain\\username", "DOMAIN\\USERNAME")]
    [InlineData(" LocalUser ", "LocalUser", "LOCALUSER")]
    [InlineData(" User@Example.com ", "User@Example.com", "USER@EXAMPLE.COM")]
    public void NormalizePreservesCaseAndAccountFormButUsesBackslash(string? input, string expected, string key)
    {
        Assert.Equal(expected, MauiUsername.Normalize(input));
        Assert.Equal(key, MauiUsername.GetKey(input));
    }

    [Fact]
    public void KeyIsIndependentOfCurrentCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.Equal("DOMAIN\\MIADMIN", MauiUsername.GetKey("domain/miadmin"));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Fact]
    public async Task LoginWithEitherSeparatorAndDifferentCaseReusesOneAccount()
    {
        await using var f = await Fixture.CreateAsync();
        var first = await f.Security.RecordSuccessfulLoginAsync(" DOMAIN/User ");
        Assert.Equal(MauiSecurityRoles.Admin, first.Role);
        var second = await f.Security.RecordSuccessfulLoginAsync("domain\\USER");
        Assert.Equal(MauiSecurityRoles.Admin, second.Role);
        Assert.True(await f.Db.SecurityUsers.CountAsync() == 1);
        var stored = await f.Db.SecurityUsers.SingleAsync();
        Assert.Equal("DOMAIN\\USER", stored.UsernameKey);
        Assert.Equal("domain\\USER", stored.Username);
        Assert.NotNull(stored.LastLoginUtc);
        Assert.True(f.Security.IsAdmin("DoMaIn/uSeR"));
        Assert.False(f.Security.IsAdmin("otherdomain/User"));
        Assert.False(f.Security.IsAdmin("User"));
    }

    [Fact]
    public async Task ExistingSlashAccountRetainsItsRoleKeyAndCreationHistory()
    {
        await using var f = await Fixture.CreateAsync();
        var created = DateTimeOffset.UtcNow.AddDays(-10);
        f.Db.SecurityUsers.Add(new SecurityUserEntity { UsernameKey = "DOMAIN/USER", Username = "DOMAIN/User",
            Role = MauiSecurityRoles.Admin, CreatedUtc = created, CreatedBy = "original-admin" });
        await f.Db.SaveChangesAsync();
        f.Db.ChangeTracker.Clear();
        Assert.True(f.Security.IsAdmin("domain\\user")); // Upgrade authorization does not enroll/mutate.
        Assert.Null((await f.Db.SecurityUsers.SingleAsync()).LastLoginUtc);
        f.Db.ChangeTracker.Clear();
        var login = await f.Security.RecordSuccessfulLoginAsync("domain/user");
        Assert.Equal(MauiSecurityRoles.Admin, login.Role);
        Assert.True(await f.Db.SecurityUsers.CountAsync() == 1);
        var stored = await f.Db.SecurityUsers.SingleAsync();
        Assert.Equal("DOMAIN/USER", stored.UsernameKey); // No primary-key rewrite/migration.
        Assert.Equal("domain\\user", stored.Username);
        Assert.Equal(created, stored.CreatedUtc);
        Assert.Equal("original-admin", stored.CreatedBy);
    }

    [Fact]
    public async Task LaterUsersAreNotPromotedAndDifferentDomainsRemainSeparate()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Security.RecordSuccessfulLoginAsync("bootstrap");
        await f.Security.RecordSuccessfulLoginAsync("DOMAIN/User");
        var login = await f.Security.RecordSuccessfulLoginAsync("domain\\USER");
        await f.Security.RecordSuccessfulLoginAsync("OTHERDOMAIN/User");
        Assert.Equal(MauiSecurityRoles.User, login.Role);
        Assert.True(await f.Db.SecurityUsers.CountAsync() == 3);
        Assert.False(f.Security.IsAdmin("domain/user"));
    }

    [Fact]
    public async Task ExistingCanonicalAccountWinsWithoutCombiningRolesOrDeletingAliasesOnLogin()
    {
        await using var f = await Fixture.CreateAsync();
        f.Db.SecurityUsers.AddRange(
            new SecurityUserEntity { UsernameKey = "DOMAIN\\USER", Username = "DOMAIN\\User", Role = MauiSecurityRoles.User },
            new SecurityUserEntity { UsernameKey = "DOMAIN/USER", Username = "DOMAIN/User", Role = MauiSecurityRoles.Admin });
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        var login = await f.Security.RecordSuccessfulLoginAsync("domain/user");
        Assert.Equal(MauiSecurityRoles.User, login.Role);
        Assert.False(f.Security.IsAdmin("domain\\user"));
        Assert.True(await f.Db.SecurityUsers.CountAsync() == 2);
    }

    [Fact]
    public async Task SecuritySaveRecognizesLegacyAccountAndPreservesConcurrentEnrollments()
    {
        await using var f = await Fixture.CreateAsync();
        f.Db.SecurityUsers.Add(new SecurityUserEntity { UsernameKey = "DOMAIN/USER", Username = "DOMAIN/User", Role = MauiSecurityRoles.Admin });
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        await f.Security.RecordSuccessfulLoginAsync("new-user"); // Not present when Settings opened.
        await f.Security.SaveUsersAsync(new[] { Entry(" domain\\USER ", MauiSecurityRoles.Admin) },
            new[] { "DOMAIN/User" }, "DOMAIN/Admin");
        Assert.True(await f.Db.SecurityUsers.CountAsync() == 2);
        var stored = await f.Db.SecurityUsers.SingleAsync(user => user.UsernameKey == "DOMAIN/USER");
        Assert.Equal("domain\\USER", stored.Username);
        Assert.Equal("DOMAIN\\Admin", stored.ModifiedBy);
        Assert.True(f.Security.IsAdmin("domain/user"));
    }

    [Fact]
    public async Task SecuritySaveRejectsCaseAndSeparatorDuplicatesWithoutPartialChanges()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Security.RecordSuccessfulLoginAsync("DOMAIN/User");
        await Assert.ThrowsAsync<ArgumentException>(() => f.Security.SaveUsersAsync(new[] {
            Entry("domain\\USER", MauiSecurityRoles.User), Entry("DOMAIN/user", MauiSecurityRoles.Admin) },
            new[] { "DOMAIN\\User" }, "admin"));
        Assert.True(await f.Db.SecurityUsers.CountAsync() == 1);
        Assert.True(f.Security.IsAdmin("DOMAIN/user"));
    }

    [Fact]
    public async Task AdministratorCanRemoveAnExistingDuplicateThroughSecuritySave()
    {
        await using var f = await Fixture.CreateAsync();
        f.Db.SecurityUsers.AddRange(
            new SecurityUserEntity { UsernameKey = "DOMAIN\\USER", Username = "DOMAIN\\User", Role = MauiSecurityRoles.Admin },
            new SecurityUserEntity { UsernameKey = "DOMAIN/USER", Username = "DOMAIN/User", Role = MauiSecurityRoles.User });
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        await f.Security.SaveUsersAsync(new[] { Entry("domain\\User", MauiSecurityRoles.Admin) },
            new[] { "DOMAIN\\User", "DOMAIN/User" }, "admin");
        Assert.Equal("DOMAIN\\USER", (await f.Db.SecurityUsers.SingleAsync()).UsernameKey);
        Assert.True(f.Security.IsAdmin("domain/user"));
    }

    [Fact]
    public async Task SecuritySaveCannotRemoveTheLastAdministratorThroughAnAlias()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Security.RecordSuccessfulLoginAsync("DOMAIN/User");
        await Assert.ThrowsAsync<ArgumentException>(() => f.Security.SaveUsersAsync(
            new[] { Entry("domain/user", MauiSecurityRoles.User) }, new[] { "DOMAIN\\User" }, "admin"));
        Assert.True(f.Security.IsAdmin("domain\\USER"));
        Assert.True(await f.Db.SecurityUsers.CountAsync() == 1);
    }

    private static ConfigurationSecurityUserSaveRequest Entry(string username, string role) => new() { Username = username, Role = role };

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _services;
        public MetadataDbContext Db { get; }
        public MauiSecurityService Security { get; }
        private Fixture(SqliteConnection connection, ServiceProvider services)
        {
            _connection = connection; _services = services;
            Db = new MetadataDbContext(new DbContextOptionsBuilder<MetadataDbContext>().UseSqlite(connection).Options);
            var setup = new MauiSetupState(); setup.MarkConfigured();
            Security = new MauiSecurityService(services.GetRequiredService<IServiceScopeFactory>(), setup, NullLogger<MauiSecurityService>.Instance);
        }
        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var services = new ServiceCollection().AddDbContext<MetadataDbContext>(options => options.UseSqlite(connection)).BuildServiceProvider();
            var fixture = new Fixture(connection, services); await fixture.Db.Database.EnsureCreatedAsync(); return fixture;
        }
        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync(); await _services.DisposeAsync(); await _connection.DisposeAsync();
        }
    }
}
