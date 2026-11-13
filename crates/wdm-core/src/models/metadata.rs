use chrono::{DateTime, FixedOffset, Utc};
use serde::{Deserialize, Serialize};

use super::{DownloadRequestOptions, DownloadSegment, DownloadStatus};

fn default_total() -> i64 {
    -1
}

fn now() -> DateTime<FixedOffset> {
    Utc::now().fixed_offset()
}

fn new_id() -> String {
    uuid::Uuid::new_v4().simple().to_string()
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub struct DownloadMetadata {
    #[serde(default = "new_id")]
    pub id: String,
    #[serde(default)]
    pub url: String,
    /// Final location of the finished file. Never exists half-written (see `part_file_path`).
    #[serde(default)]
    pub target_file_path: String,
    /// File the bytes are written to while downloading (`name.ext.wdm-part`); renamed to
    /// `target_file_path` on completion. `None` in checkpoints written by WDM 1.0.x.
    #[serde(default)]
    pub part_file_path: Option<String>,
    #[serde(default)]
    pub file_name: String,
    #[serde(default = "default_total")]
    pub total_bytes: i64,
    #[serde(default)]
    pub supports_ranges: bool,
    #[serde(default, rename = "ETag")]
    pub etag: Option<String>,
    #[serde(default)]
    pub last_modified: Option<DateTime<FixedOffset>>,
    #[serde(default)]
    pub content_type: Option<String>,
    #[serde(default = "now")]
    pub created_at: DateTime<FixedOffset>,
    #[serde(default)]
    pub completed_at: Option<DateTime<FixedOffset>>,
    #[serde(default)]
    pub segments: Vec<DownloadSegment>,
    #[serde(default)]
    pub status: DownloadStatus,
    #[serde(default)]
    pub error_message: Option<String>,
    #[serde(default)]
    pub options: Option<DownloadRequestOptions>,
}

impl Default for DownloadMetadata {
    fn default() -> Self {
        Self {
            id: new_id(),
            url: String::new(),
            target_file_path: String::new(),
            part_file_path: None,
            file_name: String::new(),
            total_bytes: -1,
            supports_ranges: false,
            etag: None,
            last_modified: None,
            content_type: None,
            created_at: now(),
            completed_at: None,
            segments: Vec::new(),
            status: DownloadStatus::Queued,
            error_message: None,
            options: None,
        }
    }
}
