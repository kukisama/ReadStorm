namespace ReadStorm.Domain.Models;

public sealed record SearchResult(
    Guid Id,
    string Title,
    string Author,
    int SourceId,
    string Url,
    string LatestChapter,
    DateTimeOffset UpdatedAt);
