namespace ReadStorm.Domain.Models;

/// <summary>封面候选项（用于手动选图）。</summary>
public sealed class CoverCandidate
{
    /// <summary>显示序号（从 1 开始）。</summary>
    public int Index { get; set; }

    /// <summary>候选图片 URL（绝对地址）。</summary>
    public string ImageUrl { get; set; } = string.Empty;

    /// <summary>规则描述（如 meta[property='og:image'] / img[data-src]）。</summary>
    public string Rule { get; set; } = string.Empty;

    /// <summary>包裹图片的 HTML 片段（后续写正则用）。</summary>
    public string HtmlSnippet { get; set; } = string.Empty;

    /// <summary>UI 显示文本。</summary>
    public string Display => $"[{Index}] {Rule} | {ImageUrl}";
}
