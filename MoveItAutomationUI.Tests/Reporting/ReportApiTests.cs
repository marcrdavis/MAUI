using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.Reporting;
using MoveItAutomationUI.Services.Security;
using MoveItAutomationUI.Tests.MoveIt;

namespace MoveItAutomationUI.Tests.Reporting;

public sealed class ReportApiTests
{
    [Theory]
    [InlineData("")]
    [InlineData("/MAUI")]
    public async Task PreparedUrlCanBeCopiedBeforeSaveAndOnlyTheSavedDraftBecomesActive(string pathBase)
    {
        await using var fixture = await Fixture.StartAsync(pathBase);
        using var scope = fixture.App.Services.CreateScope();
        var sharing = scope.ServiceProvider.GetRequiredService<ReportApiSharingService>();
        var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();
        var prepare = new RegenerateReportApiRequest { Id = fixture.Id };
        await Assert.ThrowsAsync<ArgumentException>(() => sharing.PrepareLinkAsync(prepare, "outsider", false, default));
        var first = await sharing.PrepareLinkAsync(prepare, "tester", false, default);
        var replacement = await sharing.PrepareLinkAsync(prepare, "tester", false, default);
        Assert.False(first.Enabled);
        Assert.NotNull(first.Path);
        Assert.NotNull(first.LinkDraft);
        Assert.NotEqual(first.Path, replacement.Path);
        Assert.Empty(await db.OutboundReportApiShares.ToArrayAsync());
        using var client = fixture.Client();
        using var unavailable = await client.GetAsync(pathBase + replacement.Path);
        Assert.Equal(HttpStatusCode.NotFound, unavailable.StatusCode);
        using var privatePreparation = await client.PostAsync(pathBase + "/Reports/PrepareApiLink", null);
        Assert.Equal(HttpStatusCode.Unauthorized, privatePreparation.StatusCode);
        Assert.Equal(0, fixture.GenerationCount);

        var request = fixture.Enable(replacement.Revision);
        request.ApiAccess!.LinkDraft = replacement.LinkDraft;
        fixture.UpdateFailure = new InvalidOperationException("Report validation failed.");
        await Assert.ThrowsAsync<InvalidOperationException>(() => sharing.UpdateAsync(request, "tester", false, default));
        Assert.Empty(await db.OutboundReportApiShares.ToArrayAsync());
        using var stillUnavailable = await client.GetAsync(pathBase + replacement.Path);
        Assert.Equal(HttpStatusCode.NotFound, stillUnavailable.StatusCode);
        fixture.UpdateFailure = null;
        await sharing.UpdateAsync(request, "tester", false, default);
        var saved = await sharing.GetAsync(fixture.Id, "tester", false, default);
        Assert.True(saved.Enabled);
        Assert.Equal(replacement.Path, saved.Path);
        Assert.Null(saved.LinkDraft);
        using var oldDraft = await client.GetAsync(pathBase + first.Path);
        Assert.Equal(HttpStatusCode.NotFound, oldDraft.StatusCode);
        using var csv = await client.GetAsync(pathBase + replacement.Path);
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
    }

    [Fact]
    public async Task PreparedLinksRejectTamperingOtherReportsAndStaleSavedRevisions()
    {
        await using var fixture = await Fixture.StartAsync("");
        using var scope = fixture.App.Services.CreateScope();
        var sharing = scope.ServiceProvider.GetRequiredService<ReportApiSharingService>();
        var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();
        var prepared = await sharing.PrepareLinkAsync(new() { Id = fixture.Id }, "tester", false, default);
        var invalid = fixture.Enable();
        invalid.ApiAccess!.LinkDraft = "invalid-token";
        await Assert.ThrowsAsync<ArgumentException>(() => sharing.UpdateAsync(invalid, "tester", false, default));
        var other = NewReport();
        db.OutboundReports.Add(other);
        await db.SaveChangesAsync();
        invalid.Id = other.Id;
        invalid.ApiAccess.LinkDraft = prepared.LinkDraft;
        await Assert.ThrowsAsync<ArgumentException>(() => sharing.UpdateAsync(invalid, "tester", false, default));
        Assert.Empty(await db.OutboundReportApiShares.ToArrayAsync());

        await sharing.UpdateAsync(fixture.Enable(), "tester", false, default);
        var active = await sharing.GetAsync(fixture.Id, "tester", false, default);
        var pending = await sharing.PrepareLinkAsync(new() { Id = fixture.Id, Revision = active.Revision }, "tester", false, default);
        using var client = fixture.Client();
        using var unchanged = await client.GetAsync(active.Path);
        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
        using var notActive = await client.GetAsync(pending.Path);
        Assert.Equal(HttpStatusCode.NotFound, notActive.StatusCode);
        var rotated = await sharing.RegenerateAsync(new() { Id = fixture.Id, Revision = active.Revision }, "tester", false, default);
        var stale = fixture.Enable(active.Revision);
        stale.ApiAccess!.LinkDraft = pending.LinkDraft;
        await Assert.ThrowsAsync<ReportApiConflictException>(() => sharing.UpdateAsync(stale, "tester", false, default));
        stale.ApiAccess.Revision = rotated.Revision;
        await Assert.ThrowsAsync<ArgumentException>(() => sharing.UpdateAsync(stale, "tester", false, default));
        Assert.Equal(rotated.Path, (await sharing.GetAsync(fixture.Id, "tester", false, default)).Path);
    }

    [Fact]
    public async Task SavingApiAccessDisabledDiscardsTheDraftWithoutCreatingAShare()
    {
        await using var fixture = await Fixture.StartAsync("");
        using var scope = fixture.App.Services.CreateScope();
        var sharing = scope.ServiceProvider.GetRequiredService<ReportApiSharingService>();
        var prepared = await sharing.PrepareLinkAsync(new() { Id = fixture.Id }, "tester", false, default);
        var request = fixture.Enable();
        request.ApiAccess!.Enabled = false;
        request.ApiAccess.LinkDraft = prepared.LinkDraft;
        await sharing.UpdateAsync(request, "tester", false, default);
        var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();
        Assert.Empty(await db.OutboundReportApiShares.ToArrayAsync());
        Assert.Null((await sharing.GetAsync(fixture.Id, "tester", false, default)).Path);
        using var client = fixture.Client();
        using var inactive = await client.GetAsync(prepared.Path);
        Assert.Equal(HttpStatusCode.NotFound, inactive.StatusCode);
        var method = typeof(ReportsController).GetMethod(nameof(ReportsController.PrepareApiLink))!;
        Assert.NotNull(method.GetCustomAttribute<Microsoft.AspNetCore.Mvc.HttpPostAttribute>());
        Assert.NotNull(method.GetCustomAttribute<Microsoft.AspNetCore.Mvc.ValidateAntiForgeryTokenAttribute>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("/MAUI")]
    public async Task AnonymousCsvUsesTheShareGuidAndPreservesEmailSchedule(string pathBase)
    {
        await using var fixture = await Fixture.StartAsync(pathBase);
        using var scope = fixture.App.Services.CreateScope();
        var sharing = scope.ServiceProvider.GetRequiredService<ReportApiSharingService>();
        await Assert.ThrowsAsync<ArgumentException>(() => sharing.GetAsync(fixture.Id, "outsider", false, default));
        await sharing.UpdateAsync(fixture.Enable(), "tester", false, default);
        var access = await sharing.GetAsync(fixture.Id, "tester", false, default);
        Assert.True(access.Enabled);
        Assert.NotNull(access.Path);

        using var client = fixture.Client();
        using var ordinaryId = await client.GetAsync(pathBase + "/api/reports/" + fixture.Id + "/csv");
        Assert.Equal(HttpStatusCode.NotFound, ordinaryId.StatusCode);
        using var malformed = await client.GetAsync(pathBase + "/api/reports/bad-guid/csv");
        Assert.Equal(HttpStatusCode.NotFound, malformed.StatusCode);
        using var management = await client.GetAsync(pathBase + "/Reports/ApiAccess?id=" + fixture.Id);
        Assert.Equal(HttpStatusCode.Unauthorized, management.StatusCode);
        Assert.Null(management.Headers.Location);

        using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, pathBase + access.Path));
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(0, fixture.GenerationCount);
        using var csv = await client.GetAsync(pathBase + access.Path);
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", csv.Content.Headers.ContentType?.CharSet);
        Assert.Equal("inline", csv.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("report.csv", csv.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal("Task Name,Status\r\n\"Ops, West\",Failed", await csv.Content.ReadAsStringAsync());
        Assert.True(csv.Headers.CacheControl?.NoStore);
        Assert.Equal("1", Assert.Single(csv.Headers.GetValues("X-MAUI-Row-Count")));

        using var download = await client.GetAsync(pathBase + access.Path + "?download=true");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("report.csv", download.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal("text/csv", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(await csv.Content.ReadAsStringAsync(), await download.Content.ReadAsStringAsync());
        using var inline = await client.GetAsync(pathBase + access.Path + "?download=false");
        Assert.Equal(HttpStatusCode.OK, inline.StatusCode);
        Assert.Equal("inline", inline.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("report.csv", inline.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal(await csv.Content.ReadAsStringAsync(), await inline.Content.ReadAsStringAsync());
        Assert.Equal(1, fixture.GenerationCount);
        using var overrideAttempt = await client.GetAsync(pathBase + access.Path + "?environment=Other");
        Assert.Equal(HttpStatusCode.BadRequest, overrideAttempt.StatusCode);

        var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();
        db.ChangeTracker.Clear();
        var report = await db.OutboundReports.SingleAsync();
        Assert.False(report.Enabled);
        Assert.Null(report.NextRunUtc);
        var history = await db.OutboundReportHistory.SingleAsync();
        Assert.Equal(0, history.RecipientCount);
        Assert.Equal("API CSV generated.", history.Message);
        var request = fixture.Enable(access.Revision);
        request.ApiAccess!.Enabled = false;
        await sharing.UpdateAsync(request, "tester", false, default);
        using var disabled = await client.GetAsync(pathBase + access.Path);
        Assert.Equal(HttpStatusCode.NotFound, disabled.StatusCode);
    }

    [Fact]
    public async Task RotationDuringGenerationRevokesTheOldResponseAndAllowsTheReplacement()
    {
        await using var fixture = await Fixture.StartAsync("");
        ReportApiAccessState access;
        using (var setup = fixture.App.Services.CreateScope())
        {
            var sharing = setup.ServiceProvider.GetRequiredService<ReportApiSharingService>();
            await sharing.UpdateAsync(fixture.Enable(), "tester", false, default);
            access = await sharing.GetAsync(fixture.Id, "tester", false, default);
        }
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<OutboundReportData>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Generate = token => { entered.TrySetResult(); return release.Task.WaitAsync(token); };
        using var client = fixture.Client();
        var pending = client.GetAsync(access.Path);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var busy = await client.GetAsync(access.Path);
        Assert.Equal(HttpStatusCode.TooManyRequests, busy.StatusCode);
        Assert.NotNull(busy.Headers.RetryAfter);
        ReportApiAccessState replacement;
        using (var change = fixture.App.Services.CreateScope())
        {
            var sharing = change.ServiceProvider.GetRequiredService<ReportApiSharingService>();
            replacement = await sharing.RegenerateAsync(
                new RegenerateReportApiRequest { Id = fixture.Id, Revision = access.Revision }, "tester", false, default);
            await Assert.ThrowsAsync<ReportApiConflictException>(() => sharing.RegenerateAsync(
                new RegenerateReportApiRequest { Id = fixture.Id, Revision = access.Revision }, "tester", false, default));
        }
        Assert.NotEqual(access.Path, replacement.Path);
        release.SetResult(Data());
        using var revoked = await pending;
        Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
        fixture.Generate = _ => Task.FromResult(Data());
        using var success = await client.GetAsync(replacement.Path);
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        using var old = await client.GetAsync(access.Path);
        Assert.Equal(HttpStatusCode.NotFound, old.StatusCode);
    }

    [Fact]
    public async Task FailedGenerationReturnsAnErrorAndCanBeRetried()
    {
        await using var fixture = await Fixture.StartAsync("");
        using var scope = fixture.App.Services.CreateScope();
        var sharing = scope.ServiceProvider.GetRequiredService<ReportApiSharingService>();
        await sharing.UpdateAsync(fixture.Enable(), "tester", false, default);
        var access = await sharing.GetAsync(fixture.Id, "tester", false, default);
        fixture.Generate = _ => throw new HttpRequestException("Internal source details");
        using var client = fixture.Client();
        using var failed = await client.GetAsync(access.Path);
        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        Assert.DoesNotContain("Internal source details", await failed.Content.ReadAsStringAsync());
        fixture.Generate = _ => Task.FromResult(new OutboundReportData(Data().Columns, []));
        using var success = await client.GetAsync(access.Path);
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        Assert.Equal("Task Name,Status", await success.Content.ReadAsStringAsync());
    }

    [Fact]
    public void CsvWriterRetainsEmailPreambleAndEscapesApiData()
    {
        var data = new OutboundReportData([new() { Key = "A", Label = "Value" }],
            [new() { Values = new() { ["A"] = "=SUM(1,2)\nUnicode é" } }]);
        var csv = ReportCsvWriter.Write(data);
        Assert.Equal("Value\r\n\"'=SUM(1,2)\nUnicode é\"", csv);
        var email = ReportCsvWriter.WriteEmail("Title", "Description", data);
        Assert.StartsWith("Title\r\nDescription\r\nGenerated: ", email);
        Assert.EndsWith("\r\n\r\n" + csv, email);
        var secret = "/MAUI/api/reports/" + Guid.NewGuid().ToString("D") + "/csv?download=true";
        Assert.Equal("/MAUI/api/reports/[redacted]/csv?download=true", ReportApiRoutes.Redact(secret));
    }

    [Fact]
    public async Task SchemaUpgradeAddsSharingWhilePreservingExistingReports()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<MetadataDbContext>().UseSqlite(connection).Options;
        await using var db = new MetadataDbContext(options);
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../upgrade.json"));
        var manifest = JsonSerializer.Deserialize<MauiUpgradeManifest>(await File.ReadAllTextAsync(path))!;
        var previous = new MauiUpgradeManifest { RequiredSchemaVersion = 1,
            SchemaMigrations = manifest.SchemaMigrations.Where(m => m.ToVersion <= 1).ToList() };
        var manager = new MauiDatabaseSchemaManager(db, new MauiDatabaseSchemaState(), NullLogger<MauiDatabaseSchemaManager>.Instance);
        Assert.Equal(1, await manager.ApplyRequiredMigrationsAsync(previous));
        var report = NewReport();
        db.OutboundReports.Add(report);
        await db.SaveChangesAsync();
        Assert.Equal(2, await manager.ApplyRequiredMigrationsAsync(manifest));
        Assert.Equal(report.Title, (await db.OutboundReports.SingleAsync()).Title);
        Assert.Empty(await db.OutboundReportApiShares.ToArrayAsync());
    }

    private static OutboundReportData Data() => new(
        [new() { Key = "Name", Label = "Task Name" }, new() { Key = "Status", Label = "Status" }],
        [new() { Values = new() { ["Name"] = "Ops, West", ["Status"] = "Failed" } }]);

    private static OutboundReportEntity NewReport() => new()
    {
        Id = Guid.NewGuid().ToString("D"), OwnerUsername = "tester", OwnerUsernameKey = "TESTER",
        EnvironmentKey = "TEST", GridKey = "tasks", Title = "Test", Recipients = "team@example.test",
        ViewStateJson = "{}", SourceContextJson = "{}", ScheduleJson = "{}",
        CreatedUtc = DateTimeOffset.UtcNow, ModifiedUtc = DateTimeOffset.UtcNow
    };

    private sealed class Fixture : IAsyncDisposable
    {
        public required WebApplication App { get; init; }
        public required string DirectoryPath { get; init; }
        public required string Id { get; init; }
        public int GenerationCount;
        public Exception? UpdateFailure { get; set; }
        public Func<CancellationToken, Task<OutboundReportData>> Generate { get; set; } = _ => Task.FromResult(Data());

        public UpdateOutboundReportRequest Enable(string revision = "") => new()
        {
            Id = Id, ApiAccess = new() { Enabled = true, Revision = revision }
        };

        public HttpClient Client() => new(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
        {
            BaseAddress = new Uri(App.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())
        };

        public static async Task<Fixture> StartAsync(string pathBase)
        {
            var directory = Path.Combine(Path.GetTempPath(), "maui-report-api-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.Logging.ClearProviders();
            builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
            builder.Services.AddDbContext<MetadataDbContext>(options =>
                options.UseSqlite("Data Source=" + Path.Combine(directory, "reports.db") + ";Pooling=False"));
            builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(directory, "keys")));
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
                options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; });
            builder.Services.AddAuthorization(options => options.FallbackPolicy =
                new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
            builder.Services.AddRateLimiter(options => options.AddConcurrencyLimiter("ReportCsv", limiter => limiter.PermitLimit = 20));
            builder.Services.AddControllers().AddApplicationPart(typeof(ReportApiController).Assembly);
            builder.Services.AddSingleton<ReportApiRuntime>();
            builder.Services.AddScoped<ReportApiSharingService>();
            builder.Services.AddScoped<IOutboundReportRepository, OutboundReportRepository>();
            builder.Services.AddSingleton(Stub<IMoveItSessionService>((method, _) => throw new NotSupportedException(method.Name)));
            builder.Services.AddSingleton(Stub<IMauiSecurityService>((method, _) =>
                method.Name == nameof(IMauiSecurityService.IsAdmin) ? true : throw new NotSupportedException(method.Name)));
            Fixture? fixture = null;
            builder.Services.AddSingleton(Stub<IOutboundReportService>((method, args) => method.Name switch
            {
                nameof(IOutboundReportService.GenerateAsync) => Generate((CancellationToken)args[1]!),
                nameof(IOutboundReportService.UpdateAsync) => fixture!.UpdateFailure is { } failure
                    ? throw failure : Task.FromResult<OutboundReportSummary>(null!),
                _ => throw new NotSupportedException(method.Name)
            }));
            Task<OutboundReportData> Generate(CancellationToken token)
            {
                Interlocked.Increment(ref fixture!.GenerationCount);
                return fixture!.Generate(token);
            }
            var app = builder.Build();
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();
                await db.Database.EnsureCreatedAsync();
                var report = NewReport();
                db.OutboundReports.Add(report);
                await db.SaveChangesAsync();
                fixture = new Fixture { App = app, DirectoryPath = directory, Id = report.Id };
            }
            if (pathBase.Length > 0) app.UsePathBase(pathBase);
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseRateLimiter();
            app.MapControllers();
            app.MapControllerRoute("default", "{controller=Reports}/{action=List}/{id?}");
            await app.StartAsync();
            return fixture!;
        }

        public async ValueTask DisposeAsync()
        {
            await App.StopAsync();
            await App.DisposeAsync();
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }

    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, HostSharedCrudTests.CrudProxy>();
        ((HostSharedCrudTests.CrudProxy)(object)proxy).Handler = handler;
        return proxy;
    }
}
