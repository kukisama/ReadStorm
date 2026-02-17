using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using ReadStorm.Application.Abstractions;
using ReadStorm.Domain.Models;

namespace ReadStorm.Infrastructure.Services;

/// <summary>
/// 封面管理服务——从 <see cref="RuleBasedDownloadBookUseCase"/> 中拆出，
/// 负责封面候选提取、下载、存储。
/// </summary>
public sealed class CoverService : ICoverUseCase
{
    private const int CoverTimeoutSeconds = 6;
    private static readonly Lock LogFileLock = new();

    private readonly IBookRepository _bookRepo;
    private readonly HttpClient _httpClient;

    public CoverService(IBookRepository bookRepo, HttpClient? httpClient = null)
    {
        _bookRepo = bookRepo;
        _httpClient = httpClient ?? RuleHttpHelper.CreateHttpClient();
    }

    // ==================== Public API ====================

    /// <inheritdoc />
    public async Task<string> RefreshCoverAsync(BookEntity book, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        sb.Append($"[封面诊断] 《{book.Title}》");
        CoverLog($"========== 开始刷新封面: 《{book.Title}》 TocUrl={book.TocUrl} ==========");

        var candidates = await GetCoverCandidatesAsync(book, cancellationToken);
        sb.Append($"  候选数量:{candidates.Count}");
        CoverLog($"候选数量: {candidates.Count}");
        if (candidates.Count == 0)
        {
            sb.Append("  ❌ 未找到可用候选图片");
            CoverLog("❌ 未找到可用候选图片");
            return sb.ToString();
        }

        foreach (var candidate in candidates)
        {
            CoverLog($"尝试候选 [{candidate.Index}] {candidate.Rule}: {candidate.ImageUrl}");
            var referer = GetCoverReferer(book.TocUrl, candidate);
            var (bytes, extension, failReason) = await DownloadCoverBytesAsync(candidate.ImageUrl, referer, cancellationToken);
            if (bytes.Length == 0)
            {
                sb.Append($"  候选失败:[{candidate.Index}]{candidate.Rule}—{failReason}");
                CoverLog($"候选失败 [{candidate.Index}] {candidate.Rule}: {failReason}");
                continue;
            }

            book.CoverUrl = candidate.ImageUrl;
            book.CoverBlob = bytes;
            book.CoverImage = string.Empty;
            book.CoverRule = $"auto:{candidate.Rule}|{candidate.HtmlSnippet}";
            await _bookRepo.UpsertBookAsync(book, cancellationToken);
            SaveCoverToLocal(book, bytes, extension);

            sb.Append($"  ✅ [{candidate.Index}]{candidate.Rule} blob={bytes.Length}B");
            CoverLog($"✅ 成功 [{candidate.Index}] {candidate.Rule} blob={bytes.Length}B url={candidate.ImageUrl}");
            return sb.ToString();
        }

        sb.Append("  ❌ 所有候选下载失败");
        CoverLog("❌ 所有候选下载失败");
        return sb.ToString();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CoverCandidate>> GetCoverCandidatesAsync(BookEntity book, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(book.TocUrl)) return [];
        var html = await FetchCoverHtmlAsync(book.TocUrl, cancellationToken);
        CoverLog($"TocUrl HTML长度={html.Length} url={book.TocUrl}");

        var merged = ExtractCoverCandidates(html, book.TocUrl).ToList();
        var qidianFallbacks = await GetQidianSearchCoverCandidatesAsync(book.Title, cancellationToken);

        foreach (var candidate in qidianFallbacks)
        {
            if (merged.Any(x => string.Equals(x.ImageUrl, candidate.ImageUrl, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            candidate.Index = merged.Count + 1;
            merged.Add(candidate);
        }

        return merged;
    }

    /// <inheritdoc />
    public async Task<string> ApplyCoverCandidateAsync(BookEntity book, CoverCandidate candidate, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(candidate.ImageUrl))
        {
            return "❌ 候选图片URL为空。";
        }

        var referer = GetCoverReferer(book.TocUrl, candidate);
        var (bytes, extension, failReason) = await DownloadCoverBytesAsync(candidate.ImageUrl, referer, cancellationToken);
        if (bytes.Length == 0)
        {
            return $"❌ 下载封面失败：{failReason}";
        }

        book.CoverUrl = candidate.ImageUrl;
        book.CoverBlob = bytes;
        book.CoverImage = string.Empty;
        book.CoverRule = $"manual:{candidate.Rule}|{candidate.HtmlSnippet}";
        await _bookRepo.UpsertBookAsync(book, cancellationToken);
        var localPath = SaveCoverToLocal(book, bytes, extension);
        return $"✅ 已设置封面，blobLen={bytes.Length}，本地文件：{localPath}";
    }

    // ==================== Internal (for QueueAsync inline cover) ====================

    /// <summary>
    /// 提取封面候选列表，供 <see cref="RuleBasedDownloadBookUseCase.QueueAsync"/> 内联调用。
    /// </summary>
    internal IReadOnlyList<CoverCandidate> ExtractCoverCandidatesFromHtml(string html, string pageUrl)
        => ExtractCoverCandidates(html, pageUrl);

    /// <summary>
    /// 获取起点搜索封面候选，供 <see cref="RuleBasedDownloadBookUseCase.QueueAsync"/> 内联调用。
    /// </summary>
    internal Task<IReadOnlyList<CoverCandidate>> GetQidianCandidatesAsync(string title, CancellationToken ct)
        => GetQidianSearchCoverCandidatesAsync(title, ct);

    /// <summary>
    /// 下载封面字节，供 <see cref="RuleBasedDownloadBookUseCase.QueueAsync"/> 内联调用。
    /// </summary>
    internal Task<(byte[] Bytes, string Extension, string FailReason)> DownloadCoverAsync(
        string coverUrl, string? refererUrl, CancellationToken ct)
        => DownloadCoverBytesAsync(coverUrl, refererUrl, ct);

    /// <summary>获取封面 Referer。</summary>
    internal static string GetReferer(string tocUrl, CoverCandidate candidate)
        => GetCoverReferer(tocUrl, candidate);

    /// <summary>保存封面到本地磁盘。</summary>
    internal static string SaveToLocal(BookEntity book, byte[] bytes, string extension)
        => SaveCoverToLocal(book, bytes, extension);

    // ==================== HTTP Helpers ====================

    /// <summary>封面专用的快速 HTTP GET，短超时、不重试。</summary>
    private async Task<HttpResponseMessage> SendCoverRequestAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(CoverTimeoutSeconds));
        return await _httpClient.SendAsync(request, cts.Token);
    }

    /// <summary>封面专用的快速获取 HTML，短超时、不重试。</summary>
    private async Task<string> FetchCoverHtmlAsync(string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await SendCoverRequestAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            CoverLog($"[fetch] GET {url} -> {(int)response.StatusCode}");
            return string.Empty;
        }
        return await response.Content.ReadAsStringAsync(ct);
    }

    // ==================== Candidate Extraction ====================

    private static IReadOnlyList<CoverCandidate> ExtractCoverCandidates(string html, string pageUrl)
    {
        var result = new List<CoverCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var parser = new HtmlParser();
            using var doc = parser.ParseDocument(html);

            void AddCandidate(string? rawUrl, string rule, string htmlSnippet)
            {
                if (string.IsNullOrWhiteSpace(rawUrl)) return;
                var abs = RuleFileLoader.ResolveUrl(pageUrl, rawUrl);
                if (string.IsNullOrWhiteSpace(abs)) return;
                if (!seen.Add(abs)) return;

                result.Add(new CoverCandidate
                {
                    Index = result.Count + 1,
                    ImageUrl = abs,
                    Rule = rule,
                    HtmlSnippet = htmlSnippet.Length > 1000 ? htmlSnippet[..1000] : htmlSnippet,
                });
            }

            var metaOg = doc.QuerySelector("meta[property='og:image']");
            AddCandidate(metaOg?.GetAttribute("content"), "meta[property='og:image']", metaOg?.OuterHtml ?? string.Empty);

            var metaOgName = doc.QuerySelector("meta[name='og:image']");
            AddCandidate(metaOgName?.GetAttribute("content"), "meta[name='og:image']", metaOgName?.OuterHtml ?? string.Empty);

            foreach (var img in doc.QuerySelectorAll("img"))
            {
                var src = img.GetAttribute("src")
                       ?? img.GetAttribute("data-src")
                       ?? img.GetAttribute("data-original");

                if (string.IsNullOrWhiteSpace(src)) continue;

                var lower = src.ToLowerInvariant();
                if (lower.Contains(".jpg") || lower.Contains(".jpeg") || lower.Contains(".png") || lower.Contains(".webp") || lower.Contains("cover") || lower.Contains("book"))
                {
                    AddCandidate(src, "img[src/data-src/data-original]", img.OuterHtml);
                }
            }

            // 回退：如果上面没有命中，取页面前 20 个 img 的首批
            if (result.Count == 0)
            {
                foreach (var img in doc.QuerySelectorAll("img").Take(20))
                {
                    var src = img.GetAttribute("src")
                           ?? img.GetAttribute("data-src")
                           ?? img.GetAttribute("data-original");
                    AddCandidate(src, "img:first-batch", img.OuterHtml);
                    if (result.Count >= 12) break;
                }
            }
        }
        catch
        {
            // ignore parse errors
        }

        return result;
    }

    private async Task<IReadOnlyList<CoverCandidate>> GetQidianSearchCoverCandidatesAsync(string title, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title)) return [];

        try
        {
            var encoded = Uri.EscapeDataString(title.Trim());

            // 使用起点移动端搜索，反爬比 PC 端宽松
            var searchUrl = $"https://m.qidian.com/soushu/{encoded}.html";

            using var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            // 移动端 UA
            request.Headers.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36");
            request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
            request.Headers.TryAddWithoutValidation("Referer", "https://m.qidian.com/");

            using var response = await SendCoverRequestAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                CoverLog($"[qidian] 搜索页 HTTP {(int)response.StatusCode}: {searchUrl}");
                return [];
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(html))
            {
                CoverLog($"[qidian] 搜索页返回空 HTML: {searchUrl}");
                return [];
            }

            CoverLog($"[qidian] 搜索页 HTML 长度={html.Length}, url={searchUrl}");
            if (html.Length < 1000)
            {
                // 短响应通常是反爬验证页，记录内容便于诊断
                CoverLog($"[qidian] 短响应内容: {html[..Math.Min(html.Length, 500)]}");
            }

            var candidates = new List<CoverCandidate>();

            // 策略1: AngleSharp解析 img 标签
            var parser = new HtmlParser();
            using var doc = parser.ParseDocument(html);

            // 遍历所有 img，找封面 CDN 图片
            foreach (var img in doc.QuerySelectorAll("img"))
            {
                var raw = img.GetAttribute("src")
                          ?? img.GetAttribute("data-src")
                          ?? img.GetAttribute("data-original");
                if (string.IsNullOrWhiteSpace(raw)) continue;

                var abs = RuleFileLoader.ResolveUrl(searchUrl, raw);
                if (IsLikelyCoverUrl(abs))
                {
                    candidates.Add(new CoverCandidate
                    {
                        Index = candidates.Count + 1,
                        ImageUrl = abs!,
                        Rule = "qidian:search:img",
                        HtmlSnippet = searchUrl,
                    });
                    break; // 只取第一个
                }
            }

            // 策略2: 正则从 HTML 源码中提取封面CDN链接
            if (candidates.Count == 0)
            {
                var regex = new Regex(
                    @"https?://bookcover\.yuewen\.com/qdbimg/[^\""""'\s<>]+",
                    RegexOptions.IgnoreCase);
                var matches = regex.Matches(html);
                CoverLog($"[qidian] 正则匹配 bookcover.yuewen.com 结果数: {matches.Count}");

                foreach (Match m in matches)
                {
                    var abs = RuleFileLoader.ResolveUrl(searchUrl, m.Value);
                    if (IsLikelyCoverUrl(abs))
                    {
                        candidates.Add(new CoverCandidate
                        {
                            Index = candidates.Count + 1,
                            ImageUrl = abs!,
                            Rule = "qidian:search:regex-cdn",
                            HtmlSnippet = searchUrl,
                        });
                        break; // 只取第一个
                    }
                }
            }

            // 策略3: 尝试从搜索结果获取 bookId，然后构建常见尺寸封面URL
            if (candidates.Count == 0)
            {
                var bookIdMatch = Regex.Match(html, @"/book/(\d{5,15})", RegexOptions.IgnoreCase);
                if (bookIdMatch.Success)
                {
                    var bookId = bookIdMatch.Groups[1].Value;
                    CoverLog($"[qidian] 提取到 bookId={bookId}");

                    var cdnPatterns = new[]
                    {
                        $"https://bookcover.yuewen.com/qdbimg/349573/{bookId}/300.webp",
                        $"https://bookcover.yuewen.com/qdbimg/349573/{bookId}/150.webp",
                        $"https://bookcover.yuewen.com/qdbimg/349573/{bookId}/180.webp",
                    };
                    foreach (var url in cdnPatterns)
                    {
                        candidates.Add(new CoverCandidate
                        {
                            Index = candidates.Count + 1,
                            ImageUrl = url,
                            Rule = "qidian:search:bookid-cdn",
                            HtmlSnippet = searchUrl,
                        });
                        break; // 只取一个尺寸
                    }
                }
            }

            CoverLog($"[qidian] 最终候选数: {candidates.Count}");
            return candidates;
        }
        catch (OperationCanceledException)
        {
            CoverLog($"[qidian] 超时 ({CoverTimeoutSeconds}s)");
            return [];
        }
        catch (Exception ex)
        {
            CoverLog($"[qidian] 异常: {ex.Message}");
            return [];
        }
    }

    // ==================== Download & Storage ====================

    private static string GetCoverReferer(string tocUrl, CoverCandidate candidate)
    {
        if (candidate.Rule.StartsWith("qidian:", StringComparison.OrdinalIgnoreCase))
        {
            return candidate.HtmlSnippet;
        }

        return tocUrl;
    }

    /// <summary>下载封面图片原始字节（用于 BLOB 存储）并推断扩展名。只尝试一次，不做多 Referer 兜底。</summary>
    private async Task<(byte[] Bytes, string Extension, string FailReason)> DownloadCoverBytesAsync(string coverUrl, string? refererUrl, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, coverUrl);
            request.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
            request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");
            request.Headers.TryAddWithoutValidation("Pragma", "no-cache");

            if (Uri.TryCreate(refererUrl, UriKind.Absolute, out var refUri))
            {
                request.Headers.Referrer = refUri;
                request.Headers.TryAddWithoutValidation("Origin", $"{refUri.Scheme}://{refUri.Host}");
            }

            using var response = await SendCoverRequestAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return ([], ".jpg", $"HTTP {(int)response.StatusCode}");

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0)
                return ([], ".jpg", "响应体为空");
            if (bytes.Length > 500_000)
                return ([], ".jpg", $"文件过大 ({bytes.Length} 字节)");

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (!IsLikelyImagePayload(bytes, mediaType))
                return ([], ".jpg", $"非图片内容 (type={mediaType ?? "null"}, head={BitConverter.ToString(bytes[..Math.Min(4, bytes.Length)])})");

            var ext = GuessImageExtension(coverUrl, mediaType, bytes);
            CoverLog($"[download] 成功 {coverUrl} -> {bytes.Length}B {ext}");
            return (bytes, ext, string.Empty);
        }
        catch (OperationCanceledException)
        {
            return ([], ".jpg", $"超时 ({CoverTimeoutSeconds}s)");
        }
        catch (Exception ex)
        {
            return ([], ".jpg", $"异常: {ex.Message}");
        }
    }

    private static string SaveCoverToLocal(BookEntity book, byte[] bytes, string extension)
    {
        var ext = string.IsNullOrWhiteSpace(extension) ? ".jpg" : extension;
        var workDir = WorkDirectoryManager.GetCurrentWorkDirectoryFromSettings();
        var dir = WorkDirectoryManager.GetCoversDirectory(workDir);
        Directory.CreateDirectory(dir);

        var shortId = string.IsNullOrWhiteSpace(book.Id)
            ? "book"
            : (book.Id.Length > 8 ? book.Id[..8] : book.Id);
        var fileName = $"{SanitizeFileName(book.Title)}-{shortId}{ext}";
        var filePath = Path.Combine(dir, fileName);
        File.WriteAllBytes(filePath, bytes);
        return filePath;
    }

    // ==================== Image Utilities ====================

    private static string GuessImageExtension(string coverUrl, string? mediaType, byte[]? bytes = null)
    {
        if (bytes is { Length: > 11 })
        {
            if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return ".jpg";
            if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return ".png";
            if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) return ".gif";
            if (bytes[0] == 0x42 && bytes[1] == 0x4D) return ".bmp";
            if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
                && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
                return ".webp";
        }

        if (!string.IsNullOrWhiteSpace(mediaType))
        {
            var mt = mediaType.ToLowerInvariant();
            if (mt.Contains("png")) return ".png";
            if (mt.Contains("webp")) return ".webp";
            if (mt.Contains("gif")) return ".gif";
            if (mt.Contains("bmp")) return ".bmp";
            if (mt.Contains("jpeg") || mt.Contains("jpg")) return ".jpg";
        }

        try
        {
            if (Uri.TryCreate(coverUrl, UriKind.Absolute, out var uri))
            {
                var uriExt = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
                if (uriExt is ".jpg" or ".jpeg") return ".jpg";
                if (uriExt is ".png" or ".webp" or ".gif" or ".bmp") return uriExt;
            }
        }
        catch
        {
            // ignore and fallback
        }

        return ".jpg";
    }

    private static bool IsLikelyImagePayload(byte[] bytes, string? mediaType)
    {
        if (bytes.Length < 4) return false;

        var isJpeg = bytes.Length > 2 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
        var isPng = bytes.Length > 7 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
                                  && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;
        var isGif = bytes.Length > 5 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46
                                  && bytes[3] == 0x38 && (bytes[4] == 0x37 || bytes[4] == 0x39) && bytes[5] == 0x61;
        var isBmp = bytes.Length > 1 && bytes[0] == 0x42 && bytes[1] == 0x4D;
        var isWebp = bytes.Length > 11
                     && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
                     && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50;

        if (isJpeg || isPng || isGif || isBmp || isWebp) return true;

        // 若 content-type 明确是 text/html，几乎可以判定为反盗链返回页。
        if (!string.IsNullOrWhiteSpace(mediaType)
            && mediaType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return false;
    }

    private static bool IsLikelyCoverUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        var lower = url.ToLowerInvariant();

        // 起点搜索页常见用户头像占位，不是书封面。
        if (lower.Contains("/images/user.") || lower.Contains("user.bcb60")) return false;

        return lower.Contains("bookcover")
               || lower.Contains("qdbimg")
               || lower.Contains("cover")
               || lower.EndsWith(".jpg")
               || lower.EndsWith(".jpeg")
               || lower.EndsWith(".png")
               || lower.EndsWith(".webp");
    }

    // ==================== Logging ====================

    private static void CoverLog(string message)
    {
        try
        {
            var workDir = WorkDirectoryManager.GetCurrentWorkDirectoryFromSettings();
            var coverLogPath = Path.Combine(WorkDirectoryManager.GetLogsDirectory(workDir), "cover.log");
            var dir = Path.GetDirectoryName(coverLogPath)!;
            Directory.CreateDirectory(dir);
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
            lock (LogFileLock)
            {
                File.AppendAllText(coverLogPath, line);
            }
        }
        catch
        {
            // 日志写入失败不影响主流程
        }
    }

    // ==================== Private Helpers ====================

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(fileName.Length);
        foreach (var ch in fileName)
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }

        return builder.ToString();
    }
}
