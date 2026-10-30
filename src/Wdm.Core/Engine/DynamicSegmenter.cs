using Wdm.Core.Models;

namespace Wdm.Core.Engine;

/// <summary>
/// WDM dynamic multipart segmentation algorithm.
/// Slices files into parallel segments and dynamically splits the remaining byte span
/// of the largest active segment so idle connections never wait.
/// </summary>
public class DynamicSegmenter
{
    private readonly object _lock = new();
    private readonly List<DownloadSegment> _segments = new();
    private int _nextSegmentId;
    public long MinSplitThresholdBytes { get; set; } = 1024 * 1024; // 1 MB minimum to split

    public IReadOnlyList<DownloadSegment> Segments
    {
        get
        {
            lock (_lock)
            {
                return _segments.ToList();
            }
        }
    }

    public DynamicSegmenter() { }

    /// <summary>
    /// Initializes segments from scratch for a given total file size and connection count.
    /// </summary>
    public List<DownloadSegment> Initialize(long totalBytes, int connectionCount, bool supportsRanges)
    {
        lock (_lock)
        {
            _segments.Clear();
            _nextSegmentId = 1;

            if (!supportsRanges || totalBytes <= 0 || connectionCount <= 1)
            {
                var single = new DownloadSegment(_nextSegmentId++, 0, totalBytes > 0 ? totalBytes - 1 : -1)
                {
                    IsOpenEnded = totalBytes <= 0
                };
                _segments.Add(single);
                return _segments.ToList();
            }

            // Cap connection count so segments aren't smaller than MinSplitThresholdBytes
            int count = (int)Math.Clamp(totalBytes / MinSplitThresholdBytes, 1, connectionCount);
            if (count < 1) count = 1;

            long chunkSize = totalBytes / count;
            long remainder = totalBytes % count;

            long currentStart = 0;
            for (int i = 0; i < count; i++)
            {
                long size = chunkSize + (i == count - 1 ? remainder : 0);
                long currentEnd = currentStart + size - 1;

                var seg = new DownloadSegment(_nextSegmentId++, currentStart, currentEnd);
                _segments.Add(seg);
                currentStart = currentEnd + 1;
            }

            return _segments.ToList();
        }
    }

    /// <summary>
    /// True when <paramref name="segments"/> tile [0, totalBytes) exactly (no gap, no overlap) and every
    /// progress marker lies inside its own segment. A checkpoint failing this is never trusted for writing.
    /// </summary>
    public static bool IsConsistent(IReadOnlyCollection<DownloadSegment> segments, long totalBytes)
    {
        if (totalBytes <= 0 || segments.Count == 0) return false;

        long expectedStart = 0;
        foreach (var seg in segments.OrderBy(s => s.StartOffset))
        {
            if (seg.IsOpenEnded || seg.StartOffset != expectedStart || seg.EndOffset < seg.StartOffset || seg.EndOffset >= totalBytes) return false;
            if (seg.CurrentOffset < seg.StartOffset || seg.CurrentOffset > seg.EndOffset + 1) return false;
            expectedStart = seg.EndOffset + 1;
        }
        return expectedStart == totalBytes;
    }

    /// <summary>
    /// Restores segments from an existing checkpoint.
    /// </summary>
    public void Restore(IEnumerable<DownloadSegment> existingSegments)
    {
        lock (_lock)
        {
            _segments.Clear();
            _segments.AddRange(existingSegments);
            _nextSegmentId = _segments.Count > 0 ? _segments.Max(s => s.SegmentId) + 1 : 1;
        }
    }

    /// <summary>
    /// Dynamic middle-split: when a worker is idle, find the active segment with the largest
    /// remaining byte span and split it in half, returning the new upper segment for the idle worker.
    /// </summary>
    public DownloadSegment? TrySplitLargestActiveSegment()
    {
        lock (_lock)
        {
            DownloadSegment? candidate = null;
            long maxRemaining = 0;

            foreach (var seg in _segments)
            {
                if (seg.State == SegmentState.Downloading && !seg.IsCompleted && !seg.IsOpenEnded)
                {
                    long remaining = seg.RemainingBytes;
                    if (remaining > maxRemaining)
                    {
                        maxRemaining = remaining;
                        candidate = seg;
                    }
                }
            }

            // Check if large enough to justify splitting
            if (candidate == null || maxRemaining < MinSplitThresholdBytes * 2)
            {
                return null;
            }

            // Split point in the middle of remaining bytes
            long half = maxRemaining / 2;
            long newSplitStart = candidate.CurrentOffset + half;
            long originalEnd = candidate.EndOffset;

            // Reduce candidate's end offset
            candidate.EndOffset = newSplitStart - 1;

            // Create new upper segment and atomically claim it
            var newSegment = new DownloadSegment(_nextSegmentId++, newSplitStart, originalEnd)
            {
                State = SegmentState.Connecting
            };
            _segments.Add(newSegment);

            return newSegment;
        }
    }

    /// <summary>
    /// Gets the next available unstarted segment and atomically claims it.
    /// </summary>
    public DownloadSegment? GetNextIdleSegment()
    {
        lock (_lock)
        {
            var seg = _segments.FirstOrDefault(s => s.State == SegmentState.Idle && !s.IsCompleted);
            if (seg != null)
            {
                seg.State = SegmentState.Connecting;
            }
            return seg;
        }
    }
}
