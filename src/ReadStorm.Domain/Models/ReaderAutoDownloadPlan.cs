namespace ReadStorm.Domain.Models;

/// <summary>
/// 阅读场景自动下载规划结果。
/// </summary>
public sealed class ReaderAutoDownloadPlan
{
    /// <summary>是否需要触发前台窗口下载。</summary>
    public bool ShouldQueueWindow { get; set; }

    /// <summary>窗口起始章节（0-based，含）。</summary>
    public int WindowStartIndex { get; set; }

    /// <summary>窗口章节数量。</summary>
    public int WindowTakeCount { get; set; }

    /// <summary>当前章节之后连续可读（Done）章节数。</summary>
    public int ConsecutiveDoneAfterAnchor { get; set; }

    /// <summary>是否存在可用于后台补洞的缺口。</summary>
    public bool HasGap { get; set; }

    /// <summary>首个缺口章节（0-based）。</summary>
    public int FirstGapIndex { get; set; } = -1;
}
