// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// Optional editor adapter: all Task definition patching and persistence is shared.

using System.Text.Json.Nodes;
using MoveItAutomationUI.Services.MoveIt.TraditionalTasks;
using MoveItAutomationUI.Services.MoveIt.Tasks;

namespace MoveItAutomationUI.Services.MoveIt;

public interface ITraditionalTaskEditorSaveService
{
    Task SaveAsync(string taskId, JsonObject request, CancellationToken cancellationToken = default);
}

public sealed class TraditionalTaskEditorPartialSaveException : InvalidOperationException
{
    public TraditionalTaskEditorPartialSaveException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// Thin editor integration seam. The shared Task layer owns Step creation,
/// field preservation, validation, REST update/readback, and native schedules.
/// </summary>
public sealed class TraditionalTaskEditorSaveService : ITraditionalTaskEditorSaveService
{
    private readonly ITaskDefinitionPatchService _tasks;

    public TraditionalTaskEditorSaveService(ITaskDefinitionPatchService tasks)
        => _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));

    public async Task SaveAsync(string taskId, JsonObject request, CancellationToken cancellationToken = default)
    {
        try
        {
            await _tasks.ApplyEditorPatchAsync(taskId, request, cancellationToken);
        }
        catch (TraditionalTaskPatchPartialSaveException ex)
        {
            // Preserve the controller's existing partial-save error contract.
            throw new TraditionalTaskEditorPartialSaveException(ex.Message, ex);
        }
    }
}

