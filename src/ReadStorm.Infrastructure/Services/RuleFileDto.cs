namespace ReadStorm.Infrastructure.Services;

/// <summary>
/// JSON 规则文件 DTO（所有服务共享的超集定义）。
/// 每个服务只使用自身关心的属性子集，其余保持 null / 默认值。
/// </summary>
internal sealed class RuleFileDto
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Url { get; set; }

    public RuleSearchDto? Search { get; set; }

    public RuleTocDto? Toc { get; set; }

    public RuleChapterDto? Chapter { get; set; }
}

internal sealed class RuleSearchDto
{
    public string? Url { get; set; }

    public string? Method { get; set; }

    public string? Data { get; set; }

    public string? Cookies { get; set; }

    public string? Result { get; set; }

    public string? BookName { get; set; }

    public string? Author { get; set; }

    public string? LatestChapter { get; set; }

    public bool Pagination { get; set; }

    public string? NextPage { get; set; }

    public int? LimitPage { get; set; }
}

internal sealed class RuleTocDto
{
    public string? Url { get; set; }

    public string? Item { get; set; }

    public bool Pagination { get; set; }

    public string? NextPage { get; set; }

    public int? Offset { get; set; }

    public bool Desc { get; set; }
}

internal sealed class RuleChapterDto
{
    public string? Content { get; set; }

    public bool Pagination { get; set; }

    public string? NextPage { get; set; }

    public string? FilterTxt { get; set; }
}
