namespace ReadStorm.Domain.Models;

public enum DownloadErrorKind
{
    None = 0,
    Network = 1,
    Rule = 2,
    Parse = 3,
    IO = 4,
    Cancelled = 5,
    Unknown = 99,
}
