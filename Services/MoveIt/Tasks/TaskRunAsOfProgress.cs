using System.Collections.Concurrent;

namespace MoveItAutomationUI.Services.MoveIt.Tasks;

public sealed record TaskRunAsOfState(string JobId, string TaskId, string Message, int Progress, bool Completed = false, bool Restored = false, bool Success = false, string RunStatus = "");
public sealed class TaskRunAsOfProgress
{
    private sealed record Entry(string Owner, DateTimeOffset Created, TaskRunAsOfState State);
    private readonly ConcurrentDictionary<string, Entry> _jobs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _tasks = new(StringComparer.Ordinal);
    public void Begin(string owner, string taskKey, string jobId, string taskId)
    {
        foreach (var item in _jobs.Where(pair => pair.Value.State.Completed && pair.Value.Created < DateTimeOffset.UtcNow.AddDays(-1))) _jobs.TryRemove(item.Key, out _);
        if (!_tasks.TryAdd(taskKey, jobId)) throw new InvalidOperationException("Run As Of is already active for this Task.");
        if (!_jobs.TryAdd(jobId, new(owner, DateTimeOffset.UtcNow, new(jobId, taskId, "Preparing Task…", 5))))
        { _tasks.TryRemove(taskKey, out _); throw new InvalidOperationException("This Run As Of request was already submitted. Inspect its progress before starting another run."); }
    }
    public void Set(string jobId, Func<TaskRunAsOfState, TaskRunAsOfState> update)
    { if (_jobs.TryGetValue(jobId, out var entry)) _jobs[jobId] = entry with { State = update(entry.State) }; }
    public TaskRunAsOfState? Get(string owner, string jobId) => _jobs.TryGetValue(jobId, out var entry) && entry.Owner == owner ? entry.State : null;
    public void Release(string taskKey) => _tasks.TryRemove(taskKey, out _);
}
