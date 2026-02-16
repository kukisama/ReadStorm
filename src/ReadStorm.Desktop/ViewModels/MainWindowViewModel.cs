using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReadStorm.Application.Abstractions;
using ReadStorm.Domain.Models;
using ReadStorm.Infrastructure.Services;

namespace ReadStorm.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ISearchBooksUseCase _searchBooksUseCase;
    private readonly IDownloadBookUseCase _downloadBookUseCase;
    private readonly IAppSettingsUseCase _appSettingsUseCase;
    private readonly IRuleCatalogUseCase _ruleCatalogUseCase;
    private readonly ISourceDiagnosticUseCase _sourceDiagnosticUseCase;
    private readonly IBookshelfUseCase _bookshelfUseCase;
    private readonly ISourceHealthCheckUseCase _healthCheckUseCase;
    private readonly IBookRepository _bookRepo;
    private readonly IRuleEditorUseCase _ruleEditorUseCase;
    private readonly SourceDownloadQueue _downloadQueue = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _downloadCts = new();
    private readonly HashSet<Guid> _pauseRequested = new();
    private CancellationTokenSource? _settingsAutoSaveCts;
    private bool _isLoadingSettings;
    private bool _bookshelfDirty = true;
    private DateTimeOffset _lastBookshelfRefreshAt = DateTimeOffset.MinValue;

    public MainWindowViewModel(
        ISearchBooksUseCase searchBooksUseCase,
        IDownloadBookUseCase downloadBookUseCase,
        IAppSettingsUseCase appSettingsUseCase,
        IRuleCatalogUseCase ruleCatalogUseCase,
        ISourceDiagnosticUseCase sourceDiagnosticUseCase,
        IBookshelfUseCase bookshelfUseCase,
        ISourceHealthCheckUseCase healthCheckUseCase,
        IBookRepository bookRepo,
        IRuleEditorUseCase ruleEditorUseCase)
    {
        _searchBooksUseCase = searchBooksUseCase;
        _downloadBookUseCase = downloadBookUseCase;
        _appSettingsUseCase = appSettingsUseCase;
        _ruleCatalogUseCase = ruleCatalogUseCase;
        _sourceDiagnosticUseCase = sourceDiagnosticUseCase;
        _bookshelfUseCase = bookshelfUseCase;
        _healthCheckUseCase = healthCheckUseCase;
        _bookRepo = bookRepo;
        _ruleEditorUseCase = ruleEditorUseCase;

        Title = "ReadStorm - 下载器重构M0";
        StatusMessage = "就绪：可先用假数据验证 UI 与流程。";

        _ = LoadSettingsAsync();
        _ = LoadRuleStatsAsync();
        _ = InitBookshelfAndResumeAsync();
    }

    /// <summary>加载书架后自动恢复未完成的下载任务 + 补抓缺封面。</summary>
    private async Task InitBookshelfAndResumeAsync()
    {
        await LoadBookshelfAsync();
        await ResumeIncompleteDownloadsAsync();
        _ = AutoFetchMissingCoversAsync();
    }

    /// <summary>启动时为没有封面的书籍自动抓取封面。</summary>
    private async Task AutoFetchMissingCoversAsync()
    {
        try
        {
            var booksNeedCover = DbBooks.Where(b => !b.HasCover && !string.IsNullOrWhiteSpace(b.TocUrl)).ToList();
            if (booksNeedCover.Count == 0) return;

            StatusMessage = $"正在为 {booksNeedCover.Count} 本书补抓封面…";

            foreach (var book in booksNeedCover)
            {
                try
                {
                    await _downloadBookUseCase.RefreshCoverAsync(book);
                }
                catch
                {
                    // 单本失败不影响其他
                }
            }

            await RefreshDbBooksAsync();
            StatusMessage = $"封面补抓完成（共 {booksNeedCover.Count} 本）。";
        }
        catch
        {
            // 启动时不让封面抓取异常影响主流程
        }
    }

    /// <summary>启动时扫描 DB，将未完成的书籍自动恢复下载。</summary>
    private async Task ResumeIncompleteDownloadsAsync()
    {
        try
        {
            var incompleteBooks = DbBooks.Where(b => !b.IsComplete).ToList();
            if (incompleteBooks.Count == 0) return;

            StatusMessage = $"正在恢复 {incompleteBooks.Count} 个未完成的下载任务…";

            foreach (var book in incompleteBooks)
            {
                // 避免重复添加（如果用户手动已恢复）
                if (DownloadTasks.Any(t => t.BookId == book.Id
                    && t.CurrentStatus is DownloadTaskStatus.Queued or DownloadTaskStatus.Downloading))
                    continue;

                var searchResult = new SearchResult(
                    Id: Guid.NewGuid(),
                    Title: book.Title,
                    Author: book.Author,
                    SourceId: book.SourceId,
                    SourceName: string.Empty,
                    Url: book.TocUrl,
                    LatestChapter: string.Empty,
                    UpdatedAt: DateTimeOffset.Now
                );

                var task = new DownloadTask
                {
                    Id = Guid.NewGuid(),
                    BookTitle = book.Title,
                    Author = book.Author,
                    Mode = DownloadMode.FullBook,
                    EnqueuedAt = DateTimeOffset.Now,
                    SourceSearchResult = searchResult,
                };
                task.BookId = book.Id;

                DownloadTasks.Insert(0, task);
                _ = RunDownloadInBackgroundAsync(task, searchResult);
            }

            ApplyTaskFilter();
            StatusMessage = $"已恢复 {incompleteBooks.Count} 个未完成的下载任务。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"恢复下载任务失败：{ex.Message}";
        }
    }

    // ==================== Collections ====================
    public ObservableCollection<SearchResult> SearchResults { get; } = [];

    public ObservableCollection<DownloadTask> DownloadTasks { get; } = [];

    public ObservableCollection<DownloadTask> FilteredDownloadTasks { get; } = [];

    public ObservableCollection<SourceItem> Sources { get; } = [];

    public ObservableCollection<BookRecord> BookshelfItems { get; } = [];

    /// <summary>从 SQLite 加载的书架（BookEntity）。</summary>
    public ObservableCollection<BookEntity> DbBooks { get; } = [];

    /// <summary>书架展示模式：false=标准列表，true=竖排大图。</summary>
    [ObservableProperty]
    private bool isBookshelfLargeMode;

    /// <summary>大图书架每行列数（响应式，3-5 列）。</summary>
    [ObservableProperty]
    private int bookshelfLargeColumnCount = 3;

    // ==================== Observable Properties ====================
    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private int availableSourceCount;

    [ObservableProperty]
    private string searchKeyword = "诡秘之主";

    [ObservableProperty]
    private int selectedSourceId;

    [ObservableProperty]
    private bool isSearching;

    [ObservableProperty]
    private bool isCheckingHealth;

    [ObservableProperty]
    private SearchResult? selectedSearchResult;

    // --- Settings ---
    [ObservableProperty]
    private string downloadPath = "downloads";

    [ObservableProperty]
    private int maxConcurrency = 6;

    [ObservableProperty]
    private int aggregateSearchMaxConcurrency = 5;

    [ObservableProperty]
    private int minIntervalMs = 200;

    [ObservableProperty]
    private int maxIntervalMs = 400;

    [ObservableProperty]
    private string exportFormat = "txt";

    [ObservableProperty]
    private bool proxyEnabled;

    [ObservableProperty]
    private string proxyHost = "127.0.0.1";

    [ObservableProperty]
    private int proxyPort = 7890;

    // --- Download task filter ---
    [ObservableProperty]
    private string taskFilterStatus = "全部";

    partial void OnTaskFilterStatusChanged(string value) => ApplyTaskFilter();

    // --- Diagnostic ---
    private readonly Dictionary<int, SourceDiagnosticResult> _diagnosticResults = new();

    [ObservableProperty]
    private bool isDiagnosing;

    [ObservableProperty]
    private string diagnosticSummary = string.Empty;

    public ObservableCollection<string> DiagnosticSourceNames { get; } = [];

    [ObservableProperty]
    private string? selectedDiagnosticSource;

    partial void OnSelectedDiagnosticSourceChanged(string? value)
    {
        DiagnosticLines.Clear();
        if (value is null) return;

        // 从 "[id] name" 提取 id
        var match = System.Text.RegularExpressions.Regex.Match(value, @"\[(\d+)\]");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var id)
            && _diagnosticResults.TryGetValue(id, out var result))
        {
            var header = $"[{result.SourceName}] {result.Summary} | HTTP={result.HttpStatusCode} | " +
                         $"搜索={result.SearchResultCount}条 | 目录selector='{result.TocSelector}' " +
                         $"| 章节selector='{result.ChapterContentSelector}'";
            DiagnosticLines.Add(header);
            DiagnosticLines.Add(new string('─', 60));
            foreach (var line in result.DiagnosticLines)
            {
                DiagnosticLines.Add(line);
            }
        }
    }

    public ObservableCollection<string> DiagnosticLines { get; } = [];

    // --- Reader ---
    [ObservableProperty]
    private BookRecord? selectedBookshelfItem;

    [ObservableProperty]
    private string readerContent = string.Empty;

    /// <summary>按\n拆分后的段落集合，用于 ItemsControl 渲染。</summary>
    public ObservableCollection<string> ReaderParagraphs { get; } = [];

    partial void OnReaderContentChanged(string value)
    {
        RebuildParagraphs();
    }

    private void RebuildParagraphs()
    {
        ReaderParagraphs.Clear();
        if (string.IsNullOrEmpty(ReaderContent)) return;
        foreach (var line in ReaderContent.Split('\n'))
        {
            ReaderParagraphs.Add(line);
        }
    }

    [ObservableProperty]
    private string readerTitle = string.Empty;

    public ObservableCollection<string> ReaderChapters { get; } = [];

    [ObservableProperty]
    private int readerCurrentChapterIndex;

    [ObservableProperty]
    private string? selectedReaderChapter;

    // --- Tab / TOC overlay ---
    [ObservableProperty]
    private int selectedTabIndex;

    /// <summary>首次从书架打开书籍前隐藏阅读页，避免误点空页面。</summary>
    [ObservableProperty]
    private bool isReaderTabVisible;

    /// <summary>阅读 tab 保护：没有打开任何书时不允许切换到阅读页。</summary>
    partial void OnSelectedTabIndexChanged(int oldValue, int newValue)
    {
        // 切到书架页时懒刷新（仅此时打 DB）
        if (newValue == 3)
        {
            _ = RefreshDbBooksIfNeededAsync(force: true);
        }

        if (IsReaderTabVisible && newValue == 4 && SelectedDbBook is null && SelectedBookshelfItem is null)
        {
            // 回退到之前的 tab
            Avalonia.Threading.Dispatcher.UIThread.Post(() => SelectedTabIndex = oldValue);
            StatusMessage = "请先在书架中打开一本书，再进入阅读界面。";
        }

        // 切到规则处理 tab 时自动加载规则列表
        if ((newValue == 5 || (!IsReaderTabVisible && newValue == 4)) && RuleEditorRules.Count == 0)
        {
            _ = LoadRuleListAsync();
        }
    }

    [ObservableProperty]
    private bool isTocOverlayVisible;

    /// <summary>用于通知 View 滚动到阅读器顶部。每次递增触发 PropertyChanged。</summary>
    [ObservableProperty]
    private int readerScrollVersion;

    /// <summary>TOC 目录列数（响应式，2-4列）。</summary>
    [ObservableProperty]
    private int tocColumnCount = 3;

    /// <summary>是否显示封面候选选择面板。</summary>
    [ObservableProperty]
    private bool isCoverPickerVisible;

    /// <summary>封面候选加载中。</summary>
    [ObservableProperty]
    private bool isLoadingCoverCandidates;

    /// <summary>当前封面候选项。</summary>
    public ObservableCollection<CoverCandidate> CoverCandidates { get; } = [];

    [ObservableProperty]
    private CoverCandidate? selectedCoverCandidate;

    // --- 换源 ---
    /// <summary>换源操作进行中。</summary>
    [ObservableProperty]
    private bool isSourceSwitching;

    /// <summary>换源下拉选中的书源（选中即触发换源）。</summary>
    [ObservableProperty]
    private SourceItem? selectedSwitchSource;

    /// <summary>换源可用书源列表（按健康状态排序：绿→灰→红，排除 Id=0）。</summary>
    public ObservableCollection<SourceItem> SortedSwitchSources { get; } = [];

    /// <summary>刷新换源列表：按健康状态排序，绿→灰→红。</summary>
    private void RefreshSortedSwitchSources()
    {
        SortedSwitchSources.Clear();
        var sorted = Sources
            .Where(s => s.Id > 0 && s.SearchSupported)
            .OrderByDescending(s => s.IsHealthy == true)   // 绿在前
            .ThenByDescending(s => s.IsHealthy is null)     // 灰居中
            .ThenBy(s => s.Id);
        foreach (var s in sorted)
            SortedSwitchSources.Add(s);
    }

    partial void OnSelectedReaderChapterChanged(string? value)
    {
        if (value is not null)
        {
            var index = ReaderChapters.IndexOf(value);
            if (index >= 0)
            {
                _ = NavigateToChapterAsync(index);
            }
        }
    }

    // ==================== Search & Download Commands ====================
    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchKeyword) || IsSearching)
        {
            return;
        }

        try
        {
            IsSearching = true;
            StatusMessage = "搜索中...";
            SearchResults.Clear();

            var keyword = SearchKeyword.Trim();
            var selectedSourceText = SelectedSourceId > 0 ? $"书源 {SelectedSourceId}" : "全部书源(健康)";

            // 单书源：保持原逻辑
            if (SelectedSourceId > 0)
            {
                var results = await _searchBooksUseCase.ExecuteAsync(keyword, SelectedSourceId);
                foreach (var item in results)
                {
                    var src = Sources.FirstOrDefault(s => s.Id == item.SourceId);
                    var srcName = src?.Name ?? $"书源{item.SourceId}";
                    SearchResults.Add(item with { SourceName = srcName });
                }
            }
            else
            {
                // 全部书源：只搜绿色节点；并发执行；每节点仅取前3条
                var healthySources = Sources
                    .Where(s => s.Id > 0 && s.IsHealthy == true && s.SearchSupported)
                    .ToList();

                if (healthySources.Count == 0)
                {
                    StatusMessage = "搜索完成（全部书源(健康)）：0 条。当前没有可用的绿色节点，请先刷新书源健康状态。";
                    return;
                }

                const int perSourceLimit = 3;
                var maxConcurrent = Math.Clamp(AggregateSearchMaxConcurrency, 1, 64);
                var semaphore = new SemaphoreSlim(maxConcurrent);

                var tasks = healthySources.Select(async src =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var one = await _searchBooksUseCase.ExecuteAsync(keyword, src.Id);
                        return one.Take(perSourceLimit)
                                  .Select(x => x with { SourceName = src.Name })
                                  .ToList();
                    }
                    catch
                    {
                        return [];
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }).ToList();

                var perSourceResults = await Task.WhenAll(tasks);

                // 仅在同一来源内去重（标题+作者+SourceId）；跨来源保留，便于用户比较来源质量
                var merged = perSourceResults
                    .SelectMany(x => x)
                    .GroupBy(x => $"{x.Title}|{x.Author}|{x.SourceId}", StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                foreach (var item in merged)
                {
                    SearchResults.Add(item);
                }

                selectedSourceText = $"全部书源(健康:{healthySources.Count}源,每源前{perSourceLimit}条)";
            }

            if (SearchResults.Count == 0 && SelectedSourceId > 0)
            {
                StatusMessage = $"搜索完成（{selectedSourceText}）：0 条。该书源当前可能限流/规则不兼容，请切换书源重试。";
            }
            else
            {
                StatusMessage = $"搜索完成（{selectedSourceText}）：共 {SearchResults.Count} 条";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"搜索失败：{ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private async Task QueueDownloadAsync()
    {
        if (SelectedSearchResult is null)
        {
            StatusMessage = "请先在搜索结果中选择一本书。";
            return;
        }

        if (SelectedSearchResult.Url.Contains("example.com", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = "当前是示例搜索结果，不支持真实下载。请切换具体书源重新搜索。";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedSearchResult.Url))
        {
            StatusMessage = "搜索结果缺少书籍 URL，无法下载。";
            return;
        }

        var searchResult = SelectedSearchResult;
        var task = new DownloadTask
        {
            Id = Guid.NewGuid(),
            BookTitle = searchResult.Title,
            Author = searchResult.Author,
            Mode = DownloadMode.FullBook,
            EnqueuedAt = DateTimeOffset.Now,
            SourceSearchResult = searchResult,
        };

        DownloadTasks.Insert(0, task);
        ApplyTaskFilter();
        StatusMessage = $"已加入下载队列：《{task.BookTitle}》";

        // Fire-and-forget: 立即释放 UI，后台下载
        _ = RunDownloadInBackgroundAsync(task, searchResult);
    }

    private async Task RunDownloadInBackgroundAsync(DownloadTask task, SearchResult searchResult)
    {
        var cts = new CancellationTokenSource();
        _downloadCts[task.Id] = cts;

        // 下载期间每 5 秒刷新一次 DbBooks，让用户尽早看到并打开正在下载的书
        var refreshCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        _ = PeriodicRefreshDbBooksAsync(refreshCts.Token);

        try
        {
            // 同书源串行排队，不同书源并行
            await _downloadQueue.EnqueueAsync(searchResult.SourceId, async () =>
            {
                await _downloadBookUseCase.QueueAsync(task, searchResult, task.Mode, cts.Token);
            }, cts.Token);
        }
        catch (Exception ex)
        {
            if (task.CurrentStatus is DownloadTaskStatus.Queued or DownloadTaskStatus.Downloading)
            {
                try { task.TransitionTo(DownloadTaskStatus.Failed); } catch { /* already transitioned */ }
                task.Error = ex.Message;
            }
        }
        finally
        {
            refreshCts.Cancel();
            refreshCts.Dispose();

            // 检查是否为暂停操作（而非取消）
            bool wasPaused;
            lock (_pauseRequested) wasPaused = _pauseRequested.Remove(task.Id);
            if (wasPaused && task.CurrentStatus is DownloadTaskStatus.Cancelled)
            {
                task.OverrideToPaused();
            }

            _downloadCts.TryRemove(task.Id, out _);
            cts.Dispose();
        }

        await OnDownloadCompleted(task);
    }

    private async Task PeriodicRefreshDbBooksAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(5000, ct);
                // 懒刷新：书架不可见时仅标记脏数据，不查询数据库
                if (SelectedTabIndex == 3)
                {
                    await RefreshDbBooksIfNeededAsync();
                }
                else
                {
                    MarkBookshelfDirty();
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    [RelayCommand]
    private Task RetryDownloadAsync(DownloadTask? task)
    {
        if (task is null || !task.CanRetry)
        {
            return Task.CompletedTask;
        }

        var searchResult = task.SourceSearchResult;
        if (searchResult is null)
        {
            StatusMessage = $"无法重试：《{task.BookTitle}》缺少原始搜索信息。";
            return Task.CompletedTask;
        }

        task.ResetForRetry();
        ApplyTaskFilter();
        StatusMessage = $"正在重试（第{task.RetryCount}次）：《{task.BookTitle}》...";

        _ = RunDownloadInBackgroundAsync(task, searchResult);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void CancelDownload(DownloadTask? task)
    {
        if (task is null || !task.CanCancel)
        {
            return;
        }

        try
        {
            if (_downloadCts.TryGetValue(task.Id, out var cts))
            {
                cts.Cancel();
            }

            task.TransitionTo(DownloadTaskStatus.Cancelled);
            task.Error = "用户手动取消";
            ApplyTaskFilter();
            StatusMessage = $"已取消：《{task.BookTitle}》";
        }
        catch (Exception ex)
        {
            StatusMessage = $"取消失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private void DeleteDownload(DownloadTask? task)
    {
        if (task is null || !task.CanDelete)
        {
            return;
        }

        // 如果还在运行，先取消
        if (_downloadCts.TryGetValue(task.Id, out var cts))
        {
            cts.Cancel();
        }

        DownloadTasks.Remove(task);
        FilteredDownloadTasks.Remove(task);
        StatusMessage = $"已删除任务：《{task.BookTitle}》";
    }

    [RelayCommand]
    private void PauseDownload(DownloadTask? task)
    {
        if (task is null || !task.CanPause) return;
        lock (_pauseRequested) _pauseRequested.Add(task.Id);
        if (_downloadCts.TryGetValue(task.Id, out var cts)) cts.Cancel();
        ApplyTaskFilter();
        StatusMessage = $"正在暂停：《{task.BookTitle}》…";
    }

    [RelayCommand]
    private void StopAllDownloads()
    {
        var active = DownloadTasks
            .Where(t => t.CanPause)
            .ToList();
        if (active.Count == 0)
        {
            StatusMessage = "没有可停止的下载任务。";
            return;
        }

        foreach (var task in active)
        {
            lock (_pauseRequested) _pauseRequested.Add(task.Id);
            if (_downloadCts.TryGetValue(task.Id, out var cts)) cts.Cancel();
        }
        ApplyTaskFilter();
        StatusMessage = $"已全部停止：{active.Count} 个任务。";
    }

    [RelayCommand]
    private Task StartAllDownloadsAsync()
    {
        var paused = DownloadTasks
            .Where(t => t.CanResume)
            .ToList();
        if (paused.Count == 0)
        {
            StatusMessage = "没有可恢复的下载任务。";
            return Task.CompletedTask;
        }

        foreach (var task in paused)
        {
            var searchResult = task.SourceSearchResult;
            if (searchResult is null) continue;
            task.ResetForResume();
            _ = RunDownloadInBackgroundAsync(task, searchResult);
        }
        ApplyTaskFilter();
        StatusMessage = $"已全部恢复：{paused.Count} 个任务。";
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task ResumeDownloadTaskAsync(DownloadTask? task)
    {
        if (task is null || !task.CanResume) return Task.CompletedTask;
        var searchResult = task.SourceSearchResult;
        if (searchResult is null)
        {
            StatusMessage = $"无法恢复：缺少原始搜索信息。";
            return Task.CompletedTask;
        }

        task.ResetForResume();
        ApplyTaskFilter();
        StatusMessage = $"恢复下载：《{task.BookTitle}》";
        _ = RunDownloadInBackgroundAsync(task, searchResult);
        return Task.CompletedTask;
    }

    private async Task OnDownloadCompleted(DownloadTask task)
    {
        ApplyTaskFilter();
        var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "readstorm-download.log");
        if (task.CurrentStatus == DownloadTaskStatus.Succeeded)
        {
            StatusMessage = $"下载完成：《{task.BookTitle}》。调试日志：{logPath}";

            // Auto-add to bookshelf (legacy JSON)
            await AddToBookshelfAsync(task);
        }
        else
        {
            StatusMessage = $"下载失败（{task.ErrorKind}）：{task.Error}。调试日志：{logPath}";
        }

        // 书架懒刷新：先标脏；若当前就在书架页则立即刷新
        MarkBookshelfDirty();
        if (SelectedTabIndex == 3)
        {
            await RefreshDbBooksIfNeededAsync(force: true);
        }
    }

    // ==================== Task Filtering ====================
    private void ApplyTaskFilter()
    {
        FilteredDownloadTasks.Clear();
        foreach (var task in DownloadTasks)
        {
            if (TaskFilterStatus == "全部" || task.Status == TaskFilterStatus)
            {
                FilteredDownloadTasks.Add(task);
            }
        }
    }

    // ==================== Diagnostic Commands ====================
    [RelayCommand]
    private async Task RunBatchDiagnosticAsync()
    {
        try
        {
            IsDiagnosing = true;
            DiagnosticSummary = "正在批量诊断所有书源…";
            DiagnosticLines.Clear();
            _diagnosticResults.Clear();
            DiagnosticSourceNames.Clear();

            var rules = Sources.Where(s => s.Id > 0).ToList();
            var total = rules.Count;
            var completed = 0;
            var healthy = 0;

            // 并发诊断全部书源
            var tasks = rules.Select(async source =>
            {
                var result = await _sourceDiagnosticUseCase.DiagnoseAsync(source.Id, "测试");
                var idx = Interlocked.Increment(ref completed);
                if (result.IsHealthy) Interlocked.Increment(ref healthy);
                return result;
            });

            var results = await Task.WhenAll(tasks);
            foreach (var r in results.OrderBy(r => r.SourceId))
            {
                _diagnosticResults[r.SourceId] = r;
                var dot = r.IsHealthy ? "●" : "●";
                var prefix = r.IsHealthy ? "🟢" : "🔴";
                DiagnosticSourceNames.Add($"{prefix} [{r.SourceId}] {r.SourceName}");
            }

            DiagnosticSummary = $"批量诊断完成：{healthy}/{total} 个书源正常";
            StatusMessage = DiagnosticSummary;

            // 自动选中第一个
            if (DiagnosticSourceNames.Count > 0)
            {
                SelectedDiagnosticSource = DiagnosticSourceNames[0];
            }
        }
        catch (Exception ex)
        {
            DiagnosticSummary = $"诊断异常：{ex.Message}";
            StatusMessage = $"诊断失败：{ex.Message}";
        }
        finally
        {
            IsDiagnosing = false;
        }
    }

    // ==================== Bookshelf & Reader ====================
    [RelayCommand]
    private void OpenBookshelfDirectory()
    {
        try
        {
            var fullPath = Path.GetFullPath(DownloadPath);
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = fullPath,
                UseShellExecute = true,
            });
            StatusMessage = $"已打开目录：{fullPath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"打开目录失败：{ex.Message}";
        }
    }

    private async Task LoadBookshelfAsync()
    {
        try
        {
            var books = await _bookshelfUseCase.GetAllAsync();
            BookshelfItems.Clear();
            foreach (var book in books)
            {
                BookshelfItems.Add(book);
            }
        }
        catch
        {
            // Silently ignore if bookshelf file doesn't exist yet
        }

        // 同时加载 DB 书架
        await RefreshDbBooksAsync();
    }

    private async Task RefreshDbBooksAsync()
    {
        try
        {
            var dbBooks = await _bookRepo.GetAllBooksAsync();
            // 按上次阅读时间降序；未读过的排在后面、按创建时间降序
            var sorted = dbBooks
                .OrderByDescending(b =>
                    !string.IsNullOrWhiteSpace(b.ReadAt) && DateTimeOffset.TryParse(b.ReadAt, out var rdt)
                        ? rdt : DateTimeOffset.MinValue)
                .ThenByDescending(b =>
                    DateTimeOffset.TryParse(b.CreatedAt, out var cdt) ? cdt : DateTimeOffset.MinValue)
                .ToList();
            DbBooks.Clear();
            foreach (var b in sorted)
            {
                b.IsDownloading = DownloadTasks.Any(t =>
                    t.BookId == b.Id &&
                    t.CurrentStatus is DownloadTaskStatus.Queued or DownloadTaskStatus.Downloading);
                DbBooks.Add(b);
            }

            _bookshelfDirty = false;
            _lastBookshelfRefreshAt = DateTimeOffset.UtcNow;
        }
        catch
        {
            // DB not ready yet
        }
    }

    private async Task RefreshDbBooksIfNeededAsync(bool force = false)
    {
        var tooSoon = DateTimeOffset.UtcNow - _lastBookshelfRefreshAt < TimeSpan.FromSeconds(1);
        if (!force && !_bookshelfDirty && tooSoon)
        {
            return;
        }

        await RefreshDbBooksAsync();
    }

    private void MarkBookshelfDirty() => _bookshelfDirty = true;

    private void ReplaceDbBookInList(BookEntity refreshed)
    {
        var idx = DbBooks.IndexOf(DbBooks.FirstOrDefault(b => b.Id == refreshed.Id) ?? refreshed);
        if (idx >= 0)
        {
            refreshed.IsDownloading = DbBooks[idx].IsDownloading;
            DbBooks[idx] = refreshed;
        }
    }

    private async Task AddToBookshelfAsync(DownloadTask task)
    {
        try
        {
            var existing = BookshelfItems.FirstOrDefault(b =>
                string.Equals(b.Title, task.BookTitle, StringComparison.OrdinalIgnoreCase)
                && string.Equals(b.Author, task.Author, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                existing.FilePath = task.OutputFilePath;
                return;
            }

            var format = task.OutputFilePath.EndsWith(".epub", StringComparison.OrdinalIgnoreCase)
                ? "epub"
                : "txt";

            var book = new BookRecord
            {
                Id = Guid.NewGuid(),
                Title = task.BookTitle,
                Author = task.Author,
                SourceId = task.SourceSearchResult?.SourceId ?? 0,
                FilePath = task.OutputFilePath,
                Format = format,
                AddedAt = DateTimeOffset.Now,
            };

            await _bookshelfUseCase.AddAsync(book);
            BookshelfItems.Insert(0, book);
        }
        catch
        {
            // Non-critical, don't break download flow
        }
    }

    [RelayCommand]
    private async Task OpenBookAsync(BookRecord? book)
    {
        if (book is null || string.IsNullOrWhiteSpace(book.FilePath))
        {
            StatusMessage = "无法打开：文件路径为空。";
            return;
        }

        if (!File.Exists(book.FilePath))
        {
            StatusMessage = $"文件不存在：{book.FilePath}";
            return;
        }

        try
        {
            ReaderTitle = $"《{book.Title}》- {book.Author}";
            ReaderChapters.Clear();

            if (book.Format == "txt")
            {
                var text = await File.ReadAllTextAsync(book.FilePath);
                var chapters = ParseTxtChapters(text);

                foreach (var ch in chapters)
                {
                    ReaderChapters.Add(ch.Title);
                }

                book.TotalChapters = chapters.Count;
                _currentBookChapters = chapters;

                if (chapters.Count > 0)
                {
                    var startIndex = Math.Clamp(book.Progress.CurrentChapterIndex, 0, chapters.Count - 1);
                    ReaderCurrentChapterIndex = startIndex;
                    ReaderContent = chapters[startIndex].Content;
                    SelectedReaderChapter = ReaderChapters[startIndex];
                }
                else
                {
                    ReaderContent = text;
                }
            }
            else
            {
                ReaderContent = $"（{book.Format.ToUpperInvariant()} 阅读器即将支持）";
            }

            SelectedBookshelfItem = book;
            StatusMessage = $"已打开：《{book.Title}》，共 {ReaderChapters.Count} 章";
        }
        catch (Exception ex)
        {
            StatusMessage = $"打开失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RemoveBookAsync(BookRecord? book)
    {
        if (book is null)
        {
            return;
        }

        try
        {
            await _bookshelfUseCase.RemoveAsync(book.Id);
            BookshelfItems.Remove(book);
            StatusMessage = $"已从书架移除：《{book.Title}》";
        }
        catch (Exception ex)
        {
            StatusMessage = $"移除失败：{ex.Message}";
        }
    }

    // ==================== DB 书架命令 ====================

    /// <summary>当前选中的 DB 书籍。</summary>
    [ObservableProperty]
    private BookEntity? selectedDbBook;

    /// <summary>从 DB 打开并阅读书籍（支持部分下载状态）。</summary>
    [RelayCommand]
    private async Task OpenDbBookAsync(BookEntity? book)
    {
        if (book is null) return;

        try
        {
            ReaderTitle = $"《{book.Title}》- {book.Author}";
            await LoadDbBookChaptersAsync(book);

            SelectedDbBook = book;
            SelectedBookshelfItem = null;

            var doneCount = _currentBookChapters.Count(c => !c.Content.StartsWith("（"));
            StatusMessage = $"已打开：《{book.Title}》，{doneCount}/{book.TotalChapters} 章可读";

            // 刷新换源候选列表
            RefreshSortedSwitchSources();

            // 首次从书架打开后显示阅读 tab
            IsReaderTabVisible = true;

            // 自动跳转到阅读 tab
            SelectedTabIndex = 4;
        }
        catch (Exception ex)
        {
            StatusMessage = $"打开失败：{ex.Message}";
        }
    }

    /// <summary>刷新当前阅读的 DB 书籍章节列表（边下载边读时使用）。</summary>
    [RelayCommand]
    private async Task RefreshReaderAsync()
    {
        if (SelectedDbBook is null)
        {
            StatusMessage = "当前未打开 DB 书籍，无法刷新。";
            return;
        }

        // 刷新 BookEntity 信息
        var freshBook = await _bookRepo.GetBookAsync(SelectedDbBook.Id);
        if (freshBook is not null)
        {
            SelectedDbBook.DoneChapters = freshBook.DoneChapters;
            SelectedDbBook.TotalChapters = freshBook.TotalChapters;
        }

        var savedIndex = ReaderCurrentChapterIndex;
        await LoadDbBookChaptersAsync(SelectedDbBook);

        // 尽量保持在原来的章节位置
        if (savedIndex >= 0 && savedIndex < _currentBookChapters.Count)
        {
            ReaderCurrentChapterIndex = savedIndex;
            ReaderContent = _currentBookChapters[savedIndex].Content;
            SelectedReaderChapter = ReaderChapters[savedIndex];
        }

        var doneCount = _currentBookChapters.Count(c => !c.Content.StartsWith("（"));
        StatusMessage = $"已刷新：《{SelectedDbBook.Title}》，{doneCount}/{SelectedDbBook.TotalChapters} 章可读";
    }

    /// <summary>内部方法：从 DB 加载章节列表到阅读器。</summary>
    private async Task LoadDbBookChaptersAsync(BookEntity book)
    {
        ReaderChapters.Clear();
        _currentBookChapters.Clear();

        var chapters = await _bookRepo.GetChaptersAsync(book.Id);
        foreach (var ch in chapters)
        {
            var statusTag = ch.Status switch
            {
                ChapterStatus.Done => "",
                ChapterStatus.Failed => "❌ ",
                ChapterStatus.Downloading => "⏳ ",
                _ => "⬜ ",
            };
            ReaderChapters.Add($"{statusTag}{ch.Title}");

            var displayContent = ch.Status switch
            {
                ChapterStatus.Done => ch.Content ?? string.Empty,
                ChapterStatus.Failed => $"（下载失败：{ch.Error}\n\n点击上方「刷新章节」可在重新下载后查看）",
                ChapterStatus.Downloading => "（正在下载中…）",
                _ => "（等待下载）",
            };
            _currentBookChapters.Add((ch.Title, displayContent));
        }

        if (chapters.Count > 0)
        {
            // 优先定位到上次阅读位置，如果该章未下载则找最近的已下载章
            var preferred = Math.Clamp(book.ReadChapterIndex, 0, chapters.Count - 1);
            var startIndex = preferred;
            if (chapters[preferred].Status != ChapterStatus.Done)
            {
                var afterDone = chapters.Skip(preferred).FirstOrDefault(c => c.Status == ChapterStatus.Done);
                if (afterDone is not null)
                {
                    startIndex = afterDone.IndexNo;
                }
                else
                {
                    var beforeDone = chapters.Take(preferred).LastOrDefault(c => c.Status == ChapterStatus.Done);
                    startIndex = beforeDone?.IndexNo ?? preferred;
                }
            }

            ReaderCurrentChapterIndex = startIndex;
            ReaderContent = _currentBookChapters[startIndex].Content;
            SelectedReaderChapter = ReaderChapters[startIndex];
        }
        else
        {
            ReaderContent = "（章节目录尚未加载，请等待下载开始或点击「续传」）";
        }
    }

    /// <summary>继续下载：重新下载 Pending/Failed 章节。</summary>
    [RelayCommand]
    private Task ResumeBookDownloadAsync(BookEntity? book)
    {
        if (book is null) return Task.CompletedTask;
        if (book.IsComplete)
        {
            StatusMessage = $"《{book.Title}》已全部下载完成，无需续传。";
            return Task.CompletedTask;
        }

        // 构造一个虚拟 SearchResult 用于恢复下载
        var searchResult = new SearchResult(
            Id: Guid.NewGuid(),
            Title: book.Title,
            Author: book.Author,
            SourceId: book.SourceId,
            SourceName: string.Empty,
            Url: book.TocUrl,
            LatestChapter: string.Empty,
            UpdatedAt: DateTimeOffset.Now
        );

        var task = new DownloadTask
        {
            Id = Guid.NewGuid(),
            BookTitle = book.Title,
            Author = book.Author,
            Mode = DownloadMode.FullBook,
            EnqueuedAt = DateTimeOffset.Now,
            SourceSearchResult = searchResult,
        };

        DownloadTasks.Insert(0, task);
        ApplyTaskFilter();
        StatusMessage = $"继续下载：《{book.Title}》（剩余 {book.TotalChapters - book.DoneChapters} 章）";

        _ = RunDownloadInBackgroundAsync(task, searchResult);
        return Task.CompletedTask;
    }

    /// <summary>从 DB 导出书籍为 TXT 文件。</summary>
    [RelayCommand]
    private async Task ExportDbBookAsync(BookEntity? book)
    {
        if (book is null) return;

        try
        {
            var doneContents = await _bookRepo.GetDoneChapterContentsAsync(book.Id);
            if (doneContents.Count == 0)
            {
                StatusMessage = $"《{book.Title}》尚无已下载章节，无法导出。";
                return;
            }

            var settings = await _appSettingsUseCase.LoadAsync();
            var downloadPath = settings.DownloadPath;
            if (!Path.IsPathRooted(downloadPath))
            {
                downloadPath = Path.Combine(AppContext.BaseDirectory, downloadPath);
            }
            Directory.CreateDirectory(downloadPath);

            var safeName = string.Join("_", $"{book.Title}({book.Author}).txt".Split(Path.GetInvalidFileNameChars()));
            var outputPath = Path.Combine(downloadPath, safeName);

            await using var writer = new StreamWriter(outputPath, false, new UTF8Encoding(false));
            await writer.WriteLineAsync($"书名：{book.Title}");
            await writer.WriteLineAsync($"作者：{book.Author}");
            await writer.WriteLineAsync($"已下载：{doneContents.Count}/{book.TotalChapters} 章");
            await writer.WriteLineAsync();

            foreach (var (title, content) in doneContents)
            {
                await writer.WriteLineAsync(title);
                await writer.WriteLineAsync();
                await writer.WriteLineAsync(content);
                await writer.WriteLineAsync();
            }

            StatusMessage = $"导出完成：{outputPath}（{doneContents.Count} 章）";
        }
        catch (Exception ex)
        {
            StatusMessage = $"导出失败：{ex.Message}";
        }
    }

    /// <summary>从 DB 移除书籍（含所有章节数据）。</summary>
    [RelayCommand]
    private async Task RemoveDbBookAsync(BookEntity? book)
    {
        if (book is null) return;

        try
        {
            await _bookRepo.DeleteBookAsync(book.Id);
            DbBooks.Remove(book);
            StatusMessage = $"已从书架移除：《{book.Title}》";
        }
        catch (Exception ex)
        {
            StatusMessage = $"移除失败：{ex.Message}";
        }
    }

    /// <summary>刷新封面（诊断 + 重新抓取）。</summary>
    [RelayCommand]
    private async Task RefreshCoverAsync(BookEntity? book)
    {
        if (book is null) return;

        try
        {
            StatusMessage = $"正在刷新封面：《{book.Title}》…";
            var diagnosticInfo = await _downloadBookUseCase.RefreshCoverAsync(book);

            var refreshed = await _bookRepo.GetBookAsync(book.Id);
            if (refreshed is not null)
            {
                if (SelectedDbBook?.Id == refreshed.Id)
                {
                    SelectedDbBook = refreshed;
                }
                ReplaceDbBookInList(refreshed);
            }

            // 刷新书架列表以显示新封面
            await RefreshDbBooksAsync();

            StatusMessage = diagnosticInfo.Replace("\r\n", "  ").Replace("\n", "  ").TrimEnd();
        }
        catch (Exception ex)
        {
            StatusMessage = $"刷新封面失败：{ex.Message}";
        }
    }

    /// <summary>检查单本书是否有新章节，有则自动续传。</summary>
    [RelayCommand]
    private async Task CheckNewChaptersAsync(BookEntity? book)
    {
        if (book is null) return;

        try
        {
            StatusMessage = $"正在检查新章节：《{book.Title}》…";
            var newCount = await _downloadBookUseCase.CheckNewChaptersAsync(book);
            if (newCount > 0)
            {
                StatusMessage = $"《{book.Title}》发现 {newCount} 个新章节，正在自动续传…";
                await RefreshDbBooksAsync();
                await ResumeBookDownloadAsync(book);
            }
            else
            {
                StatusMessage = $"《{book.Title}》目录无更新。";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"检查新章节失败：{ex.Message}";
        }
    }

    /// <summary>检查全部书架书籍的新章节。</summary>
    [RelayCommand]
    private async Task CheckAllNewChaptersAsync()
    {
        StatusMessage = "正在检查所有书籍的新章节…";
        var totalNew = 0;
        foreach (var book in DbBooks.ToList())
        {
            try
            {
                var newCount = await _downloadBookUseCase.CheckNewChaptersAsync(book);
                if (newCount > 0)
                {
                    totalNew += newCount;
                    await ResumeBookDownloadAsync(book);
                }
            }
            catch { /* skip failed */ }
        }

        await RefreshDbBooksAsync();
        StatusMessage = totalNew > 0
            ? $"全部检查完成，共发现 {totalNew} 个新章节，已自动续传。"
            : "全部检查完成，没有发现新章节。";
    }

    /// <summary>在默认浏览器中打开当前书籍的原始网页。</summary>
    [RelayCommand]
    private void OpenBookWebPage()
    {
        var url = SelectedDbBook?.TocUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            StatusMessage = "当前书籍没有关联的网页地址。";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
            StatusMessage = $"已打开网页：{url}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"打开网页失败：{ex.Message}";
        }
    }

    /// <summary>打开封面候选选择面板并抓取当前书籍的图片候选。</summary>
    [RelayCommand]
    private async Task OpenCoverPickerAsync()
    {
        if (SelectedDbBook is null)
        {
            StatusMessage = "请先在书架中打开一本书，再选择封面。";
            return;
        }

        try
        {
            IsLoadingCoverCandidates = true;
            IsCoverPickerVisible = true;
            CoverCandidates.Clear();
            SelectedCoverCandidate = null;

            var candidates = await _downloadBookUseCase.GetCoverCandidatesAsync(SelectedDbBook);
            foreach (var candidate in candidates)
            {
                CoverCandidates.Add(candidate);
            }

            StatusMessage = CoverCandidates.Count > 0
                ? $"已获取 {CoverCandidates.Count} 个封面候选，请选择。"
                : "未找到候选图片，可尝试点击原始网页确认页面结构。";
        }
        catch (Exception ex)
        {
            IsCoverPickerVisible = false;
            StatusMessage = $"获取封面候选失败：{ex.Message}";
        }
        finally
        {
            IsLoadingCoverCandidates = false;
        }
    }

    /// <summary>应用选中的封面候选。</summary>
    [RelayCommand]
    private async Task ApplySelectedCoverAsync(CoverCandidate? candidate)
    {
        if (SelectedDbBook is null)
        {
            StatusMessage = "当前没有打开的书籍。";
            return;
        }

        candidate ??= SelectedCoverCandidate;
        if (candidate is null)
        {
            StatusMessage = "请先选择一张封面图。";
            return;
        }

        try
        {
            var result = await _downloadBookUseCase.ApplyCoverCandidateAsync(SelectedDbBook, candidate);

            // 立即更新当前对象，确保占位符即时切换
            SelectedDbBook.CoverUrl = candidate.ImageUrl;
            var refreshed = await _bookRepo.GetBookAsync(SelectedDbBook.Id);
            if (refreshed is not null)
            {
                SelectedDbBook = refreshed;
                ReplaceDbBookInList(refreshed);
            }

            await RefreshDbBooksAsync();
            StatusMessage = result;
        }
        catch (Exception ex)
        {
            StatusMessage = $"设置封面失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private void CloseCoverPicker()
    {
        IsCoverPickerVisible = false;
        CoverCandidates.Clear();
        SelectedCoverCandidate = null;
    }

    // ==================== 换源 ====================

    /// <summary>ComboBox 选中书源后自动触发换源。</summary>
    async partial void OnSelectedSwitchSourceChanged(SourceItem? value)
    {
        if (value is null || value.Id <= 0) return;
        if (IsSourceSwitching) return;

        if (SelectedDbBook is null)
        {
            StatusMessage = "当前未打开任何书籍。";
            return;
        }

        if (ReaderCurrentChapterIndex < 0 || ReaderCurrentChapterIndex >= _currentBookChapters.Count)
        {
            StatusMessage = "当前没有正在阅读的章节。";
            return;
        }

        var chapterTitle = _currentBookChapters[ReaderCurrentChapterIndex].Title;
        IsSourceSwitching = true;
        StatusMessage = $"换源中：正在从 {value.Name} 获取「{chapterTitle}」…";

        try
        {
            var (success, content, message) = await _downloadBookUseCase.FetchChapterFromSourceAsync(
                SelectedDbBook, chapterTitle, value.Id);

            if (success && !string.IsNullOrWhiteSpace(content))
            {
                ReaderContent = content;
                _currentBookChapters[ReaderCurrentChapterIndex] = (chapterTitle, content);

                await _bookRepo.UpdateChapterAsync(
                    SelectedDbBook.Id, ReaderCurrentChapterIndex,
                    ChapterStatus.Done, content, null);

                ReaderScrollVersion++;
                StatusMessage = message;
            }
            else
            {
                // 换源失败：保留当前章节内容不动，只提示
                StatusMessage = $"换源未成功，已保留原文。{message}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"换源失败，已保留原文。{ex.Message}";
        }
        finally
        {
            IsSourceSwitching = false;
        }
    }

    /// <summary>切换目录浮层显示。</summary>
    [RelayCommand]
    private void ToggleTocOverlay()
    {
        IsTocOverlayVisible = !IsTocOverlayVisible;
    }

    /// <summary>从目录浮层选择章节。</summary>
    [RelayCommand]
    private async Task SelectTocChapterAsync(int index)
    {
        if (index < 0 || index >= _currentBookChapters.Count) return;
        await NavigateToChapterAsync(index);
        SelectedReaderChapter = ReaderChapters.Count > index ? ReaderChapters[index] : null;
    }

    /// <summary>双击搜索结果直接加入下载队列。</summary>
    public void QueueDownloadFromSearchResult(SearchResult result)
    {
        SelectedSearchResult = result;
        if (QueueDownloadCommand.CanExecute(null))
        {
            QueueDownloadCommand.Execute(null);
        }
    }

    /// <summary>双击书架打开书并跳转阅读 tab。</summary>
    public async Task OpenDbBookAndSwitchToReaderAsync(BookEntity book)
    {
        IsReaderTabVisible = true;
        await OpenDbBookAsync(book);
        SelectedTabIndex = 4; // 阅读 tab index
    }

    private List<(string Title, string Content)> _currentBookChapters = [];

    private async Task NavigateToChapterAsync(int index)
    {
        if (index < 0 || index >= _currentBookChapters.Count)
        {
            return;
        }

        ReaderCurrentChapterIndex = index;
        ReaderContent = _currentBookChapters[index].Content;

        // 通知 View 滚动到顶部
        ReaderScrollVersion++;

        // 关闭目录浮层
        IsTocOverlayVisible = false;

        // 保存阅读进度到 DB（如果是 DB 书籍）
        if (SelectedDbBook is not null)
        {
            try
            {
                await _bookRepo.UpdateReadProgressAsync(
                    SelectedDbBook.Id, index, _currentBookChapters[index].Title);
                SelectedDbBook.ReadChapterIndex = index;
                SelectedDbBook.ReadChapterTitle = _currentBookChapters[index].Title;
                MarkBookshelfDirty();
            }
            catch
            {
                // Non-critical
            }
        }
        else if (SelectedBookshelfItem is not null)
        {
            var progress = new ReadingProgress
            {
                CurrentChapterIndex = index,
                CurrentChapterTitle = _currentBookChapters[index].Title,
                LastReadAt = DateTimeOffset.Now,
            };
            SelectedBookshelfItem.Progress = progress;

            try
            {
                await _bookshelfUseCase.UpdateProgressAsync(SelectedBookshelfItem.Id, progress);
            }
            catch
            {
                // Non-critical
            }
        }
    }

    [RelayCommand]
    private async Task PreviousChapterAsync()
    {
        if (ReaderCurrentChapterIndex > 0)
        {
            await NavigateToChapterAsync(ReaderCurrentChapterIndex - 1);
            SelectedReaderChapter = ReaderChapters.Count > ReaderCurrentChapterIndex
                ? ReaderChapters[ReaderCurrentChapterIndex]
                : null;
        }
    }

    [RelayCommand]
    private async Task NextChapterAsync()
    {
        if (ReaderCurrentChapterIndex < _currentBookChapters.Count - 1)
        {
            await NavigateToChapterAsync(ReaderCurrentChapterIndex + 1);
            SelectedReaderChapter = ReaderChapters.Count > ReaderCurrentChapterIndex
                ? ReaderChapters[ReaderCurrentChapterIndex]
                : null;
        }
    }

    private static List<(string Title, string Content)> ParseTxtChapters(string text)
    {
        var chapters = new List<(string Title, string Content)>();
        var lines = text.Split('\n');
        string? currentTitle = null;
        var currentContent = new System.Text.StringBuilder();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (IsChapterTitle(trimmed))
            {
                if (currentTitle is not null)
                {
                    chapters.Add((currentTitle, currentContent.ToString().Trim()));
                }

                currentTitle = trimmed;
                currentContent.Clear();
            }
            else
            {
                currentContent.AppendLine(line);
            }
        }

        if (currentTitle is not null)
        {
            chapters.Add((currentTitle, currentContent.ToString().Trim()));
        }

        return chapters;
    }

    private const int MaxChapterTitleLength = 50;

    private static readonly System.Text.RegularExpressions.Regex ChineseChapterRegex =
        new(@"^第[一二三四五六七八九十百千\d]+[章节回]", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex EnglishChapterRegex =
        new(@"^Chapter\s+\d+", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static bool IsChapterTitle(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length > MaxChapterTitleLength)
        {
            return false;
        }

        return ChineseChapterRegex.IsMatch(line) || EnglishChapterRegex.IsMatch(line);
    }

    // ==================== Settings ====================
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
                Title = "选择下载目录",
                AllowMultiple = false,
            };

            var result = await window.StorageProvider.OpenFolderPickerAsync(dialog);
            if (result.Count > 0)
            {
                DownloadPath = result[0].Path.LocalPath;
                StatusMessage = $"下载目录已更改为：{DownloadPath}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"选择目录失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        await SaveSettingsCoreAsync(showStatus: true);
    }

    private async Task SaveSettingsCoreAsync(bool showStatus, CancellationToken cancellationToken = default)
    {
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

            ReaderFontSize = ReaderFontSize,
            ReaderFontName = SelectedFontName,
            ReaderLineHeight = ReaderLineHeight,
            ReaderParagraphSpacing = ReaderParagraphSpacing,
            ReaderBackground = ReaderBackground,
            ReaderForeground = ReaderForeground,
            ReaderDarkMode = IsDarkMode,
        };

        await _appSettingsUseCase.SaveAsync(settings, cancellationToken);
        if (showStatus)
        {
            StatusMessage = "设置已保存到本地用户配置文件。";
        }
    }

    private void QueueAutoSaveSettings()
    {
        if (_isLoadingSettings)
        {
            return;
        }

        _settingsAutoSaveCts?.Cancel();
        _settingsAutoSaveCts = new CancellationTokenSource();
        var cts = _settingsAutoSaveCts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, cts.Token);
                await SaveSettingsCoreAsync(showStatus: false, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // 防抖取消
            }
            catch
            {
                // 自动保存失败不影响主流程
            }
        });
    }

    private async Task LoadSettingsAsync()
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

            ReaderFontSize = settings.ReaderFontSize;
            SelectedFontName = string.IsNullOrWhiteSpace(settings.ReaderFontName) ? "默认" : settings.ReaderFontName;
            ReaderLineHeight = settings.ReaderLineHeight;
            ReaderParagraphSpacing = settings.ReaderParagraphSpacing;
            IsDarkMode = settings.ReaderDarkMode;
            ReaderBackground = settings.ReaderBackground;
            ReaderForeground = settings.ReaderForeground;
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

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
        {
            Sources.Add(new SourceItem(rule));
        }

        SelectedSourceId = rules.FirstOrDefault()?.Id ?? 0;
        AvailableSourceCount = rules.Count;
        StatusMessage = $"就绪：已加载 {AvailableSourceCount} 条书源规则，可切换测试。";

        // 启动后台健康检测
        _ = RefreshSourceHealthAsync();
    }

    [RelayCommand]
    private async Task RefreshSourceHealthAsync()
    {
        if (IsCheckingHealth) return;
        IsCheckingHealth = true;
        try
        {
            var rules = Sources
                .Where(s => s.Id > 0)
                .Select(s => new BookSourceRule { Id = s.Id, Name = s.Name, Url = s.Url, SearchSupported = s.SearchSupported })
                .ToList();

            var results = await _healthCheckUseCase.CheckAllAsync(rules);

            var lookup = results.ToDictionary(r => r.SourceId, r => r.IsReachable);
            foreach (var source in Sources)
            {
                if (lookup.TryGetValue(source.Id, out var healthy))
                {
                    source.IsHealthy = healthy;
                }
            }

            var ok = results.Count(r => r.IsReachable);
            StatusMessage = $"书源健康检测完成：{ok}/{results.Count} 可达";

            // 同步更新换源下拉列表排序
            RefreshSortedSwitchSources();

            // 同步更新规则处理页的健康状态显示
            SyncRuleEditorRuleHealthFromSources();
        }
        catch (Exception ex)
        {
            StatusMessage = $"书源健康检测失败：{ex.Message}";
        }
        finally
        {
            IsCheckingHealth = false;
        }
    }

    // ==================== 规则编辑器 ====================

    private static readonly JsonSerializerOptions s_jsonWrite = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions s_jsonRead = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>确保规则的所有子区段不为 null，以便 AXAML 双向绑定。</summary>
    private static void EnsureSubSections(FullBookSourceRule rule)
    {
        rule.Search ??= new RuleSearchSection();
        rule.Book ??= new RuleBookSection();
        rule.Toc ??= new RuleTocSection();
        rule.Chapter ??= new RuleChapterSection();
    }

    /// <summary>所有规则列表。</summary>
    public ObservableCollection<RuleListItem> RuleEditorRules { get; } = [];

    /// <summary>当前正在编辑的规则对象，AXAML 表单直接绑定到其各属性。</summary>
    [ObservableProperty]
    private FullBookSourceRule? currentRule;

    /// <summary>当前选中的规则 ID。</summary>
    [ObservableProperty]
    private RuleListItem? ruleEditorSelectedRule;

    /// <summary>规则测试关键字。</summary>
    [ObservableProperty]
    private string ruleTestKeyword = "诡秘之主";

    /// <summary>测试搜索结果预览。</summary>
    [ObservableProperty]
    private string ruleTestSearchPreview = string.Empty;

    /// <summary>测试目录预览。</summary>
    [ObservableProperty]
    private string ruleTestTocPreview = string.Empty;

    /// <summary>测试正文预览。</summary>
    [ObservableProperty]
    private string ruleTestContentPreview = string.Empty;

    /// <summary>测试状态信息。</summary>
    [ObservableProperty]
    private string ruleTestStatus = string.Empty;

    /// <summary>规则测试中。</summary>
    [ObservableProperty]
    private bool isRuleTesting;

    /// <summary>规则保存中。</summary>
    [ObservableProperty]
    private bool isRuleSaving;

    /// <summary>当前规则是否有用户覆盖（已被修改过）。</summary>
    [ObservableProperty]
    private bool ruleHasUserOverride;

    /// <summary>规则编辑器当前子页签：0=配置, 1=预览。</summary>
    [ObservableProperty]
    private int ruleEditorSubTab;

    /// <summary>测试诊断日志。</summary>
    [ObservableProperty]
    private string ruleTestDiagnostics = string.Empty;

    /// <summary>加载所有规则到列表。</summary>
    [RelayCommand]
    private async Task LoadRuleListAsync()
    {
        try
        {
            var rules = await _ruleEditorUseCase.LoadAllAsync();
            var healthLookup = Sources
                .Where(s => s.Id > 0)
                .ToDictionary(s => s.Id, s => s.IsHealthy);

            RuleEditorRules.Clear();
            foreach (var r in rules)
            {
                healthLookup.TryGetValue(r.Id, out var healthy);
                RuleEditorRules.Add(new RuleListItem(r.Id, r.Name, r.Url, r.Search is not null, healthy));
            }
            StatusMessage = $"规则列表已加载：共 {rules.Count} 条";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载规则失败：{ex.Message}";
        }
    }

    /// <summary>根据首页书源健康状态，同步规则处理页左侧列表状态。</summary>
    private void SyncRuleEditorRuleHealthFromSources()
    {
        if (RuleEditorRules.Count == 0) return;

        var lookup = Sources
            .Where(s => s.Id > 0)
            .ToDictionary(s => s.Id, s => s.IsHealthy);

        foreach (var item in RuleEditorRules)
        {
            item.IsHealthy = lookup.TryGetValue(item.Id, out var healthy)
                ? healthy
                : null;
        }
    }

    /// <summary>选中规则后加载其对象到编辑器表单。</summary>
    async partial void OnRuleEditorSelectedRuleChanged(RuleListItem? value)
    {
        if (value is null) { CurrentRule = null; RuleHasUserOverride = false; return; }
        try
        {
            var rule = await _ruleEditorUseCase.LoadAsync(value.Id);
            if (rule is not null)
            {
                EnsureSubSections(rule);
                CurrentRule = rule;
                RuleHasUserOverride = _ruleEditorUseCase.HasUserOverride(value.Id);
            }
            else
            {
                StatusMessage = $"未找到 rule-{value.Id}.json";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载规则失败：{ex.Message}";
        }
    }

    /// <summary>保存当前编辑的规则。</summary>
    [RelayCommand]
    private async Task SaveRuleAsync()
    {
        if (CurrentRule is null)
        {
            StatusMessage = "没有正在编辑的规则。";
            return;
        }

        if (CurrentRule.Id <= 0)
        {
            StatusMessage = "规则 ID 必须为正整数。";
            return;
        }

        IsRuleSaving = true;
        try
        {
            await _ruleEditorUseCase.SaveAsync(CurrentRule);
            RuleHasUserOverride = _ruleEditorUseCase.HasUserOverride(CurrentRule.Id);
            StatusMessage = $"规则 {CurrentRule.Id}（{CurrentRule.Name}）已保存。";
            await LoadRuleListAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存规则失败：{ex.Message}";
        }
        finally
        {
            IsRuleSaving = false;
        }
    }

    /// <summary>恢复当前规则为内置默认值。</summary>
    [RelayCommand]
    private async Task ResetRuleToDefaultAsync()
    {
        if (CurrentRule is null)
        {
            StatusMessage = "没有正在编辑的规则。";
            return;
        }

        var ruleId = CurrentRule.Id;
        try
        {
            var ok = await _ruleEditorUseCase.ResetToDefaultAsync(ruleId);
            if (!ok)
            {
                StatusMessage = $"规则 {ruleId} 没有用户覆盖或没有内置默认值，无需恢复。";
                return;
            }

            // 重新加载默认版本
            var defaultRule = await _ruleEditorUseCase.LoadAsync(ruleId);
            if (defaultRule is not null)
            {
                EnsureSubSections(defaultRule);
                CurrentRule = defaultRule;
            }

            RuleHasUserOverride = false;
            StatusMessage = $"规则 {ruleId} 已恢复为内置默认值。";
            await LoadRuleListAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"恢复默认值失败：{ex.Message}";
        }
    }

    /// <summary>新建一条空规则。</summary>
    [RelayCommand]
    private async Task NewRuleAsync()
    {
        try
        {
            var nextId = await _ruleEditorUseCase.GetNextAvailableIdAsync();
            var template = new FullBookSourceRule
            {
                Id = nextId,
                Name = $"新书源-{nextId}",
                Url = "https://",
                Type = "html",
                Language = "zh_CN",
                Search = new RuleSearchSection(),
                Book = new RuleBookSection(),
                Toc = new RuleTocSection(),
                Chapter = new RuleChapterSection(),
            };
            CurrentRule = template;
            StatusMessage = $"已创建新规则模板，ID={nextId}。编辑后请点击【保存】。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"创建规则失败：{ex.Message}";
        }
    }

    /// <summary>复制当前选中的规则为新规则。</summary>
    [RelayCommand]
    private async Task CopyRuleAsync()
    {
        if (CurrentRule is null)
        {
            StatusMessage = "请先选中一条规则再复制。";
            return;
        }

        try
        {
            // 深复制：序列化再反序列化
            var json = JsonSerializer.Serialize(CurrentRule, s_jsonWrite);
            var copy = JsonSerializer.Deserialize<FullBookSourceRule>(json, s_jsonRead);
            if (copy is null)
            {
                StatusMessage = "复制失败。";
                return;
            }

            var nextId = await _ruleEditorUseCase.GetNextAvailableIdAsync();
            copy.Id = nextId;
            copy.Name = $"{copy.Name}（副本）";
            EnsureSubSections(copy);
            CurrentRule = copy;
            StatusMessage = $"已复制为新规则 ID={nextId}，编辑后请点击【保存】。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"复制规则失败：{ex.Message}";
        }
    }

    /// <summary>删除当前选中的规则。</summary>
    [RelayCommand]
    private async Task DeleteRuleAsync()
    {
        if (RuleEditorSelectedRule is null)
        {
            StatusMessage = "请先选中一条规则。";
            return;
        }

        try
        {
            var deleted = await _ruleEditorUseCase.DeleteAsync(RuleEditorSelectedRule.Id);
            if (deleted)
            {
                StatusMessage = $"规则 {RuleEditorSelectedRule.Id}（{RuleEditorSelectedRule.Name}）已删除。";
                CurrentRule = null;
                RuleEditorSelectedRule = null;
                await LoadRuleListAsync();
            }
            else
            {
                StatusMessage = $"规则 {RuleEditorSelectedRule.Id} 未找到文件。";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"删除规则失败：{ex.Message}";
        }
    }

    /// <summary>运行完整测试：搜索 → 目录 → 第一章。</summary>
    [RelayCommand]
    private async Task TestRuleAsync()
    {
        if (CurrentRule is null)
        {
            StatusMessage = "请先加载或编辑一条规则。";
            return;
        }

        var rule = CurrentRule;

        IsRuleTesting = true;
        RuleTestSearchPreview = string.Empty;
        RuleTestTocPreview = string.Empty;
        RuleTestContentPreview = string.Empty;
        RuleTestDiagnostics = string.Empty;
        RuleTestStatus = "测试中… 第 1/3 步：搜索";

        var diagAll = new StringBuilder();

        try
        {
            // 1. 搜索
            var keyword = string.IsNullOrWhiteSpace(RuleTestKeyword) ? "诡秘之主" : RuleTestKeyword;
            var searchResult = await _ruleEditorUseCase.TestSearchAsync(rule, keyword);
            RuleTestSearchPreview = searchResult.Success
                ? string.Join("\n", searchResult.SearchItems.Take(20))
                : searchResult.Message;
            diagAll.AppendLine("=== 搜索 ===");
            foreach (var line in searchResult.DiagnosticLines) diagAll.AppendLine(line);
            diagAll.AppendLine(searchResult.Message);

            if (!searchResult.Success || searchResult.SearchItems.Count == 0)
            {
                RuleTestStatus = $"搜索未返回结果，测试终止。({searchResult.ElapsedMs}ms)";
                RuleTestDiagnostics = diagAll.ToString();
                // 自动切到预览子页
                RuleEditorSubTab = 1;
                return;
            }

            // 从第一个搜索结果提取 URL
            var firstItem = searchResult.SearchItems[0];
            var urlMatch = System.Text.RegularExpressions.Regex.Match(firstItem, @"\[(.+)\]$");
            if (!urlMatch.Success)
            {
                RuleTestStatus = "无法从搜索结果中提取 URL";
                RuleTestDiagnostics = diagAll.ToString();
                RuleEditorSubTab = 1;
                return;
            }

            var bookUrl = urlMatch.Groups[1].Value;
            if (!bookUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(rule.Url))
            {
                if (Uri.TryCreate(rule.Url, UriKind.Absolute, out var baseUri)
                    && Uri.TryCreate(baseUri, bookUrl, out var abs))
                    bookUrl = abs.ToString();
            }

            // 2. 目录
            RuleTestStatus = "测试中… 第 2/3 步：目录";
            var tocResult = await _ruleEditorUseCase.TestTocAsync(rule, bookUrl);
            RuleTestTocPreview = tocResult.Success
                ? string.Join("\n", tocResult.TocItems.Take(30))
                : tocResult.Message;
            diagAll.AppendLine("\n=== 目录 ===");
            foreach (var line in tocResult.DiagnosticLines) diagAll.AppendLine(line);
            diagAll.AppendLine(tocResult.Message);

            if (!tocResult.Success || tocResult.TocItems.Count == 0)
            {
                RuleTestStatus = $"目录解析失败，测试终止。({tocResult.ElapsedMs}ms)";
                RuleTestDiagnostics = diagAll.ToString();
                RuleEditorSubTab = 1;
                return;
            }

            // 3. 第一章正文
            RuleTestStatus = "测试中… 第 3/3 步：正文";
            // ContentPreview 存储的是第一章 URL
            var chapterUrlFromToc = tocResult.ContentPreview;
            if (string.IsNullOrWhiteSpace(chapterUrlFromToc))
            {
                // 尝试从第一个 toc item 提取
                var tocUrlMatch = System.Text.RegularExpressions.Regex.Match(tocResult.TocItems[0], @"\[(.+)\]$");
                chapterUrlFromToc = tocUrlMatch.Success ? tocUrlMatch.Groups[1].Value : string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(chapterUrlFromToc))
            {
                var chapterResult = await _ruleEditorUseCase.TestChapterAsync(rule, chapterUrlFromToc);
                RuleTestContentPreview = chapterResult.Success
                    ? chapterResult.ContentPreview
                    : chapterResult.Message;
                diagAll.AppendLine("\n=== 正文 ===");
                foreach (var line in chapterResult.DiagnosticLines) diagAll.AppendLine(line);
                diagAll.AppendLine(chapterResult.Message);

                RuleTestStatus = chapterResult.Success
                    ? $"✅ 测试完成：搜索={searchResult.SearchItems.Count}条, 目录={tocResult.TocItems.Count}章, 正文={chapterResult.ContentPreview.Length}字"
                    : $"正文提取失败：{chapterResult.Message}";
            }
            else
            {
                RuleTestStatus = $"✅ 搜索+目录成功，但无法提取第一章 URL";
            }

            RuleTestDiagnostics = diagAll.ToString();
            // 自动切到预览子页
            RuleEditorSubTab = 1;
        }
        catch (Exception ex)
        {
            RuleTestStatus = $"测试异常：{ex.Message}";
            RuleTestDiagnostics = diagAll.ToString();
        }
        finally
        {
            IsRuleTesting = false;
        }
    }

    /// <summary>
    /// 运行一次完整调试，并将详细报告复制到剪贴板（便于提交给 AI 分析）。
    /// </summary>
    [RelayCommand]
    private async Task DebugRuleAsync()
    {
        if (CurrentRule is null)
        {
            StatusMessage = "请先加载或编辑一条规则。";
            return;
        }

        var rule = CurrentRule;
        IsRuleTesting = true;
        RuleTestDiagnostics = string.Empty;
        RuleTestStatus = "Debug 中… 第 1/3 步：搜索";

        var report = new StringBuilder();
        report.AppendLine("# ReadStorm 规则调试报告");
        report.AppendLine();
        report.AppendLine("> **生成时间**: " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
        report.AppendLine("> ");
        report.AppendLine($"> **规则 ID**: {rule.Id}");
        report.AppendLine("> ");
        report.AppendLine($"> **规则名称**: {rule.Name}");
        report.AppendLine("> ");
        report.AppendLine($"> **站点 URL**: {rule.Url}");
        report.AppendLine();
        report.AppendLine("---");
        report.AppendLine();
        report.AppendLine("## 1. 规则 JSON 定义");
        report.AppendLine();
        report.AppendLine("以下是当前正在调试的完整规则配置（JSON 格式）。请检查各字段是否与目标站点的实际页面结构匹配。");
        report.AppendLine();
        report.AppendLine("```json");
        report.AppendLine(JsonSerializer.Serialize(rule, s_jsonWrite));
        report.AppendLine("```");
        report.AppendLine();

        try
        {
            var keyword = string.IsNullOrWhiteSpace(RuleTestKeyword) ? "诡秘之主" : RuleTestKeyword;
            report.AppendLine("## 2. 测试参数");
            report.AppendLine();
            report.AppendLine($"- **搜索关键字**: `{keyword}`");
            report.AppendLine();
            report.AppendLine("---");
            report.AppendLine();

            // 1) 搜索
            var searchResult = await _ruleEditorUseCase.TestSearchAsync(rule, keyword);
            AppendDebugStep(report, 3, "搜索测试", "使用关键字在目标站点上执行搜索请求，验证搜索规则的 URL、选择器是否能正确提取书籍列表。", searchResult, searchResult.SearchItems);

            if (!searchResult.Success || searchResult.SearchItems.Count == 0)
            {
                RuleTestStatus = $"Debug 终止：搜索未返回结果（{searchResult.ElapsedMs}ms）";
                RuleTestDiagnostics = report.ToString();
                RuleEditorSubTab = 1;
                await CopyDebugReportToClipboardAsync(report.ToString());
                return;
            }

            var firstItem = searchResult.SearchItems[0];
            var bookUrl = ExtractBracketUrl(firstItem);
            if (!bookUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(rule.Url)
                && Uri.TryCreate(rule.Url, UriKind.Absolute, out var baseUri)
                && Uri.TryCreate(baseUri, bookUrl, out var abs))
            {
                bookUrl = abs.ToString();
            }

            report.AppendLine("---");
            report.AppendLine();
            report.AppendLine("## 4. 中间数据：首个书籍 URL");
            report.AppendLine();
            report.AppendLine("从搜索结果的第一项中提取的书籍详情页 URL，将作为下一步目录测试的入口。");
            report.AppendLine();
            report.AppendLine("```");
            report.AppendLine(bookUrl);
            report.AppendLine("```");
            report.AppendLine();

            // 2) 目录
            RuleTestStatus = "Debug 中… 第 2/3 步：目录";
            var tocResult = await _ruleEditorUseCase.TestTocAsync(rule, bookUrl);
            AppendDebugStep(report, 5, "目录测试", "访问书籍详情页，提取章节目录列表。验证目录选择器能否正确匹配章节标题和链接。", tocResult, tocResult.TocItems);

            if (!tocResult.Success || tocResult.TocItems.Count == 0)
            {
                RuleTestStatus = $"Debug 终止：目录为空（{tocResult.ElapsedMs}ms）";
                RuleTestDiagnostics = report.ToString();
                RuleEditorSubTab = 1;
                await CopyDebugReportToClipboardAsync(report.ToString());
                return;
            }

            var chapterUrl = tocResult.ContentPreview;
            if (string.IsNullOrWhiteSpace(chapterUrl))
            {
                chapterUrl = ExtractBracketUrl(tocResult.TocItems[0]);
            }

            report.AppendLine("---");
            report.AppendLine();
            report.AppendLine("## 6. 中间数据：首章 URL");
            report.AppendLine();
            report.AppendLine("从目录的第一个章节中提取的正文页 URL，将用于正文内容提取测试。");
            report.AppendLine();
            report.AppendLine("```");
            report.AppendLine(chapterUrl);
            report.AppendLine("```");
            report.AppendLine();

            // 3) 正文
            RuleTestStatus = "Debug 中… 第 3/3 步：正文";
            var chapterResult = await _ruleEditorUseCase.TestChapterAsync(rule, chapterUrl);
            AppendDebugStep(report, 7, "正文测试", "访问某一章的页面，提取正文内容。验证正文选择器能否正确获取章节文字。", chapterResult, []);

            RuleTestStatus = chapterResult.Success
                ? "✅ Debug 完成，详细报告已复制到剪贴板。"
                : "⚠️ Debug 完成（正文提取失败），详细报告已复制到剪贴板。";

            RuleTestSearchPreview = string.Join("\n", searchResult.SearchItems.Take(20));
            RuleTestTocPreview = string.Join("\n", tocResult.TocItems.Take(30));
            RuleTestContentPreview = chapterResult.Success ? chapterResult.ContentPreview : chapterResult.Message;
            RuleTestDiagnostics = report.ToString();
            RuleEditorSubTab = 1;

            await CopyDebugReportToClipboardAsync(report.ToString());
        }
        catch (Exception ex)
        {
            report.AppendLine("## ❌ 异常信息");
            report.AppendLine();
            report.AppendLine("调试过程中发生未捕获的异常：");
            report.AppendLine();
            report.AppendLine("```");
            report.AppendLine(ex.ToString());
            report.AppendLine("```");
            RuleTestStatus = $"Debug 异常：{ex.Message}";
            RuleTestDiagnostics = report.ToString();
            RuleEditorSubTab = 1;
            await CopyDebugReportToClipboardAsync(report.ToString());
        }
        finally
        {
            IsRuleTesting = false;
        }
    }

    private static string ExtractBracketUrl(string line)
    {
        var m = System.Text.RegularExpressions.Regex.Match(line, @"\[(.+)\]$");
        return m.Success ? m.Groups[1].Value : string.Empty;
    }

    private const int MaxHtmlDumpLength = 30000;

    private static void AppendDebugStep(
        StringBuilder report,
        int sectionNo,
        string stepName,
        string stepDescription,
        RuleTestResult result,
        IReadOnlyList<string> items)
    {
        report.AppendLine($"## {sectionNo}. {stepName}");
        report.AppendLine();
        report.AppendLine(stepDescription);
        report.AppendLine();

        // ── 测试结果概览 ──
        var statusEmoji = result.Success ? "✅" : "❌";
        report.AppendLine($"### {sectionNo}.1 测试结果");
        report.AppendLine();
        report.AppendLine($"| 项目 | 值 |");
        report.AppendLine($"| --- | --- |");
        report.AppendLine($"| 状态 | {statusEmoji} {(result.Success ? "成功" : "失败")} |");
        report.AppendLine($"| 耗时 | {result.ElapsedMs} ms |");
        report.AppendLine($"| 消息 | {(string.IsNullOrWhiteSpace(result.Message) ? "（无）" : result.Message)} |");
        report.AppendLine();

        // ── 请求信息 ──
        report.AppendLine($"### {sectionNo}.2 HTTP 请求");
        report.AppendLine();
        report.AppendLine("该步骤实际发出的网络请求：");
        report.AppendLine();
        report.AppendLine("```http");
        report.AppendLine($"{result.RequestMethod} {result.RequestUrl}");
        if (!string.IsNullOrWhiteSpace(result.RequestBody))
        {
            report.AppendLine();
            report.AppendLine(result.RequestBody);
        }
        report.AppendLine("```");
        report.AppendLine();

        // ── 使用的 CSS 选择器 ──
        if (result.SelectorLines.Count > 0)
        {
            report.AppendLine($"### {sectionNo}.3 CSS 选择器");
            report.AppendLine();
            report.AppendLine("规则中配置的选择器，AngleSharp 将使用这些选择器在返回的 HTML 中查找目标元素：");
            report.AppendLine();
            report.AppendLine("```css");
            foreach (var line in result.SelectorLines)
            {
                report.AppendLine(line);
            }
            report.AppendLine("```");
            report.AppendLine();
        }

        // ── 诊断详情 ──
        if (result.DiagnosticLines.Count > 0)
        {
            report.AppendLine($"### {sectionNo}.4 诊断详情");
            report.AppendLine();
            report.AppendLine("以下是执行过程中记录的诊断信息，有助于定位选择器匹配失败的原因：");
            report.AppendLine();
            foreach (var line in result.DiagnosticLines)
            {
                report.AppendLine($"- {line}");
            }
            report.AppendLine();
        }

        // ── 匹配结果列表 ──
        if (items.Count > 0)
        {
            report.AppendLine($"### {sectionNo}.5 匹配结果（共 {items.Count} 项）");
            report.AppendLine();
            report.AppendLine("通过选择器成功提取的条目如下。格式：`标题 - 作者 [URL]` 或 `章节名 [URL]`。");
            report.AppendLine();
            var displayCount = Math.Min(items.Count, 50);
            for (int i = 0; i < displayCount; i++)
            {
                report.AppendLine($"{i + 1}. {items[i]}");
            }
            if (items.Count > displayCount)
            {
                report.AppendLine($"\n> …… 还有 {items.Count - displayCount} 项未显示");
            }
            report.AppendLine();
        }
        else if (result.Success)
        {
            report.AppendLine($"### {sectionNo}.5 匹配结果");
            report.AppendLine();
            report.AppendLine("此步骤无列表输出（正文步骤仅输出文本内容）。");
            report.AppendLine();
        }

        // ── 正文内容预览 ──
        if (!string.IsNullOrWhiteSpace(result.ContentPreview))
        {
            report.AppendLine($"### {sectionNo}.6 内容预览");
            report.AppendLine();
            report.AppendLine("提取到的正文文本片段（前 500 字符）：");
            report.AppendLine();
            report.AppendLine("```text");
            var preview = result.ContentPreview.Length > 500
                ? result.ContentPreview[..500] + "\n…（已截断）"
                : result.ContentPreview;
            report.AppendLine(preview);
            report.AppendLine("```");
            report.AppendLine();
        }

        // ── 命中的 HTML 片段 ──
        report.AppendLine($"### {sectionNo}.7 命中的 HTML 片段");
        report.AppendLine();
        if (!string.IsNullOrWhiteSpace(result.MatchedHtml))
        {
            report.AppendLine($"选择器匹配到的 **第一个** DOM 节点的 OuterHtml（长度 {result.MatchedHtml.Length} 字符）。");
            report.AppendLine("如果这个片段的结构不是你期望的，说明选择器可能需要调整。");
            report.AppendLine();
            var matchedDump = result.MatchedHtml.Length > MaxHtmlDumpLength
                ? result.MatchedHtml[..MaxHtmlDumpLength] + "\n<!-- ……已截断，共 " + result.MatchedHtml.Length + " 字符 -->"
                : result.MatchedHtml;
            report.AppendLine("```html");
            report.AppendLine(matchedDump);
            report.AppendLine("```");
        }
        else
        {
            report.AppendLine("**未命中任何 HTML 节点。** 请检查选择器是否正确，或站点页面结构是否已变更。");
        }
        report.AppendLine();

        // ── 原始 HTML ──
        report.AppendLine($"### {sectionNo}.8 原始 HTML");
        report.AppendLine();
        if (!string.IsNullOrWhiteSpace(result.RawHtml))
        {
            report.AppendLine($"服务器返回的完整 HTML 页面（长度 {result.RawHtml.Length} 字符）。");
            report.AppendLine("可以在这段 HTML 中搜索目标内容，以确认选择器应该怎样编写。");
            report.AppendLine();
            var rawDump = result.RawHtml.Length > MaxHtmlDumpLength
                ? result.RawHtml[..MaxHtmlDumpLength] + "\n<!-- ……已截断，共 " + result.RawHtml.Length + " 字符 -->"
                : result.RawHtml;
            report.AppendLine("```html");
            report.AppendLine(rawDump);
            report.AppendLine("```");
        }
        else
        {
            report.AppendLine("未获取到原始 HTML（可能是请求失败或网络超时）。");
        }
        report.AppendLine();
    }

    private async Task CopyDebugReportToClipboardAsync(string text)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow?.Clipboard is null)
        {
            StatusMessage = "Debug 报告已生成，但未能访问剪贴板（已显示在预览诊断中）。";
            return;
        }

        await desktop.MainWindow.Clipboard.SetTextAsync(text);
    }

    // ==================== 阅读器样式 ====================

    [ObservableProperty]
    private double readerFontSize = 15;

    /// <summary>当前选中的字体名称（用于 ComboBox 绑定）。</summary>
    [ObservableProperty]
    private string selectedFontName = "默认";

    /// <summary>实际用于渲染的 FontFamily 对象。</summary>
    [ObservableProperty]
    private FontFamily readerFontFamily = FontFamily.Default;

    partial void OnSelectedFontNameChanged(string value)
    {
        ReaderFontFamily = _fontMap.TryGetValue(value, out var ff) ? ff : FontFamily.Default;
        QueueAutoSaveSettings();
    }

    private static readonly Dictionary<string, FontFamily> _fontMap = new()
    {
        ["默认"] = FontFamily.Default,
        ["微软雅黑"] = new FontFamily("Microsoft YaHei"),
        ["宋体"] = new FontFamily("SimSun"),
        ["楷体"] = new FontFamily("KaiTi"),
        ["仿宋"] = new FontFamily("FangSong"),
        ["黑体"] = new FontFamily("SimHei"),
        ["Consolas"] = new FontFamily("Consolas"),
    };

    [ObservableProperty]
    private double readerLineHeight = 28;

    partial void OnReaderLineHeightChanged(double value)
    {
        QueueAutoSaveSettings();
    }

    [ObservableProperty]
    private double readerParagraphSpacing = 12;

    /// <summary>段落间距对应的 Margin（仅 Bottom 有值）。</summary>
    [ObservableProperty]
    private Avalonia.Thickness paragraphMargin = new(0, 0, 0, 12);

    partial void OnReaderParagraphSpacingChanged(double value)
    {
        ParagraphMargin = new Avalonia.Thickness(0, 0, 0, value);
        QueueAutoSaveSettings();
    }

    partial void OnReaderFontSizeChanged(double value)
    {
        QueueAutoSaveSettings();
    }

    [ObservableProperty]
    private string readerBackground = "#FFFFFF";

    partial void OnReaderBackgroundChanged(string value)
    {
        QueueAutoSaveSettings();
    }

    [ObservableProperty]
    private string readerForeground = "#1F2937";

    partial void OnReaderForegroundChanged(string value)
    {
        QueueAutoSaveSettings();
    }

    [ObservableProperty]
    private bool isDarkMode;

    /// <summary>可选字体列表（中文名称）。</summary>
    public ObservableCollection<string> AvailableFonts { get; } =
    [
        "默认",
        "微软雅黑",
        "宋体",
        "楷体",
        "仿宋",
        "黑体",
        "Consolas",
    ];

    /// <summary>切换日/夜间模式。</summary>
    partial void OnIsDarkModeChanged(bool value)
    {
        if (value)
        {
            ReaderBackground = "#1A1A2E";
            ReaderForeground = "#E0E0E0";
        }
        else
        {
            ReaderBackground = "#FFFFFF";
            ReaderForeground = "#1F2937";
        }

        QueueAutoSaveSettings();
    }

    /// <summary>预设纸张色列表。</summary>
    public ObservableCollection<PaperPreset> PaperPresets { get; } =
    [
        new("白纸", "#FFFFFF", "#1F2937"),
        new("护眼绿", "#C7EDCC", "#2D3A2E"),
        new("羊皮纸", "#F5E6C8", "#3E2723"),
        new("浅灰", "#F0F0F0", "#333333"),
        new("暖黄", "#FDF6E3", "#544D3C"),
        new("夜间", "#1A1A2E", "#E0E0E0"),
    ];

    [RelayCommand]
    private void ApplyPaperPreset(PaperPreset? preset)
    {
        if (preset is null) return;
        ReaderBackground = preset.Background;
        ReaderForeground = preset.Foreground;
        IsDarkMode = preset.Name == "夜间";
    }
}

/// <summary>规则列表条目（用于 ListBox 绑定）。</summary>
public sealed partial class RuleListItem : ObservableObject
{
    public int Id { get; }
    public string Name { get; }
    public string Url { get; }
    public bool HasSearch { get; }

    /// <summary>null=未知, true=可达(绿), false=不可达(红)。</summary>
    [ObservableProperty]
    private bool? isHealthy;

    public string HealthDot => IsHealthy switch
    {
        true => "●",
        false => "●",
        null => "○",
    };

    public string HealthColor => IsHealthy switch
    {
        true => "#22C55E",
        false => "#EF4444",
        null => "#9CA3AF",
    };

    public string Display => $"[{Id}] {Name}";

    public RuleListItem(int id, string name, string url, bool hasSearch, bool? isHealthy = null)
    {
        Id = id;
        Name = name;
        Url = url;
        HasSearch = hasSearch;
        IsHealthy = isHealthy;
    }

    partial void OnIsHealthyChanged(bool? value)
    {
        OnPropertyChanged(nameof(HealthDot));
        OnPropertyChanged(nameof(HealthColor));
    }
}

/// <summary>纸张预设。</summary>
public sealed class PaperPreset(string name, string background, string foreground)
{
    public string Name { get; } = name;
    public string Background { get; } = background;
    public string Foreground { get; } = foreground;
    public string Display => $"{Name} ({Background})";
}
