using System.Diagnostics;
using System.Text.Json;

namespace ReadStorm.Infrastructure.Services;

/// <summary>
/// 仅在 DEBUG 构建中生效的轻量级日志工具。
/// Release 编译时，所有 <see cref="Warn"/> 调用被编译器完全移除（零开销）。
/// 日志写入工作目录 logs/debug.log（默认 ~/Documents/ReadStorm/logs/debug.log）。
/// </summary>
/// <remarks>
/// 本类自行解析 appsettings.user.json 获取工作目录，
/// 不依赖 <see cref="WorkDirectoryManager"/>，以避免循环调用
/// （WorkDirectoryManager 的 catch 中调用了 AppLogger.Warn）。
/// </remarks>
public static class AppLogger
{
    /// <summary>
    /// 全局诊断日志开关。由设置页 <c>EnableDiagnosticLog</c> 控制。
    /// 值为 <c>false</c>（默认）时仅保留必要错误日志；值为 <c>true</c> 时记录详细调试信息。
    /// </summary>
    public static bool IsEnabled { get; set; }

    private static readonly Lazy<TraceSource> Source = new(() =>
    {
        var logDir = ResolveLogDirectory();
        Directory.CreateDirectory(logDir);

        var logPath = Path.Combine(logDir, "debug.log");
        var listener = new TextWriterTraceListener(logPath)
        {
            TraceOutputOptions = TraceOptions.DateTime,
        };

        var source = new TraceSource("ReadStorm", SourceLevels.Warning);
        source.Listeners.Add(listener);
        return source;
    });

    /// <summary>
    /// 记录一条警告日志。仅在 DEBUG 构建中有效，Release 中调用被完全移除。
    /// </summary>
    [Conditional("DEBUG")]
    public static void Warn(string context, Exception ex)
    {
        if (!IsEnabled) return;
        try
        {
            Source.Value.TraceEvent(TraceEventType.Warning, 0,
                $"[{context}] {ex.GetType().Name}: {ex.Message}");
            Source.Value.Flush();
        }
        catch
        {
            // 日志自身不应影响主流程
        }
    }

    /// <summary>
    /// 自行解析工作目录（不调用 WorkDirectoryManager，避免循环依赖）。
    /// 读取 %AppData%/ReadStorm/appsettings.user.json 中的 DownloadPath，
    /// 如果缺失或异常则回退到 ~/Documents/ReadStorm/。
    /// </summary>
    private static string ResolveLogDirectory()
    {
        // 优先使用外部日志目录覆盖（Android 上指向 Documents/ReadStorm/logs）
        var externalOverride = WorkDirectoryManager.ExternalLogDirectoryOverride;
        if (!string.IsNullOrEmpty(externalOverride))
            return externalOverride;

        try
        {
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ReadStorm", "appsettings.user.json");

            if (File.Exists(settingsPath))
            {
                using var stream = File.OpenRead(settingsPath);
                using var doc = JsonDocument.Parse(stream);
                if (doc.RootElement.TryGetProperty("DownloadPath", out var node)
                    && node.ValueKind == JsonValueKind.String)
                {
                    var configured = node.GetString();
                    if (!string.IsNullOrWhiteSpace(configured))
                    {
                        var workDir = Path.IsPathRooted(configured)
                            ? Path.GetFullPath(configured)
                            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured));
                        return Path.Combine(workDir, "logs");
                    }
                }
            }
        }
        catch
        {
            // 解析失败时静默回退到默认目录
        }

        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

        // Android 上 MyDocuments 返回空字符串，回退到其他可用目录
        if (string.IsNullOrEmpty(docs))
            docs = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(docs))
            docs = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        if (string.IsNullOrEmpty(docs))
            docs = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(docs))
            docs = AppContext.BaseDirectory;

        var defaultWorkDir = Path.Combine(docs, "ReadStorm");
        return Path.Combine(defaultWorkDir, "logs");
    }
}
