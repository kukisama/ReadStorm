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

    /// <summary>搜索结果摘要（标题+作者列表）。</summary>
    public IReadOnlyList<string> SearchItems { get; init; } = [];

    /// <summary>目录项列表（章节标题）。</summary>
    public IReadOnlyList<string> TocItems { get; init; } = [];

    /// <summary>正文内容预览。</summary>
    public string ContentPreview { get; init; } = string.Empty;

    /// <summary>测试耗时 (ms)。</summary>
    public long ElapsedMs { get; init; }

    /// <summary>诊断日志。</summary>
    public IReadOnlyList<string> DiagnosticLines { get; init; } = [];
}
