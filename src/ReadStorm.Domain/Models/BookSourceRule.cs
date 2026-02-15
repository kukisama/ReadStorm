namespace ReadStorm.Domain.Models;

public sealed class BookSourceRule
{
    public int Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public bool SearchSupported { get; init; }
}
