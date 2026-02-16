namespace ReadStorm.Domain.Models;

/// <summary>章节下载状态。</summary>
public enum ChapterStatus
{
    /// <summary>待下载。</summary>
    Pending = 0,

    /// <summary>下载中。</summary>
    Downloading = 1,

    /// <summary>已完成。</summary>
    Done = 2,

    /// <summary>下载失败。</summary>
    Failed = 3,
}
