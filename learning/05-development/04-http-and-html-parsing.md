# 5.4 HTTP 请求与 HTML 解析

[← 上一章：SQLite 数据访问](03-sqlite-data-access.md) | [返回首页](../README.md) | [下一章：规则引擎设计 →](05-rules-engine.md)

---

## 概述

ReadStorm 的核心功能之一是从网络书源搜索和下载图书内容。这涉及两个关键技术：

1. **HTTP 请求**：向书源网站发送请求获取 HTML
2. **HTML 解析**：从 HTML 中提取结构化数据（书名、章节列表、正文）

---

## HttpClient 使用

### 基本请求

```csharp
// 推荐使用 HttpClient 工厂模式或单例
private readonly HttpClient _httpClient = new();

public async Task<string> GetPageAsync(string url, CancellationToken ct)
{
    var response = await _httpClient.GetAsync(url, ct);
    response.EnsureSuccessStatusCode();

    // 自动检测编码
    var bytes = await response.Content.ReadAsByteArrayAsync(ct);
    var encoding = DetectEncoding(bytes, response);
    return encoding.GetString(bytes);
}
```

### 请求头设置

```csharp
// 模拟浏览器请求，避免被服务器拒绝
_httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

// 单次请求设置
var request = new HttpRequestMessage(HttpMethod.Get, url);
request.Headers.Add("Referer", baseUrl);
request.Headers.Add("Accept", "text/html");
```

### 超时与重试

```csharp
_httpClient.Timeout = TimeSpan.FromSeconds(15);

// 简单重试逻辑
public async Task<string> GetWithRetryAsync(string url, int maxRetry = 3)
{
    for (int i = 0; i < maxRetry; i++)
    {
        try
        {
            return await _httpClient.GetStringAsync(url);
        }
        catch (HttpRequestException) when (i < maxRetry - 1)
        {
            await Task.Delay(1000 * (i + 1)); // 退避重试
        }
    }
    throw new Exception($"请求失败: {url}");
}
```

---

## AngleSharp HTML 解析

ReadStorm 使用 `AngleSharp` 库解析 HTML。AngleSharp 是一个功能完整的 HTML/CSS 解析器，支持标准的 CSS 选择器。

### 基本使用

```csharp
using AngleSharp;
using AngleSharp.Html.Parser;

// 解析 HTML 文档
var parser = new HtmlParser();
var document = await parser.ParseDocumentAsync(htmlContent);

// 使用 CSS 选择器查找元素
var title = document.QuerySelector("h1.book-title")?.TextContent;
var author = document.QuerySelector("span.author")?.TextContent;

// 查找多个元素
var chapters = document.QuerySelectorAll("ul.chapter-list > li > a");
foreach (var chapter in chapters)
{
    var chapterTitle = chapter.TextContent.Trim();
    var chapterUrl = chapter.GetAttribute("href");
}
```

### CSS 选择器速查

| 选择器 | 含义 | 示例 |
|--------|------|------|
| `tag` | 标签名 | `div`, `a`, `h1` |
| `.class` | 类名 | `.book-title` |
| `#id` | ID | `#content` |
| `parent > child` | 直接子元素 | `ul > li` |
| `ancestor descendant` | 后代元素 | `div .title` |
| `[attr]` | 属性存在 | `[href]` |
| `[attr=value]` | 属性值 | `[class="main"]` |
| `:first-child` | 第一个子元素 | `li:first-child` |

### 提取链接和文本

```csharp
// 提取搜索结果
var searchResults = new List<SearchResult>();
var items = document.QuerySelectorAll(rule.SearchResultSelector);

foreach (var item in items)
{
    var nameEl = item.QuerySelector(rule.BookNameSelector);
    var authorEl = item.QuerySelector(rule.AuthorSelector);
    var linkEl = item.QuerySelector("a[href]");

    if (nameEl != null && linkEl != null)
    {
        var bookUrl = linkEl.GetAttribute("href");
        // 处理相对 URL
        var absoluteUrl = new Uri(new Uri(rule.BaseUrl), bookUrl).ToString();

        searchResults.Add(new SearchResult(
            BookName: nameEl.TextContent.Trim(),
            Author: authorEl?.TextContent.Trim() ?? "",
            SourceName: rule.Name,
            BookUrl: absoluteUrl,
            CoverUrl: null
        ));
    }
}
```

---

## 下载队列 - SourceDownloadQueue

ReadStorm 的下载采用 **同书源串行、跨书源并行** 策略：

```csharp
// SourceDownloadQueue 核心思路
public class SourceDownloadQueue
{
    // 每个书源一个信号量，确保串行
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public async Task EnqueueAsync(
        string sourceId,
        Func<CancellationToken, Task> work,
        CancellationToken ct)
    {
        // 获取或创建该书源的锁
        var semaphore = _locks.GetOrAdd(sourceId, _ => new SemaphoreSlim(1, 1));

        await semaphore.WaitAsync(ct);
        try
        {
            await work(ct);
        }
        finally
        {
            semaphore.Release();
        }
    }
}
```

**设计理由**：

- 同一书源串行：避免被服务器封禁，减少并发压力
- 跨书源并行：不同书源互不影响，提升整体效率

> 💡 并发策略的设计考量详见 [4.4 设计决策与取舍](../04-architecture/04-design-decisions.md)

---

## 编码处理

中文网站的编码可能是 GBK、GB2312 或 UTF-8，需要正确处理：

```csharp
// 从 HTTP 响应头或 HTML meta 标签检测编码
private Encoding DetectEncoding(byte[] bytes, HttpResponseMessage response)
{
    // 优先从 Content-Type 头获取
    var charset = response.Content.Headers.ContentType?.CharSet;
    if (!string.IsNullOrEmpty(charset))
    {
        try { return Encoding.GetEncoding(charset); }
        catch { }
    }

    // 从 HTML meta 标签检测
    var sample = Encoding.ASCII.GetString(bytes, 0, Math.Min(1024, bytes.Length));
    // 查找 <meta charset="gbk"> 或 <meta content="text/html; charset=gb2312">
    // ...

    // 默认 UTF-8
    return Encoding.UTF8;
}
```

---

## 小结

- HTTP 请求注意设置 UserAgent、超时和重试
- AngleSharp 提供强大的 CSS 选择器 HTML 解析能力
- 下载队列实现同书源串行、跨书源并行
- 中文网站需要正确处理字符编码

---

[← 上一章：SQLite 数据访问](03-sqlite-data-access.md) | [返回首页](../README.md) | [下一章：规则引擎设计 →](05-rules-engine.md)
