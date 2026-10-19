using System.Text.Json.Serialization;

namespace Wdm.Core.Models;

public class DownloadSegment
{
    public int SegmentId { get; set; }
    public long StartOffset { get; set; }
    public long EndOffset { get; set; } // Inclusive end offset
    public long CurrentOffset { get; set; }

    [JsonIgnore]
    public SegmentState State { get; set; } = SegmentState.Idle;

    [JsonIgnore]
    public double SpeedBytesPerSecond { get; set; }

    public long TotalBytes => EndOffset >= StartOffset ? (EndOffset - StartOffset + 1) : 0;
    public long DownloadedBytes => Math.Clamp(CurrentOffset - StartOffset, 0, TotalBytes);
    public long RemainingBytes => Math.Max(0, EndOffset - CurrentOffset + 1);
    public bool IsCompleted => CurrentOffset > EndOffset;

    /// <summary>UI alias for SegmentId (1-based display index).</summary>
    [JsonIgnore]
    public int SegmentIndex => SegmentId;

    /// <summary>Download percentage for this segment (0–100).</summary>
    [JsonIgnore]
    public double Percentage => TotalBytes > 0
        ? Math.Clamp((double)DownloadedBytes / TotalBytes * 100.0, 0, 100)
        : (IsCompleted ? 100.0 : 0.0);

    /// <summary>Human-readable transfer rate for this segment.</summary>
    [JsonIgnore]
    public string FormattedSpeed => SpeedBytesPerSecond switch
    {
        >= 1024 * 1024 * 1024 => $"{SpeedBytesPerSecond / (1024.0 * 1024 * 1024):F2} GB/s",
        >= 1024 * 1024        => $"{SpeedBytesPerSecond / (1024.0 * 1024):F2} MB/s",
        >= 1024               => $"{SpeedBytesPerSecond / 1024.0:F1} KB/s",
        _                     => $"{SpeedBytesPerSecond:F0} B/s"
    };

    /// <summary>Human-readable downloaded/total byte count for this segment.</summary>
    [JsonIgnore]
    public string FormattedDownloaded => TotalBytes > 0
        ? $"{FormatBytes(DownloadedBytes)}/{FormatBytes(TotalBytes)}"
        : FormatBytes(DownloadedBytes);

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
        >= 1024L * 1024        => $"{bytes / (1024.0 * 1024):F2} MB",
        >= 1024L               => $"{bytes / 1024.0:F1} KB",
        _                      => $"{bytes} B"
    };

    public DownloadSegment() { }

    public DownloadSegment(int segmentId, long startOffset, long endOffset)
    {
        SegmentId = segmentId;
        StartOffset = startOffset;
        EndOffset = endOffset;
        CurrentOffset = startOffset;
    }
}
