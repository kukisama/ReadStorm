using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using ReadStorm.Infrastructure.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReadStorm.Application.Abstractions;
using ReadStorm.Domain.Models;

namespace ReadStorm.Desktop.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _parent;
    private readonly IAppSettingsUseCase _appSettingsUseCase;
    private CancellationTokenSource? _autoSaveCts;
    private bool _isLoadingSettings;

    public SettingsViewModel(MainWindowViewModel parent, IAppSettingsUseCase appSettingsUseCase)
    {
        _parent = parent;
        _appSettingsUseCase = appSettingsUseCase;

        var about = AboutInfoProvider.Get();
        AboutVersion = about.Version;
        AboutContent = about.Content;
    }

    [ObservableProperty]
    private string downloadPath = WorkDirectoryManager.GetDefaultWorkDirectory();

    [ObservableProperty]
    private int maxConcurrency = 6;

    [ObservableProperty]
    private int aggregateSearchMaxConcurrency = 5;

    [ObservableProperty]
    private int minIntervalMs = 200;

    [ObservableProperty]
    private int maxIntervalMs = 400;

    public static IReadOnlyList<string> ExportFormatOptions { get; } = ["txt", "epub"];

    [ObservableProperty]
    private string exportFormat = "txt";

    [ObservableProperty]
    private bool proxyEnabled;

    [ObservableProperty]
    private string proxyHost = "127.0.0.1";

    [ObservableProperty]
    private int proxyPort = 7890;

    /// <summary>诊断日志开关（默认关闭）。</summary>
    [ObservableProperty]
    private bool enableDiagnosticLog;

    /// <summary>启动时自动续传/更新开关（默认关闭）。</summary>
    [ObservableProperty]
    private bool autoResumeAndRefreshOnStartup;

    /// <summary>阅读正文顶部预留（px）。</summary>
    [ObservableProperty]
    private double readerTopReservePx = 4;

    /// <summary>阅读正文底部预留（px）。</summary>
    [ObservableProperty]
    private double readerBottomReservePx = 0;

    /// <summary>分页计算时底部状态栏保守预留（px）。</summary>
    [ObservableProperty]
    private double readerBottomStatusBarReservePx = 0;

    /// <summary>分页估算时额外横向安全预留（px），用于避免右侧裁字。</summary>
    [ObservableProperty]
    private double readerHorizontalInnerReservePx = 0;

    /// <summary>阅读正文左右边距（px）。</summary>
    [ObservableProperty]
    private double readerSidePaddingPx = 12;

    [ObservableProperty]
    private double bookshelfProgressLeftPaddingPx = 5;

    [ObservableProperty]
    private double bookshelfProgressRightPaddingPx = 5;

    [ObservableProperty]
    private double bookshelfProgressTotalWidthPx = 106;

    [ObservableProperty]
    private double bookshelfProgressMinWidthPx = 72;

    [ObservableProperty]
    private double bookshelfProgressBarToPercentGapPx = 8;

    [ObservableProperty]
    private double bookshelfProgressPercentTailGapPx = 24;

    /// <summary>保存后显示的短暂反馈文案（✔ 已保存）。</summary>
    [ObservableProperty]
    private string saveFeedback = string.Empty;

    [ObservableProperty]
    private string aboutVersion = "未知版本";

    [ObservableProperty]
    private string aboutContent = "暂无版本说明。";

    [RelayCommand]
    private async Task BrowseDownloadPathAsync()
    {
        try
        {
            var window = Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
            if (window is null) return;

            var dialog = new Avalonia.Platform.Storage.FolderPickerOpenOptions
            {
                Title = "选择工作目录",
                AllowMultiple = false,
            };
            var result = await window.StorageProvider.OpenFolderPickerAsync(dialog);
            if (result.Count > 0)
            {
                DownloadPath = result[0].Path.LocalPath;
                _parent.StatusMessage = $"工作目录已更改为：{DownloadPath}";
            }
        }
        catch (Exception ex) { _parent.StatusMessage = $"选择目录失败：{ex.Message}"; }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        await SaveSettingsCoreAsync(showStatus: true);
    }

    /// <summary>导出诊断日志文件。</summary>
    [RelayCommand]
    private async Task ExportDiagnosticLogAsync()
    {
        try
        {
            var workDir = WorkDirectoryManager.NormalizeAndMigrateWorkDirectory(DownloadPath);
            var logDir = WorkDirectoryManager.GetLogsDirectory(workDir);
            var logFile = Path.Combine(logDir, "debug.log");

            if (!File.Exists(logFile))
            {
                _parent.StatusMessage = "日志文件不存在，可能尚未开启诊断日志或无日志内容。";
                return;
            }
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"diagnostic_log_{timestamp}.txt";

            var exportPath = await ExportFileByPlatformAsync(
                sourcePath: logFile,
                suggestedFileName: fileName,
                dialogTitle: "导出诊断日志",
                fileTypeName: "日志文件",
                fileExtensions: ["txt"],
                mimeType: "text/plain");

            _parent.StatusMessage = $"日志已导出：{exportPath}";
        }
        catch (OperationCanceledException)
        {
            _parent.StatusMessage = "已取消导出日志。";
        }
        catch (Exception ex)
        {
            _parent.StatusMessage = $"导出日志失败：{ex.Message}";
        }
    }

    /// <summary>导出书库数据库文件。</summary>
    [RelayCommand]
    private async Task ExportDatabaseAsync()
    {
        try
        {
            var workDir = WorkDirectoryManager.NormalizeAndMigrateWorkDirectory(DownloadPath);
            var dbPath = WorkDirectoryManager.GetDatabasePath(workDir);

            if (!File.Exists(dbPath))
            {
                _parent.StatusMessage = "数据库文件不存在，可能尚未初始化。";
                return;
            }

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var exportFileName = $"readstorm_db_{timestamp}.db";
            var tempExportPath = Path.Combine(Path.GetTempPath(), exportFileName);

            // 先执行 WAL checkpoint 确保数据完整
            await Task.Run(() =>
            {
                try
                {
                    using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                    cmd.ExecuteNonQuery();
                }
                catch { /* WAL checkpoint 失败不阻止拷贝 */ }

                File.Copy(dbPath, tempExportPath, overwrite: true);
            });

            try
            {
                var exportPath = await ExportFileByPlatformAsync(
                    sourcePath: tempExportPath,
                    suggestedFileName: exportFileName,
                    dialogTitle: "导出书库数据库",
                    fileTypeName: "SQLite 数据库",
                    fileExtensions: ["db"],
                    mimeType: "application/x-sqlite3");

                _parent.StatusMessage = $"数据库已导出：{exportPath}";
            }
            finally
            {
                try
                {
                    if (File.Exists(tempExportPath))
                        File.Delete(tempExportPath);
                }
                catch
                {
                    // 临时文件清理失败不影响主流程
                }
            }
        }
        catch (OperationCanceledException)
        {
            _parent.StatusMessage = "已取消导出数据库。";
        }
        catch (Exception ex)
        {
            _parent.StatusMessage = $"导出数据库失败：{ex.Message}";
        }
    }

    private async Task<string> ExportFileByPlatformAsync(
        string sourcePath,
        string suggestedFileName,
        string dialogTitle,
        string fileTypeName,
        IReadOnlyList<string> fileExtensions,
        string mimeType)
    {
        if (OperatingSystem.IsAndroid())
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                _parent.StatusMessage = "Android 10+ 导出到 Download/ReadStorm 无需额外权限弹窗。";
            }
            return await ExportToAndroidPublicFolderAsync(sourcePath, suggestedFileName, mimeType);
        }

        var selectedPath = await PickSavePathAsync(dialogTitle, suggestedFileName, fileTypeName, fileExtensions);
        if (string.IsNullOrWhiteSpace(selectedPath))
            throw new OperationCanceledException("用户已取消导出。");

        await Task.Run(() => File.Copy(sourcePath, selectedPath, overwrite: true));
        return selectedPath;
    }

    private static async Task<string?> PickSavePathAsync(
        string title,
        string suggestedFileName,
        string fileTypeName,
        IReadOnlyList<string> fileExtensions)
    {
        var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;

        if (mainWindow is null)
        {
            // 非经典桌面生命周期下回退到工作目录
            var workDir = WorkDirectoryManager.GetCurrentWorkDirectoryFromSettings();
            return Path.Combine(WorkDirectoryManager.GetDownloadsDirectory(workDir), suggestedFileName);
        }

        var options = new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            FileTypeChoices =
            [
                new FilePickerFileType(fileTypeName)
                {
                    Patterns = fileExtensions.Select(ext => $"*.{ext}").ToArray(),
                    AppleUniformTypeIdentifiers = [],
                    MimeTypes = [],
                },
            ],
        };

        var file = await mainWindow.StorageProvider.SaveFilePickerAsync(options);
        return file?.Path.LocalPath;
    }

    private static async Task<string> ExportToAndroidPublicFolderAsync(string sourcePath, string fileName, string mimeType)
    {
#if ANDROID
        // Android 10+：通过 MediaStore 写入公共 Downloads/ReadStorm，无需存储权限。
        var context = global::Android.App.Application.Context;
        if (!OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            var downloads = global::Android.OS.Environment
                .GetExternalStoragePublicDirectory(global::Android.OS.Environment.DirectoryDownloads)
                ?.AbsolutePath
                ?? "/storage/emulated/0/Download";
            var targetDir = Path.Combine(downloads, "ReadStorm");
            Directory.CreateDirectory(targetDir);
            var targetPath = Path.Combine(targetDir, fileName);
            await Task.Run(() => File.Copy(sourcePath, targetPath, overwrite: true));
            return targetPath;
        }

        var resolver = context.ContentResolver
            ?? throw new InvalidOperationException("无法获取 Android 内容解析器，导出终止。");
        var relativePath = "Download/ReadStorm";

        var values = new global::Android.Content.ContentValues();
        values.Put(global::Android.Provider.MediaStore.IMediaColumns.DisplayName, fileName);
        values.Put(global::Android.Provider.MediaStore.IMediaColumns.MimeType, mimeType);
        values.Put(global::Android.Provider.MediaStore.IMediaColumns.RelativePath, relativePath);
        values.Put(global::Android.Provider.MediaStore.IMediaColumns.IsPending, 1);

        var collection = global::Android.Provider.MediaStore.Downloads.ExternalContentUri;
        var item = resolver.Insert(collection, values)
            ?? throw new InvalidOperationException("创建导出文件失败，系统拒绝写入公共目录。");

        try
        {
            await using var input = File.OpenRead(sourcePath);
            await using var output = resolver.OpenOutputStream(item)
                ?? throw new InvalidOperationException("无法打开导出目标流。");
            await input.CopyToAsync(output);
        }
        catch
        {
            resolver.Delete(item, null, null);
            throw;
        }

        var pendingValues = new global::Android.Content.ContentValues();
        pendingValues.Put(global::Android.Provider.MediaStore.IMediaColumns.IsPending, 0);
        resolver.Update(item, pendingValues, null, null);

        return $"/storage/emulated/0/Download/ReadStorm/{fileName}";
#else
        await Task.CompletedTask;
        throw new PlatformNotSupportedException("当前平台不是 Android。导出失败。");
#endif
    }

    internal async Task SaveSettingsCoreAsync(bool showStatus, CancellationToken cancellationToken = default)
    {
        var reader = _parent.Reader;
        var settings = new AppSettings
        {
            DownloadPath = DownloadPath,
            MaxConcurrency = MaxConcurrency,
            AggregateSearchMaxConcurrency = AggregateSearchMaxConcurrency,
            MinIntervalMs = MinIntervalMs,
            MaxIntervalMs = MaxIntervalMs,
            ExportFormat = ExportFormat,
            ProxyEnabled = ProxyEnabled,
            ProxyHost = ProxyHost,
            ProxyPort = ProxyPort,
            EnableDiagnosticLog = EnableDiagnosticLog,
            AutoResumeAndRefreshOnStartup = AutoResumeAndRefreshOnStartup,

            ReaderFontSize = reader.ReaderFontSize,
            ReaderFontName = reader.SelectedFontName,
            ReaderLineHeight = reader.ReaderLineHeight,
            ReaderParagraphSpacing = reader.ReaderParagraphSpacing,
            ReaderBackground = reader.ReaderBackground,
            ReaderForeground = reader.ReaderForeground,
            ReaderDarkMode = reader.IsDarkMode,
            ReaderExtendIntoCutout = reader.ReaderExtendIntoCutout,
            ReaderContentMaxWidth = reader.ReaderContentMaxWidth,
            ReaderUseVolumeKeyPaging = reader.ReaderUseVolumeKeyPaging,
            ReaderHideSystemStatusBar = reader.ReaderHideSystemStatusBar,
            ReaderTopReservePx = ReaderTopReservePx,
            ReaderBottomReservePx = ReaderBottomReservePx,
            ReaderBottomStatusBarReservePx = ReaderBottomStatusBarReservePx,
            ReaderHorizontalInnerReservePx = ReaderHorizontalInnerReservePx,
            ReaderSidePaddingPx = ReaderSidePaddingPx,
            BookshelfProgressLeftPaddingPx = BookshelfProgressLeftPaddingPx,
            BookshelfProgressRightPaddingPx = BookshelfProgressRightPaddingPx,
            BookshelfProgressTotalWidthPx = BookshelfProgressTotalWidthPx,
            BookshelfProgressMinWidthPx = BookshelfProgressMinWidthPx,
            BookshelfProgressBarToPercentGapPx = BookshelfProgressBarToPercentGapPx,
            BookshelfProgressPercentTailGapPx = BookshelfProgressPercentTailGapPx,
        };
        await _appSettingsUseCase.SaveAsync(settings, cancellationToken);
        AppLogger.IsEnabled = settings.EnableDiagnosticLog;
        if (showStatus)
        {
            _parent.StatusMessage = "设置已保存到本地用户配置文件。";
            SaveFeedback = "✔ 已保存";
            _ = ClearSaveFeedbackAsync();
        }
    }

    private async Task ClearSaveFeedbackAsync()
    {
        await Task.Delay(2000);
        SaveFeedback = string.Empty;
    }

    public void QueueAutoSaveSettings()
    {
        if (_isLoadingSettings) return;
        _autoSaveCts?.Cancel();
        _autoSaveCts = new CancellationTokenSource();
        var cts = _autoSaveCts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, cts.Token);
                await SaveSettingsCoreAsync(showStatus: false, cts.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { AppLogger.Warn("Settings.AutoSave", ex); }
        });
    }

    internal async Task LoadSettingsAsync()
    {
        _isLoadingSettings = true;
        try
        {
            var settings = await _appSettingsUseCase.LoadAsync();
            DownloadPath = settings.DownloadPath;
            MaxConcurrency = settings.MaxConcurrency;
            AggregateSearchMaxConcurrency = settings.AggregateSearchMaxConcurrency;
            MinIntervalMs = settings.MinIntervalMs;
            MaxIntervalMs = settings.MaxIntervalMs;
            ExportFormat = settings.ExportFormat;
            ProxyEnabled = settings.ProxyEnabled;
            ProxyHost = settings.ProxyHost;
            ProxyPort = settings.ProxyPort;
            EnableDiagnosticLog = settings.EnableDiagnosticLog;
            AutoResumeAndRefreshOnStartup = settings.AutoResumeAndRefreshOnStartup;

            // 同步日志开关到全局
            AppLogger.IsEnabled = settings.EnableDiagnosticLog;

            var reader = _parent.Reader;
            reader.ReaderFontSize = settings.ReaderFontSize;
            reader.SelectedFontName = string.IsNullOrWhiteSpace(settings.ReaderFontName) ? "默认" : settings.ReaderFontName;
            reader.ReaderLineHeight = settings.ReaderLineHeight;
            reader.ReaderParagraphSpacing = settings.ReaderParagraphSpacing;
            reader.IsDarkMode = settings.ReaderDarkMode;
            reader.ReaderBackground = settings.ReaderBackground;
            reader.ReaderForeground = settings.ReaderForeground;
            reader.ReaderExtendIntoCutout = settings.ReaderExtendIntoCutout;
            reader.ReaderContentMaxWidth = settings.ReaderContentMaxWidth;
            reader.ReaderUseVolumeKeyPaging = settings.ReaderUseVolumeKeyPaging;
            reader.ReaderHideSystemStatusBar = settings.ReaderHideSystemStatusBar;

            ReaderTopReservePx = settings.ReaderTopReservePx;
            ReaderBottomReservePx = settings.ReaderBottomReservePx;
            ReaderBottomStatusBarReservePx = settings.ReaderBottomStatusBarReservePx;
            ReaderHorizontalInnerReservePx = settings.ReaderHorizontalInnerReservePx;
            ReaderSidePaddingPx = settings.ReaderSidePaddingPx;

            BookshelfProgressLeftPaddingPx = settings.BookshelfProgressLeftPaddingPx;
            BookshelfProgressRightPaddingPx = settings.BookshelfProgressRightPaddingPx;
            BookshelfProgressTotalWidthPx = settings.BookshelfProgressTotalWidthPx;
            BookshelfProgressMinWidthPx = settings.BookshelfProgressMinWidthPx;
            BookshelfProgressBarToPercentGapPx = settings.BookshelfProgressBarToPercentGapPx;
            BookshelfProgressPercentTailGapPx = settings.BookshelfProgressPercentTailGapPx;

            // 让 Reader 立即使用设置中的留白参数
            reader.ReaderTopReservePx = ReaderTopReservePx;
            reader.ReaderBottomReservePx = ReaderBottomReservePx;
            reader.ReaderBottomStatusBarReservePx = ReaderBottomStatusBarReservePx;
            reader.ReaderHorizontalInnerReservePx = ReaderHorizontalInnerReservePx;
            reader.ReaderSidePaddingPx = ReaderSidePaddingPx;

            var shelf = _parent.Bookshelf;
            shelf.BookshelfProgressLeftPaddingPx = BookshelfProgressLeftPaddingPx;
            shelf.BookshelfProgressRightPaddingPx = BookshelfProgressRightPaddingPx;
            shelf.BookshelfProgressTotalWidthPx = BookshelfProgressTotalWidthPx;
            shelf.BookshelfProgressMinWidthPx = BookshelfProgressMinWidthPx;
            shelf.BookshelfProgressBarToPercentGapPx = BookshelfProgressBarToPercentGapPx;
            shelf.BookshelfProgressPercentTailGapPx = BookshelfProgressPercentTailGapPx;

            _parent.UpdateDesktopWindowMinWidth(BookshelfProgressPercentTailGapPx);
        }
        finally { _isLoadingSettings = false; }
    }

    partial void OnReaderTopReservePxChanged(double value)
    {
        if (_isLoadingSettings) return;
        _parent.Reader.ReaderTopReservePx = value;
        QueueAutoSaveSettings();
    }

    partial void OnReaderBottomReservePxChanged(double value)
    {
        if (_isLoadingSettings) return;
        _parent.Reader.ReaderBottomReservePx = value;
        QueueAutoSaveSettings();
    }

    partial void OnReaderBottomStatusBarReservePxChanged(double value)
    {
        if (_isLoadingSettings) return;
        _parent.Reader.ReaderBottomStatusBarReservePx = value;
        QueueAutoSaveSettings();
    }

    partial void OnReaderHorizontalInnerReservePxChanged(double value)
    {
        if (_isLoadingSettings) return;
        _parent.Reader.ReaderHorizontalInnerReservePx = value;
        QueueAutoSaveSettings();
    }

    partial void OnReaderSidePaddingPxChanged(double value)
    {
        if (_isLoadingSettings) return;
        _parent.Reader.ReaderSidePaddingPx = value;
        QueueAutoSaveSettings();
    }

    partial void OnBookshelfProgressLeftPaddingPxChanged(double value)
    {
        if (_isLoadingSettings) return;
        _parent.Bookshelf.BookshelfProgressLeftPaddingPx = value;
        QueueAutoSaveSettings();
    }

    partial void OnBookshelfProgressRightPaddingPxChanged(double value)
    {
        if (_isLoadingSettings) return;
        _parent.Bookshelf.BookshelfProgressRightPaddingPx = value;
        QueueAutoSaveSettings();
    }

    partial void OnBookshelfProgressTotalWidthPxChanged(double value)
    {
        if (_isLoadingSettings) return;
        _parent.Bookshelf.BookshelfProgressTotalWidthPx = value;
        QueueAutoSaveSettings();
    }

    partial void OnBookshelfProgressMinWidthPxChanged(double value)
    {
        if (_isLoadingSettings) return;
        _parent.Bookshelf.BookshelfProgressMinWidthPx = value;
        QueueAutoSaveSettings();
    }

    partial void OnBookshelfProgressBarToPercentGapPxChanged(double value)
    {
        if (_isLoadingSettings) return;
        _parent.Bookshelf.BookshelfProgressBarToPercentGapPx = value;
        QueueAutoSaveSettings();
    }

    partial void OnBookshelfProgressPercentTailGapPxChanged(double value)
    {
        if (_isLoadingSettings) return;
        _parent.Bookshelf.BookshelfProgressPercentTailGapPx = value;
        _parent.UpdateDesktopWindowMinWidth(value);
        QueueAutoSaveSettings();
    }
}
