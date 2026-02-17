using System.Text.Json;
using ReadStorm.Application.Abstractions;
using ReadStorm.Domain.Models;

namespace ReadStorm.Infrastructure.Services;

public sealed class JsonFileAppSettingsUseCase : IAppSettingsUseCase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string _settingsFilePath;

    public JsonFileAppSettingsUseCase(string? settingsFilePath = null)
    {
        _settingsFilePath = settingsFilePath ?? ResolveDefaultSettingsFilePath();
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        AppSettings settings;
        if (!File.Exists(_settingsFilePath))
        {
            settings = new AppSettings();
        }
        else
        {
            await using var stream = File.OpenRead(_settingsFilePath);
            settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
                       ?? new AppSettings();
        }

        var normalizedWorkDir = WorkDirectoryManager.NormalizeAndMigrateWorkDirectory(settings.DownloadPath);
        if (!string.Equals(settings.DownloadPath, normalizedWorkDir, StringComparison.OrdinalIgnoreCase))
        {
            settings.DownloadPath = normalizedWorkDir;
            await SaveAsync(settings, cancellationToken);
        }
        else
        {
            WorkDirectoryManager.EnsureWorkDirectoryLayout(normalizedWorkDir);
        }

        return settings;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        settings.DownloadPath = WorkDirectoryManager.NormalizeAndMigrateWorkDirectory(settings.DownloadPath);

        var folder = Path.GetDirectoryName(_settingsFilePath)!;
        Directory.CreateDirectory(folder);

        var tempPath = _settingsFilePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
        }

        if (File.Exists(_settingsFilePath))
        {
            File.Delete(_settingsFilePath);
        }

        File.Move(tempPath, _settingsFilePath);
    }

    private static string ResolveDefaultSettingsFilePath()
    {
        return WorkDirectoryManager.GetSettingsFilePath();
    }
}
