using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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
    }

    // ==================== Static Fields ====================

    private const int MaxChapterTitleLength = 50;

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
    private double readerLineHeight = 28;

    [ObservableProperty]
    private double readerParagraphSpacing = 12;

    [ObservableProperty]
    private Avalonia.Thickness paragraphMargin = new(0, 0, 0, 12);

    [ObservableProperty]
    private string readerBackground = "#FFFFFF";

    [ObservableProperty]
    private string readerForeground = "#1F2937";

    [ObservableProperty]
    private bool isDarkMode;

    // ==================== Computed Properties ====================

    /// <summary>当前书籍中已完成的章节数（不以"（"开头的视为已完成）。</summary>
    public int CurrentBookDoneCount =>
        _currentBookChapters.Count(c => !c.Content.StartsWith("（"));

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

    private async Task NavigateToChapterAsync(int index)
    {
        if (index < 0 || index >= _currentBookChapters.Count) return;

        ReaderCurrentChapterIndex = index;
        ReaderContent = _currentBookChapters[index].Content;
        ReaderScrollVersion++;
        IsTocOverlayVisible = false;

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
        foreach (var line in ReaderContent.Split('\n'))
        {
            ReaderParagraphs.Add(line);
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

    private static bool IsChapterTitle(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.Length > MaxChapterTitleLength)
        {
            return false;
        }

        return ChineseChapterRegex.IsMatch(line) || EnglishChapterRegex.IsMatch(line);
    }

    // ==================== Partial Methods ====================

    partial void OnReaderContentChanged(string value)
    {
        RebuildParagraphs();
    }

    partial void OnSelectedReaderChapterChanged(string? value)
    {
        if (value is not null)
        {
            var index = ReaderChapters.IndexOf(value);
            if (index >= 0 && index < _currentBookChapters.Count)
            {
                _ = NavigateToChapterAsync(index);
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
        _parent.Settings.QueueAutoSaveSettings();
    }

    partial void OnReaderLineHeightChanged(double value)
    {
        _parent.Settings.QueueAutoSaveSettings();
    }

    partial void OnReaderParagraphSpacingChanged(double value)
    {
        ParagraphMargin = new Avalonia.Thickness(0, 0, 0, value);
        _parent.Settings.QueueAutoSaveSettings();
    }

    partial void OnReaderFontSizeChanged(double value)
    {
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
