using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReadStorm.Application.Abstractions;
using ReadStorm.Domain.Models;
using ReadStorm.Infrastructure.Services;

namespace ReadStorm.Desktop.ViewModels;

public sealed partial class ReaderViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _parent;
    private readonly IBookRepository _bookRepo;
    private readonly IDownloadBookUseCase _downloadBookUseCase;
    private readonly ICoverUseCase _coverUseCase;
    private readonly IBookshelfUseCase _bookshelfUseCase;

    private List<(string Title, string Content)> _currentBookChapters = new();

    public ReaderViewModel(
        MainWindowViewModel parent,
        IBookRepository bookRepo,
        IDownloadBookUseCase downloadBookUseCase,
        ICoverUseCase coverUseCase,
        IBookshelfUseCase bookshelfUseCase)
    {
        _parent = parent;
        _bookRepo = bookRepo;
        _downloadBookUseCase = downloadBookUseCase;
        _coverUseCase = coverUseCase;
        _bookshelfUseCase = bookshelfUseCase;
        RecalculateReaderInsets();
    }

    // ==================== Static Fields ====================

    private const int MaxChapterTitleLength = 50;
    private const double BottomStatusBarReserve = 28;
    private const double EffectiveLineHeightFactor = 1.12;

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

    [ObservableProperty]
    private bool isTocOverlayVisible;

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
    private double readerFontSize = 15;

    [ObservableProperty]
    private string selectedFontName = "默认";

    [ObservableProperty]
    private FontFamily readerFontFamily = FontFamily.Default;

    [ObservableProperty]
    private double readerLineHeight = 30;

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

    /// <summary>阅读正文区域外边距（Android 端可用于刘海留白）。</summary>
    [ObservableProperty]
    private Thickness readerContentMargin = new(20, 12, 20, 12);

    /// <summary>阅读顶部工具栏内边距（Android 端可用于刘海留白）。</summary>
    [ObservableProperty]
    private Thickness readerTopToolbarPadding = new(8, 6, 8, 6);

    /// <summary>阅读区域最大宽度（px），用户可自行调整。</summary>
    [ObservableProperty]
    private double readerContentMaxWidth = 860;

    // ==================== Computed Properties ====================

    /// <summary>当前书籍中已完成的章节数（不以"（"开头的视为已完成）。</summary>
    public int CurrentBookDoneCount =>
        _currentBookChapters.Count(c => !c.Content.StartsWith("（"));

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

    /// <summary>可用视口高度（由 View 在 SizeChanged 时设置）。</summary>
    private double _viewportHeight;

    /// <summary>可用视口宽度（由 View 在 SizeChanged 时设置）。</summary>
    private double _viewportWidth;

    // ==================== Collections ====================

    public ObservableCollection<string> ReaderParagraphs { get; } = [];
    public ObservableCollection<string> ReaderChapters { get; } = [];
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
    }

    [RelayCommand]
    private async Task RefreshReaderAsync()
    {
        if (SelectedDbBook is null) { _parent.StatusMessage = "当前未打开 DB 书籍，无法刷新。"; return; }

        var freshBook = await _bookRepo.GetBookAsync(SelectedDbBook.Id);
        if (freshBook is not null)
        {
            SelectedDbBook.DoneChapters = freshBook.DoneChapters;
            SelectedDbBook.TotalChapters = freshBook.TotalChapters;
        }

        var savedIndex = ReaderCurrentChapterIndex;
        await LoadDbBookChaptersAsync(SelectedDbBook);

        if (savedIndex >= 0 && savedIndex < _currentBookChapters.Count)
        {
            ReaderCurrentChapterIndex = savedIndex;
            ReaderContent = _currentBookChapters[savedIndex].Content;
            SelectedReaderChapter = ReaderChapters[savedIndex];
        }

        var doneCount = CurrentBookDoneCount;
        _parent.StatusMessage = $"已刷新：《{SelectedDbBook.Title}》，{doneCount}/{SelectedDbBook.TotalChapters} 章可读";
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
            SelectedReaderChapter = ReaderChapters.Count > ReaderCurrentChapterIndex
                ? ReaderChapters[ReaderCurrentChapterIndex]
                : null;
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
            SelectedReaderChapter = ReaderChapters.Count > ReaderCurrentChapterIndex
                ? ReaderChapters[ReaderCurrentChapterIndex]
                : null;
        }
    }

    [RelayCommand]
    private async Task SelectTocChapterAsync(int index)
    {
        if (index >= 0 && index < _currentBookChapters.Count)
        {
            await NavigateToChapterAsync(index);
            SelectedReaderChapter = ReaderChapters.Count > index
                ? ReaderChapters[index]
                : null;
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
        RefreshSortedSwitchSources();
        UpdateProgressDisplay();
    }

    // ==================== Internal Methods ====================

    internal async Task LoadDbBookChaptersAsync(BookEntity book)
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
            var preferred = Math.Clamp(book.ReadChapterIndex, 0, chapters.Count - 1);
            var startIndex = preferred;
            if (chapters[preferred].Status != ChapterStatus.Done)
            {
                var afterDone = chapters.Skip(preferred).FirstOrDefault(c => c.Status == ChapterStatus.Done);
                if (afterDone is not null)
                    startIndex = afterDone.IndexNo;
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

    private async Task NavigateToChapterAsync(int index, bool goToLastPage = false)
    {
        if (index < 0 || index >= _currentBookChapters.Count) return;

        ReaderCurrentChapterIndex = index;
        ReaderContent = _currentBookChapters[index].Content;
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
                    SelectedDbBook.Id, index, _currentBookChapters[index].Title);
                SelectedDbBook.ReadChapterIndex = index;
                SelectedDbBook.ReadChapterTitle = _currentBookChapters[index].Title;
                _parent.Bookshelf.MarkBookshelfDirty();
            }
            catch (Exception ex) { AppLogger.Warn("Reader.SaveDbProgress", ex); }
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

    private void RebuildParagraphs()
    {
        ReaderParagraphs.Clear();
        if (string.IsNullOrEmpty(ReaderContent)) return;

        // 分页模式：由 RebuildChapterPages + ShowCurrentPage 驱动，
        // RebuildParagraphs 仅在初始加载（视口尺寸未知时）提供 fallback。
        if (_chapterPages.Count > 0) return;

        foreach (var line in ReaderContent.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                ReaderParagraphs.Add(string.Empty);
                continue;
            }

            // 中文阅读习惯：段首保留两个全角空格（不重复追加）。
            var content = line.TrimStart('\r');
            if (!content.StartsWith("　　", StringComparison.Ordinal))
            {
                content = $"　　{content}";
            }

            ReaderParagraphs.Add(content);
        }
    }

    /// <summary>
    /// 由 View 在视口尺寸变化时调用，触发分页重算。
    /// </summary>
    public void UpdateViewportSize(double viewportWidth, double viewportHeight)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0) return;
        if (Math.Abs(_viewportWidth - viewportWidth) < 1 && Math.Abs(_viewportHeight - viewportHeight) < 1) return;

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
        // 视口高度扣除内容区上下边距 + 底部状态栏保守预留。
        var contentMarginVertical = ReaderContentMargin.Top + ReaderContentMargin.Bottom;
        var availableHeight = _viewportHeight - contentMarginVertical - BottomStatusBarReserve;
        if (availableHeight <= 0) availableHeight = 400;

        _effectiveLineHeight = Math.Max(
            ReaderLineHeight > 0 ? ReaderLineHeight : 30,
            (ReaderFontSize > 0 ? ReaderFontSize : 15) * EffectiveLineHeightFactor);

        // Android 刘海沉浸模式：至少预留一整行顶部安全区。
        if (OperatingSystem.IsAndroid() && ReaderExtendIntoCutout)
        {
            availableHeight -= _effectiveLineHeight;
        }

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

        var chapterTitle = ReaderCurrentChapterIndex >= 0 && ReaderCurrentChapterIndex < _currentBookChapters.Count
            ? _currentBookChapters[ReaderCurrentChapterIndex].Title
            : null;

        // 先构建所有段落（带缩进）
        var allParagraphs = new List<string>();
        foreach (var line in ReaderContent.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                allParagraphs.Add(string.Empty);
                continue;
            }
            var content = line.TrimStart('\r');
            if (!content.StartsWith("　　", StringComparison.Ordinal))
                content = $"　　{content}";
            allParagraphs.Add(content);
        }

        // 估算每个段落会占多少行（基于字符数和可用宽度的粗略估计）
        var charsPerLine = EstimateCharsPerLine();

        // 首页预留标题行：标题 + 空行 = 2 行
        var firstPageCapacity = _linesPerPage;
        if (!string.IsNullOrWhiteSpace(chapterTitle))
        {
            firstPageCapacity -= 2; // 标题 + 空行
            if (firstPageCapacity < 1) firstPageCapacity = 1;
        }

        var currentPage = new List<string>();
        var currentLineCount = 0;
        var isFirstPage = true;

        // 首页加入标题
        if (!string.IsNullOrWhiteSpace(chapterTitle))
        {
            currentPage.Add($"⌜{chapterTitle}⌝"); // 标记为标题行，View 渲染时居中
            currentPage.Add(string.Empty); // 空行
        }

        var capacity = isFirstPage ? firstPageCapacity : _linesPerPage;

        foreach (var para in allParagraphs)
        {
            var paraLines = EstimateParagraphLines(para, charsPerLine, _effectiveLineHeight, ParagraphMargin.Bottom);

            // 如果当前页放不下，先存当前页，开新页
            if (currentLineCount + paraLines > capacity && currentPage.Count > (isFirstPage && !string.IsNullOrWhiteSpace(chapterTitle) ? 2 : 0))
            {
                _chapterPages.Add(currentPage);
                currentPage = [];
                currentLineCount = 0;
                isFirstPage = false;
                capacity = _linesPerPage;
            }

            currentPage.Add(para);
            currentLineCount += paraLines;
        }

        // 最后一页
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
        availableWidth -= ReaderContentMargin.Left + ReaderContentMargin.Right + 32; // 32px 预留 padding
        if (availableWidth < 100) availableWidth = 100;

        // 对于中文，1 个字符约等于 1em（FontSize）；对英文则更窄
        // 使用 FontSize * 0.9 作为平均字宽（混合文本估计）
        var avgCharWidth = ReaderFontSize > 0 ? ReaderFontSize * 0.9 : 14;
        return Math.Max(10, (int)Math.Floor(availableWidth / avgCharWidth));
    }

    /// <summary>估算一个段落在当前排版下占用的行数。</summary>
    private static int EstimateParagraphLines(string paragraph, int charsPerLine, double effectiveLineHeight, double paragraphBottomMargin)
    {
        if (string.IsNullOrWhiteSpace(paragraph)) return 1; // 空行占 1 行

        var textLines = Math.Max(1, (int)Math.Ceiling((double)paragraph.Length / charsPerLine));
        var spacingLines = effectiveLineHeight > 0
            ? (int)Math.Ceiling(paragraphBottomMargin / effectiveLineHeight)
            : 0;
        return Math.Max(1, textLines + Math.Max(0, spacingLines));
    }

    /// <summary>将当前页内容显示到 ReaderParagraphs。</summary>
    private void ShowCurrentPage()
    {
        ReaderParagraphs.Clear();

        if (_chapterPages.Count == 0 || CurrentPageIndex < 0 || CurrentPageIndex >= _chapterPages.Count)
        {
            UpdatePageProgressDisplay();
            return;
        }

        foreach (var para in _chapterPages[CurrentPageIndex])
        {
            ReaderParagraphs.Add(para);
        }

        UpdatePageProgressDisplay();
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

        var current = ReaderCurrentChapterIndex + 1; // 1-based display
        var percent = (int)Math.Round(100.0 * current / total);
        ReaderProgressDisplay = $"第 {current}/{total} 章 ({percent}%)";
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
        if (ReaderExtendIntoCutout)
        {
            // Android 刘海沉浸模式：正文顶部至少预留一整行安全高度，避免首行贴近刘海/状态栏。
            var topSafeLine = OperatingSystem.IsAndroid()
                ? Math.Max(ReaderLineHeight, ReaderFontSize * EffectiveLineHeightFactor)
                : 0;
            ReaderContentMargin = new Thickness(20, topSafeLine, 20, 12);
            // 工具栏顶部留出 48dp 以避让状态栏/刘海高度
            ReaderTopToolbarPadding = new Thickness(8, 48, 8, 6);
            return;
        }

        ReaderContentMargin = new Thickness(20, 12, 20, 12);
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
        UpdateProgressDisplay();
    }

    partial void OnSelectedReaderChapterChanged(string? value)
    {
        if (value is not null)
        {
            var index = ReaderChapters.IndexOf(value);
            if (index >= 0 && index < _currentBookChapters.Count)
            {
                _ = NavigateToChapterAsync(index, goToLastPage: false);
            }
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
