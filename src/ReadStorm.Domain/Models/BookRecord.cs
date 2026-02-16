namespace ReadStorm.Domain.Models;

/// <summary>书架中一本书的记录。</summary>
public sealed class BookRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Title { get; init; } = string.Empty;

    public string Author { get; init; } = string.Empty;

    public int SourceId { get; init; }

    public string FilePath { get; set; } = string.Empty;

    public string Format { get; set; } = "txt";

    public DateTimeOffset AddedAt { get; init; } = DateTimeOffset.Now;

    public int TotalChapters { get; set; }

    public ReadingProgress Progress { get; set; } = new();
}

/// <summary>阅读进度。</summary>
public sealed class ReadingProgress
{
    public int CurrentChapterIndex { get; set; }

    public string CurrentChapterTitle { get; set; } = string.Empty;

    public double ScrollPercent { get; set; }

    public DateTimeOffset LastReadAt { get; set; } = DateTimeOffset.Now;
}
