using Microsoft.Data.Sqlite;
using ReadStorm.Application.Abstractions;
using ReadStorm.Domain.Models;

namespace ReadStorm.Infrastructure.Services;

/// <summary>SQLite 实现的书籍 + 章节仓库。线程安全（WAL 模式）。</summary>
public sealed class SqliteBookRepository : IBookRepository, IDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public SqliteBookRepository(string? dbPath = null)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            var workDir = WorkDirectoryManager.GetCurrentWorkDirectoryFromSettings();
            dbPath = WorkDirectoryManager.GetDatabasePath(workDir);
        }

        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connectionString = $"Data Source={dbPath}";
        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;

            CREATE TABLE IF NOT EXISTS books (
                id              TEXT PRIMARY KEY,
                title           TEXT NOT NULL,
                author          TEXT,
                source_id       INTEGER DEFAULT 0,
                toc_url         TEXT DEFAULT '',
                total_chapters  INTEGER DEFAULT 0,
                done_chapters   INTEGER DEFAULT 0,
                read_chapter_index  INTEGER DEFAULT 0,
                read_chapter_title  TEXT DEFAULT '',
                created_at      TEXT NOT NULL,
                updated_at      TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS chapters (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                book_id     TEXT NOT NULL REFERENCES books(id) ON DELETE CASCADE,
                index_no    INTEGER NOT NULL,
                title       TEXT DEFAULT '',
                content     TEXT,
                status      INTEGER DEFAULT 0,
                source_id   INTEGER DEFAULT 0,
                source_url  TEXT DEFAULT '',
                error       TEXT,
                updated_at  TEXT NOT NULL,
                UNIQUE(book_id, index_no)
            );

            CREATE TABLE IF NOT EXISTS reading_states (
                book_id             TEXT PRIMARY KEY REFERENCES books(id) ON DELETE CASCADE,
                chapter_index       INTEGER NOT NULL DEFAULT 0,
                page_index          INTEGER NOT NULL DEFAULT 0,
                anchor_text         TEXT DEFAULT '',
                layout_fingerprint  TEXT DEFAULT '',
                updated_at          TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS reading_bookmarks (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                book_id          TEXT NOT NULL REFERENCES books(id) ON DELETE CASCADE,
                chapter_index    INTEGER NOT NULL,
                page_index       INTEGER NOT NULL,
                chapter_title    TEXT DEFAULT '',
                preview_text     TEXT DEFAULT '',
                anchor_text      TEXT DEFAULT '',
                created_at       TEXT NOT NULL,
                UNIQUE(book_id, chapter_index, page_index)
            );

            CREATE INDEX IF NOT EXISTS idx_chapters_book_status ON chapters(book_id, status);
            CREATE INDEX IF NOT EXISTS idx_bookmarks_book_created ON reading_bookmarks(book_id, created_at DESC);
            """;
        cmd.ExecuteNonQuery();

        // 增量迁移：添加新列（已存在则忽略）
        MigrateAddColumn(conn, "books", "read_at", "TEXT DEFAULT ''");
        MigrateAddColumn(conn, "books", "cover_url", "TEXT DEFAULT ''");
        MigrateAddColumn(conn, "books", "cover_image", "TEXT DEFAULT ''");
        MigrateAddColumn(conn, "books", "cover_blob", "BLOB");
        MigrateAddColumn(conn, "books", "cover_rule", "TEXT DEFAULT ''");
    }

    private static void MigrateAddColumn(SqliteConnection conn, string table, string column, string def)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {def}";
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException) { /* column already exists */ }
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON";
        cmd.ExecuteNonQuery();
        return conn;
    }

    // ==================== 书籍 ====================

    public async Task<BookEntity?> GetBookAsync(string bookId, CancellationToken ct = default)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM books WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", bookId);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        return reader.Read() ? MapBook(reader) : null;
    }

    public async Task<IReadOnlyList<BookEntity>> GetAllBooksAsync(CancellationToken ct = default)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM books ORDER BY updated_at DESC";
        using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<BookEntity>();
        while (reader.Read())
        {
            list.Add(MapBook(reader));
        }
        return list;
    }

    public async Task<BookEntity?> FindBookAsync(string title, string author, CancellationToken ct = default)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM books WHERE title = @title AND author = @author LIMIT 1";
        cmd.Parameters.AddWithValue("@title", title);
        cmd.Parameters.AddWithValue("@author", author);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        return reader.Read() ? MapBook(reader) : null;
    }

    public async Task UpsertBookAsync(BookEntity book, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            book.UpdatedAt = DateTimeOffset.Now.ToString("o");
            cmd.CommandText = @"
                INSERT INTO books (id, title, author, source_id, toc_url, total_chapters, done_chapters,
                                   read_chapter_index, read_chapter_title, created_at, updated_at, read_at, cover_url, cover_image, cover_blob, cover_rule)
                VALUES (@id, @title, @author, @sourceId, @tocUrl, @total, @done,
                        @readIdx, @readTitle, @createdAt, @updatedAt, @readAt, @coverUrl, @coverImage, @coverBlob, @coverRule)
                ON CONFLICT(id) DO UPDATE SET
                    title = excluded.title,
                    author = excluded.author,
                    source_id = excluded.source_id,
                    toc_url = excluded.toc_url,
                    total_chapters = excluded.total_chapters,
                    done_chapters = excluded.done_chapters,
                    read_chapter_index = excluded.read_chapter_index,
                    read_chapter_title = excluded.read_chapter_title,
                    updated_at = excluded.updated_at,
                    cover_url = CASE WHEN excluded.cover_url != '' THEN excluded.cover_url ELSE books.cover_url END,
                    cover_image = CASE WHEN excluded.cover_image != '' THEN excluded.cover_image ELSE books.cover_image END,
                        cover_blob = CASE WHEN length(excluded.cover_blob) > 0 THEN excluded.cover_blob ELSE books.cover_blob END,
                    cover_rule = excluded.cover_rule
            ";
            cmd.Parameters.AddWithValue("@id", book.Id);
            cmd.Parameters.AddWithValue("@title", book.Title);
            cmd.Parameters.AddWithValue("@author", book.Author);
            cmd.Parameters.AddWithValue("@sourceId", book.SourceId);
            cmd.Parameters.AddWithValue("@tocUrl", book.TocUrl);
            cmd.Parameters.AddWithValue("@total", book.TotalChapters);
            cmd.Parameters.AddWithValue("@done", book.DoneChapters);
            cmd.Parameters.AddWithValue("@readIdx", book.ReadChapterIndex);
            cmd.Parameters.AddWithValue("@readTitle", book.ReadChapterTitle);
            cmd.Parameters.AddWithValue("@createdAt", book.CreatedAt);
            cmd.Parameters.AddWithValue("@updatedAt", book.UpdatedAt);
            cmd.Parameters.AddWithValue("@readAt", book.ReadAt);
            cmd.Parameters.AddWithValue("@coverUrl", book.CoverUrl);
            cmd.Parameters.AddWithValue("@coverImage", book.CoverImage);
            var pCoverBlob = cmd.Parameters.Add("@coverBlob", SqliteType.Blob);
            pCoverBlob.Value = book.CoverBlob.Length > 0 ? book.CoverBlob : DBNull.Value;
            cmd.Parameters.AddWithValue("@coverRule", book.CoverRule);
            await cmd.ExecuteNonQueryAsync(ct);
            return;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task DeleteBookAsync(string bookId, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM books WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", bookId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task UpdateReadProgressAsync(string bookId, int chapterIndex, string chapterTitle, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE books SET read_chapter_index = @idx, read_chapter_title = @title,
                                 updated_at = @now, read_at = @now
                WHERE id = @id
                """;
            cmd.Parameters.AddWithValue("@id", bookId);
            cmd.Parameters.AddWithValue("@idx", chapterIndex);
            cmd.Parameters.AddWithValue("@title", chapterTitle);
            cmd.Parameters.AddWithValue("@now", DateTimeOffset.Now.ToString("o"));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // ==================== 章节 ====================

    public async Task<IReadOnlyList<ChapterEntity>> GetChaptersAsync(string bookId, CancellationToken ct = default)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM chapters WHERE book_id = @bookId ORDER BY index_no";
        cmd.Parameters.AddWithValue("@bookId", bookId);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<ChapterEntity>();
        while (reader.Read())
        {
            list.Add(MapChapter(reader));
        }
        return list;
    }

    public async Task<ChapterEntity?> GetChapterAsync(string bookId, int indexNo, CancellationToken ct = default)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM chapters WHERE book_id = @bookId AND index_no = @idx LIMIT 1";
        cmd.Parameters.AddWithValue("@bookId", bookId);
        cmd.Parameters.AddWithValue("@idx", indexNo);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        return reader.Read() ? MapChapter(reader) : null;
    }

    public async Task<IReadOnlyList<ChapterEntity>> GetChaptersByStatusAsync(string bookId, ChapterStatus status, CancellationToken ct = default)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM chapters WHERE book_id = @bookId AND status = @status ORDER BY index_no";
        cmd.Parameters.AddWithValue("@bookId", bookId);
        cmd.Parameters.AddWithValue("@status", (int)status);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<ChapterEntity>();
        while (reader.Read())
        {
            list.Add(MapChapter(reader));
        }
        return list;
    }

    public async Task InsertChaptersAsync(string bookId, IReadOnlyList<ChapterEntity> chapters, CancellationToken ct = default)
    {
        if (chapters.Count == 0) return;

        await _writeLock.WaitAsync(ct);
        try
        {
            using var conn = Open();
            using var transaction = conn.BeginTransaction();
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT OR IGNORE INTO chapters (book_id, index_no, title, content, status, source_id, source_url, error, updated_at)
                VALUES (@bookId, @idx, @title, @content, @status, @sourceId, @sourceUrl, @error, @now)
                """;
            var pBookId = cmd.Parameters.Add("@bookId", SqliteType.Text);
            var pIdx = cmd.Parameters.Add("@idx", SqliteType.Integer);
            var pTitle = cmd.Parameters.Add("@title", SqliteType.Text);
            var pContent = cmd.Parameters.Add("@content", SqliteType.Text);
            var pStatus = cmd.Parameters.Add("@status", SqliteType.Integer);
            var pSourceId = cmd.Parameters.Add("@sourceId", SqliteType.Integer);
            var pSourceUrl = cmd.Parameters.Add("@sourceUrl", SqliteType.Text);
            var pError = cmd.Parameters.Add("@error", SqliteType.Text);
            var pNow = cmd.Parameters.Add("@now", SqliteType.Text);

            var now = DateTimeOffset.Now.ToString("o");
            foreach (var ch in chapters)
            {
                pBookId.Value = bookId;
                pIdx.Value = ch.IndexNo;
                pTitle.Value = ch.Title;
                pContent.Value = (object?)ch.Content ?? DBNull.Value;
                pStatus.Value = (int)ch.Status;
                pSourceId.Value = ch.SourceId;
                pSourceUrl.Value = ch.SourceUrl;
                pError.Value = (object?)ch.Error ?? DBNull.Value;
                pNow.Value = now;
                await cmd.ExecuteNonQueryAsync(ct);
            }

            transaction.Commit();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task UpdateChapterAsync(string bookId, int indexNo, ChapterStatus status, string? content, string? error, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE chapters SET status = @status, content = @content, error = @error, updated_at = @now
                WHERE book_id = @bookId AND index_no = @idx
                """;
            cmd.Parameters.AddWithValue("@bookId", bookId);
            cmd.Parameters.AddWithValue("@idx", indexNo);
            cmd.Parameters.AddWithValue("@status", (int)status);
            cmd.Parameters.AddWithValue("@content", (object?)content ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@error", (object?)error ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@now", DateTimeOffset.Now.ToString("o"));
            await cmd.ExecuteNonQueryAsync(ct);

            // 同步更新 book.done_chapters
            await SyncDoneCountAsync(conn, bookId, ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task UpdateChapterSourceAsync(string bookId, int indexNo, int newSourceId, string newSourceUrl, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE chapters SET source_id = @sourceId, source_url = @sourceUrl,
                    status = 0, content = NULL, error = NULL, updated_at = @now
                WHERE book_id = @bookId AND index_no = @idx
                """;
            cmd.Parameters.AddWithValue("@bookId", bookId);
            cmd.Parameters.AddWithValue("@idx", indexNo);
            cmd.Parameters.AddWithValue("@sourceId", newSourceId);
            cmd.Parameters.AddWithValue("@sourceUrl", newSourceUrl);
            cmd.Parameters.AddWithValue("@now", DateTimeOffset.Now.ToString("o"));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<int> CountDoneChaptersAsync(string bookId, CancellationToken ct = default)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM chapters WHERE book_id = @bookId AND status = 2";
        cmd.Parameters.AddWithValue("@bookId", bookId);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result);
    }

    public async Task<IReadOnlyList<(string Title, string Content)>> GetDoneChapterContentsAsync(string bookId, CancellationToken ct = default)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT title, content FROM chapters WHERE book_id = @bookId AND status = 2 ORDER BY index_no";
        cmd.Parameters.AddWithValue("@bookId", bookId);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<(string, string)>();
        while (reader.Read())
        {
            var title = reader.GetString(0);
            var content = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            list.Add((title, content));
        }
        return list;
    }

    // ==================== 阅读记忆 / 书签 ====================

    public async Task<ReadingStateEntity?> GetReadingStateAsync(string bookId, CancellationToken ct = default)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM reading_states WHERE book_id = @bookId LIMIT 1";
        cmd.Parameters.AddWithValue("@bookId", bookId);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!reader.Read()) return null;

        return new ReadingStateEntity
        {
            BookId = SafeGetString(reader, "book_id"),
            ChapterIndex = SafeGetInt(reader, "chapter_index"),
            PageIndex = SafeGetInt(reader, "page_index"),
            AnchorText = SafeGetString(reader, "anchor_text"),
            LayoutFingerprint = SafeGetString(reader, "layout_fingerprint"),
            UpdatedAt = SafeGetString(reader, "updated_at"),
        };
    }

    public async Task UpsertReadingStateAsync(ReadingStateEntity state, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            var now = DateTimeOffset.Now.ToString("o");
            cmd.CommandText = """
                INSERT INTO reading_states (book_id, chapter_index, page_index, anchor_text, layout_fingerprint, updated_at)
                VALUES (@bookId, @chapterIndex, @pageIndex, @anchorText, @layoutFingerprint, @updatedAt)
                ON CONFLICT(book_id) DO UPDATE SET
                    chapter_index = excluded.chapter_index,
                    page_index = excluded.page_index,
                    anchor_text = excluded.anchor_text,
                    layout_fingerprint = excluded.layout_fingerprint,
                    updated_at = excluded.updated_at
                """;
            cmd.Parameters.AddWithValue("@bookId", state.BookId);
            cmd.Parameters.AddWithValue("@chapterIndex", state.ChapterIndex);
            cmd.Parameters.AddWithValue("@pageIndex", state.PageIndex);
            cmd.Parameters.AddWithValue("@anchorText", state.AnchorText);
            cmd.Parameters.AddWithValue("@layoutFingerprint", state.LayoutFingerprint);
            cmd.Parameters.AddWithValue("@updatedAt", now);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IReadOnlyList<ReadingBookmarkEntity>> GetReadingBookmarksAsync(string bookId, CancellationToken ct = default)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM reading_bookmarks WHERE book_id = @bookId ORDER BY created_at DESC";
        cmd.Parameters.AddWithValue("@bookId", bookId);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<ReadingBookmarkEntity>();
        while (reader.Read())
        {
            list.Add(new ReadingBookmarkEntity
            {
                BookId = SafeGetString(reader, "book_id"),
                ChapterIndex = SafeGetInt(reader, "chapter_index"),
                PageIndex = SafeGetInt(reader, "page_index"),
                ChapterTitle = SafeGetString(reader, "chapter_title"),
                PreviewText = SafeGetString(reader, "preview_text"),
                AnchorText = SafeGetString(reader, "anchor_text"),
                CreatedAt = SafeGetString(reader, "created_at"),
            });
        }
        return list;
    }

    public async Task UpsertReadingBookmarkAsync(ReadingBookmarkEntity bookmark, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            var now = DateTimeOffset.Now.ToString("o");
            cmd.CommandText = """
                INSERT INTO reading_bookmarks (book_id, chapter_index, page_index, chapter_title, preview_text, anchor_text, created_at)
                VALUES (@bookId, @chapterIndex, @pageIndex, @chapterTitle, @previewText, @anchorText, @createdAt)
                ON CONFLICT(book_id, chapter_index, page_index) DO UPDATE SET
                    chapter_title = excluded.chapter_title,
                    preview_text = excluded.preview_text,
                    anchor_text = excluded.anchor_text,
                    created_at = excluded.created_at
                """;
            cmd.Parameters.AddWithValue("@bookId", bookmark.BookId);
            cmd.Parameters.AddWithValue("@chapterIndex", bookmark.ChapterIndex);
            cmd.Parameters.AddWithValue("@pageIndex", bookmark.PageIndex);
            cmd.Parameters.AddWithValue("@chapterTitle", bookmark.ChapterTitle);
            cmd.Parameters.AddWithValue("@previewText", bookmark.PreviewText);
            cmd.Parameters.AddWithValue("@anchorText", bookmark.AnchorText);
            cmd.Parameters.AddWithValue("@createdAt", now);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task DeleteReadingBookmarkAsync(string bookId, int chapterIndex, int pageIndex, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM reading_bookmarks WHERE book_id = @bookId AND chapter_index = @chapterIndex AND page_index = @pageIndex";
            cmd.Parameters.AddWithValue("@bookId", bookId);
            cmd.Parameters.AddWithValue("@chapterIndex", chapterIndex);
            cmd.Parameters.AddWithValue("@pageIndex", pageIndex);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    // ==================== Helpers ====================

    private static async Task SyncDoneCountAsync(SqliteConnection conn, string bookId, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE books SET done_chapters = (SELECT COUNT(*) FROM chapters WHERE book_id = @bookId AND status = 2) WHERE id = @bookId";
        cmd.Parameters.AddWithValue("@bookId", bookId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static BookEntity MapBook(SqliteDataReader r) => new()
    {
        Id = r.GetString(r.GetOrdinal("id")),
        Title = r.GetString(r.GetOrdinal("title")),
        Author = r.IsDBNull(r.GetOrdinal("author")) ? string.Empty : r.GetString(r.GetOrdinal("author")),
        SourceId = r.GetInt32(r.GetOrdinal("source_id")),
        TocUrl = r.IsDBNull(r.GetOrdinal("toc_url")) ? string.Empty : r.GetString(r.GetOrdinal("toc_url")),
        TotalChapters = r.GetInt32(r.GetOrdinal("total_chapters")),
        DoneChapters = r.GetInt32(r.GetOrdinal("done_chapters")),
        ReadChapterIndex = r.GetInt32(r.GetOrdinal("read_chapter_index")),
        ReadChapterTitle = r.IsDBNull(r.GetOrdinal("read_chapter_title")) ? string.Empty : r.GetString(r.GetOrdinal("read_chapter_title")),
        CreatedAt = r.GetString(r.GetOrdinal("created_at")),
        UpdatedAt = r.GetString(r.GetOrdinal("updated_at")),
        ReadAt = SafeGetString(r, "read_at"),
        CoverUrl = SafeGetString(r, "cover_url"),
        CoverImage = SafeGetString(r, "cover_image"),
        CoverBlob = SafeGetBytes(r, "cover_blob"),
        CoverRule = SafeGetString(r, "cover_rule"),
    };

    private static ChapterEntity MapChapter(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("id")),
        BookId = r.GetString(r.GetOrdinal("book_id")),
        IndexNo = r.GetInt32(r.GetOrdinal("index_no")),
        Title = r.IsDBNull(r.GetOrdinal("title")) ? string.Empty : r.GetString(r.GetOrdinal("title")),
        Content = r.IsDBNull(r.GetOrdinal("content")) ? null : r.GetString(r.GetOrdinal("content")),
        Status = (ChapterStatus)r.GetInt32(r.GetOrdinal("status")),
        SourceId = r.GetInt32(r.GetOrdinal("source_id")),
        SourceUrl = r.IsDBNull(r.GetOrdinal("source_url")) ? string.Empty : r.GetString(r.GetOrdinal("source_url")),
        Error = r.IsDBNull(r.GetOrdinal("error")) ? null : r.GetString(r.GetOrdinal("error")),
        UpdatedAt = r.GetString(r.GetOrdinal("updated_at")),
    };

    private static string SafeGetString(SqliteDataReader r, string col)
    {
        try
        {
            var ord = r.GetOrdinal(col);
            return r.IsDBNull(ord) ? string.Empty : r.GetString(ord);
        }
        catch (Exception ex) { AppLogger.Warn("SqliteBookRepository.SafeGetString", ex); return string.Empty; }
    }

    private static int SafeGetInt(SqliteDataReader r, string col)
    {
        try
        {
            var ord = r.GetOrdinal(col);
            return r.IsDBNull(ord) ? 0 : Convert.ToInt32(r.GetValue(ord));
        }
        catch (Exception ex) { AppLogger.Warn("SqliteBookRepository.SafeGetInt", ex); return 0; }
    }

    private static byte[] SafeGetBytes(SqliteDataReader r, string col)
    {
        try
        {
            var ord = r.GetOrdinal(col);
            return r.IsDBNull(ord) ? [] : (byte[])r[col];
        }
        catch (Exception ex) { AppLogger.Warn("SqliteBookRepository.SafeGetBytes", ex); return []; }
    }

    public void Dispose()
    {
        _writeLock.Dispose();
    }
}
