using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using ReadStorm.Application.Abstractions;
using ReadStorm.Domain.Models;

namespace ReadStorm.Infrastructure.Services;

/// <summary>
/// 规则编辑器的 Infrastructure 实现：加载/保存/删除/测试书源规则。
/// </summary>
public sealed class FileBasedRuleEditorUseCase : IRuleEditorUseCase, IDisposable
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly HttpClient _httpClient;

    /// <summary>规则文件的写入目标目录（优先使用第一个可写目录）。</summary>
    private string? _writeDirectory;

    public FileBasedRuleEditorUseCase()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
        };
        _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
    }

    public void Dispose() => _httpClient.Dispose();

    private IReadOnlyList<string> GetRuleDirectories()
        => RulePathResolver.ResolveDefaultRuleDirectories();

    /// <summary>确定一个可写目录：优先 AppBase/rules，否则创建之。</summary>
    private string GetWriteDirectory()
    {
        if (_writeDirectory is not null) return _writeDirectory;

        // 优先用 AppContext.BaseDirectory/rules
        var appRulesDir = Path.Combine(AppContext.BaseDirectory, "rules");
        Directory.CreateDirectory(appRulesDir);
        _writeDirectory = appRulesDir;
        return _writeDirectory;
    }

    public async Task<IReadOnlyList<FullBookSourceRule>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        var dirs = GetRuleDirectories();
        var files = dirs
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.GetFiles(dir, "rule-*.json", SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new Dictionary<int, FullBookSourceRule>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rule = await TryLoadFileAsync(file, cancellationToken);
            if (rule is null || rule.Id <= 0) continue;

            var fileName = Path.GetFileName(file);
            if (fileName.Contains("template", StringComparison.OrdinalIgnoreCase)
                || fileName.Contains("unavailable", StringComparison.OrdinalIgnoreCase))
                continue;

            result.TryAdd(rule.Id, rule);
        }

        return result.Values.OrderBy(r => r.Id).ToList();
    }

    public async Task<FullBookSourceRule?> LoadAsync(int ruleId, CancellationToken cancellationToken = default)
    {
        var dirs = GetRuleDirectories();
        foreach (var dir in dirs)
        {
            var path = Path.Combine(dir, $"rule-{ruleId}.json");
            if (!File.Exists(path)) continue;
            return await TryLoadFileAsync(path, cancellationToken);
        }
        return null;
    }

    public async Task SaveAsync(FullBookSourceRule rule, CancellationToken cancellationToken = default)
    {
        var dir = GetWriteDirectory();
        var path = Path.Combine(dir, rule.FileName);

        var json = JsonSerializer.Serialize(rule, WriteOptions);
        await File.WriteAllTextAsync(path, json, Encoding.UTF8, cancellationToken);
    }

    public Task<bool> DeleteAsync(int ruleId, CancellationToken cancellationToken = default)
    {
        var dirs = GetRuleDirectories();
        var deleted = false;
        foreach (var dir in dirs)
        {
            var path = Path.Combine(dir, $"rule-{ruleId}.json");
            if (File.Exists(path))
            {
                File.Delete(path);
                deleted = true;
            }
        }
        return Task.FromResult(deleted);
    }

    public async Task<int> GetNextAvailableIdAsync(CancellationToken cancellationToken = default)
    {
        var all = await LoadAllAsync(cancellationToken);
        return all.Count > 0 ? all.Max(r => r.Id) + 1 : 1;
    }

    // ==================== 测试功能 ====================

    public async Task<RuleTestResult> TestSearchAsync(FullBookSourceRule rule, string keyword,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var diag = new List<string>();

        try
        {
            if (rule.Search is null)
                return Fail("此规则没有 search 配置", sw, diag);

            var searchUrl = rule.Search.Url.Replace("%s", Uri.EscapeDataString(keyword));
            diag.Add($"[search] url={searchUrl}, method={rule.Search.Method}");

            string html;
            if (string.Equals(rule.Search.Method, "post", StringComparison.OrdinalIgnoreCase))
            {
                var formData = ParseFormData(rule.Search.Data, keyword);
                diag.Add($"[search] formData={string.Join("&", formData.Select(kv => $"{kv.Key}={kv.Value}"))}");
                using var request = new HttpRequestMessage(HttpMethod.Post, searchUrl)
                {
                    Content = new FormUrlEncodedContent(formData),
                };
                AddCookies(request, rule.Search.Cookies);
                html = await FetchHtmlAsync(request, cancellationToken);
            }
            else
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
                AddCookies(request, rule.Search.Cookies);
                html = await FetchHtmlAsync(request, cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(html))
                return Fail("搜索页面返回空内容", sw, diag);

            diag.Add($"[search] html.Length={html.Length}");

            var parser = new HtmlParser();
            var doc = parser.ParseDocument(html);

            if (string.IsNullOrWhiteSpace(rule.Search.Result))
                return Fail("search.result 选择器为空", sw, diag);

            var rows = doc.QuerySelectorAll(NormalizeSel(rule.Search.Result));
            diag.Add($"[search] result rows={rows.Length}");

            var items = new List<string>();
            foreach (var row in rows)
            {
                var bookNameEl = string.IsNullOrWhiteSpace(rule.Search.BookName)
                    ? null
                    : row.QuerySelector(NormalizeSel(rule.Search.BookName));
                var authorEl = string.IsNullOrWhiteSpace(rule.Search.Author)
                    ? null
                    : row.QuerySelector(NormalizeSel(rule.Search.Author));

                var bookName = bookNameEl?.TextContent?.Trim() ?? "(?)";
                var author = authorEl?.TextContent?.Trim() ?? "";
                var href = bookNameEl?.GetAttribute("href") ?? "";

                items.Add(string.IsNullOrWhiteSpace(author)
                    ? $"{bookName}  [{href}]"
                    : $"{bookName} / {author}  [{href}]");
            }

            sw.Stop();
            return new RuleTestResult
            {
                Success = items.Count > 0,
                Message = items.Count > 0
                    ? $"搜索成功：共 {items.Count} 条结果 ({sw.ElapsedMilliseconds}ms)"
                    : $"搜索结果为空（选择器可能不匹配）({sw.ElapsedMilliseconds}ms)",
                SearchItems = items,
                ElapsedMs = sw.ElapsedMilliseconds,
                DiagnosticLines = diag,
            };
        }
        catch (Exception ex)
        {
            return Fail($"搜索测试异常：{ex.Message}", sw, diag);
        }
    }

    public async Task<RuleTestResult> TestTocAsync(FullBookSourceRule rule, string bookUrl,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var diag = new List<string>();

        try
        {
            if (rule.Toc is null)
                return Fail("此规则没有 toc 配置", sw, diag);

            // 构建目录 URL
            var tocUrl = bookUrl;
            if (!string.IsNullOrWhiteSpace(rule.Toc.Url) && rule.Toc.Url.Contains("%s"))
            {
                var idMatch = Regex.Match(bookUrl, @"(\d+)");
                if (idMatch.Success)
                    tocUrl = rule.Toc.Url.Replace("%s", idMatch.Groups[1].Value);
            }

            diag.Add($"[toc] url={tocUrl}");

            using var request = new HttpRequestMessage(HttpMethod.Get, tocUrl);
            var html = await FetchHtmlAsync(request, cancellationToken);

            if (string.IsNullOrWhiteSpace(html))
                return Fail("目录页返回空内容", sw, diag);

            diag.Add($"[toc] html.Length={html.Length}");

            var parser = new HtmlParser();
            var doc = parser.ParseDocument(html);

            if (string.IsNullOrWhiteSpace(rule.Toc.Item))
                return Fail("toc.item 选择器为空", sw, diag);

            var nodes = doc.QuerySelectorAll(NormalizeSel(rule.Toc.Item)).ToList();
            diag.Add($"[toc] raw items={nodes.Count}");

            // apply offset
            if (rule.Toc.Offset > 0 && nodes.Count > rule.Toc.Offset)
                nodes = nodes.Skip(rule.Toc.Offset).ToList();
            else if (rule.Toc.Offset < 0 && nodes.Count > -rule.Toc.Offset)
                nodes = nodes.Take(nodes.Count + rule.Toc.Offset).ToList();

            if (rule.Toc.Desc)
                nodes.Reverse();

            var items = new List<string>();
            string? firstChapterUrl = null;
            foreach (var node in nodes)
            {
                var title = node.TextContent?.Trim() ?? "";
                var href = node.GetAttribute("href") ?? "";
                if (!string.IsNullOrWhiteSpace(href) && !href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    if (Uri.TryCreate(tocUrl, UriKind.Absolute, out var baseUri)
                        && Uri.TryCreate(baseUri, href, out var abs))
                        href = abs.ToString();
                }
                firstChapterUrl ??= href;
                items.Add($"{title}  [{href}]");
            }

            sw.Stop();
            return new RuleTestResult
            {
                Success = items.Count > 0,
                Message = items.Count > 0
                    ? $"目录解析成功：共 {items.Count} 章 (offset={rule.Toc.Offset}, desc={rule.Toc.Desc}) ({sw.ElapsedMilliseconds}ms)"
                    : $"目录为空（选择器可能不匹配）({sw.ElapsedMilliseconds}ms)",
                TocItems = items,
                ContentPreview = firstChapterUrl ?? string.Empty, // 存储第一章 URL 供后续测试
                ElapsedMs = sw.ElapsedMilliseconds,
                DiagnosticLines = diag,
            };
        }
        catch (Exception ex)
        {
            return Fail($"目录测试异常：{ex.Message}", sw, diag);
        }
    }

    public async Task<RuleTestResult> TestChapterAsync(FullBookSourceRule rule, string chapterUrl,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var diag = new List<string>();

        try
        {
            if (rule.Chapter is null)
                return Fail("此规则没有 chapter 配置", sw, diag);

            diag.Add($"[chapter] url={chapterUrl}");

            using var request = new HttpRequestMessage(HttpMethod.Get, chapterUrl);
            var html = await FetchHtmlAsync(request, cancellationToken);

            if (string.IsNullOrWhiteSpace(html))
                return Fail("章节页返回空内容", sw, diag);

            diag.Add($"[chapter] html.Length={html.Length}");

            var contentSel = NormalizeSel(rule.Chapter.Content);
            if (string.IsNullOrWhiteSpace(contentSel))
                return Fail("chapter.content 选择器为空", sw, diag);

            var parser = new HtmlParser();
            var doc = parser.ParseDocument(html);
            var contentNode = doc.QuerySelector(contentSel);

            if (contentNode is null)
                return Fail($"选择器 '{contentSel}' 未匹配到任何元素", sw, diag);

            // 转换为纯文本
            var rawHtml = contentNode.InnerHtml;
            // <br> → \n
            rawHtml = Regex.Replace(rawHtml, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            rawHtml = Regex.Replace(rawHtml, @"</p>", "\n", RegexOptions.IgnoreCase);
            rawHtml = Regex.Replace(rawHtml, @"<p[^>]*>", "", RegexOptions.IgnoreCase);

            // 去除残余 HTML 标签
            var textContent = Regex.Replace(rawHtml, @"<[^>]+>", "").Trim();
            // 去除多余空行
            textContent = Regex.Replace(textContent, @"\n{3,}", "\n\n");

            // apply filterTxt
            if (!string.IsNullOrWhiteSpace(rule.Chapter.FilterTxt))
            {
                try
                {
                    textContent = Regex.Replace(textContent, rule.Chapter.FilterTxt, "",
                        RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
                    diag.Add($"[chapter] filterTxt applied: '{rule.Chapter.FilterTxt}'");
                }
                catch (RegexParseException ex)
                {
                    diag.Add($"[chapter] filterTxt regex error: {ex.Message}");
                }
            }

            diag.Add($"[chapter] content.Length={textContent.Length}");

            sw.Stop();
            return new RuleTestResult
            {
                Success = !string.IsNullOrWhiteSpace(textContent),
                Message = !string.IsNullOrWhiteSpace(textContent)
                    ? $"正文提取成功：{textContent.Length} 字 ({sw.ElapsedMilliseconds}ms)"
                    : $"正文为空 ({sw.ElapsedMilliseconds}ms)",
                ContentPreview = textContent,
                ElapsedMs = sw.ElapsedMilliseconds,
                DiagnosticLines = diag,
            };
        }
        catch (Exception ex)
        {
            return Fail($"章节测试异常：{ex.Message}", sw, diag);
        }
    }

    // ==================== 内部辅助 ====================

    private async Task<FullBookSourceRule?> TryLoadFileAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<FullBookSourceRule>(stream, ReadOptions, ct);
        }
        catch { return null; }
    }

    private async Task<string> FetchHtmlAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        using var response = await _httpClient.SendAsync(request, cts.Token);
        return await response.Content.ReadAsStringAsync(cts.Token);
    }

    private static void AddCookies(HttpRequestMessage request, string? cookies)
    {
        if (string.IsNullOrWhiteSpace(cookies)) return;
        request.Headers.TryAddWithoutValidation("Cookie", cookies);
    }

    private static string NormalizeSel(string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector)) return string.Empty;
        var idx = selector.IndexOf("@js:", StringComparison.OrdinalIgnoreCase);
        return idx >= 0 ? selector[..idx].Trim() : selector.Trim();
    }

    private static Dictionary<string, string> ParseFormData(string? dataTemplate, string keyword)
    {
        var result = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(dataTemplate)) return result;

        var raw = dataTemplate.Trim().TrimStart('{').TrimEnd('}');
        foreach (var pair in raw.Split(','))
        {
            var parts = pair.Split(':', 2);
            if (parts.Length != 2) continue;
            var key = parts[0].Trim().Trim('"', '\'');
            var val = parts[1].Trim().Trim('"', '\'');
            val = val.Replace("%s", keyword);
            result[key] = val;
        }
        return result;
    }

    private static RuleTestResult Fail(string msg, Stopwatch sw, List<string> diag)
    {
        sw.Stop();
        return new RuleTestResult
        {
            Success = false,
            Message = msg,
            ElapsedMs = sw.ElapsedMilliseconds,
            DiagnosticLines = diag,
        };
    }
}
