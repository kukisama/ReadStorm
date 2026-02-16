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

    /// <summary>阅读 tab 保护：没有打开任何书时不允许切换到阅读页。</summary>
    partial void OnSelectedTabIndexChanged(int oldValue, int newValue)
    {
        if (newValue == 4 && SelectedDbBook is null && SelectedBookshelfItem is null)
        {
            // 回退到之前的 tab
            Avalonia.Threading.Dispatcher.UIThread.Post(() => SelectedTabIndex = oldValue);
            StatusMessage = "请先在书架中打开一本书，再进入阅读界面。";
        }

        // 切到规则处理 tab 时自动加载规则列表
        if (newValue == 5 && RuleEditorRules.Count == 0)
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

            int? sourceId = SelectedSourceId > 0 ? SelectedSourceId : null;
            var results = await _searchBooksUseCase.ExecuteAsync(SearchKeyword.Trim(), sourceId);
            foreach (var item in results)
            {
                SearchResults.Add(item);
            }

            var selectedSourceText = SelectedSourceId > 0 ? $"书源 {SelectedSourceId}" : "全部书源";
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
                await RefreshDbBooksAsync();
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

        // 刷新 DB 书架
        await RefreshDbBooksAsync();
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
        }
        catch
        {
            // DB not ready yet
        }
    }

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
        var settings = new AppSettings
        {
            DownloadPath = DownloadPath,
            MaxConcurrency = MaxConcurrency,
            MinIntervalMs = MinIntervalMs,
            MaxIntervalMs = MaxIntervalMs,
            ExportFormat = ExportFormat,
            ProxyEnabled = ProxyEnabled,
            ProxyHost = ProxyHost,
            ProxyPort = ProxyPort,
        };

        await _appSettingsUseCase.SaveAsync(settings);
        StatusMessage = "设置已保存到本地用户配置文件。";
    }

    private async Task LoadSettingsAsync()
    {
        var settings = await _appSettingsUseCase.LoadAsync();
        DownloadPath = settings.DownloadPath;
        MaxConcurrency = settings.MaxConcurrency;
        MinIntervalMs = settings.MinIntervalMs;
        MaxIntervalMs = settings.MaxIntervalMs;
        ExportFormat = settings.ExportFormat;
        ProxyEnabled = settings.ProxyEnabled;
        ProxyHost = settings.ProxyHost;
        ProxyPort = settings.ProxyPort;
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
            RuleEditorRules.Clear();
            foreach (var r in rules)
            {
                RuleEditorRules.Add(new RuleListItem(r.Id, r.Name, r.Url, r.Search is not null));
            }
            StatusMessage = $"规则列表已加载：共 {rules.Count} 条";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载规则失败：{ex.Message}";
        }
    }

    /// <summary>选中规则后加载其对象到编辑器表单。</summary>
    async partial void OnRuleEditorSelectedRuleChanged(RuleListItem? value)
    {
        if (value is null) { CurrentRule = null; return; }
        try
        {
            var rule = await _ruleEditorUseCase.LoadAsync(value.Id);
            if (rule is not null)
            {
                EnsureSubSections(rule);
                CurrentRule = rule;
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

    // ==================== 阅读器样式 ====================

    [ObservableProperty]
    private double readerFontSize = 15;

    [ObservableProperty]
    private string readerFontFamily = "Default";

    [ObservableProperty]
    private double readerLineHeight = 28;

    [ObservableProperty]
    private double readerParagraphSpacing = 12;

    [ObservableProperty]
    private string readerBackground = "#FFFFFF";

    [ObservableProperty]
    private string readerForeground = "#1F2937";

    [ObservableProperty]
    private bool isDarkMode;

    /// <summary>可选字体列表。</summary>
    public ObservableCollection<string> AvailableFonts { get; } =
    [
        "Default",
        "Microsoft YaHei",
        "SimSun",
        "KaiTi",
        "FangSong",
        "SimHei",
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
public sealed class RuleListItem(int id, string name, string url, bool hasSearch)
{
    public int Id { get; } = id;
    public string Name { get; } = name;
    public string Url { get; } = url;
    public bool HasSearch { get; } = hasSearch;
    public string Display => $"[{Id}] {Name}";
}

/// <summary>纸张预设。</summary>
public sealed class PaperPreset(string name, string background, string foreground)
{
    public string Name { get; } = name;
    public string Background { get; } = background;
    public string Foreground { get; } = foreground;
    public string Display => $"{Name} ({Background})";
}
