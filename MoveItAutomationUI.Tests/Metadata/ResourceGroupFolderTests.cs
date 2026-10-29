using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.Metadata;

public sealed class ResourceGroupFolderTests
{
    [Fact]
    public async Task CreatesSortedRootFoldersWithoutChangingExistingFoldersOrAssignments()
    {
        await using var fixture = await Database.CreateAsync();
        var db = fixture.Context;
        var alpha = new MetadataFolderEntity { EnvironmentKey = "PROD", ObjectType = "Task", Name = "ALPHA" };
        db.MetadataFolders.Add(alpha);
        await db.SaveChangesAsync();
        var beta = new MetadataFolderEntity { EnvironmentKey = "PROD", ObjectType = "Task", Name = "Beta", ParentId = alpha.Id };
        db.MetadataFolders.AddRange(beta,
            new() { EnvironmentKey = "OTHER", ObjectType = "Task", Name = "Zulu" },
            new() { EnvironmentKey = "PROD", ObjectType = "Host", Name = "Charlie" });
        db.MetadataObjects.Add(new() { EnvironmentKey = "PROD", ObjectType = "Task", ObjectId = "1", FolderId = alpha.Id });
        await db.SaveChangesAsync();
        var repository = new MetadataRepository(db);

        var result = await repository.CreateMissingRootFoldersAsync("prod", "Task", ["Zulu", "beta", " alpha ", "Charlie", "charlie"]);
        Assert.Equal(new MetadataFolderCreationResult(2, 2), result);
        var added = await db.MetadataFolders.AsNoTracking().Where(f => f.EnvironmentKey == "PROD" && f.ObjectType == "Task" && f.Id != alpha.Id && f.Id != beta.Id).OrderBy(f => f.Id).ToListAsync();
        Assert.Equal(new[] { "Charlie", "Zulu" }, added.Select(f => f.Name).ToArray());
        Assert.All(added, folder => Assert.Null(folder.ParentId));
        var preserved = await db.MetadataFolders.AsNoTracking().FirstAsync(f => f.Id == beta.Id);
        Assert.Equal("Beta", preserved.Name);
        Assert.Equal(alpha.Id, preserved.ParentId);
        Assert.Equal(alpha.Id, (await db.MetadataObjects.AsNoTracking().FirstAsync()).FolderId);
        Assert.Equal(new MetadataFolderCreationResult(0, 4), await repository.CreateMissingRootFoldersAsync("PROD", "Task", ["alpha", "Beta", "Charlie", "ZULU"]));
        Assert.Equal("ALPHA", (await db.MetadataFolders.AsNoTracking().FirstAsync(f => f.Id == alpha.Id)).Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("too-long")]
    public async Task InvalidNamesDoNotLeavePartialFolders(string invalid)
    {
        await using var fixture = await Database.CreateAsync();
        var name = invalid.Length == 0 ? invalid : new string('x', 201);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new MetadataRepository(fixture.Context)
            .CreateMissingRootFoldersAsync("PROD", "Task", ["Valid", name]));
        Assert.False(await fixture.Context.MetadataFolders.AnyAsync());
        Assert.False(fixture.Context.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task DatabaseFailureRollsBackTheWholeBatch()
    {
        await using var fixture = await Database.CreateAsync();
        await fixture.Context.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_folder BEFORE INSERT ON MetadataFolders WHEN NEW.Name = 'Zulu' BEGIN SELECT RAISE(ABORT, 'fixture failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => new MetadataRepository(fixture.Context)
            .CreateMissingRootFoldersAsync("PROD", "Task", ["Alpha", "Zulu"]));
        fixture.Context.ChangeTracker.Clear();
        Assert.False(await fixture.Context.MetadataFolders.AnyAsync());
    }

    [Theory]
    [InlineData(null, 401)]
    [InlineData(false, 403)]
    public async Task UnauthorizedCallsCannotReadGroupsOrWriteFolders(bool? isAdmin, int expectedStatus)
    {
        var state = isAdmin.HasValue ? new MoveItSessionState { Info = new() { IsAdmin = isAdmin.Value } } : null;
        var groups = Stub<IResourceGroupLookupService>((_, _) => throw new Exception("No inventory access expected"));
        var metadata = Stub<IMetadataRepository>((_, _) => throw new Exception("No folder write expected"));
        var result = Assert.IsAssignableFrom<ObjectResult>(await Controller(state).CreateResourceGroupFolders(groups, metadata, default));
        Assert.Equal(expectedStatus, result.StatusCode);
    }

    [Fact]
    public async Task ActionUsesCurrentEnvironmentAndInventoryWithoutChangingConfiguration()
    {
        await using var fixture = await Database.CreateAsync();
        var reads = 0;
        var groups = Stub<IResourceGroupLookupService>((method, _) =>
        {
            Assert.Equal(nameof(IResourceGroupLookupService.GetVisibleNamesAsync), method.Name);
            reads++;
            return Task.FromResult<IReadOnlyList<string>>(["Zulu", "Alpha"]);
        });
        var controller = Controller(new() { EnvironmentKey = "prod", ServerKey = "other", Info = new() { IsAdmin = true } });
        var result = Assert.IsType<JsonResult>(await controller.CreateResourceGroupFolders(groups, new MetadataRepository(fixture.Context), default));
        var json = JsonSerializer.SerializeToElement(result.Value);
        Assert.True(json.GetProperty("success").GetBoolean());
        Assert.Equal(2, json.GetProperty("created").GetInt32());
        Assert.Equal(1, reads);
        Assert.All(await fixture.Context.MetadataFolders.ToListAsync(), folder => Assert.Equal("PROD", folder.EnvironmentKey));
    }

    [Fact]
    public async Task InventoryFailureDoesNotStartFolderCreation()
    {
        var groups = Stub<IResourceGroupLookupService>((_, _) => throw new MoveItApiException("Unable to read Resource Groups", System.Net.HttpStatusCode.BadGateway));
        var metadata = Stub<IMetadataRepository>((_, _) => throw new Exception("No folder write expected"));
        Assert.IsType<BadRequestObjectResult>(await Controller(new() { Info = new() { IsAdmin = true } }).CreateResourceGroupFolders(groups, metadata, default));
    }

    private static ConfigurationController Controller(MoveItSessionState? state) => new(
        null!, null!, Stub<IMoveItSessionService>((_, _) => state), null!, null!, null!, null!, null!, null!, null!,
        new Snapshot<UiOptions>(), new Snapshot<GridBehaviorOptions>(), NullLogger<ConfigurationController>.Instance)
        { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
    private sealed class Snapshot<T> : IOptionsSnapshot<T> where T : class, new()
    { public T Value { get; } = new(); public T Get(string? name) => Value; }
    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    { var proxy = DispatchProxy.Create<T, Proxy>(); ((Proxy)(object)proxy).Handler = handler; return proxy; }
    public class Proxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args ?? []);
    }
    private sealed class Database(SqliteConnection connection, MetadataDbContext context) : IAsyncDisposable
    {
        public MetadataDbContext Context { get; } = context;
        public static async Task<Database> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new MetadataDbContext(new DbContextOptionsBuilder<MetadataDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            return new(connection, context);
        }
        public async ValueTask DisposeAsync() { await Context.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
