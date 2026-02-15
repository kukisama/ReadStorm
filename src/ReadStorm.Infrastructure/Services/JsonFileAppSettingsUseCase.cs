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
        if (!File.Exists(_settingsFilePath))
        {
            return new AppSettings();
        }

        await using var stream = File.OpenRead(_settingsFilePath);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken);
        return settings ?? new AppSettings();
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
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
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "ReadStorm");
        return Path.Combine(dir, "appsettings.user.json");
    }
}
