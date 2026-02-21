using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReadStorm.Application.Abstractions;
using ReadStorm.Domain.Models;
using ReadStorm.Infrastructure.Services;

namespace ReadStorm.Desktop.ViewModels;

public sealed partial class SearchDownloadViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _parent;
    private readonly ISearchBooksUseCase _searchBooksUseCase;
    private readonly IDownloadBookUseCase _downloadBookUseCase;
    private readonly IBookRepository _bookRepo;
    private readonly ISourceHealthCheckUseCase _healthCheckUseCase;

    private readonly SourceDownloadQueue _downloadQueue = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _downloadCts = new();
    private readonly HashSet<Guid> _pauseRequested = new();
    private CancellationTokenSource? _searchCts;
    private int _searchSerial;

    public SearchDownloadViewModel(
        MainWindowViewModel parent,
        ISearchBooksUseCase searchBooksUseCase,
        IDownloadBookUseCase downloadBookUseCase,
        IBookRepository bookRepo,
        ISourceHealthCheckUseCase healthCheckUseCase)
    {
        _parent = parent;
        _searchBooksUseCase = searchBooksUseCase;
        _downloadBookUseCase = downloadBookUseCase;
        _bookRepo = bookRepo;
        _healthCheckUseCase = healthCheckUseCase;
    }

    // ==================== Observable Properties ====================

    [ObservableProperty]
    private string searchKeyword = string.Empty;

    [ObservableProperty]
    private int selectedSourceId;

    [ObservableProperty]
    private bool isSearching;

    /// <summary>搜索完毕且无结果时为 true，用于控制空状态提示文案的可见性。</summary>
    [ObservableProperty]
    private bool hasNoSearchResults;

    [ObservableProperty]
    private bool isCheckingHealth;

    [ObservableProperty]
    private SearchResult? selectedSearchResult;

    public static IReadOnlyList<string> TaskFilterStatusOptions { get; } =
        ["全部", "排队中", "下载中", "已完成", "已失败", "已取消", "已暂停"];

    private static DownloadTaskStatus? MapFilterToStatus(string filter) => filter switch
    {
        "排队中" => DownloadTaskStatus.Queued,
        "下载中" => DownloadTaskStatus.Downloading,
        "已完成" => DownloadTaskStatus.Succeeded,
        "已失败" => DownloadTaskStatus.Failed,
        "已取消" => DownloadTaskStatus.Cancelled,
        "已暂停" => DownloadTaskStatus.Paused,
        _ => null, // "全部"
    };

    [ObservableProperty]
    private string taskFilterStatus = "全部";

    partial void OnTaskFilterStatusChanged(string value) => ApplyTaskFilter();

    // ==================== Collections ====================

    public ObservableCollection<SearchResult> SearchResults { get; } = new();
    public ObservableCollection<DownloadTask> DownloadTasks { get; } = new();
    public ObservableCollection<DownloadTask> FilteredDownloadTasks { get; } = new();

    /// <summary>状态栏显示的活跃下载摘要，如「下载中 3/10」。</summary>
    [ObservableProperty]
    private string activeDownloadSummary = string.Empty;

    /// <summary>Proxy for AXAML ComboBox binding – delegates to the shared collection on MainWindowViewModel.</summary>
    public ObservableCollection<SourceItem> Sources => _parent.Sources;

    // ==================== Commands ====================

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchKeyword)) return;

        // 再次点击搜索：立刻取消上一轮并启动新一轮
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;
        var serial = Interlocked.Increment(ref _searchSerial);

        try
        {
            IsSearching = true;
            _parent.StatusMessage = "搜索中...";
            SearchResults.Clear();
            HasNoSearchResults = false;

            var keyword = SearchKeyword.Trim();
            var selectedSourceText = SelectedSourceId > 0 ? $"书源 {SelectedSourceId}" : "全部书源(健康)";

            if (SelectedSourceId > 0)
            {
                var results = await _searchBooksUseCase.ExecuteAsync(keyword, SelectedSourceId, ct);
                foreach (var item in results)
                {
                    var src = _parent.Sources.FirstOrDefault(s => s.Id == item.SourceId);
                    var srcName = src?.Name ?? $"书源{item.SourceId}";
                    SearchResults.Add(item with { SourceName = srcName });
                }
            }
            else
            {
                var healthySources = _parent.Sources
                    .Where(s => s.Id > 0 && s.IsHealthy == true && s.SearchSupported)
                    .ToList();

                if (healthySources.Count == 0)
                {
                    _parent.StatusMessage = "搜索完成（全部书源(健康)）：0 条。当前没有可用的绿色节点，请先刷新书源健康状态。";
                    return;
                }

                const int perSourceLimit = 3;
                var maxConcurrent = Math.Clamp(_parent.Settings.AggregateSearchMaxConcurrency, 1, 64);
                var semaphore = new SemaphoreSlim(maxConcurrent);

                var tasks = healthySources.Select(async src =>
                {
                    await semaphore.WaitAsync(ct);
                    try
                    {
                        var one = await _searchBooksUseCase.ExecuteAsync(keyword, src.Id, ct);
                        return one.Take(perSourceLimit).Select(x => x with { SourceName = src.Name }).ToList();
                    }
                    catch (OperationCanceledException)
                    {
                        return new List<SearchResult>();
                    }
                    catch (Exception ex) { AppLogger.Warn($"Search.PerSource:{src.Name}", ex); return new List<SearchResult>(); }
                    finally { semaphore.Release(); }
                }).ToList();

                var perSourceResults = await Task.WhenAll(tasks);

                var merged = perSourceResults
                    .SelectMany(x => x)
                    .GroupBy(x => $"{x.Title}|{x.Author}|{x.SourceId}", StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();

                foreach (var item in merged) SearchResults.Add(item);

                selectedSourceText = $"全部书源(健康:{healthySources.Count}源,每源前{perSourceLimit}条)";
            }

            if (SearchResults.Count == 0 && SelectedSourceId > 0)
                _parent.StatusMessage = $"搜索完成（{selectedSourceText}）：0 条。该书源当前可能限流/规则不兼容，请切换书源重试。";
            else
                _parent.StatusMessage = $"搜索完成（{selectedSourceText}）：共 {SearchResults.Count} 条";
        }
        catch (OperationCanceledException)
        {
            // 被下一次搜索打断时静默结束；由新一轮接管状态文案。
        }
        catch (Exception ex) { _parent.StatusMessage = $"搜索失败：{ex.Message}"; }
        finally
        {
            if (serial == _searchSerial)
            {
                IsSearching = false;
                HasNoSearchResults = SearchResults.Count == 0;
            }
        }
    }

    [RelayCommand]
    private async Task QueueDownloadAsync()
    {
        if (SelectedSearchResult is null) { _parent.StatusMessage = "请先在搜索结果中选择一本书。"; return; }
        if (SelectedSearchResult.Url.Contains("example.com", StringComparison.OrdinalIgnoreCase))
        { _parent.StatusMessage = "当前是示例搜索结果，不支持真实下载。请切换具体书源重新搜索。"; return; }
        if (string.IsNullOrWhiteSpace(SelectedSearchResult.Url))
        { _parent.StatusMessage = "搜索结果缺少书籍 URL，无法下载。"; return; }

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
        _parent.StatusMessage = $"已加入下载队列：《{task.BookTitle}》";
        _ = RunDownloadInBackgroundAsync(task, searchResult);
    }

    [RelayCommand]
    private async Task RefreshSourceHealthAsync()
    {
        if (IsCheckingHealth) return;
        IsCheckingHealth = true;
        try
        {
            var rules = _parent.Sources
                .Where(s => s.Id > 0)
                .Select(s => new BookSourceRule { Id = s.Id, Name = s.Name, Url = s.Url, SearchSupported = s.SearchSupported })
                .ToList();

            var results = await _healthCheckUseCase.CheckAllAsync(rules);

            var lookup = results.ToDictionary(r => r.SourceId, r => r.IsReachable);
            foreach (var source in _parent.Sources)
            {
                if (lookup.TryGetValue(source.Id, out var healthy))
                    source.IsHealthy = healthy;
            }

            var ok = results.Count(r => r.IsReachable);
            _parent.StatusMessage = $"书源健康检测完成：{ok}/{results.Count} 可达";

            _parent.Reader.RefreshSortedSwitchSources();
            _parent.RuleEditor.SyncRuleEditorRuleHealthFromSources();
        }
        catch (Exception ex) { _parent.StatusMessage = $"书源健康检测失败：{ex.Message}"; }
        finally { IsCheckingHealth = false; }
    }

    [RelayCommand]
    private Task RetryDownloadAsync(DownloadTask? task)
    {
        if (task is null || !task.CanRetry)
            return Task.CompletedTask;

        var searchResult = task.SourceSearchResult;
        if (searchResult is null)
        {
            _parent.StatusMessage = $"无法重试：《{task.BookTitle}》缺少原始搜索信息。";
            return Task.CompletedTask;
        }

        task.ResetForRetry();
        ApplyTaskFilter();
        _parent.StatusMessage = $"正在重试（第{task.RetryCount}次）：《{task.BookTitle}》...";

        _ = RunDownloadInBackgroundAsync(task, searchResult);
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void CancelDownload(DownloadTask? task)
    {
        if (task is null || !task.CanCancel)
            return;

        try
        {
            if (_downloadCts.TryGetValue(task.Id, out var cts))
                cts.Cancel();

            task.TransitionTo(DownloadTaskStatus.Cancelled);
            task.Error = "用户手动取消";
            ApplyTaskFilter();
            _parent.StatusMessage = $"已取消：《{task.BookTitle}》";
        }
        catch (Exception ex)
        {
            _parent.StatusMessage = $"取消失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteDownloadAsync(DownloadTask? task)
    {
        if (task is null || !task.CanDelete)
            return;

        var confirmed = await Views.DialogHelper.ConfirmAsync(
            "确认删除",
            $"确定要删除下载任务《{task.BookTitle}》吗？");
        if (!confirmed) return;

        // 如果还在运行，先取消
        if (_downloadCts.TryGetValue(task.Id, out var cts))
            cts.Cancel();

        DownloadTasks.Remove(task);
        FilteredDownloadTasks.Remove(task);
        _parent.StatusMessage = $"已删除任务：《{task.BookTitle}》";
    }

    [RelayCommand]
    private void PauseDownload(DownloadTask? task)
    {
        if (task is null || !task.CanPause) return;
        lock (_pauseRequested) _pauseRequested.Add(task.Id);
        if (_downloadCts.TryGetValue(task.Id, out var cts)) cts.Cancel();
        ApplyTaskFilter();
        _parent.StatusMessage = $"正在暂停：《{task.BookTitle}》…";
    }

    [RelayCommand]
    private void StopAllDownloads()
    {
        var active = DownloadTasks
            .Where(t => t.CanPause)
            .ToList();
        if (active.Count == 0)
        {
            _parent.StatusMessage = "没有可停止的下载任务。";
            return;
        }

        foreach (var task in active)
        {
            lock (_pauseRequested) _pauseRequested.Add(task.Id);
            if (_downloadCts.TryGetValue(task.Id, out var cts)) cts.Cancel();
        }
        ApplyTaskFilter();
        _parent.StatusMessage = $"已全部停止：{active.Count} 个任务。";
    }

    [RelayCommand]
    private Task StartAllDownloadsAsync()
    {
        var paused = DownloadTasks
            .Where(t => t.CanResume)
            .ToList();
        if (paused.Count == 0)
        {
            _parent.StatusMessage = "没有可恢复的下载任务。";
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
        _parent.StatusMessage = $"已全部恢复：{paused.Count} 个任务。";
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task ResumeDownloadTaskAsync(DownloadTask? task)
    {
        if (task is null || !task.CanResume) return Task.CompletedTask;
        var searchResult = task.SourceSearchResult;
        if (searchResult is null)
        {
            _parent.StatusMessage = $"无法恢复：缺少原始搜索信息。";
            return Task.CompletedTask;
        }

        task.ResetForResume();
        ApplyTaskFilter();
        _parent.StatusMessage = $"恢复下载：《{task.BookTitle}》";
        _ = RunDownloadInBackgroundAsync(task, searchResult);
        return Task.CompletedTask;
    }

    // ==================== Download Pipeline ====================

    internal async Task RunDownloadInBackgroundAsync(DownloadTask task, SearchResult searchResult)
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
                try { task.TransitionTo(DownloadTaskStatus.Failed); } catch (Exception transEx) { AppLogger.Warn("Download.TransitionFailed", transEx); }
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
                task.OverrideToPaused();

            _downloadCts.TryRemove(task.Id, out _);
            cts.Dispose();
        }

        await OnDownloadCompleted(task);
    }

    // ==================== Public API (called by BookshelfVM / code-behind) ====================

    public void QueueDownloadTask(DownloadTask task, SearchResult searchResult)
    {
        DownloadTasks.Insert(0, task);
        ApplyTaskFilter();
        _ = RunDownloadInBackgroundAsync(task, searchResult);
    }

    public void QueueDownloadFromSearchResult(SearchResult result)
    {
        SelectedSearchResult = result;
        if (QueueDownloadCommand.CanExecute(null))
            QueueDownloadCommand.Execute(null);
    }

    // ==================== Private Helpers ====================

    private async Task PeriodicRefreshDbBooksAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(5000, ct);
                // 懒刷新：书架不可见时仅标记脏数据，不查询数据库
                if (_parent.SelectedTabIndex == TabIndex.Bookshelf)
                    await _parent.Bookshelf.RefreshDbBooksIfNeededAsync();
                else
                    _parent.Bookshelf.MarkBookshelfDirty();
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task OnDownloadCompleted(DownloadTask task)
    {
        ApplyTaskFilter();
        var logPath = RuleBasedDownloadBookUseCase.GetLogFilePath();
        if (task.CurrentStatus == DownloadTaskStatus.Succeeded)
        {
            _parent.StatusMessage = $"下载完成：《{task.BookTitle}》。调试日志：{logPath}";
            await _parent.Bookshelf.AddToBookshelfAsync(task);
        }
        else
        {
            _parent.StatusMessage = $"下载失败（{task.ErrorKind}）：{task.Error}。调试日志：{logPath}";
        }

        _parent.Bookshelf.MarkBookshelfDirty();
        if (_parent.SelectedTabIndex == TabIndex.Bookshelf)
            await _parent.Bookshelf.RefreshDbBooksIfNeededAsync(force: true);
    }

    internal void ApplyTaskFilter()
    {
        FilteredDownloadTasks.Clear();
        var status = MapFilterToStatus(TaskFilterStatus);
        foreach (var task in DownloadTasks)
        {
            if (status is null || task.CurrentStatus == status)
                FilteredDownloadTasks.Add(task);
        }
        UpdateActiveDownloadSummary();
    }

    /// <summary>更新下载活跃摘要文本。</summary>
    internal void UpdateActiveDownloadSummary()
    {
        var active = DownloadTasks.Count(t => t.CurrentStatus is DownloadTaskStatus.Downloading or DownloadTaskStatus.Queued);
        ActiveDownloadSummary = active > 0
            ? $"下载中 {active}/{DownloadTasks.Count}"
            : DownloadTasks.Count > 0 ? $"任务 {DownloadTasks.Count}" : string.Empty;
    }
}
