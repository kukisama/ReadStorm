namespace ReadStorm.Domain.Models;

public sealed class AppSettings
{
    public string DownloadPath { get; set; } = "downloads";

    public int MaxConcurrency { get; set; } = 6;

    /// <summary>“全部书源(健康)”聚合搜索的最大并发书源数。</summary>
    public int AggregateSearchMaxConcurrency { get; set; } = 5;

    public int MinIntervalMs { get; set; } = 200;

    public int MaxIntervalMs { get; set; } = 400;

    public string ExportFormat { get; set; } = "txt";

    public bool ProxyEnabled { get; set; }

    public string ProxyHost { get; set; } = "127.0.0.1";

    public int ProxyPort { get; set; } = 7890;

    // ====== Reader global style ======
    public double ReaderFontSize { get; set; } = 31;

    public string ReaderFontName { get; set; } = "默认";

    public double ReaderLineHeight { get; set; } = 42;

    public double ReaderParagraphSpacing { get; set; } = 22;

    public string ReaderBackground { get; set; } = "#FFFFFF";

    public string ReaderForeground { get; set; } = "#1F2937";

    public bool ReaderDarkMode { get; set; }

    /// <summary>Android 阅读页是否扩展到刘海/状态栏区域。</summary>
    public bool ReaderExtendIntoCutout { get; set; }

    /// <summary>阅读区域最大宽度（px）。</summary>
    public double ReaderContentMaxWidth { get; set; } = 860;

    /// <summary>阅读正文顶部预留（px）。</summary>
    public double ReaderTopReservePx { get; set; } = 4;

    /// <summary>阅读正文底部预留（px）。</summary>
    public double ReaderBottomReservePx { get; set; } = 0;

    /// <summary>分页计算时底部状态栏保守预留（px）。</summary>
    public double ReaderBottomStatusBarReservePx { get; set; } = 0;

    /// <summary>分页估算时额外横向安全预留（px），用于避免右侧裁字；数值越大每行字数越少。</summary>
    public double ReaderHorizontalInnerReservePx { get; set; } = 0;

    /// <summary>阅读正文左右边距（px），同时影响可视宽度与分页估算。</summary>
    public double ReaderSidePaddingPx { get; set; } = 12;

    /// <summary>是否启用音量键翻页（下键下一页，上键上一页）。</summary>
    public bool ReaderUseVolumeKeyPaging { get; set; }

    /// <summary>是否启用阅读手势翻页（左滑右滑）。默认关闭，关闭时仅保留点击翻页。</summary>
    public bool ReaderUseSwipePaging { get; set; }

    /// <summary>阅读沉浸模式下是否隐藏系统状态栏（时间/电量等图标）。</summary>
    public bool ReaderHideSystemStatusBar { get; set; } = true;

    /// <summary>书架进度条左侧内边距（px）。</summary>
    public double BookshelfProgressLeftPaddingPx { get; set; } = 5;

    /// <summary>书架进度条右侧内边距（px）。</summary>
    public double BookshelfProgressRightPaddingPx { get; set; } = 5;

    /// <summary>书架进度条目标总宽度（px）。</summary>
    public double BookshelfProgressTotalWidthPx { get; set; } = 106;

    /// <summary>书架进度条最小宽度（px）。</summary>
    public double BookshelfProgressMinWidthPx { get; set; } = 72;

    /// <summary>书架进度条与百分比文本间距（px）。</summary>
    public double BookshelfProgressBarToPercentGapPx { get; set; } = 8;

    /// <summary>书架百分比文本右侧额外留白（px）。</summary>
    public double BookshelfProgressPercentTailGapPx { get; set; } = 24;

    /// <summary>是否启用诊断日志（默认关闭，开启后记录详细调试信息）。</summary>
    public bool EnableDiagnosticLog { get; set; }

    /// <summary>应用启动时是否自动检测未完成下载并自动续传/更新（默认关闭）。</summary>
    public bool AutoResumeAndRefreshOnStartup { get; set; }
}
