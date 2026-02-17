using ReadStorm.Domain.Models;

namespace ReadStorm.Application.Abstractions;

/// <summary>
/// 封面管理用例——从 <see cref="IDownloadBookUseCase"/> 中拆出，
/// 负责封面候选提取、下载、存储。
/// </summary>
public interface ICoverUseCase
{
    /// <summary>
    /// 重新抓取书籍封面图片。返回诊断信息字符串。
    /// </summary>
    Task<string> RefreshCoverAsync(
        BookEntity book,
        CancellationToken cancellationToken = default);

    /// <summary>抓取目录页可选封面图片候选列表（包含规则和 HTML 片段）。</summary>
    Task<IReadOnlyList<CoverCandidate>> GetCoverCandidatesAsync(
        BookEntity book,
        CancellationToken cancellationToken = default);

    /// <summary>将指定候选图应用为封面（写入 cover_url / cover_image / cover_rule）。</summary>
    Task<string> ApplyCoverCandidateAsync(
        BookEntity book,
        CoverCandidate candidate,
        CancellationToken cancellationToken = default);
}
