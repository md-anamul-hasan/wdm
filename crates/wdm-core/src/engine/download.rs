//! WDM core orchestration engine: multi-connection dynamic-segmentation download pipeline
//! with middle-split load balancing and zero-merge direct file writes.

use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, AtomicI64, AtomicU32, AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use chrono::Utc;
use reqwest::Client;
use tokio::task::JoinSet;
use tokio_util::sync::CancellationToken;

use super::http;
use super::limiter::SpeedLimiter;
use super::prober;
use super::resume;
use super::segmenter::DynamicSegmenter;
use super::storage::StorageWriter;
use super::worker::HttpConnectionWorker;
use crate::models::{DownloadMetadata, DownloadProgress, DownloadRequestOptions, DownloadStatus, Sha256Checksum};
use crate::{Error, Result};

type ProgressCallback = Arc<dyn Fn(DownloadProgress) + Send + Sync>;
type StatusCallback = Arc<dyn Fn(DownloadStatus) + Send + Sync>;

struct Speed {
    bytes_per_second: f64,
    last_check: Instant,
}

/// One download. Create with [`DownloadEngine::new`], then call [`start`](DownloadEngine::start);
/// [`pause`](DownloadEngine::pause) can be called from any task while `start` is running.
pub struct DownloadEngine {
    client: Client,
    limiter: Arc<SpeedLimiter>,
    segmenter: DynamicSegmenter,

    metadata: Mutex<Option<DownloadMetadata>>,
    /// Kept apart from the metadata so failures before any metadata exists (the probe) are still visible.
    current_status: Mutex<DownloadStatus>,
    options: Mutex<Option<DownloadRequestOptions>>,
    writer: Mutex<Option<Arc<StorageWriter>>>,
    cancel: Mutex<CancellationToken>,

    total_downloaded: AtomicI64,
    bytes_in_window: AtomicI64,
    speed: Mutex<Speed>,

    enable_dynamic_split: AtomicBool,
    network_timeout_seconds: AtomicU32,
    retry_backoff_millis: AtomicU64,

    on_progress: Mutex<Option<ProgressCallback>>,
    on_status: Mutex<Option<StatusCallback>>,
}

impl DownloadEngine {
    /// `client`: shared client; when `None` the engine makes its own.
    /// `max_bytes_per_second`: per-engine limit, ignored when `limiter` is supplied.
    /// `limiter`: limiter shared with other engines to enforce one global limit.
    pub fn new(client: Option<Client>, max_bytes_per_second: i64, limiter: Option<Arc<SpeedLimiter>>) -> Result<Arc<Self>> {
        Ok(Arc::new(Self {
            client: match client {
                Some(c) => c,
                None => http::create_client(None)?,
            },
            limiter: limiter.unwrap_or_else(|| Arc::new(SpeedLimiter::new(max_bytes_per_second))),
            segmenter: DynamicSegmenter::new(),
            metadata: Mutex::new(None),
            current_status: Mutex::new(DownloadStatus::Queued),
            options: Mutex::new(None),
            writer: Mutex::new(None),
            cancel: Mutex::new(CancellationToken::new()),
            total_downloaded: AtomicI64::new(0),
            bytes_in_window: AtomicI64::new(0),
            speed: Mutex::new(Speed { bytes_per_second: 0.0, last_check: Instant::now() }),
            enable_dynamic_split: AtomicBool::new(true),
            network_timeout_seconds: AtomicU32::new(30),
            retry_backoff_millis: AtomicU64::new(1000),
            on_progress: Mutex::new(None),
            on_status: Mutex::new(None),
        }))
    }

    pub fn set_on_progress(&self, f: impl Fn(DownloadProgress) + Send + Sync + 'static) {
        *self.on_progress.lock().unwrap() = Some(Arc::new(f));
    }

    pub fn set_on_status(&self, f: impl Fn(DownloadStatus) + Send + Sync + 'static) {
        *self.on_status.lock().unwrap() = Some(Arc::new(f));
    }

    /// When false, idle connections never split an active segment (static segmentation only).
    pub fn set_enable_dynamic_split(&self, enabled: bool) {
        self.enable_dynamic_split.store(enabled, Ordering::Relaxed);
    }

    /// Seconds without receiving data before a connection is considered stalled and retried.
    pub fn set_network_timeout_seconds(&self, seconds: u32) {
        self.network_timeout_seconds.store(seconds, Ordering::Relaxed);
    }

    /// Base unit of the exponential retry backoff (1 s by default; tests shorten it).
    pub fn set_retry_backoff_unit(&self, unit: Duration) {
        self.retry_backoff_millis.store(unit.as_millis() as u64, Ordering::Relaxed);
    }

    pub fn speed_limiter(&self) -> &Arc<SpeedLimiter> {
        &self.limiter
    }

    pub fn metadata(&self) -> Option<DownloadMetadata> {
        let mut m = self.metadata.lock().unwrap().clone()?;
        m.segments = self.segmenter.snapshot();
        Some(m)
    }

    pub fn status(&self) -> DownloadStatus {
        *self.current_status.lock().unwrap()
    }

    /// Stops the download; `start` returns once the workers have stopped and progress is saved.
    pub fn pause(&self) {
        self.cancel.lock().unwrap().cancel();
    }

    /// Starts or resumes a download. Returns `Ok` when it completed or was paused; check [`status`](Self::status).
    pub async fn start(
        self: &Arc<Self>,
        url: &str,
        destination_directory: Option<&Path>,
        connection_count: i32,
        cancellation: CancellationToken,
        options: Option<DownloadRequestOptions>,
    ) -> Result<()> {
        let ct = cancellation.child_token();
        *self.cancel.lock().unwrap() = ct.clone();
        *self.options.lock().unwrap() = options.clone();

        // A checksum the user typed wrongly must fail now, not after hours of downloading
        if let Some(expected) = options.as_ref().and_then(|o| o.expected_sha256.as_deref()) {
            if !expected.trim().is_empty() && Sha256Checksum::try_normalize(Some(expected)).is_none() {
                return Err(Error::InvalidArgument("The SHA-256 checksum is not valid (expected 64 hex characters).".into()));
            }
        }

        if is_media_platform_url(url) {
            return Err(Error::InvalidArgument("Video platform downloads are not supported by the Rust engine yet.".into()));
        }

        self.set_status(DownloadStatus::Connecting);

        // Check if an existing checkpoint exists for this URL
        let (probe, existing) = match self.probe_and_load(url, destination_directory, options.as_ref(), &ct).await {
            Ok(r) => r,
            Err(Error::Canceled) => {
                self.set_status(DownloadStatus::Paused);
                return Err(Error::Canceled);
            }
            Err(e) => {
                self.set_status(DownloadStatus::Error);
                return Err(e);
            }
        };

        // Only servers that honour Range can resume; unknown-length downloads and a validator missing on both sides prove nothing
        let mut same_file = existing.as_ref().is_some_and(|e| {
            e.url == url
                && e.total_bytes == probe.total_bytes
                && e.total_bytes > 0
                && probe.supports_ranges
                && match &probe.etag {
                    Some(tag) => e.etag.as_ref() == Some(tag),
                    None => probe.last_modified.is_some() && e.last_modified == probe.last_modified,
                }
        });

        // The checkpoint only describes bytes that are still on disk: a deleted, replaced or truncated partial file,
        // or a damaged segment table, means the saved progress cannot be trusted (restart rather than complete with holes).
        // Checkpoints from WDM 1.0.x have no part file: they were written straight into the target file.
        let legacy_in_place = existing.as_ref().is_some_and(|e| e.part_file_path.is_none());
        let existing_write_path = if legacy_in_place { probe.target_file_path.clone() } else { resume::part_path(&probe.target_file_path) };
        if same_file && !checkpoint_matches_disk(existing.as_ref().unwrap(), &existing_write_path) {
            same_file = false;
        }

        let metadata = match existing {
            Some(mut m) if same_file => {
                // Resume! Location and identity come from the probe, never from the checkpoint file.
                m.target_file_path = probe.target_file_path.clone();
                m.part_file_path = if legacy_in_place { None } else { Some(existing_write_path) };
                m.file_name = probe.file_name.clone();
                m.options = options.or(m.options.take());
                *self.options.lock().unwrap() = m.options.clone();
                self.segmenter.restore(&m.segments);
                m
            }
            _ => {
                let mut m = probe;
                m.segments = self.segmenter.initialize(m.total_bytes, connection_count, m.supports_ranges);
                m
            }
        };
        let write_path = metadata.part_file_path.clone().unwrap_or_else(|| metadata.target_file_path.clone());
        let total_bytes = metadata.total_bytes;
        *self.metadata.lock().unwrap() = Some(metadata);

        *self.writer.lock().unwrap() = Some(Arc::new(StorageWriter::new(&write_path, total_bytes)?));

        // Persist right away so the file reserved by the probe is always tied to this URL
        self.save_checkpoint().await?;

        self.total_downloaded.store(self.segmenter.downloaded_bytes(), Ordering::SeqCst);
        self.set_status(DownloadStatus::Downloading);

        self.run_with_range_fallback(connection_count, &ct).await
    }

    async fn probe_and_load(
        &self,
        url: &str,
        dest: Option<&Path>,
        options: Option<&DownloadRequestOptions>,
        ct: &CancellationToken,
    ) -> Result<(DownloadMetadata, Option<DownloadMetadata>)> {
        let probe = prober::probe(&self.client, url, dest, options, ct).await?;
        let existing = resume::load_checkpoint(&probe.target_file_path).await;
        Ok((probe, existing))
    }

    /// A server that advertises byte ranges but ignores them cannot be downloaded in parallel (or resumed).
    /// Instead of failing, start over once as a single plain stream, which every HTTP server supports.
    async fn run_with_range_fallback(self: &Arc<Self>, connection_count: i32, ct: &CancellationToken) -> Result<()> {
        match self.run_download_loop(connection_count, ct).await {
            Err(Error::RangeIgnored(_)) if !ct.is_cancelled() => {
                let (write_path, total) = {
                    let mut g = self.metadata.lock().unwrap();
                    let m = g.as_mut().expect("metadata is set once a download runs");
                    m.supports_ranges = false;
                    m.error_message = None;
                    m.segments = self.segmenter.initialize(m.total_bytes, 1, false);
                    (m.part_file_path.clone().unwrap_or_else(|| m.target_file_path.clone()), m.total_bytes)
                };
                *self.writer.lock().unwrap() = Some(Arc::new(StorageWriter::new(&write_path, total)?));
                self.total_downloaded.store(0, Ordering::SeqCst);
                self.save_checkpoint().await?;

                self.set_status(DownloadStatus::Downloading);
                self.run_download_loop(1, ct).await
            }
            other => other,
        }
    }

    fn write_path(&self) -> String {
        let g = self.metadata.lock().unwrap();
        let m = g.as_ref().expect("metadata is set once a download runs");
        m.part_file_path.clone().unwrap_or_else(|| m.target_file_path.clone())
    }

    /// Flushes file data, then persists progress that is guaranteed to be on disk.
    async fn save_checkpoint(&self) -> Result<()> {
        let snapshot = {
            let mut g = self.metadata.lock().unwrap();
            let Some(m) = g.as_mut() else { return Ok(()) };
            m.segments = self.segmenter.snapshot();
            m.clone()
        };
        let writer = self.writer.lock().unwrap().clone();
        if let Some(writer) = writer {
            tokio::task::spawn_blocking(move || writer.flush()).await.map_err(std::io::Error::other)??;
        }
        resume::save_checkpoint(&snapshot, None).await?;
        Ok(())
    }

    async fn run_download_loop(self: &Arc<Self>, connection_count: i32, ct: &CancellationToken) -> Result<()> {
        self.speed.lock().unwrap().last_check = Instant::now();

        // Progress is reported twice a second; the (fsync-ing) checkpoint every two seconds
        let reporting_ct = ct.child_token();
        let reporter = {
            let me = self.clone();
            let rct = reporting_ct.clone();
            tokio::spawn(async move {
                let mut interval = tokio::time::interval_at(tokio::time::Instant::now() + Duration::from_millis(500), Duration::from_millis(500));
                interval.set_missed_tick_behavior(tokio::time::MissedTickBehavior::Delay);
                let mut tick = 0u32;
                loop {
                    tokio::select! {
                        _ = rct.cancelled() => break,
                        _ = interval.tick() => {}
                    }
                    me.update_metrics();
                    tick += 1;
                    if tick % 4 == 0 && me.status() != DownloadStatus::Completed {
                        let _ = me.save_checkpoint().await;
                    }
                }
            })
        };

        let workers_result = self.run_workers(connection_count, ct).await;

        // Stop reporting before finalizing: a late checkpoint must not outlive the completed download
        reporting_ct.cancel();
        let _ = reporter.await;

        let result = match workers_result {
            Ok(()) => self.finalize(ct).await,
            Err(e) => Err(e),
        };

        let outcome = match result {
            Ok(()) => Ok(()),
            Err(Error::Canceled) => {
                self.set_status(DownloadStatus::Paused);
                self.save_checkpoint().await
            }
            Err(e) => {
                self.set_status(DownloadStatus::Error);
                if let Some(m) = self.metadata.lock().unwrap().as_mut() {
                    m.error_message = Some(e.to_string());
                }
                let _ = self.save_checkpoint().await;
                Err(e)
            }
        };

        let writer = self.writer.lock().unwrap().take();
        if let Some(w) = writer {
            let _ = w.close();
        }
        outcome
    }

    /// Launches the worker pool; the first worker failure cancels its siblings so the error surfaces promptly.
    async fn run_workers(self: &Arc<Self>, connection_count: i32, ct: &CancellationToken) -> Result<()> {
        let actual = {
            let g = self.metadata.lock().unwrap();
            match g.as_ref() {
                Some(m) if m.supports_ranges && m.total_bytes > 0 => connection_count.max(1),
                _ => 1,
            }
        };

        let workers_ct = ct.child_token();
        let mut set = JoinSet::new();
        for _ in 0..actual {
            let me = self.clone();
            let wct = workers_ct.clone();
            let parent = ct.clone();
            set.spawn(async move {
                let r = me.worker_loop(&wct).await;
                if r.is_err() && !parent.is_cancelled() {
                    wct.cancel();
                }
                r
            });
        }

        let mut failure: Option<Error> = None;
        while let Some(joined) = set.join_next().await {
            match joined {
                Ok(Ok(())) | Ok(Err(Error::Canceled)) => {}
                Ok(Err(e)) => failure = failure.or(Some(e)),
                Err(join_err) => failure = failure.or(Some(Error::Io(std::io::Error::other(join_err)))),
            }
        }

        if ct.is_cancelled() {
            return Err(Error::Canceled);
        }
        match failure {
            Some(e) => Err(e),
            None => Ok(()),
        }
    }

    async fn worker_loop(self: &Arc<Self>, ct: &CancellationToken) -> Result<()> {
        let Some(writer) = self.writer.lock().unwrap().clone() else { return Ok(()) };
        let (url, ranges, total) = {
            let g = self.metadata.lock().unwrap();
            let Some(m) = g.as_ref() else { return Ok(()) };
            (m.url.clone(), m.supports_ranges, m.total_bytes)
        };

        let on_bytes: Arc<dyn Fn(i64) + Send + Sync> = {
            let engine = self.clone();
            Arc::new(move |n| {
                engine.total_downloaded.fetch_add(n, Ordering::Relaxed);
                engine.bytes_in_window.fetch_add(n, Ordering::Relaxed);
            })
        };

        let worker = HttpConnectionWorker {
            client: self.client.clone(),
            writer,
            limiter: self.limiter.clone(),
            on_bytes_read: on_bytes,
            options: self.options.lock().unwrap().clone(),
            stall_timeout: Duration::from_secs(self.network_timeout_seconds.load(Ordering::Relaxed).max(5) as u64),
            backoff_unit: Duration::from_millis(self.retry_backoff_millis.load(Ordering::Relaxed)),
        };

        while !ct.is_cancelled() {
            // 1. Try to get an idle segment
            let mut segment = self.segmenter.get_next_idle_segment();

            // 2. If no idle segments, try a dynamic middle-split of the largest active segment
            if segment.is_none() && ranges && total > 0 && self.enable_dynamic_split.load(Ordering::Relaxed) {
                segment = self.segmenter.try_split_largest_active_segment();
            }

            let Some(segment) = segment else {
                // No work right now: wait briefly in case another active segment grows large enough to split or finishes
                tokio::select! {
                    _ = ct.cancelled() => return Err(Error::Canceled),
                    _ = tokio::time::sleep(Duration::from_millis(100)) => {}
                }
                if self.segmenter.all_completed() {
                    break;
                }
                continue;
            };

            worker.download_segment(&url, &segment, ct).await?;
        }
        if ct.is_cancelled() {
            return Err(Error::Canceled);
        }
        Ok(())
    }

    async fn finalize(&self, ct: &CancellationToken) -> Result<()> {
        if !self.segmenter.all_completed() {
            return Err(Error::Io(std::io::Error::other("The download stopped before every segment finished.")));
        }

        // Size was unknown up front: it is whatever the server delivered
        let unknown_size = self.metadata.lock().unwrap().as_ref().is_some_and(|m| m.total_bytes <= 0);
        if unknown_size {
            let total = self.total_downloaded.load(Ordering::SeqCst);
            if let Some(m) = self.metadata.lock().unwrap().as_mut() {
                m.total_bytes = total;
            }
            if let Some(w) = self.writer.lock().unwrap().as_ref() {
                w.set_len(total)?;
            }
        }

        // Close (and flush) first; only then does the file get its real name, and only after that is the checkpoint
        // dropped: until the rename succeeded, the checkpoint is the record that the data is not finished.
        let writer = self.writer.lock().unwrap().take();
        if let Some(w) = writer {
            tokio::task::spawn_blocking(move || w.close()).await.map_err(std::io::Error::other)??;
        }

        // Optional integrity check on the finished bytes, before the file gets its real name.
        // On mismatch the data is kept (never deleted on a guess) and the download ends in Error.
        let expected = self.metadata.lock().unwrap().as_ref().and_then(|m| m.options.as_ref().and_then(|o| o.expected_sha256.clone()));
        if let Some(expected) = Sha256Checksum::try_normalize(expected.as_deref()) {
            let path = self.write_path();
            let actual = tokio::select! {
                _ = ct.cancelled() => return Err(Error::Canceled),
                r = Sha256Checksum::compute_file(Path::new(&path)) => r?,
            };
            if actual != expected {
                let kept = Path::new(&path).file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or(path);
                return Err(Error::ChecksumMismatch { expected, actual, kept_file: kept });
            }
        }

        let owner = {
            let mut g = self.metadata.lock().unwrap();
            let m = g.as_mut().expect("metadata is set once a download runs");
            m.completed_at = Some(Utc::now().fixed_offset());
            m.target_file_path.clone()
        };
        self.publish_finished_file()?;
        resume::delete_checkpoint(&owner);

        self.set_status(DownloadStatus::Completed);
        self.update_metrics();
        Ok(())
    }

    /// Gives the finished part file its final name; if that name was taken meanwhile, the next free "name (n).ext" is used.
    fn publish_finished_file(&self) -> Result<()> {
        let mut g = self.metadata.lock().unwrap();
        let m = g.as_mut().expect("metadata is set once a download runs");
        let Some(part) = m.part_file_path.clone() else { return Ok(()) }; // legacy in-place download: already under its final name

        let target = PathBuf::from(&m.target_file_path);
        let dir = target.parent().map(Path::to_path_buf).unwrap_or_default();
        let stem = target.file_stem().map(|s| s.to_string_lossy().into_owned()).unwrap_or_default();
        let ext = target.extension().map(|e| format!(".{}", e.to_string_lossy())).unwrap_or_default();

        for counter in 0u32.. {
            let candidate = if counter == 0 { target.clone() } else { dir.join(format!("{stem} ({counter}){ext}")) };
            let candidate_str = candidate.to_string_lossy().into_owned();
            if counter > 0 && Path::new(&resume::part_path(&candidate_str)).exists() {
                continue; // reserved by another download
            }
            if candidate.exists() {
                continue;
            }
            match std::fs::rename(&part, &candidate) {
                Ok(()) => {
                    m.file_name = candidate.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default();
                    m.target_file_path = candidate_str;
                    m.part_file_path = None;
                    return Ok(());
                }
                Err(_) if candidate.exists() => {}
                Err(e) => return Err(e.into()),
            }
        }
        unreachable!()
    }

    fn update_metrics(&self) {
        let now = Instant::now();
        let speed = {
            let mut s = self.speed.lock().unwrap();
            let elapsed = now.duration_since(s.last_check).as_secs_f64();
            if elapsed > 0.0 {
                let bytes = self.bytes_in_window.swap(0, Ordering::Relaxed);
                s.bytes_per_second = bytes as f64 / elapsed;
                s.last_check = now;
            }
            s.bytes_per_second
        };

        let total = self.metadata.lock().unwrap().as_ref().map_or(-1, |m| m.total_bytes);
        let downloaded = self.total_downloaded.load(Ordering::Relaxed);
        let remaining = if total > 0 { (total - downloaded).max(0) } else { 0 };
        let eta = (speed > 0.0 && remaining > 0).then(|| remaining as f64 / speed);

        let progress = DownloadProgress {
            total_bytes: total,
            downloaded_bytes: downloaded,
            speed_bytes_per_second: speed,
            estimated_seconds_remaining: eta,
            segments: self.segmenter.snapshot(),
            status: self.status(),
        };
        let cb = self.on_progress.lock().unwrap().clone();
        if let Some(cb) = cb {
            cb(progress);
        }
    }

    fn set_status(&self, status: DownloadStatus) {
        *self.current_status.lock().unwrap() = status;
        if let Some(m) = self.metadata.lock().unwrap().as_mut() {
            m.status = status;
        }
        let cb = self.on_status.lock().unwrap().clone();
        if let Some(cb) = cb {
            cb(status);
        }
    }
}

fn checkpoint_matches_disk(checkpoint: &DownloadMetadata, write_path: &str) -> bool {
    match std::fs::metadata(write_path) {
        Ok(info) => info.is_file() && info.len() as i64 == checkpoint.total_bytes && DynamicSegmenter::is_consistent(&checkpoint.segments, checkpoint.total_bytes),
        Err(_) => false,
    }
}

fn is_media_platform_url(url: &str) -> bool {
    url::Url::parse(url.trim()).ok().and_then(|u| u.host_str().map(str::to_ascii_lowercase)).is_some_and(|h| {
        h == "youtu.be" || h == "youtube.com" || h.ends_with(".youtube.com") || h == "youtube-nocookie.com" || h.ends_with(".youtube-nocookie.com")
    })
}
