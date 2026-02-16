using System.Text.Json;
using ReadStorm.Application.Abstractions;
using ReadStorm.Domain.Models;

namespace ReadStorm.Infrastructure.Services;

public sealed class EmbeddedRuleCatalogUseCase : IRuleCatalogUseCase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IReadOnlyList<string>? _customRuleDirectories;

    public EmbeddedRuleCatalogUseCase()
    {
    }

    public EmbeddedRuleCatalogUseCase(IReadOnlyList<string> customRuleDirectories)
    {
        _customRuleDirectories = customRuleDirectories;
    }

    public async Task<IReadOnlyList<BookSourceRule>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var ruleDirs = _customRuleDirectories ?? RulePathResolver.ResolveDefaultRuleDirectories();
        var files = ruleDirs
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.GetFiles(dir, "rule-*.json", SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();

        if (files.Count == 0)
        {
            return [];
        }

        var result = new Dictionary<int, BookSourceRule>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            RuleDto? dto;
            try
            {
                await using var stream = File.OpenRead(file);
                dto = await JsonSerializer.DeserializeAsync<RuleDto>(stream, JsonOptions, cancellationToken);
            }
            catch (JsonException)
            {
                // 跳过不符合 RuleDto 结构的文件，例如 rule-unavailable.json（数组结构）
                continue;
            }
            catch (NotSupportedException)
            {
                continue;
            }

            if (dto is null)
            {
                continue;
            }

            if (dto.Id <= 0)
            {
                continue;
            }

            if (IsTestRule(dto, file))
            {
                continue;
            }

            result[dto.Id] = new BookSourceRule
            {
                Id = dto.Id,
                Name = dto.Name ?? $"Rule-{dto.Id}",
                Url = dto.Url ?? string.Empty,
                SearchSupported = dto.Search is not null,
            };
        }

        return result.Values.OrderBy(x => x.Id).ToList();
    }

    private static bool IsTestRule(RuleDto dto, string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (fileName.Contains("template", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains("unavailable", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(dto.Name)
            && dto.Name.Contains("示例", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(dto.Url)
            && dto.Url.Contains("example-source", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private sealed class RuleDto
    {
        public int Id { get; set; }

        public string? Name { get; set; }

        public string? Url { get; set; }

        public object? Search { get; set; }
    }
}
