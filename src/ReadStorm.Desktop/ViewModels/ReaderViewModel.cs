using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReadStorm.Application.Abstractions;
using ReadStorm.Application.Services;
using ReadStorm.Domain.Models;
using ReadStorm.Infrastructure.Services;

namespace ReadStorm.Desktop.ViewModels;

public sealed record ReaderChapterItem(int IndexNo, string Title, string DisplayTitle);

public sealed partial class ReaderViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _parent;
    private readonly IBookRepository _bookRepo;
    private readonly IDownloadBookUseCase _downloadBookUseCase;
    private readonly IReaderAutoDownloadPlanner _autoDownloadPlanner;
    private readonly ICoverUseCase _coverUseCase;
    private readonly IBookshelfUseCase _bookshelfUseCase;

    private List<(string Title, string Content)> _currentBookChapters = new();
    private bool _suppressReaderIndexChangedNavigation;
    private bool _isApplyingPersistedState;
    private CancellationTokenSource? _readingStateSaveCts;
    private CancellationTokenSource? _autoPrefetchCts;
    private bool _pendingRefreshWhenTocClosed;
    private DateTimeOffset _lastForceCurrentPrefetchAt = DateTimeOffset.MinValue;

    public ReaderViewModel(
        MainWindowViewModel parent,
        IBookRepository bookRepo,
        IDownloadBookUseCase downloadBookUseCase,
        IReaderAutoDownloadPlanner autoDownloadPlanner,
        ICoverUseCase coverUseCase,
        IBookshelfUseCase bookshelfUseCase)
    {
        _parent = parent;
        _bookRepo = bookRepo;
        _downloadBookUseCase = downloadBookUseCase;
        _autoDownloadPlanner = autoDownloadPlanner;
        _coverUseCase = coverUseCase;
        _bookshelfUseCase = bookshelfUseCase;
        RecalculateReaderInsets();
    }

    // ==================== Static Fields ====================

    private const int MaxChapterTitleLength = 50;
    private const double EffectiveLineHeightFactor = 1.12;
    private const double DesktopBottomOverlayReservePx = 28;
    private const int MinCharsPerLine = 6;
    private const int DefaultAutoPrefetchBatchSize = 10;
    private const int DefaultAutoPrefetchLowWatermark = 4;

    private static readonly Regex ChineseChapterRegex =
        new(@"^第[一二三四五六七八九十百千\d]+[章节回]", RegexOptions.Compiled);

    private static readonly Regex EnglishChapterRegex =
        new(@"^Chapter\s+\d+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

    // ==================== Observable Properties ====================

    [ObservableProperty]
    private string readerTitle = string.Empty;

    [ObservableProperty]
    private string? selectedReaderChapter;

    public string CurrentChapterTitleDisplay =>
        ReaderCurrentChapterIndex >= 0 && ReaderCurrentChapterIndex < _currentBookChapters.Count
            ? _currentBookChapters[ReaderCurrentChapterIndex].Title
            : string.Empty;

    [ObservableProperty]
    private bool isTocOverlayVisible;

    [ObservableProperty]
    private bool isBookmarkPanelVisible;

    [ObservableProperty]
    private int readerScrollVersion;

    [ObservableProperty]
    private int tocColumnCount = 3;

    [ObservableProperty]
    private string readerContent = string.Empty;

    [ObservableProperty]
    private int readerCurrentChapterIndex;

    /// <summary>阅读进度显示文案，如「第 120/500 章 (24%)」。</summary>
    [ObservableProperty]
    private string readerProgressDisplay = string.Empty;

    /// <summary>分页进度显示文案，如「第 3/15 页」。</summary>
    [ObservableProperty]
    private string pageProgressDisplay = string.Empty;

    [ObservableProperty]
    private BookRecord? selectedBookshelfItem;

    [ObservableProperty]
    private BookEntity? selectedDbBook;

    [ObservableProperty]
    private bool isCoverPickerVisible;

    [ObservableProperty]
    private bool isLoadingCoverCandidates;

    [ObservableProperty]
    private CoverCandidate? selectedCoverCandidate;

    [ObservableProperty]
    private bool isSourceSwitching;

    [ObservableProperty]
    private SourceItem? selectedSwitchSource;

    [ObservableProperty]
    private double readerFontSize = 31;

    [ObservableProperty]
    private string selectedFontName = "默认";

    [ObservableProperty]
    private FontFamily readerFontFamily = FontFamily.Default;

    [ObservableProperty]
    private double readerLineHeight = 42;

    [ObservableProperty]
    private double readerParagraphSpacing = 22;

    [ObservableProperty]
    private Avalonia.Thickness paragraphMargin = new(0, 0, 0, 12);

    [ObservableProperty]
    private string readerBackground = "#FFFFFF";

    [ObservableProperty]
    private string readerForeground = "#1F2937";

    [ObservableProperty]
    private bool isDarkMode;

    /// <summary>Android 阅读页是否扩展到刘海/状态栏区域。</summary>
    [ObservableProperty]
    private bool readerExtendIntoCutout;

    /// <summary>是否启用音量键翻页（下键下一页，上键上一页）。</summary>
    [ObservableProperty]
    private bool readerUseVolumeKeyPaging;

    /// <summary>是否启用阅读手势翻页（左滑右滑，默认关闭）。</summary>
    [ObservableProperty]
    private bool readerUseSwipePaging;

    /// <summary>阅读沉浸模式下是否隐藏系统状态栏（时间/电量图标）。</summary>
    [ObservableProperty]
    private bool readerHideSystemStatusBar = true;

    /// <summary>是否启用阅读自动预取。</summary>
    [ObservableProperty]
    private bool readerAutoPrefetchEnabled = true;

    /// <summary>自动预取窗口章节数。</summary>
    [ObservableProperty]
    private int readerPrefetchBatchSize = DefaultAutoPrefetchBatchSize;

    /// <summary>自动预取低水位阈值（连续可读章节数）。</summary>
    [ObservableProperty]
    private int readerPrefetchLowWatermark = DefaultAutoPrefetchLowWatermark;

    /// <summary>阅读正文区域外边距（Android 端可用于刘海留白）。</summary>
    [ObservableProperty]
    private Thickness readerContentMargin = new(12, 4, 12, 0);

    /// <summary>阅读顶部工具栏内边距（Android 端可用于刘海留白）。</summary>
    [ObservableProperty]
    private Thickness readerTopToolbarPadding = new(8, 6, 8, 6);

    /// <summary>阅读区域最大宽度（px），用户可自行调整。</summary>
    [ObservableProperty]
    private double readerContentMaxWidth = 860;

    /// <summary>阅读正文顶部预留（px）。</summary>
    [ObservableProperty]
    private double readerTopReservePx = 4;

    /// <summary>阅读正文底部预留（px）。</summary>
    [ObservableProperty]
    private double readerBottomReservePx = 0;

    /// <summary>分页计算时底部状态栏保守预留（px）。</summary>
    [ObservableProperty]
    private double readerBottomStatusBarReservePx = 0;

    /// <summary>分页估算时额外横向安全预留（px），用于避免右侧裁字；数值越大每行字数越少。</summary>
    [ObservableProperty]
    private double readerHorizontalInnerReservePx = 0;

    /// <summary>阅读正文左右边距（px），同时影响可视宽度与分页估算。</summary>
    [ObservableProperty]
    private double readerSidePaddingPx = 12;

    // ==================== Computed Properties ====================

    /// <summary>当前书籍中已完成的章节数（不以"（"开头的视为已完成）。</summary>
    public int CurrentBookDoneCount =>
        _currentBookChapters.Count(c => !c.Content.StartsWith("（"));

    public string BookmarkToggleText => IsCurrentPageBookmarked ? "取消书签" : "添加书签";

    public string BookmarkButtonBackground => IsCurrentPageBookmarked ? "#DBEAFE" : "#FFFFFF";

    public string BookmarkButtonForeground => IsCurrentPageBookmarked ? "#1D4ED8" : "#111827";

    public string BookmarkToolbarForeground => IsCurrentPageBookmarked ? "#60A5FA" : "White";

    // ==================== 分页模型 ====================

    /// <summary>每页可容纳的完整行数（按 floor 截断，不显示半行）。</summary>
    private int _linesPerPage = 20;

    /// <summary>当前排版下用于保守分页的有效行高。</summary>
    private double _effectiveLineHeight = 30;

    /// <summary>当前章节按行容量切分后的页面列表，每页为一组段落文本。</summary>
    private List<List<string>> _chapterPages = [];

    /// <summary>当前页索引（0-based）。</summary>
    [ObservableProperty]
    private int currentPageIndex;

    /// <summary>当前章节总页数。</summary>
    [ObservableProperty]
    private int totalPages;

    [ObservableProperty]
    private bool isCurrentPageBookmarked;

    /// <summary>可用视口高度（由 View 在 SizeChanged 时设置）。</summary>
    private double _viewportHeight;

    /// <summary>可用视口宽度（由 View 在 SizeChanged 时设置）。</summary>
    private double _viewportWidth;

    private CancellationTokenSource? _viewportRebuildCts;
    private const int ViewportRebuildDebounceMs = 500;

    // ==================== Collections ====================

    public ObservableCollection<string> ReaderParagraphs { get; } = [];
    public ObservableCollection<ReaderChapterItem> ReaderChapters { get; } = [];
    public ObservableCollection<ReadingBookmarkEntity> ReaderBookmarks { get; } = [];
    public ObservableCollection<CoverCandidate> CoverCandidates { get; } = [];
    public ObservableCollection<SourceItem> SortedSwitchSources { get; } = [];

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

    public ObservableCollection<PaperPreset> PaperPresets { get; } =
    [
        new("白纸", "#FFFFFF", "#1F2937"),
        new("护眼绿", "#C7EDCC", "#2D3A2E"),
        new("羊皮纸", "#F5E6C8", "#3E2723"),
        new("浅灰", "#F0F0F0", "#333333"),
        new("暖黄", "#FDF6E3", "#544D3C"),
        new("夜间", "#1A1A2E", "#E0E0E0"),
    ];

    // ==================== Commands ====================

    [RelayCommand]
    private void ToggleTocOverlay()
    {
        IsTocOverlayVisible = !IsTocOverlayVisible;
        if (IsTocOverlayVisible)
            IsBookmarkPanelVisible = false;
    }

    [RelayCommand]
    private void ShowTocPanel()
    {
        IsTocOverlayVisible = true;
        IsBookmarkPanelVisible = false;
    }

    [RelayCommand]
    private void ShowBookmarkPanel()
    {
        IsTocOverlayVisible = true;
        IsBookmarkPanelVisible = true;
    }

    [RelayCommand]
    private async Task ToggleBookmarkAsync()
    {
        try
        {
            if (SelectedDbBook is null || _currentBookChapters.Count == 0 || TotalPages <= 0)
                return;

            var chapterIndex = ReaderCurrentChapterIndex;
            var pageIndex = CurrentPageIndex;
            var existing = ReaderBookmarks.FirstOrDefault(b =>
                b.ChapterIndex == chapterIndex && b.PageIndex == pageIndex);

            if (existing is not null)
            {
                await _bookRepo.DeleteReadingBookmarkAsync(SelectedDbBook.Id, chapterIndex, pageIndex);
                ReaderBookmarks.Remove(existing);
                IsCurrentPageBookmarked = false;
                _parent.StatusMessage = $"已删除书签：第 {chapterIndex + 1} 章 第 {pageIndex + 1} 页";
                return;
            }

            var chapterTitle = chapterIndex >= 0 && chapterIndex < _currentBookChapters.Count
                ? _currentBookChapters[chapterIndex].Title
                : string.Empty;
            var preview = BuildCurrentPagePreviewText();
            var anchor = BuildCurrentPageAnchorText();

            var bookmark = new ReadingBookmarkEntity
            {
                BookId = SelectedDbBook.Id,
                ChapterIndex = chapterIndex,
                PageIndex = pageIndex,
                ChapterTitle = chapterTitle,
                PreviewText = preview,
                AnchorText = anchor,
                CreatedAt = DateTimeOffset.Now.ToString("o"),
            };

            await _bookRepo.UpsertReadingBookmarkAsync(bookmark);
            ReaderBookmarks.Insert(0, bookmark);
            IsCurrentPageBookmarked = true;
            _parent.StatusMessage = $"已添加书签：第 {chapterIndex + 1} 章 第 {pageIndex + 1} 页";
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Reader.ToggleBookmark", ex);
            _parent.StatusMessage = $"书签操作失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task JumpToBookmarkAsync(ReadingBookmarkEntity? bookmark)
    {
        try
        {
            if (bookmark is null || bookmark.ChapterIndex < 0 || bookmark.ChapterIndex >= _currentBookChapters.Count)
                return;

            await NavigateToChapterAsync(bookmark.ChapterIndex);
            CurrentPageIndex = Math.Clamp(bookmark.PageIndex, 0, Math.Max(0, TotalPages - 1));

            if (!string.IsNullOrWhiteSpace(bookmark.AnchorText))
            {
                var anchorPage = FindPageByAnchorText(bookmark.AnchorText);
                if (anchorPage >= 0)
                    CurrentPageIndex = anchorPage;
            }

            ShowCurrentPage();
            IsTocOverlayVisible = false;
            _parent.StatusMessage = $"已跳转书签：第 {bookmark.ChapterIndex + 1} 章 第 {CurrentPageIndex + 1} 页";
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Reader.JumpToBookmark", ex);
            _parent.StatusMessage = $"跳转书签失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RemoveBookmarkAsync(ReadingBookmarkEntity? bookmark)
    {
        try
        {
            if (bookmark is null || SelectedDbBook is null)
                return;

            await _bookRepo.DeleteReadingBookmarkAsync(SelectedDbBook.Id, bookmark.ChapterIndex, bookmark.PageIndex);
            ReaderBookmarks.Remove(bookmark);
            RefreshCurrentPageBookmarkFlag();
            _parent.StatusMessage = "已删除书签。";
        }
        catch (Exception ex)
        {
            AppLogger.Warn("Reader.RemoveBookmark", ex);
            _parent.StatusMessage = $"删除书签失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RefreshReaderAsync()
    {
        if (SelectedDbBook is null) { _parent.StatusMessage = "当前未打开 DB 书籍，无法刷新。"; return; }
        await RefreshCurrentDbBookAsync(silent: false);
    }

    internal async Task RefreshCurrentBookFromDownloadSignalAsync(string? bookId)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            await Dispatcher.UIThread.InvokeAsync(() => RefreshCurrentBookFromDownloadSignalAsync(bookId));
            return;
        }

        if (SelectedDbBook is null || string.IsNullOrWhiteSpace(bookId))
        {
            return;
        }

        if (!string.Equals(SelectedDbBook.Id, bookId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        EnsureValidCurrentChapterIndex();

        if (IsTocOverlayVisible)
        {
            _pendingRefreshWhenTocClosed = true;
            return;
        }

        if (ShouldDoFullRefreshFromSignal())
        {
            await RefreshCurrentDbBookAsync(silent: true);
            return;
        }

        if (!ShouldRefreshCurrentChapterFromSignal())
        {
            return;
        }

        if (ReaderAutoPrefetchEnabled && ShouldForceCurrentPrefetchNow())
        {
            QueueAutoPrefetch("foreground-direct");
        }

        await RefreshCurrentChapterOnlyFromDownloadSignalAsync();
    }

    [RelayCommand]
    private async Task PreviousChapterAsync()
    {
        if (ReaderCurrentChapterIndex > 0)
        {
            await NavigateToChapterAsync(ReaderCurrentChapterIndex - 1);
        }
    }

    [RelayCommand]
    private async Task NextChapterAsync()
    {
        if (ReaderCurrentChapterIndex < _currentBookChapters.Count - 1)
        {
            await NavigateToChapterAsync(ReaderCurrentChapterIndex + 1);
        }
    }

    /// <summary>翻到下一页；若当前章节已到末页，则自动进入下一章首页。</summary>
    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (CurrentPageIndex < TotalPages - 1)
        {
            CurrentPageIndex++;
            ShowCurrentPage();
        }
        else if (ReaderCurrentChapterIndex < _currentBookChapters.Count - 1)
        {
            // 跨章节：进入下一章首页
            await NavigateToChapterAsync(ReaderCurrentChapterIndex + 1, goToLastPage: false);
        }
    }

    /// <summary>翻到上一页；若当前章节已到首页，则自动回到上一章末页。</summary>
    [RelayCommand]
    private async Task PreviousPageAsync()
    {
        if (CurrentPageIndex > 0)
        {
            CurrentPageIndex--;
            ShowCurrentPage();
        }
        else if (ReaderCurrentChapterIndex > 0)
        {
            // 跨章节：回到上一章末页
            await NavigateToChapterAsync(ReaderCurrentChapterIndex - 1, goToLastPage: true);
        }
    }

    [RelayCommand]
    private async Task SelectTocChapterAsync(int index)
    {
        if (index >= 0 && index < _currentBookChapters.Count)
        {
            await NavigateToChapterAsync(index, prefetchReason: "manual-priority");
        }
    }

    [RelayCommand]
    private void OpenBookWebPage()
    {
        var url = SelectedDbBook?.TocUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            _parent.StatusMessage = "当前书籍没有关联的网页地址。";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
            _parent.StatusMessage = $"已打开网页：{url}";
        }
        catch (Exception ex)
        {
            _parent.StatusMessage = $"打开网页失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task OpenCoverPickerAsync()
    {
        if (SelectedDbBook is null) { _parent.StatusMessage = "请先在书架中打开一本书，再选择封面。"; return; }
        try
        {
            IsLoadingCoverCandidates = true;
            IsCoverPickerVisible = true;
            CoverCandidates.Clear();
            SelectedCoverCandidate = null;
            var candidates = await _coverUseCase.GetCoverCandidatesAsync(SelectedDbBook);
            foreach (var candidate in candidates) CoverCandidates.Add(candidate);
            _parent.StatusMessage = CoverCandidates.Count > 0
                ? $"已获取 {CoverCandidates.Count} 个封面候选，请选择。"
                : "未找到候选图片，可尝试点击原始网页确认页面结构。";
        }
        catch (Exception ex)
        {
            IsCoverPickerVisible = false;
            _parent.StatusMessage = $"获取封面候选失败：{ex.Message}";
        }
        finally { IsLoadingCoverCandidates = false; }
    }

    [RelayCommand]
    private async Task ApplySelectedCoverAsync(CoverCandidate? candidate)
    {
        if (SelectedDbBook is null) { _parent.StatusMessage = "当前没有打开的书籍。"; return; }
        candidate ??= SelectedCoverCandidate;
        if (candidate is null) { _parent.StatusMessage = "请先选择一张封面图。"; return; }

        try
        {
            var result = await _coverUseCase.ApplyCoverCandidateAsync(SelectedDbBook, candidate);
            SelectedDbBook.CoverUrl = candidate.ImageUrl;
            var refreshed = await _bookRepo.GetBookAsync(SelectedDbBook.Id);
            if (refreshed is not null)
            {
                SelectedDbBook = refreshed;
                _parent.Bookshelf.ReplaceDbBookInList(refreshed);
            }
            await _parent.Bookshelf.RefreshDbBooksAsync();
            _parent.StatusMessage = result;
        }
        catch (Exception ex) { _parent.StatusMessage = $"设置封面失败：{ex.Message}"; }
    }

    [RelayCommand]
    private void CloseCoverPicker()
    {
        IsCoverPickerVisible = false;
    }

    [RelayCommand]
    private void ApplyPaperPreset(PaperPreset? preset)
    {
        if (preset is null) return;
        ReaderBackground = preset.Background;
        ReaderForeground = preset.Foreground;
        IsDarkMode = preset.Name == "夜间";
    }

    [RelayCommand]
    private async Task OpenBookAsync(BookRecord? book)
    {
        if (book is null || string.IsNullOrWhiteSpace(book.FilePath))
        {
            _parent.StatusMessage = "无法打开：文件路径为空。";
            return;
        }

        if (!File.Exists(book.FilePath))
        {
            _parent.StatusMessage = $"文件不存在：{book.FilePath}";
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
                    ReaderChapters.Add(new ReaderChapterItem(ReaderChapters.Count, ch.Title, ch.Title));
                }

                book.TotalChapters = chapters.Count;
                _currentBookChapters = chapters;

                if (chapters.Count > 0)
                {
                    var startIndex = Math.Clamp(book.Progress.CurrentChapterIndex, 0, chapters.Count - 1);
                    ReaderCurrentChapterIndex = startIndex;
                    ReaderContent = chapters[startIndex].Content;
                    SelectedReaderChapter = ReaderChapters[startIndex].Title;
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
            SelectedDbBook = null;
            _parent.StatusMessage = $"已打开：《{book.Title}》，共 {ReaderChapters.Count} 章";
        }
        catch (Exception ex)
        {
            _parent.StatusMessage = $"打开失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RemoveBookAsync(BookRecord? book)
    {
        if (book is null) return;

        try
        {
            await _bookshelfUseCase.RemoveAsync(book.Id);
            _parent.StatusMessage = $"已从书架移除：《{book.Title}》";
        }
        catch (Exception ex)
        {
            _parent.StatusMessage = $"移除失败：{ex.Message}";
        }
    }

    // ==================== Public Methods ====================

    /// <summary>从书架打开 DB 书籍（由 BookshelfVM 调用）。</summary>
    public async Task OpenBookAsync(BookEntity book)
    {
        ReaderTitle = $"《{book.Title}》- {book.Author}";
        await LoadDbBookChaptersAsync(book);
        SelectedDbBook = book;
        SelectedBookshelfItem = null;
        await LoadBookmarksAsync(book.Id);
        await RestoreReadingStateAsync(book.Id);
        RefreshSortedSwitchSources();
        UpdateProgressDisplay();
        RefreshCurrentPageBookmarkFlag();
        QueueAutoPrefetch("open");
    }

    // ==================== Internal Methods ====================

    internal async Task LoadDbBookChaptersAsync(BookEntity book, bool preserveCurrentSelection = false)
    {
        ReaderChapters.Clear();
        _currentBookChapters.Clear();

        var chapters = await _bookRepo.GetChaptersAsync(book.Id);
        foreach (var ch in chapters)
        {
            ReaderChapters.Add(new ReaderChapterItem(ch.IndexNo, ch.Title, $"{BuildChapterStatusTag(ch.Status)}{ch.Title}"));
            var displayContent = BuildChapterDisplayContent(ch);
            _currentBookChapters.Add((ch.Title, displayContent));
        }

        if (chapters.Count > 0)
        {
            var preferred = Math.Clamp(book.ReadChapterIndex, 0, chapters.Count - 1);
            var startIndex = preferred;

            if (preserveCurrentSelection
                && ReaderCurrentChapterIndex >= 0
                && ReaderCurrentChapterIndex < chapters.Count)
            {
                startIndex = ReaderCurrentChapterIndex;
            }

            _suppressReaderIndexChangedNavigation = true;
            ReaderCurrentChapterIndex = startIndex;
            _suppressReaderIndexChangedNavigation = false;
            ReaderContent = _currentBookChapters[startIndex].Content;
            SelectedReaderChapter = ReaderChapters[startIndex].Title;
        }
        else
        {
            ReaderContent = "（章节目录尚未加载，请等待下载开始或点击「续传」）";
        }
    }

    private static string BuildChapterStatusTag(ChapterStatus status)
    {
        return status switch
        {
            ChapterStatus.Done => string.Empty,
            ChapterStatus.Failed => "❌ ",
            ChapterStatus.Downloading => "⏳ ",
            _ => "⬜ ",
        };
    }

    private static string BuildChapterDisplayContent(ChapterEntity chapter)
    {
        return chapter.Status switch
        {
            ChapterStatus.Done => chapter.Content ?? string.Empty,
            ChapterStatus.Failed => $"（下载失败：{chapter.Error}\n\n点击上方「刷新章节」可在重新下载后查看）",
            ChapterStatus.Downloading => "（正在下载中…）",
            _ => "（等待下载）",
        };
    }

    internal void RefreshSortedSwitchSources()
    {
        SortedSwitchSources.Clear();
        var sorted = _parent.Sources
            .Where(s => s.Id > 0 && s.SearchSupported)
            .OrderByDescending(s => s.IsHealthy == true)
            .ThenByDescending(s => s.IsHealthy is null)
            .ThenBy(s => s.Id);
        foreach (var s in sorted)
            SortedSwitchSources.Add(s);
    }

    // ==================== Private Methods ====================

    private async Task NavigateToChapterAsync(int index, bool goToLastPage = false, string? prefetchReason = null)
    {
        if (index < 0 || index >= _currentBookChapters.Count) return;

        var chapterTitle = _currentBookChapters[index].Title;
        var displayContent = _currentBookChapters[index].Content;
        var hasReadyContent = !IsPlaceholderChapterContent(displayContent);

        // 跨章瞬间先做一次快速查库：如果下一章已下载，直接展示正文，避免误走“强制下载当前章”链路。
        if (SelectedDbBook is not null)
        {
            var freshChapter = await _bookRepo.GetChapterAsync(SelectedDbBook.Id, index);
            if (freshChapter is not null)
            {
                chapterTitle = freshChapter.Title;
                displayContent = BuildChapterDisplayContent(freshChapter);
                hasReadyContent = freshChapter.Status == ChapterStatus.Done
                    && !string.IsNullOrWhiteSpace(freshChapter.Content);

                _currentBookChapters[index] = (chapterTitle, displayContent);
                if (index < ReaderChapters.Count)
                {
                    ReaderChapters[index] = new ReaderChapterItem(index, chapterTitle, $"{BuildChapterStatusTag(freshChapter.Status)}{chapterTitle}");
                }
            }
        }

        _suppressReaderIndexChangedNavigation = true;
        ReaderCurrentChapterIndex = index;
        _suppressReaderIndexChangedNavigation = false;
        ReaderContent = displayContent;
        SelectedReaderChapter = ReaderChapters.Count > index ? ReaderChapters[index].Title : null;
        ReaderScrollVersion++;
        IsTocOverlayVisible = false;

        // 分页：重新计算当前章节的页面
        RebuildChapterPages();
        CurrentPageIndex = goToLastPage ? Math.Max(0, TotalPages - 1) : 0;
        ShowCurrentPage();

        if (SelectedDbBook is not null)
        {
            try
            {
                await _bookRepo.UpdateReadProgressAsync(
                    SelectedDbBook.Id, index, chapterTitle);
                SelectedDbBook.ReadChapterIndex = index;
                SelectedDbBook.ReadChapterTitle = chapterTitle;
                _parent.Bookshelf.MarkBookshelfDirty();
            }
            catch (Exception ex) { AppLogger.Warn("Reader.SaveDbProgress", ex); }

            // 已有正文：只做温和窗口预取；无正文：继续走高优先触发下载当前/后续。
            if (hasReadyContent)
            {
                QueueAutoPrefetch("low-watermark");
            }
            else
            {
                QueueAutoPrefetch(string.IsNullOrWhiteSpace(prefetchReason) ? "jump" : prefetchReason);
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
            try { await _bookshelfUseCase.UpdateProgressAsync(SelectedBookshelfItem.Id, progress); }
            catch (Exception ex) { AppLogger.Warn("Reader.SaveBookshelfProgress", ex); }
        }
    }

    private async Task RefreshCurrentDbBookAsync(bool silent)
    {
        if (SelectedDbBook is null)
        {
            return;
        }

        var freshBook = await _bookRepo.GetBookAsync(SelectedDbBook.Id);
        if (freshBook is not null)
        {
            SelectedDbBook.DoneChapters = freshBook.DoneChapters;
            SelectedDbBook.TotalChapters = freshBook.TotalChapters;
        }

        var savedChapterIndex = ReaderCurrentChapterIndex;
        var savedPageIndex = CurrentPageIndex;

        await LoadDbBookChaptersAsync(SelectedDbBook, preserveCurrentSelection: true);

        if (savedChapterIndex >= 0 && savedChapterIndex < _currentBookChapters.Count)
        {
            _suppressReaderIndexChangedNavigation = true;
            ReaderCurrentChapterIndex = savedChapterIndex;
            _suppressReaderIndexChangedNavigation = false;

            ReaderContent = _currentBookChapters[savedChapterIndex].Content;

            SelectedReaderChapter = ReaderChapters[savedChapterIndex].Title;

            RebuildChapterPages();
            CurrentPageIndex = Math.Clamp(savedPageIndex, 0, Math.Max(0, TotalPages - 1));
            ShowCurrentPage();
        }

        UpdateProgressDisplay();
        RefreshCurrentPageBookmarkFlag();

        if (!silent)
        {
            var doneCount = CurrentBookDoneCount;
            _parent.StatusMessage = $"已刷新：《{SelectedDbBook.Title}》，{doneCount}/{SelectedDbBook.TotalChapters} 章可读";
        }
    }

    private async Task RefreshCurrentChapterOnlyFromDownloadSignalAsync()
    {
        if (SelectedDbBook is null)
        {
            return;
        }

        EnsureValidCurrentChapterIndex();

        var chapterIndex = ReaderCurrentChapterIndex;
        if (chapterIndex < 0 || chapterIndex >= _currentBookChapters.Count)
        {
            return;
        }

        var wasPlaceholder = IsPlaceholderChapterContent(_currentBookChapters[chapterIndex].Content);

        var freshBook = await _bookRepo.GetBookAsync(SelectedDbBook.Id);
        if (freshBook is not null)
        {
            SelectedDbBook.DoneChapters = freshBook.DoneChapters;
            SelectedDbBook.TotalChapters = freshBook.TotalChapters;
        }

        var chapter = await _bookRepo.GetChapterAsync(SelectedDbBook.Id, chapterIndex);
        if (chapter is null)
        {
            return;
        }

        var displayContent = BuildChapterDisplayContent(chapter);
        _currentBookChapters[chapterIndex] = (chapter.Title, displayContent);

        if (chapterIndex < ReaderChapters.Count)
        {
            ReaderChapters[chapterIndex] = new ReaderChapterItem(chapterIndex, chapter.Title, $"{BuildChapterStatusTag(chapter.Status)}{chapter.Title}");

            if (chapterIndex == ReaderCurrentChapterIndex)
            {
                SelectedReaderChapter = ReaderChapters[chapterIndex].Title;
            }
        }

        ReaderContent = displayContent;
        RebuildChapterPages();
        CurrentPageIndex = Math.Clamp(CurrentPageIndex, 0, Math.Max(0, TotalPages - 1));
        ShowCurrentPage();
        UpdateProgressDisplay();

        // 当前章节由“等待/下载中/失败占位”转为可读后，立刻接力触发窗口预取。
        // 这样可以在“先保当前章可读”完成后，继续下载后续章节。
        if (ReaderAutoPrefetchEnabled
            && wasPlaceholder
            && chapter.Status == ChapterStatus.Done)
        {
            // 接力阶段强制从当前章节窗口继续下载，避免被 gap-fill 分支抢占到历史缺口。
            QueueAutoPrefetch("foreground-direct");
        }
    }

    private bool ShouldRefreshCurrentChapterFromSignal()
    {
        if (ReaderCurrentChapterIndex < 0 || ReaderCurrentChapterIndex >= _currentBookChapters.Count)
        {
            return false;
        }

        var currentContent = _currentBookChapters[ReaderCurrentChapterIndex].Content;
        return IsPlaceholderChapterContent(currentContent);
    }

    internal bool NeedsCurrentChapterForegroundRefresh()
    {
        return ShouldRefreshCurrentChapterFromSignal();
    }

    private bool ShouldForceCurrentPrefetchNow()
    {
        if (SelectedDbBook is null)
        {
            return false;
        }

        if (ReaderCurrentChapterIndex < 0 || ReaderCurrentChapterIndex >= _currentBookChapters.Count)
        {
            return false;
        }

        var now = DateTimeOffset.Now;
        if ((now - _lastForceCurrentPrefetchAt) < TimeSpan.FromSeconds(1))
        {
            return false;
        }

        var hasActiveCoveringTask = _parent.SearchDownload.DownloadTasks.Any(t =>
            (
                (!string.IsNullOrWhiteSpace(t.BookId)
                    && string.Equals(t.BookId, SelectedDbBook.Id, StringComparison.OrdinalIgnoreCase))
                || (string.IsNullOrWhiteSpace(t.BookId)
                    && string.Equals(t.BookTitle, SelectedDbBook.Title, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(t.Author, SelectedDbBook.Author, StringComparison.OrdinalIgnoreCase))
            )
            && t.CurrentStatus is DownloadTaskStatus.Queued or DownloadTaskStatus.Downloading
            && t.Mode == DownloadMode.Range
            && t.RangeStartIndex.HasValue
            && t.RangeTakeCount.HasValue
            && ReaderCurrentChapterIndex >= t.RangeStartIndex.Value
            && ReaderCurrentChapterIndex < (t.RangeStartIndex.Value + Math.Max(1, t.RangeTakeCount.Value)));

        if (hasActiveCoveringTask)
        {
            return false;
        }

        _lastForceCurrentPrefetchAt = now;
        return true;
    }

    private static bool IsPlaceholderChapterContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return true;
        }

        return content.StartsWith("（等待下载）", StringComparison.Ordinal)
            || content.StartsWith("（正在下载中", StringComparison.Ordinal)
            || content.StartsWith("（下载失败：", StringComparison.Ordinal);
    }

    private bool ShouldDoFullRefreshFromSignal()
    {
        if (_currentBookChapters.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(ReaderContent))
        {
            return true;
        }

        return ReaderContent.StartsWith("（章节目录尚未加载", StringComparison.Ordinal);
    }

    private void QueueAutoPrefetch(string trigger)
    {
        if (SelectedDbBook is null || !ReaderAutoPrefetchEnabled)
            return;

        AppLogger.Info("Reader.AutoPrefetch.Queue", $"bookId={SelectedDbBook.Id}, trigger={trigger}, chapter={ReaderCurrentChapterIndex + 1}, batch={Math.Max(1, ReaderPrefetchBatchSize)}, lowWatermark={Math.Max(1, ReaderPrefetchLowWatermark)}");

        _autoPrefetchCts?.Cancel();
        _autoPrefetchCts?.Dispose();

        var cts = new CancellationTokenSource();
        _autoPrefetchCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(350, cts.Token);
                await ExecuteAutoPrefetchAsync(trigger, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // 防抖取消，忽略
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Reader.AutoPrefetch", ex);
            }
        });
    }

    private async Task ExecuteAutoPrefetchAsync(string trigger, CancellationToken cancellationToken)
    {
        var book = SelectedDbBook;
        if (book is null)
            return;

        if (string.Equals(trigger, "foreground-direct", StringComparison.OrdinalIgnoreCase))
        {
            var currentIndex = Math.Clamp(ReaderCurrentChapterIndex, 0, Math.Max(0, _currentBookChapters.Count - 1));
            var take = Math.Max(1, ReaderPrefetchBatchSize);
            await _parent.SearchDownload.QueueOrReplaceAutoPrefetchAsync(
                book,
                currentIndex,
                take,
                "foreground-direct");
            AppLogger.Warn("Reader.AutoPrefetch.Dispatch", $"bookId={book.Id}, reason=foreground-direct, chapter={currentIndex + 1}, take={take}");
            return;
        }

        var plan = await _autoDownloadPlanner.BuildPlanAsync(
            book.Id,
            ReaderCurrentChapterIndex,
            Math.Max(1, ReaderPrefetchBatchSize),
            Math.Max(1, ReaderPrefetchLowWatermark),
            cancellationToken);

        var shouldQueueWindow = ReaderAutoPrefetchPolicy.ShouldQueueWindow(plan, trigger);
        AppLogger.Info(
            "Reader.AutoPrefetch.Plan",
            $"bookId={book.Id}, trigger={trigger}, chapter={ReaderCurrentChapterIndex + 1}, shouldQueueWindow={shouldQueueWindow}, planWindow={plan.ShouldQueueWindow}, windowStart={plan.WindowStartIndex + 1}, windowTake={plan.WindowTakeCount}, gap={plan.HasGap}, firstGap={(plan.FirstGapIndex >= 0 ? (plan.FirstGapIndex + 1).ToString() : "none")}, doneAfterAnchor={plan.ConsecutiveDoneAfterAnchor}");

        if (shouldQueueWindow)
        {
            await _parent.SearchDownload.QueueOrReplaceAutoPrefetchAsync(
                book,
                plan.WindowStartIndex,
                plan.WindowTakeCount,
                trigger);
            AppLogger.Info("Reader.AutoPrefetch.Dispatch", $"bookId={book.Id}, reason={trigger}, mode=window, start={plan.WindowStartIndex + 1}, take={plan.WindowTakeCount}");
            return;
        }

        if (plan.HasGap && plan.FirstGapIndex >= 0)
        {
            await _parent.SearchDownload.QueueOrReplaceAutoPrefetchAsync(
                book,
                plan.FirstGapIndex,
                Math.Max(1, ReaderPrefetchBatchSize),
                "gap-fill");
            AppLogger.Warn("Reader.AutoPrefetch.Dispatch", $"bookId={book.Id}, reason=gap-fill, trigger={trigger}, start={plan.FirstGapIndex + 1}, take={Math.Max(1, ReaderPrefetchBatchSize)}");
        }
        else
        {
            AppLogger.Info("Reader.AutoPrefetch.Dispatch", $"bookId={book.Id}, trigger={trigger}, action=skip(no-window-no-gap)");
        }
    }

    private void RebuildParagraphs()
    {
        ReaderParagraphs.Clear();
        if (string.IsNullOrEmpty(ReaderContent)) return;

        // 分页模式：由 RebuildChapterPages + ShowCurrentPage 驱动，
        // RebuildParagraphs 仅在初始加载（视口尺寸未知时）提供 fallback。
        if (_chapterPages.Count > 0) return;

        var lines = BuildDisplayLinesForCurrentChapter(EstimateCharsPerLine(), includeHeader: true);
        foreach (var line in lines)
            ReaderParagraphs.Add(line);
    }

    /// <summary>
    /// 由 View 在视口尺寸变化时调用，触发分页重算。
    /// </summary>
    public void UpdateViewportSize(double viewportWidth, double viewportHeight)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0) return;

        // 首次拿到有效视口时立即计算；之后在窗口连续变化时做 500ms 防抖重排。
        var isFirstValidViewport = _viewportWidth <= 0 || _viewportHeight <= 0;
        if (isFirstValidViewport)
        {
            ApplyViewportSizeAndRepaginate(viewportWidth, viewportHeight);
            return;
        }

        if (Math.Abs(_viewportWidth - viewportWidth) < 1 && Math.Abs(_viewportHeight - viewportHeight) < 1)
            return;

        _viewportWidth = viewportWidth;
        _viewportHeight = viewportHeight;

        _viewportRebuildCts?.Cancel();
        var cts = new CancellationTokenSource();
        _viewportRebuildCts = cts;

        _ = DebouncedViewportRebuildAsync(cts.Token);
    }

    private async Task DebouncedViewportRebuildAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(ViewportRebuildDebounceMs, token);
            if (token.IsCancellationRequested) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                RecalculateLinesPerPage();
                if (_currentBookChapters.Count > 0 && !string.IsNullOrEmpty(ReaderContent))
                {
                    var savedPage = CurrentPageIndex;
                    RebuildChapterPages();
                    CurrentPageIndex = Math.Clamp(savedPage, 0, Math.Max(0, TotalPages - 1));
                    ShowCurrentPage();
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Ignore debounce cancel.
        }
    }

    private void ApplyViewportSizeAndRepaginate(double viewportWidth, double viewportHeight)
    {
        if (Math.Abs(_viewportWidth - viewportWidth) < 1 && Math.Abs(_viewportHeight - viewportHeight) < 1)
            return;

        _viewportWidth = viewportWidth;
        _viewportHeight = viewportHeight;
        RecalculateLinesPerPage();

        if (_currentBookChapters.Count > 0 && !string.IsNullOrEmpty(ReaderContent))
        {
            var savedPage = CurrentPageIndex;
            RebuildChapterPages();
            CurrentPageIndex = Math.Clamp(savedPage, 0, Math.Max(0, TotalPages - 1));
            ShowCurrentPage();
        }
    }

    /// <summary>根据视口和排版参数计算每页完整行数。</summary>
    private void RecalculateLinesPerPage()
    {
        // 视口高度扣除内容区上下边距 + 底部翻页状态栏预留。
        var contentMarginVertical = ReaderContentMargin.Top + ReaderContentMargin.Bottom;
        var bottomOverlayReserve = OperatingSystem.IsAndroid()
            ? Math.Max(0, ReaderBottomStatusBarReservePx)
            : DesktopBottomOverlayReservePx;
        var availableHeight = _viewportHeight - contentMarginVertical - bottomOverlayReserve;
        if (availableHeight <= 0) availableHeight = 400;

        _effectiveLineHeight = Math.Max(
            ReaderLineHeight > 0 ? ReaderLineHeight : 30,
            (ReaderFontSize > 0 ? ReaderFontSize : 15) * EffectiveLineHeightFactor);

        if (availableHeight <= _effectiveLineHeight)
        {
            _linesPerPage = 1;
            return;
        }

        _linesPerPage = CalculateConservativeLineCapacity(availableHeight, _effectiveLineHeight);
    }

    private static int CalculateConservativeLineCapacity(double availableHeight, double lineHeight)
    {
        var lines = 0;
        var used = 0d;

        while (true)
        {
            var nextUsed = used + lineHeight;
            if (nextUsed > availableHeight)
                break;

            lines++;
            used = nextUsed;

            var remaining = availableHeight - used;
            // 1.5 倍规则：剩余高度不足 1.5 行时，停止继续放置下一行。
            if (remaining < 1.5 * lineHeight)
                break;
        }

        return Math.Max(1, lines);
    }

    /// <summary>
    /// 将当前章节内容按行容量分页。
    /// 每章首页结构：标题（居中）+ 空行 + 正文。
    /// </summary>
    private void RebuildChapterPages()
    {
        _chapterPages.Clear();

        if (string.IsNullOrEmpty(ReaderContent) || _linesPerPage <= 0)
        {
            TotalPages = 0;
            return;
        }

        var displayLines = BuildDisplayLinesForCurrentChapter(EstimateCharsPerLine(), includeHeader: true);
        if (displayLines.Count == 0)
        {
            TotalPages = 0;
            return;
        }

        var currentPage = new List<string>();
        var currentLineCount = 0;

        foreach (var line in displayLines)
        {
            if (currentLineCount >= _linesPerPage)
            {
                _chapterPages.Add(currentPage);
                currentPage = [];
                currentLineCount = 0;
            }

            currentPage.Add(line);
            currentLineCount++;
        }

        if (currentPage.Count > 0)
            _chapterPages.Add(currentPage);

        TotalPages = _chapterPages.Count;
        if (TotalPages == 0) TotalPages = 1;
    }

    /// <summary>估算当前排版下每行可容纳的字符数。</summary>
    private int EstimateCharsPerLine()
    {
        // 可用文本宽度 = 视口宽度 - 左右边距 - 内边距约束
        var contentMaxWidth = ReaderContentMaxWidth > 0 ? ReaderContentMaxWidth : 860;
        var availableWidth = Math.Min(_viewportWidth > 0 ? _viewportWidth : 800, contentMaxWidth);
        availableWidth -= ReaderContentMargin.Left + ReaderContentMargin.Right + Math.Max(0, ReaderHorizontalInnerReservePx);
        if (availableWidth < 100) availableWidth = 100;

        // 按汉字宽度估算：不使用标点/拉丁字符平均宽度。
        var hanCharWidth = ReaderFontSize > 0 ? ReaderFontSize : 14;
        var rawChars = (int)Math.Floor(availableWidth / hanCharWidth);
        return Math.Max(MinCharsPerLine, rawChars);
    }

    private readonly record struct ParagraphUnit(string Text, bool IsParagraphBreak);

    private List<string> BuildDisplayLinesForCurrentChapter(int charsPerLine, bool includeHeader)
    {
        var result = new List<string>();

        if (includeHeader)
        {
            var chapterTitle = ReaderCurrentChapterIndex >= 0 && ReaderCurrentChapterIndex < _currentBookChapters.Count
                ? _currentBookChapters[ReaderCurrentChapterIndex].Title
                : null;
            if (!string.IsNullOrWhiteSpace(chapterTitle))
            {
                // 章节首页固定两行：标题 + 空行
                result.Add($"⌜{chapterTitle}⌝");
                result.Add(string.Empty);
            }
        }

        var paragraphs = BuildLogicalParagraphs(ReaderContent);
        var spacingLines = _effectiveLineHeight > 0
            ? Math.Max(0, (int)Math.Round(ReaderParagraphSpacing / _effectiveLineHeight, MidpointRounding.AwayFromZero))
            : 0;

        for (var i = 0; i < paragraphs.Length; i++)
        {
            var unit = paragraphs[i];
            var raw = unit.Text;

            var paragraph = raw.StartsWith("　　", StringComparison.Ordinal)
                ? raw
                : unit.IsParagraphBreak ? $"　　{raw}" : raw;

            foreach (var wrapped in WrapParagraphToLines(paragraph, charsPerLine))
                result.Add(wrapped);

            // 段距：仅在识别为段落边界时追加，避免把源文本软换行当段距来源。
            if (spacingLines > 0 && unit.IsParagraphBreak && i < paragraphs.Length - 1)
            {
                for (var s = 0; s < spacingLines; s++)
                    result.Add(string.Empty);
            }
        }

        return result;
    }

    private static IEnumerable<string> WrapParagraphToLines(string paragraph, int charsPerLine)
    {
        if (string.IsNullOrEmpty(paragraph))
        {
            yield return string.Empty;
            yield break;
        }

        if (charsPerLine <= 0)
        {
            yield return paragraph;
            yield break;
        }

        // 宽度单位模型：全角=1.0，半角=0.5（ASCII 字母/数字/常见半角标点）。
        // 每行以“汉字单位”容量 charsPerLine 为上限。
        var index = 0;
        while (index < paragraph.Length)
        {
            var lineStart = index;
            var usedUnits = 0.0;

            while (index < paragraph.Length)
            {
                var ch = paragraph[index];
                var w = GetCharWidthUnits(ch);
                if (usedUnits + w > charsPerLine)
                {
                    break;
                }

                usedUnits += w;
                index++;
            }

            // 极端情况下首字符超限，至少放 1 个字符，避免死循环。
            if (index == lineStart)
            {
                index++;
            }

            var length = index - lineStart;
            yield return paragraph.Substring(lineStart, length);
        }
    }

    private static double GetCharWidthUnits(char ch)
    {
        // ASCII 可见字符与空白按半角处理
        if (ch <= 0x007F)
            return 0.5;

        // 半角片假名与兼容半角符号
        if (ch is >= '\uFF61' and <= '\uFF9F')
            return 0.5;

        // 其它字符统一按全角处理（含中文、全角标点）
        return 1.0;
    }

    private static ParagraphUnit[] BuildLogicalParagraphs(string content)
    {
        var lines = content.Split('\n');
        var list = new List<ParagraphUnit>();
        var previousWasBlank = true;

        foreach (var raw in lines)
        {
            var line = raw.Trim('\r');

            if (string.IsNullOrWhiteSpace(line))
            {
                previousWasBlank = true;
                continue;
            }

            var trimmed = line.Trim();
            var hasIndent = raw.StartsWith("　　", StringComparison.Ordinal)
                            || (!string.IsNullOrEmpty(raw) && char.IsWhiteSpace(raw[0]));

            // 识别真实段落起点：空行后、显式缩进，或首行。
            var isParagraphBreak = previousWasBlank || hasIndent || list.Count == 0;

            list.Add(new ParagraphUnit(trimmed, isParagraphBreak));
            previousWasBlank = false;
        }

        return list.Count == 0 ? [new ParagraphUnit(string.Empty, true)] : list.ToArray();
    }

    /// <summary>将当前页内容显示到 ReaderParagraphs。</summary>
    private void ShowCurrentPage()
    {
        ReaderParagraphs.Clear();

        if (_chapterPages.Count == 0 || CurrentPageIndex < 0 || CurrentPageIndex >= _chapterPages.Count)
        {
            UpdatePageProgressDisplay();
            RefreshCurrentPageBookmarkFlag();
            return;
        }

        foreach (var para in _chapterPages[CurrentPageIndex])
        {
            ReaderParagraphs.Add(para);
        }

        UpdatePageProgressDisplay();
        RefreshCurrentPageBookmarkFlag();
        QueuePersistReadingState();
    }

    /// <summary>更新分页进度显示。</summary>
    private void UpdatePageProgressDisplay()
    {
        if (TotalPages <= 0)
        {
            PageProgressDisplay = string.Empty;
            return;
        }
        PageProgressDisplay = $"第 {CurrentPageIndex + 1}/{TotalPages} 页";
    }

    private void UpdateProgressDisplay()
    {
        var total = _currentBookChapters.Count;
        if (total <= 0)
        {
            ReaderProgressDisplay = string.Empty;
            return;
        }

        var current = Math.Clamp(ReaderCurrentChapterIndex + 1, 1, total); // 1-based display
        var percent = (int)Math.Round(100.0 * current / total);
        ReaderProgressDisplay = $"第 {current}/{total} 章 ({percent}%)";
    }

    private void EnsureValidCurrentChapterIndex()
    {
        if (_currentBookChapters.Count <= 0)
        {
            return;
        }

        if (ReaderCurrentChapterIndex >= 0 && ReaderCurrentChapterIndex < _currentBookChapters.Count)
        {
            return;
        }

        var recoveredIndex = Math.Clamp(ReaderCurrentChapterIndex, 0, _currentBookChapters.Count - 1);
        _suppressReaderIndexChangedNavigation = true;
        ReaderCurrentChapterIndex = recoveredIndex;
        _suppressReaderIndexChangedNavigation = false;

        SelectedReaderChapter = ReaderChapters.Count > recoveredIndex
            ? ReaderChapters[recoveredIndex].Title
            : null;
        AppLogger.Warn("Reader.IndexRecover", $"Recovered invalid chapter index to {recoveredIndex + 1}/{_currentBookChapters.Count}");
    }

    private async Task LoadBookmarksAsync(string bookId)
    {
        ReaderBookmarks.Clear();
        var list = await _bookRepo.GetReadingBookmarksAsync(bookId);
        foreach (var bookmark in list)
            ReaderBookmarks.Add(bookmark);
    }

    private async Task RestoreReadingStateAsync(string bookId)
    {
        var state = await _bookRepo.GetReadingStateAsync(bookId);
        if (state is null || _currentBookChapters.Count == 0)
            return;

        _isApplyingPersistedState = true;
        try
        {
            // 只恢复页码/锚点，不自动改章节，避免将用户从当前阅读章（如第100章）拉回旧章节（如第1章）。
            var chapterIndex = Math.Clamp(state.ChapterIndex, 0, _currentBookChapters.Count - 1);
            if (chapterIndex != ReaderCurrentChapterIndex)
            {
                AppLogger.Info(
                    "Reader.RestoreReadingState.SkipChapterJump",
                    $"bookId={bookId}, stateChapter={chapterIndex + 1}, currentChapter={ReaderCurrentChapterIndex + 1}");
                return;
            }

            var targetPage = Math.Clamp(state.PageIndex, 0, Math.Max(0, TotalPages - 1));
            if (!string.IsNullOrWhiteSpace(state.AnchorText))
            {
                var anchorPage = FindPageByAnchorText(state.AnchorText);
                if (anchorPage >= 0)
                    targetPage = anchorPage;
            }

            CurrentPageIndex = targetPage;
            ShowCurrentPage();
        }
        finally
        {
            _isApplyingPersistedState = false;
        }
    }

    private void QueuePersistReadingState()
    {
        if (_isApplyingPersistedState || SelectedDbBook is null || _currentBookChapters.Count == 0)
            return;

        _readingStateSaveCts?.Cancel();
        var cts = new CancellationTokenSource();
        _readingStateSaveCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400, cts.Token);
                var state = new ReadingStateEntity
                {
                    BookId = SelectedDbBook.Id,
                    ChapterIndex = ReaderCurrentChapterIndex,
                    PageIndex = CurrentPageIndex,
                    AnchorText = BuildCurrentPageAnchorText(),
                    LayoutFingerprint = BuildLayoutFingerprint(),
                    UpdatedAt = DateTimeOffset.Now.ToString("o"),
                };
                await _bookRepo.UpsertReadingStateAsync(state, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Reader.SaveReadingState", ex);
            }
        });
    }

    private string BuildLayoutFingerprint()
        => $"{ReaderFontSize:F0}|{ReaderLineHeight:F0}|{ReaderContentMaxWidth:F0}|{ReaderSidePaddingPx:F0}|{ReaderHorizontalInnerReservePx:F0}|{_viewportWidth:F0}|{_viewportHeight:F0}";

    private string BuildCurrentPageAnchorText()
    {
        if (_chapterPages.Count == 0 || CurrentPageIndex < 0 || CurrentPageIndex >= _chapterPages.Count)
            return string.Empty;

        return _chapterPages[CurrentPageIndex]
            .Select(l => l?.Trim() ?? string.Empty)
            .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("⌜", StringComparison.Ordinal))
            ?? string.Empty;
    }

    private string BuildCurrentPagePreviewText()
    {
        if (_chapterPages.Count == 0 || CurrentPageIndex < 0 || CurrentPageIndex >= _chapterPages.Count)
            return string.Empty;

        var merged = string.Join("", _chapterPages[CurrentPageIndex]
            .Select(l => l?.Trim() ?? string.Empty)
            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("⌜", StringComparison.Ordinal)));
        if (merged.Length <= 48) return merged;
        return merged[..48] + "…";
    }

    private int FindPageByAnchorText(string anchorText)
    {
        if (string.IsNullOrWhiteSpace(anchorText) || _chapterPages.Count == 0)
            return -1;

        for (var i = 0; i < _chapterPages.Count; i++)
        {
            if (_chapterPages[i].Any(l => (l?.Contains(anchorText, StringComparison.Ordinal) ?? false)))
                return i;
        }

        return -1;
    }

    private void RefreshCurrentPageBookmarkFlag()
    {
        if (SelectedDbBook is null)
        {
            IsCurrentPageBookmarked = false;
            return;
        }

        IsCurrentPageBookmarked = ReaderBookmarks.Any(b =>
            b.ChapterIndex == ReaderCurrentChapterIndex && b.PageIndex == CurrentPageIndex);
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

    private static bool IsChapterTitle(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length > MaxChapterTitleLength)
        {
            return false;
        }

        return ChineseChapterRegex.IsMatch(line) || EnglishChapterRegex.IsMatch(line);
    }

    private void RecalculateReaderInsets()
    {
        var sidePadding = Math.Max(0, ReaderSidePaddingPx);

        if (ReaderExtendIntoCutout)
        {
            // Android 刘海沉浸模式：正文顶部至少预留一整行安全高度，避免首行贴近刘海/状态栏。
            var topSafeLine = OperatingSystem.IsAndroid()
                ? Math.Max(ReaderLineHeight, ReaderFontSize * EffectiveLineHeightFactor)
                : 0;
            ReaderContentMargin = new Thickness(sidePadding, ReaderTopReservePx + topSafeLine, sidePadding, ReaderBottomReservePx);
            // 工具栏顶部留出 48dp 以避让状态栏/刘海高度
            ReaderTopToolbarPadding = new Thickness(8, 48, 8, 6);
            return;
        }

        ReaderContentMargin = new Thickness(sidePadding, ReaderTopReservePx, sidePadding, ReaderBottomReservePx);
        ReaderTopToolbarPadding = new Thickness(8, 6, 8, 6);
    }

    // ==================== Partial Methods ====================

    partial void OnReaderContentChanged(string value)
    {
        // 当内容改变时重建分页
        RebuildChapterPages();
        CurrentPageIndex = 0;
        ShowCurrentPage();
        // fallback：如果分页尚未初始化，走旧的全量段落渲染
        if (_chapterPages.Count == 0) RebuildParagraphs();
    }

    partial void OnReaderCurrentChapterIndexChanged(int value)
    {
        if (_currentBookChapters.Count > 0 && (value < 0 || value >= _currentBookChapters.Count))
        {
            EnsureValidCurrentChapterIndex();
            return;
        }

        UpdateProgressDisplay();
        OnPropertyChanged(nameof(CurrentChapterTitleDisplay));

        if (_suppressReaderIndexChangedNavigation)
            return;

        if (value < 0 || value >= _currentBookChapters.Count)
            return;

        _ = NavigateToChapterAsync(value, goToLastPage: false);
    }

    partial void OnIsCurrentPageBookmarkedChanged(bool value)
    {
        OnPropertyChanged(nameof(BookmarkToggleText));
        OnPropertyChanged(nameof(BookmarkButtonBackground));
        OnPropertyChanged(nameof(BookmarkButtonForeground));
        OnPropertyChanged(nameof(BookmarkToolbarForeground));
    }

    partial void OnSelectedReaderChapterChanged(string? value)
    {
        // 目录跳转统一走 SelectedIndex / Command，避免字符串匹配造成选中漂移。
    }

    partial void OnIsTocOverlayVisibleChanged(bool value)
    {
        if (!value && _pendingRefreshWhenTocClosed)
        {
            _pendingRefreshWhenTocClosed = false;
            _ = RefreshCurrentBookFromDownloadSignalAsync(SelectedDbBook?.Id);
        }
    }

    async partial void OnSelectedSwitchSourceChanged(SourceItem? value)
    {
        if (value is null || value.Id <= 0) return;
        if (IsSourceSwitching) return;
        if (SelectedDbBook is null) { _parent.StatusMessage = "当前未打开任何书籍。"; return; }
        if (ReaderCurrentChapterIndex < 0 || ReaderCurrentChapterIndex >= _currentBookChapters.Count)
        { _parent.StatusMessage = "当前没有正在阅读的章节。"; return; }

        var chapterTitle = _currentBookChapters[ReaderCurrentChapterIndex].Title;
        IsSourceSwitching = true;
        _parent.StatusMessage = $"换源中：正在从 {value.Name} 获取「{chapterTitle}」…";

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
                _parent.StatusMessage = message;
            }
            else
            {
                _parent.StatusMessage = $"换源未成功，已保留原文。{message}";
            }
        }
        catch (Exception ex) { _parent.StatusMessage = $"换源失败，已保留原文。{ex.Message}"; }
        finally { IsSourceSwitching = false; }
    }

    partial void OnSelectedFontNameChanged(string value)
    {
        ReaderFontFamily = _fontMap.TryGetValue(value, out var ff) ? ff : FontFamily.Default;
        RecalculateLinesPerPage();
        RebuildChapterPages();
        CurrentPageIndex = Math.Clamp(CurrentPageIndex, 0, Math.Max(0, TotalPages - 1));
        ShowCurrentPage();
        _parent.Settings.QueueAutoSaveSettings();
    }

    partial void OnReaderLineHeightChanged(double value)
    {
        RecalculateReaderInsets();
        RecalculateLinesPerPage();
        RebuildChapterPages();
        CurrentPageIndex = Math.Clamp(CurrentPageIndex, 0, Math.Max(0, TotalPages - 1));
        ShowCurrentPage();
        _parent.Settings.QueueAutoSaveSettings();
    }

    partial void OnReaderParagraphSpacingChanged(double value)
    {
        ParagraphMargin = new Avalonia.Thickness(0, 0, 0, value);
        RebuildChapterPages();
        CurrentPageIndex = Math.Clamp(CurrentPageIndex, 0, Math.Max(0, TotalPages - 1));
        ShowCurrentPage();
        _parent.Settings.QueueAutoSaveSettings();
    }

    partial void OnReaderFontSizeChanged(double value)
    {
        RecalculateReaderInsets();
        RecalculateLinesPerPage();
        RebuildChapterPages();
        CurrentPageIndex = Math.Clamp(CurrentPageIndex, 0, Math.Max(0, TotalPages - 1));
        ShowCurrentPage();
        _parent.Settings.QueueAutoSaveSettings();
    }

    partial void OnReaderContentMaxWidthChanged(double value)
    {
        RebuildChapterPages();
        CurrentPageIndex = Math.Clamp(CurrentPageIndex, 0, Math.Max(0, TotalPages - 1));
        ShowCurrentPage();
        _parent.Settings.QueueAutoSaveSettings();
    }

    partial void OnReaderTopReservePxChanged(double value)
    {
        RecalculateReaderInsets();
        RecalculateLinesPerPage();
        RebuildChapterPages();
        CurrentPageIndex = Math.Clamp(CurrentPageIndex, 0, Math.Max(0, TotalPages - 1));
        ShowCurrentPage();
        _parent.Settings.QueueAutoSaveSettings();
    }

    partial void OnReaderBottomReservePxChanged(double value)
    {
        RecalculateReaderInsets();
        RecalculateLinesPerPage();
        RebuildChapterPages();
        CurrentPageIndex = Math.Clamp(CurrentPageIndex, 0, Math.Max(0, TotalPages - 1));
        ShowCurrentPage();
        _parent.Settings.QueueAutoSaveSettings();
    }

    partial void OnReaderBottomStatusBarReservePxChanged(double value)
    {
        if (!OperatingSystem.IsAndroid())
        {
            // Desktop 端底部状态栏预留改为内部固定计算，不暴露独立调参。
            _parent.Settings.QueueAutoSaveSettings();
            return;
        }

        RecalculateLinesPerPage();
        RebuildChapterPages();
        CurrentPageIndex = Math.Clamp(CurrentPageIndex, 0, Math.Max(0, TotalPages - 1));
        ShowCurrentPage();
        _parent.Settings.QueueAutoSaveSettings();
    }

    partial void OnReaderHorizontalInnerReservePxChanged(double value)
    {
        RecalculateLinesPerPage();
        RebuildChapterPages();
        CurrentPageIndex = Math.Clamp(CurrentPageIndex, 0, Math.Max(0, TotalPages - 1));
        ShowCurrentPage();
        _parent.Settings.QueueAutoSaveSettings();
    }

    partial void OnReaderSidePaddingPxChanged(double value)
    {
        RecalculateReaderInsets();
        RecalculateLinesPerPage();
        RebuildChapterPages();
        CurrentPageIndex = Math.Clamp(CurrentPageIndex, 0, Math.Max(0, TotalPages - 1));
        ShowCurrentPage();
        _parent.Settings.QueueAutoSaveSettings();
    }

    partial void OnReaderExtendIntoCutoutChanged(bool value)
    {
        RecalculateReaderInsets();
        // 刘海模式变化影响文本安全区，需重新计算分页
        RecalculateLinesPerPage();
        if (_chapterPages.Count > 0)
        {
            var savedPage = CurrentPageIndex;
            RebuildChapterPages();
            CurrentPageIndex = Math.Clamp(savedPage, 0, Math.Max(0, TotalPages - 1));
            ShowCurrentPage();
        }
        _parent.Settings.QueueAutoSaveSettings();
    }

    partial void OnReaderUseVolumeKeyPagingChanged(bool value)
    {
        _parent.Settings.QueueAutoSaveSettings();
    }

    partial void OnReaderUseSwipePagingChanged(bool value)
    {
        _parent.Settings.QueueAutoSaveSettings();
    }

    partial void OnReaderHideSystemStatusBarChanged(bool value)
    {
        _parent.Settings.QueueAutoSaveSettings();
    }

    partial void OnReaderAutoPrefetchEnabledChanged(bool value)
    {
        _parent.Settings.QueueAutoSaveSettings();
        if (value)
        {
            QueueAutoPrefetch("toggle-on");
        }
    }

    partial void OnReaderPrefetchBatchSizeChanged(int value)
    {
        if (value < 1)
        {
            ReaderPrefetchBatchSize = 1;
            return;
        }

        _parent.Settings.QueueAutoSaveSettings();
    }

    partial void OnReaderPrefetchLowWatermarkChanged(int value)
    {
        if (value < 1)
        {
            ReaderPrefetchLowWatermark = 1;
            return;
        }

        _parent.Settings.QueueAutoSaveSettings();
    }

    partial void OnReaderBackgroundChanged(string value)
    {
        _parent.Settings.QueueAutoSaveSettings();
    }

    partial void OnReaderForegroundChanged(string value)
    {
        _parent.Settings.QueueAutoSaveSettings();
    }

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

        _parent.Settings.QueueAutoSaveSettings();
    }
}
