using ReadStorm.Application.Abstractions;
using ReadStorm.Domain.Models;

namespace ReadStorm.Infrastructure.Services;

public sealed class ReaderAutoDownloadPlanner(IBookRepository bookRepository) : IReaderAutoDownloadPlanner
{
    public async Task<ReaderAutoDownloadPlan> BuildPlanAsync(
        string bookId,
        int anchorChapterIndex,
        int batchSize,
        int lowWatermark,
        CancellationToken cancellationToken = default)
    {
        var chapters = await bookRepository.GetChaptersAsync(bookId, cancellationToken);
        if (chapters.Count == 0)
        {
            return new ReaderAutoDownloadPlan();
        }

        var anchor = Math.Clamp(anchorChapterIndex, 0, chapters.Count - 1);
        var normalizedBatch = Math.Max(1, batchSize);
        var normalizedLowWatermark = Math.Max(1, lowWatermark);

        var consecutiveDone = 0;
        for (var i = anchor; i < chapters.Count; i++)
        {
            if (chapters[i].Status != ChapterStatus.Done)
            {
                break;
            }

            consecutiveDone++;
        }

        var anchorDone = chapters[anchor].Status == ChapterStatus.Done;
        var shouldQueueWindow = !anchorDone || consecutiveDone < normalizedLowWatermark;

        // 缺口定义：Pending/Failed/Downloading 都视为未就绪。
        var firstGap = chapters
            .Where(c => c.Status != ChapterStatus.Done)
            .Select(c => c.IndexNo)
            .DefaultIfEmpty(-1)
            .Min();

        var start = anchor;
        var maxStart = Math.Max(0, chapters.Count - 1);
        if (start > maxStart)
        {
            start = maxStart;
        }

        var take = Math.Min(normalizedBatch, chapters.Count - start);

        return new ReaderAutoDownloadPlan
        {
            ShouldQueueWindow = shouldQueueWindow,
            WindowStartIndex = start,
            WindowTakeCount = Math.Max(0, take),
            ConsecutiveDoneAfterAnchor = consecutiveDone,
            HasGap = firstGap >= 0,
            FirstGapIndex = firstGap,
        };
    }
}
