using System.Net;
using System.Net.Http.Headers;

namespace ReadStorm.Infrastructure.Services;

/// <summary>
/// 共享的 HTTP 辅助工具——消除 4 个服务中重复的
/// <c>CreateHttpClient</c>、<c>SendWithRetryAsync</c>、<c>CloneRequestAsync</c>。
/// </summary>
internal static class RuleHttpHelper
{
    /// <summary>
    /// 创建通用 HttpClient（带 UA 和自动解压）。
    /// 各服务可传入不同的 <paramref name="timeout"/>。
    /// </summary>
    public static HttpClient CreateHttpClient(TimeSpan? timeout = null)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
        };

        var client = new HttpClient(handler)
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(15),
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

        return client;
    }

    /// <summary>
    /// 创建用于搜索的 HttpClient（通过 <see cref="ProductInfoHeaderValue"/> 设置 UA）。
    /// 保持与原 <c>RuleBasedSearchBooksUseCase</c> 一致的行为。
    /// </summary>
    public static HttpClient CreateSearchHttpClient(TimeSpan? timeout = null)
    {
        var client = new HttpClient
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(15),
        };

        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("ReadStorm", "0.1"));
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("Mozilla", "5.0"));

        return client;
    }

    /// <summary>
    /// 创建用于快速探活的 HttpClient（使用 SocketsHttpHandler，极短超时）。
    /// 保持与原 <c>FastSourceHealthCheckUseCase</c> 一致的行为。
    /// </summary>
    public static HttpClient CreateHealthCheckHttpClient(TimeSpan perSourceTimeout)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = perSourceTimeout,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            AutomaticDecompression = DecompressionMethods.All,
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(4),
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

        return client;
    }

    /// <summary>
    /// 带指数退避重试的 HTTP 发送（简单版：3 次，起步 300ms，仅针对 5xx 和网络异常）。
    /// 供 <c>RuleBasedSearchBooksUseCase</c> 等轻量场景使用。
    /// </summary>
    public static async Task<HttpResponseMessage> SendWithSimpleRetryAsync(
        HttpClient httpClient,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        var delay = TimeSpan.FromMilliseconds(300);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var clone = await CloneRequestAsync(request, cancellationToken);
                var response = await httpClient.SendAsync(clone, cancellationToken);
                if ((int)response.StatusCode >= 500 && attempt < maxAttempts)
                {
                    response.Dispose();
                    await Task.Delay(delay, cancellationToken);
                    delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
                    continue;
                }

                return response;
            }
            catch (HttpRequestException) when (attempt < maxAttempts)
            {
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
            }
            catch (TaskCanceledException) when (attempt < maxAttempts)
            {
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
            }
        }

        var fallback = await CloneRequestAsync(request, cancellationToken);
        return await httpClient.SendAsync(fallback, cancellationToken);
    }

    /// <summary>
    /// 克隆 <see cref="HttpRequestMessage"/>（因为 HttpRequestMessage 不可重发）。
    /// </summary>
    public static async Task<HttpRequestMessage> CloneRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            clone.Content = new ByteArrayContent(bytes);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }
}
