using System.Text.Json;

namespace ReadStorm.Infrastructure.Services;

/// <summary>
/// 统一管理 ReadStorm 工作目录（数据库/下载/日志/封面）与旧目录迁移。
/// </summary>
public static class WorkDirectoryManager
{
    /// <summary>
    /// Android 等平台可在启动时设置此属性，将日志重定向到外部存储（如 Documents/ReadStorm/logs），
    /// 方便用户通过文件管理器直接访问。为 null 时使用默认工作目录下的 logs 子目录。
    /// </summary>
    public static string? ExternalLogDirectoryOverride { get; set; }
    public static string GetDefaultWorkDirectory()
    {
        // 默认优先 LocalApplicationData，避免 Windows 上 MyDocuments 被 OneDrive 重定向。
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // 兼容不同平台，逐级回退到可用目录
        if (string.IsNullOrEmpty(baseDir))
            baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(baseDir))
            baseDir = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        if (string.IsNullOrEmpty(baseDir))
            baseDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrEmpty(baseDir))
            baseDir = AppContext.BaseDirectory; // 最终兜底

        return Path.GetFullPath(Path.Combine(baseDir, "ReadStorm"));
    }

    public static string GetSettingsFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        // Android 上 ApplicationData 可能返回空字符串，需逐级回退
        if (string.IsNullOrEmpty(appData))
            appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(appData))
            appData = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        if (string.IsNullOrEmpty(appData))
            appData = AppContext.BaseDirectory;

        return Path.Combine(appData, "ReadStorm", "appsettings.user.json");
    }

    public static string ResolveConfiguredWorkDirectory(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return GetDefaultWorkDirectory();
        }

        var path = configuredPath.Trim();
        if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, path);
        }

        return Path.GetFullPath(path);
    }

    public static string NormalizeAndMigrateWorkDirectory(string? configuredPath)
    {
        var candidate = ResolveConfiguredWorkDirectory(configuredPath);

        // Android 上工作目录天然位于应用数据目录内，属于正常情况，不需要迁移。
        // 仅在桌面平台上才执行旧数据迁移逻辑。
        if (!OperatingSystem.IsAndroid() && IsSameOrDirectChildOfAppDirectory(candidate))
        {
            var target = GetDefaultWorkDirectory();
            MigrateLegacyData(candidate, target);
            candidate = target;
        }

        EnsureWorkDirectoryLayout(candidate);
        return candidate;
    }

    public static string GetCurrentWorkDirectoryFromSettings()
    {
        try
        {
            var settingsFile = GetSettingsFilePath();
            if (!File.Exists(settingsFile))
            {
                return NormalizeAndMigrateWorkDirectory(null);
            }

            using var stream = File.OpenRead(settingsFile);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.TryGetProperty("DownloadPath", out var node)
                && node.ValueKind == JsonValueKind.String)
            {
                return NormalizeAndMigrateWorkDirectory(node.GetString());
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("WorkDir.GetCurrentFromSettings", ex);
        }

        return NormalizeAndMigrateWorkDirectory(null);
    }

    public static string GetDatabasePath(string workDirectory)
        => Path.Combine(workDirectory, "readstorm.db");

    public static string GetDownloadsDirectory(string workDirectory)
        => Path.Combine(workDirectory, "downloads");

    public static string GetLogsDirectory(string workDirectory)
    {
        if (!string.IsNullOrEmpty(ExternalLogDirectoryOverride))
            return ExternalLogDirectoryOverride;
        return Path.Combine(workDirectory, "logs");
    }

    public static string GetCoversDirectory(string workDirectory)
        => Path.Combine(workDirectory, "covers");

    public static bool IsSameOrDirectChildOfAppDirectory(string path)
    {
        var appDir = Path.GetFullPath(AppContext.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var full = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(full, appDir, PlatformPathComparer.PathComparison))
        {
            return true;
        }

        var parent = Path.GetDirectoryName(full)
            ?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return !string.IsNullOrWhiteSpace(parent)
            && string.Equals(parent, appDir, PlatformPathComparer.PathComparison);
    }

    public static void EnsureWorkDirectoryLayout(string workDirectory)
    {
        Directory.CreateDirectory(workDirectory);
        Directory.CreateDirectory(GetDownloadsDirectory(workDirectory));
        Directory.CreateDirectory(GetLogsDirectory(workDirectory));
        Directory.CreateDirectory(GetCoversDirectory(workDirectory));
    }

    private static void MigrateLegacyData(string legacyPath, string targetWorkDirectory)
    {
        try
        {
            EnsureWorkDirectoryLayout(targetWorkDirectory);

            var appDir = Path.GetFullPath(AppContext.BaseDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var source = Path.GetFullPath(legacyPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // 固定迁移：主目录中的数据库与常见数据目录
            MoveFileIfExists(Path.Combine(appDir, "readstorm.db"), Path.Combine(targetWorkDirectory, "readstorm.db"));
            MoveFileIfExists(Path.Combine(appDir, "readstorm.db-shm"), Path.Combine(targetWorkDirectory, "readstorm.db-shm"));
            MoveFileIfExists(Path.Combine(appDir, "readstorm.db-wal"), Path.Combine(targetWorkDirectory, "readstorm.db-wal"));

            MoveDirectoryContentIfExists(Path.Combine(appDir, "downloads"), GetDownloadsDirectory(targetWorkDirectory));
            MoveDirectoryContentIfExists(Path.Combine(appDir, "logs"), GetLogsDirectory(targetWorkDirectory));
            MoveDirectoryContentIfExists(Path.Combine(appDir, "covers"), GetCoversDirectory(targetWorkDirectory));

            // 若用户配置为主目录的一级子目录，也迁移该目录内容到目标下同名目录
            if (!string.Equals(source, appDir, PlatformPathComparer.PathComparison))
            {
                var leaf = Path.GetFileName(source);
                if (!string.IsNullOrWhiteSpace(leaf) && Directory.Exists(source))
                {
                    var dest = Path.Combine(targetWorkDirectory, leaf);
                    MoveDirectoryContentIfExists(source, dest);
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("WorkDir.MigrateLegacyData", ex);
        }
    }

    private static void MoveDirectoryContentIfExists(string sourceDir, string targetDir)
    {
        if (!Directory.Exists(sourceDir))
        {
            return;
        }

        Directory.CreateDirectory(targetDir);
        var sourceRoot = Path.GetFullPath(sourceDir);

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceRoot, file);
            var dest = Path.Combine(targetDir, rel);
            var destFolder = Path.GetDirectoryName(dest);
            if (!string.IsNullOrWhiteSpace(destFolder))
            {
                Directory.CreateDirectory(destFolder);
            }

            MoveFileIfExists(file, dest);
        }
    }

    private static void MoveFileIfExists(string source, string destination)
    {
        if (!File.Exists(source))
        {
            return;
        }

        if (File.Exists(destination))
        {
            return;
        }

        var destFolder = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(destFolder))
        {
            Directory.CreateDirectory(destFolder);
        }

        try
        {
            File.Move(source, destination);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"WorkDir.MoveFile:{source}", ex);
            // 部分场景（占用/跨卷）降级为复制
            try
            {
                File.Copy(source, destination, overwrite: false);
            }
            catch (Exception copyEx)
            {
                AppLogger.Warn($"WorkDir.CopyFallback:{source}", copyEx);
            }
        }
    }
}
