using ReadStorm.Application.Abstractions;
using ReadStorm.Domain.Models;

namespace ReadStorm.Infrastructure.Services;

public sealed class InMemoryAppSettingsUseCase : IAppSettingsUseCase
{
    private AppSettings _settings = new();

    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new AppSettings
        {
            DownloadPath = _settings.DownloadPath,
            MaxConcurrency = _settings.MaxConcurrency,
            MinIntervalMs = _settings.MinIntervalMs,
            MaxIntervalMs = _settings.MaxIntervalMs,
            ExportFormat = _settings.ExportFormat,
            ProxyEnabled = _settings.ProxyEnabled,
            ProxyHost = _settings.ProxyHost,
            ProxyPort = _settings.ProxyPort,
        });
    }

    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        _settings = new AppSettings
        {
            DownloadPath = settings.DownloadPath,
            MaxConcurrency = settings.MaxConcurrency,
            MinIntervalMs = settings.MinIntervalMs,
            MaxIntervalMs = settings.MaxIntervalMs,
            ExportFormat = settings.ExportFormat,
            ProxyEnabled = settings.ProxyEnabled,
            ProxyHost = settings.ProxyHost,
            ProxyPort = settings.ProxyPort,
        };

        return Task.CompletedTask;
    }
}
