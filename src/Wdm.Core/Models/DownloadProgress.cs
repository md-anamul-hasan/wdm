namespace Wdm.Core.Models;

public record DownloadProgress(
    long TotalBytes,
    long DownloadedBytes,
    double SpeedBytesPerSecond,
    TimeSpan? EstimatedTimeRemaining,
    IReadOnlyList<DownloadSegment> Segments,
    DownloadStatus Status
)
{
    public double Percentage => TotalBytes > 0 
        ? Math.Clamp((double)DownloadedBytes / TotalBytes * 100.0, 0, 100) 
        : 0;

    public string FormattedSpeed => SpeedBytesPerSecond switch
    {
        >= 1024 * 1024 * 1024 => $"{(SpeedBytesPerSecond / (1024.0 * 1024 * 1024)):F2} GB/s",
        >= 1024 * 1024 => $"{(SpeedBytesPerSecond / (1024.0 * 1024)):F2} MB/s",
        >= 1024 => $"{(SpeedBytesPerSecond / 1024.0):F1} KB/s",
        _ => $"{SpeedBytesPerSecond:F0} B/s"
    };

    public string FormattedDownloaded => TotalBytes > 0
        ? $"{FormatBytes(DownloadedBytes)} / {FormatBytes(TotalBytes)}"
        : FormatBytes(DownloadedBytes);

    public static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{(bytes / (1024.0 * 1024 * 1024)):F2} GB",
        >= 1024L * 1024 => $"{(bytes / (1024.0 * 1024)):F2} MB",
        >= 1024L => $"{(bytes / 1024.0):F1} KB",
        _ => $"{bytes} B"
    };
}
