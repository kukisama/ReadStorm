package com.readstorm.app.application.abstractions

import com.readstorm.app.domain.models.BookEntity
import com.readstorm.app.domain.models.DownloadMode
import com.readstorm.app.domain.models.DownloadTask
import com.readstorm.app.domain.models.SearchResult

interface IDownloadBookUseCase {
    suspend fun queue(task: DownloadTask, selectedBook: SearchResult, mode: DownloadMode)

    suspend fun checkNewChapters(book: BookEntity): Int

    /** Returns (success, content, message). */
    suspend fun fetchChapterFromSource(
        book: BookEntity,
        chapterTitle: String,
        sourceId: Int
    ): Triple<Boolean, String, String>
}
