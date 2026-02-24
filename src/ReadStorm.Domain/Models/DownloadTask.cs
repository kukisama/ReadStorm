using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ReadStorm.Domain.Models;

public sealed class DownloadTask : INotifyPropertyChanged
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>关联的 SQLite 书籍 ID，下载开始后赋值。</summary>
    public string BookId { get; set; } = string.Empty;

    public string BookTitle { get; init; } = string.Empty;

    public string Author { get; init; } = string.Empty;

    public DownloadMode Mode { get; init; }

    private DownloadTaskStatus _currentStatus = DownloadTaskStatus.Queued;

    public DownloadTaskStatus CurrentStatus
    {
        get => _currentStatus;
        private set => SetField(ref _currentStatus, value, nameof(Status), nameof(CanRetry), nameof(CanCancel), nameof(CanPause), nameof(CanResume), nameof(CanDelete));
    }

    public string Status => CurrentStatus.ToString();

    public bool CanRetry => CurrentStatus is DownloadTaskStatus.Failed or DownloadTaskStatus.Cancelled;

    public bool CanCancel => CurrentStatus is DownloadTaskStatus.Queued or DownloadTaskStatus.Downloading or DownloadTaskStatus.Paused;

    public bool CanPause => CurrentStatus is DownloadTaskStatus.Downloading;

    public bool CanResume => CurrentStatus is DownloadTaskStatus.Paused;

    /// <summary>可删除：非下载中均可删除。</summary>
    public bool CanDelete => CurrentStatus is not DownloadTaskStatus.Downloading;

    private int _retryCount;

    public int RetryCount
    {
        get => _retryCount;
        private set => SetField(ref _retryCount, value);
    }

    private int _progressPercent;

    public int ProgressPercent
    {
        get => _progressPercent;
        private set => SetField(ref _progressPercent, value);
    }

    private int _currentChapterIndex;

    public int CurrentChapterIndex
    {
        get => _currentChapterIndex;
        private set => SetField(ref _currentChapterIndex, value, nameof(ChapterProgressDisplay));
    }

    private int _totalChapterCount;

    public int TotalChapterCount
    {
        get => _totalChapterCount;
        private set => SetField(ref _totalChapterCount, value, nameof(ChapterProgressDisplay));
    }

    private string _currentChapterTitle = string.Empty;

    public string CurrentChapterTitle
    {
        get => _currentChapterTitle;
        private set => SetField(ref _currentChapterTitle, value, nameof(ChapterProgressDisplay));
    }

    /// <summary>章节进度显示，如 "33/1883 第33章 多喝了一杯"</summary>
    public string ChapterProgressDisplay =>
        TotalChapterCount > 0
            ? $"{Math.Clamp(CurrentChapterIndex, 0, TotalChapterCount)}/{TotalChapterCount} {CurrentChapterTitle}"
            : string.Empty;

    private DateTimeOffset? _startedAt;

    public DateTimeOffset? StartedAt
    {
        get => _startedAt;
        private set => SetField(ref _startedAt, value);
    }

    private DateTimeOffset? _completedAt;

    public DateTimeOffset? CompletedAt
    {
        get => _completedAt;
        private set => SetField(ref _completedAt, value);
    }

    private string _outputFilePath = string.Empty;

    public string OutputFilePath
    {
        get => _outputFilePath;
        set => SetField(ref _outputFilePath, value);
    }

    private string _error = string.Empty;

    public string Error
    {
        get => _error;
        set => SetField(ref _error, value, nameof(ErrorDisplay));
    }

    private DownloadErrorKind _errorKind = DownloadErrorKind.None;

    public DownloadErrorKind ErrorKind
    {
        get => _errorKind;
        set => SetField(ref _errorKind, value, nameof(ErrorKindDisplay));
    }

    public string ErrorKindDisplay => ErrorKind == DownloadErrorKind.None ? string.Empty : $"错误类型：{ErrorKind}";

    public string ErrorDisplay => string.IsNullOrWhiteSpace(Error) ? string.Empty : $"错误：{Error}";

    public IReadOnlyList<DownloadTaskStatus> StateHistory => _stateHistory;

    private readonly List<DownloadTaskStatus> _stateHistory = [DownloadTaskStatus.Queued];

    public event PropertyChangedEventHandler? PropertyChanged;

    public DateTimeOffset EnqueuedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>保留原始搜索结果，用于失败后重试。</summary>
    public SearchResult? SourceSearchResult { get; set; }

    /// <summary>
    /// 范围下载起始章节（0-based，含）。仅在 Mode=Range 时生效。
    /// </summary>
    public int? RangeStartIndex { get; set; }

    /// <summary>
    /// 范围下载章节数量。仅在 Mode=Range 时生效。
    /// </summary>
    public int? RangeTakeCount { get; set; }

    /// <summary>
    /// 是否为阅读自动预取任务。
    /// </summary>
    public bool IsAutoPrefetch { get; set; }

    /// <summary>
    /// 自动预取触发原因（如 open / jump / low-watermark / gap-fill）。
    /// </summary>
    public string AutoPrefetchReason { get; set; } = string.Empty;

    /// <summary>
    /// 自动预取标签展示文本。
    /// </summary>
    public string AutoPrefetchTagDisplay
    {
        get
        {
            if (!IsAutoPrefetch)
            {
                return string.Empty;
            }

            var reason = AutoPrefetchReason switch
            {
                "open" => "打开书籍",
                "jump" => "跳章",
                "manual-priority" => "手动选章优先",
                "low-watermark" => "低水位补拉",
                "gap-fill" => "缺口补齐",
                "foreground-direct" => "前台单章直下",
                _ => string.IsNullOrWhiteSpace(AutoPrefetchReason) ? "自动预取" : AutoPrefetchReason
            };

            return $"自动预取 · {reason}";
        }
    }

    public void TransitionTo(DownloadTaskStatus nextStatus)
    {
        if (!IsAllowed(CurrentStatus, nextStatus))
        {
            throw new InvalidOperationException($"非法状态流转: {CurrentStatus} -> {nextStatus}");
        }

        CurrentStatus = nextStatus;
        _stateHistory.Add(nextStatus);
        OnPropertyChanged(nameof(StateHistory));

        if (nextStatus == DownloadTaskStatus.Downloading)
        {
            StartedAt ??= DateTimeOffset.Now;
        }

        if (nextStatus is DownloadTaskStatus.Succeeded or DownloadTaskStatus.Failed or DownloadTaskStatus.Cancelled)
        {
            CompletedAt = DateTimeOffset.Now;
            if (nextStatus == DownloadTaskStatus.Succeeded)
            {
                ProgressPercent = 100;
            }
        }
    }

    /// <summary>将失败/已取消的任务重置为排队状态以便重试。</summary>
    public void ResetForRetry()
    {
        if (!CanRetry)
        {
            throw new InvalidOperationException($"当前状态 {CurrentStatus} 不允许重试。");
        }

        RetryCount++;
        Error = string.Empty;
        ErrorKind = DownloadErrorKind.None;
        ProgressPercent = 0;
        CompletedAt = null;

        CurrentStatus = DownloadTaskStatus.Queued;
        _stateHistory.Add(DownloadTaskStatus.Queued);
        OnPropertyChanged(nameof(StateHistory));
    }

    /// <summary>暂停后由 ViewModel 调用，将 Cancelled 覆盖为 Paused（绕过状态机）。</summary>
    public void OverrideToPaused()
    {
        _currentStatus = DownloadTaskStatus.Paused;
        Error = string.Empty;
        ErrorKind = DownloadErrorKind.None;
        _stateHistory.Add(DownloadTaskStatus.Paused);
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(StateHistory));
    }

    /// <summary>恢复暂停的任务，重置为排队状态。</summary>
    public void ResetForResume()
    {
        if (_currentStatus != DownloadTaskStatus.Paused)
            throw new InvalidOperationException($"当前状态 {_currentStatus} 不允许恢复。");

        _currentStatus = DownloadTaskStatus.Queued;
        Error = string.Empty;
        ErrorKind = DownloadErrorKind.None;
        CompletedAt = null;
        _stateHistory.Add(DownloadTaskStatus.Queued);
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanDelete));
        OnPropertyChanged(nameof(StateHistory));
    }

    public void UpdateProgress(int progressPercent)
    {
        ProgressPercent = Math.Clamp(progressPercent, 0, 100);
    }

    /// <summary>更新章节进度信息。</summary>
    public void UpdateChapterProgress(int currentIndex, int totalCount, string chapterTitle)
    {
        var safeTotal = Math.Max(0, totalCount);
        var safeCurrent = safeTotal > 0
            ? Math.Clamp(currentIndex, 0, safeTotal)
            : Math.Max(0, currentIndex);

        CurrentChapterIndex = safeCurrent;
        TotalChapterCount = safeTotal;
        CurrentChapterTitle = chapterTitle;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = "", params string[] additionalProperties)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        foreach (var property in additionalProperties)
        {
            OnPropertyChanged(property);
        }

        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string propertyName = "")
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static bool IsAllowed(DownloadTaskStatus current, DownloadTaskStatus next)
    {
        return current switch
        {
            DownloadTaskStatus.Queued => next is DownloadTaskStatus.Downloading or DownloadTaskStatus.Cancelled or DownloadTaskStatus.Failed,
            DownloadTaskStatus.Downloading => next is DownloadTaskStatus.Succeeded or DownloadTaskStatus.Failed or DownloadTaskStatus.Cancelled or DownloadTaskStatus.Paused,
            DownloadTaskStatus.Paused => next is DownloadTaskStatus.Downloading or DownloadTaskStatus.Cancelled,
            DownloadTaskStatus.Succeeded => false,
            DownloadTaskStatus.Failed => false,
            DownloadTaskStatus.Cancelled => false,
            _ => false,
        };
    }
}
