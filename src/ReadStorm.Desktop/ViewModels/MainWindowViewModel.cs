using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReadStorm.Application.Abstractions;
using ReadStorm.Domain.Models;

namespace ReadStorm.Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ISearchBooksUseCase _searchBooksUseCase;
    private readonly IDownloadBookUseCase _downloadBookUseCase;
    private readonly IAppSettingsUseCase _appSettingsUseCase;
    private readonly IRuleCatalogUseCase _ruleCatalogUseCase;
    private readonly ISourceDiagnosticUseCase _sourceDiagnosticUseCase;
    private readonly IBookshelfUseCase _bookshelfUseCase;

    public MainWindowViewModel(
        ISearchBooksUseCase searchBooksUseCase,
        IDownloadBookUseCase downloadBookUseCase,
        IAppSettingsUseCase appSettingsUseCase,
        IRuleCatalogUseCase ruleCatalogUseCase,
        ISourceDiagnosticUseCase sourceDiagnosticUseCase,
        IBookshelfUseCase bookshelfUseCase)
    {
        _searchBooksUseCase = searchBooksUseCase;
        _downloadBookUseCase = downloadBookUseCase;
        _appSettingsUseCase = appSettingsUseCase;
        _ruleCatalogUseCase = ruleCatalogUseCase;
        _sourceDiagnosticUseCase = sourceDiagnosticUseCase;
        _bookshelfUseCase = bookshelfUseCase;

        Title = "ReadStorm - 下载器重构M0";
        StatusMessage = "就绪：可先用假数据验证 UI 与流程。";

        _ = LoadSettingsAsync();
        _ = LoadRuleStatsAsync();
        _ = LoadBookshelfAsync();
    }

    // ==================== Collections ====================
    public ObservableCollection<SearchResult> SearchResults { get; } = [];

    public ObservableCollection<DownloadTask> DownloadTasks { get; } = [];

    public ObservableCollection<DownloadTask> FilteredDownloadTasks { get; } = [];

    public ObservableCollection<BookSourceRule> Sources { get; } = [];

    public ObservableCollection<BookRecord> BookshelfItems { get; } = [];

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
    private string exportFormat = "epub";

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
    [ObservableProperty]
    private int diagnosticSourceId;

    [ObservableProperty]
    private string diagnosticKeyword = "测试";

    [ObservableProperty]
    private bool isDiagnosing;

    [ObservableProperty]
    private string diagnosticSummary = string.Empty;

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

        try
        {
            var task = new DownloadTask
            {
                Id = Guid.NewGuid(),
                BookTitle = SelectedSearchResult.Title,
                Author = SelectedSearchResult.Author,
                Mode = DownloadMode.FullBook,
                EnqueuedAt = DateTimeOffset.Now,
                SourceSearchResult = SelectedSearchResult,
            };

            DownloadTasks.Insert(0, task);
            ApplyTaskFilter();
            StatusMessage = $"已加入下载队列：《{task.BookTitle}》，开始下载...";

            await _downloadBookUseCase.QueueAsync(task, SelectedSearchResult, DownloadMode.FullBook);

            await OnDownloadCompleted(task);
        }
        catch (Exception ex)
        {
            StatusMessage = $"加入下载失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RetryDownloadAsync(DownloadTask? task)
    {
        if (task is null || !task.CanRetry)
        {
            return;
        }

        var searchResult = task.SourceSearchResult;
        if (searchResult is null)
        {
            StatusMessage = $"无法重试：《{task.BookTitle}》缺少原始搜索信息。";
            return;
        }

        try
        {
            task.ResetForRetry();
            ApplyTaskFilter();
            StatusMessage = $"正在重试（第{task.RetryCount}次）：《{task.BookTitle}》...";

            await _downloadBookUseCase.QueueAsync(task, searchResult, task.Mode);

            await OnDownloadCompleted(task);
        }
        catch (Exception ex)
        {
            StatusMessage = $"重试失败：{ex.Message}";
        }
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

    private async Task OnDownloadCompleted(DownloadTask task)
    {
        ApplyTaskFilter();
        var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "readstorm-download.log");
        if (task.CurrentStatus == DownloadTaskStatus.Succeeded)
        {
            StatusMessage = $"下载完成：《{task.BookTitle}》。调试日志：{logPath}";

            // Auto-add to bookshelf
            await AddToBookshelfAsync(task);
        }
        else
        {
            StatusMessage = $"下载失败（{task.ErrorKind}）：{task.Error}。调试日志：{logPath}";
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
    private async Task RunDiagnosticAsync()
    {
        if (DiagnosticSourceId <= 0)
        {
            StatusMessage = "请输入有效的书源 ID。";
            return;
        }

        try
        {
            IsDiagnosing = true;
            DiagnosticSummary = "诊断中...";
            DiagnosticLines.Clear();

            var result = await _sourceDiagnosticUseCase.DiagnoseAsync(
                DiagnosticSourceId,
                DiagnosticKeyword);

            DiagnosticSummary = $"[{result.SourceName}] {result.Summary} | HTTP={result.HttpStatusCode} | " +
                                $"搜索={result.SearchResultCount}条 | 目录selector='{result.TocSelector}' " +
                                $"| 章节selector='{result.ChapterContentSelector}'";

            foreach (var line in result.DiagnosticLines)
            {
                DiagnosticLines.Add(line);
            }

            StatusMessage = $"书源 {DiagnosticSourceId} 诊断完成：{result.Summary}";
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

    private List<(string Title, string Content)> _currentBookChapters = [];

    private async Task NavigateToChapterAsync(int index)
    {
        if (index < 0 || index >= _currentBookChapters.Count)
        {
            return;
        }

        ReaderCurrentChapterIndex = index;
        ReaderContent = _currentBookChapters[index].Content;

        if (SelectedBookshelfItem is not null)
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
        Sources.Add(new BookSourceRule
        {
            Id = 0,
            Name = "全部书源",
            Url = string.Empty,
            SearchSupported = true,
        });

        foreach (var rule in rules)
        {
            Sources.Add(rule);
        }

        SelectedSourceId = 0;
        AvailableSourceCount = rules.Count;
        StatusMessage = $"就绪：已加载 {AvailableSourceCount} 条书源规则，可切换测试。";
    }
}
