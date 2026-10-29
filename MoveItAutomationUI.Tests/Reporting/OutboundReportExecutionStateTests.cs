using System.Security.Claims;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.Reporting;

namespace MoveItAutomationUI.Tests.Reporting;

public sealed class OutboundReportExecutionStateTests
{
    [Fact]
    public async Task RecordExecution_DoesNotReEnableReportDisabledDuringExecution()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<MetadataDbContext>().UseSqlite(connection).Options;

        var originalModified = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var reportId = Guid.NewGuid().ToString("D");
        await using (var seed = new MetadataDbContext(options))
        {
            await seed.Database.EnsureCreatedAsync();
            seed.OutboundReports.Add(NewReport(reportId, originalModified));
            await seed.SaveChangesAsync();
        }

        OutboundReportEntity executionSnapshot;
        await using (var executionDb = new MetadataDbContext(options))
        {
            executionSnapshot = await executionDb.OutboundReports.AsNoTracking().SingleAsync(item => item.Id == reportId);
        }

        await using (var adminDb = new MetadataDbContext(options))
        {
            var current = await adminDb.OutboundReports.SingleAsync(item => item.Id == reportId);
            current.Enabled = false;
            current.NextRunUtc = null;
            current.ModifiedUtc = originalModified.AddMinutes(1);
            await adminDb.SaveChangesAsync();
        }

        await using (var completionDb = new MetadataDbContext(options))
        {
            var repository = new OutboundReportRepository(completionDb, new FakeSessionService());
            var completed = originalModified.AddMinutes(2);
            await repository.RecordExecutionAsync(
                executionSnapshot,
                originalModified,
                new OutboundReportExecutionResult(true, 5, "ok", completed),
                originalModified.AddDays(1),
                keepEnabled: true,
                recipientCount: 1,
                updateScheduleState: true);
        }

        await using var verify = new MetadataDbContext(options);
        var saved = await verify.OutboundReports.AsNoTracking().SingleAsync(item => item.Id == reportId);
        Assert.False(saved.Enabled);
        Assert.Null(saved.NextRunUtc);
        Assert.Equal(originalModified.AddMinutes(1), saved.ModifiedUtc);
        Assert.Equal("Success", saved.LastStatus);
        Assert.Equal(5, (await verify.OutboundReportHistory.AsNoTracking().SingleAsync()).RowCount);
    }

    [Fact]
    public async Task RecordExecution_AdvancesScheduleWhenDefinitionIsUnchanged()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<MetadataDbContext>().UseSqlite(connection).Options;
        var modified = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var reportId = Guid.NewGuid().ToString("D");

        await using var db = new MetadataDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.OutboundReports.Add(NewReport(reportId, modified));
        await db.SaveChangesAsync();
        var snapshot = await db.OutboundReports.AsNoTracking().SingleAsync(item => item.Id == reportId);
        var next = modified.AddDays(1);
        var completed = modified.AddMinutes(2);
        var repository = new OutboundReportRepository(db, new FakeSessionService());

        await repository.RecordExecutionAsync(snapshot, modified,
            new OutboundReportExecutionResult(true, 1, "ok", completed), next,
            keepEnabled: true, recipientCount: 1, updateScheduleState: true);

        db.ChangeTracker.Clear();
        var saved = await db.OutboundReports.AsNoTracking().SingleAsync(item => item.Id == reportId);
        Assert.True(saved.Enabled);
        Assert.Equal(next, saved.NextRunUtc);
        Assert.Equal(completed, saved.ModifiedUtc);
    }

    private static OutboundReportEntity NewReport(string id, DateTimeOffset modified) => new()
    {
        Id = id,
        EnvironmentKey = "TEST",
        OwnerUsernameKey = "TESTER",
        OwnerUsername = "tester",
        GridKey = "tasks",
        Title = "Test Report",
        Description = string.Empty,
        Recipients = "test@example.com",
        ViewStateJson = "{}",
        SourceContextJson = "{}",
        ScheduleJson = "{}",
        Enabled = true,
        NextRunUtc = modified.AddHours(1),
        LastStatus = "Pending",
        LastMessage = string.Empty,
        CreatedUtc = modified,
        ModifiedUtc = modified
    };

    private sealed class FakeSessionService : IMoveItSessionService
    {
        public bool IsAuthenticated => false;
        public MoveItSessionState? Get() => null;
        public string Create(MoveItSessionState state) => throw new NotSupportedException();
        public void Update(MoveItSessionState state) => throw new NotSupportedException();
        public bool HasLiveBrowserSession(ClaimsPrincipal principal) => false;
        public void UpdateTokens(string accessToken, string refreshToken, DateTimeOffset accessTokenExpiresUtc) => throw new NotSupportedException();
        public bool DismissSystemMessage() => false;
        public bool RecordUserActivity() => false;
        public void Clear() { }
        public IDisposable PushBackgroundSession(MoveItSessionState state) => throw new NotSupportedException();
    }
}
