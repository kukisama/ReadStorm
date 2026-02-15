using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using ReadStorm.Application.Abstractions;
using ReadStorm.Domain.Models;

namespace ReadStorm.Infrastructure.Services;

public sealed class RuleBasedDownloadBookUseCase : IDownloadBookUseCase
{
    private const int MaxErrorTraceLines = 18;

    private static readonly Lock LogFileLock = new();

    private readonly IAppSettingsUseCase _settingsUseCase;
    private readonly HttpClient _httpClient;
    private readonly IReadOnlyList<string> _ruleDirectories;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public RuleBasedDownloadBookUseCase(
        IAppSettingsUseCase settingsUseCase,
        HttpClient? httpClient = null,
        IReadOnlyList<string>? ruleDirectories = null)
    {
        _settingsUseCase = settingsUseCase;
        _httpClient = httpClient ?? CreateHttpClient();
        _ruleDirectories = ruleDirectories ?? RulePathResolver.ResolveDefaultRuleDirectories();
    }

    public async Task QueueAsync(
        DownloadTask task,
        SearchResult selectedBook,
        DownloadMode mode,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<string>(capacity: 256);

        void Trace(string message)
        {
            var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            diagnostics.Add(line);
            AppendDiagnosticLog(line);
        }

        try
        {
            Trace($"[download-start] taskId={task.Id}, sourceId={selectedBook.SourceId}, book='{selectedBook.Title}', author='{selectedBook.Author}', mode={mode}");
            task.TransitionTo(DownloadTaskStatus.Downloading);

            if (string.IsNullOrWhiteSpace(selectedBook.Url))
            {
                throw new InvalidOperationException("当前搜索结果未包含书籍详情 URL，无法下载。");
            }

            Trace($"[input] bookUrl={selectedBook.Url}");

            var settings = await _settingsUseCase.LoadAsync(cancellationToken);
            Trace($"[settings] downloadPath='{settings.DownloadPath}', exportFormat='{settings.ExportFormat}', proxyEnabled={settings.ProxyEnabled}");

            var rule = await LoadRuleAsync(selectedBook.SourceId, cancellationToken)
                       ?? throw new InvalidOperationException($"未找到书源 {selectedBook.SourceId} 对应规则文件。");
            Trace($"[rule] sourceId={rule.Id}, baseUrl='{rule.Url}', toc-item='{rule.Toc?.Item}', chapter-content='{rule.Chapter?.Content}'");

            var chapters = await FetchTocAsync(rule, selectedBook, mode, cancellationToken, Trace);
            Trace($"[toc] parsedChapters={chapters.Count}");
            if (chapters.Count == 0)
            {
                throw new InvalidOperationException("章节目录为空，无法下载。");
            }

            var chapterTexts = new List<(string Title, string Content)>(chapters.Count);
            for (var i = 0; i < chapters.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var chapter = chapters[i];
                Trace($"[chapter-fetch-start] index={i + 1}/{chapters.Count}, title='{chapter.Title}', url='{chapter.Url}'");
                var content = await FetchChapterContentAsync(rule, chapter.Url, cancellationToken, Trace);
                if (string.IsNullOrWhiteSpace(content))
                {
                    Trace($"[chapter-fetch-empty] index={i + 1}/{chapters.Count}, title='{chapter.Title}'");
                    continue;
                }

                chapterTexts.Add((chapter.Title, content));
                var percent = (int)Math.Round(((i + 1d) / chapters.Count) * 100d);
                task.UpdateProgress(percent);
                Trace($"[chapter-fetch-ok] index={i + 1}/{chapters.Count}, chars={content.Length}, progress={percent}%");
            }

            Trace($"[chapter-summary] nonEmptyChapters={chapterTexts.Count}, totalCandidates={chapters.Count}");
            if (chapterTexts.Count == 0)
            {
                throw new InvalidOperationException("正文抓取结果为空。");
            }

            var outputPath = string.Equals(settings.ExportFormat, "epub", StringComparison.OrdinalIgnoreCase)
                ? await EpubExporter.ExportAsync(settings.DownloadPath, selectedBook.Title, selectedBook.Author, selectedBook.SourceId, chapterTexts, cancellationToken)
                : await ExportTxtAsync(settings, selectedBook, chapterTexts, cancellationToken);
            task.OutputFilePath = outputPath;
            task.TransitionTo(DownloadTaskStatus.Succeeded);
            Trace($"[export-ok] output='{outputPath}', finalStatus={task.CurrentStatus}");
            Trace($"[download-end] taskId={task.Id}, elapsed={(DateTimeOffset.Now - task.EnqueuedAt).TotalSeconds:F2}s");
            return;
        }
        catch (OperationCanceledException)
        {
            task.TransitionTo(DownloadTaskStatus.Cancelled);
            task.ErrorKind = DownloadErrorKind.Cancelled;
            task.Error = "任务已取消";
            Trace($"[download-cancelled] taskId={task.Id}");
            return;
        }
        catch (Exception ex)
        {
            if (task.CurrentStatus == DownloadTaskStatus.Queued)
            {
                task.TransitionTo(DownloadTaskStatus.Failed);
            }
            else if (task.CurrentStatus == DownloadTaskStatus.Downloading)
            {
                task.TransitionTo(DownloadTaskStatus.Failed);
            }

            task.ErrorKind = Classify(ex);
            task.Error = BuildErrorWithDiagnostics(ex.Message, diagnostics);
            Trace($"[download-failed] taskId={task.Id}, kind={task.ErrorKind}, error={ex.Message}");
            return;
        }
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

    private async Task<IReadOnlyList<ChapterRef>> FetchTocAsync(
        RuleFileDto rule,
        SearchResult book,
        DownloadMode mode,
        CancellationToken cancellationToken,
        Action<string>? trace = null)
    {
        var toc = rule.Toc;
        if (toc is null || string.IsNullOrWhiteSpace(toc.Item))
        {
            trace?.Invoke("[toc] rule.toc.item is empty");
            return [];
        }

        var firstUrl = BuildTocUrl(rule, book.Url);
        if (string.IsNullOrWhiteSpace(firstUrl))
        {
            trace?.Invoke("[toc] first toc url is empty");
            return [];
        }

        trace?.Invoke($"[toc] firstPageUrl='{firstUrl}'");

        var pageUrls = new List<string> { firstUrl };
        var firstHtml = await FetchHtmlByUrlAsync(firstUrl, cancellationToken, trace);
        if (string.IsNullOrWhiteSpace(firstHtml))
        {
            trace?.Invoke("[toc] first page html is empty");
            return [];
        }

        if (toc.Pagination && !string.IsNullOrWhiteSpace(toc.NextPage))
        {
            var extraUrls = ExtractNextPageUrls(firstHtml, firstUrl, toc.NextPage, 2);
            pageUrls.AddRange(extraUrls);
            trace?.Invoke($"[toc] pagination enabled, extraPages={extraUrls.Count}");
        }

        var allItems = new List<ChapterRef>();
        foreach (var pageUrl in pageUrls.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            trace?.Invoke($"[toc-page] parsing='{pageUrl}'");
            var html = pageUrl == firstUrl ? firstHtml : await FetchHtmlByUrlAsync(pageUrl, cancellationToken, trace);
            if (string.IsNullOrWhiteSpace(html))
            {
                trace?.Invoke($"[toc-page] empty html, skip='{pageUrl}'");
                continue;
            }

            var refs = ParseTocPage(html, pageUrl, toc.Item, toc.Offset ?? 0);
            trace?.Invoke($"[toc-page] parsedItems={refs.Count}, page='{pageUrl}'");
            allItems.AddRange(refs);
        }

        allItems = allItems
            .GroupBy(x => x.Url, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        if (toc.Desc)
        {
            allItems.Reverse();
            trace?.Invoke("[toc] chapter order reversed by rule.desc=true");
        }

        var limited = mode switch
        {
            DownloadMode.FullBook => allItems.Take(80).ToList(),
            DownloadMode.LatestN => allItems.TakeLast(20).ToList(),
            _ => allItems.Take(40).ToList(),
        };

        for (var i = 0; i < limited.Count; i++)
        {
            limited[i] = limited[i] with { Order = i + 1 };
        }

        trace?.Invoke($"[toc] deduplicated={allItems.Count}, mode={mode}, selected={limited.Count}");

        return limited;
    }

    private static List<ChapterRef> ParseTocPage(string html, string pageUrl, string itemSelectorRaw, int offset)
    {
        var parser = new HtmlParser();
        var doc = parser.ParseDocument(html);

        var itemSelector = NormalizeSelector(itemSelectorRaw);
        if (string.IsNullOrWhiteSpace(itemSelector))
        {
            return [];
        }

        var nodes = doc.QuerySelectorAll(itemSelector).ToList();
        if (offset > 0 && nodes.Count > offset)
        {
            nodes = nodes.Skip(offset).ToList();
        }
        else if (offset < 0 && nodes.Count > -offset)
        {
            nodes = nodes.Take(nodes.Count + offset).ToList();
        }

        var result = new List<ChapterRef>(nodes.Count);
        foreach (var node in nodes)
        {
            var title = node.TextContent?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var href = node.GetAttribute("href") ?? string.Empty;
            var url = ResolveUrl(pageUrl, href);
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            result.Add(new ChapterRef(0, title, url));
        }

        return result;
    }

    private async Task<string> FetchChapterContentAsync(
        RuleFileDto rule,
        string chapterUrl,
        CancellationToken cancellationToken,
        Action<string>? trace = null)
    {
        var chapter = rule.Chapter;
        if (chapter is null || string.IsNullOrWhiteSpace(chapter.Content))
        {
            trace?.Invoke($"[chapter] content selector empty, url='{chapterUrl}'");
            return string.Empty;
        }

        var contentSelector = NormalizeSelector(chapter.Content);
        if (string.IsNullOrWhiteSpace(contentSelector))
        {
            trace?.Invoke($"[chapter] normalized selector empty, raw='{chapter.Content}', url='{chapterUrl}'");
            return string.Empty;
        }

        var html = await FetchHtmlByUrlAsync(chapterUrl, cancellationToken, trace);
        if (string.IsNullOrWhiteSpace(html))
        {
            trace?.Invoke($"[chapter] html empty, url='{chapterUrl}'");
            return string.Empty;
        }

        var pages = new List<string> { html };
        if (chapter.Pagination && !string.IsNullOrWhiteSpace(chapter.NextPage))
        {
            var nextUrls = ExtractNextPageUrls(html, chapterUrl, chapter.NextPage, 5);
            trace?.Invoke($"[chapter] pagination nextPages={nextUrls.Count}, url='{chapterUrl}'");
            foreach (var next in nextUrls)
            {
                var nextHtml = await FetchHtmlByUrlAsync(next, cancellationToken, trace);
                if (!string.IsNullOrWhiteSpace(nextHtml))
                {
                    pages.Add(nextHtml);
                }
            }
        }

        var full = new StringBuilder();
        foreach (var page in pages)
        {
            var text = ExtractTextFromContent(page, contentSelector);
            if (!string.IsNullOrWhiteSpace(text))
            {
                full.AppendLine(text);
            }
        }

        var content = full.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(chapter.FilterTxt))
        {
            try
            {
                content = Regex.Replace(content, chapter.FilterTxt, string.Empty, RegexOptions.Multiline);
                trace?.Invoke($"[chapter] filter applied, pattern='{chapter.FilterTxt}', url='{chapterUrl}'");
            }
            catch
            {
                // ignore bad regex from rule
                trace?.Invoke($"[chapter] bad filter regex ignored, pattern='{chapter.FilterTxt}', url='{chapterUrl}'");
            }
        }

        trace?.Invoke($"[chapter] extractedChars={content.Length}, pages={pages.Count}, url='{chapterUrl}'");

        return content.Trim();
    }

    private static string ExtractTextFromContent(string html, string contentSelector)
    {
        var parser = new HtmlParser();
        var doc = parser.ParseDocument(html);
        var node = doc.QuerySelector(contentSelector);
        if (node is null)
        {
            return string.Empty;
        }

        var innerHtml = node.InnerHtml
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</p>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<p>", string.Empty, StringComparison.OrdinalIgnoreCase);

        var textDoc = parser.ParseDocument($"<div>{innerHtml}</div>");
        var text = textDoc.Body?.TextContent ?? string.Empty;
        text = Regex.Replace(text, "\\n{3,}", "\n\n");
        return text.Trim();
    }

    private static async Task<string> ExportTxtAsync(
        AppSettings settings,
        SearchResult book,
        IReadOnlyList<(string Title, string Content)> chapters,
        CancellationToken cancellationToken)
    {
        var downloadPath = settings.DownloadPath;
        if (!Path.IsPathRooted(downloadPath))
        {
            downloadPath = Path.Combine(AppContext.BaseDirectory, downloadPath);
        }

        Directory.CreateDirectory(downloadPath);

        var safeName = SanitizeFileName($"{book.Title}({book.Author}).txt");
        var outputPath = Path.Combine(downloadPath, safeName);

        await using var writer = new StreamWriter(outputPath, false, new UTF8Encoding(false));
        await writer.WriteLineAsync($"书名：{book.Title}");
        await writer.WriteLineAsync($"作者：{book.Author}");
        await writer.WriteLineAsync($"书源：{book.SourceId}");
        await writer.WriteLineAsync();

        foreach (var chapter in chapters)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await writer.WriteLineAsync(chapter.Title);
            await writer.WriteLineAsync();
            await writer.WriteLineAsync(chapter.Content);
            await writer.WriteLineAsync();
        }

        return outputPath;
    }

    private static string BuildTocUrl(RuleFileDto rule, string bookUrl)
    {
        var toc = rule.Toc;
        if (toc is null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(toc.Url) && toc.Url.Contains("%s", StringComparison.Ordinal))
        {
            var path = Uri.TryCreate(bookUrl, UriKind.Absolute, out var uri)
                ? uri.AbsolutePath
                : bookUrl;

            var idMatch = Regex.Match(path, "(\\d+)(?!.*\\d)");
            if (idMatch.Success)
            {
                return toc.Url.Replace("%s", idMatch.Groups[1].Value, StringComparison.Ordinal);
            }
        }

        return bookUrl;
    }

    private static List<string> ExtractNextPageUrls(string html, string pageUrl, string selectorRaw, int maxPages)
    {
        var parser = new HtmlParser();
        var doc = parser.ParseDocument(html);
        var selector = NormalizeSelector(selectorRaw);
        if (string.IsNullOrWhiteSpace(selector))
        {
            return [];
        }

        var result = new List<string>();
        foreach (var node in doc.QuerySelectorAll(selector))
        {
            if (result.Count >= maxPages)
            {
                break;
            }

            var href = node.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href))
            {
                href = node.GetAttribute("value");
            }

            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            var url = ResolveUrl(pageUrl, href);
            if (string.IsNullOrWhiteSpace(url) || result.Contains(url, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(url);
        }

        return result;
    }

    private async Task<string> FetchHtmlByUrlAsync(string url, CancellationToken cancellationToken, Action<string>? trace = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await SendWithRetryAsync(request, cancellationToken, trace);
        if (!response.IsSuccessStatusCode)
        {
            trace?.Invoke($"[http] GET {url} -> {(int)response.StatusCode} {response.StatusCode}");
            return string.Empty;
        }

        trace?.Invoke($"[http] GET {url} -> {(int)response.StatusCode}");
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static string NormalizeSelector(string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return string.Empty;
        }

        var idx = selector.IndexOf("@js:", StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            selector = selector[..idx];
        }

        return selector.Trim();
    }

    private static string ResolveUrl(string baseUrl, string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            && Uri.TryCreate(baseUri, url, out var merged))
        {
            return merged.ToString();
        }

        return string.Empty;
    }

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

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15),
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        return client;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken,
        Action<string>? trace = null)
    {
        const int maxAttempts = 3;
        var delay = TimeSpan.FromMilliseconds(300);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var clone = await CloneRequestAsync(request, cancellationToken);
                trace?.Invoke($"[http-attempt] method={clone.Method}, url={clone.RequestUri}, attempt={attempt}/{maxAttempts}");
                var response = await _httpClient.SendAsync(clone, cancellationToken);
                if ((int)response.StatusCode >= 500 && attempt < maxAttempts)
                {
                    trace?.Invoke($"[http-retry] status={(int)response.StatusCode}, waitMs={delay.TotalMilliseconds:F0}, attempt={attempt}");
                    response.Dispose();
                    await Task.Delay(delay, cancellationToken);
                    delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
                    continue;
                }

                return response;
            }
            catch (HttpRequestException) when (attempt < maxAttempts)
            {
                trace?.Invoke($"[http-retry] HttpRequestException, waitMs={delay.TotalMilliseconds:F0}, attempt={attempt}");
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
            }
            catch (TaskCanceledException) when (attempt < maxAttempts)
            {
                trace?.Invoke($"[http-retry] TaskCanceledException, waitMs={delay.TotalMilliseconds:F0}, attempt={attempt}");
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
            }
        }

        var fallback = await CloneRequestAsync(request, cancellationToken);
        trace?.Invoke($"[http-fallback] method={fallback.Method}, url={fallback.RequestUri}");
        return await _httpClient.SendAsync(fallback, cancellationToken);
    }

    private static string BuildErrorWithDiagnostics(string message, IReadOnlyList<string> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            return message;
        }

        var tail = string.Join(Environment.NewLine, diagnostics.TakeLast(MaxErrorTraceLines));
        return $"{message}{Environment.NewLine}[诊断片段]{Environment.NewLine}{tail}";
    }

    public static string GetLogFilePath()
    {
        var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logDir);
        return Path.Combine(logDir, "readstorm-download.log");
    }

    private static void AppendDiagnosticLog(string line)
    {
        try
        {
            lock (LogFileLock)
            {
                File.AppendAllText(GetLogFilePath(), line + Environment.NewLine, Encoding.UTF8);
            }

            Console.WriteLine(line);
        }
        catch
        {
            // 避免日志写入异常影响主流程。
        }
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
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

    private static DownloadErrorKind Classify(Exception ex)
    {
        return ex switch
        {
            HttpRequestException => DownloadErrorKind.Network,
            JsonException => DownloadErrorKind.Rule,
            RegexParseException => DownloadErrorKind.Rule,
            IOException => DownloadErrorKind.IO,
            InvalidOperationException => DownloadErrorKind.Parse,
            OperationCanceledException => DownloadErrorKind.Cancelled,
            _ => DownloadErrorKind.Unknown,
        };
    }

    private sealed record ChapterRef(int Order, string Title, string Url);

    private sealed class RuleFileDto
    {
        public int Id { get; set; }

        public string? Url { get; set; }

        public RuleTocDto? Toc { get; set; }

        public RuleChapterDto? Chapter { get; set; }
    }

    private sealed class RuleTocDto
    {
        public string? Url { get; set; }

        public string? Item { get; set; }

        public bool Pagination { get; set; }

        public string? NextPage { get; set; }

        public int? Offset { get; set; }

        public bool Desc { get; set; }
    }

    private sealed class RuleChapterDto
    {
        public string? Content { get; set; }

        public bool Pagination { get; set; }

        public string? NextPage { get; set; }

        public string? FilterTxt { get; set; }
    }
}
