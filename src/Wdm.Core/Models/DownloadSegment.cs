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

    public DownloadSegment() { }

    public DownloadSegment(int segmentId, long startOffset, long endOffset)
    {
        SegmentId = segmentId;
        StartOffset = startOffset;
        EndOffset = endOffset;
        CurrentOffset = startOffset;
    }
}
