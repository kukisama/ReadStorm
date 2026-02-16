using ReadStorm.Domain.Models;

namespace ReadStorm.Application.Abstractions;

public interface IBookshelfUseCase
{
    Task<IReadOnlyList<BookRecord>> GetAllAsync(CancellationToken cancellationToken = default);

    Task AddAsync(BookRecord book, CancellationToken cancellationToken = default);

    Task RemoveAsync(Guid bookId, CancellationToken cancellationToken = default);

    Task UpdateProgressAsync(Guid bookId, ReadingProgress progress, CancellationToken cancellationToken = default);
}
