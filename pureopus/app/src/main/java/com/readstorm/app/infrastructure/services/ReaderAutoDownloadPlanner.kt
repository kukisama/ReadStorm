package com.readstorm.app.infrastructure.services

import com.readstorm.app.application.abstractions.IBookRepository
import com.readstorm.app.application.abstractions.IReaderAutoDownloadPlanner
import com.readstorm.app.domain.models.ChapterStatus
import com.readstorm.app.domain.models.ReaderAutoDownloadPlan

class ReaderAutoDownloadPlanner(
    private val bookRepository: IBookRepository
) : IReaderAutoDownloadPlanner {

    override suspend fun buildPlan(
        bookId: String,
        anchorChapterIndex: Int,
        batchSize: Int,
        lowWatermark: Int
    ): ReaderAutoDownloadPlan {
        val chapters = bookRepository.getChapters(bookId)
        if (chapters.isEmpty()) return ReaderAutoDownloadPlan()

        val anchor = anchorChapterIndex.coerceIn(0, chapters.size - 1)
        val normalizedBatch = maxOf(1, batchSize)
        val normalizedLowWatermark = maxOf(1, lowWatermark)

        var consecutiveDone = 0
        for (i in anchor until chapters.size) {
            if (chapters[i].status != ChapterStatus.Done) break
            consecutiveDone++
        }

        val anchorDone = chapters[anchor].status == ChapterStatus.Done
        val shouldQueueWindow = !anchorDone || consecutiveDone < normalizedLowWatermark

        val firstGap = chapters
            .filter { it.status != ChapterStatus.Done }
            .minByOrNull { it.indexNo }
            ?.indexNo ?: -1

        val start = anchor.coerceAtMost(maxOf(0, chapters.size - 1))
        val take = minOf(normalizedBatch, chapters.size - start)

        return ReaderAutoDownloadPlan(
            shouldQueueWindow = shouldQueueWindow,
            windowStartIndex = start,
            windowTakeCount = maxOf(0, take),
            consecutiveDoneAfterAnchor = consecutiveDone,
            hasGap = firstGap >= 0,
            firstGapIndex = firstGap
        )
    }
}
