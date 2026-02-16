using ReadStorm.Application.Abstractions;
using ReadStorm.Domain.Models;

namespace ReadStorm.Infrastructure.Services;

public sealed class MockDownloadBookUseCase : IDownloadBookUseCase
{
    public async Task QueueAsync(
        DownloadTask task,
        SearchResult selectedBook,
        DownloadMode mode,
        CancellationToken cancellationToken = default)
    {
        await Task.Delay(120, cancellationToken);
        task.TransitionTo(DownloadTaskStatus.Downloading);

        task.UpdateProgress(35);
        await Task.Delay(120, cancellationToken);

        task.UpdateProgress(78);
        await Task.Delay(120, cancellationToken);

        task.TransitionTo(DownloadTaskStatus.Succeeded);
    }

    public Task<int> CheckNewChaptersAsync(BookEntity book, CancellationToken cancellationToken = default)
        => Task.FromResult(0);

    public Task<string> RefreshCoverAsync(BookEntity book, CancellationToken cancellationToken = default)
        => Task.FromResult("[mock] 封面刷新功能在 Mock 模式不可用。");

    public Task<IReadOnlyList<CoverCandidate>> GetCoverCandidatesAsync(BookEntity book, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<CoverCandidate>>([]);

    public Task<string> ApplyCoverCandidateAsync(BookEntity book, CoverCandidate candidate, CancellationToken cancellationToken = default)
        => Task.FromResult("[mock] 手动封面设置在 Mock 模式不可用。");

    public Task<(bool Success, string Content, string Message)> FetchChapterFromSourceAsync(BookEntity book, string chapterTitle, int sourceId, CancellationToken cancellationToken = default)
        => Task.FromResult((false, string.Empty, "[mock] 换源功能在 Mock 模式不可用。"));
}
