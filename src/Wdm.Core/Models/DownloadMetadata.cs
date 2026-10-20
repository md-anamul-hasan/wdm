namespace Wdm.Core.Models;

public class DownloadMetadata
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Url { get; set; } = string.Empty;
    public string TargetFilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long TotalBytes { get; set; } = -1;
    public bool SupportsRanges { get; set; }
    public string? ETag { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public string? ContentType { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public List<DownloadSegment> Segments { get; set; } = new();
    public DownloadStatus Status { get; set; } = DownloadStatus.Queued;
    public string? ErrorMessage { get; set; }
    public DownloadRequestOptions? Options { get; set; }
}
