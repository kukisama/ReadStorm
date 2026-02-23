namespace ReadStorm.Domain.Models;

/// <summary>SQLite 中的书籍实体。</summary>
public sealed class BookEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Title { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    /// <summary>默认书源 ID。</summary>
    public int SourceId { get; set; }

    /// <summary>目录页 URL，用于重新获取章节列表。</summary>
    public string TocUrl { get; set; } = string.Empty;

    public int TotalChapters { get; set; }

    public int DoneChapters { get; set; }

    /// <summary>阅读进度 — 当前章节序号。</summary>
    public int ReadChapterIndex { get; set; }

    /// <summary>阅读进度 — 当前章节标题。</summary>
    public string ReadChapterTitle { get; set; } = string.Empty;

    public string CreatedAt { get; set; } = DateTimeOffset.Now.ToString("o");

    public string UpdatedAt { get; set; } = DateTimeOffset.Now.ToString("o");

    /// <summary>上次实际阅读时间（仅在翻页时更新）。</summary>
    public string ReadAt { get; set; } = string.Empty;

    /// <summary>计算属性：完成百分比。</summary>
    public int ProgressPercent => TotalChapters > 0
        ? (int)Math.Round(100.0 * Math.Clamp(DoneChapters, 0, TotalChapters) / TotalChapters)
        : 0;

    /// <summary>是否全部章节已下载完成。</summary>
    public bool IsComplete => TotalChapters > 0 && DoneChapters >= TotalChapters;

    /// <summary>运行时标记：是否有正在执行的下载任务（不持久化）。</summary>
    public bool IsDownloading { get; set; }

    /// <summary>封面图片URL（如有）。</summary>
    public string CoverUrl { get; set; } = string.Empty;

    /// <summary>封面图片Base64（如有）。</summary>
    public string CoverImage { get; set; } = string.Empty;

    /// <summary>封面图片二进制（BLOB，jpg/png原始字节）。</summary>
    public byte[] CoverBlob { get; set; } = [];

    /// <summary>用于 UI 显示的封面数据（优先 BLOB，其次 Base64）。</summary>
    public object? CoverDisplayData => CoverBlob.Length > 0
        ? CoverBlob
        : (string.IsNullOrWhiteSpace(CoverImage) ? null : CoverImage);

    /// <summary>书名首字，用作无封面占位符。</summary>
    public string TitleInitial => string.IsNullOrWhiteSpace(Title) ? "書" : Title[..1];

    /// <summary>是否有封面图。</summary>
    public bool HasCover => CoverBlob.Length > 0 || !string.IsNullOrWhiteSpace(CoverImage);

    /// <summary>续传是否可用：书未完成且没有正在下载。</summary>
    public bool CanResume => !IsComplete && !IsDownloading;

    /// <summary>封面提取规则描述（如 og:image、第1张img、手动选第3张|<img ...>）。</summary>
    public string CoverRule { get; set; } = string.Empty;
}
