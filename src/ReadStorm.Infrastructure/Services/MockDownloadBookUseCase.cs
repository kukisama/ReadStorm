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
}
