package com.readstorm.app.domain.models

import java.util.UUID

data class BookRecord(
    val id: UUID = UUID.randomUUID(),
    val title: String = "",
    val author: String = "",
    val sourceId: Int = 0,
    var filePath: String = "",
    var format: String = "txt",
    val addedAt: Long = System.currentTimeMillis(),
    var totalChapters: Int = 0,
    var progress: ReadingProgress? = null
) {
    data class ReadingProgress(
        var currentChapterIndex: Int = 0,
        var currentChapterTitle: String = "",
        var scrollPercent: Double = 0.0,
        var lastReadAt: Long = System.currentTimeMillis()
    )
}
