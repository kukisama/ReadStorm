namespace ReadStorm.Domain.Models;

public enum DownloadTaskStatus
{
    Queued = 1,
    Downloading = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
}
