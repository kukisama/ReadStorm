using ReadStorm.Domain.Models;

namespace ReadStorm.Application.Abstractions;

/// <summary>书籍 + 章节级存储仓库（SQLite）。</summary>
public interface IBookRepository
{
    // ==================== 书籍 ====================

    Task<BookEntity?> GetBookAsync(string bookId, CancellationToken ct = default);

    Task<IReadOnlyList<BookEntity>> GetAllBooksAsync(CancellationToken ct = default);

    /// <summary>查找同名同作者的书（防重复）。</summary>
    Task<BookEntity?> FindBookAsync(string title, string author, CancellationToken ct = default);

    Task UpsertBookAsync(BookEntity book, CancellationToken ct = default);

    Task DeleteBookAsync(string bookId, CancellationToken ct = default);

    Task UpdateReadProgressAsync(string bookId, int chapterIndex, string chapterTitle, CancellationToken ct = default);

    // ==================== 章节 ====================

    Task<IReadOnlyList<ChapterEntity>> GetChaptersAsync(string bookId, CancellationToken ct = default);

    Task<ChapterEntity?> GetChapterAsync(string bookId, int indexNo, CancellationToken ct = default);

    /// <summary>获取指定状态的章节。</summary>
    Task<IReadOnlyList<ChapterEntity>> GetChaptersByStatusAsync(string bookId, ChapterStatus status, CancellationToken ct = default);

    /// <summary>批量插入章节（首次创建目录时）。</summary>
    Task InsertChaptersAsync(string bookId, IReadOnlyList<ChapterEntity> chapters, CancellationToken ct = default);

    /// <summary>更新单章状态 + 内容。</summary>
    Task UpdateChapterAsync(string bookId, int indexNo, ChapterStatus status, string? content, string? error, CancellationToken ct = default);

    /// <summary>更新章节的书源信息（换源）。</summary>
    Task UpdateChapterSourceAsync(string bookId, int indexNo, int newSourceId, string newSourceUrl, CancellationToken ct = default);

    /// <summary>获取已完成章节数。</summary>
    Task<int> CountDoneChaptersAsync(string bookId, CancellationToken ct = default);

    /// <summary>获取所有已完成章节的内容（用于导出）。</summary>
    Task<IReadOnlyList<(string Title, string Content)>> GetDoneChapterContentsAsync(string bookId, CancellationToken ct = default);

    // ==================== 阅读记忆 / 书签 ====================

    /// <summary>获取某本书的阅读记忆状态。</summary>
    Task<ReadingStateEntity?> GetReadingStateAsync(string bookId, CancellationToken ct = default);

    /// <summary>写入或更新某本书的阅读记忆状态。</summary>
    Task UpsertReadingStateAsync(ReadingStateEntity state, CancellationToken ct = default);

    /// <summary>获取某本书的书签列表（按创建时间倒序）。</summary>
    Task<IReadOnlyList<ReadingBookmarkEntity>> GetReadingBookmarksAsync(string bookId, CancellationToken ct = default);

    /// <summary>新增或更新书签（同一书同一章同一页唯一）。</summary>
    Task UpsertReadingBookmarkAsync(ReadingBookmarkEntity bookmark, CancellationToken ct = default);

    /// <summary>删除指定页书签。</summary>
    Task DeleteReadingBookmarkAsync(string bookId, int chapterIndex, int pageIndex, CancellationToken ct = default);
}
