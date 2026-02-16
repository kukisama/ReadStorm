using ReadStorm.Domain.Models;

namespace ReadStorm.Application.Abstractions;

/// <summary>快速测试书源可达性（关键词探活请求，默认 2-4 秒内返回）。</summary>
public interface ISourceHealthCheckUseCase
{
    /// <summary>
    /// 并发测试所有书源，返回每个书源的 (Id, IsReachable) 结果。
    /// 每个书源通过规则里的 search 请求进行探活；不再使用 HEAD。
    /// </summary>
    Task<IReadOnlyList<SourceHealthResult>> CheckAllAsync(
        IReadOnlyList<BookSourceRule> sources,
        CancellationToken cancellationToken = default);
}

/// <summary>单个书源的健康检测结果。</summary>
public sealed record SourceHealthResult(int SourceId, bool IsReachable);
