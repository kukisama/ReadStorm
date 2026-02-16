using ReadStorm.Domain.Models;

namespace ReadStorm.Application.Abstractions;

/// <summary>
/// 规则编辑器用例：加载、保存、删除完整的书源规则。
/// </summary>
public interface IRuleEditorUseCase
{
    /// <summary>获取所有完整规则。</summary>
    Task<IReadOnlyList<FullBookSourceRule>> LoadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>按 ID 获取单条完整规则。</summary>
    Task<FullBookSourceRule?> LoadAsync(int ruleId, CancellationToken cancellationToken = default);

    /// <summary>保存规则（新建或覆盖）。</summary>
    Task SaveAsync(FullBookSourceRule rule, CancellationToken cancellationToken = default);

    /// <summary>删除规则。</summary>
    Task<bool> DeleteAsync(int ruleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 恢复指定规则为内置默认值（删除用户覆盖文件）。
    /// 如果该规则没有用户覆盖或没有内置默认值，返回 false。
    /// </summary>
    Task<bool> ResetToDefaultAsync(int ruleId, CancellationToken cancellationToken = default);

    /// <summary>检查指定规则是否有用户覆盖（已被用户修改过）。</summary>
    bool HasUserOverride(int ruleId);

    /// <summary>获取下一个可用 ID。</summary>
    Task<int> GetNextAvailableIdAsync(CancellationToken cancellationToken = default);

    /// <summary>测试搜索规则：使用关键字搜索并返回结果。</summary>
    Task<RuleTestResult> TestSearchAsync(FullBookSourceRule rule, string keyword, CancellationToken cancellationToken = default);

    /// <summary>测试目录规则：抓取指定书的目录列表。</summary>
    Task<RuleTestResult> TestTocAsync(FullBookSourceRule rule, string bookUrl, CancellationToken cancellationToken = default);

    /// <summary>测试章节规则：抓取第一章内容。</summary>
    Task<RuleTestResult> TestChapterAsync(FullBookSourceRule rule, string chapterUrl, CancellationToken cancellationToken = default);
}

/// <summary>规则测试结果。</summary>
public sealed class RuleTestResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;

    /// <summary>请求 URL（调试用）。</summary>
    public string RequestUrl { get; init; } = string.Empty;

    /// <summary>请求方法（调试用）。</summary>
    public string RequestMethod { get; init; } = string.Empty;

    /// <summary>请求体/表单（调试用）。</summary>
    public string RequestBody { get; init; } = string.Empty;

    /// <summary>关键选择器说明（调试用）。</summary>
    public IReadOnlyList<string> SelectorLines { get; init; } = [];

    /// <summary>搜索结果摘要（标题+作者列表）。</summary>
    public IReadOnlyList<string> SearchItems { get; init; } = [];

    /// <summary>目录项列表（章节标题）。</summary>
    public IReadOnlyList<string> TocItems { get; init; } = [];

    /// <summary>正文内容预览。</summary>
    public string ContentPreview { get; init; } = string.Empty;

    /// <summary>原始 HTML（调试用）。</summary>
    public string RawHtml { get; init; } = string.Empty;

    /// <summary>命中节点 HTML（调试用，可能为空）。</summary>
    public string MatchedHtml { get; init; } = string.Empty;

    /// <summary>测试耗时 (ms)。</summary>
    public long ElapsedMs { get; init; }

    /// <summary>诊断日志。</summary>
    public IReadOnlyList<string> DiagnosticLines { get; init; } = [];
}
