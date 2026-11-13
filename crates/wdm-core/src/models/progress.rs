use super::{DownloadSegment, DownloadStatus};
use serde::{Deserialize, Serialize};

/// Snapshot reported about twice a second while a download runs.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct DownloadProgress {
    /// -1 while unknown.
    pub total_bytes: i64,
    pub downloaded_bytes: i64,
    pub speed_bytes_per_second: f64,
    pub estimated_seconds_remaining: Option<f64>,
    pub segments: Vec<DownloadSegment>,
    pub status: DownloadStatus,
}

impl Default for DownloadProgress {
    fn default() -> Self {
        Self {
            total_bytes: -1,
            downloaded_bytes: 0,
            speed_bytes_per_second: 0.0,
            estimated_seconds_remaining: None,
            segments: Vec::new(),
            status: DownloadStatus::default(),
        }
    }
}

impl DownloadProgress {
    pub fn percentage(&self) -> f64 {
        if self.total_bytes > 0 {
            (self.downloaded_bytes as f64 / self.total_bytes as f64 * 100.0).clamp(0.0, 100.0)
        } else {
            0.0
        }
    }

    pub fn format_bytes(bytes: i64) -> String {
        if bytes < 0 {
            return "Unknown".to_string();
        }
        let b = bytes as f64;
        if b < 1024.0 {
            format!("{bytes} B")
        } else if b < 1024.0 * 1024.0 {
            format!("{:.1} KB", b / 1024.0)
        } else if b < 1024.0 * 1024.0 * 1024.0 {
            format!("{:.2} MB", b / (1024.0 * 1024.0))
        } else {
            format!("{:.2} GB", b / (1024.0 * 1024.0 * 1024.0))
        }
    }

    pub fn formatted_downloaded(&self) -> String {
        Self::format_bytes(self.downloaded_bytes)
    }

    pub fn formatted_total(&self) -> String {
        Self::format_bytes(self.total_bytes)
    }

    pub fn formatted_speed(&self) -> String {
        if self.speed_bytes_per_second <= 0.0 {
            "0 KB/s".to_string()
        } else if self.speed_bytes_per_second < 1024.0 * 1024.0 {
            format!("{:.1} KB/s", self.speed_bytes_per_second / 1024.0)
        } else {
            format!(
                "{:.2} MB/s",
                self.speed_bytes_per_second / (1024.0 * 1024.0)
            )
        }
    }
}
