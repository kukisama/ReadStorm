using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
    }

    [ObservableProperty]
    private string downloadPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ReadStorm");

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

            ReaderFontSize = reader.ReaderFontSize,
            ReaderFontName = reader.SelectedFontName,
            ReaderLineHeight = reader.ReaderLineHeight,
            ReaderParagraphSpacing = reader.ReaderParagraphSpacing,
            ReaderBackground = reader.ReaderBackground,
            ReaderForeground = reader.ReaderForeground,
            ReaderDarkMode = reader.IsDarkMode,
        };
        await _appSettingsUseCase.SaveAsync(settings, cancellationToken);
        if (showStatus) _parent.StatusMessage = "设置已保存到本地用户配置文件。";
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

            var reader = _parent.Reader;
            reader.ReaderFontSize = settings.ReaderFontSize;
            reader.SelectedFontName = string.IsNullOrWhiteSpace(settings.ReaderFontName) ? "默认" : settings.ReaderFontName;
            reader.ReaderLineHeight = settings.ReaderLineHeight;
            reader.ReaderParagraphSpacing = settings.ReaderParagraphSpacing;
            reader.IsDarkMode = settings.ReaderDarkMode;
            reader.ReaderBackground = settings.ReaderBackground;
            reader.ReaderForeground = settings.ReaderForeground;
        }
        finally { _isLoadingSettings = false; }
    }
}
