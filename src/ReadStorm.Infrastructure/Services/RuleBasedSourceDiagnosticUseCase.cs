using AngleSharp.Html.Parser;
using ReadStorm.Application.Abstractions;
using ReadStorm.Domain.Models;

namespace ReadStorm.Infrastructure.Services;

public sealed class RuleBasedSourceDiagnosticUseCase : ISourceDiagnosticUseCase
{
    private readonly IReadOnlyList<string> _ruleDirectories;
    private readonly HttpClient _httpClient;

    public RuleBasedSourceDiagnosticUseCase(
        HttpClient? httpClient = null,
        IReadOnlyList<string>? ruleDirectories = null)
    {
        _ruleDirectories = ruleDirectories ?? RulePathResolver.ResolveAllRuleDirectories();
        _httpClient = httpClient ?? RuleHttpHelper.CreateHttpClient(TimeSpan.FromSeconds(5));
    }

    public async Task<SourceDiagnosticResult> DiagnoseAsync(
        int sourceId,
        string testKeyword,
        CancellationToken cancellationToken = default)
    {
        // 每个书源诊断最多 8 秒
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(8));
        var ct = timeoutCts.Token;

        var result = new SourceDiagnosticResult { SourceId = sourceId };

        void Log(string message) => result.DiagnosticLines.Add($"[{DateTimeOffset.Now:HH:mm:ss.fff}] {message}");

        try
        {
            Log($"开始诊断书源 {sourceId}");

            var rule = await LoadRuleAsync(sourceId, ct);
            if (rule is null)
            {
                result.Summary = $"未找到书源 {sourceId} 的规则文件。";
                Log(result.Summary);
                return result;
            }

            result.SourceName = rule.Name ?? $"Rule-{rule.Id}";
            result.BaseUrl = rule.Url ?? string.Empty;

            // Check search rule
            result.SearchRuleFound = rule.Search is not null
                                     && !string.IsNullOrWhiteSpace(rule.Search.Url);
            Log($"搜索规则：{(result.SearchRuleFound ? "已配置" : "缺失")}");

            // Check toc rule
            result.TocRuleFound = rule.Toc is not null
                                  && !string.IsNullOrWhiteSpace(rule.Toc.Item);
            result.TocSelector = rule.Toc?.Item ?? string.Empty;
            Log($"目录规则：{(result.TocRuleFound ? "已配置" : "缺失")}，selector='{result.TocSelector}'");

            // Check chapter rule
            result.ChapterRuleFound = rule.Chapter is not null
                                      && !string.IsNullOrWhiteSpace(rule.Chapter.Content);
            result.ChapterContentSelector = rule.Chapter?.Content ?? string.Empty;
            Log($"章节规则：{(result.ChapterRuleFound ? "已配置" : "缺失")}，selector='{result.ChapterContentSelector}'");

            // HTTP connectivity check
            if (!string.IsNullOrWhiteSpace(result.BaseUrl))
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Head, result.BaseUrl);
                    using var response = await _httpClient.SendAsync(request, ct);
                    result.HttpStatusCode = (int)response.StatusCode;
                    result.HttpStatusMessage = response.StatusCode.ToString();
                    Log($"HTTP 连通性：{result.HttpStatusCode} {result.HttpStatusMessage}");
                }
                catch (Exception ex)
                {
                    result.HttpStatusCode = 0;
                    result.HttpStatusMessage = ex.GetType().Name + ": " + ex.Message;
                    Log($"HTTP 连通性检测失败：{result.HttpStatusMessage}");
                }
            }

            // Search test
            if (result.SearchRuleFound && !string.IsNullOrWhiteSpace(testKeyword))
            {
                try
                {
                    var searchUrl = rule.Search!.Url!.Replace("%s", Uri.EscapeDataString(testKeyword));
                    Log($"尝试搜索：{searchUrl}");
                    using var searchRequest = new HttpRequestMessage(HttpMethod.Get, searchUrl);
                    using var searchResponse = await _httpClient.SendAsync(searchRequest, ct);
                    if (searchResponse.IsSuccessStatusCode)
                    {
                        var html = await searchResponse.Content.ReadAsStringAsync(ct);
                        var resultSelector = RuleFileLoader.NormalizeSelector(rule.Search.Result);
                        if (!string.IsNullOrWhiteSpace(resultSelector))
                        {
                            var parser = new HtmlParser();
                            var doc = parser.ParseDocument(html);
                            result.SearchResultCount = doc.QuerySelectorAll(resultSelector).Length;
                            Log($"搜索结果 selector '{resultSelector}' 命中：{result.SearchResultCount} 条");
                        }
                        else
                        {
                            Log("搜索结果 selector 为空，无法解析。");
                        }
                    }
                    else
                    {
                        Log($"搜索请求返回：{(int)searchResponse.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    Log($"搜索测试异常：{ex.Message}");
                }
            }

            result.Summary = result.IsHealthy ? "书源状态正常" : "书源存在配置或连通问题";
            Log($"诊断结论：{result.Summary}");
        }
        catch (Exception ex)
        {
            result.Summary = $"诊断异常：{ex.Message}";
            Log(result.Summary);
        }

        return result;
    }

    private Task<RuleFileDto?> LoadRuleAsync(int sourceId, CancellationToken cancellationToken)
        => RuleFileLoader.LoadRuleAsync(_ruleDirectories, sourceId, cancellationToken);
}
