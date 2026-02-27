package com.readstorm.app.application.abstractions

import com.readstorm.app.domain.models.BookEntity
import com.readstorm.app.domain.models.ChapterEntity
import com.readstorm.app.domain.models.ChapterStatus
import com.readstorm.app.domain.models.ReadingBookmarkEntity
import com.readstorm.app.domain.models.ReadingStateEntity

interface IBookRepository {

    // ── Books ────────────────────────────────────────────────

    suspend fun getBook(bookId: String): BookEntity?

    suspend fun getAllBooks(): List<BookEntity>

    suspend fun findBook(title: String, author: String): BookEntity?

    suspend fun upsertBook(book: BookEntity)

    suspend fun deleteBook(bookId: String)

    suspend fun updateReadProgress(bookId: String, chapterIndex: Int, chapterTitle: String)

    // ── Chapters ─────────────────────────────────────────────

    suspend fun getChapters(bookId: String): List<ChapterEntity>

    suspend fun getChapter(bookId: String, chapterIndex: Int): ChapterEntity?

    suspend fun getChaptersByStatus(bookId: String, status: ChapterStatus): List<ChapterEntity>

    suspend fun insertChapters(bookId: String, chapters: List<ChapterEntity>)

    suspend fun updateChapter(
        bookId: String,
        chapterIndex: Int,
        status: ChapterStatus,
        content: String?,
        errorMessage: String?
    )

    suspend fun updateChapterSource(
        bookId: String,
        chapterIndex: Int,
        sourceId: Int,
        sourceUrl: String
    )

    suspend fun countDoneChapters(bookId: String): Int

    suspend fun getDoneChapterContents(bookId: String): List<ChapterEntity>

    // ── Reading state ────────────────────────────────────────

    suspend fun getReadingState(bookId: String): ReadingStateEntity?

    suspend fun upsertReadingState(state: ReadingStateEntity)

    // ── Bookmarks ────────────────────────────────────────────

    suspend fun getReadingBookmarks(bookId: String): List<ReadingBookmarkEntity>

    suspend fun upsertReadingBookmark(bookmark: ReadingBookmarkEntity)

    suspend fun deleteReadingBookmark(bookId: String, chapterIndex: Int, pageIndex: Int)

    // ── Maintenance ────────────────────────────────────────────

    /** Execute WAL checkpoint to merge write-ahead log into main database file. */
    suspend fun walCheckpoint()
}
