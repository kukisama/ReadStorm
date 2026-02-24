using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReadStorm.Application.Abstractions;
using ReadStorm.Domain.Models;
using ReadStorm.Infrastructure.Services;

namespace ReadStorm.Desktop.ViewModels;

public sealed partial class BookshelfViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _parent;
    private readonly IBookshelfUseCase _bookshelfUseCase;
    private readonly IBookRepository _bookRepo;
    private readonly IDownloadBookUseCase _downloadBookUseCase;
    private readonly ICoverUseCase _coverUseCase;
    private readonly IAppSettingsUseCase _appSettingsUseCase;

    // --- Fields ---
    private bool _bookshelfDirty = true;
    private DateTimeOffset _lastBookshelfRefreshAt = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _bookshelfRefreshLock = new(1, 1);

    public BookshelfViewModel(
        MainWindowViewModel parent,
        IBookshelfUseCase bookshelfUseCase,
        IBookRepository bookRepo,
        IDownloadBookUseCase downloadBookUseCase,
        ICoverUseCase coverUseCase,
        IAppSettingsUseCase appSettingsUseCase)
    {
        _parent = parent;
        _bookshelfUseCase = bookshelfUseCase;
        _bookRepo = bookRepo;
        _downloadBookUseCase = downloadBookUseCase;
        _coverUseCase = coverUseCase;
        _appSettingsUseCase = appSettingsUseCase;
    }

    // --- Properties ---

    [ObservableProperty]
    private bool isBookshelfLargeMode;

    [ObservableProperty]
    private int bookshelfLargeColumnCount = 3;

    /// <summary>书架搜索关键字，支持按书名/作者模糊搜索。</summary>
    [ObservableProperty]
    private string bookshelfFilterText = string.Empty;

    /// <summary>书架排序方式。</summary>
    [ObservableProperty]
    private string bookshelfSortMode = "最近阅读";

    [ObservableProperty]
    private double bookshelfProgressLeftPaddingPx = 5;

    [ObservableProperty]
    private double bookshelfProgressRightPaddingPx = 5;

    [ObservableProperty]
    private double bookshelfProgressTotalWidthPx = 106;

    [ObservableProperty]
    private double bookshelfProgressMinWidthPx = 72;

    [ObservableProperty]
    private Thickness bookshelfProgressPadding = new(5, 0, 5, 0);

    [ObservableProperty]
    private double bookshelfProgressEffectiveWidth = 106;

    [ObservableProperty]
    private double bookshelfProgressBarToPercentGapPx = 8;

    [ObservableProperty]
    private double bookshelfProgressPercentTailGapPx = 24;

    [ObservableProperty]
    private Thickness bookshelfProgressBarMargin = new(0, 0, 8, 0);

    [ObservableProperty]
    private Thickness bookshelfProgressPercentMargin = new(0, 0, 24, 0);

    public static IReadOnlyList<string> BookshelfSortOptions { get; } =
        ["最近阅读", "书名", "作者", "下载进度"];

    partial void OnBookshelfFilterTextChanged(string value) => ApplyBookshelfFilter();
    partial void OnBookshelfSortModeChanged(string value) => ApplyBookshelfFilter();
    partial void OnBookshelfProgressLeftPaddingPxChanged(double value) => RecalculateProgressLayout();
    partial void OnBookshelfProgressRightPaddingPxChanged(double value) => RecalculateProgressLayout();
    partial void OnBookshelfProgressTotalWidthPxChanged(double value) => RecalculateProgressLayout();
    partial void OnBookshelfProgressMinWidthPxChanged(double value) => RecalculateProgressLayout();
    partial void OnBookshelfProgressBarToPercentGapPxChanged(double value) => RecalculateProgressLayout();
    partial void OnBookshelfProgressPercentTailGapPxChanged(double value) => RecalculateProgressLayout();

    // --- Collections ---

    public ObservableCollection<BookEntity> DbBooks { get; } = [];

    /// <summary>经过搜索和排序后的书架视图。</summary>
    public ObservableCollection<BookEntity> FilteredDbBooks { get; } = [];

    public ObservableCollection<BookRecord> BookshelfItems { get; } = [];

    // ==================== Commands ====================

    /// <summary>在文件资源管理器中打开下载目录。</summary>
    [RelayCommand]
    private void OpenBookshelfDirectory()
    {
        try
        {
            var fullPath = Path.GetFullPath(_parent.Settings.DownloadPath);
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = fullPath,
                UseShellExecute = true,
            });
            _parent.StatusMessage = $"已打开目录：{fullPath}";
        }
        catch (Exception ex)
        {
            _parent.StatusMessage = $"打开目录失败：{ex.Message}";
        }
    }

    /// <summary>在阅读器中打开书籍。</summary>
    [RelayCommand]
    private async Task OpenDbBookAsync(BookEntity? book)
    {
        if (book is null) return;
        try
        {
            await _parent.Reader.OpenBookAsync(book);
            _parent.IsReaderTabVisible = true;
            _parent.SelectedTabIndex = TabIndex.Reader;
        }
        catch (Exception ex)
        {
            _parent.StatusMessage = $"打开失败：{ex.Message}";
        }
    }

    /// <summary>继续下载未完成的书籍。</summary>
    [RelayCommand]
    private Task ResumeBookDownloadAsync(BookEntity? book)
    {
        if (book is null) return Task.CompletedTask;
        if (book.IsComplete)
        {
            _parent.StatusMessage = $"《{book.Title}》已全部下载完成，无需续传。";
            return Task.CompletedTask;
        }

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

        _parent.SearchDownload.QueueDownloadTask(task, searchResult);
        _parent.StatusMessage = $"继续下载：《{book.Title}》（剩余 {book.TotalChapters - book.DoneChapters} 章）";
        return Task.CompletedTask;
    }

    /// <summary>导出书籍为 txt 文件。</summary>
    [RelayCommand]
    private async Task ExportDbBookAsync(BookEntity? book)
    {
        if (book is null) return;
        try
        {
            var doneContents = await _bookRepo.GetDoneChapterContentsAsync(book.Id);
            if (doneContents.Count == 0)
            {
                _parent.StatusMessage = $"《{book.Title}》尚无已下载章节，无法导出。";
                return;
            }

            var settings = await _appSettingsUseCase.LoadAsync();
            var workDir = WorkDirectoryManager.NormalizeAndMigrateWorkDirectory(settings.DownloadPath);
            var downloadPath = WorkDirectoryManager.GetDownloadsDirectory(workDir);
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

            _parent.StatusMessage = $"导出完成：{outputPath}（{doneContents.Count} 章）";
        }
        catch (Exception ex)
        {
            _parent.StatusMessage = $"导出失败：{ex.Message}";
        }
    }

    /// <summary>从 DB 移除书籍（含所有章节数据）。</summary>
    [RelayCommand]
    private async Task RemoveDbBookAsync(BookEntity? book)
    {
        if (book is null) return;

        var confirmed = await Views.DialogHelper.ConfirmAsync(
            "确认删除",
            $"确定要删除《{book.Title}》吗？\n此操作将移除该书籍及其所有章节数据，不可恢复。");
        if (!confirmed) return;

        try
        {
            await _bookRepo.DeleteBookAsync(book.Id);
            DbBooks.Remove(book);
            _parent.StatusMessage = $"已从书架移除：《{book.Title}》";
        }
        catch (Exception ex)
        {
            _parent.StatusMessage = $"移除失败：{ex.Message}";
        }
    }

    /// <summary>刷新封面（诊断 + 重新抓取）。</summary>
    [RelayCommand]
    private async Task RefreshCoverAsync(BookEntity? book)
    {
        if (book is null) return;
        try
        {
            _parent.StatusMessage = $"正在刷新封面：《{book.Title}》…";
            var diagnosticInfo = await _coverUseCase.RefreshCoverAsync(book);

            var refreshed = await _bookRepo.GetBookAsync(book.Id);
            if (refreshed is not null)
            {
                if (_parent.Reader.SelectedDbBook?.Id == refreshed.Id)
                    _parent.Reader.SelectedDbBook = refreshed;
                ReplaceDbBookInList(refreshed);
            }

            await RefreshDbBooksAsync();
            _parent.StatusMessage = diagnosticInfo.Replace("\r\n", "  ").Replace("\n", "  ").TrimEnd();
        }
        catch (Exception ex)
        {
            _parent.StatusMessage = $"刷新封面失败：{ex.Message}";
        }
    }

    /// <summary>检查单本书是否有新章节，有则自动续传。</summary>
    [RelayCommand]
    private async Task CheckNewChaptersAsync(BookEntity? book)
    {
        if (book is null) return;
        try
        {
            _parent.StatusMessage = $"正在检查新章节：《{book.Title}》…";
            var newCount = await _downloadBookUseCase.CheckNewChaptersAsync(book);
            if (newCount > 0)
            {
                _parent.StatusMessage = $"《{book.Title}》发现 {newCount} 个新章节，正在自动续传…";
                await RefreshDbBooksAsync();
                await ResumeBookDownloadAsync(book);
            }
            else
            {
                _parent.StatusMessage = $"《{book.Title}》目录无更新。";
            }
        }
        catch (Exception ex)
        {
            _parent.StatusMessage = $"检查新章节失败：{ex.Message}";
        }
    }

    /// <summary>检查全部书架书籍的新章节。</summary>
    [RelayCommand]
    private async Task CheckAllNewChaptersAsync()
    {
        _parent.StatusMessage = "正在检查所有书籍的新章节…";
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
            catch (Exception ex) { AppLogger.Warn($"Bookshelf.CheckNewChapters:{book.Title}", ex); }
        }

        await RefreshDbBooksAsync();
        _parent.StatusMessage = totalNew > 0
            ? $"全部检查完成，共发现 {totalNew} 个新章节，已自动续传。"
            : "全部检查完成，没有发现新章节。";
    }

    // ==================== Internal / Public Methods ====================

    /// <summary>初始化书架：加载数据、按设置恢复下载、补抓封面。</summary>
    internal async Task InitAsync()
    {
        await LoadBookshelfAsync();
        // 仅在用户开启"启动自动续传"开关时才自动恢复未完成的下载
        var settings = await _appSettingsUseCase.LoadAsync();
        if (settings.AutoResumeAndRefreshOnStartup)
        {
            await ResumeIncompleteDownloadsAsync();
        }
        _ = AutoFetchMissingCoversAsync();
    }

    /// <summary>从持久化加载书架数据。</summary>
    internal async Task LoadBookshelfAsync()
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

        await RefreshDbBooksAsync();
    }

    /// <summary>从 DB 加载书籍列表并排序。</summary>
    internal async Task RefreshDbBooksAsync()
    {
        await _bookshelfRefreshLock.WaitAsync();
        try
        {
            var dbBooks = await _bookRepo.GetAllBooksAsync();
            var sorted = dbBooks
                .OrderByDescending(b =>
                    !string.IsNullOrWhiteSpace(b.ReadAt) && DateTimeOffset.TryParse(b.ReadAt, out var rdt)
                        ? rdt : DateTimeOffset.MinValue)
                .ThenByDescending(b =>
                    DateTimeOffset.TryParse(b.CreatedAt, out var cdt) ? cdt : DateTimeOffset.MinValue)
                .ToList();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                DbBooks.Clear();
                foreach (var b in sorted)
                {
                    if (b.DoneChapters > b.TotalChapters)
                    {
                        b.TotalChapters = b.DoneChapters;
                    }

                    b.IsDownloading = _parent.SearchDownload.DownloadTasks.Any(t =>
                        t.BookId == b.Id &&
                        t.CurrentStatus is DownloadTaskStatus.Queued or DownloadTaskStatus.Downloading);
                    DbBooks.Add(b);
                }

                _bookshelfDirty = false;
                _lastBookshelfRefreshAt = DateTimeOffset.UtcNow;
                ApplyBookshelfFilterCore();
            });
        }
        catch (Exception ex)
        {
            _parent.StatusMessage = $"书架刷新失败：{ex.Message}";
            System.Diagnostics.Debug.WriteLine($"[Bookshelf] RefreshDbBooksAsync error: {ex}");
        }
        finally
        {
            _bookshelfRefreshLock.Release();
        }
    }

    /// <summary>延迟刷新守卫，避免频繁刷新。</summary>
    internal async Task RefreshDbBooksIfNeededAsync(bool force = false)
    {
        var tooSoon = DateTimeOffset.UtcNow - _lastBookshelfRefreshAt < TimeSpan.FromSeconds(1);
        if (!force && !_bookshelfDirty && tooSoon)
        {
            return;
        }

        await RefreshDbBooksAsync();
    }

    /// <summary>标记书架数据需要刷新。</summary>
    internal void MarkBookshelfDirty() => _bookshelfDirty = true;

    /// <summary>应用搜索和排序过滤到 FilteredDbBooks。</summary>
    internal void ApplyBookshelfFilter()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(ApplyBookshelfFilterCore);
            return;
        }

        ApplyBookshelfFilterCore();
    }

    private void ApplyBookshelfFilterCore()
    {
        IEnumerable<BookEntity> source = DbBooks;

        // 搜索过滤
        var keyword = BookshelfFilterText?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(keyword))
        {
            source = source.Where(b =>
                (b.Title?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true) ||
                (b.Author?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true));
        }

        // 排序
        var sorted = BookshelfSortMode switch
        {
            "书名" => source.OrderBy(b => b.Title, StringComparer.OrdinalIgnoreCase),
            "作者" => source.OrderBy(b => b.Author, StringComparer.OrdinalIgnoreCase),
            "下载进度" => source.OrderByDescending(b => b.ProgressPercent),
            _ => source.OrderByDescending(b =>
                    !string.IsNullOrWhiteSpace(b.ReadAt) && DateTimeOffset.TryParse(b.ReadAt, out var rdt)
                        ? rdt : DateTimeOffset.MinValue)
                .ThenByDescending(b =>
                    DateTimeOffset.TryParse(b.CreatedAt, out var cdt) ? cdt : DateTimeOffset.MinValue),
        };

        FilteredDbBooks.Clear();
        foreach (var b in sorted)
            FilteredDbBooks.Add(b);
    }

    /// <summary>替换列表中的书籍实体（保留下载状态）。</summary>
    internal void ReplaceDbBookInList(BookEntity refreshed)
    {
        var idx = DbBooks.IndexOf(DbBooks.FirstOrDefault(b => b.Id == refreshed.Id) ?? refreshed);
        if (idx >= 0)
        {
            refreshed.IsDownloading = DbBooks[idx].IsDownloading;
            DbBooks[idx] = refreshed;
        }
    }

    /// <summary>添加到 JSON 书架（旧版兼容）。</summary>
    internal async Task AddToBookshelfAsync(DownloadTask task)
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

    // ==================== Private Helpers ====================

    /// <summary>启动时为没有封面的书籍自动抓取封面。</summary>
    private async Task AutoFetchMissingCoversAsync()
    {
        try
        {
            var booksNeedCover = DbBooks.Where(b => !b.HasCover && !string.IsNullOrWhiteSpace(b.TocUrl)).ToList();
            if (booksNeedCover.Count == 0) return;

            _parent.StatusMessage = $"正在为 {booksNeedCover.Count} 本书补抓封面…";

            foreach (var book in booksNeedCover)
            {
                try
                {
                    await _coverUseCase.RefreshCoverAsync(book);
                }
                catch
                {
                    // 单本失败不影响其他
                }
            }

            await RefreshDbBooksAsync();
            _parent.StatusMessage = $"封面补抓完成（共 {booksNeedCover.Count} 本）。";
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

            _parent.StatusMessage = $"正在恢复 {incompleteBooks.Count} 个未完成的下载任务…";

            foreach (var book in incompleteBooks)
            {
                if (_parent.SearchDownload.DownloadTasks.Any(t => t.BookId == book.Id
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

                _parent.SearchDownload.QueueDownloadTask(task, searchResult);
            }

            _parent.StatusMessage = $"已恢复 {incompleteBooks.Count} 个未完成的下载任务。";
        }
        catch (Exception ex)
        {
            _parent.StatusMessage = $"恢复下载任务失败：{ex.Message}";
        }
    }

    private void RecalculateProgressLayout()
    {
        var left = Math.Max(0, BookshelfProgressLeftPaddingPx);
        var right = Math.Max(0, BookshelfProgressRightPaddingPx);
        var minWidth = Math.Max(24, BookshelfProgressMinWidthPx);
        var totalWidth = Math.Max(minWidth, BookshelfProgressTotalWidthPx);
        var barGap = Math.Max(0, BookshelfProgressBarToPercentGapPx);
        var tailGap = Math.Max(0, BookshelfProgressPercentTailGapPx);

        BookshelfProgressPadding = new Thickness(left, 0, right, 0);
        BookshelfProgressEffectiveWidth = totalWidth;
        BookshelfProgressBarMargin = new Thickness(0, 0, barGap, 0);
        BookshelfProgressPercentMargin = new Thickness(0, 0, tailGap, 0);
    }
}
