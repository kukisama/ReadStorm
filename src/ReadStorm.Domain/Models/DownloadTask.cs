using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ReadStorm.Domain.Models;

public sealed class DownloadTask : INotifyPropertyChanged
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string BookTitle { get; init; } = string.Empty;

    public string Author { get; init; } = string.Empty;

    public DownloadMode Mode { get; init; }

    private DownloadTaskStatus _currentStatus = DownloadTaskStatus.Queued;

    public DownloadTaskStatus CurrentStatus
    {
        get => _currentStatus;
        private set => SetField(ref _currentStatus, value, nameof(Status), nameof(CanRetry), nameof(CanCancel));
    }

    public string Status => CurrentStatus.ToString();

    public bool CanRetry => CurrentStatus is DownloadTaskStatus.Failed or DownloadTaskStatus.Cancelled;

    public bool CanCancel => CurrentStatus is DownloadTaskStatus.Queued or DownloadTaskStatus.Downloading or DownloadTaskStatus.Paused;

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

    public void UpdateProgress(int progressPercent)
    {
        ProgressPercent = Math.Clamp(progressPercent, 0, 100);
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
