using System.Collections.Concurrent;

namespace ReadStorm.Infrastructure.Services;

/// <summary>
/// 按书源串行化下载队列。同一书源的下载任务串行排队，不同书源可并行。
/// </summary>
public sealed class SourceDownloadQueue : IDisposable
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();

    /// <summary>
    /// 在指定书源的串行队列中执行任务。
    /// 同一 sourceId 同时只有一个任务在运行；不同 sourceId 可同时运行。
    /// </summary>
    public async Task<T> EnqueueAsync<T>(int sourceId, Func<Task<T>> work, CancellationToken ct = default)
    {
        var semaphore = _locks.GetOrAdd(sourceId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        try
        {
            return await work();
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// 在指定书源的串行队列中执行任务（无返回值）。
    /// </summary>
    public async Task EnqueueAsync(int sourceId, Func<Task> work, CancellationToken ct = default)
    {
        var semaphore = _locks.GetOrAdd(sourceId, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        try
        {
            await work();
        }
        finally
        {
            semaphore.Release();
        }
    }

    public void Dispose()
    {
        foreach (var semaphore in _locks.Values)
        {
            semaphore.Dispose();
        }
        _locks.Clear();
    }
}
