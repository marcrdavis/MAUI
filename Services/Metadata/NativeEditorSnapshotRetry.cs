// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

using MoveItAutomationUI.Services.MoveIt;

namespace MoveItAutomationUI.Services.Metadata;

/// <summary>
/// Applies one short retry to read-only MOVEit COM snapshot capture used by the native-editor workflow.
/// This absorbs brief COM/API contention around editor launch, route rebinding, and post-save Undo capture without masking persistent failures.
/// </summary>
public static class NativeEditorSnapshotRetry
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(1500);

    public static async Task<T> CaptureAsync<T>(
        Func<CancellationToken, Task<T>> capture,
        string objectType,
        string objectId,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            return await capture(cancellationToken);
        }
        catch (MoveItComException ex)
        {
            logger.LogInformation(ex,
                "MOVEit {ObjectType} {ObjectId} snapshot failed during the native-editor workflow; retrying once after {DelayMilliseconds} ms.",
                objectType, objectId, (int)RetryDelay.TotalMilliseconds);
            await Task.Delay(RetryDelay, cancellationToken);
            return await capture(cancellationToken);
        }
    }
}
