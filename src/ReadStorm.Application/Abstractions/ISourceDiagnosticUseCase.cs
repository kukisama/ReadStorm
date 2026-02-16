using ReadStorm.Domain.Models;

namespace ReadStorm.Application.Abstractions;

public interface ISourceDiagnosticUseCase
{
    Task<SourceDiagnosticResult> DiagnoseAsync(
        int sourceId,
        string testKeyword,
        CancellationToken cancellationToken = default);
}
