//! Checkpointing and resuming of download state: chunk ranges and progress are persisted to a
//! `.wdm-state` JSON file next to the target.

use std::io;
use std::path::{Path, PathBuf};
use std::time::Duration;

use crate::models::DownloadMetadata;

fn with_suffix(target: &str, suffix: &str) -> String {
    format!("{target}{suffix}")
}

pub fn checkpoint_path(target_file_path: &str) -> PathBuf {
    PathBuf::from(with_suffix(target_file_path, ".wdm-state"))
}

/// The in-progress file for a download whose final name is `target_file_path`.
pub fn part_path(target_file_path: &str) -> String {
    with_suffix(target_file_path, ".wdm-part")
}

/// `flush_data` runs after the progress snapshot is taken and before it is written: the checkpoint
/// may only claim bytes that have reached the disk, otherwise a power loss would resume over data
/// that never landed.
pub async fn save_checkpoint(metadata: &DownloadMetadata, flush_data: Option<&(dyn Fn() + Send + Sync)>) -> io::Result<()> {
    let path = checkpoint_path(&metadata.target_file_path);
    let temp = PathBuf::from(format!("{}.{}.tmp", path.display(), uuid::Uuid::new_v4().simple()));

    let json = serde_json::to_string_pretty(metadata).map_err(io::Error::other)?;
    if let Some(flush) = flush_data {
        flush();
    }
    tokio::fs::write(&temp, json).await?;

    let mut retries = 5;
    loop {
        match tokio::fs::rename(&temp, &path).await {
            Ok(()) => return Ok(()),
            Err(e) if retries > 0 && matches!(e.kind(), io::ErrorKind::PermissionDenied | io::ErrorKind::Other | io::ErrorKind::AlreadyExists) => {
                retries -= 1;
                tokio::time::sleep(Duration::from_millis(25)).await;
            }
            Err(e) => {
                let _ = tokio::fs::remove_file(&temp).await;
                return Err(e);
            }
        }
    }
}

/// `None` when there is no checkpoint, or it is unreadable.
pub async fn load_checkpoint(target_file_path: &str) -> Option<DownloadMetadata> {
    let json = tokio::fs::read_to_string(checkpoint_path(target_file_path)).await.ok()?;
    serde_json::from_str(json.trim_start_matches('\u{feff}')).ok()
}

pub fn delete_checkpoint(target_file_path: &str) {
    let path = checkpoint_path(target_file_path);
    if Path::new(&path).exists() {
        let _ = std::fs::remove_file(path);
    }
}

#[cfg(test)]
mod tests {
    use std::sync::atomic::{AtomicBool, Ordering};
    use std::sync::Arc;

    use super::*;
    use crate::models::{DownloadSegment, DownloadStatus};

    fn sample(dir: &Path) -> DownloadMetadata {
        DownloadMetadata {
            url: "http://x/y.bin".into(),
            target_file_path: dir.join("y.bin").to_string_lossy().into_owned(),
            part_file_path: Some(part_path(&dir.join("y.bin").to_string_lossy())),
            file_name: "y.bin".into(),
            total_bytes: 100,
            supports_ranges: true,
            etag: Some("\"abc\"".into()),
            segments: vec![DownloadSegment::new(1, 0, 49), DownloadSegment::new(2, 50, 99)],
            status: DownloadStatus::Paused,
            ..Default::default()
        }
    }

    #[test]
    fn path_naming() {
        assert_eq!(checkpoint_path("C:\\d\\a.zip"), PathBuf::from("C:\\d\\a.zip.wdm-state"));
        assert_eq!(part_path("C:\\d\\a.zip"), "C:\\d\\a.zip.wdm-part");
    }

    #[tokio::test]
    async fn save_load_round_trip_and_delete() {
        let dir = tempfile::tempdir().unwrap();
        let m = sample(dir.path());
        save_checkpoint(&m, None).await.unwrap();
        let loaded = load_checkpoint(&m.target_file_path).await.unwrap();
        assert_eq!(loaded.url, m.url);
        assert_eq!(loaded.etag, m.etag);
        assert_eq!(loaded.segments, m.segments);
        assert_eq!(loaded.status, DownloadStatus::Paused);
        assert_eq!(loaded.id, m.id);

        delete_checkpoint(&m.target_file_path);
        assert!(load_checkpoint(&m.target_file_path).await.is_none());
        // no temp files left behind
        assert_eq!(std::fs::read_dir(dir.path()).unwrap().count(), 0);
    }

    #[tokio::test]
    async fn flush_runs_before_the_checkpoint_is_written() {
        let dir = tempfile::tempdir().unwrap();
        let m = sample(dir.path());
        let flushed = Arc::new(AtomicBool::new(false));
        let (f, cp) = (flushed.clone(), checkpoint_path(&m.target_file_path));
        let flush = move || {
            assert!(!cp.exists());
            f.store(true, Ordering::SeqCst);
        };
        save_checkpoint(&m, Some(&flush)).await.unwrap();
        assert!(flushed.load(Ordering::SeqCst));
    }

    #[tokio::test]
    async fn corrupt_checkpoint_loads_as_none() {
        let dir = tempfile::tempdir().unwrap();
        let target = dir.path().join("z.bin").to_string_lossy().into_owned();
        std::fs::write(checkpoint_path(&target), "{ nope").unwrap();
        assert!(load_checkpoint(&target).await.is_none());
    }

    #[tokio::test]
    async fn reads_a_checkpoint_written_by_the_csharp_app() {
        let dir = tempfile::tempdir().unwrap();
        let target = dir.path().join("c.bin").to_string_lossy().into_owned();
        let json = r#"{
          "Id": "0123456789abcdef0123456789abcdef",
          "Url": "https://example.com/c.bin",
          "TargetFilePath": "C:\\d\\c.bin",
          "PartFilePath": null,
          "FileName": "c.bin",
          "TotalBytes": 4194304,
          "SupportsRanges": true,
          "ETag": "\"v1\"",
          "LastModified": "2025-01-02T03:04:05+00:00",
          "ContentType": "application/zip",
          "CreatedAt": "2025-01-02T03:04:05.1234567+06:00",
          "CompletedAt": null,
          "Segments": [{"SegmentId":1,"StartOffset":0,"EndOffset":2097151,"CurrentOffset":1000,"IsOpenEnded":false},
                       {"SegmentId":2,"StartOffset":2097152,"EndOffset":4194303,"CurrentOffset":2097152,"IsOpenEnded":false}],
          "Status": 3,
          "ErrorMessage": null,
          "Options": {"Referer":"https://example.com/","Cookies":"c=1","CookieHost":null,"UserAgent":null,"Username":null,"Password":null,"FileName":null,"ExpectedSha256":null}
        }"#;
        std::fs::write(checkpoint_path(&target), json).unwrap();
        let m = load_checkpoint(&target).await.unwrap();
        assert_eq!(m.total_bytes, 4_194_304);
        assert_eq!(m.status, DownloadStatus::Paused);
        assert!(m.part_file_path.is_none());
        assert_eq!(m.segments[0].current_offset, 1000);
        assert_eq!(m.options.unwrap().cookies.as_deref(), Some("c=1")); // legacy plaintext still readable
    }
}

