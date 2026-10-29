using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MoveItAutomationUI.Controllers;
using MoveItAutomationUI.Models;
using MoveItAutomationUI.Services.Metadata;
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Tests.MoveIt;

public sealed class HostInformationSaveTests
{
    [Theory]
    [InlineData("validation", 400)] [InlineData("conflict", 409)]
    [InlineData("uncertain", 502)] [InlineData("audit", 500)]
    [InlineData("metadata", 500)] [InlineData("success", 200)] [InlineData("default", 200)] [InlineData("metadata-only", 200)]
    public async Task SaveReportsCommittedAndUncertainOutcomesWithoutReplayingNativeWrites(string failure, int status)
    {
        var calls = new List<string>();
        var service = Stub<IAutomationHostService>((method, _) =>
        {
            Assert.Equal(nameof(IAutomationHostService.UpdateBasicDetailsAsync), method.Name); calls.Add("PUT");
            if (failure == "conflict") throw new ObjectEditConflictException("Host");
            if (failure == "uncertain") throw new HostPartialSaveException("Readback failed", new HttpRequestException(), "1");
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
                _ => throw new NotSupportedException(method.Name)
            };
            calls.Add(operation); if (failure == operation) throw new InvalidOperationException("Local write failed");
            return Task.CompletedTask;
        });
        var session = Stub<IMoveItSessionService>((_, _) => new MoveItSessionState { ServerKey = "Test", Username = "tester" });
        var controller = new HostsController(service, null!, null!, null!, null!, metadata, validation, null!, null!, session,
            new Snapshot<UiOptions>(), new Snapshot<GridBehaviorOptions>(), new Snapshot<MetadataUiOptions>(),
            new Snapshot<HostBehaviorOptions>(), NullLogger<HostsController>.Instance);
        var result = await controller.SaveDetails(failure == "default" ? "0" : "1", new HostDetailsUpdateRequest
        { EditVersion = "revision", Name = failure == "default" ? "(Default)" : "Name", Description = "Updated", UpdateMoveItFields = failure != "metadata-only" }, CancellationToken.None);
        var objectResult = result as ObjectResult; var jsonResult = result as JsonResult;
        Assert.Equal(status, objectResult?.StatusCode ?? jsonResult?.StatusCode ?? 200);
        var payload = JsonSerializer.SerializeToElement(objectResult?.Value ?? jsonResult?.Value);
        Assert.Equal("validate", calls[0]);
        if (failure == "validation") { Assert.Collection(calls, call => Assert.Equal("validate", call)); return; }
        if (failure == "metadata-only") { Assert.Equal(new[] { "validate", "metadata" }, calls); return; }
        Assert.Equal("PUT", calls[1]);
        if (failure is "success" or "default") Assert.Equal(new[] { "validate", "PUT", "audit", "metadata" }, calls);
        else if (failure == "conflict") { Assert.False(payload.GetProperty("moveItSaved").GetBoolean()); Assert.True(payload.GetProperty("editConflict").GetBoolean()); }
        else
        {
            Assert.True(payload.GetProperty("moveItSaved").GetBoolean());
            if (failure == "uncertain") { Assert.True(payload.GetProperty("outcomeUnknown").GetBoolean()); Assert.Equal(new[] { "validate", "PUT" }, calls); }
        }
    }
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
