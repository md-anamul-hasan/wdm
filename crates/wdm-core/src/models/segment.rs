use std::sync::atomic::{AtomicBool, AtomicI64, AtomicU64, AtomicU8, Ordering};

use serde::{Deserialize, Serialize};

use super::status::SegmentState;

/// Persisted / reported snapshot of a segment. `EndOffset` is inclusive.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize, Default)]
#[serde(rename_all = "PascalCase")]
pub struct DownloadSegment {
    pub segment_id: i32,
    pub start_offset: i64,
    pub end_offset: i64,
    pub current_offset: i64,
    /// True while the total size is unknown: the segment streams until the server closes the response.
    #[serde(default)]
    pub is_open_ended: bool,
    #[serde(skip)]
    pub state: SegmentState,
    #[serde(skip)]
    pub speed_bytes_per_second: f64,
}

impl DownloadSegment {
    pub fn new(segment_id: i32, start_offset: i64, end_offset: i64) -> Self {
        Self {
            segment_id,
            start_offset,
            end_offset,
            current_offset: start_offset,
            ..Default::default()
        }
    }

    pub fn total_bytes(&self) -> i64 {
        if !self.is_open_ended && self.end_offset >= self.start_offset {
            self.end_offset - self.start_offset + 1
        } else {
            0
        }
    }

    pub fn downloaded_bytes(&self) -> i64 {
        let d = self.current_offset - self.start_offset;
        if self.is_open_ended {
            d.max(0)
        } else {
            d.clamp(0, self.total_bytes())
        }
    }

    pub fn remaining_bytes(&self) -> i64 {
        if self.is_open_ended {
            i64::MAX
        } else {
            (self.end_offset - self.current_offset + 1).max(0)
        }
    }

    pub fn is_completed(&self) -> bool {
        !self.is_open_ended && self.current_offset > self.end_offset
    }
}

/// Live segment shared between the segmenter and a worker. Offsets are atomics so a middle-split
/// (which shrinks `end`) and the worker's progress never need a common lock.
#[derive(Debug)]
pub struct Segment {
    pub id: i32,
    pub start: i64,
    end: AtomicI64,
    current: AtomicI64,
    open_ended: AtomicBool,
    state: AtomicU8,
    speed_bits: AtomicU64,
}

impl Segment {
    pub fn new(id: i32, start: i64, end: i64, open_ended: bool) -> Self {
        Self {
            id,
            start,
            end: AtomicI64::new(end),
            current: AtomicI64::new(start),
            open_ended: AtomicBool::new(open_ended),
            state: AtomicU8::new(SegmentState::Idle as u8),
            speed_bits: AtomicU64::new(0f64.to_bits()),
        }
    }

    pub fn from_snapshot(s: &DownloadSegment) -> Self {
        let seg = Self::new(s.segment_id, s.start_offset, s.end_offset, s.is_open_ended);
        seg.current.store(s.current_offset, Ordering::SeqCst);
        seg
    }

    pub fn end(&self) -> i64 {
        self.end.load(Ordering::SeqCst)
    }
    pub fn set_end(&self, v: i64) {
        self.end.store(v, Ordering::SeqCst)
    }
    pub fn current(&self) -> i64 {
        self.current.load(Ordering::SeqCst)
    }
    pub fn advance(&self, bytes: i64) {
        self.current.fetch_add(bytes, Ordering::SeqCst);
    }
    pub fn is_open_ended(&self) -> bool {
        self.open_ended.load(Ordering::SeqCst)
    }
    pub fn state(&self) -> SegmentState {
        SegmentState::from_u8(self.state.load(Ordering::SeqCst))
    }
    pub fn set_state(&self, s: SegmentState) {
        self.state.store(s as u8, Ordering::SeqCst)
    }
    pub fn set_speed(&self, v: f64) {
        self.speed_bits.store(v.to_bits(), Ordering::Relaxed)
    }

    pub fn remaining(&self) -> i64 {
        if self.is_open_ended() {
            i64::MAX
        } else {
            (self.end() - self.current() + 1).max(0)
        }
    }

    pub fn is_completed(&self) -> bool {
        !self.is_open_ended() && self.current() > self.end()
    }

    /// Ends an open-ended segment at the bytes received so far (server closed the response).
    pub fn close_open_end(&self) {
        self.open_ended.store(false, Ordering::SeqCst);
        self.end.store(self.current() - 1, Ordering::SeqCst);
    }

    pub fn snapshot(&self) -> DownloadSegment {
        DownloadSegment {
            segment_id: self.id,
            start_offset: self.start,
            end_offset: self.end(),
            current_offset: self.current(),
            is_open_ended: self.is_open_ended(),
            state: self.state(),
            speed_bytes_per_second: f64::from_bits(self.speed_bits.load(Ordering::Relaxed)),
        }
    }
}
