use super::{DownloadSegment, DownloadStatus};

/// Snapshot reported about twice a second while a download runs.
#[derive(Debug, Clone)]
pub struct DownloadProgress {
    /// -1 while unknown.
    pub total_bytes: i64,
    pub downloaded_bytes: i64,
    pub speed_bytes_per_second: f64,
    pub estimated_seconds_remaining: Option<f64>,
    pub segments: Vec<DownloadSegment>,
    pub status: DownloadStatus,
}

