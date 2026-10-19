using Wdm.Core.Engine;
using Wdm.Core.Models;
using Xunit;

namespace Wdm.Tests;

public class DynamicSegmenterTests
{
    [Fact]
    public void Initialize_EvenlySplitsTotalBytesAcrossConnections()
    {
        var segmenter = new DynamicSegmenter();
        long totalBytes = 80 * 1024 * 1024; // 80 MB
        int connections = 8;

        var segments = segmenter.Initialize(totalBytes, connections, supportsRanges: true);

        Assert.Equal(connections, segments.Count);
        Assert.Equal(0, segments[0].StartOffset);
        Assert.Equal(totalBytes - 1, segments[^1].EndOffset);

        // Verify continuity with no gaps and no overlaps
        for (int i = 0; i < segments.Count - 1; i++)
        {
            Assert.Equal(segments[i].EndOffset + 1, segments[i + 1].StartOffset);
        }

        // Verify total sum equals totalBytes
        long totalSpan = segments.Sum(s => s.TotalBytes);
        Assert.Equal(totalBytes, totalSpan);
    }

    [Fact]
    public void Initialize_SingleSegmentWhenRangesUnsupported()
    {
        var segmenter = new DynamicSegmenter();
        var segments = segmenter.Initialize(1000, 8, supportsRanges: false);

        Assert.Single(segments);
        Assert.Equal(0, segments[0].StartOffset);
        Assert.Equal(999, segments[0].EndOffset);
    }

    [Fact]
    public void TrySplitLargestActiveSegment_SplitsRemainingBytesCorrectly()
    {
        var segmenter = new DynamicSegmenter();
        segmenter.MinSplitThresholdBytes = 1024 * 1024; // 1 MB

        // Create 2 segments: [0..9,999,999] (10 MB), [10,000,000..19,999,999] (10 MB)
        var segments = segmenter.Initialize(20 * 1024 * 1024, 2, supportsRanges: true);

        var seg1 = segments[0];
        var seg2 = segments[1];

        // Simulate seg1 downloading, and seg2 already finished
        seg1.State = SegmentState.Downloading;
        seg1.CurrentOffset = 2 * 1024 * 1024; // 2 MB downloaded, 8 MB remaining

        seg2.State = SegmentState.Completed;
        seg2.CurrentOffset = seg2.EndOffset + 1;

        // An idle worker asks for work:
        var newSegment = segmenter.TrySplitLargestActiveSegment();

        Assert.NotNull(newSegment);
        Assert.Equal(3, segmenter.Segments.Count);

        // Verify new segment takes upper half of seg1
        Assert.Equal(seg1.EndOffset + 1, newSegment.StartOffset);
        Assert.Equal(newSegment.StartOffset, newSegment.CurrentOffset);

        // Verify seg1 + newSegment combined still equals the original span [0..10,485,759]
        Assert.Equal(0, seg1.StartOffset);
        Assert.Equal(10 * 1024 * 1024 - 1, newSegment.EndOffset);

        // Verify overall byte coverage remains identical
        long totalSpan = segmenter.Segments.Sum(s => s.TotalBytes);
        Assert.Equal(20 * 1024 * 1024, totalSpan);
    }
}
