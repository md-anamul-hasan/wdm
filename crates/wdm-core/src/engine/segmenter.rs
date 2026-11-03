use std::sync::{Arc, Mutex};

use crate::models::{DownloadSegment, Segment, SegmentState};

/// WDM dynamic multipart segmentation algorithm.
/// Slices files into parallel segments and dynamically splits the remaining byte span
/// of the largest active segment so idle connections never wait.
pub struct DynamicSegmenter {
    inner: Mutex<Inner>,
    /// 1 MB minimum to split.
    pub min_split_threshold_bytes: i64,
}

struct Inner {
    segments: Vec<Arc<Segment>>,
    next_id: i32,
}

impl Default for DynamicSegmenter {
    fn default() -> Self {
        Self::new()
    }
}

impl DynamicSegmenter {
    pub fn new() -> Self {
        Self {
            inner: Mutex::new(Inner { segments: Vec::new(), next_id: 0 }),
            min_split_threshold_bytes: 1024 * 1024,
        }
    }

    /// Live handles, in creation order.
    pub fn segments(&self) -> Vec<Arc<Segment>> {
        self.inner.lock().unwrap().segments.clone()
    }

    pub fn snapshot(&self) -> Vec<DownloadSegment> {
        self.segments().iter().map(|s| s.snapshot()).collect()
    }

    pub fn all_completed(&self) -> bool {
        self.inner.lock().unwrap().segments.iter().all(|s| s.is_completed())
    }

    pub fn downloaded_bytes(&self) -> i64 {
        self.snapshot().iter().map(|s| s.downloaded_bytes()).sum()
    }

    /// Initializes segments from scratch for a given total file size and connection count.
    pub fn initialize(&self, total_bytes: i64, connection_count: i32, supports_ranges: bool) -> Vec<DownloadSegment> {
        let mut g = self.inner.lock().unwrap();
        g.segments.clear();
        g.next_id = 1;

        if !supports_ranges || total_bytes <= 0 || connection_count <= 1 {
            let id = g.next_id;
            g.next_id += 1;
            let end = if total_bytes > 0 { total_bytes - 1 } else { -1 };
            g.segments.push(Arc::new(Segment::new(id, 0, end, total_bytes <= 0)));
        } else {
            // Cap connection count so segments aren't smaller than the split threshold
            let count = (total_bytes / self.min_split_threshold_bytes).clamp(1, connection_count as i64);
            let chunk = total_bytes / count;
            let remainder = total_bytes % count;
            let mut start = 0;
            for i in 0..count {
                let size = chunk + if i == count - 1 { remainder } else { 0 };
                let end = start + size - 1;
                let id = g.next_id;
                g.next_id += 1;
                g.segments.push(Arc::new(Segment::new(id, start, end, false)));
                start = end + 1;
            }
        }
        g.segments.iter().map(|s| s.snapshot()).collect()
    }

    /// True when `segments` tile [0, total_bytes) exactly (no gap, no overlap) and every
    /// progress marker lies inside its own segment. A checkpoint failing this is never trusted for writing.
    pub fn is_consistent(segments: &[DownloadSegment], total_bytes: i64) -> bool {
        if total_bytes <= 0 || segments.is_empty() {
            return false;
        }
        let mut sorted: Vec<&DownloadSegment> = segments.iter().collect();
        sorted.sort_by_key(|s| s.start_offset);

        let mut expected_start = 0;
        for s in sorted {
            if s.is_open_ended || s.start_offset != expected_start || s.end_offset < s.start_offset || s.end_offset >= total_bytes {
                return false;
            }
            if s.current_offset < s.start_offset || s.current_offset > s.end_offset + 1 {
                return false;
            }
            expected_start = s.end_offset + 1;
        }
        expected_start == total_bytes
    }

    /// Restores segments from an existing checkpoint.
    pub fn restore(&self, existing: &[DownloadSegment]) {
        let mut g = self.inner.lock().unwrap();
        g.segments = existing.iter().map(|s| Arc::new(Segment::from_snapshot(s))).collect();
        g.next_id = existing.iter().map(|s| s.segment_id).max().map_or(1, |m| m + 1);
    }

    /// Dynamic middle-split: when a worker is idle, find the active segment with the largest
    /// remaining byte span and split it in half, returning the new upper segment for the idle worker.
    pub fn try_split_largest_active_segment(&self) -> Option<Arc<Segment>> {
        let mut g = self.inner.lock().unwrap();

        let mut candidate: Option<Arc<Segment>> = None;
        let mut max_remaining = 0;
        for seg in &g.segments {
            if seg.state() == SegmentState::Downloading && !seg.is_completed() && !seg.is_open_ended() {
                let remaining = seg.remaining();
                if remaining > max_remaining {
                    max_remaining = remaining;
                    candidate = Some(seg.clone());
                }
            }
        }

        let candidate = candidate?;
        if max_remaining < self.min_split_threshold_bytes * 2 {
            return None;
        }

        let half = max_remaining / 2;
        let new_start = candidate.current() + half;
        let original_end = candidate.end();
        candidate.set_end(new_start - 1);

        // Create the new upper segment and atomically claim it
        let id = g.next_id;
        g.next_id += 1;
        let new_seg = Arc::new(Segment::new(id, new_start, original_end, false));
        new_seg.set_state(SegmentState::Connecting);
        g.segments.push(new_seg.clone());
        Some(new_seg)
    }

    /// Gets the next available unstarted segment and atomically claims it.
    pub fn get_next_idle_segment(&self) -> Option<Arc<Segment>> {
        let g = self.inner.lock().unwrap();
        let seg = g.segments.iter().find(|s| s.state() == SegmentState::Idle && !s.is_completed())?;
        seg.set_state(SegmentState::Connecting);
        Some(seg.clone())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const MB: i64 = 1024 * 1024;

    #[test]
    fn initialize_tiles_file_exactly() {
        let s = DynamicSegmenter::new();
        let segs = s.initialize(10 * MB + 7, 4, true);
        assert_eq!(segs.len(), 4);
        assert!(DynamicSegmenter::is_consistent(&segs, 10 * MB + 7));
        assert_eq!(segs[0].start_offset, 0);
        assert_eq!(segs.last().unwrap().end_offset, 10 * MB + 6);
    }

    #[test]
    fn small_files_get_fewer_segments_than_connections() {
        let s = DynamicSegmenter::new();
        assert_eq!(s.initialize(3 * MB, 8, true).len(), 3);
        assert_eq!(s.initialize(100, 8, true).len(), 1);
    }

    #[test]
    fn no_ranges_or_unknown_size_gives_single_segment() {
        let s = DynamicSegmenter::new();
        let one = s.initialize(50 * MB, 8, false);
        assert_eq!((one.len(), one[0].end_offset, one[0].is_open_ended), (1, 50 * MB - 1, false));
        let unknown = s.initialize(-1, 8, true);
        assert_eq!((unknown.len(), unknown[0].is_open_ended), (1, true));
        assert_eq!(unknown[0].remaining_bytes(), i64::MAX);
    }

    #[test]
    fn consistency_rejects_gaps_overlaps_and_bad_progress() {
        let ok = vec![DownloadSegment::new(1, 0, 9), DownloadSegment::new(2, 10, 19)];
        assert!(DynamicSegmenter::is_consistent(&ok, 20));
        assert!(!DynamicSegmenter::is_consistent(&ok, 21)); // short
        assert!(!DynamicSegmenter::is_consistent(&ok, 19)); // overrun
        assert!(!DynamicSegmenter::is_consistent(&[], 20));
        let gap = vec![DownloadSegment::new(1, 0, 9), DownloadSegment::new(2, 11, 19)];
        assert!(!DynamicSegmenter::is_consistent(&gap, 20));
        let mut bad = ok.clone();
        bad[0].current_offset = 12;
        assert!(!DynamicSegmenter::is_consistent(&bad, 20));
        let mut open = ok.clone();
        open[1].is_open_ended = true;
        assert!(!DynamicSegmenter::is_consistent(&open, 20));
    }

    #[test]
    fn idle_segments_are_claimed_once() {
        let s = DynamicSegmenter::new();
        s.initialize(4 * MB, 2, true);
        let a = s.get_next_idle_segment().unwrap();
        let b = s.get_next_idle_segment().unwrap();
        assert_ne!(a.id, b.id);
        assert!(s.get_next_idle_segment().is_none());
    }

    #[test]
    fn split_halves_the_largest_active_segment() {
        let s = DynamicSegmenter::new();
        s.initialize(20 * MB, 1, true);
        // one segment, not yet downloading: nothing to split
        assert!(s.try_split_largest_active_segment().is_none());

        let seg = s.get_next_idle_segment().unwrap();
        seg.set_state(SegmentState::Downloading);
        seg.advance(2 * MB);

        let upper = s.try_split_largest_active_segment().unwrap();
        // remaining was 18 MB: lower keeps 9, upper takes the rest
        assert_eq!(seg.end(), 11 * MB - 1);
        assert_eq!(upper.start, 11 * MB);
        assert_eq!(upper.end(), 20 * MB - 1);
        assert_eq!(upper.state(), SegmentState::Connecting);
        assert!(DynamicSegmenter::is_consistent(&s.snapshot(), 20 * MB));
    }

    #[test]
    fn split_refuses_small_remainders() {
        let s = DynamicSegmenter::new();
        s.initialize(2 * MB - 1, 1, true);
        let seg = s.get_next_idle_segment().unwrap();
        seg.set_state(SegmentState::Downloading);
        // remaining is below twice the 1 MB threshold: not worth a new connection
        assert!(s.try_split_largest_active_segment().is_none());
    }

    #[test]
    fn restore_continues_ids() {
        let s = DynamicSegmenter::new();
        let mut snap = s.initialize(4 * MB, 2, true);
        snap[0].current_offset = 123;
        let r = DynamicSegmenter::new();
        r.restore(&snap);
        assert_eq!(r.snapshot()[0].current_offset, 123);
        r.segments()[0].set_state(SegmentState::Downloading);
        r.segments()[1].set_state(SegmentState::Downloading);
        let upper = r.try_split_largest_active_segment();
        assert_eq!(upper.map(|u| u.id), Some(3));
    }
}
