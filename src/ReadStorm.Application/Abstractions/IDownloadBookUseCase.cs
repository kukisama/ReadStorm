using ReadStorm.Domain.Models;

namespace ReadStorm.Application.Abstractions;

public interface IDownloadBookUseCase
{
    Task QueueAsync(
        DownloadTask task,
        SearchResult selectedBook,
        DownloadMode mode,
        CancellationToken cancellationToken = default);
}
