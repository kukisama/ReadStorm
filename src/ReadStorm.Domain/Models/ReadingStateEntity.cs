namespace ReadStorm.Domain.Models;

/// <summary>阅读记忆状态（按书籍维度）。</summary>
public sealed class ReadingStateEntity
{
    public string BookId { get; set; } = string.Empty;

    public int ChapterIndex { get; set; }

    public int PageIndex { get; set; }

    /// <summary>当前页锚点文本（用于布局变化后的回退定位）。</summary>
    public string AnchorText { get; set; } = string.Empty;

    /// <summary>排版指纹（字号/行高/宽度等）用于判断是否可直接按页码恢复。</summary>
    public string LayoutFingerprint { get; set; } = string.Empty;

    public string UpdatedAt { get; set; } = DateTimeOffset.Now.ToString("o");
}
