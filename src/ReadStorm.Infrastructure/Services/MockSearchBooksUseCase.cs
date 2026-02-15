using ReadStorm.Application.Abstractions;
using ReadStorm.Domain.Models;

namespace ReadStorm.Infrastructure.Services;

public sealed class MockSearchBooksUseCase : ISearchBooksUseCase
{
    private static readonly IReadOnlyList<SearchResult> Seed =
    [
        new(Guid.NewGuid(), "诡秘之主", "爱潜水的乌贼", 9, "https://example.com/book/guimi", "第一千三百九十四章 回归", DateTimeOffset.Now.AddDays(-1)),
        new(Guid.NewGuid(), "宿命之环", "爱潜水的乌贼", 20, "https://example.com/book/suming", "第九百九十八章 旧日回响", DateTimeOffset.Now.AddHours(-12)),
        new(Guid.NewGuid(), "道诡异仙", "狐尾的笔", 8, "https://example.com/book/daogui", "第九百二十章 心猿", DateTimeOffset.Now.AddHours(-5)),
        new(Guid.NewGuid(), "玄鉴仙族", "季越人", 17, "https://example.com/book/xuanjian", "第七百六十二章 海内局", DateTimeOffset.Now.AddHours(-8)),
    ];

    public async Task<IReadOnlyList<SearchResult>> ExecuteAsync(
        string keyword,
        int? sourceId = null,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(250, cancellationToken);

        if (string.IsNullOrWhiteSpace(keyword))
        {
            return [];
        }

        var query = Seed.AsEnumerable();
        if (sourceId is > 0)
        {
            query = query.Where(x => x.SourceId == sourceId.Value);
        }

        var results = query
            .Where(x => x.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                     || x.Author.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (results.Count > 0)
        {
            return results;
        }

        var fallbackSource = sourceId is > 0 ? sourceId.Value : 1;
        return
        [
            new SearchResult(Guid.NewGuid(), $"{keyword}（示例结果）", "ReadStorm", fallbackSource, "https://example.com/book/mock", "第1章", DateTimeOffset.Now),
        ];
    }
}
