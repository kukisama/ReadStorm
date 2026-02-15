using ReadStorm.Domain.Models;

namespace ReadStorm.Application.Abstractions;

public interface IRuleCatalogUseCase
{
    Task<IReadOnlyList<BookSourceRule>> GetAllAsync(CancellationToken cancellationToken = default);
}
