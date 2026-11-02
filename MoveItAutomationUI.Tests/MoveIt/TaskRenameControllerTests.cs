using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Tasks;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class TaskRenameControllerTests
{
    [Fact]
    public void RenameRequiresAuthenticationAntiforgeryAndNativeTaskEditPermission()
    {
        Assert.NotNull(typeof(TasksController).GetCustomAttribute<AuthorizeAttribute>());
        var action = typeof(TasksController).GetMethod(nameof(TasksController.RenameTask))!;
        Assert.NotNull(action.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
        Assert.NotNull(action.GetCustomAttribute<MoveItObjectPermissionAttribute>());
    }

    [Theory]
    [InlineData("", "New")] [InlineData("abc", "New")] [InlineData("-1", "New")]
    [InlineData("1", "")] [InlineData("1", "   ")]
    public async Task MalformedRequestsNeverReachUndoOrRename(string id, string name)
    {
        var fixture = new Fixture();
        Assert.IsType<BadRequestObjectResult>(await fixture.Controller.RenameTask(new() { TaskId = id, Name = name }));
        Assert.Empty(fixture.Events);
    }

    [Fact]
    public async Task NullInvalidModelAndOverlongRequestsNeverWrite()
    {
        var fixture = new Fixture();
        Assert.IsType<BadRequestObjectResult>(await fixture.Controller.RenameTask(null));
        Assert.IsType<BadRequestObjectResult>(await fixture.Controller.RenameTask(new() { TaskId = "1", Name = new string('x', 255) }));
        fixture.Controller.ModelState.AddModelError("Name", "Invalid JSON");
        Assert.IsType<BadRequestObjectResult>(await fixture.Controller.RenameTask(fixture.Request));
        Assert.Empty(fixture.Events);
    }

    [Theory]
    [InlineData("none", 200)] [InlineData("duplicate", 409)] [InlineData("conflict", 409)]
    [InlineData("undo", 500)] [InlineData("partial", 502)] [InlineData("audit", 500)] [InlineData("unknown", 500)]
    public async Task ValidatesBeforeUndoAndAuditsOnlyConfirmedRename(string failure, int status)
    {
        var fixture = new Fixture { Failure = failure };
        var result = await fixture.Controller.RenameTask(fixture.Request);
        var payload = result is JsonResult json ? json.Value : Assert.IsAssignableFrom<ObjectResult>(result).Value;
        Assert.Equal(status, result is JsonResult ? 200 : ((ObjectResult)result).StatusCode);
        var document = JsonSerializer.SerializeToElement(payload);
        if (failure == "none")
        {
            Assert.Equal("Renamed", document.GetProperty("taskName").GetString());
            Assert.Equal(new[] { "validate:1:Renamed:Original", "undo", "rename:1:Renamed:Original", "audit", "invalidate" }, fixture.Events);
        }
        else
        {
            Assert.True(document.GetProperty("message").GetString()!.Length > 0);
            if (failure is "duplicate" or "conflict") Assert.Single(fixture.Events);
            if (failure == "undo") Assert.Equal(new[] { "validate:1:Renamed:Original", "undo" }, fixture.Events);
            if (failure == "undo")
            {
                Assert.False(document.GetProperty("moveItSaved").GetBoolean());
                Assert.False(document.GetProperty("outcomeUnknown").GetBoolean());
            }
            if (failure == "unknown")
            {
                Assert.True(document.GetProperty("outcomeUnknown").GetBoolean());
                Assert.True(document.GetProperty("moveItSaved").GetBoolean());
                Assert.Single(fixture.Events, item => item.StartsWith("rename:"));
                Assert.DoesNotContain("audit", fixture.Events);
            }
            if (failure is "partial" or "audit")
            {
                Assert.True(document.GetProperty("moveItSaved").GetBoolean());
                Assert.Equal("invalidate", fixture.Events.Last());
                Assert.Single(fixture.Events, item => item.StartsWith("rename:"));
                if (failure == "partial") { Assert.True(document.GetProperty("outcomeUnknown").GetBoolean()); Assert.DoesNotContain("audit", fixture.Events); }
            }
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task SqliteAuditFailureLogsUnderlyingCauseWithoutRepeatingTheSuccessfulRename(bool existingMetadata)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new MetadataDbContext(new DbContextOptionsBuilder<MetadataDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var previousModified = DateTimeOffset.Parse("2026-10-01T12:00:00Z");
        if (existingMetadata)
        {
            db.MetadataObjects.Add(new() { EnvironmentKey = "TEST", ObjectType = "Task", ObjectId = "1",
                MetadataModifiedUtc = previousModified, MetadataModifiedBy = "previous",
                ConfigurationModifiedUtc = previousModified, ConfigurationModifiedBy = "previous" });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }
        // Real SQLite/EF failure, rather than a mocked DbUpdateException. This verifies
        // diagnostics for either first-time tracking-row creation or an existing audit row.
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_audit_insert BEFORE INSERT ON MetadataObjects BEGIN SELECT RAISE(ABORT, 'fixture audit write rejected'); END;");
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_audit_update BEFORE UPDATE ON MetadataObjects BEGIN SELECT RAISE(ABORT, 'fixture audit write rejected'); END;");
        var fixture = new Fixture(new MetadataRepository(db));

        var result = Assert.IsType<ObjectResult>(await fixture.Controller.RenameTask(fixture.Request));
        Assert.Equal(500, result.StatusCode);
        var payload = JsonSerializer.SerializeToElement(result.Value);
        Assert.True(payload.GetProperty("moveItSaved").GetBoolean());
        Assert.DoesNotContain("fixture audit", payload.GetProperty("message").GetString()!);
        Assert.Equal(new[] { "validate:1:Renamed:Original", "undo", "rename:1:Renamed:Original", "audit", "invalidate" }, fixture.Events);
        var entry = Assert.Single(fixture.Logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        var databaseFailure = Assert.IsType<DbUpdateException>(entry.Exception);
        var cause = Assert.IsType<SqliteException>(databaseFailure.GetBaseException());
        Assert.Equal(19, cause.SqliteErrorCode);
        Assert.Contains("Task 1 was renamed", entry.Message);
        Assert.Contains("DbUpdateException", entry.Message);
        Assert.Contains(cause.Message, entry.Message);
        Assert.Contains("fixture audit write rejected", entry.Exception!.ToString());
        db.ChangeTracker.Clear();
        var persisted = await db.MetadataObjects.AsNoTracking().SingleOrDefaultAsync();
        if (existingMetadata)
        {
            Assert.NotNull(persisted);
            Assert.Equal(previousModified, persisted.MetadataModifiedUtc);
            Assert.Equal(previousModified, persisted.ConfigurationModifiedUtc);
            Assert.Equal("previous", persisted.ConfigurationModifiedBy);
        }
        else Assert.Null(persisted);
    }

    private sealed class RecordingLogger : ILogger<TasksController>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception), exception));
    }

    private sealed class Fixture
    {
        public List<string> Events { get; } = [];
        public string Failure { get; set; } = "none";
        public TaskRenameRequest Request => new() { TaskId = " 1 ", Name = " Renamed ", OriginalName = "Original" };
        public TasksController Controller { get; }
        public RecordingLogger Logger { get; } = new();
        public Fixture(IMetadataRepository? realMetadata = null)
        {
            var crud = Stub<ITaskCrudCoordinator>((method, args) =>
            {
                if (method.Name == nameof(ITaskCrudCoordinator.ValidateRenameAsync))
                {
                    Events.Add($"validate:{args[0]}:{args[1]}:{args[2]}");
                    if (Failure == "duplicate") throw new TaskNameAlreadyExistsException("Renamed");
                    if (Failure == "conflict") throw new ObjectEditConflictException("Task");
                    return Task.CompletedTask;
                }
                if (method.Name == nameof(ITaskCrudCoordinator.RenameAsync))
                {
                    Events.Add($"rename:{args[0]}:{args[1]}:{args[2]}");
                    if (Failure == "partial") throw new TraditionalTaskPartialSaveException("1", "Inspect the Task before retrying.", new IOException("Response lost"));
                    if (Failure == "unknown") throw new Exception("Native result unavailable");
                    return Task.CompletedTask;
                }
                throw new NotSupportedException(method.Name);
            });
            var session = Stub<IMoveItSessionService>((method, _) => method.Name == nameof(IMoveItSessionService.Get)
                ? new MoveItSessionState { Username = "editor", EnvironmentKey = "test" } : throw new NotSupportedException(method.Name));
            var undo = Stub<ITaskUndoService>((_, _) =>
            {
                Events.Add("undo"); if (Failure == "undo") throw new IOException("Snapshot unavailable");
                return Task.FromResult(new TaskUndoSnapshot("snapshot", "1", "Original", "editor", DateTimeOffset.UtcNow, "<Task/>"));
            });
            var metadata = Stub<IMetadataRepository>((method, args) =>
            {
                if (method.Name != nameof(IMetadataRepository.RecordObjectConfigurationModifiedAsync)) throw new NotSupportedException(method.Name);
                Events.Add("audit"); if (Failure == "audit") throw new IOException("Audit unavailable");
                return realMetadata?.RecordObjectConfigurationModifiedAsync((string)args[0]!, (string)args[1]!, (string)args[2]!, (string)args[3]!, (CancellationToken)args[4]!) ?? Task.CompletedTask;
            });
            var tasks = Stub<IAutomationTaskService>((method, _) =>
            {
                if (method.Name != nameof(IAutomationTaskService.InvalidateCache)) throw new NotSupportedException(method.Name);
                Events.Add("invalidate"); return null;
            });
            // Supply unrelated controller dependencies without introducing alternate production paths.
            var constructor = typeof(TasksController).GetConstructors().Single();
            Controller = (TasksController)constructor.Invoke(constructor.GetParameters().Select(parameter =>
            {
                var type = parameter.ParameterType;
                if (type == typeof(ITaskCrudCoordinator)) return (object)crud;
                if (type == typeof(IMoveItSessionService)) return session;
                if (type == typeof(ITaskUndoService)) return undo;
                if (type == typeof(IMetadataRepository)) return metadata;
                if (type == typeof(IAutomationTaskService)) return tasks;
                if (type == typeof(ILogger<TasksController>)) return Logger;
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IOptionsSnapshot<>))
                    return Activator.CreateInstance(typeof(Snapshot<>).MakeGenericType(type.GenericTypeArguments[0]));
                return null;
            }).ToArray());
        }
    }
    public sealed class Snapshot<T> : IOptionsSnapshot<T> where T : class, new()
    { public T Value { get; } = new(); public T Get(string? name) => Value; }
    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, TaskCrudCoordinatorTests.CrudProxy>();
        ((TaskCrudCoordinatorTests.CrudProxy)(object)proxy).Handler = handler; return proxy;
    }
}
