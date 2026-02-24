package com.readstorm.app.infrastructure.services

import android.content.ContentValues
import android.content.Context
import android.database.Cursor
import android.database.sqlite.SQLiteDatabase
import android.database.sqlite.SQLiteOpenHelper
import com.readstorm.app.application.abstractions.IBookRepository
import com.readstorm.app.domain.models.BookEntity
import com.readstorm.app.domain.models.ChapterEntity
import com.readstorm.app.domain.models.ChapterStatus
import com.readstorm.app.domain.models.ReadingBookmarkEntity
import com.readstorm.app.domain.models.ReadingStateEntity
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

class SqliteBookRepository(context: Context) : IBookRepository {

    private val dbHelper = BookDatabaseHelper(context)

    // ── Books ──────────────────────────────────────────────────

    override suspend fun getBook(bookId: String): BookEntity? = withContext(Dispatchers.IO) {
        dbHelper.readableDatabase.query(
            "books", null, "id = ?", arrayOf(bookId), null, null, null
        ).use { cursor ->
            if (cursor.moveToFirst()) cursor.toBookEntity() else null
        }
    }

    override suspend fun getAllBooks(): List<BookEntity> = withContext(Dispatchers.IO) {
        val books = mutableListOf<BookEntity>()
        dbHelper.readableDatabase.query(
            "books", null, null, null, null, null, "updated_at DESC"
        ).use { cursor ->
            while (cursor.moveToNext()) {
                books.add(cursor.toBookEntity())
            }
        }
        books
    }

    override suspend fun findBook(title: String, author: String): BookEntity? =
        withContext(Dispatchers.IO) {
            dbHelper.readableDatabase.query(
                "books", null, "title = ? AND author = ?",
                arrayOf(title, author), null, null, null
            ).use { cursor ->
                if (cursor.moveToFirst()) cursor.toBookEntity() else null
            }
        }

    override suspend fun upsertBook(book: BookEntity): Unit = withContext(Dispatchers.IO) {
        val db = dbHelper.writableDatabase
        val cv = ContentValues().apply {
            put("id", book.id)
            put("title", book.title)
            put("author", book.author)
            put("source_id", book.sourceId)
            put("toc_url", book.tocUrl)
            put("total_chapters", book.totalChapters)
            put("done_chapters", book.doneChapters)
            put("read_chapter_index", book.readChapterIndex)
            put("read_chapter_title", book.readChapterTitle)
            put("created_at", book.createdAt)
            put("updated_at", book.updatedAt)
            put("read_at", book.readAt)
            put("cover_url", book.coverUrl)
            put("cover_image", book.coverImage)
            put("cover_blob", book.coverBlob)
            put("cover_rule", book.coverRule)
        }
        db.insertWithOnConflict("books", null, cv, SQLiteDatabase.CONFLICT_REPLACE)
    }

    override suspend fun deleteBook(bookId: String): Unit = withContext(Dispatchers.IO) {
        val db = dbHelper.writableDatabase
        db.beginTransaction()
        try {
            db.delete("chapters", "book_id = ?", arrayOf(bookId))
            db.delete("reading_states", "book_id = ?", arrayOf(bookId))
            db.delete("reading_bookmarks", "book_id = ?", arrayOf(bookId))
            db.delete("books", "id = ?", arrayOf(bookId))
            db.setTransactionSuccessful()
        } finally {
            db.endTransaction()
        }
    }

    override suspend fun updateReadProgress(
        bookId: String,
        chapterIndex: Int,
        chapterTitle: String
    ): Unit = withContext(Dispatchers.IO) {
        val cv = ContentValues().apply {
            put("read_chapter_index", chapterIndex)
            put("read_chapter_title", chapterTitle)
            put("read_at", System.currentTimeMillis())
            put("updated_at", System.currentTimeMillis())
        }
        dbHelper.writableDatabase.update("books", cv, "id = ?", arrayOf(bookId))
    }

    // ── Chapters ───────────────────────────────────────────────

    override suspend fun getChapters(bookId: String): List<ChapterEntity> =
        withContext(Dispatchers.IO) {
            val chapters = mutableListOf<ChapterEntity>()
            dbHelper.readableDatabase.query(
                "chapters", null, "book_id = ?", arrayOf(bookId),
                null, null, "index_no ASC"
            ).use { cursor ->
                while (cursor.moveToNext()) {
                    chapters.add(cursor.toChapterEntity())
                }
            }
            chapters
        }

    override suspend fun getChapter(bookId: String, chapterIndex: Int): ChapterEntity? =
        withContext(Dispatchers.IO) {
            dbHelper.readableDatabase.query(
                "chapters", null, "book_id = ? AND index_no = ?",
                arrayOf(bookId, chapterIndex.toString()), null, null, null
            ).use { cursor ->
                if (cursor.moveToFirst()) cursor.toChapterEntity() else null
            }
        }

    override suspend fun getChaptersByStatus(
        bookId: String,
        status: ChapterStatus
    ): List<ChapterEntity> = withContext(Dispatchers.IO) {
        val chapters = mutableListOf<ChapterEntity>()
        dbHelper.readableDatabase.query(
            "chapters", null, "book_id = ? AND status = ?",
            arrayOf(bookId, status.value.toString()),
            null, null, "index_no ASC"
        ).use { cursor ->
            while (cursor.moveToNext()) {
                chapters.add(cursor.toChapterEntity())
            }
        }
        chapters
    }

    override suspend fun insertChapters(
        bookId: String,
        chapters: List<ChapterEntity>
    ): Unit = withContext(Dispatchers.IO) {
        val db = dbHelper.writableDatabase
        db.beginTransaction()
        try {
            for (chapter in chapters) {
                val cv = ContentValues().apply {
                    put("book_id", bookId)
                    put("index_no", chapter.indexNo)
                    put("title", chapter.title)
                    put("content", chapter.content)
                    put("status", chapter.status.value)
                    put("source_id", chapter.sourceId)
                    put("source_url", chapter.sourceUrl)
                    put("error", chapter.error)
                    put("updated_at", chapter.updatedAt)
                }
                db.insertWithOnConflict(
                    "chapters", null, cv, SQLiteDatabase.CONFLICT_IGNORE
                )
            }
            db.setTransactionSuccessful()
        } finally {
            db.endTransaction()
        }
    }

    override suspend fun updateChapter(
        bookId: String,
        chapterIndex: Int,
        status: ChapterStatus,
        content: String?,
        errorMessage: String?
    ): Unit = withContext(Dispatchers.IO) {
        val cv = ContentValues().apply {
            put("status", status.value)
            put("content", content)
            put("error", errorMessage)
            put("updated_at", System.currentTimeMillis())
        }
        dbHelper.writableDatabase.update(
            "chapters", cv, "book_id = ? AND index_no = ?",
            arrayOf(bookId, chapterIndex.toString())
        )
    }

    override suspend fun updateChapterSource(
        bookId: String,
        chapterIndex: Int,
        sourceId: Int,
        sourceUrl: String
    ): Unit = withContext(Dispatchers.IO) {
        val cv = ContentValues().apply {
            put("source_id", sourceId)
            put("source_url", sourceUrl)
            put("updated_at", System.currentTimeMillis())
        }
        dbHelper.writableDatabase.update(
            "chapters", cv, "book_id = ? AND index_no = ?",
            arrayOf(bookId, chapterIndex.toString())
        )
    }

    override suspend fun countDoneChapters(bookId: String): Int = withContext(Dispatchers.IO) {
        dbHelper.readableDatabase.rawQuery(
            "SELECT COUNT(*) FROM chapters WHERE book_id = ? AND status = ?",
            arrayOf(bookId, ChapterStatus.Done.value.toString())
        ).use { cursor ->
            if (cursor.moveToFirst()) cursor.getInt(0) else 0
        }
    }

    override suspend fun getDoneChapterContents(bookId: String): List<ChapterEntity> =
        withContext(Dispatchers.IO) {
            val chapters = mutableListOf<ChapterEntity>()
            dbHelper.readableDatabase.query(
                "chapters", null, "book_id = ? AND status = ?",
                arrayOf(bookId, ChapterStatus.Done.value.toString()),
                null, null, "index_no ASC"
            ).use { cursor ->
                while (cursor.moveToNext()) {
                    chapters.add(cursor.toChapterEntity())
                }
            }
            chapters
        }

    // ── Reading state ──────────────────────────────────────────

    override suspend fun getReadingState(bookId: String): ReadingStateEntity? =
        withContext(Dispatchers.IO) {
            dbHelper.readableDatabase.query(
                "reading_states", null, "book_id = ?", arrayOf(bookId),
                null, null, null
            ).use { cursor ->
                if (cursor.moveToFirst()) cursor.toReadingStateEntity() else null
            }
        }

    override suspend fun upsertReadingState(state: ReadingStateEntity): Unit =
        withContext(Dispatchers.IO) {
            val cv = ContentValues().apply {
                put("book_id", state.bookId)
                put("chapter_index", state.chapterIndex)
                put("page_index", state.pageIndex)
                put("anchor_text", state.anchorText)
                put("layout_fingerprint", state.layoutFingerprint)
                put("updated_at", state.updatedAt)
            }
            dbHelper.writableDatabase.insertWithOnConflict(
                "reading_states", null, cv, SQLiteDatabase.CONFLICT_REPLACE
            )
        }

    // ── Bookmarks ──────────────────────────────────────────────

    override suspend fun getReadingBookmarks(bookId: String): List<ReadingBookmarkEntity> =
        withContext(Dispatchers.IO) {
            val bookmarks = mutableListOf<ReadingBookmarkEntity>()
            dbHelper.readableDatabase.query(
                "reading_bookmarks", null, "book_id = ?", arrayOf(bookId),
                null, null, "created_at DESC"
            ).use { cursor ->
                while (cursor.moveToNext()) {
                    bookmarks.add(cursor.toReadingBookmarkEntity())
                }
            }
            bookmarks
        }

    override suspend fun upsertReadingBookmark(bookmark: ReadingBookmarkEntity): Unit =
        withContext(Dispatchers.IO) {
            val cv = ContentValues().apply {
                put("book_id", bookmark.bookId)
                put("chapter_index", bookmark.chapterIndex)
                put("page_index", bookmark.pageIndex)
                put("chapter_title", bookmark.chapterTitle)
                put("preview_text", bookmark.previewText)
                put("anchor_text", bookmark.anchorText)
                put("created_at", bookmark.createdAt)
            }
            dbHelper.writableDatabase.insertWithOnConflict(
                "reading_bookmarks", null, cv, SQLiteDatabase.CONFLICT_REPLACE
            )
        }

    override suspend fun deleteReadingBookmark(
        bookId: String,
        chapterIndex: Int,
        pageIndex: Int
    ): Unit = withContext(Dispatchers.IO) {
        dbHelper.writableDatabase.delete(
            "reading_bookmarks",
            "book_id = ? AND chapter_index = ? AND page_index = ?",
            arrayOf(bookId, chapterIndex.toString(), pageIndex.toString())
        )
    }

    // ── Cursor mappers ─────────────────────────────────────────

    private fun Cursor.toBookEntity() = BookEntity(
        id = getString(getColumnIndexOrThrow("id")),
        title = getString(getColumnIndexOrThrow("title")),
        author = getString(getColumnIndexOrThrow("author")),
        sourceId = getInt(getColumnIndexOrThrow("source_id")),
        tocUrl = getString(getColumnIndexOrThrow("toc_url")),
        totalChapters = getInt(getColumnIndexOrThrow("total_chapters")),
        doneChapters = getInt(getColumnIndexOrThrow("done_chapters")),
        readChapterIndex = getInt(getColumnIndexOrThrow("read_chapter_index")),
        readChapterTitle = getString(getColumnIndexOrThrow("read_chapter_title")),
        createdAt = getLong(getColumnIndexOrThrow("created_at")),
        updatedAt = getLong(getColumnIndexOrThrow("updated_at")),
        readAt = getLong(getColumnIndexOrThrow("read_at")),
        coverUrl = getStringOrNull(getColumnIndexOrThrow("cover_url")),
        coverImage = getStringOrNull(getColumnIndexOrThrow("cover_image")),
        coverBlob = getBlobOrNull(getColumnIndexOrThrow("cover_blob")),
        coverRule = getStringOrNull(getColumnIndexOrThrow("cover_rule"))
    )

    private fun Cursor.toChapterEntity() = ChapterEntity(
        id = getLong(getColumnIndexOrThrow("id")),
        bookId = getString(getColumnIndexOrThrow("book_id")),
        indexNo = getInt(getColumnIndexOrThrow("index_no")),
        title = getString(getColumnIndexOrThrow("title")),
        content = getStringOrNull(getColumnIndexOrThrow("content")),
        status = ChapterStatus.fromValue(getInt(getColumnIndexOrThrow("status"))),
        sourceId = getInt(getColumnIndexOrThrow("source_id")),
        sourceUrl = getString(getColumnIndexOrThrow("source_url")),
        error = getStringOrNull(getColumnIndexOrThrow("error")),
        updatedAt = getLong(getColumnIndexOrThrow("updated_at"))
    )

    private fun Cursor.toReadingStateEntity() = ReadingStateEntity(
        bookId = getString(getColumnIndexOrThrow("book_id")),
        chapterIndex = getInt(getColumnIndexOrThrow("chapter_index")),
        pageIndex = getInt(getColumnIndexOrThrow("page_index")),
        anchorText = getStringOrNull(getColumnIndexOrThrow("anchor_text")),
        layoutFingerprint = getStringOrNull(getColumnIndexOrThrow("layout_fingerprint")),
        updatedAt = getLong(getColumnIndexOrThrow("updated_at"))
    )

    private fun Cursor.toReadingBookmarkEntity() = ReadingBookmarkEntity(
        bookId = getString(getColumnIndexOrThrow("book_id")),
        chapterIndex = getInt(getColumnIndexOrThrow("chapter_index")),
        pageIndex = getInt(getColumnIndexOrThrow("page_index")),
        chapterTitle = getString(getColumnIndexOrThrow("chapter_title")),
        previewText = getStringOrNull(getColumnIndexOrThrow("preview_text")),
        anchorText = getStringOrNull(getColumnIndexOrThrow("anchor_text")),
        createdAt = getLong(getColumnIndexOrThrow("created_at"))
    )

    private fun Cursor.getStringOrNull(index: Int): String? =
        if (isNull(index)) null else getString(index)

    private fun Cursor.getBlobOrNull(index: Int): ByteArray? =
        if (isNull(index)) null else getBlob(index)

    // ── Database helper ────────────────────────────────────────

    private class BookDatabaseHelper(context: Context) :
        SQLiteOpenHelper(context, DATABASE_NAME, null, DATABASE_VERSION) {

        companion object {
            private const val DATABASE_NAME = "readstorm.db"
            private const val DATABASE_VERSION = 1
        }

        override fun onCreate(db: SQLiteDatabase) {
            db.execSQL(
                """
                CREATE TABLE IF NOT EXISTS books (
                    id TEXT PRIMARY KEY,
                    title TEXT NOT NULL DEFAULT '',
                    author TEXT NOT NULL DEFAULT '',
                    source_id INTEGER NOT NULL DEFAULT 0,
                    toc_url TEXT NOT NULL DEFAULT '',
                    total_chapters INTEGER NOT NULL DEFAULT 0,
                    done_chapters INTEGER NOT NULL DEFAULT 0,
                    read_chapter_index INTEGER NOT NULL DEFAULT 0,
                    read_chapter_title TEXT NOT NULL DEFAULT '',
                    created_at INTEGER NOT NULL DEFAULT 0,
                    updated_at INTEGER NOT NULL DEFAULT 0,
                    read_at INTEGER NOT NULL DEFAULT 0,
                    cover_url TEXT,
                    cover_image TEXT,
                    cover_blob BLOB,
                    cover_rule TEXT
                )
                """.trimIndent()
            )

            db.execSQL(
                """
                CREATE TABLE IF NOT EXISTS chapters (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    book_id TEXT NOT NULL DEFAULT '',
                    index_no INTEGER NOT NULL DEFAULT 0,
                    title TEXT NOT NULL DEFAULT '',
                    content TEXT,
                    status INTEGER NOT NULL DEFAULT 0,
                    source_id INTEGER NOT NULL DEFAULT 0,
                    source_url TEXT NOT NULL DEFAULT '',
                    error TEXT,
                    updated_at INTEGER NOT NULL DEFAULT 0,
                    UNIQUE(book_id, index_no)
                )
                """.trimIndent()
            )

            db.execSQL(
                """
                CREATE TABLE IF NOT EXISTS reading_states (
                    book_id TEXT PRIMARY KEY,
                    chapter_index INTEGER NOT NULL DEFAULT 0,
                    page_index INTEGER NOT NULL DEFAULT 0,
                    anchor_text TEXT,
                    layout_fingerprint TEXT,
                    updated_at INTEGER NOT NULL DEFAULT 0
                )
                """.trimIndent()
            )

            db.execSQL(
                """
                CREATE TABLE IF NOT EXISTS reading_bookmarks (
                    book_id TEXT NOT NULL DEFAULT '',
                    chapter_index INTEGER NOT NULL DEFAULT 0,
                    page_index INTEGER NOT NULL DEFAULT 0,
                    chapter_title TEXT NOT NULL DEFAULT '',
                    preview_text TEXT,
                    anchor_text TEXT,
                    created_at INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY(book_id, chapter_index, page_index)
                )
                """.trimIndent()
            )

            db.execSQL(
                "CREATE INDEX IF NOT EXISTS idx_chapters_book_id ON chapters(book_id)"
            )
            db.execSQL(
                "CREATE INDEX IF NOT EXISTS idx_bookmarks_book_id ON reading_bookmarks(book_id)"
            )
        }

        override fun onUpgrade(db: SQLiteDatabase, oldVersion: Int, newVersion: Int) {
            // Future migrations go here
        }
    }
}
