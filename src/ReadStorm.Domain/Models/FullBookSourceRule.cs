using System.Text.Json.Serialization;

namespace ReadStorm.Domain.Models;

/// <summary>
/// 完整的书源规则，与 rule-*.json 一一对应。
/// 用于规则编辑器的加载、保存和测试。
/// </summary>
public sealed class FullBookSourceRule
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("comment")]
    public string Comment { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "html";

    [JsonPropertyName("language")]
    public string Language { get; set; } = "zh_CN";

    [JsonPropertyName("search")]
    public RuleSearchSection? Search { get; set; }

    [JsonPropertyName("book")]
    public RuleBookSection? Book { get; set; }

    [JsonPropertyName("toc")]
    public RuleTocSection? Toc { get; set; }

    [JsonPropertyName("chapter")]
    public RuleChapterSection? Chapter { get; set; }

    /// <summary>
    /// 返回规则文件保存时的文件名。
    /// </summary>
    [JsonIgnore]
    public string FileName => $"rule-{Id}.json";
}

/// <summary>搜索规则区段。</summary>
public sealed class RuleSearchSection
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("method")]
    public string Method { get; set; } = "get";

    [JsonPropertyName("data")]
    public string Data { get; set; } = "{}";

    [JsonPropertyName("cookies")]
    public string Cookies { get; set; } = string.Empty;

    [JsonPropertyName("result")]
    public string Result { get; set; } = string.Empty;

    [JsonPropertyName("bookName")]
    public string BookName { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("wordCount")]
    public string WordCount { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("latestChapter")]
    public string LatestChapter { get; set; } = string.Empty;

    [JsonPropertyName("lastUpdateTime")]
    public string LastUpdateTime { get; set; } = string.Empty;

    [JsonPropertyName("pagination")]
    public bool Pagination { get; set; }

    [JsonPropertyName("nextPage")]
    public string NextPage { get; set; } = string.Empty;

    [JsonPropertyName("limitPage")]
    public int LimitPage { get; set; } = 3;
}

/// <summary>书籍详情规则区段（暂未在代码中使用，仅做结构保留）。</summary>
public sealed class RuleBookSection
{
    [JsonPropertyName("bookName")]
    public string BookName { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("intro")]
    public string Intro { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("coverUrl")]
    public string CoverUrl { get; set; } = string.Empty;

    [JsonPropertyName("latestChapter")]
    public string LatestChapter { get; set; } = string.Empty;

    [JsonPropertyName("lastUpdateTime")]
    public string LastUpdateTime { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}

/// <summary>目录规则区段。</summary>
public sealed class RuleTocSection
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("item")]
    public string Item { get; set; } = string.Empty;

    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("desc")]
    public bool Desc { get; set; }

    [JsonPropertyName("pagination")]
    public bool Pagination { get; set; }

    [JsonPropertyName("nextPage")]
    public string NextPage { get; set; } = string.Empty;
}

/// <summary>章节内容规则区段。</summary>
public sealed class RuleChapterSection
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("paragraphTagClosed")]
    public bool ParagraphTagClosed { get; set; }

    [JsonPropertyName("paragraphTag")]
    public string ParagraphTag { get; set; } = string.Empty;

    [JsonPropertyName("filterTxt")]
    public string FilterTxt { get; set; } = string.Empty;

    [JsonPropertyName("filterTag")]
    public string FilterTag { get; set; } = string.Empty;

    [JsonPropertyName("pagination")]
    public bool Pagination { get; set; }

    [JsonPropertyName("nextPage")]
    public string NextPage { get; set; } = string.Empty;
}
