// A row-local opt-in: ordinary MOVEit editing and other callers keep their existing retry behavior.
namespace MoveItAutomationUI.Services.MoveIt;

internal sealed class MoveItImportRowRetryScope : IDisposable
{
    private static readonly AsyncLocal<MoveItImportRowRetryScope?> Active = new();
    private readonly MoveItImportRowRetryScope? _previous;
    private bool _disposed;

    private MoveItImportRowRetryScope()
    {
        _previous = Active.Value;
        Active.Value = this;
    }

    internal static bool IsEnabled => Active.Value is { } scope && !scope._disposed;
    internal static IDisposable Enter() => new MoveItImportRowRetryScope();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Active.Value = _previous;
    }
}
