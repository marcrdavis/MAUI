// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using System.Text.Json.Nodes;

using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Tasks;
using static MoveItAutomationUI.Services.MoveIt.TraditionalTasks.TraditionalTaskDefinitionEditorOperations;

namespace MoveItAutomationUI.Services.MoveIt.TraditionalTasks;

public interface ITraditionalTaskDefinitionPatchService
{
    Task ApplyEditorPatchAsync(string taskId, JsonObject request, CancellationToken cancellationToken = default);
}

public sealed class TraditionalTaskPatchPartialSaveException : InvalidOperationException
{
    public TraditionalTaskPatchPartialSaveException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

// Compatibility adapter for existing Traditional callers. Native patch/save logic is shared.
public sealed class TraditionalTaskDefinitionPatchService : ITraditionalTaskDefinitionPatchService
{
    private readonly ITaskDefinitionPatchService _shared;
    public TraditionalTaskDefinitionPatchService(IMoveItRestClient restClient, ITaskCrudCoordinator taskCrud, ITraditionalTaskDefinitionComposer composer)
        => _shared = new TaskDefinitionPatchService(restClient, taskCrud, composer);
    public Task ApplyEditorPatchAsync(string taskId, JsonObject request, CancellationToken cancellationToken = default)
        => _shared.ApplyEditorPatchAsync(taskId, request, cancellationToken);
}
