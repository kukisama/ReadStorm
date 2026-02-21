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
    public double ReaderFontSize { get; set; } = 15;

    public string ReaderFontName { get; set; } = "默认";

    public double ReaderLineHeight { get; set; } = 30;

    public double ReaderParagraphSpacing { get; set; } = 22;

    public string ReaderBackground { get; set; } = "#FFFFFF";

    public string ReaderForeground { get; set; } = "#1F2937";

    public bool ReaderDarkMode { get; set; }

    /// <summary>Android 阅读页是否扩展到刘海/状态栏区域。</summary>
    public bool ReaderExtendIntoCutout { get; set; }

    /// <summary>阅读区域最大宽度（px）。</summary>
    public double ReaderContentMaxWidth { get; set; } = 860;

    /// <summary>是否启用诊断日志（默认关闭，开启后记录详细调试信息）。</summary>
    public bool EnableDiagnosticLog { get; set; }

    /// <summary>应用启动时是否自动检测未完成下载并自动续传/更新（默认关闭）。</summary>
    public bool AutoResumeAndRefreshOnStartup { get; set; }
}
