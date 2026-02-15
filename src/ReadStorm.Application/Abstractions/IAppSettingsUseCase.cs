using ReadStorm.Domain.Models;

namespace ReadStorm.Application.Abstractions;

public interface IAppSettingsUseCase
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
