using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;
using MoveItAutomationUI.Models;

namespace MoveItAutomationUI.Services.Reporting;

// A single active IIS instance bounds CSV memory and MOVEit generation independently of browser sessions.
public sealed class ReportApiRuntime : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 16 * 1024 * 1024 });
    private readonly ConcurrentDictionary<string, byte> _active = new();
    private readonly SemaphoreSlim _slots = new(2, 2);

    public bool TryGet(string key, out ReportApiCsv? result) => _cache.TryGetValue(key, out result);

    public void Store(string key, ReportApiCsv result)
    {
        if (result.Content.Length <= 4 * 1024 * 1024)
            _cache.Set(key, result, new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromSeconds(30)).SetSize(result.Content.Length + 1024));
    }

    public bool TryBegin(string reportId)
    {
        if (!_slots.Wait(0)) return false;
        if (_active.TryAdd(reportId, 0)) return true;
        _slots.Release();
        return false;
    }

    public void End(string reportId)
    {
        _active.TryRemove(reportId, out _);
        _slots.Release();
    }

    public void Dispose()
    {
        _cache.Dispose();
        _slots.Dispose();
    }
}
