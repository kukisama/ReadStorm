namespace ReadStorm.Domain.Models;

/// <summary>SQLite 中的章节实体。</summary>
public sealed class ChapterEntity
{
    public long Id { get; set; }

    public string BookId { get; set; } = string.Empty;

    /// <summary>章节序号（从 1 开始）。</summary>
    public int IndexNo { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>章节正文（已下载则有值）。</summary>
    public string? Content { get; set; }

    public ChapterStatus Status { get; set; } = ChapterStatus.Pending;

    /// <summary>该章使用的书源 ID。</summary>
    public int SourceId { get; set; }

    /// <summary>该章的原始 URL。</summary>
    public string SourceUrl { get; set; } = string.Empty;

    /// <summary>失败原因。</summary>
    public string? Error { get; set; }

    public string UpdatedAt { get; set; } = DateTimeOffset.Now.ToString("o");
}
