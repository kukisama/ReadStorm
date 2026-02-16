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

    public double ReaderLineHeight { get; set; } = 28;

    public double ReaderParagraphSpacing { get; set; } = 12;

    public string ReaderBackground { get; set; } = "#FFFFFF";

    public string ReaderForeground { get; set; } = "#1F2937";

    public bool ReaderDarkMode { get; set; }
}
