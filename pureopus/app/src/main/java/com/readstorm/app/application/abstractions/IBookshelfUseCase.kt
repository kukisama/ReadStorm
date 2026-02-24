package com.readstorm.app.application.abstractions

import com.readstorm.app.domain.models.BookRecord
import java.util.UUID

interface IBookshelfUseCase {
    suspend fun getAll(): List<BookRecord>

    suspend fun add(book: BookRecord)

    suspend fun remove(bookId: UUID)

    suspend fun updateProgress(bookId: UUID, progress: BookRecord.ReadingProgress)
}
