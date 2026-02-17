using ReadStorm.Domain.Models;

namespace ReadStorm.Application.Abstractions;

public interface IDownloadBookUseCase
{
    Task QueueAsync(
        DownloadTask task,
        SearchResult selectedBook,
        DownloadMode mode,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查书籍是否有新章节。重新抓取目录并与 DB 对比，
    /// 新章节以 Pending 状态插入。返回新增章节数。
    /// </summary>
    Task<int> CheckNewChaptersAsync(
        BookEntity book,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 换源：从指定书源重新抓取当前章节内容。
    /// 返回 (成功?, 新正文, 诊断信息)。
    /// </summary>
    Task<(bool Success, string Content, string Message)> FetchChapterFromSourceAsync(
        BookEntity book,
        string chapterTitle,
        int sourceId,
        CancellationToken cancellationToken = default);
}
