namespace ReadStorm.Domain.Models;

/// <summary>阅读书签（按书籍 + 章节 + 页定位）。</summary>
public sealed class ReadingBookmarkEntity
{
    public string BookId { get; set; } = string.Empty;

    public int ChapterIndex { get; set; }

    public int PageIndex { get; set; }

    public string ChapterTitle { get; set; } = string.Empty;

    public string PreviewText { get; set; } = string.Empty;

    public string AnchorText { get; set; } = string.Empty;

    public string CreatedAt { get; set; } = DateTimeOffset.Now.ToString("o");

    public string Display => $"{ChapterTitle} · 第 {PageIndex + 1} 页";
}
