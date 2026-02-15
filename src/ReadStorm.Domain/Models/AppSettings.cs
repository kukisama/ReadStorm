namespace ReadStorm.Domain.Models;

public sealed class AppSettings
{
    public string DownloadPath { get; set; } = "downloads";

    public int MaxConcurrency { get; set; } = 6;

    public int MinIntervalMs { get; set; } = 200;

    public int MaxIntervalMs { get; set; } = 400;

    public string ExportFormat { get; set; } = "epub";

    public bool ProxyEnabled { get; set; }

    public string ProxyHost { get; set; } = "127.0.0.1";

    public int ProxyPort { get; set; } = 7890;
}
