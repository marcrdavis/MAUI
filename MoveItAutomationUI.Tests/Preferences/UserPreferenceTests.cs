using MoveItAutomationUI.Models;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.Preferences;

public sealed class UserPreferenceTests
{
    [Fact]
    public async Task HostAutoSavePersistsSeparatelyFromTasksAndOtherUsers()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new MetadataDbContext(new DbContextOptionsBuilder<MetadataDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var sessions = DispatchProxy.Create<IMoveItSessionService, SessionProxy>();
        var proxy = (SessionProxy)(object)sessions;
        proxy.State = new MoveItSessionState { Username = "Marc", EnvironmentKey = "test", ServerKey = "Automation" };
        var service = new UserPreferencesService(db, sessions, new Snapshot<UserPreferencesOptions>(new()),
            new Snapshot<GridBehaviorOptions>(new()), NullLogger<UserPreferencesService>.Instance);
        Assert.False((await service.GetCurrentHostEditorPreferencesAsync()).AutoSave);
        await service.SaveCurrentHostEditorPreferencesAsync(new(true));
        Assert.True((await service.GetCurrentHostEditorPreferencesAsync()).AutoSave);
        Assert.False((await service.GetCurrentTraditionalTaskEditorPreferencesAsync()).AutoSave);
        await service.SaveCurrentTraditionalTaskEditorPreferencesAsync(new(true));
        await service.SaveCurrentHostEditorPreferencesAsync(new(false));
        Assert.False((await service.GetCurrentHostEditorPreferencesAsync()).AutoSave);
        Assert.True((await service.GetCurrentTraditionalTaskEditorPreferencesAsync()).AutoSave);
        proxy.State.Username = "Other";
        Assert.False((await service.GetCurrentHostEditorPreferencesAsync()).AutoSave);
        Assert.False((await service.GetCurrentTraditionalTaskEditorPreferencesAsync()).AutoSave);
        Assert.Equal(2, await db.UserGridPreferences.CountAsync());
    }

    [Fact]
    public async Task ScriptAutoSaveIsIndependentAndScopedToUserAndEnvironment()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new MetadataDbContext(new DbContextOptionsBuilder<MetadataDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var sessions = DispatchProxy.Create<IMoveItSessionService, SessionProxy>();
        var proxy = (SessionProxy)(object)sessions;
        proxy.State = new MoveItSessionState { Username = "Marc", EnvironmentKey = "test", ServerKey = "Automation" };
        var service = new UserPreferencesService(db, sessions, new Snapshot<UserPreferencesOptions>(new()),
            new Snapshot<GridBehaviorOptions>(new()), NullLogger<UserPreferencesService>.Instance);
        Assert.False((await service.GetCurrentScriptEditorPreferencesAsync()).AutoSave);
        await service.SaveCurrentScriptEditorPreferencesAsync(new(true));
        Assert.True((await service.GetCurrentScriptEditorPreferencesAsync()).AutoSave);
        Assert.False((await service.GetCurrentHostEditorPreferencesAsync()).AutoSave);
        Assert.False((await service.GetCurrentTraditionalTaskEditorPreferencesAsync()).AutoSave);
        proxy.State.EnvironmentKey = "other-environment";
        Assert.False((await service.GetCurrentScriptEditorPreferencesAsync()).AutoSave);
        proxy.State.EnvironmentKey = "test";
        proxy.State.Username = "Other";
        Assert.False((await service.GetCurrentScriptEditorPreferencesAsync()).AutoSave);
        proxy.State.Username = "Marc";
        Assert.True((await service.GetCurrentScriptEditorPreferencesAsync()).AutoSave);
    }

    [Fact]
    public async Task DateListAutoSaveIsIndependentAndScopedToUserAndEnvironment()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new MetadataDbContext(new DbContextOptionsBuilder<MetadataDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var sessions = DispatchProxy.Create<IMoveItSessionService, SessionProxy>();
        var proxy = (SessionProxy)(object)sessions;
        proxy.State = new MoveItSessionState { Username = "Marc", EnvironmentKey = "test", ServerKey = "Automation" };
        var service = new UserPreferencesService(db, sessions, new Snapshot<UserPreferencesOptions>(new()),
            new Snapshot<GridBehaviorOptions>(new()), NullLogger<UserPreferencesService>.Instance);
        Assert.False((await service.GetCurrentDateListEditorPreferencesAsync()).AutoSave);
        await service.SaveCurrentDateListEditorPreferencesAsync(new(true));
        Assert.True((await service.GetCurrentDateListEditorPreferencesAsync()).AutoSave);
        Assert.False((await service.GetCurrentHostEditorPreferencesAsync()).AutoSave);
        Assert.False((await service.GetCurrentTraditionalTaskEditorPreferencesAsync()).AutoSave);
        proxy.State.EnvironmentKey = "other-environment";
        Assert.False((await service.GetCurrentDateListEditorPreferencesAsync()).AutoSave);
        proxy.State.EnvironmentKey = "test";
        proxy.State.Username = "Other";
        Assert.False((await service.GetCurrentDateListEditorPreferencesAsync()).AutoSave);
        proxy.State.Username = "Marc";
        Assert.True((await service.GetCurrentDateListEditorPreferencesAsync()).AutoSave);
    }

    public class SessionProxy : DispatchProxy
    {
        public MoveItSessionState State { get; set; } = new();
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod?.Name == nameof(IMoveItSessionService.Get) ? State : throw new NotSupportedException();
    }
    private sealed class Snapshot<T>(T value) : IOptionsSnapshot<T> where T : class
    {
        public T Value => value;
        public T Get(string? name) => value;
    }

    [Theory]
    [InlineData("dark", "dark")]
    [InlineData(" DARK ", "dark")]
    [InlineData("light", "light")]
    [InlineData("unknown", "light")]
    [InlineData(null, "light")]
    public void ThemeNormalize_ReturnsSupportedTheme(string? input, string expected)
        => Assert.Equal(expected, UserPreferenceThemes.Normalize(input));

    [Theory]
    [InlineData("Tasks", "Tasks")]
    [InlineData(" hosts ", "Hosts")]
    [InlineData("dashboard", "Dashboard")]
    [InlineData("other", "Dashboard")]
    [InlineData(null, "Dashboard")]
    public void StartPageNormalize_ReturnsSupportedPage(string? input, string expected)
        => Assert.Equal(expected, UserPreferenceStartPages.Normalize(input));

    [Theory]
    [InlineData("Tasks", "Tasks", "Index")]
    [InlineData("Hosts", "Hosts", "Index")]
    [InlineData("Dashboard", "Home", "Index")]
    [InlineData("bogus", "Home", "Index")]
    public void ResolveRoute_MapsStartPage(string input, string controller, string action)
    {
        var result = UserPreferenceStartPages.ResolveRoute(input);
        Assert.Equal(controller, result.Controller);
        Assert.Equal(action, result.Action);
    }

    [Theory]
    [InlineData(null, "/Home/Index")]
    [InlineData("Dashboard", "/Home/Index")]
    [InlineData("Tasks", "/Tasks/Index")]
    [InlineData("Hosts", "/Hosts/Index")]
    [InlineData("unexpected", "/Home/Index")]
    [InlineData("//untrusted.invalid", "/Home/Index")]
    public void ResolvePath_UsesOnlySupportedCanonicalDestinations(string? startPage, string expected)
        => Assert.Equal(expected, UserPreferenceStartPages.ResolvePath(startPage));
}
