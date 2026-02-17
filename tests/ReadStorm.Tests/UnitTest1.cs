using ReadStorm.Application.Abstractions;
using ReadStorm.Domain.Models;
using ReadStorm.Infrastructure.Services;

namespace ReadStorm.Tests;

/// <summary>空规则目录，用于测试 HybridSearchBooksUseCase 构造。</summary>
file sealed class EmptyRuleCatalog : IRuleCatalogUseCase
{
    public Task<IReadOnlyList<BookSourceRule>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<BookSourceRule>>([]);
}

public class UnitTest1
{
    [Fact]
    public async Task HybridSearch_ShouldReturnEmpty_WhenSpecificSourceUnavailable()
    {
        var sut = new HybridSearchBooksUseCase(new EmptyRuleCatalog());

        var results = await sut.ExecuteAsync("测试关键字", 9999);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SettingsUseCase_ShouldPersistValues_InMemory()
    {
        var sut = new InMemoryAppSettingsUseCase();

        var updated = new AppSettings
        {
            DownloadPath = "my-downloads",
            MaxConcurrency = 12,
            MinIntervalMs = 300,
            MaxIntervalMs = 800,
            ExportFormat = "txt",
            ProxyEnabled = true,
            ProxyHost = "127.0.0.1",
            ProxyPort = 10809,
        };

        await sut.SaveAsync(updated);
        var loaded = await sut.LoadAsync();

        Assert.Equal(updated.DownloadPath, loaded.DownloadPath);
        Assert.Equal(updated.MaxConcurrency, loaded.MaxConcurrency);
        Assert.Equal(updated.ExportFormat, loaded.ExportFormat);
        Assert.Equal(updated.ProxyEnabled, loaded.ProxyEnabled);
        Assert.Equal(updated.ProxyPort, loaded.ProxyPort);
    }

    [Fact]
    public async Task SettingsUseCase_ShouldPersistValues_InJsonFile()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"readstorm-settings-{Guid.NewGuid():N}.json");
        var sut = new JsonFileAppSettingsUseCase(filePath);

        var updated = new AppSettings
        {
            DownloadPath = "json-downloads",
            MaxConcurrency = 9,
            MinIntervalMs = 180,
            MaxIntervalMs = 420,
            ExportFormat = "epub",
            ProxyEnabled = false,
            ProxyHost = "127.0.0.1",
            ProxyPort = 7890,
        };

        await sut.SaveAsync(updated);
        var loaded = await sut.LoadAsync();

        Assert.Equal(updated.DownloadPath, loaded.DownloadPath);
        Assert.Equal(updated.MaxConcurrency, loaded.MaxConcurrency);
        Assert.Equal(updated.MinIntervalMs, loaded.MinIntervalMs);
        Assert.Equal(updated.MaxIntervalMs, loaded.MaxIntervalMs);
        Assert.Equal(updated.ExportFormat, loaded.ExportFormat);
    }

    [Fact]
    public void DownloadTask_ShouldReachSucceeded_WithValidTransitions()
    {
        var task = new DownloadTask
        {
            Id = Guid.NewGuid(),
            BookTitle = "测试小说",
            Author = "测试作者",
            Mode = DownloadMode.FullBook,
            EnqueuedAt = DateTimeOffset.Now,
        };

        task.TransitionTo(DownloadTaskStatus.Downloading);
        task.UpdateProgress(35);
        task.UpdateProgress(78);
        task.TransitionTo(DownloadTaskStatus.Succeeded);

        Assert.Equal(DownloadTaskStatus.Succeeded, task.CurrentStatus);
        Assert.Equal(100, task.ProgressPercent);
        Assert.Contains(DownloadTaskStatus.Queued, task.StateHistory);
        Assert.Contains(DownloadTaskStatus.Downloading, task.StateHistory);
        Assert.Contains(DownloadTaskStatus.Succeeded, task.StateHistory);
    }

    [Fact]
    public void DownloadTask_ShouldRejectInvalidTransition_AfterSucceeded()
    {
        var task = new DownloadTask
        {
            BookTitle = "状态机测试",
            Author = "ReadStorm",
            Mode = DownloadMode.FullBook,
        };

        task.TransitionTo(DownloadTaskStatus.Downloading);
        task.TransitionTo(DownloadTaskStatus.Succeeded);

        Assert.Throws<InvalidOperationException>(() => task.TransitionTo(DownloadTaskStatus.Downloading));
    }

    [Fact]
    public async Task RuleCatalog_ShouldLoadRules_FromCustomDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"readstorm-rules-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "rule-101.json");
        await File.WriteAllTextAsync(filePath, """
        {
          "id": 101,
          "name": "测试书源",
          "url": "https://example.com",
          "search": { "url": "https://example.com/search?q=%s" }
        }
        """);

        var sut = new EmbeddedRuleCatalogUseCase([tempDir]);
        var rules = await sut.GetAllAsync();

        var rule = Assert.Single(rules);
        Assert.Equal(101, rule.Id);
        Assert.Equal("测试书源", rule.Name);
        Assert.True(rule.SearchSupported);
    }
}
