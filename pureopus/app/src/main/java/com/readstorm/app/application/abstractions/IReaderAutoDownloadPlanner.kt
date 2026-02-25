package com.readstorm.app.application.abstractions

import com.readstorm.app.domain.models.ReaderAutoDownloadPlan

interface IReaderAutoDownloadPlanner {
    suspend fun buildPlan(
        bookId: String,
        anchorChapterIndex: Int,
        batchSize: Int,
        lowWatermark: Int
    ): ReaderAutoDownloadPlan
}
