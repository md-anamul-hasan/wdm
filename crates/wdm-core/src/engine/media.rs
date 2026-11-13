use regex::Regex;
use std::collections::VecDeque;
use std::path::{Path, PathBuf};
use std::process::Stdio;
use std::sync::OnceLock;
use std::time::Duration;
use tokio::io::{AsyncBufReadExt, BufReader};
use tokio::process::Command;

use crate::engine::prober::sanitize_file_name;
use crate::error::{Error, Result};
use crate::models::{DownloadMetadata, DownloadProgress, DownloadStatus};

const TITLE_PREFIX: &str = "WDMTITLE:";
const FILE_PREFIX: &str = "WDMFILE:";

static YOUTUBE_REGEX: OnceLock<Regex> = OnceLock::new();
static PROGRESS_REGEX: OnceLock<Regex> = OnceLock::new();

fn youtube_regex() -> &'static Regex {
    YOUTUBE_REGEX.get_or_init(|| {
        Regex::new(
            r"(?i)^(?:https?://)?(?:www\.|m\.)?(?:youtube\.com/(?:watch\?(?:[^#]*&)?v=|shorts/|embed/)|youtu\.be/)[a-zA-Z0-9_-]{11}",
        )
        .expect("valid regex")
    })
}

fn progress_regex() -> &'static Regex {
    PROGRESS_REGEX.get_or_init(|| {
        Regex::new(
            r"\[download\]\s+([\d.]+)%\s+of\s+~?\s*([\d.]+[A-Za-z]+)(?:\s+at\s+([\d.]+[A-Za-z]+/s))?(?:\s+ETA\s+(\d+(?::\d+){1,2}))?",
        )
        .expect("valid regex")
    })
}

pub struct MediaResolver;

impl MediaResolver {
    pub fn is_youtube_url(url: &str) -> bool {
        let trimmed = url.trim();
        if trimmed.is_empty() {
            return false;
        }
        youtube_regex().is_match(trimmed)
    }

    pub fn find_resolver_executable() -> Option<PathBuf> {
        Self::find_on_path_or_beside("yt-dlp.exe", None)
    }

    pub fn find_on_path_or_beside(
        exe_name: &str,
        beside_executable: Option<&Path>,
    ) -> Option<PathBuf> {
        let mut candidates = Vec::new();

        if let Ok(current_exe) = std::env::current_exe() {
            if let Some(dir) = current_exe.parent() {
                candidates.push(dir.join(exe_name));
            }
        }

        if let Some(beside) = beside_executable {
            if let Some(dir) = beside.parent() {
                candidates.push(dir.join(exe_name));
            }
        }

        if let Some(path_var) = std::env::var_os("PATH") {
            for dir in std::env::split_paths(&path_var) {
                candidates.push(dir.join(exe_name));
            }
        }

        candidates.into_iter().find(|p| p.is_file())
    }

    pub async fn download_video<P, S, M>(
        video_url: &str,
        destination_dir: Option<&Path>,
        mut on_progress: P,
        mut on_status_changed: S,
        mut on_metadata_loaded: M,
        mut cancel_rx: tokio::sync::watch::Receiver<bool>,
    ) -> Result<()>
    where
        P: FnMut(DownloadProgress) + Send + 'static,
        S: FnMut(DownloadStatus) + Send + 'static,
        M: FnMut(DownloadMetadata) + Send + 'static,
    {
        let resolver = Self::find_resolver_executable().ok_or_else(|| {
            Error::NonRetryable(
                "The video resolver (yt-dlp.exe) was not found next to WDM or on PATH.".into(),
            )
        })?;

        on_status_changed(DownloadStatus::Connecting);

        let target_dir = match destination_dir {
            Some(d) => d.to_path_buf(),
            None => {
                let user_profile = std::env::var_os("USERPROFILE")
                    .or_else(|| std::env::var_os("HOME"))
                    .map(PathBuf::from)
                    .unwrap_or_else(|| PathBuf::from("."));
                user_profile.join("Downloads")
            }
        };

        let _ = tokio::fs::create_dir_all(&target_dir).await;

        let has_ffmpeg = Self::find_on_path_or_beside("ffmpeg.exe", Some(&resolver)).is_some();
        let mut cmd = Command::new(&resolver);

        if has_ffmpeg {
            cmd.args(["--merge-output-format", "mp4"]);
        } else {
            cmd.args(["-f", "b[ext=mp4]/b"]);
        }

        let out_template = target_dir
            .join("%(title)s.%(ext)s")
            .to_string_lossy()
            .into_owned();

        cmd.args([
            "--encoding",
            "utf-8",
            "--no-warnings",
            "--no-playlist",
            "--newline",
            "--progress",
            "--no-simulate",
            "--print",
            &format!("before_dl:{TITLE_PREFIX}%(title)s"),
            "--print",
            &format!("after_move:{FILE_PREFIX}%(filepath)s"),
            "-o",
            &out_template,
            "--",
            video_url,
        ]);

        cmd.stdout(Stdio::piped());
        cmd.stderr(Stdio::piped());

        let mut child = cmd.spawn().map_err(Error::Io)?;
        let stdout = child
            .stdout
            .take()
            .ok_or_else(|| Error::NonRetryable("Failed to open stdout".into()))?;
        let stderr = child
            .stderr
            .take()
            .ok_or_else(|| Error::NonRetryable("Failed to open stderr".into()))?;

        let mut reader = BufReader::new(stdout).lines();
        let mut err_reader = BufReader::new(stderr).lines();

        let mut meta = DownloadMetadata {
            url: video_url.to_string(),
            file_name: "YouTube video".into(),
            supports_ranges: false,
            status: DownloadStatus::Connecting,
            ..Default::default()
        };

        let mut error_tail: VecDeque<String> = VecDeque::with_capacity(10);
        let mut final_path: Option<String> = None;
        let mut metadata_published = false;

        loop {
            tokio::select! {
                line_res = reader.next_line() => {
                    match line_res {
                        Ok(Some(line)) => {
                            if let Some(stripped) = line.strip_prefix(TITLE_PREFIX) {
                                let title = sanitize_file_name(stripped.trim());
                                meta.file_name = title.clone();
                                meta.target_file_path = target_dir.join(&title).to_string_lossy().into_owned();
                                continue;
                            }

                            if let Some(stripped) = line.strip_prefix(FILE_PREFIX) {
                                final_path = Some(stripped.trim().to_string());
                                continue;
                            }

                            if let Some(caps) = progress_regex().captures(&line) {
                                if !metadata_published {
                                    metadata_published = true;
                                    meta.status = DownloadStatus::Downloading;
                                    on_status_changed(DownloadStatus::Downloading);
                                    on_metadata_loaded(meta.clone());
                                }

                                let pct: f64 = caps.get(1).and_then(|m| m.as_str().parse().ok()).unwrap_or(0.0);
                                let speed_str = caps.get(3).map(|m| m.as_str()).unwrap_or("");
                                let eta_str = caps.get(4).map(|m| m.as_str()).unwrap_or("");

                                let speed_bytes = parse_speed_bps(speed_str);
                                let eta_dur = parse_eta_duration(eta_str);

                                let prog = DownloadProgress {
                                    total_bytes: -1,
                                    downloaded_bytes: (pct * 1000.0) as i64,
                                    speed_bytes_per_second: speed_bytes,
                                    estimated_seconds_remaining: eta_dur.map(|d| d.as_secs_f64()),
                                    status: DownloadStatus::Downloading,
                                    ..Default::default()
                                };
                                on_progress(prog);
                            }
                        }
                        Ok(None) => break,
                        Err(e) => {
                            let _ = child.kill().await;
                            return Err(Error::Io(e));
                        }
                    }
                }
                err_line_res = err_reader.next_line() => {
                    if let Ok(Some(err_line)) = err_line_res {
                        let trimmed = err_line.trim().to_string();
                        if !trimmed.is_empty() {
                            if error_tail.len() >= 8 {
                                error_tail.pop_front();
                            }
                            error_tail.push_back(trimmed);
                        }
                    }
                }
                _ = cancel_rx.changed() => {
                    let _ = child.kill().await;
                    on_status_changed(DownloadStatus::Paused);
                    return Err(Error::Canceled);
                }
            }
        }

        let status = child.wait().await.map_err(Error::Io)?;
        if !status.success() {
            let msg = if error_tail.is_empty() {
                format!("yt-dlp exited with status {status}")
            } else {
                error_tail.into_iter().collect::<Vec<_>>().join(" ")
            };
            return Err(Error::NonRetryable(msg));
        }

        if let Some(fp) = final_path {
            meta.target_file_path = fp;
        }
        meta.status = DownloadStatus::Completed;
        on_status_changed(DownloadStatus::Completed);
        on_metadata_loaded(meta);

        Ok(())
    }
}

fn parse_speed_bps(s: &str) -> f64 {
    let trimmed = s.trim_end_matches("/s").trim();
    if trimmed.is_empty() {
        return 0.0;
    }
    if let Some(val) = trimmed
        .strip_suffix("KiB")
        .or_else(|| trimmed.strip_suffix("K"))
    {
        val.parse::<f64>().unwrap_or(0.0) * 1024.0
    } else if let Some(val) = trimmed
        .strip_suffix("MiB")
        .or_else(|| trimmed.strip_suffix("M"))
    {
        val.parse::<f64>().unwrap_or(0.0) * 1024.0 * 1024.0
    } else if let Some(val) = trimmed
        .strip_suffix("GiB")
        .or_else(|| trimmed.strip_suffix("G"))
    {
        val.parse::<f64>().unwrap_or(0.0) * 1024.0 * 1024.0 * 1024.0
    } else if let Some(val) = trimmed.strip_suffix("B") {
        val.parse::<f64>().unwrap_or(0.0)
    } else {
        trimmed.parse::<f64>().unwrap_or(0.0)
    }
}

fn parse_eta_duration(s: &str) -> Option<Duration> {
    let parts: Vec<&str> = s.split(':').collect();
    match parts.len() {
        2 => {
            let m: u64 = parts[0].parse().ok()?;
            let sec: u64 = parts[1].parse().ok()?;
            Some(Duration::from_secs(m * 60 + sec))
        }
        3 => {
            let h: u64 = parts[0].parse().ok()?;
            let m: u64 = parts[1].parse().ok()?;
            let sec: u64 = parts[2].parse().ok()?;
            Some(Duration::from_secs(h * 3600 + m * 60 + sec))
        }
        _ => None,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_is_youtube_url() {
        assert!(MediaResolver::is_youtube_url(
            "https://www.youtube.com/watch?v=dQw4w9WgXcQ"
        ));
        assert!(MediaResolver::is_youtube_url(
            "https://youtu.be/dQw4w9WgXcQ"
        ));
        assert!(MediaResolver::is_youtube_url(
            "https://youtube.com/shorts/dQw4w9WgXcQ"
        ));
        assert!(!MediaResolver::is_youtube_url(
            "https://example.com/video.mp4"
        ));
    }
}
