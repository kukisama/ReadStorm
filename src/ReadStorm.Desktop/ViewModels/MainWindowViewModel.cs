using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using ReadStorm.Application.Abstractions;
using ReadStorm.Domain.Models;

namespace ReadStorm.Desktop.ViewModels;

/// <summary>Tab 页索引常量，避免魔法数字。</summary>
public static class TabIndex
{
    public const int SearchDownload = 0;
    public const int DownloadTask = 1;
    public const int Diagnostic = 2;
    public const int Bookshelf = 3;
    public const int Reader = 4;
    public const int RuleEditor = 5;
    public const int Settings = 6;
}

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IRuleCatalogUseCase _ruleCatalogUseCase;

#if DEBUG
    public bool IsDebugBuild => true;
#else
    public bool IsDebugBuild => false;
#endif

    // ── Lazy-init infrastructure ──
    private readonly SemaphoreSlim _settingsInitLock = new(1, 1);
    private readonly SemaphoreSlim _sourcesInitLock = new(1, 1);
    private readonly SemaphoreSlim _bookshelfInitLock = new(1, 1);
    private readonly SemaphoreSlim _ruleEditorInitLock = new(1, 1);
    private readonly SemaphoreSlim _readerInitLock = new(1, 1);
    private readonly SemaphoreSlim _diagnosticInitLock = new(1, 1);

    private bool _settingsInitialized;
    private bool _sourcesInitialized;
    private bool _bookshelfInitialized;
    private bool _ruleEditorInitialized;
    private bool _readerInitialized;
    private bool _diagnosticInitialized;

    // ── Sub-ViewModels ──
    public SearchDownloadViewModel SearchDownload { get; }
    public DiagnosticViewModel Diagnostic { get; }
    public BookshelfViewModel Bookshelf { get; }
    public ReaderViewModel Reader { get; }
    public RuleEditorViewModel RuleEditor { get; }
    public SettingsViewModel Settings { get; }

    public MainWindowViewModel(
        ISearchBooksUseCase searchBooksUseCase,
        IDownloadBookUseCase downloadBookUseCase,
        ICoverUseCase coverUseCase,
        IAppSettingsUseCase appSettingsUseCase,
        IRuleCatalogUseCase ruleCatalogUseCase,
        ISourceDiagnosticUseCase sourceDiagnosticUseCase,
        IBookshelfUseCase bookshelfUseCase,
        ISourceHealthCheckUseCase healthCheckUseCase,
        IBookRepository bookRepo,
        IRuleEditorUseCase ruleEditorUseCase)
    {
        _ruleCatalogUseCase = ruleCatalogUseCase;

        Settings = new SettingsViewModel(this, appSettingsUseCase);
        Diagnostic = new DiagnosticViewModel(this, sourceDiagnosticUseCase);
        RuleEditor = new RuleEditorViewModel(this, ruleEditorUseCase);
        Bookshelf = new BookshelfViewModel(this, bookshelfUseCase, bookRepo, downloadBookUseCase, coverUseCase, appSettingsUseCase);
        Reader = new ReaderViewModel(this, bookRepo, downloadBookUseCase, coverUseCase, bookshelfUseCase);
        SearchDownload = new SearchDownloadViewModel(this, searchBooksUseCase, downloadBookUseCase, bookRepo, healthCheckUseCase);

        Title = "ReadStorm - 下载器重构M0";
        StatusMessage = "就绪：可先用假数据验证 UI 与流程。";

        _ = SafeFireAndForgetAsync(EnsureSettingsInitializedAsync());
        _ = SafeFireAndForgetAsync(EnsureSearchDownloadInitializedAsync());
    }

    // ==================== Shared Properties ====================

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private int availableSourceCount;

    [ObservableProperty]
    private int selectedTabIndex;

    /// <summary>首次从书架打开书籍前隐藏阅读页，避免误点空页面。</summary>
    [ObservableProperty]
    private bool isReaderTabVisible;

    /// <summary>所有书源（共享集合，供多个子 VM 引用）。</summary>
    public ObservableCollection<SourceItem> Sources { get; } = [];

    /// <summary>阅读 tab 保护：没有打开任何书时不允许切换到阅读页。</summary>
    partial void OnSelectedTabIndexChanged(int oldValue, int newValue)
    {
        _ = EnsureTabInitializedAsync(newValue);

        // 切到书架页时懒刷新
        if (newValue == TabIndex.Bookshelf)
        {
            _ = Bookshelf.RefreshDbBooksIfNeededAsync(force: true);
        }

        if (IsReaderTabVisible && newValue == TabIndex.Reader
            && Reader.SelectedDbBook is null && Reader.SelectedBookshelfItem is null)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => SelectedTabIndex = oldValue);
            StatusMessage = "请先在书架中打开一本书，再进入阅读界面。";
        }
    }

    // ==================== Lazy Initialization ====================

    private async Task EnsureSettingsInitializedAsync()
    {
        if (_settingsInitialized) return;
        await _settingsInitLock.WaitAsync();
        try
        {
            if (_settingsInitialized) return;
            await Settings.LoadSettingsAsync();
            _settingsInitialized = true;
        }
        finally { _settingsInitLock.Release(); }
    }

    private async Task EnsureSourcesInitializedAsync()
    {
        if (_sourcesInitialized) return;
        await _sourcesInitLock.WaitAsync();
        try
        {
            if (_sourcesInitialized) return;
            await LoadRuleStatsAsync();
            _sourcesInitialized = true;
        }
        finally { _sourcesInitLock.Release(); }
    }

    private async Task EnsureSearchDownloadInitializedAsync()
    {
        await EnsureSourcesInitializedAsync();
    }

    private async Task EnsureDiagnosticInitializedAsync()
    {
        if (_diagnosticInitialized) return;
        await _diagnosticInitLock.WaitAsync();
        try
        {
            if (_diagnosticInitialized) return;
            await EnsureSourcesInitializedAsync();
            _diagnosticInitialized = true;
        }
        finally { _diagnosticInitLock.Release(); }
    }

    private async Task EnsureBookshelfInitializedAsync()
    {
        if (_bookshelfInitialized) return;
        await _bookshelfInitLock.WaitAsync();
        try
        {
            if (_bookshelfInitialized) return;
            await Bookshelf.InitAsync();
            _bookshelfInitialized = true;
        }
        finally { _bookshelfInitLock.Release(); }
    }

    private async Task EnsureRuleEditorInitializedAsync()
    {
        if (_ruleEditorInitialized) return;
        await _ruleEditorInitLock.WaitAsync();
        try
        {
            if (_ruleEditorInitialized) return;
            await EnsureSourcesInitializedAsync();
            await RuleEditor.LoadRuleListCommand.ExecuteAsync(null);
            _ruleEditorInitialized = true;
        }
        finally { _ruleEditorInitLock.Release(); }
    }

    private async Task EnsureReaderInitializedAsync()
    {
        if (_readerInitialized) return;
        await _readerInitLock.WaitAsync();
        try
        {
            if (_readerInitialized) return;
            await EnsureSettingsInitializedAsync();
            _readerInitialized = true;
        }
        finally { _readerInitLock.Release(); }
    }

    private async Task EnsureTabInitializedAsync(int newValue)
    {
        try
        {
            if (newValue is TabIndex.SearchDownload or TabIndex.DownloadTask)
            {
                await EnsureSearchDownloadInitializedAsync();
                return;
            }

            if (newValue == TabIndex.Diagnostic)
            {
                await EnsureDiagnosticInitializedAsync();
                return;
            }

            if (newValue == TabIndex.Bookshelf)
            {
                await EnsureBookshelfInitializedAsync();
                return;
            }

            if (newValue == TabIndex.Reader)
            {
                // Tab 4 = 阅读器（隐藏时用户无法点击，保留 fallback）
                if (IsReaderTabVisible)
                    await EnsureReaderInitializedAsync();
                return;
            }

            if (newValue == TabIndex.RuleEditor)
            {
                await EnsureRuleEditorInitializedAsync();
                return;
            }

            if (newValue == TabIndex.Settings)
            {
                await EnsureSettingsInitializedAsync();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"模块初始化失败：{ex.Message}";
        }
    }

    // ==================== Shared Methods ====================

    /// <summary>加载书源规则列表到共享 Sources 集合。</summary>
    private async Task LoadRuleStatsAsync()
    {
        var rules = await _ruleCatalogUseCase.GetAllAsync();
        Sources.Clear();
        Sources.Add(new SourceItem(new BookSourceRule
        {
            Id = 0,
            Name = "全部书源",
            Url = string.Empty,
            SearchSupported = true,
        }));

        foreach (var rule in rules)
            Sources.Add(new SourceItem(rule));

        SearchDownload.SelectedSourceId = rules.FirstOrDefault()?.Id ?? 0;
        AvailableSourceCount = rules.Count;
        StatusMessage = $"就绪：已加载 {AvailableSourceCount} 条书源规则，可切换测试。";

        // 启动后台健康检测
        _ = SearchDownload.RefreshSourceHealthCommand.ExecuteAsync(null);
    }

    /// <summary>双击书架打开书并跳转阅读 tab（由 code-behind 调用）。</summary>
    public async Task OpenDbBookAndSwitchToReaderAsync(BookEntity book)
    {
        await EnsureReaderInitializedAsync();
        await Bookshelf.OpenDbBookCommand.ExecuteAsync(book);
    }

    /// <summary>
    /// 安全执行 fire-and-forget 异步任务，捕获异常并显示状态栏提示，
    /// 避免未观察异常导致 Android 闪退。
    /// </summary>
    private async Task SafeFireAndForgetAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            StatusMessage = $"初始化异常：{ex.Message}";
            System.Diagnostics.Trace.WriteLine($"[ReadStorm] SafeFireAndForget: {ex}");
        }
    }
}
