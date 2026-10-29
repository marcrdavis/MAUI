// Author: Marc R. Davis (marcdavis@comcast.net)
// Project: MAUI - MOVEit Automation UI
// This source file is part of the MAUI application.

namespace MoveItAutomationUI.Services.Concurrency;

/// <summary>
/// Serializes asynchronous work by string key while allowing unused key entries to be reclaimed safely.
/// Holders and waiters are reference-counted under a short synchronous registry lock so one active key
/// can never be represented by two different semaphores.
/// </summary>
internal sealed class KeyedAsyncLock
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _entries;

    public KeyedAsyncLock(IEqualityComparer<string> comparer)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        _entries = new Dictionary<string, Entry>(comparer);
    }

    // Acquires the gate for one key. The returned lease releases both the semaphore and the key reference.
    public async ValueTask<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Lock key is required.", nameof(key));
        }

        Entry entry;
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                entry = existing;
            }
            else
            {
                entry = new Entry();
                _entries.Add(key, entry);
            }

            checked
            {
                entry.ReferenceCount++;
            }
        }

        try
        {
            await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new Lease(this, key, entry);
        }
        catch
        {
            ReleaseReference(key, entry);
            throw;
        }
    }

    private void Release(string key, Entry entry)
    {
        entry.Gate.Release();
        ReleaseReference(key, entry);
    }

    private void ReleaseReference(string key, Entry entry)
    {
        var dispose = false;
        lock (_sync)
        {
            if (entry.ReferenceCount <= 0)
            {
                throw new InvalidOperationException("Keyed async lock reference count is invalid.");
            }

            entry.ReferenceCount--;
            if (entry.ReferenceCount == 0)
            {
                if (!_entries.TryGetValue(key, out var current) || !ReferenceEquals(current, entry))
                {
                    throw new InvalidOperationException("Keyed async lock registry is inconsistent.");
                }

                _entries.Remove(key);
                dispose = true;
            }
        }

        if (dispose)
        {
            entry.Gate.Dispose();
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public int ReferenceCount { get; set; }
    }

    private sealed class Lease : IDisposable
    {
        private readonly KeyedAsyncLock _owner;
        private readonly Entry _entry;
        private readonly string _key;
        private int _disposed;

        public Lease(KeyedAsyncLock owner, string key, Entry entry)
        {
            _owner = owner;
            _key = key;
            _entry = entry;
        }

        public void Dispose()
        {
            // One atomic disposal flag prevents two concurrent Dispose calls from splitting
            // ownership of the owner/entry state and accidentally losing the release.
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _owner.Release(_key, _entry);
        }
    }
}
