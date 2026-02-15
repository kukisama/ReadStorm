using ReadStorm.Domain.Models;

namespace ReadStorm.Application.Abstractions;

public interface ISearchBooksUseCase
{
    Task<IReadOnlyList<SearchResult>> ExecuteAsync(
        string keyword,
        int? sourceId = null,
        CancellationToken cancellationToken = default);
}
