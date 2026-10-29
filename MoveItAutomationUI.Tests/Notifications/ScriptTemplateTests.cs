using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Tests.MoveIt;

namespace MoveItAutomationUI.Tests.Notifications;

public sealed class ScriptTemplateTests
{
    [Fact]
    public async Task DefaultsSeedOncePerEnvironmentAndDeletedTemplatesStayDeleted()
    {
        await using var f = await Fixture.Create();
        var defaults = await f.Service.GetAsync(); Assert.Equal(4, defaults.Count);
        Assert.Equal(new[] { "Download Basic Auth", "Get JSON Next ID", "MOVEit Transfer REST API", "Process File Line by Line" }, defaults.Select(x => x.Name));
        Assert.All(defaults, item => Assert.Contains("$miaclient", item.Source));
        foreach (var item in defaults) await f.Service.DeleteAsync(item.Id);
        Assert.Empty(await f.Service.GetAsync());
        f.State.EnvironmentKey = "QA"; Assert.Equal(4, (await f.Service.GetAsync()).Count);
        f.State.EnvironmentKey = "PROD"; Assert.Empty(await f.Service.GetAsync());
    }
    [Fact]
    public async Task DuplicateNamesRequireExplicitOverwriteAndPreserveExactSource()
    {
        await using var f = await Fixture.Create();
        var original = await f.Service.SaveAsync(new("Custom", "$x = 'literal'\r\n"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.SaveAsync(new(" custom ", "other")));
        var updated = await f.Service.SaveAsync(new(" CUSTOM ", "# exact\r\n\t", true));
        Assert.Equal(original.Id, updated.Id); Assert.Equal("# exact\r\n\t", updated.Source);
        var renamed = await f.Service.SaveAsync(new("Renamed", updated.Source, TemplateId: updated.Id));
        Assert.Equal(original.Id, renamed.Id); Assert.Equal("Renamed", renamed.Name);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.SaveAsync(new("Download Basic Auth", "bad", true, original.Id)));
    }
    [Fact]
    public async Task VbscriptAndInvalidNamesAreRejectedWithoutChangingTemplates()
    {
        await using var f = await Fixture.Create();
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.SaveAsync(new("VB", "Dim x", Language: "VBScript")));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.SaveAsync(new(" ", "source")));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.SaveAsync(new("Empty", " ")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.SaveAsync(new("Missing", "source", TemplateId: "missing")));
        Assert.Equal(4, (await f.Service.GetAsync()).Count);
    }
    private sealed class Fixture(SqliteConnection connection, MetadataDbContext db, MoveItSessionState state, ScriptTemplateService service) : IAsyncDisposable
    {
        public MoveItSessionState State { get; } = state;
        public ScriptTemplateService Service { get; } = service;
        public static async Task<Fixture> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var db = new MetadataDbContext(new DbContextOptionsBuilder<MetadataDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var state = new MoveItSessionState { EnvironmentKey = "PROD", ServerKey = "Scripts", Username = "tester" };
            var sessions = ScriptCrudTests.Stub<IMoveItSessionService>((_, _) => state);
            return new(connection, db, state, new(db, sessions));
        }
        public async ValueTask DisposeAsync() { await db.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
