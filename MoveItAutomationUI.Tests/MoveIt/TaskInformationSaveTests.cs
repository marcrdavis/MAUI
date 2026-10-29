using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class TaskInformationSaveTests
{
    [Theory]
    [InlineData("validation", 400)] [InlineData("conflict", 409)]
    [InlineData("uncertain", 502)] [InlineData("audit", 500)]
    [InlineData("metadata", 500)] [InlineData("folder", 500)] [InlineData("success", 200)]
    public async Task SaveReportsCommittedAndUncertainOutcomesWithoutReplayingNativeWrites(string failure, int status)
    {
        var calls = new List<string>();
        var service = Stub<IAutomationTaskService>((method, _) =>
        {
            if (method.Name == nameof(IAutomationTaskService.InvalidateCache)) { calls.Add("invalidate"); return null; }
            Assert.Equal(nameof(IAutomationTaskService.UpdateBasicDetailsAsync), method.Name); calls.Add("PUT");
            if (failure == "conflict") throw new ObjectEditConflictException("TaskInformation");
            if (failure == "uncertain") throw new TraditionalTaskPartialSaveException("1", "Readback failed", new HttpRequestException());
            return Task.CompletedTask;
        });
        var validation = Stub<IMetadataValueValidationService>((_, _) =>
        {
            calls.Add("validate");
            if (failure == "validation") throw new ArgumentException("Invalid metadata");
            return Task.FromResult(new Dictionary<string, string>());
        });
        var metadata = Stub<IMetadataRepository>((method, _) =>
        {
            var operation = method.Name switch
            {
                nameof(IMetadataRepository.RecordObjectConfigurationModifiedAsync) => "audit",
                nameof(IMetadataRepository.SaveObjectMetadataAsync) => "metadata",
                nameof(IMetadataRepository.AssignObjectToFolderAsync) => "folder",
                _ => throw new NotSupportedException(method.Name)
            };
            calls.Add(operation); if (failure == operation) throw new InvalidOperationException("Local write failed");
            return Task.CompletedTask;
        });
        var session = Stub<IMoveItSessionService>((_, _) => new MoveItSessionState { ServerKey = "Test", Username = "tester", Info = new MoveItInfo { IsAdmin = true } });
        var controller = new TasksController(service, null!, null!, null!, null!, null!, null!, null!, null!, metadata, validation,
            null!, null!, null!, null!, null!, null!, session, new Snapshot<UiOptions>(), new Snapshot<GridBehaviorOptions>(),
            new Snapshot<MetadataUiOptions>(), new Snapshot<TaskBehaviorOptions>(), NullLogger<TasksController>.Instance);
        var result = await controller.SaveDetails("1", new TaskDetailsUpdateRequest { EditVersion = "revision", Name = "Name", UpdateMoveItFields = true }, CancellationToken.None);
        var objectResult = result as ObjectResult; var jsonResult = result as JsonResult;
        Assert.Equal(status, objectResult?.StatusCode ?? jsonResult?.StatusCode ?? 200);
        var payload = JsonSerializer.SerializeToElement(objectResult?.Value ?? jsonResult?.Value);
        Assert.Equal("validate", calls[0]);
        if (failure == "validation") { Assert.Collection(calls, call => Assert.Equal("validate", call)); return; }
        Assert.Equal("PUT", calls[1]);
        if (failure == "success") Assert.Equal(new[] { "validate", "PUT", "audit", "metadata", "folder" }, calls);
        else if (failure == "conflict") { Assert.False(payload.GetProperty("moveItSaved").GetBoolean()); Assert.True(payload.GetProperty("editConflict").GetBoolean()); }
        else
        {
            Assert.True(payload.GetProperty("moveItSaved").GetBoolean());
            if (failure == "uncertain") { Assert.True(payload.GetProperty("outcomeUnknown").GetBoolean()); Assert.Equal(new[] { "validate", "PUT", "invalidate" }, calls); }
        }
    }
    [Theory]
    [InlineData("create")] [InlineData("rename")] [InlineData("move")] [InlineData("delete")]
    [InlineData("task")] [InlineData("tasks")]
    public async Task NonAdminFolderRequestsStopBeforeAnyRepositoryWrite(string action)
    {
        var session = Stub<IMoveItSessionService>((_, _) => new MoveItSessionState { ServerKey = "Test", Username = "editor", Info = new MoveItInfo { IsAdmin = false } });
        var metadata = Stub<IMetadataRepository>((method, _) => throw new InvalidOperationException("Unauthorized repository call: " + method.Name));
        var controller = Controller(null!, metadata, null!, session);
        var result = action switch
        {
            "create" => await controller.CreateFolder(new() { Name = "New" }, default),
            "rename" => await controller.RenameFolder(new() { FolderId = 7, Name = "Rename" }, default),
            "move" => await controller.MoveFolder(new() { FolderId = 7, ParentId = 8 }, default),
            "delete" => await controller.DeleteFolder(new() { FolderId = 7 }, default),
            "task" => await controller.MoveTask(new() { TaskId = "42", FolderId = 7 }, default),
            _ => await controller.MoveTasks(new() { TaskIds = ["42"], FolderId = 7 }, default)
        };
        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task InformationSavePreservesNonAdminAssignmentWhileAllowingOtherEdits(bool admin)
    {
        var writes = new List<string>();
        var session = Stub<IMoveItSessionService>((_, _) => new MoveItSessionState { ServerKey = "Test", Username = "editor", Info = new MoveItInfo { IsAdmin = admin } });
        var metadata = Stub<IMetadataRepository>((method, args) =>
        {
            writes.Add(method.Name);
            if (method.Name == nameof(IMetadataRepository.AssignObjectToFolderAsync)) Assert.Equal(999, args[3]);
            return Task.CompletedTask;
        });
        var validation = Stub<IMetadataValueValidationService>((_, _) => Task.FromResult(new Dictionary<string, string>()));
        var result = await Controller(null!, metadata, validation, session).SaveDetails("42", new() { FolderId = 999, UpdateMoveItFields = false }, default);
        Assert.IsType<JsonResult>(result);
        Assert.Contains(nameof(IMetadataRepository.SaveObjectMetadataAsync), writes);
        if (admin) Assert.Contains(nameof(IMetadataRepository.AssignObjectToFolderAsync), writes);
        else Assert.DoesNotContain(nameof(IMetadataRepository.AssignObjectToFolderAsync), writes);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task FullEditorSaveUsesTheSameAdminOnlyFolderRule(bool admin)
    {
        var writes = new List<string>();
        var session = Stub<IMoveItSessionService>((_, _) => new MoveItSessionState { ServerKey = "Test", Username = "editor", Info = new MoveItInfo { IsAdmin = admin } });
        var metadata = Stub<IMetadataRepository>((method, _) => { writes.Add(method.Name); return Task.CompletedTask; });
        var validation = Stub<IMetadataValueValidationService>((_, _) => Task.FromResult(new Dictionary<string, string>()));
        var save = Stub<ITraditionalTaskEditorSaveService>((_, _) => { writes.Add("native save"); return Task.CompletedTask; });
        var tasks = Stub<IAutomationTaskService>((_, _) => Task.FromResult(new TaskCacheSnapshot([], DateTimeOffset.UtcNow)));
        var controller = new TraditionalTaskEditorController(null!, save, tasks, null!, null!, null!, metadata, validation, null!, session,
            null!, null!, new Snapshot<MetadataUiOptions>(), NullLogger<TraditionalTaskEditorController>.Instance);
        var result = await controller.Save("42", new JsonObject { ["editVersion"] = "revision", ["captureUndo"] = false, ["folderId"] = 999 }, default);
        Assert.IsType<JsonResult>(result); Assert.Contains("native save", writes);
        Assert.Contains(nameof(IMetadataRepository.SaveObjectMetadataAsync), writes);
        if (admin) Assert.Contains(nameof(IMetadataRepository.AssignObjectToFolderAsync), writes);
        else Assert.DoesNotContain(nameof(IMetadataRepository.AssignObjectToFolderAsync), writes);
    }
    private static TasksController Controller(IAutomationTaskService service, IMetadataRepository metadata,
        IMetadataValueValidationService validation, IMoveItSessionService session)
        => new(service, null!, null!, null!, null!, null!, null!, null!, null!, metadata, validation,
            null!, null!, null!, null!, null!, null!, session, new Snapshot<UiOptions>(), new Snapshot<GridBehaviorOptions>(),
            new Snapshot<MetadataUiOptions>(), new Snapshot<TaskBehaviorOptions>(), NullLogger<TasksController>.Instance);
    private sealed class Snapshot<T> : IOptionsSnapshot<T> where T : class, new()
    { public T Value { get; } = new(); public T Get(string? name) => Value; }
    private static T Stub<T>(Func<MethodInfo, object?[], object?> handler) where T : class
    { var value = DispatchProxy.Create<T, Proxy>(); ((Proxy)(object)value).Handler = handler; return value; }
    public class Proxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args ?? []);
    }
}
