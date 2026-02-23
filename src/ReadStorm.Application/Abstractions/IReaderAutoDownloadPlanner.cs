using ReadStorm.Domain.Models;

namespace ReadStorm.Application.Abstractions;

/// <summary>
/// 根据阅读位置和章节状态生成自动下载计划。
/// </summary>
public interface IReaderAutoDownloadPlanner
{
    Task<ReaderAutoDownloadPlan> BuildPlanAsync(
        string bookId,
        int anchorChapterIndex,
        int batchSize,
        int lowWatermark,
        CancellationToken cancellationToken = default);
}
