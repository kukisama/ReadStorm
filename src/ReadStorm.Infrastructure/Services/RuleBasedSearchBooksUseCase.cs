using AngleSharp.Html.Parser;
using ReadStorm.Application.Abstractions;
using ReadStorm.Domain.Models;

namespace ReadStorm.Infrastructure.Services;

public sealed class RuleBasedSearchBooksUseCase : ISearchBooksUseCase
{
    private static readonly HttpClient HttpClient = RuleHttpHelper.CreateSearchHttpClient();

    public async Task<IReadOnlyList<SearchResult>> ExecuteAsync(
        string keyword,
        int? sourceId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyword) || sourceId is null or <= 0)
        {
            return [];
        }

        var rule = await LoadRuleAsync(sourceId.Value, cancellationToken);
        if (rule?.Search is null)
        {
            return [];
        }

        try
        {
            var firstPage = await FetchSearchPageAsync(rule, keyword, cancellationToken);
            if (string.IsNullOrWhiteSpace(firstPage.Html))
            {
                return [];
            }

            var allPages = new List<SearchPageContent> { firstPage };
            var nextPages = ExtractPaginationUrls(rule, firstPage, keyword);

            foreach (var nextUrl in nextPages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var html = await FetchHtmlByUrlAsync(nextUrl, cancellationToken);
                if (string.IsNullOrWhiteSpace(html))
                {
                    continue;
                }

                allPages.Add(new SearchPageContent(html, nextUrl));
            }

            var merged = allPages
                .SelectMany(page => ParseSearchResults(rule, page))
                .GroupBy(x => $"{x.Title}|{x.Author}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .Take(50)
                .ToList();

            return merged;
        }
        catch (Exception ex)
        {
            _ = Classify(ex);
            return [];
        }
    }

    private static Task<RuleFileDto?> LoadRuleAsync(int sourceId, CancellationToken cancellationToken)
        => RuleFileLoader.LoadRuleAsync(RulePathResolver.ResolveAllRuleDirectories(), sourceId, cancellationToken);

    private static async Task<SearchPageContent> FetchSearchPageAsync(RuleFileDto rule, string keyword, CancellationToken cancellationToken)
    {
        var search = rule.Search!;
        var url = ReplaceKeywordForUrl(search.Url ?? rule.Url ?? string.Empty, keyword);
        if (string.IsNullOrWhiteSpace(url))
        {
            return new SearchPageContent(string.Empty, string.Empty);
        }

        using var request = new HttpRequestMessage(
            string.Equals(search.Method, "post", StringComparison.OrdinalIgnoreCase)
                ? HttpMethod.Post
                : HttpMethod.Get,
            url);

        if (!string.IsNullOrWhiteSpace(search.Cookies))
        {
            request.Headers.TryAddWithoutValidation("Cookie", search.Cookies);
        }

        if (request.Method == HttpMethod.Post)
        {
            var formData = BuildFormData(search.Data, keyword);
            request.Content = new FormUrlEncodedContent(formData);
        }

        using var response = await RuleHttpHelper.SendWithSimpleRetryAsync(HttpClient, request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new SearchPageContent(string.Empty, url);
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        return new SearchPageContent(html, url);
    }

    private static async Task<string> FetchHtmlByUrlAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await RuleHttpHelper.SendWithSimpleRetryAsync(HttpClient, request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return string.Empty;
        }

        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static IReadOnlyList<SearchResult> ParseSearchResults(RuleFileDto rule, SearchPageContent page)
    {
        var search = rule.Search!;
        var resultSelector = RuleFileLoader.NormalizeSelector(search.Result);
        var bookNameSelector = RuleFileLoader.NormalizeSelector(search.BookName);

        if (string.IsNullOrWhiteSpace(resultSelector) || string.IsNullOrWhiteSpace(bookNameSelector))
        {
            return [];
        }

        var parser = new HtmlParser();
        var doc = parser.ParseDocument(page.Html);
        var rows = doc.QuerySelectorAll(resultSelector);
        var authorSelector = RuleFileLoader.NormalizeSelector(search.Author);
        var latestChapterSelector = RuleFileLoader.NormalizeSelector(search.LatestChapter);

        var result = new List<SearchResult>();

        foreach (var row in rows)
        {
            var bookNode = row.QuerySelector(bookNameSelector);
            var title = bookNode?.TextContent?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var relativeHref = bookNode?.GetAttribute("href") ?? string.Empty;
            var bookUrl = RuleFileLoader.ResolveUrl(page.PageUrl, relativeHref);

            var author = !string.IsNullOrWhiteSpace(authorSelector)
                ? row.QuerySelector(authorSelector)?.TextContent?.Trim() ?? "未知作者"
                : "未知作者";

            var latestChapter = !string.IsNullOrWhiteSpace(latestChapterSelector)
                ? row.QuerySelector(latestChapterSelector)?.TextContent?.Trim() ?? "/"
                : "/";

            result.Add(new SearchResult(
                Guid.NewGuid(),
                title,
                author,
                rule.Id,
                rule.Name ?? string.Empty,
                bookUrl,
                latestChapter,
                DateTimeOffset.Now));
        }

        return result;
    }

    private static IReadOnlyList<string> ExtractPaginationUrls(RuleFileDto rule, SearchPageContent firstPage, string keyword)
    {
        var search = rule.Search!;
        if (!search.Pagination || string.IsNullOrWhiteSpace(search.NextPage))
        {
            return [];
        }

        var selector = RuleFileLoader.NormalizeSelector(search.NextPage);
        if (string.IsNullOrWhiteSpace(selector))
        {
            return [];
        }

        var parser = new HtmlParser();
        var doc = parser.ParseDocument(firstPage.Html);

        var limitPage = search.LimitPage is > 1 ? search.LimitPage.Value : 3;
        var maxExtraPages = Math.Max(0, limitPage - 1);

        var urls = new LinkedHashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in doc.QuerySelectorAll(selector))
        {
            if (urls.Count >= maxExtraPages)
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

            href = ReplaceKeywordForUrl(href, keyword);
            var absoluteUrl = RuleFileLoader.ResolveUrl(firstPage.PageUrl, href);
            if (string.IsNullOrWhiteSpace(absoluteUrl) || string.Equals(absoluteUrl, firstPage.PageUrl, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            urls.Add(absoluteUrl);
        }

        return urls.ToList();
    }

    private static Dictionary<string, string> BuildFormData(string? template, string keyword)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(template))
        {
            return dict;
        }

        var body = template.Trim();
        if (body.StartsWith('{') && body.EndsWith('}'))
        {
            body = body[1..^1];
        }

        var pairs = body.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var pair in pairs)
        {
            var idx = pair.IndexOf(':');
            if (idx <= 0 || idx >= pair.Length - 1)
            {
                continue;
            }

            var key = pair[..idx].Trim();
            var value = pair[(idx + 1)..].Trim();
            value = value.Trim('"', '\'', ' ');
            value = ReplaceKeyword(value, keyword);

            if (!string.IsNullOrWhiteSpace(key))
            {
                dict[key] = value;
            }
        }

        return dict;
    }

    private static string ReplaceKeyword(string template, string keyword)
    {
        return template.Replace("%s", keyword, StringComparison.Ordinal);
    }

    private static string ReplaceKeywordForUrl(string template, string keyword)
    {
        return template.Replace("%s", Uri.EscapeDataString(keyword), StringComparison.Ordinal);
    }

    private static SearchFailureKind Classify(Exception ex)
    {
        return ex switch
        {
            OperationCanceledException => SearchFailureKind.Cancelled,
            HttpRequestException => SearchFailureKind.Network,
            System.Text.Json.JsonException => SearchFailureKind.RuleFormat,
            _ => SearchFailureKind.Unknown,
        };
    }

    private sealed record SearchPageContent(string Html, string PageUrl);

    private enum SearchFailureKind
    {
        Network,
        RuleFormat,
        Cancelled,
        Unknown,
    }

    private sealed class LinkedHashSet<T>(IEqualityComparer<T>? comparer = null)
    {
        private readonly HashSet<T> _set = new(comparer);
        private readonly List<T> _list = [];

        public int Count => _list.Count;

        public bool Add(T value)
        {
            if (!_set.Add(value))
            {
                return false;
            }

            _list.Add(value);
            return true;
        }

        public List<T> ToList() => [.. _list];
    }
}
