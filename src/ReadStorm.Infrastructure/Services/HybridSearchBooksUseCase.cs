using ReadStorm.Application.Abstractions;
using ReadStorm.Domain.Models;

namespace ReadStorm.Infrastructure.Services;

public sealed class HybridSearchBooksUseCase : ISearchBooksUseCase
{
    private readonly RuleBasedSearchBooksUseCase _ruleBased = new();
    private readonly IRuleCatalogUseCase _catalog;

    /// <summary>最多同时并发搜索的书源数量。</summary>
    private const int MaxConcurrentSources = 5;

    /// <summary>全部书源搜索的单源超时。</summary>
    private static readonly TimeSpan PerSourceTimeout = TimeSpan.FromSeconds(12);

    public HybridSearchBooksUseCase(IRuleCatalogUseCase catalog)
    {
        _catalog = catalog;
    }

    public async Task<IReadOnlyList<SearchResult>> ExecuteAsync(
        string keyword,
        int? sourceId = null,
        CancellationToken cancellationToken = default)
    {
        // 指定具体书源时只搜该书源
        if (sourceId is > 0)
        {
            try
            {
                return await _ruleBased.ExecuteAsync(keyword, sourceId, cancellationToken);
            }
            catch
            {
                return [];
            }
        }

        // "全部书源"：并发搜索所有支持搜索的真实书源，汇总去重
        return await SearchAllSourcesAsync(keyword, cancellationToken);
    }

    private async Task<IReadOnlyList<SearchResult>> SearchAllSourcesAsync(
        string keyword, CancellationToken cancellationToken)
    {
        var allRules = await _catalog.GetAllAsync(cancellationToken);
        var searchableRules = allRules
            .Where(r => r.SearchSupported && r.Id > 0)
            .ToList();

        if (searchableRules.Count == 0)
        {
            return [];
        }

        var semaphore = new SemaphoreSlim(MaxConcurrentSources);

        var tasks = searchableRules.Select(async rule =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                using var perSourceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                perSourceCts.CancelAfter(PerSourceTimeout);
                return await _ruleBased.ExecuteAsync(keyword, rule.Id, perSourceCts.Token);
            }
            catch
            {
                // 单个书源失败不影响整体
                return (IReadOnlyList<SearchResult>)[];
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        var batchResults = await Task.WhenAll(tasks);

        // 按书名+作者去重，保留来源 SourceId
        var merged = batchResults
            .SelectMany(x => x)
            .GroupBy(x => $"{x.Title}|{x.Author}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(100)
            .ToList();

        return merged;
    }
}