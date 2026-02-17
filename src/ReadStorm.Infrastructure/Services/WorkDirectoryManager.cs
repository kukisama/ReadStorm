using System.Text.Json;

namespace ReadStorm.Infrastructure.Services;

/// <summary>
/// 统一管理 ReadStorm 工作目录（数据库/下载/日志/封面）与旧目录迁移。
/// </summary>
public static class WorkDirectoryManager
{
    public static string GetDefaultWorkDirectory()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.GetFullPath(Path.Combine(docs, "ReadStorm"));
    }

    public static string GetSettingsFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
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
        if (IsSameOrDirectChildOfAppDirectory(candidate))
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
        => Path.Combine(workDirectory, "logs");

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
