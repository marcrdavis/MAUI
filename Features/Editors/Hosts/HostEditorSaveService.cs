// Thin optional editor adapter; native Host mutation belongs to the shared Host layer.
using MoveItAutomationUI.Services.MoveIt;
using MoveItAutomationUI.Services.MoveIt.Hosts;

namespace MoveItAutomationUI.Features.Editors.Hosts;

public interface IHostEditorSaveService
{
    Task<HostEditorMoveItSaveResult> SaveMoveItAsync(string hostId, HostEditorSaveRequest request,
        string username, CancellationToken cancellationToken = default);
}

public sealed class HostEditorSaveService : IHostEditorSaveService
{
    private readonly IAutomationHostService _hosts;
    public HostEditorSaveService(IAutomationHostService hosts) => _hosts = hosts;

    public async Task<HostEditorMoveItSaveResult> SaveMoveItAsync(string hostId, HostEditorSaveRequest request,
        string username, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await _hosts.SaveDefinitionAsync(hostId,
            new HostDefinitionSaveRequest(request.Name, request.Description, request.Configuration), username, cancellationToken);
        return new HostEditorMoveItSaveResult(result.Changed, result.HostName, result.TypeCode, result.UndoSnapshotId);
    }
}
