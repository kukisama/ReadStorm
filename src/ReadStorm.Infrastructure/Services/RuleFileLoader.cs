using System.Text.Json;

namespace ReadStorm.Infrastructure.Services;

/// <summary>
/// 共享的规则文件加载器——消除 4 个服务中重复的
/// <c>LoadRuleAsync</c>、<c>JsonOptions</c>、<c>NormalizeSelector</c>、<c>ResolveUrl</c>。
/// </summary>
internal static class RuleFileLoader
{
    /// <summary>所有规则 JSON 反序列化共用的选项。</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// 按书源 ID 从规则目录中加载规则文件。
    /// 搜索顺序由 <see cref="RulePathResolver.ResolveAllRuleDirectories"/> 决定（用户目录优先）。
    /// </summary>
    public static async Task<RuleFileDto?> LoadRuleAsync(
        IReadOnlyList<string> ruleDirectories,
        int sourceId,
        CancellationToken cancellationToken)
    {
        var filePath = ruleDirectories
            .Select(dir => Path.Combine(dir, $"rule-{sourceId}.json"))
            .FirstOrDefault(File.Exists);

        if (filePath is null)
        {
            return null;
        }

        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<RuleFileDto>(stream, JsonOptions, cancellationToken);
    }

    /// <summary>
    /// 移除 CSS selector 中尾部的 <c>@js:...</c> 脚本标记。
    /// 多个服务需要在解析 HTML 前调用此方法。
    /// </summary>
    public static string NormalizeSelector(string? selector)
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

    /// <summary>
    /// 将可能的相对 URL 解析为绝对 URL。
    /// </summary>
    /// <remarks>
    /// 在 Android/Linux 上，以 '/' 开头的路径（如 /0/743/615708.html）会被
    /// <see cref="Uri.TryCreate(string, UriKind, out Uri)"/> 误判为 file:// 绝对 URI。
    /// 本方法过滤掉 file:// 协议，确保此类路径走相对解析逻辑。
    /// </remarks>
    public static string ResolveUrl(string baseUrl, string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        // 先尝试当作绝对 URL 解析，但排除 file:// 协议：
        // Linux/Android 上 "/path" 会被解析为 file:///path，
        // 在网络小说下载场景中这永远是误判，应走相对路径解析。
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute)
            && !absolute.IsFile)
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
}
