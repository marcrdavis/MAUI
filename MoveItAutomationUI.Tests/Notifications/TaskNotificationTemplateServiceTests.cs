using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Tests.Notifications;

public sealed class TaskNotificationTemplateServiceTests
{
    [Fact]
    public async Task ConfigurationEdit_RenamesAndUpdatesContents_WithoutChangingIdentity()
    {
        await using var fixture = await TemplateFixture.CreateAsync("PROD");
        var original = await fixture.Service.SaveCurrentAsync(new TaskNotificationTemplateSaveRequest
            { Name = "Original", Subject = "Old", Message = "Old body", Format = "text" });
        var updated = await fixture.Service.SaveCurrentAsync(new TaskNotificationTemplateSaveRequest
            { TemplateId = original.Id, OverwriteExisting = true, Name = "Renamed", Subject = "New", Message = "<p>New</p>", Format = "html" });
        Assert.Equal(original.Id, updated.Id);
        var stored = Assert.Single(await fixture.Service.GetCurrentAsync());
        Assert.Equal("Renamed", stored.Name);
        Assert.Equal("New", stored.Subject);
        Assert.Equal("<p>New</p>", stored.Message);
        Assert.Equal("html", stored.Format);
    }

    [Fact]
    public async Task ConfigurationEdit_DuplicateName_DoesNotPartiallyUpdateContents()
    {
        await using var fixture = await TemplateFixture.CreateAsync("PROD");
        var original = await fixture.Service.SaveCurrentAsync(new TaskNotificationTemplateSaveRequest
            { Name = "Original", Subject = "Old", Message = "Old body" });
        await fixture.Service.SaveCurrentAsync(new TaskNotificationTemplateSaveRequest
            { Name = "Other", Subject = "Other", Message = "Other body" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SaveCurrentAsync(new TaskNotificationTemplateSaveRequest
            { TemplateId = original.Id, OverwriteExisting = true, Name = "other", Subject = "New", Message = "New body" }));
        var stored = (await fixture.Service.GetCurrentAsync()).First(item => item.Id == original.Id);
        Assert.Equal("Original", stored.Name);
        Assert.Equal("Old", stored.Subject);
        Assert.Equal("Old body", stored.Message);
    }

    [Fact]
    public async Task SaveCurrentAsync_AllowsLegacyTemplateWithoutFormat()
    {
        await using var fixture = await TemplateFixture.CreateAsync("PROD");

        var saved = await fixture.Service.SaveCurrentAsync(new TaskNotificationTemplateSaveRequest
        {
            Name = "Legacy",
            Subject = "Subject",
            Message = "Plain body",
            Format = null
        });

        Assert.Null(saved.Format);
        var loaded = Assert.Single(await fixture.Service.GetCurrentAsync());
        Assert.Null(loaded.Format);
    }

    [Fact]
    public async Task SaveCurrentAsync_PersistsExplicitHtmlFormat()
    {
        await using var fixture = await TemplateFixture.CreateAsync("PROD");

        var saved = await fixture.Service.SaveCurrentAsync(new TaskNotificationTemplateSaveRequest
        {
            Name = "HTML",
            Subject = "Subject",
            Message = "<p>Failure</p>",
            Format = "HTML"
        });

        Assert.Equal("html", saved.Format);
        Assert.Equal("html", Assert.Single(await fixture.Service.GetCurrentAsync()).Format);
    }

    [Fact]
    public async Task SaveCurrentAsync_OverwriteWithoutFormat_PreservesExistingExplicitFormat()
    {
        await using var fixture = await TemplateFixture.CreateAsync("PROD");
        var original = await fixture.Service.SaveCurrentAsync(new TaskNotificationTemplateSaveRequest
        {
            Name = "HTML",
            Subject = "Old",
            Message = "<p>Old</p>",
            Format = "html"
        });

        var updated = await fixture.Service.SaveCurrentAsync(new TaskNotificationTemplateSaveRequest
        {
            TemplateId = original.Id,
            OverwriteExisting = true,
            Subject = "New",
            Message = "<p>New</p>",
            Format = null
        });

        Assert.Equal("html", updated.Format);
        Assert.Equal("html", Assert.Single(await fixture.Service.GetCurrentAsync()).Format);
    }

    [Fact]
    public async Task GetCurrentAsync_IsolatesTemplatesByEnvironment()
    {
        await using var fixture = await TemplateFixture.CreateAsync("PROD");
        await fixture.Service.SaveCurrentAsync(new TaskNotificationTemplateSaveRequest
        {
            Name = "Shared Name",
            Subject = "Prod",
            Message = "Prod body",
            Format = "text"
        });

        fixture.Session.State.EnvironmentKey = "QA";
        Assert.Empty(await fixture.Service.GetCurrentAsync());

        await fixture.Service.SaveCurrentAsync(new TaskNotificationTemplateSaveRequest
        {
            Name = "Shared Name",
            Subject = "QA",
            Message = "QA body",
            Format = "text"
        });

        var qaTemplate = Assert.Single(await fixture.Service.GetCurrentAsync());
        Assert.Equal("QA", qaTemplate.Subject);

        fixture.Session.State.EnvironmentKey = "PROD";
        var prodTemplate = Assert.Single(await fixture.Service.GetCurrentAsync());
        Assert.Equal("Prod", prodTemplate.Subject);
    }

    private sealed class TemplateFixture : IAsyncDisposable
    {
        private TemplateFixture(SqliteConnection connection, MetadataDbContext db, FakeSessionService session)
        {
            Connection = connection;
            Db = db;
            Session = session;
            Service = new TaskNotificationTemplateService(db, session);
        }

        public SqliteConnection Connection { get; }
        public MetadataDbContext Db { get; }
        public FakeSessionService Session { get; }
        public TaskNotificationTemplateService Service { get; }

        public static async Task<TemplateFixture> CreateAsync(string environmentKey)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<MetadataDbContext>().UseSqlite(connection).Options;
            var db = new MetadataDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var session = new FakeSessionService(environmentKey);
            return new TemplateFixture(connection, db, session);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private sealed class FakeSessionService : IMoveItSessionService
    {
        public FakeSessionService(string environmentKey)
        {
            State = new MoveItSessionState
            {
                ServerKey = "NODE41",
                EnvironmentKey = environmentKey,
                Username = "tester"
            };
        }

        public MoveItSessionState State { get; }
        public bool IsAuthenticated => true;
        public MoveItSessionState? Get() => State;
        public string Create(MoveItSessionState state) => throw new NotSupportedException();
        public void Update(MoveItSessionState state) => throw new NotSupportedException();
        public bool HasLiveBrowserSession(System.Security.Claims.ClaimsPrincipal principal) => true;
        public void UpdateTokens(string accessToken, string refreshToken, DateTimeOffset accessTokenExpiresUtc) => throw new NotSupportedException();
        public bool DismissSystemMessage() => true;
        public bool RecordUserActivity() => true;
        public void Clear() { }
        public IDisposable PushBackgroundSession(MoveItSessionState state) => new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
