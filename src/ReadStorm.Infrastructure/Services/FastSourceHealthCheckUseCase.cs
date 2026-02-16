using System.Net.Http;
using System.Text;
using System.Text.Json;
using ReadStorm.Application.Abstractions;
using ReadStorm.Domain.Models;

namespace ReadStorm.Infrastructure.Services;

/// <summary>
/// 并发关键词探活：对每个书源发起一次搜索请求（GET/POST），
/// 单源 3 秒超时，全量并发一次完成。
/// </summary>
public sealed class FastSourceHealthCheckUseCase : ISourceHealthCheckUseCase
{
    private static readonly TimeSpan PerSourceTimeout = TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly IReadOnlyList<string> _ruleDirectories;

    public FastSourceHealthCheckUseCase(HttpClient? httpClient = null, IReadOnlyList<string>? ruleDirectories = null)
    {
        _httpClient = httpClient ?? CreateDefaultHttpClient();
        _ruleDirectories = ruleDirectories ?? RulePathResolver.ResolveDefaultRuleDirectories();
    }

    public async Task<IReadOnlyList<SourceHealthResult>> CheckAllAsync(
        IReadOnlyList<BookSourceRule> sources,
        CancellationToken cancellationToken = default)
    {
        // 全量并发：每个书源独立超时，总耗时 ≈ 最慢的一个。
        var tasks = sources
            .Where(s => s.Id > 0 && !string.IsNullOrWhiteSpace(s.Url))
            .Select(s => PingOneAsync(s.Id, s.Url, cancellationToken))
            .ToList();

        var results = await Task.WhenAll(tasks);
        return results;
    }

    private async Task<SourceHealthResult> PingOneAsync(
        int sourceId, string url, CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(PerSourceTimeout);

            var request = await BuildSearchProbeRequestAsync(sourceId, url, "测试", cts.Token);
            if (request is null)
            {
                return new SourceHealthResult(sourceId, false);
            }

            using (request)
            {
                using var response = await _httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                var code = (int)response.StatusCode;
                return new SourceHealthResult(sourceId, code is >= 200 and < 400);
            }
        }
        catch
        {
            return new SourceHealthResult(sourceId, false);
        }
    }

    private async Task<HttpRequestMessage?> BuildSearchProbeRequestAsync(
        int sourceId,
        string fallbackUrl,
        string keyword,
        CancellationToken cancellationToken)
    {
        var rule = await LoadRuleAsync(sourceId, cancellationToken);
        var search = rule?.Search;
        if (search is null || string.IsNullOrWhiteSpace(search.Url))
        {
            // 没有搜索规则时回退到首页 GET 探活（不再使用 HEAD）
            return new HttpRequestMessage(HttpMethod.Get, fallbackUrl);
        }

        var escapedKeyword = Uri.EscapeDataString(keyword);
        var resolvedUrl = search.Url.Contains("%s", StringComparison.Ordinal)
            ? search.Url.Replace("%s", escapedKeyword, StringComparison.Ordinal)
            : search.Url;

        var isPost = string.Equals(search.Method, "post", StringComparison.OrdinalIgnoreCase);
        var request = new HttpRequestMessage(isPost ? HttpMethod.Post : HttpMethod.Get, resolvedUrl);

        if (!string.IsNullOrWhiteSpace(search.Cookies))
        {
            request.Headers.TryAddWithoutValidation("Cookie", search.Cookies);
        }

        if (isPost)
        {
            var payload = BuildFormBody(search.Data, keyword);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/x-www-form-urlencoded");
        }

        return request;
    }

    private async Task<RuleFileDto?> LoadRuleAsync(int sourceId, CancellationToken cancellationToken)
    {
        var filePath = _ruleDirectories
            .Select(dir => Path.Combine(dir, $"rule-{sourceId}.json"))
            .FirstOrDefault(File.Exists);

        if (filePath is null)
        {
            return null;
        }

        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<RuleFileDto>(stream, JsonOptions, cancellationToken);
    }

    private static string BuildFormBody(string? rawData, string keyword)
    {
        if (string.IsNullOrWhiteSpace(rawData))
        {
            return $"searchkey={Uri.EscapeDataString(keyword)}";
        }

        var text = rawData.Trim();
        if (text.StartsWith("{", StringComparison.Ordinal) && text.EndsWith("}", StringComparison.Ordinal) && text.Length >= 2)
        {
            text = text[1..^1];
        }

        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var pairs = new List<string>(parts.Length);

        foreach (var part in parts)
        {
            var idx = part.IndexOf(':');
            if (idx <= 0)
            {
                continue;
            }

            var key = part[..idx].Trim().Trim('\'', '"');
            var value = part[(idx + 1)..].Trim().Trim('\'', '"');
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            value = value.Replace("%s", keyword, StringComparison.Ordinal);
            pairs.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
        }

        if (pairs.Count == 0)
        {
            return $"searchkey={Uri.EscapeDataString(keyword)}";
        }

        return string.Join("&", pairs);
    }

    private sealed class RuleFileDto
    {
        public RuleSearchDto? Search { get; set; }
    }

    private sealed class RuleSearchDto
    {
        public string? Url { get; set; }

        public string? Method { get; set; }

        public string? Data { get; set; }

        public string? Cookies { get; set; }
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = PerSourceTimeout,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            AutomaticDecompression = System.Net.DecompressionMethods.All,
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(4), // 全局兜底
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

        return client;
    }
}
